using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.PnP3dObjects;
using PipingSpecPlugin.Audit;

namespace AuditSmoke
{
    public partial class Commands
    {
        [CommandMethod("AUDITSUPPORTFIXTURE")]
        public void SupportFixture()
        {
            string output = Environment.GetEnvironmentVariable("AUDIT_SUPPORT_OUTPUT");
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            int before = Convert.ToInt32(Application.GetSystemVariable("DBMOD"));
            try
            {
                var results = new List<object>();
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var space = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    var block = new BlockTableRecord { Name = "SupportAuditFixture_" + Guid.NewGuid().ToString("N") };
                    table.Add(block); tr.AddNewlyCreatedDBObject(block, true);
                    var box = new Solid3d(); box.CreateBox(60, 120, 80);
                    var ext = box.GeometricExtents;
                    var center = ext.MinPoint + (ext.MaxPoint - ext.MinPoint) * 0.5;
                    box.TransformBy(Matrix3d.Displacement(new Point3d(250, 25, 0) - center));
                    block.AppendEntity(box); tr.AddNewlyCreatedDBObject(box, true);
                    var pipe = new Pipe { StartPoint = Point3d.Origin, EndPoint = new Point3d(500, 0, 0), OuterDiameter = 40 };
                    var other = new Pipe { StartPoint = new Point3d(0, 50, 0), EndPoint = new Point3d(500, 50, 0), OuterDiameter = 40 };
                    var support = new Support { SymbolId = block.ObjectId };
                    foreach (var entity in new Entity[] { pipe, other, support }) { space.AppendEntity(entity); tr.AddNewlyCreatedDBObject(entity, true); }
                    using var pipePort = new Port { Name = "SupportTap", Position = new Point3d(250, 0, 0), IsDynamic = true, IsSymbolic = true };
                    using var supportPort = new Port { Name = "S1", Position = new Point3d(250, 0, 0), IsDynamic = true, IsSymbolic = true };
                    if (!pipe.AddPort(pipePort) || !support.AddPort(supportPort)) throw new InvalidOperationException("Cannot add fixture ports");
                    using var manager = new ConnectionManager();
                    using var a = new Pair { ObjectId = pipe.ObjectId, Port = pipePort };
                    using var b = new Pair { ObjectId = support.ObjectId, Port = supportPort };
                    manager.Connect(a, b);
                    var reader = typeof(DrawingAuditReader);
                    var type = reader.GetNestedType("Shape", BindingFlags.NonPublic);
                    var collect = reader.GetMethod("CollectCollisionEntity", BindingFlags.NonPublic | BindingFlags.Static);
                    var check = reader.GetMethod("CheckCollisions", BindingFlags.NonPublic | BindingFlags.Static);
                    foreach (bool connected in new[] { true, false })
                    {
                        if (!connected) manager.Disconnect(pipe.ObjectId, support.ObjectId);
                        var snapshot = new AuditSnapshot();
                        var shapes = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(type));
                        try
                        {
                            foreach (var entity in new Entity[] { pipe, other, support })
                            {
                                string key = entity.Handle.ToString();
                                var part = new AuditPart { Key = key, Description = entity.GetType().Name, LineKey = "L" };
                                snapshot.Parts.Add(part);
                                collect.Invoke(null, new object[] { entity, part, tr, Matrix3d.Identity, key, key, "", false, 0, new HashSet<ObjectId>(), shapes, snapshot, true });
                            }
                            check.Invoke(null, new object[] { shapes, snapshot, null });
                            bool passed = snapshot.GeometryIssues.All(i => i.Severity != "Не проверено")
                                && snapshot.GeometryIssues.Count(i => i.Severity == "Пересечение") == (connected ? 1 : 2)
                                && snapshot.SupportContactsIgnored == (connected ? 1 : 0)
                                && (!connected || snapshot.GeometryIssues.All(i => !i.Keys.Contains(pipe.Handle.ToString())));
                            results.Add(new { Connected = connected, Passed = passed, snapshot.SupportContactsIgnored, Issues = snapshot.GeometryIssues });
                        }
                        finally { foreach (IDisposable shape in shapes) shape.Dispose(); }
                    }
                    // All fixture entities and connections are rolled back.
                }
                File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(new { Before = before,
                    After = Convert.ToInt32(Application.GetSystemVariable("DBMOD")), Results = results }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch (System.Exception ex) { File.WriteAllText(output + ".error.txt", ex.ToString()); }
        }
    }
}
