using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Writes a populated spec into a .docx that follows the same layout
    /// the Python edition uses:
    ///   1. find the table whose body contains a single-cell header row
    ///      with text "Изделия и материалы";
    ///   2. find a 9-cell row anywhere in that table and use it as the
    ///      template for new data rows (its style, borders and column
    ///      widths get cloned for every SpecRow we emit);
    ///   3. emit a sub-header per (line, category) group;
    ///   4. emit a data row per SpecRow with the running position number
    ///      and 9 columns filled in.
    /// The implementation deliberately avoids touching anything outside
    /// the materials section so the rest of the template (headers, title
    /// blocks, page setup) survives untouched.
    /// </summary>
    public static class SpecTemplateWriter
    {
        public static void Write(string templatePath, string outputPath,
                                 IList<SpecRow> rows, bool groupByLine = false)
        {
            // Plant 3D users routinely keep templates open in Word while
            // they tweak the document, so we never write back over the
            // input — always copy first.
            File.Copy(templatePath, outputPath, overwrite: true);

            using var doc = WordprocessingDocument.Open(outputPath, isEditable: true);
            var body = doc.MainDocumentPart.Document.Body;

            var (table, headerIdx) = FindMaterialsTable(body)
                ?? throw new InvalidOperationException(
                    $"Section header '{SpecConstants.MaterialsSectionHeader}' "
                  + "not found in any table of the template.");

            var trs = table.Elements<TableRow>().ToList();
            // The slice reserved for data sits between the header and the
            // next single-cell header row (or end of table). We'll fill
            // it first and append new rows when it runs out.
            var reservedStart = headerIdx + 1;
            var reservedEnd = trs.Count;
            for (var i = reservedStart; i < trs.Count; i++)
            {
                if (IsHeaderRow(trs[i])) { reservedEnd = i; break; }
            }
            var reserved = Enumerable.Range(reservedStart, reservedEnd - reservedStart).ToList();

            // Pick a 9-cell row anywhere in the table as the data-row
            // stencil. Anywhere — not just below the header — because
            // some templates have zero reserved rows and the only 9-cell
            // example lives elsewhere.
            var stencil = trs.FirstOrDefault(r => r.Elements<TableCell>().Count() == 9
                                                  && !IsHeaderRow(r))
                ?? throw new InvalidOperationException(
                    "Template has no 9-cell row to use as a data-row stencil.");

            var groups = GroupRows(rows, groupByLine);
            var pos = 1;
            var cursor = 0;
            var inserted = new List<TableRow>();

            foreach (var (lineKey, byCat) in groups)
            {
                if (groupByLine)
                {
                    var lineLabel = lineKey == SpecConstants.NullLine
                        ? SpecConstants.NullLineLabel
                        : $"Линия {lineKey}";
                    var row = NextRow(table, reserved, ref cursor, stencil);
                    WriteSubHeader(row, lineLabel);
                    inserted.Add(row);
                }

                foreach (var (cat, items) in byCat)
                {
                    SpecConstants.CategoryLabelRu.TryGetValue(cat, out var catLabel);
                    if (string.IsNullOrEmpty(catLabel)) catLabel = cat;
                    var hdr = NextRow(table, reserved, ref cursor, stencil);
                    WriteSubHeader(hdr, catLabel);
                    inserted.Add(hdr);

                    foreach (var sr in items)
                    {
                        var dr = NextRow(table, reserved, ref cursor, stencil);
                        WriteDataRow(dr, pos++, sr);
                        inserted.Add(dr);
                    }
                }
            }

            // Drop reserved rows we didn't consume (would otherwise stay
            // blank in the output).
            for (var i = cursor; i < reserved.Count; i++)
            {
                var idx = reserved[i];
                // Need fresh row reference since insertions above may have
                // shifted indices in the live element list.
                var tr = table.Elements<TableRow>().ElementAtOrDefault(idx);
                tr?.Remove();
            }

            doc.MainDocumentPart.Document.Save();
        }

        // --- table walk --------------------------------------------------

        static (Table table, int headerIdx)? FindMaterialsTable(Body body)
        {
            var target = SpecConstants.MaterialsSectionHeader
                .Trim().ToLowerInvariant();
            foreach (var t in body.Descendants<Table>())
            {
                var trs = t.Elements<TableRow>().ToList();
                for (var i = 0; i < trs.Count; i++)
                {
                    if (!IsHeaderRow(trs[i])) continue;
                    var txt = RowText(trs[i]).Trim().ToLowerInvariant()
                        .TrimEnd(':', '.', ' ');
                    if (txt == target) return (t, i);
                }
            }
            return null;
        }

        static bool IsHeaderRow(TableRow row)
            => row.Elements<TableCell>().Count() == 1;

        static string RowText(TableRow row)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var t in row.Descendants<Text>()) sb.Append(t.Text);
            return sb.ToString();
        }

        // --- row insertion -----------------------------------------------

        /// <summary>
        /// Returns the next row to fill. If the reserved slice still has
        /// blank rows we reuse them in-place (preserves layout exactly);
        /// otherwise we clone the stencil and append it after the last
        /// inserted row so column widths and borders carry over.
        /// </summary>
        static TableRow NextRow(Table table, List<int> reserved, ref int cursor, TableRow stencil)
        {
            if (cursor < reserved.Count)
            {
                var idx = reserved[cursor++];
                var tr = table.Elements<TableRow>().ElementAt(idx);
                ClearRowText(tr);
                return tr;
            }
            cursor++;
            var clone = (TableRow)stencil.CloneNode(deep: true);
            ClearRowText(clone);
            table.AppendChild(clone);
            return clone;
        }

        static void ClearRowText(TableRow row)
        {
            foreach (var c in row.Elements<TableCell>())
            {
                // Wipe every paragraph's text runs but keep paragraph
                // properties so styling (alignment, font) survives.
                foreach (var p in c.Elements<Paragraph>())
                {
                    var keepProps = p.Elements<ParagraphProperties>().FirstOrDefault();
                    p.RemoveAllChildren();
                    if (keepProps != null) p.AppendChild(keepProps);
                }
                if (!c.Elements<Paragraph>().Any())
                    c.AppendChild(new Paragraph());
            }
        }

        // --- cell writers ------------------------------------------------

        static void WriteSubHeader(TableRow row, string text)
        {
            // Section headers in this template are single-merged cells.
            // If the row already has 9 separate cells (because we cloned
            // a data-row stencil), merge them on the fly by writing into
            // the first cell and emptying the rest. We can't fully merge
            // cells without rewriting tcPr — leaving them empty matches
            // what the Python edition does and Word renders it acceptably.
            var cells = row.Elements<TableCell>().ToList();
            if (cells.Count == 0) return;
            SetCellText(cells[0], text, bold: true);
            for (var i = 1; i < cells.Count; i++)
                SetCellText(cells[i], "", bold: false);
        }

        static void WriteDataRow(TableRow row, int pos, SpecRow r)
        {
            var cells = row.Elements<TableCell>().ToList();
            if (cells.Count < 9) return;
            // Columns (from the template):
            //  0 Поз.  1 Наименование  2 Тип/марка  3 Код  4 Поставщик
            //  5 Ед. изм.  6 Кол.  7 Масса 1 ед., кг  8 Примечание
            SetCellText(cells[0], pos.ToString());
            SetCellText(cells[1], r.Name ?? "");
            SetCellText(cells[2], r.TypeMark ?? "");
            SetCellText(cells[3], r.ItemCode ?? "");
            SetCellText(cells[4], r.Manufacturer ?? "");
            SetCellText(cells[5], r.Unit ?? "");
            SetCellText(cells[6], FormatNumber(r.Qty, r.Unit == "м" ? 1 : 0));
            SetCellText(cells[7], r.Weight > 0 ? FormatNumber(r.Weight, 3) : "");
            SetCellText(cells[8], r.Note ?? "");
        }

        static void SetCellText(TableCell cell, string text, bool bold = false)
        {
            // Drop any existing paragraphs (we keep the first one's pPr
            // through ClearRowText so style is preserved) and put one
            // paragraph with a single run holding the text.
            var existing = cell.Elements<Paragraph>().FirstOrDefault();
            ParagraphProperties pPr = null;
            if (existing != null)
            {
                pPr = existing.Elements<ParagraphProperties>().FirstOrDefault();
                pPr = pPr != null ? (ParagraphProperties)pPr.CloneNode(true) : null;
                existing.Remove();
            }
            var p = new Paragraph();
            if (pPr != null) p.AppendChild(pPr);
            var run = new Run();
            if (bold) run.AppendChild(new RunProperties(new Bold()));
            run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            p.AppendChild(run);
            cell.AppendChild(p);
        }

        static string FormatNumber(double v, int decimals)
            => v.ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture);

        // --- grouping ----------------------------------------------------

        /// <summary>
        /// Produces line → category → [SpecRow] in the same order the
        /// Python edition uses: lines sorted alphabetically with the
        /// untagged bucket last; inside each line, categories in
        /// CategoryOrder followed by anything else, with "Прочее" last.
        /// </summary>
        static List<(string lineKey, List<(string cat, List<SpecRow> items)> byCat)>
            GroupRows(IList<SpecRow> rows, bool groupByLine)
        {
            var byLine = new Dictionary<string, Dictionary<string, List<SpecRow>>>();
            foreach (var r in rows)
            {
                var lineKey = groupByLine ? (r.LineTag ?? "") : "";
                if (!byLine.TryGetValue(lineKey, out var inner))
                    byLine[lineKey] = inner = new Dictionary<string, List<SpecRow>>();
                var catBucket = !string.IsNullOrEmpty(r.Category)
                                && r.Category != SpecConstants.NullCategory
                    ? r.Category
                    : SpecConstants.OtherCategoryLabel;
                if (!inner.TryGetValue(catBucket, out var list))
                    inner[catBucket] = list = new List<SpecRow>();
                list.Add(r);
            }

            var lineOrder = byLine.Keys
                .OrderBy(t => t == SpecConstants.NullLine ? 1 : 0)
                .ThenBy(t => t, StringComparer.Ordinal)
                .ToList();

            var result = new List<(string, List<(string, List<SpecRow>)>)>();
            foreach (var lk in lineOrder)
            {
                var byCat = byLine[lk];
                var ordered = SpecConstants.CategoryOrder
                    .Where(c => byCat.ContainsKey(c))
                    .ToList();
                foreach (var c in byCat.Keys)
                    if (!ordered.Contains(c) && c != SpecConstants.OtherCategoryLabel)
                        ordered.Add(c);
                if (byCat.ContainsKey(SpecConstants.OtherCategoryLabel))
                    ordered.Add(SpecConstants.OtherCategoryLabel);
                result.Add((lk, ordered.Select(c => (c, byCat[c])).ToList()));
            }
            return result;
        }
    }
}
