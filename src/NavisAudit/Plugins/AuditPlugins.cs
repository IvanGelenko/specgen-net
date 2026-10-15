using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisAudit.Core;
using NavisAudit.Navis;
using NavisAudit.UI;
using PipingSpecPlugin.Audit;
using ComBridge = Autodesk.Navisworks.Api.ComApi.ComApiBridge;
using NavisApp = Autodesk.Navisworks.Api.Application;

namespace NavisAudit.Plugins
{
    // «Аудит трубопроводов» on the Add-ins tab.
    [Plugin("NavisAudit", "PSPG", DisplayName = "Аудит трубопроводов",
        ToolTip = "Аудит трубопроводов Plant 3D в модели Navisworks: коллизии, толщины стенок, материал, уклон, статус, детали из PlaceHolder, идентификаторы арматуры, соединения")]
    [AddInPlugin(AddInLocation.AddIn, Icon = "Images\\Audit16.png", LargeIcon = "Images\\Audit32.png")]
    public sealed class AuditPlugin : AddInPlugin
    {
        static NavisAuditWindow _window;

        public override int Execute(params string[] parameters)
        {
            var document = NavisApp.ActiveDocument;
            if (document == null || document.Models.Count == 0)
            {
                System.Windows.MessageBox.Show("Откройте модель Navisworks.", "Аудит трубопроводов");
                return 0;
            }
            if (_window != null)
            {
                _window.Activate();
                return 0;
            }
            var auditor = new NavisAuditor(document);
            try { ModelView.WithProgress("Чтение модели", report => { auditor.Read(report); return 0; }); }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Не удалось прочитать модель: " + ex.Message, "Аудит трубопроводов");
                return 0;
            }

