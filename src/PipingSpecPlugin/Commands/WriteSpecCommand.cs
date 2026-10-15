using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Interop;
using Autodesk.AutoCAD.ApplicationServices;
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
    ///   1. read saved "Plant 3D Models" drawings as side databases
    ///   2. open the picker so the user can choose statuses / categories
    ///      / lines and tweak the optional rules
    ///   3. aggregate into spec rows, write the .docx, open it in Word.
    /// All editor output is short; a full trace lives in
    /// %APPDATA%\PipingSpecPlugin\last-run.log for support tickets.
    /// </summary>
    public class WriteSpecCommand
    {
        [CommandMethod("WRITESPEC", CommandFlags.Session)]
        public void Run() => RunCore(forcedScope: null);

        /// <summary>
        /// Same flow as WRITESPEC but forces scope=proj — the ribbon's
        /// "Весь проект" button. Bypasses the saved scope preference so
        /// the user gets the project-wide spec they asked for regardless
        /// of what was selected last time.
        /// </summary>
        [CommandMethod("WRITESPECPROJ", CommandFlags.Session)]
        public void RunProject() => RunCore(forcedScope: "proj");

        /// <summary>
        /// Same flow as WRITESPEC but forces scope=dwg — the ribbon's
        /// "Текущий чертёж" button. Refuses to run if no drawing is open.
        /// </summary>
        [CommandMethod("WRITESPECDWG", CommandFlags.Session)]
        public void RunDrawing() => RunCore(forcedScope: "dwg");

        void RunCore(string forcedScope)
        {
            using var logger = new RunLogger(
                forcedScope == "proj" ? "WRITESPECPROJ"
              : forcedScope == "dwg" ? "WRITESPECDWG"
              : "WRITESPEC");

            void EditorWrite(string line)
            {
                try
                {
                    // Re-acquire the editor each time: during close/reopen the
                    // captured reference goes stale and WriteMessage throws eNotApplicable.
                    AcadApp.DocumentManager.MdiActiveDocument?.Editor
                           ?.WriteMessage("\n" + line);
                }
                catch { /* swallow — happens when no doc context is active */ }
            }
            void Say(string line) { logger.Log(line); EditorWrite(line); }
            void Trace(string line) => logger.Log(line);
            // Surface a line to the editor AND mark it WARN/ERROR in the log,
            // so the diagnostic file separates real problems from chatter.
            void SayWarn(string line) { logger.Warn(line); EditorWrite(line); }
            void SayErr(string line) { logger.Error(line); EditorWrite(line); }

            Say($"[Спецификация] Запуск, лог: {logger.Path}");

            // Record the runtime context up front so a support log is
            // self-contained (which AutoCAD build, which project).
            try
            {
                Trace("AutoCAD: " + AcadApp.GetSystemVariable("ACADVER")
                    + " / product " + AcadApp.GetSystemVariable("PRODUCT"));
            }
            catch { }

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
            if (settings.LoadFailed)
                logger.Warn($"settings.json не прочитан ({settings.LoadError}) — используются значения по умолчанию; "
                    + "при сохранении старый файл будет скопирован в settings.json.unreadable.");
            var docMgr = AcadApp.DocumentManager;
            var activeDoc = docMgr.MdiActiveDocument;

            // Fail fast if the user clicked "Текущий чертёж" but no DWG is
            // open — no point spending minutes on a project-wide scan only
            // to discover the request was unfulfillable.
            if (forcedScope == "dwg"
                && (activeDoc == null || string.IsNullOrEmpty(activeDoc.Name)))
            {
                const string msg = "Команда «Текущий чертёж» требует открытый чертёж Plant 3D.";
                Say("[Спецификация] " + msg);
                WinForms.MessageBox.Show(msg, "Спецификация Plant 3D",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            // Disk-based scans require saved inputs. Never save or close users' drawings here.
            if (!ProjectScanner.EnsureDrawingsSaved(project, activeDoc, forcedScope == "dwg",
                                                    "Спецификация Plant 3D", SayErr))
                return;

            // Capture the active document path BEFORE the picker dialog
            // opens. Inside the dialog the user could close drawings or
            // switch to a different doc, which would shift MdiActiveDocument
            // under us. We commit to whatever was active at scan time —
            // that's the drawing whose parts populated activeDwgRecords
            // and whose path was tagged into PartRecord.SourceDwgPath.
            var activePath = activeDoc != null ? (activeDoc.Name ?? "") : "";
            var dwgLeaf = !string.IsNullOrEmpty(activePath)
                ? Path.GetFileName(activePath) : "";

            // Modeless progress popup: parented to AutoCAD's main window
            // so it inherits z-order, shown as Dialog so we can pump events
            // on the same thread. CollectFromProject reports against it
            // via the Action<int,int,string> callback below.
            var progress = new UI.ProgressWindow();
            new WindowInteropHelper(progress).Owner = AcadApp.MainWindow.Handle;
            progress.Indeterminate("Поиск чертежей проекта…");
            progress.Show();

            // Fast path for the explicit "Текущий чертёж" ribbon button:
            // scope is known BEFORE the picker opens, so we can read only
            // the active DWG instead of sweeping the whole project. Safe
            // because P3dPartConnection topology never crosses a drawing
            // boundary (ConnectionManager.GetConnections is scoped to one
            // Database) — bend-fold and valve-companion discovery operate
            // entirely within the active sheet. The only thing lost is the
            // cross-sheet "N parts of this line live in other drawings"
            // diagnostic, which by definition has no data in single-DWG mode.
            //
            // Plain WRITESPEC (forcedScope == null) still scans the whole
            // project even when the user later picks "dwg" in the dialog —
            // there the scope isn't known until after the scan.
            bool singleDwg = forcedScope == "dwg" && !string.IsNullOrEmpty(activePath);
            Say(singleDwg
                ? "[Спецификация] Сканирование текущего чертежа…"
                : "[Спецификация] Сканирование проекта…");
            ScanResult scan;
            try
            {
                scan = singleDwg
                    ? ProjectScanner.ScanSingleDrawing(project, activePath, Say, Trace, progress)
                    : ProjectScanner.ScanProject(project, Say, Trace, progress);
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
                logger.Warn($"Сканирование: пропущено чертежей из-за ошибок чтения: {scan.skippedFiles} (см. строки FAILED выше).");
                foreach (var failure in scan.failures) logger.Warn("  " + failure);
                const int shownFailures = 8;
                var failureList = string.Join("\n", scan.failures.Take(shownFailures).Select(s => "• " + s))
                    + (scan.failures.Count > shownFailures ? $"\n… и ещё {scan.failures.Count - shownFailures}" : "");
                WinForms.MessageBox.Show(
                    $"При сканировании пропущено чертежей: {scan.skippedFiles}.\n\n" +
                    failureList + "\n\n" +
                    $"Подробности см. в логе:\n{logger.Path}\n\n" +
                    "Генерация остановлена: нельзя гарантировать полноту спецификации. Исправьте ошибки чтения и повторите.",
                    "Предупреждение",
                    WinForms.MessageBoxButtons.OK,
                    WinForms.MessageBoxIcon.Warning);
                return;
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
                    // w / lw: own mass vs. linear mass — a bend counted in
                    // pieces gets its «Масса 1 ед.» from len × lw when it
                    // has no Weight (SpecRowBuilder.BendMassFromLinearWeight).
                    Trace($"  bend#{b.PnPID} class='{b.PnPClassName}' "
                        + $"line='{b.LineNumberTag ?? ""}' "
                        + $"len={b.Length}{b.LengthUnit} "
                        + $"w={b.Weight}{b.WeightUnit} lw={b.LinearWeight}{b.LinearWeightUnit} "
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
                                              activeDrawingAvailable: !string.IsNullOrEmpty(dwgLeaf),
                                              forcedScope: forcedScope);
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

            // Default: feed every project record into the builder. The
            // scope=dwg branch below narrows it to the active drawing by
            // SourceDwgPath — see note there for why PnPID-based filtering
            // would be wrong.
            IEnumerable<PartRecord> input = scan.records;
            // Diagnostic: dump every Pipe row's BucketKey fields so we
            // can see exactly which attribute separates pipes that SHOULD
            // merge into one row but appear as separate spec lines.
            // Key fields: Name | ND | Material | Spec | TypeMark | ItemCode | Manuf | LineTag
            var allPipes = scan.records
                .Where(r => string.Equals(r.PartCategory, "Pipe",
                                           StringComparison.OrdinalIgnoreCase)
                            && !r.IsPipeBend)
                .ToList();
            Trace($"Pipe partов (без bends): {allPipes.Count}");
            foreach (var r in allPipes.Take(40))
            {
                var nm = string.IsNullOrWhiteSpace(r.PartSizeLongDesc)
                       ? (string.IsNullOrWhiteSpace(r.PartFamilyLongDesc)
                          ? r.ShortDescription : r.PartFamilyLongDesc)
                       : r.PartSizeLongDesc;
                Trace($"  pipe#{r.PnPID} line='{r.LineNumberTag ?? ""}' "
                    + $"len={r.Length}{r.LengthUnit} "
                    + $"ND={r.NominalDiameter} mat='{r.Material ?? ""}' "
                    + $"spec='{r.Spec ?? ""}' std='{r.DesignStd ?? ""}' "
                    + $"ic='{r.ItemCode ?? ""}' mf='{r.Manufacturer ?? ""}' "
                    + $"name='{nm ?? ""}'");
            }
            // Diagnostic: dump every Pipe row's resolved LineNumberTag.
            // Helps explain "why does a line show N pipes in Data
            // Manager but N-1 in the spec" cases — usually one pipe's
            // LineGroup link is stale.
            var pipesByLine = scan.records
                .Where(r => string.Equals(r.PartCategory, "Pipe",
                                           StringComparison.OrdinalIgnoreCase)
                            && !r.IsPipeBend)
                .GroupBy(r => string.IsNullOrEmpty(r.LineNumberTag)
                              ? "<no line>"
                              : r.LineNumberTag)
                .OrderByDescending(g => g.Count())
                .ToList();
            Trace($"Pipe partов: {pipesByLine.Sum(g => g.Count())}, по линиям:");
            foreach (var g in pipesByLine)
            {
                Trace($"  line='{g.Key}' x{g.Count()}: "
                    + string.Join(", ", g.Take(10).Select(r =>
                        $"#{r.PnPID}(len={r.Length}{r.LengthUnit})")));
            }

            if (result.Scope == "dwg" && !string.IsNullOrEmpty(activePath))
            {
                // Filter strictly by source DWG path. PnPID-based filtering
                // is unsafe — IDs collide across drawings (drawing-local
                // namespace) so a part from another DWG that happens to
                // share a PnPID with anything in the active drawing would
                // leak into the spec.
                var inScope = scan.records
                    .Where(r => ProjectScanner.PathEquals(r.SourceDwgPath, activePath))
                    .ToList();

                // Diagnostic: when the user picked specific lines, log how
                // many parts of those lines live in OTHER drawings — they'll
                // be silently excluded by the scope filter and it's the most
                // common "why is qty short" answer. Only meaningful when the
                // scan actually swept the whole project (plain WRITESPEC with
                // scope=dwg picked in the dialog). The "Текущий чертёж" button
                // reads a single DWG, so scan.records only ever holds the
                // active drawing and this block has nothing to report.
                if (result.Lines != null)
                {
                    var sel = new HashSet<string>(result.Lines, StringComparer.Ordinal);
                    var hiddenByScope = scan.records
                        .Where(r => !ProjectScanner.PathEquals(r.SourceDwgPath, activePath))
                        .Where(r => sel.Contains(string.IsNullOrEmpty(r.LineNumberTag)
                                                  ? SpecConstants.NullLine
                                                  : r.LineNumberTag))
                        .ToList();
                    if (hiddenByScope.Count > 0)
                    {
                        var byLine = hiddenByScope
                            .GroupBy(r => r.LineNumberTag ?? "")
                            .Select(g => $"{g.Key}: {g.Count()}");
                        SayWarn($"[Спецификация] ⚠ scope=dwg исключает "
                          + $"{hiddenByScope.Count} парт(ов) из выбранных линий "
                          + $"которые находятся в других чертежах "
                          + $"({string.Join("; ", byLine)}). "
                          + $"Используйте 'Весь проект' чтобы их включить.");
                    }
                }

                input = inScope;
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
                DisableElementGrouping = result.DisableElementGrouping,
                IncludePipeBendsAsPipe = result.IncludePipeBendsAsPipe,
                Topology = scan.topology,
            });
            List<SpecRow> specRows;
            try { specRows = builder.Build(input); }
            catch (System.Exception ex)
            {
                SayErr(ex.Message);
                WinForms.MessageBox.Show("Расчёт остановлен: " + ex.Message,
                    "Ошибка данных спецификации", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
                return;
            }
            // Builder warnings are per-part / per-row; each one now also has a
            // row in the review section, so the editor gets only the summary
            // line at the end and the detail goes to the log.
            foreach (var w in builder.Warnings) logger.Warn(w);
            if (builder.DropReasons.Count > 0)
            {
                Trace("Фильтры (отброшено по причинам):");
                foreach (var kv in builder.DropReasons.OrderByDescending(kv => kv.Value))
                    Trace($"  {kv.Value,6}  {kv.Key}");
            }

            // «Позиции, требующие проверки»: unread parts first (they are the
            // most serious — nothing at all is known about them), then the
            // builder's findings in pipeline order. Unread parts have no
            // category/line/status, so the picker filters can't apply to
            // them — only the drawing scope can.
            var reviewItems = new List<ReviewItem>();
            reviewItems.AddRange(result.Scope == "dwg" && !string.IsNullOrEmpty(activePath)
                ? scan.unreadParts.Where(u => ProjectScanner.PathEquals(u.DrawingPath, activePath))
                : scan.unreadParts);
            reviewItems.AddRange(builder.ReviewItems);
            var reviewRows = ReviewSection.BuildRows(reviewItems);
            if (reviewItems.Count > 0)
            {
                logger.Warn($"Позиции, требующие проверки: {reviewItems.Count} деталей/строк → {reviewRows.Count} строк раздела:");
                foreach (var it in reviewItems) logger.Warn("  " + ReviewSection.Describe(it));
            }

            if (specRows.Count == 0 && reviewRows.Count == 0)
            {
                const string msg = "Спецификация пуста. Выбранные фильтры исключили все компоненты.";
                SayWarn("[Спецификация] " + msg);
                WinForms.MessageBox.Show(msg, "Спецификация Plant 3D",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }
            if (specRows.Count == 0)
            {
                // Nothing countable, but there ARE doubts: the parts the user
                // selected were all rejected for a reason (zero length,
                // unknown unit, unreadable, …). Stopping here used to hide
                // those reasons in the log; the document now carries only the
                // «Позиции, требующие проверки» section so the user sees WHY
                // the spec is empty.
                SayWarn($"[Спецификация] Основная спецификация пуста: ни одна выбранная деталь не прошла проверку. "
                      + $"Документ будет содержать только раздел \"{SpecConstants.ReviewSectionHeader}\" "
                      + $"({reviewRows.Count} позиций). Подробности в логе: {logger.Path}");
            }

            var outputPath = ProjectScanner.AskOutputPath(settings.LastOutputDir, project.Name ?? "Спецификация",
                                                          "Сохранить спецификацию");
            if (string.IsNullOrEmpty(outputPath))
            {
                Say("[Спецификация] Сохранение отменено.");
                return;
            }

            try
            {
                SpecTemplateWriter.Write(result.TemplatePath, outputPath, specRows,
                                         groupByLine: result.GroupByLine,
                                         reviewRows: reviewRows);
            }
            catch (SpecFileLockedException ex) when (ex.IsTemplate)
            {
                SayErr($"[Спецификация] FAILED: файл шаблона занят другим процессом: {ex.FilePath}");
                WinForms.MessageBox.Show(
                    $"Не удалось сформировать спецификацию.\n\nФайл ШАБЛОНА открыт в Microsoft Word или другой программе:\n{ex.FilePath}\n\nЗакройте шаблон в Word и попробуйте снова.",
                    "Спецификация Plant 3D",
                    WinForms.MessageBoxButtons.OK,
                    WinForms.MessageBoxIcon.Warning);
                return;
            }
            catch (System.IO.IOException ex) when (ex is SpecFileLockedException || ProjectScanner.IsFileLocked(ex))
            {
                SayErr($"[Спецификация] FAILED: файл занят другим процессом: {ex.Message}");
                WinForms.MessageBox.Show(
                    $"Не удалось сохранить спецификацию.\n\nФайл уже открыт в Microsoft Word или другой программе:\n{outputPath}\n\nПожалуйста, закройте этот файл в Word и попробуйте снова.",
                    "Спецификация Plant 3D",
                    WinForms.MessageBoxButtons.OK,
                    WinForms.MessageBoxIcon.Warning);
                return;
            }
            catch (System.Exception ex) when (ex is System.IO.FileFormatException || ex.GetType().FullName.Contains("OpenXml"))
            {
                SayErr($"[Спецификация] FAILED: некорректный файл шаблона: {ex.Message}");
                WinForms.MessageBox.Show(
                    $"Выбранный файл шаблона поврежден или не является корректным документом Word (.docx):\n{result.TemplatePath}\n\nПожалуйста, проверьте файл шаблона.",
                    "Спецификация Plant 3D",
                    WinForms.MessageBoxButtons.OK,
                    WinForms.MessageBoxIcon.Error);
                return;
            }
            catch (System.Exception ex)
            {
                SayErr($"[Спецификация] FAILED: {ex.GetType().Name}: {ex.Message}");
                Trace(ex.StackTrace ?? "");
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
            // The picker showed the chips of the chosen scope only. A
            // drawing-scope view is a PART of the project: keys it did not
            // show keep their previous checked/unchecked state instead of
            // being stored as "seen and unchecked" (they then silently
            // dropped out of the next «Весь проект» run) or being forgotten.
            // See PickerSelectionMemory.
            var shownFacts = result.Scope == "dwg" ? activeFacts : facts;
            var partialView = result.Scope == "dwg";
            (settings.Statuses, settings.KnownStatuses) = PickerSelectionMemory.Merge(
                settings.Statuses, settings.KnownStatuses,
                shownFacts.Statuses.Select(s => s.key), result.Statuses, partialView);
            (settings.Categories, settings.KnownCategories) = PickerSelectionMemory.Merge(
                settings.Categories, settings.KnownCategories,
                shownFacts.Categories.Select(c => c.key), result.Categories, partialView);
            (settings.Lines, settings.KnownLines) = PickerSelectionMemory.Merge(
                settings.Lines, settings.KnownLines,
                shownFacts.Lines.Select(l => l.key), result.Lines, partialView);

            settings.GroupByLine = result.GroupByLine;
            settings.ExcludeValveCompanions = result.ExcludeValveCompanions;
            settings.ExcludeEquipment = result.ExcludeEquipment;
            settings.ExcludeConnectors = result.ExcludeConnectors;
            settings.IncludePipeBendsAsPipe = result.IncludePipeBendsAsPipe;
            settings.OpenAfterGenerate = result.OpenAfterGenerate;
            settings.DisableElementGrouping = result.DisableElementGrouping;
            settings.CheckForUpdates = result.CheckForUpdates;
            // The .docx is already on disk; a settings-write failure (e.g.
            // %APPDATA% read-only / disk full) must not turn a successful
            // generation into an error. Log it and move on.
            try { settings.Save(); }
            catch (System.Exception ex) { Trace($"Не удалось сохранить настройки: {ex.Message}"); }

            Say(specRows.Count > 0
                ? $"[Спецификация] Готово: {specRows.Count} строк → {outputPath}"
                : $"[Спецификация] Готово (только позиции, требующие проверки) → {outputPath}");
            if (reviewRows.Count > 0)
                SayWarn($"[Спецификация] В спецификацию добавлен раздел \"{SpecConstants.ReviewSectionHeader}\": "
                      + $"{reviewRows.Count} позиций. Подробности в логе: {logger.Path}");
            if (result.OpenAfterGenerate)
                ProjectScanner.TryOpenInWord(outputPath, "Спецификация", Say);
        }
    }
}
