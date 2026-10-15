using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Interop;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using PipingSpecPlugin.Audit;
using PipingSpecPlugin.Settings;
using PipingSpecPlugin.UI;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PipingSpecPlugin.Commands
{
    public sealed class AuditCommand
    {
        const string Title = "Аудит модели";

        [CommandMethod("PSAUDIT", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void Run()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using var logger = new RunLogger("PSAUDIT");
            // Line numbers and properties come from the saved copy (the live
            // DLM cache is stale). Same policy as SELECTBYLINE: unsaved changes
            // → ask to save; it used to refuse on any DBMOD bit, view changes too.
            if (!DrawingSaveGuard.EnsureSavedForDiskRead(doc, doc.Database, Title, logger.Log)) return;
            AuditLineCatalog catalog;
            try { catalog = AuditLineCatalog.Read(doc, logger.Log); }
            catch (System.Exception ex)
            {
                logger.Error("Каталог линий: " + ex);
                System.Windows.MessageBox.Show(ex.Message + "\n\nЖурнал: " + logger.Path, Title);
                return;
            }
            var settings = PluginSettings.Load();
            var window = new AuditWindow(doc.Name, catalog.Lines, options =>
                {
                    SaveTolerance(settings, options.CollisionToleranceMm, logger);
                    return ReadSnapshot(doc, options, catalog, logger);
                },
                (owner, issues) => AuditModelView.Show(doc, owner, issues, logger), settings.AuditCollisionToleranceMm);
            new WindowInteropHelper(window).Owner = AcadApp.MainWindow.Handle;
            if (window.ShowDialog() != true || window.SelectedKeys.Count == 0) return;
            var ids = new List<ObjectId>();
            foreach (var key in window.SelectedKeys)
            {
                try
                {
                    var id = doc.Database.GetObjectId(false, new Handle(long.Parse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture)), 0);
                    if (!id.IsNull && !id.IsErased) ids.Add(id);
                }
                catch (System.Exception ex) { logger.Log($"Не найден элемент {key}: {ex.Message}"); }
            }
            doc.Editor.SetImpliedSelection(ids.ToArray());
            doc.Editor.WriteMessage($"\n[Аудит] Выделено элементов: {ids.Count}.");
        }

        // Scriptable read-only report, also used for native integration checks.
        // Reads the same saved-copy catalogue as PSAUDIT, so both commands see
        // the same line numbers.
        [CommandMethod("PSAUDITJSON", CommandFlags.Modal)]
        public void Export()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var path = ed.GetString(new PromptStringOptions("\nПуть нового JSON-отчёта: ") { AllowSpaces = true });
            if (path.Status != PromptStatus.OK) return;
            var output = path.StringResult.Trim().Trim('"');
            if (output.Length == 0) return;
            try
            {
                // A bare name lands next to the drawing, not in AutoCAD's
                // current directory.
                if (!Path.IsPathRooted(output) && File.Exists(doc.Name))
                    output = Path.Combine(Path.GetDirectoryName(doc.Name), output);
                output = Path.GetFullPath(output);
            }
            catch (System.Exception ex) { ed.WriteMessage($"\nНедопустимый путь: {ex.Message}"); return; }
            if (File.Exists(output)) { ed.WriteMessage("\nФайл уже существует. Укажите новое имя."); return; }
            // Checked before the audit, which can take minutes on a large model.
            if (!Directory.Exists(Path.GetDirectoryName(output)))
            { ed.WriteMessage($"\nПапка не найдена: {Path.GetDirectoryName(output)}"); return; }

            using var logger = new RunLogger("PSAUDITJSON");
            // No dialog here (scripts): unsaved changes stop the command.
            if (DrawingSaveGuard.IsDirty(doc, logger.Log))
            {
                ed.WriteMessage("\nАудит не выполнен: в чертеже есть несохранённые изменения. Сохраните чертёж — номера линий читаются из сохранённой копии.");
                return;
            }
            try
            {
                var catalog = AuditLineCatalog.Read(doc, logger.Log);
                var options = new AuditOptions { CollisionToleranceMm = Math.Max(0, PluginSettings.Load().AuditCollisionToleranceMm) };
                foreach (var rule in AuditRules.All) options.Enabled.Add(rule.Id);
                var snapshot = ReadSnapshot(doc, options, catalog, logger);
                var issues = AuditRules.Run(snapshot, options);
                // CreateNew: never replace a file that appeared meanwhile.
                using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream))
                    writer.Write(AuditReport.Serialize(snapshot, options, issues));
                ed.WriteMessage($"\nАудит: деталей {snapshot.Parts.Count}, замечаний {issues.Count}. Отчёт: {output}\n");
            }
            catch (System.Exception ex)
            {
                // The stack trace belongs in the log, not on the command line.
                logger.Error(ex.ToString());
                ed.WriteMessage($"\nАудит не завершён: {ex.Message}\nПодробности: {logger.Path}\n");
            }
        }

        // The tolerance entered in the window is kept for the next run (and
        // for PSAUDITJSON). Settings that failed to load are not overwritten.
        static void SaveTolerance(PluginSettings settings, double toleranceMm, RunLogger logger)
        {
            if (settings.LoadFailed || settings.AuditCollisionToleranceMm == toleranceMm) return;
            try
            {
                settings.AuditCollisionToleranceMm = toleranceMm;
                settings.Save();
            }
            catch (System.Exception ex) { logger.Log("Допуск коллизий не сохранён: " + ex.Message); }
        }

        static AuditSnapshot ReadSnapshot(Document doc, AuditOptions options, AuditLineCatalog catalog, RunLogger logger)
        {
            var snapshot = DrawingAuditReader.Read(doc, options, logger.Log, catalog.Parts);
            snapshot.ReadIssues.InsertRange(0, catalog.ReadIssues);
            return snapshot;
        }
    }
}
