using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using PipingSpecPlugin.Layers;

namespace AuditSmoke
{
    public partial class Commands
    {
        [CommandMethod("AUDITLAYERSELECTION")]
        public void LayerSelectionFixture()
        {
            var output = Environment.GetEnvironmentVariable("AUDIT_LAYER_SELECTION_OUTPUT");
            try
            {
                using var db = new Database(true, true);
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                    foreach (var name in new[] { "Трубы", "Арматура", "Внутри блока", "Пустой" })
                    {
                        var layer = new LayerTableRecord { Name = name }; layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true);
                    }
                    var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    var model = (BlockTableRecord)tr.GetObject(blocks[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    var paper = (BlockTableRecord)tr.GetObject(blocks[BlockTableRecord.PaperSpace], OpenMode.ForWrite);
                    void AddLine(BlockTableRecord space, string layer)
                    {
                        var line = new Line(Point3d.Origin, new Point3d(10, 0, 0)) { LayerId = layers[layer] };
                        space.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                    }
                    AddLine(model, "Трубы"); AddLine(model, "Трубы"); AddLine(model, "Арматура");
                    AddLine(paper, "Трубы");
                    var definition = new BlockTableRecord { Name = "FixtureBlock" };
                    blocks.Add(definition); tr.AddNewlyCreatedDBObject(definition, true);
                    AddLine(definition, "Внутри блока");
                    var insert = new BlockReference(Point3d.Origin, definition.ObjectId) { LayerId = layers["Арматура"] };
                    model.AppendEntity(insert); tr.AddNewlyCreatedDBObject(insert, true);
                    tr.Commit();
                }
                var result = LayerSelectionService.Read(db);
                bool passed = result.Count == 2 && result.Single(c => c.Name == "Трубы").Ids.Length == 2
                    && result.Single(c => c.Name == "Арматура").Ids.Length == 2;
                File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(new { Passed = passed,
                    Layers = result.Select(c => new { c.Name, Count = c.Ids.Length }) }));
            }
            catch (System.Exception ex) { File.WriteAllText(output + ".error.txt", ex.ToString()); }
        }
    }
}
