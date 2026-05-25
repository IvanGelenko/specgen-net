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
    /// Entry point invoked from the ribbon button. One scan per click:
    ///   1. read every "Plant 3D Models" DWG (active doc in-memory,
    ///      everything else as a side database)
    ///   2. open the picker so the user can choose statuses / categories
    ///      / lines and tweak the optional rules
    ///   3. aggregate into spec rows, write the .docx, open it in Word.
    /// All editor output is short; a full trace lives in
    /// %APPDATA%\PipingSpecPlugin\last-run.log for support tickets.
    /// </summary>
    public class WriteSpecCommand
    {
        [CommandMethod("WRITESPEC")]
        public void Run()
        {
            var editor = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            if (editor == null) return;
            using var logger = new RunLogger();

            void Say(string line)
            {
                editor.WriteMessage("\n" + line);
                logger.Log(line);
            }
            void Trace(string line) => logger.Log(line);

            Say($"[Спецификация] Запуск, лог: {logger.Path}");

            var project = PlantApplication.CurrentProject;
            if (project == null)
            {
                const string msg = "Не найден открытый проект AutoCAD Plant 3D. "
                                 + "Откройте проект перед генерацией.";
                Say("[Спецификация] " + msg);
                WinForms.MessageBox.Show(msg, "Спецификация Plant 3D",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            var settings = PluginSettings.Load();
            var activeDoc = AcadApp.DocumentManager.MdiActiveDocument;
            var dwgLeaf = activeDoc != null && !string.IsNullOrEmpty(activeDoc.Name)
                ? Path.GetFileName(activeDoc.Name) : "";

            // Modeless progress popup: parented to AutoCAD's main window
            // so it inherits z-order, shown as Dialog so we can pump events
            // on the same thread. CollectFromProject reports against it
            // via the Action<int,int,string> callback below.
            var progress = new UI.ProgressWindow();
            new WindowInteropHelper(progress).Owner = AcadApp.MainWindow.Handle;
            progress.Indeterminate("Поиск чертежей проекта…");
            progress.Show();

            Say("[Спецификация] Сканирование проекта…");
            ScanResult scan;
            try
            {
                scan = ScanWithCache(project, Say, Trace, progress);
            }
            finally
            {
                progress.Complete();
            }
            if (progress.IsCancelled)
            {
                Say("[Спецификация] Сканирование отменено пользователем.");
                return;
            }

            Say($"[Спецификация] Найдено компонентов: {scan.records.Count}"
              + (scan.ownedPnPIds.Count > 0
                    ? $"; исключено патрубков оборудования: {scan.ownedPnPIds.Count}"
                    : ""));

            if (scan.skippedFiles > 0)
            {
                // Только реальные сбои (не "файл не Plant 3D"). При нуле
                // не дергаем пользователя — это нормальный путь.
                WinForms.MessageBox.Show(
                    $"При сканировании пропущено чертежей: {scan.skippedFiles}.\n" +
                    $"Подробности см. в логе:\n{logger.Path}\n\n" +
                    "Спецификация может быть неполной.",
                    "Предупреждение",
                    WinForms.MessageBoxButtons.OK,
                    WinForms.MessageBoxIcon.Warning);
            }

            // Топология и valve-companion: считаем один раз, передаем
            // и в picker (через facts), и в builder. Подробности — в лог.
            var companionIds = ValveCompanionFinder.Discover(scan.records, scan.topology);
            Trace($"Топология: {scan.topology.Count} узлов; "
                + $"найдено ответных фланцев+прокладок+болтов: {companionIds.Count}");

            // Diagnostic: log every PipeBend we found, its line, length,
            // and whether the topology offers a Pipe neighbour. When the
            // user reports "bends not counted", this is the first thing
            // to check.
            var bends = scan.records.Where(r => r.IsPipeBend).ToList();
            if (bends.Count > 0)
            {
                Trace($"PipeBend partов: {bends.Count}");
                var byIdAll = new Dictionary<int, PartRecord>();
                foreach (var rr in scan.records)
                    if (!byIdAll.ContainsKey(rr.PnPID)) byIdAll[rr.PnPID] = rr;
                foreach (var b in bends.Take(20))
                {
                    scan.topology.TryGetValue(b.PnPID, out var nbrs);
                    var nbrStr = nbrs == null ? "(no nbrs)"
                        : string.Join(",", nbrs.Select(id =>
                            byIdAll.TryGetValue(id, out var rr)
                                ? $"{id}[{rr.PartCategory}/{rr.PnPClassName}]"
                                : $"{id}[?]"));
                    Trace($"  bend#{b.PnPID} class='{b.PnPClassName}' "
                        + $"line='{b.LineNumberTag ?? ""}' "
                        + $"len={b.Length}{b.LengthUnit} "
                        + $"-> {nbrStr}");
                }
            }
            else
            {
                Trace("PipeBend partов не найдено (или класс распознан иначе).");
            }

            // ProjectFacts питает чекбоксы picker'а. Парты, всегда
            // исключаемые (равно nozzle/owned), убираем чтобы счетчики
            // совпадали с тем, что попадет в .docx.
            var visibleProj = scan.ownedPnPIds.Count > 0
                ? scan.records.Where(r => !scan.ownedPnPIds.Contains(r.PnPID)).ToList()
                : scan.records;
            var facts = ProjectFacts.From(visibleProj);

            var visibleActive = scan.ownedPnPIds.Count > 0
                ? scan.activeDwgRecords.Where(r => !scan.ownedPnPIds.Contains(r.PnPID)).ToList()
                : scan.activeDwgRecords;
            var activeFacts = ProjectFacts.From(visibleActive);

            var picker = new SpecPickerWindow(facts, activeFacts, project.Name, dwgLeaf,
                                              settings,
                                              activeDrawingAvailable: !string.IsNullOrEmpty(dwgLeaf));
            new WindowInteropHelper(picker).Owner = AcadApp.MainWindow.Handle;
            if (picker.ShowDialog() != true)
            {
                Say("[Спецификация] Отменено пользователем.");
                return;
            }
            var result = picker.Result;
            Trace($"Picker: scope={result.Scope}; "
                + $"statuses={result.Statuses?.Count ?? 0}; "
                + $"categories={result.Categories?.Count ?? 0}; "
                + $"lines={result.Lines?.Count ?? 0}; "
                + $"groupByLine={result.GroupByLine}; "
                + $"excludeValveCompanions={result.ExcludeValveCompanions}; "
                + $"includePipeBends={result.IncludePipeBendsAsPipe}");

            // Активный чертеж — фильтруем по PnPID-ам.
            IEnumerable<PartRecord> input = scan.records;
            // Fold PipeBend into adjacent Pipe across the *whole* project
            // record set, before any scope filter narrows things down.
            // Otherwise a bend on the active drawing whose pipe lives in
            // a different DWG never folds when scope=dwg — the pipe isn't
            // in input and PipeBendFolder couldn't find it.
            PipeBendFoldResult bendFold = null;
            if (result.IncludePipeBendsAsPipe)
            {
                bendFold = PipeBendFolder.Fold(scan.records, scan.topology);
                Trace($"PipeBendFold: свернуто гибов: {bendFold.BendsConsumed.Count}; "
                    + $"труб с добавленной длиной: {bendFold.ExtraPipeMetres.Count}");
            }

            if (result.Scope == "dwg" && activeDoc != null)
            {
                var activeIds = new HashSet<int>(scan.activeDwgRecords.Select(r => r.PnPID));
                input = scan.records.Where(r => activeIds.Contains(r.PnPID));
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
                EquipmentOwnedPnPIds = scan.ownedPnPIds,
                ValveCompanionPnPIds = companionIds,
                BendsConsumed = bendFold?.BendsConsumed,
                ExtraPipeMetres = bendFold?.ExtraPipeMetres,
            });
            var specRows = builder.Build(input);
            foreach (var w in builder.Warnings) Trace("WARN: " + w);
            if (builder.DropReasons.Count > 0)
            {
                Trace("Фильтры (отброшено по причинам):");
                foreach (var kv in builder.DropReasons.OrderByDescending(kv => kv.Value))
                    Trace($"  {kv.Value,6}  {kv.Key}");
            }

            if (specRows.Count == 0)
            {
                const string msg = "Спецификация пуста. Выбранные фильтры исключили все компоненты.";
                Say("[Спецификация] " + msg);
                WinForms.MessageBox.Show(msg, "Спецификация Plant 3D",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }

            var outputPath = AskOutputPath(settings, project.Name);
            if (string.IsNullOrEmpty(outputPath))
            {
                Say("[Спецификация] Сохранение отменено.");
                return;
            }

            try
            {
                SpecTemplateWriter.Write(result.TemplatePath, outputPath, specRows,
                                         groupByLine: result.GroupByLine);
            }
            catch (System.Exception ex)
            {
                Trace($"FAILED: {ex.GetType().Name}: {ex.Message}");
                Trace(ex.StackTrace ?? "");
                Say($"[Спецификация] Ошибка сохранения: {ex.Message}");
                WinForms.MessageBox.Show(
                    $"Ошибка сохранения спецификации:\n\n{ex.Message}\n\n" +
                    $"Подробности в логе:\n{logger.Path}",
                    "Спецификация Plant 3D",
                    WinForms.MessageBoxButtons.OK,
                    WinForms.MessageBoxIcon.Error);
                return;
            }

            // Сохраняем prefs только после успешной записи — провальный
            // прогон не должен закрепить сломанный выбор.
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
            settings.OpenAfterGenerate = result.OpenAfterGenerate;
            settings.Save();

            Say($"[Спецификация] Готово: {specRows.Count} строк → {outputPath}");
            if (result.OpenAfterGenerate)
                TryOpenInWord(outputPath, Say);
        }

        // --- helpers -----------------------------------------------------

        static string AskOutputPath(PluginSettings settings, string projectName)
        {
            var initialDir = string.IsNullOrEmpty(settings.LastOutputDir)
                ? null
                : settings.LastOutputDir;
            using var dlg = new WinForms.SaveFileDialog
            {
                Title = "Сохранить спецификацию",
                Filter = "Word документ (*.docx)|*.docx",
                FileName = SanitizeFileName(projectName ?? "Спецификация") + ".docx",
                InitialDirectory = initialDir,
            };
            return dlg.ShowDialog() == WinForms.DialogResult.OK ? dlg.FileName : null;
        }

        /// <summary>
        /// Strip filename-illegal characters so the SaveFileDialog never
        /// receives "Foo/Bar.docx" or similar. Project names occasionally
        /// have colons or slashes; leaving them in tripped GUI dialogs on
        /// Russian Windows.
        /// </summary>
        static string SanitizeFileName(string name)
        {
            var bad = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var ch in name)
                sb.Append(Array.IndexOf(bad, ch) >= 0 ? '_' : ch);
            var cleaned = sb.ToString().Trim();
            return string.IsNullOrEmpty(cleaned) ? "Спецификация" : cleaned;
        }

        static void TryOpenInWord(string path, Action<string> say)
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
                say($"[Спецификация] Файл сохранен, открыть в Word не удалось: {ex.Message}");
            }
        }

        // --- scan --------------------------------------------------------

        sealed class ScanResult
        {
            public List<PartRecord> records = new();
            public List<PartRecord> activeDwgRecords = new();
            public HashSet<int> ownedPnPIds = new();
            public Dictionary<int, List<int>> topology = new();
            public int skippedFiles;
        }

        /// <summary>
        /// Wrap CollectFromProject with a per-session cache. Two
        /// consecutive WRITESPEC clicks against an unchanged project
        /// skip the long scan entirely.
        /// </summary>
        static ScanResult ScanWithCache(PlantProject project,
            Action<string> say, Action<string> trace, UI.ProgressWindow progress)
        {
            // Build the file list once so cache fingerprinting and the
            // real scan see the same set.
            Project piping = null;
            foreach (var pp in project.ProjectParts)
                if (pp is Autodesk.ProcessPower.P3dProjectParts.PipingProject)
                { piping = pp; break; }
            if (piping == null)
            {
                say("[Спецификация] В проекте нет дисциплины Piping.");
                return new ScanResult();
            }
            var dwgFiles = ((Autodesk.ProcessPower.P3dProjectParts.PipingProject)piping)
                .ProjectFiles
                .Where(f => !string.IsNullOrEmpty(f.Name)
                            && f.Name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)
                            && IsUnderPlant3dModels(f))
                .ToList();

            var dwgPaths = new List<string>(dwgFiles.Count);
            foreach (var f in dwgFiles)
            {
                try { dwgPaths.Add(f.ResolvedFilePath ?? f.AbsoluteFileName); }
                catch { }
            }
            var activeDocPath = AcadApp.DocumentManager.MdiActiveDocument?.Name ?? "";
            var fingerprint = ScanCache.Fingerprint(dwgPaths, activeDocPath);
            var key = project.ProjectFolderPath ?? project.Name ?? "";

            // File mtime can't tell us about *unsaved* edits in the
            // active document — Plant 3D keeps new parts and renamed
            // lines in-memory until the user saves. If a drawing is
            // open in this session, always do a fresh read so the spec
            // reflects what the user is looking at on screen.
            var hasOpenDocument = !string.IsNullOrEmpty(activeDocPath);
            var cached = hasOpenDocument ? null : ScanCache.TryGet(key, fingerprint);
            if (cached != null)
            {
                trace("ScanCache HIT — пропускаем сканирование.");
                progress.Report(dwgFiles.Count, dwgFiles.Count, "Загрузка из кэша…");
                return new ScanResult
                {
                    records = cached.Records,
                    activeDwgRecords = cached.ActiveDwgRecords,
                    ownedPnPIds = cached.EquipmentOwnedPnPIds,
                    topology = cached.Topology,
                    skippedFiles = cached.SkippedFiles,
                };
            }
            trace("ScanCache MISS — выполняем полное сканирование.");

            var result = CollectFromProject(project, dwgFiles, piping, activeDocPath,
                                            say, trace, progress);
            // Only cache when no document is open — see hasOpenDocument
            // comment above. A scan that included an in-memory document
            // would risk returning stale data to the next run.
            if (!progress.IsCancelled && !hasOpenDocument)
            {
                ScanCache.Put(key, new ScanCache.Entry
                {
                    Records = result.records,
                    ActiveDwgRecords = result.activeDwgRecords,
                    EquipmentOwnedPnPIds = result.ownedPnPIds,
                    Topology = result.topology,
                    SkippedFiles = result.skippedFiles,
                    Fingerprint = fingerprint,
                });
            }
            return result;
        }

        static ScanResult CollectFromProject(PlantProject project,
            List<PnPProjectFile> dwgFiles, Project piping, string activeDocPath,
            Action<string> say, Action<string> trace, UI.ProgressWindow progress)
        {
            var scan = new ScanResult();

            trace($"Чертежей в Plant 3D Models: {dwgFiles.Count}");
            var projectDlm = piping.DataLinksManager as DataLinksManager;

            for (var i = 0; i < dwgFiles.Count; i++)
            {
                if (progress.IsCancelled) break;
                var f = dwgFiles[i];

                string path = null;
                try { path = !string.IsNullOrEmpty(f.ResolvedFilePath) ? f.ResolvedFilePath : f.AbsoluteFileName; }
                catch { }
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    trace($"Пропущен (не найден на диске): {f.Name}");
                    progress.Report(i + 1, dwgFiles.Count, $"Пропущен: {f.Name}");
                    continue;
                }

                progress.Report(i, dwgFiles.Count, $"Чертёж: {Path.GetFileName(path)}");

                try
                {
                    PartReadResult result = ReadOneDrawing(path, activeDocPath, projectDlm, trace);
                    if (result == null) continue;

                    scan.records.AddRange(result.Records);
                    if (PathEquals(path, activeDocPath))
                        scan.activeDwgRecords.AddRange(result.Records);
                    foreach (var id in result.EquipmentOwnedPnPIds) scan.ownedPnPIds.Add(id);
                    foreach (var kv in result.Topology)
                    {
                        if (!scan.topology.TryGetValue(kv.Key, out var existing))
                            scan.topology[kv.Key] = new List<int>(kv.Value);
                        else
                            foreach (var id in kv.Value)
                                if (!existing.Contains(id)) existing.Add(id);
                    }
                }
                catch (System.Exception ex)
                {
                    trace($"FAILED: {Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
                    if (ex.InnerException != null)
                        trace($"  inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    scan.skippedFiles++;
                }
                progress.Report(i + 1, dwgFiles.Count, $"Готово: {Path.GetFileName(path)}");
            }
            return scan;
        }

        static PartReadResult ReadOneDrawing(string path, string activeDocPath,
            DataLinksManager projectDlm, Action<string> trace)
        {
            // Если чертеж открыт пользователем — читаем из памяти, иначе
            // открываем side-database. In-memory путь ловит несохраненные
            // изменения, sideDB — все остальное.
            foreach (Autodesk.AutoCAD.ApplicationServices.Document doc
                     in AcadApp.DocumentManager)
            {
                if (doc != null && PathEquals(doc.Name, path))
                {
                    try { return PartReader.Read(doc.Database, projectDlm); }
                    catch (System.Exception ex)
                    {
                        trace($"In-memory read failed for {Path.GetFileName(path)}: {ex.Message}; falling back to side-DB");
                        break;
                    }
                }
            }
            using var db = new Database(false, true);
            db.ReadDwgFile(path, FileShare.ReadWrite, true, "");
            db.CloseInput(true);
            return PartReader.Read(db, projectDlm);
        }

        /// <summary>
        /// Только DWG из "Plant 3D Models" поддерева. Ortho / Iso /
        /// symbol-style DWG в спецификацию не идут и только тормозят
        /// сканирование. Совпадение по фрагменту пути, не по абсолютному
        /// родителю — на случай локализованных установок.
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

        static bool PathEquals(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                var p1 = Path.GetFullPath(a).Replace('/', '\\').TrimEnd('\\');
                var p2 = Path.GetFullPath(b).Replace('/', '\\').TrimEnd('\\');
                return string.Equals(p1, p2, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(
                    a.Replace('/', '\\').TrimEnd('\\'),
                    b.Replace('/', '\\').TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
