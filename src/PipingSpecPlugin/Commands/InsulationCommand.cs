using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Interop;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.PlantInstance;
using PipingSpecPlugin.Settings;
using PipingSpecPlugin.Spec;
using PipingSpecPlugin.UI;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// «Данные для расчёта изоляции»: per line, the insulated straight pipes
    /// (metres) and fittings — elbows, pipe bends, tees (pieces) — as input
    /// for an insulation calculation program. Counted like the spec: same
    /// scan, filters, names and aggregation (SpecRowBuilder, grouped by
    /// line); see InsulationTakeoff for the bend split (Д1 + Д2 → pipe).
    /// Written as a plain list (InsulationListWriter), no template.
    /// The ribbon's «Изоляция» panel runs INSULSPECPROJ / INSULSPECDWG with
    /// the scope fixed by the button; INSULSPEC (command line) lets the
    /// dialog choose. Trace: %APPDATA%\PipingSpecPlugin\last-run-insulspec.log.
    /// </summary>
    public class InsulationCommand
    {
        const string Tag = "Изоляция";
        const string Caption = "Данные для расчёта изоляции";

        [CommandMethod("INSULSPEC", CommandFlags.Session)]
        public void Run() => RunCore(forcedScope: null);

        /// <summary>Ribbon «Изоляция → Весь проект».</summary>
        [CommandMethod("INSULSPECPROJ", CommandFlags.Session)]
        public void RunProject() => RunCore(forcedScope: "proj");

        /// <summary>
        /// Ribbon «Изоляция → Текущий чертёж»: reads only the active drawing
        /// (no project sweep). Refuses to run without an open drawing.
        /// </summary>
        [CommandMethod("INSULSPECDWG", CommandFlags.Session)]
        public void RunDrawing() => RunCore(forcedScope: "dwg");

        /// <summary>Ribbon «Изоляция → Методика»: how the data is collected, no scan.</summary>
        [CommandMethod("INSULHELP")]
        public void Help()
        {
            var win = new InsulationHelpWindow();
            new WindowInteropHelper(win).Owner = AcadApp.MainWindow.Handle;
            win.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
            win.ShowDialog();
        }

        void RunCore(string forcedScope)
        {
            using var logger = new RunLogger(
                forcedScope == "proj" ? "INSULSPECPROJ"
              : forcedScope == "dwg" ? "INSULSPECDWG"
              : "INSULSPEC");
            void EditorWrite(string line)
            {
                try { AcadApp.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage("\n" + line); }
                catch { /* no document context */ }
            }
            void Say(string line) { logger.Log(line); EditorWrite(line); }
            void Trace(string line) => logger.Log(line);
            void SayWarn(string line) { logger.Warn(line); EditorWrite(line); }
            void SayErr(string line) { logger.Error(line); EditorWrite(line); }

            Say($"[{Tag}] Запуск, лог: {logger.Path}");
            var project = PlantApplication.CurrentProject;
            if (project == null)
            {
                const string msg = "Не найден открытый проект AutoCAD Plant 3D. Откройте проект перед формированием данных.";
                Say($"[{Tag}] " + msg);
                WinForms.MessageBox.Show(msg, Caption, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            var settings = PluginSettings.Load();
            if (settings.LoadFailed)
                logger.Warn($"settings.json не прочитан ({settings.LoadError}) — используются значения по умолчанию.");
            var activeDoc = AcadApp.DocumentManager.MdiActiveDocument;
            var singleDwg = forcedScope == "dwg";
            if (singleDwg && (activeDoc == null || string.IsNullOrEmpty(activeDoc.Name)))
            {
                const string msg = "Команда «Текущий чертёж» требует открытый чертёж Plant 3D.";
                Say($"[{Tag}] " + msg);
                WinForms.MessageBox.Show(msg, Caption, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }
            if (!ProjectScanner.EnsureDrawingsSaved(project, activeDoc, singleDwg, Caption, SayErr))
                return;
            var activePath = activeDoc?.Name ?? "";
            var dwgLeaf = string.IsNullOrEmpty(activePath) ? "" : Path.GetFileName(activePath);

            var progress = new ProgressWindow();
            new WindowInteropHelper(progress).Owner = AcadApp.MainWindow.Handle;
            progress.Indeterminate("Поиск чертежей проекта…");
            progress.Show();
            Say(singleDwg ? $"[{Tag}] Сканирование текущего чертежа…" : $"[{Tag}] Сканирование проекта…");
            ScanResult scan;
            try
            {
                scan = singleDwg
                    ? ProjectScanner.ScanSingleDrawing(project, activePath, Say, Trace, progress, tag: Tag)
                    : ProjectScanner.ScanProject(project, Say, Trace, progress, tag: Tag);
            }
            finally { progress.Complete(); }
            if (progress.IsCancelled)
            {
                Say($"[{Tag}] Сканирование отменено пользователем.");
                return;
            }
            Say($"[{Tag}] Найдено компонентов: {scan.records.Count}");
            if (scan.skippedFiles > 0)
            {
                logger.Warn($"Сканирование: пропущено чертежей из-за ошибок чтения: {scan.skippedFiles}.");
                foreach (var failure in scan.failures) logger.Warn("  " + failure);
                WinForms.MessageBox.Show(
                    $"При сканировании пропущено чертежей: {scan.skippedFiles}.\n\n"
                    + string.Join("\n", scan.failures.Take(8).Select(s => "• " + s))
                    + (scan.failures.Count > 8 ? $"\n… и ещё {scan.failures.Count - 8}" : "")
                    + $"\n\nПодробности см. в логе:\n{logger.Path}\n\n"
                    + "Формирование остановлено: нельзя гарантировать полноту данных.",
                    Caption, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            // Insulated pipes, bends, elbows, tees — the parts the picker offers.
            var parts = scan.records
                .Where(r => !scan.ownedPnPIds.Contains(r.PnPID)
                            && InsulationTakeoff.KindOf(r) != null && InsulationTakeoff.IsInsulated(r))
                .ToList();
            var drawingParts = parts.Where(r => ProjectScanner.PathEquals(r.SourceDwgPath, activePath)).ToList();
            var facts = ProjectFacts.From(parts);
            var drawingFacts = ProjectFacts.From(drawingParts);
            Trace($"Труб, изгибов, отводов, тройников с изоляцией: {parts.Count} (в текущем чертеже: {drawingParts.Count})");

            var picker = new InsulationPickerWindow(facts, drawingFacts, project.Name, dwgLeaf, settings,
                                                    activeDrawingAvailable: !string.IsNullOrEmpty(dwgLeaf),
                                                    forcedScope: forcedScope);
            new WindowInteropHelper(picker).Owner = AcadApp.MainWindow.Handle;
            if (picker.ShowDialog() != true)
            {
                Say($"[{Tag}] Отменено пользователем.");
                return;
            }
            var result = picker.Result;
            Trace($"Picker: scope={result.Scope}; lines={result.Lines.Count}; statuses={result.Statuses.Count}");

            var inScope = result.Scope == "dwg" && !string.IsNullOrEmpty(activePath);
            var input = inScope ? drawingParts : parts;
            var prepared = InsulationTakeoff.Prepare(input);
            foreach (var line in prepared.Trace) Trace(line);

            // The spec's own aggregation: filters, names, line grouping,
            // pipe metres rounded up to 0.1 m, bends in pieces.
            var builder = new SpecRowBuilder(new SpecBuilderOptions
            {
                Statuses = result.Statuses,
                Lines = result.Lines,
                GroupByLine = true,
                ExcludeEquipment = false,
                ExcludeConnectors = false,
                EquipmentOwnedPnPIds = scan.ownedPnPIds,
                IncludePipeBendsAsPipe = false,
                Topology = scan.topology,
            });
            List<SpecRow> rows;
            try { rows = builder.Build(prepared.Records); }
            catch (System.Exception ex)
            {
                SayErr(ex.Message);
                WinForms.MessageBox.Show("Расчёт остановлен: " + ex.Message, Caption,
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
                return;
            }
            foreach (var w in builder.Warnings) logger.Warn(w);

            var reviewItems = new List<ReviewItem>();
            reviewItems.AddRange(inScope
                ? scan.unreadParts.Where(u => ProjectScanner.PathEquals(u.DrawingPath, activePath))
                : scan.unreadParts);
            reviewItems.AddRange(prepared.ReviewItems);
            reviewItems.AddRange(InsulationTakeoff.WithoutMassNotes(builder.ReviewItems));
            var reviewRows = ReviewSection.BuildRows(reviewItems);
            if (reviewItems.Count > 0)
            {
                logger.Warn($"Позиции, требующие проверки: {reviewItems.Count} деталей → {reviewRows.Count} строк:");
                foreach (var it in reviewItems) logger.Warn("  " + ReviewSection.Describe(it));
            }

            if (rows.Count == 0 && reviewRows.Count == 0)
            {
                const string msg = "На выбранных линиях нет труб, отводов, изгибов и тройников с изоляцией.";
                SayWarn($"[{Tag}] " + msg);
                WinForms.MessageBox.Show(msg, Caption, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }

            var outputPath = ProjectScanner.AskOutputPath(settings.LastOutputDir,
                (project.Name ?? "Проект") + " - данные для расчёта изоляции", "Сохранить данные для расчёта изоляции");
            if (string.IsNullOrEmpty(outputPath))
            {
                Say($"[{Tag}] Сохранение отменено.");
                return;
            }

            try
            {
                var subtitle = "Проект: " + (project.Name ?? "")
                             + (inScope ? ", чертёж " + dwgLeaf : ", весь проект");
                InsulationListWriter.Write(outputPath, subtitle, rows, reviewRows);
            }
            catch (IOException ex) when (ex is SpecFileLockedException || ProjectScanner.IsFileLocked(ex))
            {
                SayErr($"[{Tag}] FAILED: файл занят другим процессом: {ex.Message}");
                WinForms.MessageBox.Show(
                    $"Не удалось сохранить документ.\n\nФайл уже открыт в Microsoft Word или другой программе:\n{outputPath}\n\nЗакройте его и попробуйте снова.",
                    Caption, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }
            catch (System.Exception ex)
            {
                SayErr($"[{Tag}] FAILED: {ex.GetType().Name}: {ex.Message}");
                Trace(ex.StackTrace ?? "");
                WinForms.MessageBox.Show($"Ошибка формирования документа:\n\n{ex.Message}\n\nПодробности в логе:\n{logger.Path}",
                    Caption, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
                return;
            }

            // Persist only after a successful write, like WRITESPEC.
            settings.LastOutputDir = Path.GetDirectoryName(outputPath);
            settings.InsulationScope = result.Scope;
            settings.OpenAfterGenerate = result.OpenAfterGenerate;
            var shown = result.Scope == "dwg" ? drawingFacts : facts;
            var partial = result.Scope == "dwg";
            (settings.InsulationLines, settings.KnownInsulationLines) = PickerSelectionMemory.Merge(
                settings.InsulationLines, settings.KnownInsulationLines, shown.Lines.Select(l => l.key), result.Lines, partial);
            (settings.InsulationStatuses, settings.KnownInsulationStatuses) = PickerSelectionMemory.Merge(
                settings.InsulationStatuses, settings.KnownInsulationStatuses, shown.Statuses.Select(s => s.key), result.Statuses, partial);
            try { settings.Save(); }
            catch (System.Exception ex) { Trace($"Не удалось сохранить настройки: {ex.Message}"); }

            Say($"[{Tag}] Готово: линий {InsulationListWriter.GroupByLine(rows).Count}, строк {rows.Count} → {outputPath}");
            if (reviewRows.Count > 0)
                SayWarn($"[{Tag}] Добавлен раздел \"{SpecConstants.ReviewSectionHeader}\": "
                      + $"{reviewRows.Count} позиций. Подробности в логе: {logger.Path}");
            if (result.OpenAfterGenerate)
                ProjectScanner.TryOpenInWord(outputPath, Tag, Say);
        }
    }
}
