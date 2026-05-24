using System.Linq;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.DataLinks;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// Diagnostic: dump every property on the first Plant 3D row in the
    /// active drawing. We need the exact property names (Tag, Size,
    /// LongDescription, DryWeight, ...) before we can build a template
    /// mapping — without this we'd be guessing. Throwaway command;
    /// will be removed once the property names are baked into the
    /// template engine.
    /// </summary>
    public class DumpRowCommand
    {
        [CommandMethod("DUMPROW")]
        public void Run()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var editor = doc?.Editor;
            if (editor == null) return;

            DataLinksManager dlm;
            try { dlm = DataLinksManager.GetManager(doc.Database); }
            catch (System.Exception ex)
            {
                editor.WriteMessage("\n[DUMPROW] DataLinksManager unavailable: " + ex.Message);
                return;
            }
            if (dlm == null)
            {
                editor.WriteMessage("\n[DUMPROW] No DataLinksManager for this drawing.");
                return;
            }

            var rowIds = dlm.SelectAcPpRowIds(doc.Database);
            if (rowIds == null)
            {
                editor.WriteMessage("\n[DUMPROW] No Plant 3D rows.");
                return;
            }

            var first = rowIds.Cast<int>().FirstOrDefault();
            if (first == 0)
            {
                editor.WriteMessage("\n[DUMPROW] Row collection empty.");
                return;
            }

            var cls = dlm.GetObjectClassname(first);
            editor.WriteMessage($"\n[DUMPROW] rowId={first}  class={cls}");
            // GetAllProperties(rowId, includeInherited) — second arg true
            // pulls inherited properties from base classes too, which is
            // what we want for the spec mapping.
            var props = dlm.GetAllProperties(first, true);
            editor.WriteMessage($"\n  {props.Count} propert{(props.Count == 1 ? "y" : "ies")}:");
            foreach (var kv in props.OrderBy(kv => kv.Key))
                editor.WriteMessage($"\n    {kv.Key,-32}  {kv.Value}");
        }
    }
}
