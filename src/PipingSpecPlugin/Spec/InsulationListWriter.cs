using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// INSULSPEC document — no template: the line number, under it a numbered
    /// list of its elements («1) Труба 25х4 ГОСТ … — 1.4 м»,
    /// «2) ИЗГИБ ТРУБЫ 25х4, Р = 90, У = 90 град, ДЛИНА = 141.4 — 2 шт.»), then
    /// «Позиции, требующие проверки». Rows come from SpecRowBuilder grouped by
    /// line, in its order. Written through SpecTemplateWriter.WriteDocument
    /// (temp copy → atomic replace, "file open in Word" detection).
    /// </summary>
    public static class InsulationListWriter
    {
        public const string DocumentTitle = "Исходные данные для расчёта тепловой изоляции трубопроводов";

        public static void Write(string outputPath, string subtitle, IList<SpecRow> rows, IList<SpecRow> reviewRows = null)
        {
            var blank = Path.Combine(Path.GetTempPath(), $"insulation-{Guid.NewGuid():N}.docx");
            try
            {
                CreateBlankDocument(blank);
                SpecTemplateWriter.WriteDocument(blank, outputPath, body => Fill(body, subtitle, rows, reviewRows));
            }
            finally
            {
                try { if (File.Exists(blank)) File.Delete(blank); } catch { }
            }
        }

        static void Fill(Body body, string subtitle, IList<SpecRow> rows, IList<SpecRow> reviewRows)
        {
            var section = body.Elements<SectionProperties>().LastOrDefault();
            void Add(Paragraph p)
            {
                if (section != null) body.InsertBefore(p, section);
                else body.AppendChild(p);
            }

            Add(Para(DocumentTitle, bold: true, size: 28, center: true, after: 120));
            if (!string.IsNullOrWhiteSpace(subtitle)) Add(Para(subtitle, center: true, after: 240));

            foreach (var group in GroupByLine(rows ?? Array.Empty<SpecRow>()))
            {
                Add(Para(LineHeading(group.Key), bold: true, before: 240, after: 60));
                var n = 1;
                foreach (var r in group.Value)
                    Add(Para($"{n++}) {ItemText(r)}", indent: 360));
            }
            if (reviewRows != null && reviewRows.Count > 0)
            {
                Add(Para(SpecConstants.ReviewSectionHeader, bold: true, before: 360, after: 60));
                foreach (var r in reviewRows)
                    Add(Para("– " + string.Join(" — ", new[] { r.Name, r.Note }.Where(s => !string.IsNullOrWhiteSpace(s))), indent: 360));
            }
        }

        /// <summary>Line → rows, lines in first-appearance (= SpecRowBuilder) order.</summary>
        internal static List<KeyValuePair<string, List<SpecRow>>> GroupByLine(IEnumerable<SpecRow> rows)
        {
            var result = new List<KeyValuePair<string, List<SpecRow>>>();
            var index = new Dictionary<string, List<SpecRow>>(StringComparer.Ordinal);
            foreach (var r in rows)
            {
                var key = string.IsNullOrEmpty(r.LineTag) ? SpecConstants.NullLine : r.LineTag;
                if (!index.TryGetValue(key, out var list))
                {
                    index[key] = list = new List<SpecRow>();
                    result.Add(new KeyValuePair<string, List<SpecRow>>(key, list));
                }
                list.Add(r);
            }
            return result;
        }

        internal static string LineHeading(string lineKey)
            => lineKey == SpecConstants.NullLine ? SpecConstants.NullLineLabel : "Линия " + lineKey;

        /// <summary>«Труба 25х4 … — 1.4 м» / «Отвод 90 … — 3 шт.» — the spec's quantity and unit.</summary>
        internal static string ItemText(SpecRow r)
        {
            var qty = r.Unit == "м"
                ? r.Qty.ToString("F1", CultureInfo.InvariantCulture)
                : r.Qty.ToString("0.###", CultureInfo.InvariantCulture);
            return $"{r.Name} — {qty} {r.Unit}".TrimEnd();
        }

        static Paragraph Para(string text, bool bold = false, int size = 0, bool center = false,
                              int before = 0, int after = 0, int indent = 0)
        {
            var pPr = new ParagraphProperties();
            if (before > 0 || after > 0)
                pPr.SpacingBetweenLines = new SpacingBetweenLines
                {
                    Before = before.ToString(CultureInfo.InvariantCulture),
                    After = after.ToString(CultureInfo.InvariantCulture),
                };
            if (indent > 0) pPr.Indentation = new Indentation { Left = indent.ToString(CultureInfo.InvariantCulture) };
            if (center) pPr.Justification = new Justification { Val = JustificationValues.Center };
            var rPr = new RunProperties();
            if (bold) rPr.Bold = new Bold();
            if (size > 0)
            {
                rPr.FontSize = new FontSize { Val = size.ToString(CultureInfo.InvariantCulture) };
                rPr.FontSizeComplexScript = new FontSizeComplexScript { Val = size.ToString(CultureInfo.InvariantCulture) };
            }
            var run = new Run();
            if (rPr.HasChildren) run.AppendChild(rPr);
            run.AppendChild(new Text(XmlText.SingleLine(text)) { Space = SpaceProcessingModeValues.Preserve });
            return new Paragraph(pPr, run);
        }

        /// <summary>Empty A4 portrait document, Arial 11 pt.</summary>
        internal static void CreateBlankDocument(string path)
        {
            using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
            var main = doc.AddMainDocumentPart();
            var styles = main.AddNewPart<StyleDefinitionsPart>();
            styles.Styles = new Styles(new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = "Arial", HighAnsi = "Arial", ComplexScript = "Arial", EastAsia = "Arial" },
                    new FontSize { Val = "22" },
                    new FontSizeComplexScript { Val = "22" },
                    new Languages { Val = "ru-RU" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                    new SpacingBetweenLines { After = "40", Line = "264", LineRule = LineSpacingRuleValues.Auto }))));
            main.Document = new Document(new Body(
                new SectionProperties(
                    new PageSize { Width = 11906U, Height = 16838U },
                    new PageMargin { Top = 851, Right = 567U, Bottom = 851, Left = 1134U, Header = 425U, Footer = 425U, Gutter = 0U })));
            main.Document.Save();
        }
    }
}
