using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace AuditSmoke
{
    public partial class Commands
    {
        // For every model-space Solid3d: its history flags, then an intersect of
        // its clone with each exploded Plant pipe solid, tried several ways.
        [CommandMethod("AUDITBOOLTEST")]
        public void BooleanTest()
        {
            var rows = new List<string>();
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            var bodies = new List<(string Key, Solid3d Solid)>();
            var pipes = new List<(string Key, Solid3d Solid)>();
            foreach (ObjectId id in space)
            {
                var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (entity is Solid3d s)
                {
                    rows.Add($"{id.Handle} Solid3d RecordHistory={s.RecordHistory} ShowHistory={s.ShowHistory} IsNull={s.IsNull}");
                    bodies.Add((id.Handle.ToString(), s));
                }
                else if (entity is Autodesk.ProcessPower.PnP3dObjects.Part)
                {
                    using var pieces = new DBObjectCollection();
                    try { entity.Explode(pieces); } catch { continue; }
                    foreach (DBObject piece in pieces)
                        if (piece is Solid3d ps) pipes.Add((id.Handle.ToString(), ps)); else piece.Dispose();
                }
            }
            void Try(string label, Func<bool> action)
            {
                try { rows.Add($"   {label}: {(action() ? "overlap" : "no overlap")}"); }
                catch (System.Exception ex) { rows.Add($"   {label}: {ex.GetType().Name} {ex.Message}"); }
            }
            foreach (var (bodyKey, body) in bodies)
                foreach (var (pipeKey, pipe) in pipes)
                {
                    var b = body.GeometricExtents; var p = pipe.GeometricExtents;
                    if (b.MaxPoint.X < p.MinPoint.X || p.MaxPoint.X < b.MinPoint.X || b.MaxPoint.Y < p.MinPoint.Y || p.MaxPoint.Y < b.MinPoint.Y) continue;
                    rows.Add($"{bodyKey} x {pipeKey}");
                    Try("pipe clone ∩ body clone", () =>
                    {
                        using var first = (Solid3d)pipe.Clone(); using var second = (Solid3d)body.Clone();
                        if (!first.CheckInterference(second)) return false;
                        first.BooleanOperation(BooleanOperationType.BoolIntersect, second);
                        return first.MassProperties.Volume > 1e-9;
                    });
                    Try("body clone ∩ pipe clone", () =>
                    {
                        using var first = (Solid3d)body.Clone(); using var second = (Solid3d)pipe.Clone();
                        if (!first.CheckInterference(second)) return false;
                        first.BooleanOperation(BooleanOperationType.BoolIntersect, second);
                        return first.MassProperties.Volume > 1e-9;
                    });
                    Try("history off on both", () =>
                    {
                        using var first = (Solid3d)pipe.Clone(); using var second = (Solid3d)body.Clone();
                        first.RecordHistory = false; second.RecordHistory = false;
                        if (!first.CheckInterference(second)) return false;
                        first.BooleanOperation(BooleanOperationType.BoolIntersect, second);
                        return first.MassProperties.Volume > 1e-9;
                    });
                    Try("fresh copy via CopyFrom", () =>
                    {
                        using var first = (Solid3d)pipe.Clone();
                        using var second = new Solid3d(); second.CopyFrom(body);
                        if (!first.CheckInterference(second)) return false;
                        first.BooleanOperation(BooleanOperationType.BoolIntersect, second);
                        return first.MassProperties.Volume > 1e-9;
                    });
                    Try("CheckInterference only", () => { using var first = (Solid3d)pipe.Clone(); using var second = (Solid3d)body.Clone(); return first.CheckInterference(second); });
                }
            foreach (var (_, s) in pipes) s.Dispose();
            File.WriteAllLines(Environment.GetEnvironmentVariable("AUDIT_SMOKE_OUTPUT") + ".bool.txt", rows);
        }
    }
}
