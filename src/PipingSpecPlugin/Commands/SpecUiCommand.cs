using System.Collections.Generic;
using System.Windows.Interop;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PlantInstance;
using PipingSpecPlugin.UI;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// Iteration 4 (skeleton): show a WPF window populated with the same
    /// counts <c>LISTPARTS</c> dumps to the editor. The window itself is
    /// throwaway; the real test is that WPF dispatch survives inside the
    /// AutoCAD process. Plant 3D's own ribbon uses WPF heavily, so this
    /// should "just work" — but it has historically broken on first try
    /// when our app domain is missing pack:// URI support or when the
    /// window is shown before AutoCAD's MainWindow is ready.
    /// </summary>
    public class SpecUiCommand
    {
        [CommandMethod("SPECUI")]
        public void Run()
        {
            var editor = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            var project = PlantApplication.CurrentProject;
            if (project == null)
            {
                editor?.WriteMessage("\n[SPECUI] No Plant 3D project is currently open.");
                return;
            }

            var (byCategory, byClass) = CollectCountsForActiveDrawing();

            var window = new SpecWindow();
            window.Populate(project.Name, byCategory, byClass);
            // AutoCAD's Application.ShowModalWindow(Uri,...) overloads only
            // accept XAML/XBAP URIs, not constructed Window objects. For an
            // in-process WPF Window we parent it to AutoCAD's main window
            // via WindowInteropHelper and call ShowDialog() directly —
            // that's the documented pattern for Plant 3D / AutoCAD 2025+.
            new WindowInteropHelper(window).Owner = AcadApp.MainWindow.Handle;
            window.ShowDialog();
        }

        static (IReadOnlyDictionary<string, int> byCategory,
                IReadOnlyDictionary<string, int> byClass) CollectCountsForActiveDrawing()
        {
            var byCategory = new Dictionary<string, int>();
            var byClass = new Dictionary<string, int>();

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return (byCategory, byClass);

            DataLinksManager dlm;
            try { dlm = DataLinksManager.GetManager(doc.Database); }
            catch { return (byCategory, byClass); }
            if (dlm == null) return (byCategory, byClass);

            var rowIds = dlm.SelectAcPpRowIds(doc.Database);
            if (rowIds == null) return (byCategory, byClass);

            foreach (int rowId in rowIds)
            {
                string cls;
                try { cls = dlm.GetObjectClassname(rowId); }
                catch { continue; }
                if (string.IsNullOrEmpty(cls)) continue;
                var category = cls.Split('\\')[0];
                byCategory.TryGetValue(category, out var c); byCategory[category] = c + 1;
                byClass.TryGetValue(cls, out var cc);        byClass[cls] = cc + 1;
            }
            return (byCategory, byClass);
        }
    }
}
