using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PlantInstance;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// Iteration 3: enumerate Plant 3D parts (EngineeringItems) in the
    /// active drawing and tally counts by top-level class. This is the
    /// first real read of project data and the foundation for the spec
    /// generator — once we can group parts by class here, the same
    /// mechanism extends to statuses, lines, and category filters.
    ///
    /// Why active-drawing only (not project-wide) for now:
    ///   * project-wide enumeration would have to side-open every DWG
    ///     through the document manager, which is slow and error-prone;
    ///   * the Python edition gets the same data from a Piping.dcf
    ///     snapshot — we'll add side-databases later when needed.
    /// </summary>
    public class ListPartsCommand
    {
        [CommandMethod("LISTPARTS")]
        public void Run()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var editor = doc?.Editor;
            if (editor == null) return;

            if (PlantApplication.CurrentProject == null)
            {
                editor.WriteMessage("\n[LISTPARTS] No Plant 3D project is currently open.");
                return;
            }

            DataLinksManager dlm;
            try
            {
                dlm = DataLinksManager.GetManager(doc.Database);
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage("\n[LISTPARTS] DataLinksManager unavailable for this drawing: "
                    + ex.GetType().Name + ": " + ex.Message);
                return;
            }
            if (dlm == null)
            {
                editor.WriteMessage("\n[LISTPARTS] Drawing has no Plant 3D blob (not part of a project).");
                return;
            }

            var rowIds = dlm.SelectAcPpRowIds(doc.Database);
            if (rowIds == null)
            {
                editor.WriteMessage("\n[LISTPARTS] No Plant 3D rows in active drawing.");
                return;
            }

            // PnPRowIdArray exposes IEnumerable<int>; the classname returned by
            // GetObjectClassname is a backslash-delimited path
            // (e.g. "Piping\ButtWeldedFitting\Elbow90Long"). Group by the
            // first segment for a category-level tally; keep the full class
            // breakdown for diagnostics.
            var byCategory = new Dictionary<string, int>();
            var byClass = new Dictionary<string, int>();
            var total = 0;
            foreach (int rowId in rowIds)
            {
                string cls;
                try { cls = dlm.GetObjectClassname(rowId); }
                catch { continue; }
                if (string.IsNullOrEmpty(cls)) continue;
                total++;
                var category = cls.Split('\\')[0];
                byCategory.TryGetValue(category, out var c);
                byCategory[category] = c + 1;
                byClass.TryGetValue(cls, out var cc);
                byClass[cls] = cc + 1;
            }

            editor.WriteMessage($"\n[LISTPARTS] {total} Plant 3D row(s) in active drawing");
            editor.WriteMessage("\n  --- by category ---");
            foreach (var kv in byCategory.OrderByDescending(kv => kv.Value))
                editor.WriteMessage($"\n    {kv.Value,6}  {kv.Key}");

            editor.WriteMessage("\n  --- by class (top 15) ---");
            foreach (var kv in byClass.OrderByDescending(kv => kv.Value).Take(15))
                editor.WriteMessage($"\n    {kv.Value,6}  {kv.Key}");
        }
    }
}
