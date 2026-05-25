using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Interop;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.DataLinks;
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
                var msg = "Не найден открытый проект AutoCAD Plant 3D. Пожалуйста, откройте проект перед генерацией спецификации.";
                editor.WriteMessage("\n[WRITESPEC] " + msg);
                WinForms.MessageBox.Show(msg, "Спецификация Plant 3D",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
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
            var (projectRecords, activeDwgRecords, ownedPnPIds, topology, eqCount, eqNbrCount, failedCount)
                = CollectFromProject(project, editor);
            editor.WriteMessage($"\n[WRITESPEC] Found {projectRecords.Count} part record(s) across the project.");
            editor.WriteMessage(
                $"\n[WRITESPEC] Equipment entities seen: {eqCount}; "
              + $"connection-neighbours: {eqNbrCount}; "
              + $"owned PnPIDs (incl. connector hops): {ownedPnPIds.Count}.");

            if (failedCount > 0)
            {
                WinForms.MessageBox.Show(
                    $"При сканировании проекта были пропущены некоторые чертежи ({failedCount} шт.).\n" +
                    "Детали ошибок выведены в командную строку AutoCAD.\n\n" +
                    "Сгенерированная спецификация может быть неполной.",
                    "Предупреждение",
                    WinForms.MessageBoxButtons.OK,
                    WinForms.MessageBoxIcon.Warning);
            }

            // Discover valve companions up-front so we can show the user
            // the count before they generate. The actual exclusion happens
            // only when the checkbox is on, but we always run the search.
            // First, a topology diagnostic — print every Valve's neighbour
            // list and the class of each neighbour. If this comes back
            // empty or wrong, the Discovery is going to fail too and we
            // need a different relationship API.
            var byId = new Dictionary<int, PartRecord>();
            foreach (var r in projectRecords)
                if (!byId.ContainsKey(r.PnPID)) byId.Add(r.PnPID, r);
            var valves = byId.Values
                .Where(r => string.Equals(r.PartCategory, "Valves",
                                           StringComparison.OrdinalIgnoreCase))
                .ToList();
            editor.WriteMessage($"\n[WRITESPEC] Topology graph: {topology.Count} nodes; {valves.Count} valve(s) seen.");
            string Tag(int id) => byId.TryGetValue(id, out var rr)
                ? $"{id}[{rr.PartCategory ?? "?"}/{rr.PnPClassName ?? "?"}]"
                : $"{id}[?]";
            foreach (var v in valves.Take(3))
            {
                topology.TryGetValue(v.PnPID, out var nbrs);
                editor.WriteMessage($"\n    valve#{v.PnPID} immediate -> "
                    + (nbrs == null ? "(no neighbours)"
                                    : string.Join(", ", nbrs.Select(Tag))));
                // Hop +1: for each immediate neighbour print what it sees.
                if (nbrs != null)
                {
                    foreach (var n1 in nbrs)
                    {
                        topology.TryGetValue(n1, out var n2s);
                        editor.WriteMessage($"\n      {Tag(n1)} -> "
                            + (n2s == null ? "(leaf)"
                                           : string.Join(", ", n2s.Where(x => x != v.PnPID).Select(Tag))));
                    }
                }
            }

            var companionIds = ValveCompanionFinder.Discover(projectRecords, topology);
            editor.WriteMessage($"\n[WRITESPEC] Valve-companion candidates found: {companionIds.Count}.");
            if (companionIds.Count > 0)
            {
                editor.WriteMessage("\n  companion PnPIDs: "
                    + string.Join(", ", companionIds.Take(20).Select(Tag))
                    + (companionIds.Count > 20 ? $" (+{companionIds.Count - 20} more)" : ""));
            }

            // ProjectFacts drives the picker's checkbox counts. Strip the
            // owned-by-equipment rows now so what the user sees in the UI
            // matches what actually ends up in the spec.
            var visibleRecords = ownedPnPIds.Count > 0
                ? projectRecords.Where(r => !ownedPnPIds.Contains(r.PnPID)).ToList()
                : projectRecords;
            var facts = ProjectFacts.From(visibleRecords);

            var visibleActiveRecords = ownedPnPIds.Count > 0
                ? activeDwgRecords.Where(r => !ownedPnPIds.Contains(r.PnPID)).ToList()
                : activeDwgRecords;
            var activeFacts = ProjectFacts.From(visibleActiveRecords);

            var picker = new SpecPickerWindow(facts, activeFacts, project.Name, dwgLeaf,
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

            // Reuse the pre-scan snapshot for the actual build. The
            // earlier "Recalculating..." pass that scanned all DWGs a
            // second time was added defensively for "data freshness"
            // but it doubled the latency on big projects and offered
            // no real benefit — the user's changes between opening the
            // picker and clicking 'Сгенерировать' don't reach a
            // background DataLinks store anyway. One scan per ribbon
            // click is the contract.
            IEnumerable<PartRecord> input = projectRecords;
            if (result.Scope == "dwg" && activeDoc != null)
            {
                var activeIds = new HashSet<int>(activeDwgRecords.Select(r => r.PnPID));
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
                IncludePipeBendsAsPipe = result.IncludePipeBendsAsPipe,
                Topology = topology,
            });
            var specRows = builder.Build(input);
            foreach (var w in builder.Warnings)
                editor.WriteMessage("\n  [WARN] " + w);
            if (builder.DropReasons.Count > 0)
            {
                editor.WriteMessage("\n[WRITESPEC] Filter audit (drops by reason):");
                foreach (var kv in builder.DropReasons.OrderByDescending(kv => kv.Value))
                    editor.WriteMessage($"\n    {kv.Value,6}  {kv.Key}");
            }

            if (specRows.Count == 0)
            {
                var msg = "Спецификация пуста. Выбранные фильтры исключили все компоненты.";
                editor.WriteMessage("\n[WRITESPEC] " + msg);
                WinForms.MessageBox.Show(msg, "Спецификация Plant 3D",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
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
                WinForms.MessageBox.Show(
                    $"Ошибка сохранения спецификации:\n\n{ex.Message}",
                    "Спецификация Plant 3D",
                    WinForms.MessageBoxButtons.OK,
                    WinForms.MessageBoxIcon.Error);
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
            settings.IncludePipeBendsAsPipe = result.IncludePipeBendsAsPipe;
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

        static (List<PartRecord> records, List<PartRecord> activeDwgRecords, HashSet<int> ownedPnPIds,
                Dictionary<int, List<int>> topology,
                int equipmentCount, int equipmentNeighbourCount, int failedCount) CollectFromProject(
            PlantProject project, Autodesk.AutoCAD.EditorInput.Editor editor)
        {
            Project piping = null;
            foreach (var pp in project.ProjectParts)
                if (pp is Autodesk.ProcessPower.P3dProjectParts.PipingProject)
                { piping = pp; break; }
            if (piping == null)
            {
                editor.WriteMessage("\n[WRITESPEC] Project has no Piping discipline.");
                return (new List<PartRecord>(), new List<PartRecord>(), new HashSet<int>(),
                        new Dictionary<int, List<int>>(), 0, 0, 0);
            }

            var dwgFiles = ((Autodesk.ProcessPower.P3dProjectParts.PipingProject)piping)
                .ProjectFiles
                .Where(f => !string.IsNullOrEmpty(f.Name)
                            && f.Name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)
                            && IsUnderPlant3dModels(f))
                .ToList();

            var activeDocPath = AcadApp.DocumentManager.MdiActiveDocument?.Name ?? "";
            var projectDlm = piping.DataLinksManager;
            var all = new List<PartRecord>();
            var activeDwgRecs = new List<PartRecord>();
            var owned = new HashSet<int>();
            var topology = new Dictionary<int, List<int>>();
            var eqCount = 0;
            var eqNbrCount = 0;
            var count = 0;
            var failedCount = 0;
            foreach (var f in dwgFiles)
            {
                count++;
                if (count % 5 == 0 || count == dwgFiles.Count)
                {
                    editor.WriteMessage($"\n[WRITESPEC] Сканирование: {count} из {dwgFiles.Count} чертежей...");
                    System.Windows.Forms.Application.DoEvents();
                }

                string path = null;
                try { path = !string.IsNullOrEmpty(f.ResolvedFilePath) ? f.ResolvedFilePath : f.AbsoluteFileName; }
                catch { }
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    failedCount++;
                    continue;
                }

                try
                {
                    PartReadResult result = null;

                    // Try to locate open document to get unsaved in-memory changes
                    Autodesk.AutoCAD.ApplicationServices.Document openDoc = null;
                    try
                    {
                        foreach (Autodesk.AutoCAD.ApplicationServices.Document doc in AcadApp.DocumentManager)
                        {
                            if (doc != null && PathEquals(doc.Name, path))
                            {
                                openDoc = doc;
                                break;
                            }
                        }
                    }
                    catch { }

                    if (openDoc != null)
                    {
                        try
                        {
                            result = PartReader.Read(openDoc.Database, projectDlm);
                            if (PathEquals(path, activeDocPath))
                            {
                                activeDwgRecs.AddRange(result.Records);
                            }
                        }
                        catch (System.Exception ex)
                        {
                            editor.WriteMessage($"\n  [WARN] Не удалось прочитать открытый чертеж {Path.GetFileName(path)} из памяти, пробуем с диска: {ex.Message}");
                        }
                    }

                    if (result == null)
                    {
                        using var db = new Database(false, true);
                        // FileShare.ReadWrite (not just Read) is required
                        // because Plant 3D leaves a .dwl write-lock next
                        // to drawings the user has touched in the current
                        // session — read-only sharing throws
                        // eFileSharingViolation and we lose half the
                        // project's parts.
                        db.ReadDwgFile(path, FileShare.ReadWrite, true, "");
                        db.CloseInput(true);
                        result = PartReader.Read(db, projectDlm);
                        if (PathEquals(path, activeDocPath))
                        {
                            activeDwgRecs.AddRange(result.Records);
                        }
                    }
                    all.AddRange(result.Records);
                    foreach (var id in result.EquipmentOwnedPnPIds) owned.Add(id);
                    eqCount += result.EquipmentCount;
                    eqNbrCount += result.EquipmentNeighbourCount;
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
                    failedCount++;
                }
            }
            return (all, activeDwgRecs, owned, topology, eqCount, eqNbrCount, failedCount);
        }

        [CommandMethod("TESTREL")]
        public void TestRel()
        {
            var editor = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            if (editor == null) return;

            try
            {
                var db = AcadApp.DocumentManager.MdiActiveDocument.Database;
                var dlm = DataLinksManager.GetManager(db);
                if (dlm == null)
                {
                    editor.WriteMessage("\nDataLinksManager is null.");
                    return;
                }

                var rowIds = dlm.SelectAcPpRowIds(db);
                if (rowIds == null || rowIds.Count == 0)
                {
                    editor.WriteMessage("\nNo parts found in active database.");
                    return;
                }

                editor.WriteMessage($"\nTotal parts: {rowIds.Count}");

                int targetRowId = 0;
                string cachedTag = null;
                foreach (int rowId in rowIds)
                {
                    var props = dlm.GetAllProperties(rowId, true);
                    if (props == null) continue;
                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in props)
                        if (kv.Key != null && !dict.ContainsKey(kv.Key)) dict.Add(kv.Key, kv.Value);

                    if (dict.TryGetValue("LineNumberTag", out var val) && !string.IsNullOrEmpty(val))
                    {
                        targetRowId = rowId;
                        cachedTag = val;
                        break;
                    }
                }

                if (targetRowId == 0)
                {
                    foreach (int id in rowIds)
                    {
                        targetRowId = id;
                        break;
                    }
                }

                editor.WriteMessage($"\nTarget RowId: {targetRowId}, cached LineNumberTag: '{cachedTag ?? "null"}'");

                try
                {
                    var related = dlm.GetRelatedRowIds(targetRowId);
                    editor.WriteMessage($"\nRelatedRowIds(rowId) count: {related?.Count ?? 0}");
                    if (related != null)
                    {
                        foreach (int rId in related)
                        {
                            var rProps = dlm.GetAllProperties(rId, true);
                            var rClass = rProps?.FirstOrDefault(kv => kv.Key.Equals("PnPClassName", StringComparison.OrdinalIgnoreCase)).Value;
                            var rTag = rProps?.FirstOrDefault(kv => kv.Key.Equals("Tag", StringComparison.OrdinalIgnoreCase)).Value;
                            editor.WriteMessage($"\n  -> Related RowId: {rId}, Class: '{rClass}', Tag: '{rTag}'");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    editor.WriteMessage($"\nError querying bare GetRelatedRowIds: {ex.Message}");
                }

                string[] relationshipNames = { "P3dLineGroupPartRelationship", "LineGroupPartRelationship", "P3dLineGroupPart" };
                foreach (var relName in relationshipNames)
                {
                    try
                    {
                        var relatedIds = dlm.GetRelatedRowIds(relName, "Part", targetRowId, "LineGroup");
                        editor.WriteMessage($"\nQuery: {relName} (Part -> LineGroup): count={relatedIds?.Count ?? 0}");
                        if (relatedIds != null)
                        {
                            foreach (int lgId in relatedIds)
                            {
                                var lgProps = dlm.GetAllProperties(lgId, true);
                                var lgTag = lgProps?.FirstOrDefault(kv => kv.Key.Equals("Tag", StringComparison.OrdinalIgnoreCase)).Value;
                                editor.WriteMessage($"\n    -> RowId {lgId}, Tag: '{lgTag}'");
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        editor.WriteMessage($"\nError querying {relName}: {ex.Message}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nFatal error in TESTREL: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Only DWGs under the project's "Plant 3D Models" sub-tree
        /// hold the 3D piping parts the spec cares about. The Iso / Ortho /
        /// symbol-style drawings also live in ProjectFiles but they don't
        /// carry the Engineering Items we aggregate — including them
        /// just slows the scan and adds noise. Match on a path fragment
        /// rather than a fixed parent so user-renamed sub-folders still
        /// pass (e.g. localised 'Plant 3D Models' under Plant3D
        /// installations).
        /// </summary>
        static bool IsUnderPlant3dModels(PnPProjectFile f)
        {
            string path = null;
            try { path = f.RelativeFileName; } catch { }
            if (string.IsNullOrEmpty(path))
            {
                try { path = f.ResolvedFilePath ?? f.AbsoluteFileName; } catch { }
            }
            if (string.IsNullOrEmpty(path)) return false;
            var normalized = path.Replace('/', '\\');
            return normalized.IndexOf("\\Plant 3D Models\\",
                       StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.StartsWith("Plant 3D Models\\",
                       StringComparison.OrdinalIgnoreCase);
        }

        static bool PathEquals(string path1, string path2)
        {
            if (string.IsNullOrEmpty(path1) || string.IsNullOrEmpty(path2)) return false;
            try
            {
                var p1 = Path.GetFullPath(path1).Replace('/', '\\').TrimEnd('\\');
                var p2 = Path.GetFullPath(path2).Replace('/', '\\').TrimEnd('\\');
                return string.Equals(p1, p2, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(path1.Replace('/', '\\').TrimEnd('\\'), 
                                     path2.Replace('/', '\\').TrimEnd('\\'), 
                                     StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
