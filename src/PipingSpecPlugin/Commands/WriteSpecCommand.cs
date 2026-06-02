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

            if (activeDoc != null)
            {
                string activeDocName = activeDoc.Name;

                // Активный документ — просто сохраняем если есть изменения.
                // Читать из памяти не нужно: ReadOneDrawing всегда читает с диска,
                // поэтому достаточно убедиться что файл на диске актуален.
                try
                {
                    object dbmodObj = AcadApp.GetSystemVariable("DBMOD");
                    bool activeModified = System.Convert.ToInt32(dbmodObj) != 0;
                    if (activeModified)
                    {
                        Say($"[Спецификация] Автосохранение {Path.GetFileName(activeDocName)}...");
                        bool saved = false;
                        try { activeDoc.Database.SaveAs(activeDocName, true, DwgVersion.Current, activeDoc.Database.SecurityParameters); saved = true; }
                        catch (System.Exception ex)
                        {
                            Trace($"SaveAs активного {Path.GetFileName(activeDocName)} не удался: {ex.Message}; пробую Save()");
                            try { activeDoc.Database.Save(); saved = true; }
                            catch (System.Exception ex2) { Trace($"Save() активного тоже не удался: {ex2.Message}"); }
                        }
                        // Если оба сохранения упали — диск содержит устаревшую
                        // версию, и спецификация будет неполной без следа.
                        // Предупреждаем явно, а не молчим.
                        if (!saved)
                            SayWarn($"[Спецификация] ВНИМАНИЕ: не удалось сохранить {Path.GetFileName(activeDocName)} — данные могут быть устаревшими.");
                    }
                }
                catch (System.Exception ex) { Trace($"Проверка модификации активного документа не удалась: {ex.Message}"); }

                // Фоновые открытые чертежи с несохранёнными изменениями —
                // закрываем и открываем заново, чтобы их данные оказались на диске.
                //
                // Снимок путей делаем ДО закрытия/открытия: CloseAndSave + Open
                // мутируют docMgr, поэтому итерировать по нему напрямую нельзя
                // (InvalidOperationException / пропуск документов). Сначала
                // собираем список «грязных» фоновых чертежей, затем работаем
                // по снимку — мутация коллекции больше на цикл не влияет.
                var dirtyBackgroundPaths = new List<string>();
                foreach (Document doc in docMgr)
                {
                    if (doc == null || string.IsNullOrEmpty(doc.Name) || doc == activeDoc) continue;
                    try
                    {
                        // The active doc uses the DBMOD sysvar above, but DBMOD
                        // is global — it only reflects the active document, so
                        // for background docs we must ask each one's COM
                        // AcadDocument.Saved instead. The two mechanisms are
                        // intentional: there's no per-document dirty flag on
                        // the managed Database that matches DBMOD.
                        dynamic acadDoc = doc.GetAcadDocument();
                        bool bgModified = !acadDoc.Saved;
                        if (bgModified)
                            dirtyBackgroundPaths.Add(doc.Name);
                    }
                    catch (System.Exception ex)
                    {
                        Trace($"Не удалось проверить состояние {Path.GetFileName(doc.Name)}: {ex.Message}");
                    }
                }

                foreach (string docPath in dirtyBackgroundPaths)
                {
                    Say($"[Спецификация] Сохранение и перезапуск {Path.GetFileName(docPath)}...");
                    Document doc = FindOpenDocument(docMgr, docPath);
                    if (doc == null)
                    {
                        Trace($"Документ {Path.GetFileName(docPath)} больше не открыт — пропуск перезапуска.");
                        continue;
                    }
                    try
                    {
                        doc.CloseAndSave(docPath);
                        docMgr.Open(docPath, false);
                    }
                    catch (System.Exception ex)
                    {
                        Trace($"Ошибка перезапуска {Path.GetFileName(docPath)}: {ex.Message}");
                    }
                }

                // Восстанавливаем активный документ
                if (!string.IsNullOrEmpty(activeDocName))
                {
                    Document restored = FindOpenDocument(docMgr, activeDocName);
                    if (restored != null)
                    {
                        try { docMgr.MdiActiveDocument = restored; }
                        catch (System.Exception ex)
                        {
                            Trace($"Не удалось вернуть активным {Path.GetFileName(activeDocName)}: {ex.Message}");
                        }
                    }
                    else
                    {
                        Trace($"Активный документ {Path.GetFileName(activeDocName)} не найден среди открытых после перезапуска.");
                    }
                }

                activeDoc = docMgr.MdiActiveDocument;
                project = PlantApplication.CurrentProject;
                if (project == null)
                {
                    const string msg = "Не найден открытый проект AutoCAD Plant 3D после перезапуска чертежей.";
                    Say("[Спецификация] " + msg);
                    return;
                }
            }

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
                    ? ScanSingleDrawing(project, activePath, Say, Trace, progress)
                    : ScanWithCache(project, Say, Trace, progress);
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
                // Per-pipe breakdown so we can see whether bend metres
                // land on the right line. Caps at 20 entries — projects
                // rarely have more than a handful of folded pipes.
                var byIdAll2 = new Dictionary<int, PartRecord>();
                foreach (var rr in scan.records)
                    if (!byIdAll2.ContainsKey(rr.PnPID)) byIdAll2[rr.PnPID] = rr;
                foreach (var kv in bendFold.ExtraPipeMetres.Take(20))
                {
                    byIdAll2.TryGetValue(kv.Key, out var pipe);
                    bendFold.ExtraPipeLineTags.TryGetValue(kv.Key, out var bendLine);
                    Trace($"  pipe#{kv.Key} (pipeLine='{pipe?.LineNumberTag ?? "?"}', "
                        + $"bendLine='{bendLine ?? "?"}') += {kv.Value * 1000:F1} mm");
                }
            }

            if (result.Scope == "dwg" && !string.IsNullOrEmpty(activePath))
            {
                // Filter strictly by source DWG path. PnPID-based filtering
                // is unsafe — IDs collide across drawings (drawing-local
                // namespace) so a part from another DWG that happens to
                // share a PnPID with anything in the active drawing would
                // leak into the spec.
                var inScope = scan.records
                    .Where(r => PathEquals(r.SourceDwgPath, activePath))
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
                        .Where(r => !PathEquals(r.SourceDwgPath, activePath))
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
                BendsConsumed = bendFold?.BendsConsumed,
                ExtraPipeMetres = bendFold?.ExtraPipeMetres,
                ExtraPipeLineTags = bendFold?.ExtraPipeLineTags,
                ExtraPipeOrphanInfo = bendFold?.ExtraPipeOrphanInfo,
                DisableElementGrouping = result.DisableElementGrouping,
            });
            var specRows = builder.Build(input);
            foreach (var w in builder.Warnings) logger.Warn(w);
            if (builder.DropReasons.Count > 0)
            {
                Trace("Фильтры (отброшено по причинам):");
                foreach (var kv in builder.DropReasons.OrderByDescending(kv => kv.Value))
                    Trace($"  {kv.Value,6}  {kv.Key}");
            }

            if (specRows.Count == 0)
            {
                const string msg = "Спецификация пуста. Выбранные фильтры исключили все компоненты.";
                SayWarn("[Спецификация] " + msg);
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
            catch (System.IO.IOException ex) when (IsFileLocked(ex))
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
            settings.Statuses = result.Statuses?.ToArray();
            settings.Categories = result.Categories?.ToArray();
            settings.Lines = result.Lines?.ToArray();

            settings.KnownStatuses = facts.Statuses.Select(s => s.key).ToArray();
            settings.KnownCategories = facts.Categories.Select(c => c.key).ToArray();
            settings.KnownLines = facts.Lines.Select(l => l.key).ToArray();

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

        private static bool IsFileLocked(System.IO.IOException exception)
        {
            int errorCode = System.Runtime.InteropServices.Marshal.GetHRForException(exception) & ((1 << 16) - 1);
            return errorCode == 32 || errorCode == 33; // 32 = ERROR_SHARING_VIOLATION, 33 = ERROR_LOCK_VIOLATION
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
        /// Read a single DWG — the active drawing — instead of the whole
        /// project. Used by the "Текущий чертёж" button (forcedScope=dwg)
        /// where scope is fixed before the picker. Not cached: a one-file
        /// read is already fast, and ScanCache is keyed on the full project
        /// fingerprint which doesn't apply here.
        ///
        /// records == activeDwgRecords by construction (one and the same
        /// drawing), so ProjectFacts/activeFacts in RunCore match and the
        /// downstream scope=dwg filter is a no-op.
        /// </summary>
        static ScanResult ScanSingleDrawing(PlantProject project, string activePath,
            Action<string> say, Action<string> trace, UI.ProgressWindow progress)
        {
            // Need the project DataLinksManager: a side-DB read alone can't
            // resolve every property without the project-level link blob.
            Project piping = null;
            foreach (var pp in project.ProjectParts)
                if (pp is Autodesk.ProcessPower.P3dProjectParts.PipingProject)
                { piping = pp; break; }
            var projectDlm = piping?.DataLinksManager as DataLinksManager;

            progress.Report(0, 1, $"Чертёж: {Path.GetFileName(activePath)}");
            var scan = new ScanResult();

            if (!File.Exists(activePath))
            {
                say($"[Спецификация] Активный чертёж не найден на диске: {Path.GetFileName(activePath)}");
                scan.skippedFiles++;
                return scan;
            }

            try
            {
                PartReadResult result = ReadOneDrawing(activePath, activePath, projectDlm, trace);
                if (result != null)
                {
                    foreach (var r in result.Records)
                        r.SourceDwgPath = activePath;
                    scan.records.AddRange(result.Records);
                    scan.activeDwgRecords.AddRange(result.Records);
                    foreach (var id in result.EquipmentOwnedPnPIds) scan.ownedPnPIds.Add(id);
                    foreach (var kv in result.Topology)
                        scan.topology[kv.Key] = new List<int>(kv.Value);
                }
            }
            catch (System.Exception ex)
            {
                trace($"FAILED: {Path.GetFileName(activePath)}: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    trace($"  inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                scan.skippedFiles++;
            }
            progress.Report(1, 1, $"Готово: {Path.GetFileName(activePath)}");
            return scan;
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

                    // Tag every record with the absolute path it came from.
                    // The scope=dwg filter (and any future per-drawing
                    // diagnostics) must compare by path rather than PnPID:
                    // PnPIDs are drawing-local row-ids and the same int
                    // identifies different parts across different DWGs.
                    foreach (var r in result.Records)
                        r.SourceDwgPath = path;

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
            // Всегда читаем с диска как side-DB — это гарантирует актуальность
            // данных вне зависимости от того, сохранил ли пользователь чертёж
            // до или после нажатия «Сгенерировать». Чтение из памяти (activeDoc.Database)
            // невозможно использовать надёжно: даже после QSAVE DLM-кеш в памяти
            // остаётся устаревшим и не отражает новые элементы. Автосохранение
            // активного документа в Run() гарантирует что последняя версия
            // уже лежит на диске к моменту вызова этого метода.
            using var db = new Database(false, true);
            db.ReadDwgFile(path, FileShare.ReadWrite, true, "");
            db.CloseInput(true);
            string dwgName = Path.GetFileName(path);
            var result = PartReader.Read(db, projectDlm,
                msg => trace?.Invoke($"  [{dwgName}] {msg}"));

            // Surface read-stage drops in the log: a part that vanished here
            // never reaches the filter pipeline, so DropReasons won't show it.
            if (result != null && result.SkippedRows > 0)
                trace?.Invoke($"  [{dwgName}] пропущено строк при чтении: {result.SkippedRows} (см. строки выше)");
            if (result != null && result.WorkingDbSwapFailed)
                trace?.Invoke($"  [{dwgName}] ВНИМАНИЕ: swap WorkingDatabase не удался — часть строк могла не прочитаться.");
            return result;
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

        // Locate a still-open Document by path. Used after we close/reopen
        // background drawings: the original Document objects from the
        // pre-mutation snapshot are stale, so we re-resolve by path each time.
        static Document FindOpenDocument(DocumentCollection docMgr, string path)
        {
            foreach (Document d in docMgr)
                if (d != null && PathEquals(d.Name, path))
                    return d;
            return null;
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
