using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.PlantInstance;
using Autodesk.ProcessPower.ProjectManager;
using PipingSpecPlugin.Settings;
using PipingSpecPlugin.Spec;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// Generates a full piping spec .docx from the active Plant 3D
    /// project. By default the scope is the whole project (matches the
    /// Python edition's default); pass <c>WRITESPECDWG</c> instead to
    /// limit it to the active drawing. The template path comes from the
    /// user's settings if set, otherwise the user is prompted.
    /// </summary>
    public class WriteSpecCommand
    {
        [CommandMethod("WRITESPEC")]
        public void RunProject() => Run(projectScope: true);

        [CommandMethod("WRITESPECDWG")]
        public void RunActiveDwg() => Run(projectScope: false);

        static void Run(bool projectScope)
        {
            var editor = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            if (editor == null) return;

            var project = PlantApplication.CurrentProject;
            if (project == null)
            {
                editor.WriteMessage("\n[WRITESPEC] No Plant 3D project is currently open.");
                return;
            }

            var settings = PluginSettings.Load();
            var templatePath = ResolveTemplatePath(settings);
            if (string.IsNullOrEmpty(templatePath))
            {
                editor.WriteMessage("\n[WRITESPEC] Aborted — no template selected.");
                return;
            }

            editor.WriteMessage($"\n[WRITESPEC] Template: {templatePath}");
            editor.WriteMessage(projectScope
                ? "\n[WRITESPEC] Scope: whole project"
                : "\n[WRITESPEC] Scope: active drawing only");

            var parts = (projectScope
                ? CollectFromProject(project, editor)
                : CollectFromActiveDrawing(editor)).ToList();

            // Histogram of statuses — helps the user notice when a parts'
            // bulk sits under a status they didn't intend to filter out.
            var statusHist = parts.GroupBy(p => string.IsNullOrEmpty(p.Status) ? "<empty>" : p.Status)
                                   .OrderByDescending(g => g.Count())
                                   .ToList();
            editor.WriteMessage($"\n[WRITESPEC] {parts.Count} raw row(s); status histogram:");
            foreach (var g in statusHist)
                editor.WriteMessage($"\n    {g.Count(),6}  {g.Key}");

            var builder = new SpecRowBuilder(new SpecBuilderOptions
            {
                // settings.Statuses == null means "include all statuses",
                // which is the .NET edition's default until we ship the
                // GUI status picker that mirrors the Python edition's
                // checkboxes.
                Statuses = settings.Statuses,
                GroupByLine = settings.GroupByLine,
            });
            if (settings.Statuses == null)
                editor.WriteMessage("\n[WRITESPEC] Status filter: all statuses included");
            else
                editor.WriteMessage("\n[WRITESPEC] Status filter: " + string.Join(", ", settings.Statuses));
            var specRows = builder.Build(parts);
            foreach (var w in builder.Warnings)
                editor.WriteMessage("\n  [WARN] " + w);

            if (specRows.Count == 0)
            {
                editor.WriteMessage("\n[WRITESPEC] No spec rows produced — nothing to write.");
                return;
            }

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

            try
            {
                SpecTemplateWriter.Write(templatePath, outputPath, specRows,
                                         groupByLine: settings.GroupByLine);
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage("\n[WRITESPEC] FAILED: "
                    + ex.GetType().Name + ": " + ex.Message);
                return;
            }

            settings.LastOutputDir = Path.GetDirectoryName(outputPath);
            settings.TemplatePath = templatePath;
            settings.Save();

            editor.WriteMessage($"\n[WRITESPEC] Wrote {specRows.Count} spec row(s) to {outputPath}");
        }

        // --- template resolution ----------------------------------------

        static string ResolveTemplatePath(PluginSettings settings)
        {
            if (!string.IsNullOrEmpty(settings.TemplatePath)
                && File.Exists(settings.TemplatePath))
                return settings.TemplatePath;

            using var dlg = new WinForms.OpenFileDialog
            {
                Title = "Select spec template (.docx)",
                Filter = "Word template (*.docx)|*.docx",
                CheckFileExists = true,
            };
            return dlg.ShowDialog() == WinForms.DialogResult.OK ? dlg.FileName : null;
        }

        // --- data collection --------------------------------------------

        static IEnumerable<PartRecord> CollectFromActiveDrawing(
            Autodesk.AutoCAD.EditorInput.Editor editor)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return Array.Empty<PartRecord>();
            return PartReader.ReadRows(doc.Database).ToList();
        }

        static IEnumerable<PartRecord> CollectFromProject(
            PlantProject project, Autodesk.AutoCAD.EditorInput.Editor editor)
        {
            Project piping = null;
            foreach (var pp in project.ProjectParts)
                if (pp is Autodesk.ProcessPower.P3dProjectParts.PipingProject)
                { piping = pp; break; }
            if (piping == null)
            {
                editor.WriteMessage("\n[WRITESPEC] Project has no Piping discipline.");
                return Array.Empty<PartRecord>();
            }

            var dwgFiles = ((Autodesk.ProcessPower.P3dProjectParts.PipingProject)piping)
                .ProjectFiles
                .Where(f => !string.IsNullOrEmpty(f.Name)
                            && f.Name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                .ToList();

            editor.WriteMessage($"\n[WRITESPEC] Scanning {dwgFiles.Count} drawing(s)...");

            var activeDocPath = AcadApp.DocumentManager.MdiActiveDocument?.Name ?? "";
            var all = new List<PartRecord>();
            foreach (var f in dwgFiles)
            {
                string path = null;
                try { path = !string.IsNullOrEmpty(f.ResolvedFilePath) ? f.ResolvedFilePath : f.AbsoluteFileName; }
                catch { }
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;

                try
                {
                    if (string.Equals(path, activeDocPath, StringComparison.OrdinalIgnoreCase))
                    {
                        var doc = AcadApp.DocumentManager.MdiActiveDocument;
                        all.AddRange(PartReader.ReadRows(doc.Database));
                    }
                    else
                    {
                        using var db = new Database(false, true);
                        db.ReadDwgFile(path, FileShare.Read, true, "");
                        db.CloseInput(true);
                        all.AddRange(PartReader.ReadRows(db));
                    }
                }
                catch (System.Exception ex)
                {
                    editor.WriteMessage($"\n  ! {Path.GetFileName(path)}: {ex.GetType().Name}");
                }
            }
            return all;
        }
    }
}
