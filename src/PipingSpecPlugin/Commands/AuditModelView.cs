using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using PipingSpecPlugin.Audit;
using PipingSpecPlugin.Settings;

namespace PipingSpecPlugin.Commands
{
    // «Показать в модели» from the audit results. The modal window steps aside
    // (EditorUserInteraction, the AutoCAD way for modal dialogs), the view is
    // zoomed to the issue and its elements are highlighted — a nested xref /
    // block element itself, not its whole insertion — until Enter; then the
    // same results come back. «Выделить и закрыть» used to be the only way to
    // see an element, and every further issue needed a new audit run.
    static class AuditModelView
    {
        public static void Show(Document doc, System.Windows.Window owner, IReadOnlyList<AuditIssue> issues, RunLogger logger)
        {
            var ed = doc.Editor;
            var db = doc.Database;
            // A collision is zoomed to where the two parts overlap; other issues
            // to their elements.
            Extents3d? box = null;
            foreach (var issue in issues)
                if (issue.Bounds is { Length: 6 } b)
                    box = Union(box, new Extents3d(new Point3d(b[0], b[1], b[2]), new Point3d(b[3], b[4], b[5])));
            var atIssue = box.HasValue;
            var paths = new List<ObjectId[]>();
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var key in issues.SelectMany(i => i.Keys).Distinct(StringComparer.Ordinal))
                {
                    ObjectId[] path;
                    try { path = Resolve(db, key); }
                    catch (System.Exception ex) { logger.Log($"Аудит: элемент {key} не найден — {ex.Message}"); continue; }
                    if (path == null) { logger.Log($"Аудит: элемент {key} не найден в чертеже."); continue; }
                    paths.Add(path);
                    if (atIssue) continue;
                    try { box = Union(box, ((Entity)tr.GetObject(path[0], OpenMode.ForRead)).GeometricExtents); }
                    catch (System.Exception ex) { logger.Log($"Аудит: границы {key} не получены — {ex.Message}"); }
                }
                tr.Commit();
            }
            if (paths.Count == 0)
            {
                System.Windows.MessageBox.Show(owner, "Элементы этого замечания в чертеже не найдены.", "Аудит модели",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            using var interaction = ed.StartUserInteraction(owner);
            try
            {
                if (box.HasValue) Zoom(ed, box.Value, atIssue ? 3.0 : 1.3);
                Highlight(db, paths, true, logger);
                ed.UpdateScreen();
                // Transparent zoom / pan (wheel) works while the prompt waits.
                ed.GetString(new PromptStringOptions(
                    $"\nАудит: подсвечено элементов — {paths.Count}. Enter — вернуться к результатам аудита: ") { AllowSpaces = false });
            }
            finally
            {
                Highlight(db, paths, false, logger);
                interaction.End();
            }
        }

        // A key is a model-space handle or a path "insert/child/…": each child
        // lives in its parent's block definition — the xref's own database for
        // an external reference, the same database for an ordinary block.
        internal static ObjectId[] Resolve(Database host, string key)
        {
            var ids = new List<ObjectId>();
            var database = host;
            foreach (var part in key.Split('/'))
            {
                if (!long.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return null;
                if (ids.Count > 0)
                {
                    using var tr = database.TransactionManager.StartOpenCloseTransaction();
                    if (tr.GetObject(ids[^1], OpenMode.ForRead) is not BlockReference parent) return null;
                    var definition = (BlockTableRecord)tr.GetObject(parent.BlockTableRecord, OpenMode.ForRead);
                    if (definition.IsFromExternalReference)
                        database = definition.GetXrefDatabase(false);
                    if (database == null) return null;
                }
                if (!database.TryGetObjectId(new Handle(value), out var id) || id.IsNull || id.IsErased) return null;
                ids.Add(id);
            }
            return ids.Count == 0 ? null : ids.ToArray();
        }

        internal static void Highlight(Database db, List<ObjectId[]> paths, bool on, RunLogger logger)
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var path in paths)
            {
                Entity top;
                try { top = (Entity)tr.GetObject(path[0], OpenMode.ForRead); }
                catch (System.Exception ex) { logger.Log($"Аудит: подсветка — {ex.Message}"); continue; }
                try
                {
                    if (path.Length == 1) { if (on) top.Highlight(); else top.Unhighlight(); continue; }
                    // The nested element itself: insertion(s) outermost first, then the element.
                    var nested = new FullSubentityPath(path, new SubentityId(SubentityType.Null, IntPtr.Zero));
                    if (on) top.Highlight(nested, false); else top.Unhighlight(nested, false);
                }
                catch (System.Exception ex)
                {
                    // Fall back to the whole insertion rather than show nothing.
                    logger.Log($"Аудит: вложенный элемент не подсвечен — {ex.Message}");
                    try { if (on) top.Highlight(); else top.Unhighlight(); } catch { }
                }
            }
            tr.Commit();
        }

        // Fit the box (WCS) into the current view, whatever its direction.
        static void Zoom(Editor ed, Extents3d box, double margin)
        {
            using var view = ed.GetCurrentView();
            var toView = (Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target)
                * Matrix3d.Displacement(view.Target - Point3d.Origin)
                * Matrix3d.PlaneToWorld(view.ViewDirection)).Inverse();
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var x in new[] { box.MinPoint.X, box.MaxPoint.X })
                foreach (var y in new[] { box.MinPoint.Y, box.MaxPoint.Y })
                    foreach (var z in new[] { box.MinPoint.Z, box.MaxPoint.Z })
                    {
                        var p = new Point3d(x, y, z).TransformBy(toView);
                        minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                        minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                    }
            double width = Math.Max(maxX - minX, 1e-6) * margin, height = Math.Max(maxY - minY, 1e-6) * margin;
            var aspect = view.Height > 0 ? view.Width / view.Height : 1;
            if (width / height < aspect) width = height * aspect; else height = width / aspect;
            view.CenterPoint = new Point2d((minX + maxX) / 2, (minY + maxY) / 2);
            view.Width = width;
            view.Height = height;
            ed.SetCurrentView(view);
        }

        static Extents3d Union(Extents3d? total, Extents3d next)
        {
            if (!total.HasValue) return next;
            var result = total.Value;
            result.AddExtents(next);
            return result;
        }
    }
}