            var settings = NavisSettings.Load();
            AuditOptions last = null;
            var host = new NavisAuditWindow.Host
            {
                Model = auditor.Scan.Drawing,
                Lines = auditor.Lines(),
                Checks = NavisAuditor.Checks.Select(id => (id, NavisTexts.Title(id), NavisTexts.Describe(id))).ToList(),
                Settings = settings,
                Run = options =>
                {
                    last = options;
                    return ModelView.WithProgress("Проверка", report => auditor.Run(options, report));
                },
                Evaluate = NavisTexts.Evaluate,
                Summary = snapshot => NavisTexts.Summary(auditor.Model, last ?? new AuditOptions()),
                Show = (issues, zoom) => ModelView.Show(auditor, issues, zoom),
                SaveReport = (path, snapshot, options, issues) => AuditDocxReport.Save(path, snapshot, options, issues, NavisTexts.Report(auditor.Model, options)),
                Empty = auditor.Scan.ProxyKeys.Count > 0
                    ? $"В модели нет деталей Plant 3D со свойствами: {auditor.Scan.ProxyKeys.Count} объектов Plant 3D загружены как прокси (NWC сделан без Plant 3D Object Enabler). Переэкспортируйте NWC из Plant 3D."
                    : "В модели не найдено деталей Plant 3D (свойства вкладки «AutoCAD» с классом Plant 3D) — проверять нечего.",
            };
            _window = new NavisAuditWindow(host);
            new WindowInteropHelper(_window).Owner = NavisApp.Gui.MainWindow.Handle;
            System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(_window);
            _window.Closed += (_, __) => _window = null;
            _window.Show();
            return 0;
        }
    }

    // Scripted read-only run (Automation: ExecuteAddInPlugin), the
    // PSAUDITJSON of the Plant 3D plug-in. Parameters: the JSON path, then
    // key=value: lines=all|A;B, checks=collisions,wall,…, tolerance=2,
    // structures=1, parts=1 (also <json>.parts.json), docx=<path>, show=1
    // (selects and zooms to the first remark's elements, as the window does).
    [Plugin("NavisAuditJson", "PSPG", DisplayName = "Аудит трубопроводов (JSON)")]
    [AddInPlugin(AddInLocation.None)]
    public sealed class AuditJsonPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            if (parameters == null || parameters.Length == 0) return 1;
            var output = parameters[0];
            try
            {
                var args = parameters.Skip(1).Select(p => p.Split(new[] { '=' }, 2)).Where(p => p.Length == 2)
                    .ToDictionary(p => p[0].Trim().ToLowerInvariant(), p => p[1].Trim());
                string Arg(string key) => args.TryGetValue(key, out var value) ? value : null;
                var auditor = new NavisAuditor(NavisApp.ActiveDocument);
                var started = DateTime.Now;
                ModelView.WithProgress("Чтение модели", report => { auditor.Read(report); return 0; });
                var read = DateTime.Now - started;
                var options = new AuditOptions();
                foreach (var check in (Arg("checks") ?? string.Join(",", NavisAuditor.Checks)).Split(',')) options.Enabled.Add(check.Trim());
                var lines = Arg("lines");
                if (!string.IsNullOrEmpty(lines) && lines != "all") options.SelectedLines = new HashSet<string>(lines.Split(';'), StringComparer.Ordinal);
                if (Arg("tolerance") != null) options.CollisionToleranceMm = double.Parse(Arg("tolerance"), CultureInfo.InvariantCulture);
                options.StructureCollisions = Arg("structures") == "1";
                var snapshot = ModelView.WithProgress("Проверка", report => auditor.Run(options, report));
                var issues = NavisTexts.Evaluate(snapshot, options);
                var shown = "";
                if (Arg("show") == "1" && issues.FirstOrDefault(i => i.Keys.Count > 0) is AuditIssue first)
                {
                    ModelView.Show(auditor, new[] { first }, true);
                    shown = $", shown {NavisApp.ActiveDocument.CurrentSelection.SelectedItems.Count} of {first.SelectableKeys.Count()} ({string.Join(", ", first.SelectableKeys.Take(3))})";
                }
                File.WriteAllText(output, NavisJsonReport.Serialize(auditor.Model, options, issues), new System.Text.UTF8Encoding(false));
                if (Arg("parts") == "1") File.WriteAllText(output + ".parts.json", NavisJsonReport.Parts(auditor.Model), new System.Text.UTF8Encoding(false));
                if (Arg("docx") != null) AuditDocxReport.Save(Arg("docx"), snapshot, options, issues, NavisTexts.Report(auditor.Model, options));
                // props=<path>: every Plant 3D property name the model carries,
                // with how many objects have it and sample values — to map a
                // check onto the project's (localized) property names.
                if (Arg("props") != null)
                    File.WriteAllLines(Arg("props"), auditor.Scan.Items.SelectMany(i => i.Props.Select(p => (Class: i.ClassName, p.Key, p.Value)))
                        .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(g => $"{g.Key}\t{g.Count()}\t{string.Join(" | ", g.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => $"{p.Class}: {p.Value}").Distinct().Take(6))}"),
                        new System.Text.UTF8Encoding(false));
                File.WriteAllText(output + ".log", $"read {read.TotalSeconds:0.0}s, total {(DateTime.Now - started).TotalSeconds:0.0}s, parts {snapshot.Parts.Count}, issues {issues.Count}{shown}");
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(output + ".error.txt", ex.ToString());
                return 1;
            }
        }
    }

    public static class ModelView
    {
        // The Navisworks progress bar over a stage; its «Cancel» stops the audit.
        public static T WithProgress<T>(string stage, Func<Action<string, double>, T> work)
        {
            var progress = NavisApp.BeginProgress("Аудит трубопроводов", stage);
            try
            {
                return work((text, fraction) =>
                {
                    if (progress.IsCanceled) throw new OperationCanceledException("Аудит прерван пользователем.");
                    progress.Update(Math.Max(0, Math.Min(1, fraction)));
                });
            }
            finally { NavisApp.EndProgress(); }
        }

        // Selects the issues' elements; zoom — and brings them into view.
        public static void Show(NavisAuditor auditor, IReadOnlyList<AuditIssue> issues, bool zoom)
        {
            var document = auditor.Document;
            var items = auditor.Items(issues.SelectMany(i => i.SelectableKeys));
            if (items.Count == 0) return;
            document.CurrentSelection.CopyFrom(items);
            if (!zoom) return;
            try { ComBridge.State.ZoomInCurViewOnCurSel(); }
            catch { document.ActiveView.FocusOnCurrentSelection(); }
        }
    }
}
