using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Interop;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.PlantInstance;
using Autodesk.ProcessPower.ProjectManager;
using PipingSpecPlugin.Settings;
using PipingSpecPlugin.Spec;
using PipingSpecPlugin.UI;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// Entry point users invoke from the ribbon. Reads all PartRecords
    /// from the current project once, opens the picker so the user can
    /// choose statuses / categories / lines / exclude flags, then runs
    /// SpecRowBuilder + SpecTemplateWriter with the picked filters.
    /// Saves user choices back to settings.json so the picker remembers
    /// the last selection.
    /// </summary>
    public class WriteSpecCommand
    {
        [CommandMethod("WRITESPEC")]
        public void Run()
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
            var activeDoc = AcadApp.DocumentManager.MdiActiveDocument;
            var dwgLeaf = activeDoc != null && !string.IsNullOrEmpty(activeDoc.Name)
                ? Path.GetFileName(activeDoc.Name) : "";

            // Read everything once: the picker needs project-level facts
            // (statuses / categories / lines with counts), and most users
            // will pick "whole project" anyway. If they pick "active dwg"
            // we filter the records in-memory rather than re-reading.
            editor.WriteMessage("\n[WRITESPEC] Scanning project...");
            var (projectRecords, ownedPnPIds, topology) = CollectFromProject(project, editor);
            editor.WriteMessage($"\n[WRITESPEC] Found {projectRecords.Count} part record(s) across the project.");

            // Discover valve companions up-front so we can show the user
            // the count before they generate. The actual exclusion happens
            // only when the checkbox is on, but we always run the search.
            var companionIds = ValveCompanionFinder.Discover(projectRecords, topology);
            if (companionIds.Count > 0)
                editor.WriteMessage($"\n[WRITESPEC] Valve-companion candidates found: {companionIds.Count} (flange/gasket/bolt-set tuples).");

            // ProjectFacts drives the picker's checkbox counts. Strip the
            // owned-by-equipment rows now so what the user sees in the UI
            // matches what actually ends up in the spec.
            var visibleRecords = ownedPnPIds.Count > 0
                ? projectRecords.Where(r => !ownedPnPIds.Contains(r.PnPID)).ToList()
                : projectRecords;
            var facts = ProjectFacts.From(visibleRecords);

            var picker = new SpecPickerWindow(facts, project.Name, dwgLeaf,
                                              settings,
                                              activeDrawingAvailable: !string.IsNullOrEmpty(dwgLeaf));
            new WindowInteropHelper(picker).Owner = AcadApp.MainWindow.Handle;
            var ok = picker.ShowDialog();
            if (ok != true)
            {
                editor.WriteMessage("\n[WRITESPEC] Cancelled.");
                return;
            }
            var result = picker.Result;

            // Narrow the input set if the user picked active-drawing scope.
            // We do this in-memory rather than re-running PartReader so the
            // counts the user saw in the picker line up with what we feed
            // to the builder.
            IEnumerable<PartRecord> input = projectRecords;
            if (result.Scope == "dwg" && activeDoc != null)
            {
                var dwgRecords = PartReader.ReadRows(activeDoc.Database).ToList();
                var activeIds = new HashSet<int>(dwgRecords.Select(r => r.PnPID));
                input = projectRecords.Where(r => activeIds.Contains(r.PnPID));
            }

            var builder = new SpecRowBuilder(new SpecBuilderOptions
            {
                Statuses = result.Statuses,
                Categories = result.Categories,
                Lines = result.Lines,
                GroupByLine = result.GroupByLine,
                ExcludeEquipment = result.ExcludeEquipment,
                ExcludeConnectors = result.ExcludeConnectors,
                ExcludeValveCompanions = result.ExcludeValveCompanions,
                EquipmentOwnedPnPIds = ownedPnPIds,
                ValveCompanionPnPIds = companionIds,
            });
            var specRows = builder.Build(input);
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
                Title = "Сохранить спецификацию",
                Filter = "Word документ (*.docx)|*.docx",
                FileName = $"{project.Name}.docx",
                InitialDirectory = initialDir,
            })
            {
                if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
                outputPath = dlg.FileName;
            }

            try
            {
                SpecTemplateWriter.Write(result.TemplatePath, outputPath, specRows,
                                         groupByLine: result.GroupByLine);
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage("\n[WRITESPEC] FAILED: "
                    + ex.GetType().Name + ": " + ex.Message);
                return;
            }

            // Persist user prefs after a successful run only — a failed
            // run should not pin broken selections (matches Python).
            settings.LastOutputDir = Path.GetDirectoryName(outputPath);
            settings.TemplatePath = result.TemplatePath;
            settings.Scope = result.Scope;
            settings.Statuses = result.Statuses?.ToArray();
            settings.Categories = result.Categories?.ToArray();
            settings.Lines = result.Lines?.ToArray();
            settings.GroupByLine = result.GroupByLine;
            settings.ExcludeValveCompanions = result.ExcludeValveCompanions;
            settings.ExcludeEquipment = result.ExcludeEquipment;
            settings.ExcludeConnectors = result.ExcludeConnectors;
            settings.Save();

            editor.WriteMessage($"\n[WRITESPEC] Wrote {specRows.Count} spec row(s) to {outputPath}");
            TryOpenInWord(outputPath, editor);
        }

        static void TryOpenInWord(string path, Autodesk.AutoCAD.EditorInput.Editor editor)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\n[WRITESPEC] Could not auto-open: {ex.Message}");
            }
        }

        // --- project-wide collection -------------------------------------

        static (List<PartRecord> records, HashSet<int> ownedPnPIds,
                Dictionary<int, List<int>> topology) CollectFromProject(
            PlantProject project, Autodesk.AutoCAD.EditorInput.Editor editor)
        {
            Project piping = null;
            foreach (var pp in project.ProjectParts)
                if (pp is Autodesk.ProcessPower.P3dProjectParts.PipingProject)
                { piping = pp; break; }
            if (piping == null)
            {
                editor.WriteMessage("\n[WRITESPEC] Project has no Piping discipline.");
                return (new List<PartRecord>(), new HashSet<int>(),
                        new Dictionary<int, List<int>>());
            }

            var dwgFiles = ((Autodesk.ProcessPower.P3dProjectParts.PipingProject)piping)
                .ProjectFiles
                .Where(f => !string.IsNullOrEmpty(f.Name)
                            && f.Name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var activeDocPath = AcadApp.DocumentManager.MdiActiveDocument?.Name ?? "";
            var all = new List<PartRecord>();
            var owned = new HashSet<int>();
            var topology = new Dictionary<int, List<int>>();
            foreach (var f in dwgFiles)
            {
                string path = null;
                try { path = !string.IsNullOrEmpty(f.ResolvedFilePath) ? f.ResolvedFilePath : f.AbsoluteFileName; }
                catch { }
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;

                try
                {
                    PartReadResult result;
                    if (string.Equals(path, activeDocPath, StringComparison.OrdinalIgnoreCase))
                    {
                        var doc = AcadApp.DocumentManager.MdiActiveDocument;
                        result = PartReader.Read(doc.Database);
                    }
                    else
                    {
                        using var db = new Database(false, true);
                        db.ReadDwgFile(path, FileShare.Read, true, "");
                        db.CloseInput(true);
                        result = PartReader.Read(db);
                    }
                    all.AddRange(result.Records);
                    foreach (var id in result.EquipmentOwnedPnPIds) owned.Add(id);
                    foreach (var kv in result.Topology)
                    {
                        // Different drawings can describe the same part
                        // with disjoint neighbours; union them.
                        if (!topology.TryGetValue(kv.Key, out var existing))
                            topology[kv.Key] = new List<int>(kv.Value);
                        else
                            foreach (var id in kv.Value)
                                if (!existing.Contains(id)) existing.Add(id);
                    }
                }
                catch (System.Exception ex)
                {
                    editor.WriteMessage($"\n  ! {Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
                    if (ex.InnerException != null)
                        editor.WriteMessage($"\n    inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
            }
            return (all, owned, topology);
        }
    }
}
