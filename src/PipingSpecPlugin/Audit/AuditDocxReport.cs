using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace PipingSpecPlugin.Audit
{
    public static class AuditDocxReport
    {
        public const string TemplateResource = "PipingSpecPlugin.AuditReportTemplate.docx";

        public static void Save(string path, AuditSnapshot snapshot, AuditOptions options, IReadOnlyList<AuditIssue> issues)
        {
            // Finish the document before replacing a previously saved report.
            var fullPath = Path.GetFullPath(path);
            var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var source = typeof(AuditDocxReport).Assembly.GetManifestResourceStream(TemplateResource)
                    ?? throw new InvalidOperationException("Не найден шаблон отчёта аудита."))
                using (var target = File.Create(temporary)) source.CopyTo(target);
                using (var document = WordprocessingDocument.Open(temporary, true))
                {
                    var body = document.MainDocumentPart.Document.Body;
                    var selectedParts = snapshot.Parts.Where(options.Includes).ToList();
                    // Same order as the dialog's line list (natural, no line last);
                    // OrderBy(s => s) sorted by the current culture, «10» before «9».
                    var lines = (options.SelectedLines == null
                        ? selectedParts.Select(p => p.LineKey ?? AuditOptions.NoLine).Distinct(StringComparer.Ordinal)
                        : options.SelectedLines).ToList();
                    lines.Sort(AuditLineOrder.Compare);
                    var rules = AuditRules.All.Where(r => options.Enabled.Contains(r.Id)).ToList();
                    var unknown = issues.Count(i => i.Severity == "Не проверено");
                    string result = selectedParts.Count == 0 ? "На выбранных линиях не найдено деталей. Результат требует проверки исходных данных."
                        : issues.Count == 0 ? "Замечаний по выбранным проверкам не выявлено. Результат относится только к указанному объёму аудита."
                        : $"Зарегистрировано замечаний: {issues.Count}. Из них непроверенных случаев: {unknown}. См. реестр замечаний.";
                    var fields = new Dictionary<string, string>
                    {
                        ["DRAWING"] = snapshot.Drawing ?? "Не указан",
                        ["DATE"] = DateTime.Now.ToString("dd.MM.yyyy HH:mm zzz", CultureInfo.InvariantCulture),
                        ["RESULT"] = result,
                        ["LINES"] = lines.Count == 0 ? "Линии не выбраны." : string.Join("; ", lines.Select(s => s == AuditOptions.NoLine ? AuditOptions.NoLineLabel : s)),
                        ["COUNTS"] = $"Линий: {lines.Count}. Деталей на выбранных линиях: {selectedParts.Count}. Замечаний: {issues.Count}.",
                        ["CHECKS"] = rules.Count == 0 ? "Проверки не выбраны." : string.Join("\n", rules.Select(r => r.Title + ". " + (r.Id == "wall"
                            ? $"Трубы, отводы и изгибы трубы одной линии с одинаковым номинальным диаметром; тройники и переходы — стенка конца не тоньше соединённой трубы. Допуск {options.WallToleranceMm.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU"))} мм."
                            : r.Description))),
                        ["SCOPE"] = "Выбранные линии текущего чертежа. " + (options.Enabled.Contains("collisions")
                            ? $"Коллизии проверяются со всеми объектами и загруженными внешними ссылками. Проверено пар: {snapshot.CollisionPairsChecked}. Пересечения деталей, соединённых через объект соединения, не учитываются (не учтено: {snapshot.ConnectedJointsIgnored}). Вставок внешних ссылок: {snapshot.XrefInstancesChecked}; недоступно: {snapshot.XrefInstancesUnavailable}. Используется полная геометрия без учёта XCLIP. "
                              + (options.CollisionToleranceMm > 0 ? $"Допуск коллизий {options.CollisionToleranceMm.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU"))} мм: пересечения не глубже допуска не учитываются (не учтено: {snapshot.CollisionsWithinTolerance}). " : "") : "")
                            + (options.Enabled.Contains("connections") ? "Соединения через другие DWG не проверяются. Свободные концы могут быть предусмотрены проектом. " : "")
                            + (options.Enabled.Contains("bolts") ? $"Соединений с комплектами болтов в чертеже: {snapshot.Parts.Count(p => p.IsConnector && p.BoltSets.Count > 0)}. " : "")
                            + "Непроверенные случаи не считаются успешной проверкой. Модель при аудите не изменяется."
                    };
                    Replace(body, fields);
                    var summary = new List<string[]>();
                    foreach (var rule in rules)
                        summary.Add(new[] { rule.Title, issues.Count(i => i.RuleId == rule.Id && i.Severity != "Не проверено").ToString(),
                            issues.Count(i => i.RuleId == rule.Id && i.Severity == "Не проверено").ToString() });
                    foreach (var group in issues.Where(i => rules.All(r => r.Id != i.RuleId)).GroupBy(i => i.Check ?? "Чтение данных"))
                        summary.Add(new[] { group.Key, group.Count(i => i.Severity != "Не проверено").ToString(), group.Count(i => i.Severity == "Не проверено").ToString() });
                    FillRows(body, "SUMMARY", summary.Count > 0 ? summary : new List<string[]> { new[] { "Проверки не выбраны", "—", "—" } });
                    // A remark without a line (unavailable xref, foreign object)
                    // is not a part «без номера линии»; parts that really have no
                    // line carry that label in Line already.
                    var detail = issues.Select((issue, n) => new[] { (n + 1).ToString(), string.IsNullOrWhiteSpace(issue.Line) ? "—" : issue.Line,
                        (issue.Check ?? "Чтение данных") + "\n" + issue.Severity,
                        (issue.Message ?? "") + (string.IsNullOrWhiteSpace(issue.Sources) ? "" : "\nИсточник: " + issue.Sources)
                        + (issue.Keys.Count == 0 ? "" : "\nЭлементы: " + string.Join(", ", issue.Keys)) }).ToList();
                    FillRows(body, "ISSUE", detail.Count > 0 ? detail : new List<string[]> { new[] { "—", "—", "—", "Замечаний не зарегистрировано." } });
                    document.MainDocumentPart.Document.Save();
                }
                File.Move(temporary, fullPath, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        static void Replace(OpenXmlElement root, IReadOnlyDictionary<string, string> values)
        {
            foreach (var paragraph in root.Descendants<Paragraph>().ToList())
            {
                string text = paragraph.InnerText;
                foreach (var pair in values) text = text.Replace("{{" + pair.Key + "}}", pair.Value);
                if (text != paragraph.InnerText) SetText(paragraph, text);
            }
        }

        static void FillRows(Body body, string marker, IReadOnlyList<string[]> rows)
        {
            var prototype = body.Descendants<TableRow>().Single(r => r.InnerText.Contains("{{" + marker + "}}"));
            foreach (var values in rows)
            {
                var row = (TableRow)prototype.CloneNode(true);
                if (values.All(v => (v ?? "").Length < 600))
                {
                    row.TableRowProperties ??= new TableRowProperties();
                    row.TableRowProperties.Append(new CantSplit());
                }
                var cells = row.Elements<TableCell>().ToList();
                for (int i = 0; i < cells.Count; i++)
                {
                    // A cell without a paragraph is invalid WordprocessingML anyway.
                    var paragraph = cells[i].Elements<Paragraph>().FirstOrDefault() ?? cells[i].AppendChild(new Paragraph());
                    SetText(paragraph, i < values.Length ? values[i] : "");
                }
                prototype.InsertBeforeSelf(row);
            }
            prototype.Remove();
        }

        static void SetText(Paragraph paragraph, string value)
        {
            if ((value ?? "").Contains('\n'))
            {
                paragraph.ParagraphProperties ??= new ParagraphProperties();
                paragraph.ParagraphProperties.Justification = new Justification { Val = JustificationValues.Left };
            }
            var style = paragraph.Elements<Run>().FirstOrDefault()?.RunProperties?.CloneNode(true);
            foreach (var child in paragraph.ChildElements.Where(e => e is not ParagraphProperties).ToList()) child.Remove();
            var run = new Run();
            if (style != null) run.Append(style);
            // Part descriptions come straight from the DLM; one control
            // character in them (see Spec.XmlText) made Document.Save() throw
            // and the whole report was lost. Line breaks are kept — they
            // become w:br below.
            var lines = Spec.XmlText.Clean(value ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) run.Append(new Break());
                run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
            }
            paragraph.Append(run);
        }
    }
}
