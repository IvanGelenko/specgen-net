using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using PipingSpecPlugin.Audit;
using PipingSpecPlugin.Layers;

namespace AuditSmoke
{
    public partial class Commands
    {
        [Autodesk.AutoCAD.Runtime.CommandMethod("COLORSMOKE")]
        public void Colors()
        {
            var path = Environment.GetEnvironmentVariable("AUDIT_SMOKE_OUTPUT");
            try
            {
                var document = Application.DocumentManager.MdiActiveDocument;
                var db = document.Database;
                var metadata = new Dictionary<string, AuditPart>();
                var handles = new Dictionary<string, ObjectId>();
                ObjectId lockedLayer;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                    ObjectId Layer(string name, Color color)
                    {
                        var layer = new LayerTableRecord { Name = name, Color = color };
                        var id = table.Add(layer); tr.AddNewlyCreatedDBObject(layer, true); return id;
                    }
                    var pipeLayer = Layer("test_pipe", Color.FromColorIndex(ColorMethod.ByAci, 3));
                    var valveLayer = Layer("test_valve", Color.FromColorIndex(ColorMethod.ByAci, 1));
                    var rgbLayer = Layer("test_rgb", Color.FromRgb(20, 70, 130));
                    lockedLayer = valveLayer;
                    var space = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    void Add(string key, ObjectId layer, string kind, string line)
                    {
                        var solid = new Solid3d(); solid.CreateBox(1, 1, 1); solid.LayerId = layer; solid.ColorIndex = 2;
                        space.AppendEntity(solid); tr.AddNewlyCreatedDBObject(solid, true);
                        handles[key] = solid.ObjectId;
                        metadata[solid.Handle.ToString()] = new AuditPart { Key = solid.Handle.ToString(), ClassName = kind, LineKey = line };
                    }
                    Add("pipe", pipeLayer, "Pipe", "L1"); Add("valve", valveLayer, "Valve", "L1");
                    Add("pipeRgb", rgbLayer, "Pipe", "L2"); Add("valveRgb", valveLayer, "Valve", "L2");
                    Add("unknown", valveLayer, "Valve", null);
                    ((LayerTableRecord)tr.GetObject(valveLayer, OpenMode.ForWrite)).IsLocked = true;
                    tr.Commit();
                }
                var first = LayerColorService.Apply(document, metadata);
                var second = LayerColorService.Apply(document, metadata);
                using var read = db.TransactionManager.StartTransaction();
                Entity Entity(string key) => (Entity)read.GetObject(handles[key], OpenMode.ForRead);
                bool passed = Entity("pipe").ColorIndex == 256 && Entity("valve").ColorIndex == 3 &&
                    Entity("valve").Layer == "test_valve" && Entity("valveRgb").Layer == "test_valve" &&
                    Entity("valveRgb").Color.ColorMethod == ColorMethod.ByColor && Entity("valveRgb").Color.Red == 20 &&
                    Entity("valveRgb").Color.Green == 70 && Entity("valveRgb").Color.Blue == 130 && Entity("unknown").ColorIndex == 256 &&
                    ((LayerTableRecord)read.GetObject(lockedLayer, OpenMode.ForRead)).IsLocked && second.Changed == 0;
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { Passed = passed, First = first, Second = second }));
            }
            catch (Exception ex) { File.WriteAllText(path + ".error.txt", ex.ToString()); }
        }
    }
}
