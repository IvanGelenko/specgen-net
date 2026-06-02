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
    /// Writes a populated spec into a .docx that follows this layout:
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
            // A valid .docx always has a main document part with a body. If
            // the template is corrupt or not really a Word document, these
            // are null — surface a clear message instead of a bare
            // NullReferenceException the user can't act on.
            var body = doc.MainDocumentPart?.Document?.Body
                ?? throw new InvalidOperationException(
                    "Файл шаблона повреждён или не является корректным документом Word (.docx): "
                  + "в нём отсутствует основная часть документа.");

            var (table, headerIdx) = FindMaterialsTable(body)
                ?? throw new InvalidOperationException(
                    $"В шаблоне не найдена таблица спецификации. Убедитесь, что в шаблоне присутствует таблица, "
                  + $"содержащая строку с заголовком '{SpecConstants.MaterialsSectionHeader}'.");

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
            var reserved = new List<TableRow>();
            for (var i = reservedStart; i < reservedEnd; i++)
            {
                reserved.Add(trs[i]);
            }

            // Pick a 9-cell row anywhere in the table as the data-row
            // stencil. Anywhere — not just below the header — because
            // some templates have zero reserved rows and the only 9-cell
            // example lives elsewhere.
            var stencilSrc = trs.FirstOrDefault(r => r.Elements<TableCell>().Count() == 9
                                                     && !IsHeaderRow(r));
            if (stencilSrc == null)
            {
                var anyDataRow = trs.FirstOrDefault(r => r.Elements<TableCell>().Count() > 1
                                                         && !IsHeaderRow(r));
                if (anyDataRow != null)
                {
                    var count = anyDataRow.Elements<TableCell>().Count();
                    throw new InvalidOperationException(
                        $"Таблица материалов в шаблоне имеет некорректное количество колонок ({count} вместо требуемых 9). "
                      + "Пожалуйста, убедитесь, что таблица соответствует стандарту ГОСТ (9 колонок).");
                }
                throw new InvalidOperationException(
                    "В таблице материалов шаблона не найдено строк-образцов для вывода данных спецификации.");
            }
            // Take an isolated copy and strip vertical text + fixed-height
            // properties before using it as the source of new rows. Some
            // templates (including the ГОСТ one) keep a single tall row
            // with btLr text direction in column 5 as the master sample;
            // cloning that verbatim makes every data row tall and turns
            // "шт." sideways. We don't touch the original stencil row in
            // the template body, only this working copy.
            var stencil = (TableRow)stencilSrc.CloneNode(deep: true);
            NormalizeStencilCells(stencil);
            NormalizeStencilHeight(stencil);

            // Capture the per-column styling (font, size, paragraph
            // alignment, ...) from the master stencil row *before* the
            // build loop starts writing into clones. The reference cells
            // remember the original template formatting; every data row
            // we write will inherit pPr+rPr from these so the output
            // looks identical to the template's first data row regardless
            // of how many rows we append.
            var styleRef = (TableRow)stencilSrc.CloneNode(deep: true);
            NormalizeStencilCells(styleRef);
            NormalizeStencilHeight(styleRef);
            var styleCells = styleRef.Elements<TableCell>().ToList();

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
                    WriteSubHeader(row, lineLabel, styleCells);
                    inserted.Add(row);
                }

                foreach (var (cat, items) in byCat)
                {
                    SpecConstants.CategoryLabelRu.TryGetValue(cat, out var catLabel);
                    if (string.IsNullOrEmpty(catLabel)) catLabel = cat;
                    var hdr = NextRow(table, reserved, ref cursor, stencil);
                    WriteSubHeader(hdr, catLabel, styleCells);
                    inserted.Add(hdr);

                    foreach (var sr in items)
                    {
                        var dr = NextRow(table, reserved, ref cursor, stencil);
                        WriteDataRow(dr, pos++, sr, styleCells);
                        inserted.Add(dr);
                    }
                }
            }

            // Drop reserved rows we didn't consume (would otherwise stay
            // blank in the output).
            for (var i = cursor; i < reserved.Count; i++)
            {
                var tr = reserved[i];
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
                    // Match when the header equals the target, or CONTAINS it
                    // (templates prefix a section number, e.g. "2 Изделия и
                    // материалы"). We deliberately do NOT match the reverse
                    // (target contains header): a short unrelated header that
                    // happens to be a substring of "изделия и материалы"
                    // (e.g. a stray "Изделия" cell) would otherwise hijack
                    // the fill and write the spec into the wrong table.
                    if (txt == target || txt.Contains(target)) return (t, i);
                }
            }
            return null;
        }

        static bool IsHeaderRow(TableRow row)
            => row.Elements<TableCell>().Count() == 1;

        /// <summary>
        /// Strip stencil-only properties that would otherwise propagate
        /// to every data row: vertical text direction (cell-level) and
        /// any vertical merge marker. Borders, shading, widths and
        /// alignment stay — those define the table's look.
        /// </summary>
        static void NormalizeStencilCells(TableRow row)
        {
            foreach (var c in row.Elements<TableCell>())
            {
                var tcPr = c.Elements<TableCellProperties>().FirstOrDefault();
                if (tcPr == null) continue;
                foreach (var td in tcPr.Elements<TextDirection>().ToList()) td.Remove();
                foreach (var vm in tcPr.Elements<VerticalMerge>().ToList()) vm.Remove();
                foreach (var hm in tcPr.Elements<HorizontalMerge>().ToList()) hm.Remove();
            }
        }

        /// <summary>
        /// Drop any fixed row-height the stencil came with so each data
        /// row auto-sizes to its content. Plant 3D's ГОСТ template keeps
        /// the master row tall (about 1400 twentieths-of-a-point) for
        /// vertical text — that's a stencil artefact, not a layout rule.
        /// </summary>
        static void NormalizeStencilHeight(TableRow row)
        {
            var trPr = row.Elements<TableRowProperties>().FirstOrDefault();
            if (trPr == null) return;
            foreach (var h in trPr.Elements<TableRowHeight>().ToList()) h.Remove();
        }

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
        static TableRow NextRow(Table table, List<TableRow> reserved, ref int cursor, TableRow stencil)
        {
            if (cursor < reserved.Count)
            {
                var tr = reserved[cursor++];
                // Reserved rows in the ГОСТ template inherit the same
                // vertical-text / tall-row formatting as the stencil; we
                // want data behaviour on them too.
                NormalizeStencilCells(tr);
                NormalizeStencilHeight(tr);
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

        static void WriteSubHeader(TableRow row, string text,
                                    List<TableCell> styleCells = null)
        {
            // Section headers span all 9 columns. Word's horizontal merge
            // works by giving the first cell HorizontalMergeValues.Restart
            // and every subsequent cell HorizontalMergeValues.Continue —
            // the cells stay in the XML (so column widths are preserved)
            // but render as one. We must also strip any pre-existing
            // hMerge from the stencil before applying our own so cloned
            // data-row cells (which have no merge) don't accidentally
            // inherit one from a previous run.
            var cells = row.Elements<TableCell>().ToList();
            if (cells.Count == 0) return;

            for (var i = 0; i < cells.Count; i++)
            {
                var tcPr = cells[i].Elements<TableCellProperties>().FirstOrDefault();
                if (tcPr == null)
                {
                    tcPr = new TableCellProperties();
                    cells[i].InsertAt(tcPr, 0);
                }
                var existing = tcPr.Elements<HorizontalMerge>().ToList();
                foreach (var hm in existing) hm.Remove();
                tcPr.AppendChild(new HorizontalMerge
                {
                    Val = i == 0
                        ? MergedCellValues.Restart
                        : MergedCellValues.Continue,
                });
            }
            SetCellText(cells[0], text, bold: true,
                        styleCell: styleCells != null && styleCells.Count > 0 ? styleCells[0] : null);
            for (var i = 1; i < cells.Count; i++)
                SetCellText(cells[i], "", bold: false,
                            styleCell: styleCells != null && i < styleCells.Count ? styleCells[i] : null);
        }

        static void WriteDataRow(TableRow row, int pos, SpecRow r,
                                  List<TableCell> styleCells = null)
        {
            var cells = row.Elements<TableCell>().ToList();
            if (cells.Count < 9) return;
            // Cells cloned from the stencil might carry a residual hMerge
            // from a previous header insertion — strip it so data rows
            // always render as 9 separate cells.
            foreach (var c in cells)
            {
                var tcPr = c.Elements<TableCellProperties>().FirstOrDefault();
                if (tcPr == null) continue;
                foreach (var hm in tcPr.Elements<HorizontalMerge>().ToList())
                    hm.Remove();
            }
            TableCell Style(int i) => styleCells != null && i < styleCells.Count
                ? styleCells[i] : null;
            // Columns (from the template):
            //  0 Поз.  1 Наименование  2 Тип/марка  3 Код  4 Поставщик
            //  5 Ед. изм.  6 Кол.  7 Масса 1 ед., кг  8 Примечание
            SetCellText(cells[0], pos.ToString(), styleCell: Style(0));
            SetCellText(cells[1], r.Name ?? "", styleCell: Style(1));
            SetCellText(cells[2], r.TypeMark ?? "", styleCell: Style(2));
            SetCellText(cells[3], r.ItemCode ?? "", styleCell: Style(3));
            SetCellText(cells[4], r.Manufacturer ?? "", styleCell: Style(4));
            SetCellText(cells[5], r.Unit ?? "", styleCell: Style(5));
            SetCellText(cells[6], FormatNumber(r.Qty, r.Unit == "м" ? 1 : 0), styleCell: Style(6));
            SetCellText(cells[7], r.Weight > 0 ? FormatNumber(r.Weight, 3) : "", styleCell: Style(7));
            SetCellText(cells[8], r.Note ?? "", styleCell: Style(8));
        }

        /// <summary>
        /// Replace the cell's contents with one paragraph/run containing
        /// <paramref name="text"/>. Paragraph + run formatting (font,
        /// size, alignment, color) is copied from <paramref name="styleCell"/>
        /// when provided — that's the master stencil cell, so every
        /// output row inherits the same look as the template's first
        /// data row. Falls back to the cell's own existing formatting
        /// when no styleCell is supplied.
        /// </summary>
        static void SetCellText(TableCell cell, string text,
                                 bool bold = false, TableCell styleCell = null)
        {
            ParagraphProperties pPr = null;
            RunProperties originalRPr = null;

            // Prefer the master styleCell as the source of formatting —
            // it stays untouched throughout the build, so every row
            // gets identical font/size/alignment. The cell's own
            // formatting is a fallback for headers that don't pass a
            // styleCell or for templates where styleCells weren't
            // captured.
            var source = styleCell?.Elements<Paragraph>().FirstOrDefault()
                      ?? cell.Elements<Paragraph>().FirstOrDefault();
            if (source != null)
            {
                pPr = source.Elements<ParagraphProperties>().FirstOrDefault();
                pPr = pPr != null ? (ParagraphProperties)pPr.CloneNode(true) : null;
                var firstRun = source.Elements<Run>().FirstOrDefault();
                if (firstRun != null)
                {
                    var rPr = firstRun.Elements<RunProperties>().FirstOrDefault();
                    originalRPr = rPr != null ? (RunProperties)rPr.CloneNode(true) : null;
                }
            }

            foreach (var p2 in cell.Elements<Paragraph>().ToList()) p2.Remove();

            // Paragraph-level rPr inside pPr also propagates bold. Strip
            // it for data rows so only the run-level styling decides.
            if (pPr != null && !bold)
            {
                foreach (var pprRPr in pPr.Elements<ParagraphMarkRunProperties>().ToList())
                {
                    foreach (var b in pprRPr.Elements<Bold>().ToList()) b.Remove();
                    foreach (var bcs in pprRPr.Elements<BoldComplexScript>().ToList()) bcs.Remove();
                }
            }

            var p = new Paragraph();
            if (pPr != null) p.AppendChild(pPr);

            var run = new Run();
            if (originalRPr != null)
            {
                // The stencil row may have been bold in the template
                // (some ГОСТ templates start the data area with a bold
                // header). For data rows we must explicitly normalise:
                // bold=false ⇒ strip any inherited <w:b/> so only the
                // category sub-headers render bold.
                run.AppendChild(originalRPr);
                if (bold)
                {
                    if (!originalRPr.Elements<Bold>().Any())
                        originalRPr.AppendChild(new Bold());
                }
                else
                {
                    foreach (var b in originalRPr.Elements<Bold>().ToList())
                        b.Remove();
                    foreach (var bcs in originalRPr.Elements<BoldComplexScript>().ToList())
                        bcs.Remove();
                }
            }
            else if (bold)
            {
                run.AppendChild(new RunProperties(new Bold()));
            }

            run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            p.AppendChild(run);
            cell.AppendChild(p);
        }

        static string FormatNumber(double v, int decimals)
            => v.ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture);

        // --- grouping ----------------------------------------------------

        /// <summary>
        /// Produces line → category → [SpecRow] in display order: lines
        /// sorted alphabetically with the untagged bucket last; inside each
        /// line, categories in CategoryOrder followed by anything else,
        /// with "Прочее" last.
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
