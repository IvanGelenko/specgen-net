using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using PipingSpecPlugin.Audit;
using PipingSpecPlugin.Commands;
using PipingSpecPlugin.Settings;

namespace AuditSmoke
{
    public partial class Commands
    {
        // What the user sees for each collision (line, text, source, elements,
        // selection) and what «Показать в модели» does with it: each key is
        // resolved like AuditModelView, the leaf's box is brought into host
        // coordinates and compared with the issue's Bounds; highlight is tried.
        [CommandMethod("AUDITXREFCHECK")]
        public void XrefCheck()
        {
            var rows = new List<string>();
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var logger = new RunLogger("AUDITXREFCHECK");
            try
            {
                var options = new AuditOptions();
                foreach (var rule in AuditRules.All) options.Enabled.Add(rule.Id);
                var line = Environment.GetEnvironmentVariable("AUDIT_SMOKE_LINE");
                if (!string.IsNullOrEmpty(line)) options.SelectedLines = new HashSet<string> { line };
                var snapshot = DrawingAuditReader.Read(doc, options);
                rows.Add($"pairs={snapshot.CollisionPairsChecked} xrefs={snapshot.XrefInstancesChecked} unavailable={snapshot.XrefInstancesUnavailable} joints={snapshot.ConnectedJointsIgnored}");
                foreach (var issue in AuditRules.Run(snapshot, options).Where(i => i.RuleId == "collisions"))
                {
                    rows.Add($"[{issue.Severity}] line=«{issue.Line}» sources=«{issue.Sources}»");
                    rows.Add($"   message: {issue.Message}");
                    rows.Add($"   elements: {issue.Elements} | select: {string.Join(", ", issue.SelectableKeys)}");
                    if (issue.Bounds != null) rows.Add($"   bounds: ({string.Join(", ", issue.Bounds.Select(v => v.ToString("0.#")))})");
                    var paths = new List<ObjectId[]>();
                    foreach (var key in issue.Keys)
                    {
                        ObjectId[] path;
                        try { path = AuditModelView.Resolve(db, key); }
                        catch (System.Exception ex) { rows.Add($"   resolve {key}: {ex.GetType().Name} {ex.Message}"); continue; }
                        if (path == null) { rows.Add($"   resolve {key}: NOT FOUND"); continue; }
                        paths.Add(path);
                        // Leaf box in host coordinates through the insert chain.
                        var transform = Matrix3d.Identity;
                        string Describe(ObjectId id, out Entity entity)
                        {
                            using var t = id.Database.TransactionManager.StartOpenCloseTransaction();
                            entity = t.GetObject(id, OpenMode.ForRead) as Entity;
                            return $"{Path.GetFileName(id.Database.Filename)}:{id.Handle}:{entity?.GetType().Name}";
                        }
                        var parts = new List<string>();
                        for (int i = 0; i < path.Length; i++)
                        {
                            parts.Add(Describe(path[i], out var e));
                            if (i < path.Length - 1 && e is BlockReference br) transform = transform * br.BlockTransform;
                            if (i == path.Length - 1 && e != null)
                            {
                                try
                                {
                                    var box = e.GeometricExtents; box.TransformBy(transform);
                                    parts.Add($"host box ({box.MinPoint.X:0.#},{box.MinPoint.Y:0.#},{box.MinPoint.Z:0.#})-({box.MaxPoint.X:0.#},{box.MaxPoint.Y:0.#},{box.MaxPoint.Z:0.#})");
                                }
                                catch (System.Exception ex) { parts.Add("box error " + ex.Message); }
                            }
                        }
                        rows.Add($"   resolve {key}: {string.Join(" → ", parts)}");
                    }
                    try { AuditModelView.Highlight(db, paths, true, logger); AuditModelView.Highlight(db, paths, false, logger); rows.Add("   highlight on/off: ok"); }
                    catch (System.Exception ex) { rows.Add($"   highlight: {ex.GetType().Name} {ex.Message}"); }
                }
            }
            catch (System.Exception ex) { rows.Add("ERROR " + ex); }
            finally
            {
                // The probe's own run log goes into the report, not %APPDATA%.
                logger.Dispose();
                try { rows.AddRange(File.ReadAllLines(logger.Path).Where(l => l.Contains("Аудит:")).Select(l => "   log: " + l)); File.Delete(logger.Path); } catch { }
                try { File.Delete(Path.Combine(Path.GetDirectoryName(logger.Path), "prev-run-auditxrefcheck.log")); } catch { }
            }
            File.WriteAllLines(Environment.GetEnvironmentVariable("AUDIT_SMOKE_OUTPUT") + ".xref.txt", rows);
        }
    }
}
