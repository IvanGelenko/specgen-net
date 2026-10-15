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
        [CommandMethod("AUDITINSULATION")]
        public void InsulationFixture()
        {
            var result = new List<object>();
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            int before = Convert.ToInt32(Application.GetSystemVariable("DBMOD"));
            var insulationDisplay = Application.GetSystemVariable("PLANTINSULATIONDISPLAY");
            try
            {
                Application.SetSystemVariable("PLANTINSULATIONDISPLAY", 1);
                using var transaction = db.TransactionManager.StartTransaction();
                var reader = typeof(DrawingAuditReader);
                var shapeType = reader.GetNestedType("Shape", BindingFlags.NonPublic);
                var collect = reader.GetMethod("CollectShape", BindingFlags.NonPublic | BindingFlags.Static);
                var check = reader.GetMethod("CheckCollisions", BindingFlags.NonPublic | BindingFlags.Static);
                foreach (var scenario in new[] { "joint", "unconnected", "metal" })
                {
                    using var a = new Pipe { StartPoint = Point3d.Origin, EndPoint = new Point3d(500, 0, 0), OuterDiameter = 89, InsulationDiameter = 249 };
                    using var b = new Pipe { StartPoint = scenario == "metal" ? new Point3d(300, -300, 0) : new Point3d(600, 100, 0),
                        EndPoint = scenario == "metal" ? new Point3d(300, 300, 0) : new Point3d(600, 600, 0), OuterDiameter = 89, InsulationDiameter = 249 };
                    var snapshot = new AuditSnapshot();
                    var left = new AuditPart { Key = "A", LineKey = "L1", ClassName = "Pipe", Description = "Труба A", ConnectionsRead = true };
                    var right = new AuditPart { Key = "B", LineKey = scenario == "unconnected" ? "L2" : "L1", ClassName = "Pipe", Description = "Труба B", ConnectionsRead = true };
                    var elbow = new AuditPart { Key = "E", ClassName = "Elbow", ConnectionsRead = true };
                    if (scenario != "unconnected") { left.Neighbours.Add("E"); elbow.Neighbours.Add("B"); }
                    snapshot.Parts.AddRange(new[] { left, right, elbow });
                    var shapes = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(shapeType));
                    try
                    {
                        collect.Invoke(null, new object[] { a, left, transaction, shapes, snapshot, true, Matrix3d.Identity });
                        collect.Invoke(null, new object[] { b, right, transaction, shapes, snapshot, true, Matrix3d.Identity });
                        check.Invoke(null, new object[] { shapes, snapshot, null });
                        var issues = snapshot.GeometryIssues;
                        bool passed = issues.Count(i => i.Severity == "Не проверено") == 0
                            && issues.Count(i => i.Severity == "Пересечение") == (scenario == "joint" ? 0 : 1)
                            && (scenario != "unconnected" || issues.Any(i => i.Message.Contains("изоляции")))
                            && a.InsulationDiameter == 249 && b.InsulationDiameter == 249;
                        result.Add(new { Scenario = scenario, Passed = passed, Shapes = shapes.Count, Issues = issues });
                    }
                    finally { foreach (IDisposable shape in shapes) shape.Dispose(); }
                }
                File.WriteAllText(Environment.GetEnvironmentVariable("AUDIT_INSULATION_OUTPUT"), System.Text.Json.JsonSerializer.Serialize(new
                    { Before = before, After = Convert.ToInt32(Application.GetSystemVariable("DBMOD")), Results = result }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch (System.Exception ex) { File.WriteAllText(Environment.GetEnvironmentVariable("AUDIT_INSULATION_OUTPUT") + ".error.txt", ex.ToString()); }
            finally { Application.SetSystemVariable("PLANTINSULATIONDISPLAY", insulationDisplay); }
        }
    }
}
