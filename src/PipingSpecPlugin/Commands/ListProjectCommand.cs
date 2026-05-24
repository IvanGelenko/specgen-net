using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PlantInstance;
using Autodesk.ProcessPower.ProjectManager;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// Iteration 9: project-wide row enumeration. Walks every drawing
    /// registered with the current Plant 3D project, opens each as a
    /// side-database (no UI, no document-manager lock), and tallies
    /// rows by category — same shape as LISTPARTS but for the whole
    /// project instead of the active drawing.
    ///
    /// The active drawing is read directly from the document manager
    /// rather than re-opened, both because it would be locked and
    /// because the in-memory copy includes unsaved edits.
    /// </summary>
    public class ListProjectCommand
    {
        [CommandMethod("LISTPROJECT")]
        public void Run()
        {
            var editor = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            if (editor == null) return;

            var project = PlantApplication.CurrentProject;
            if (project == null)
            {
                editor.WriteMessage("\n[LISTPROJECT] No Plant 3D project is currently open.");
                return;
            }

            // PlantProject is the host; the per-discipline Project objects
            // (Piping, P&ID, Iso) live underneath. We only need the Piping
            // one — that's where 3D parts live.
            Project pipingProject = null;
            foreach (var pp in project.ProjectParts)
            {
                if (pp is Autodesk.ProcessPower.P3dProjectParts.PipingProject)
                {
                    pipingProject = pp;
                    break;
                }
            }
            if (pipingProject == null)
            {
                editor.WriteMessage("\n[LISTPROJECT] Project has no Piping discipline.");
                return;
            }

            var drawings = pipingProject.GetPnPDrawingFiles();
            editor.WriteMessage($"\n[LISTPROJECT] Scanning {drawings.Count} drawing(s)...");

            var byCategory = new Dictionary<string, int>();
            var activeDocPath = AcadApp.DocumentManager.MdiActiveDocument?.Name ?? "";
            var scanned = 0;
            var failed = 0;
            foreach (var pdrw in drawings)
            {
                string path;
                try { path = pipingProject.ComposeDwgName(pdrw); }
                catch { failed++; continue; }
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    failed++;
                    continue;
                }

                try
                {
                    if (string.Equals(path, activeDocPath, StringComparison.OrdinalIgnoreCase))
                        CountFromActiveDoc(byCategory);
                    else
                        CountFromSideDatabase(path, byCategory);
                    scanned++;
                }
                catch (System.Exception ex)
                {
                    editor.WriteMessage($"\n  ! {Path.GetFileName(path)}: {ex.GetType().Name}");
                    failed++;
                }
            }

            var total = byCategory.Values.Sum();
            editor.WriteMessage($"\n[LISTPROJECT] {total} row(s) across {scanned} drawing(s)"
                + (failed > 0 ? $" ({failed} skipped)" : ""));
            foreach (var kv in byCategory.OrderByDescending(kv => kv.Value))
                editor.WriteMessage($"\n    {kv.Value,6}  {kv.Key}");
        }

        static void CountFromActiveDoc(Dictionary<string, int> byCategory)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var dlm = DataLinksManager.GetManager(doc.Database);
            if (dlm == null) return;
            TallyRows(dlm, doc.Database, byCategory);
        }

        static void CountFromSideDatabase(string path, Dictionary<string, int> byCategory)
        {
            // (buildDefaultDrawing: false, noDocument: true) opens the file
            // without touching the editor or document manager. Always
            // dispose so the underlying file handle is released — Plant 3D
            // checkpoints can hold the .dcf next to it.
            using var db = new Database(false, true);
            db.ReadDwgFile(path, FileShare.Read, true, "");
            db.CloseInput(true);

            var dlm = DataLinksManager.GetManager(db);
            if (dlm == null) return;
            TallyRows(dlm, db, byCategory);
        }

        static void TallyRows(DataLinksManager dlm, Database db,
                              Dictionary<string, int> byCategory)
        {
            var rowIds = dlm.SelectAcPpRowIds(db);
            if (rowIds == null) return;
            foreach (int rowId in rowIds)
            {
                string cls;
                try { cls = dlm.GetObjectClassname(rowId); }
                catch { continue; }
                if (string.IsNullOrEmpty(cls)) continue;
                var category = cls.Split('\\')[0];
                byCategory.TryGetValue(category, out var c);
                byCategory[category] = c + 1;
            }
        }
    }
}
