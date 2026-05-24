using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PlantInstance;
using PipingSpecPlugin.Output;
using PipingSpecPlugin.Settings;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// Iteration 5: write a .docx of the active-drawing category counts.
    /// This is a deliberately thin slice — same input as LISTPARTS, same
    /// output structure (category, count) — its only job is to prove
    /// DocumentFormat.OpenXml resolves correctly inside AutoCAD and to
    /// let the user verify nothing in the build/load pipeline blocks
    /// NuGet dependencies. Template-based spec generation comes next.
    /// </summary>
    public class WriteSpecCommand
    {
        [CommandMethod("WRITESPEC")]
        public void Run()
        {
            var editor = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            var project = PlantApplication.CurrentProject;
            if (project == null)
            {
                editor?.WriteMessage("\n[WRITESPEC] No Plant 3D project is currently open.");
                return;
            }

            var rows = CollectCounts();
            if (rows.Count == 0)
            {
                editor?.WriteMessage("\n[WRITESPEC] No Plant 3D parts in active drawing — nothing to write.");
                return;
            }

            var settings = PluginSettings.Load();
            var initialDir = string.IsNullOrEmpty(settings.LastOutputDir)
                ? project.ProjectFolderPath
                : settings.LastOutputDir;
            string outputPath;
            using (var dlg = new WinForms.SaveFileDialog
            {
                Title = "Save spec output",
                Filter = "Word document (*.docx)|*.docx",
                FileName = $"{project.Name}.docx",
                InitialDirectory = initialDir,
            })
            {
                if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
                outputPath = dlg.FileName;
            }

            SpecWordWriter.Write(outputPath, project.Name, rows.OrderByDescending(r => r.Value));

            settings.LastOutputDir = Path.GetDirectoryName(outputPath);
            settings.Save();

            editor?.WriteMessage($"\n[WRITESPEC] Wrote {rows.Count} category rows to {outputPath}");
        }

        static List<KeyValuePair<string, int>> CollectCounts()
        {
            var byCategory = new Dictionary<string, int>();
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return new List<KeyValuePair<string, int>>();

            DataLinksManager dlm;
            try { dlm = DataLinksManager.GetManager(doc.Database); }
            catch { return new List<KeyValuePair<string, int>>(); }
            if (dlm == null) return new List<KeyValuePair<string, int>>();

            var rowIds = dlm.SelectAcPpRowIds(doc.Database);
            if (rowIds == null) return new List<KeyValuePair<string, int>>();

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
            return byCategory.ToList();
        }
    }
}
