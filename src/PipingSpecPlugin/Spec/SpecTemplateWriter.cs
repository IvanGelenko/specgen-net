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
    /// A file the writer needs is held open by another process (typically
    /// Word). <see cref="IsTemplate"/> tells WHICH one, so the message can
    /// name the right document: the template being open blocks the copy,
    /// the output being open blocks the final replace.
    /// </summary>
    public sealed class SpecFileLockedException : IOException
    {
        public string FilePath { get; }
        public bool IsTemplate { get; }
        public SpecFileLockedException(string filePath, bool isTemplate, Exception inner)
            : base((isTemplate ? "Файл шаблона занят другим процессом: " : "Выходной файл занят другим процессом: ")
                   + filePath, inner)
        {
            FilePath = filePath;
            IsTemplate = isTemplate;
        }
    }

    /// <summary>
    /// Writes a populated spec into a .docx that follows this layout:
    ///   1. find the table whose body contains a single-cell header row
    ///      with text "Изделия и материалы";
    ///   2. take the template's own data rows under that header (the
    ///      "reserved" slice) as the stencil for new rows — borders, row
    ///      properties and column widths are cloned from the LAST reserved
    ///      row, paragraph/run formatting per column from the FIRST one.
    ///      Only a template without reserved rows falls back to any 9-cell
    ///      row of the table (usually the column-heading row);
    ///   3. emit a sub-header per (line, category) group;
    ///   4. emit a data row per SpecRow with the running position number
    ///      and 9 columns filled in.
    /// The implementation deliberately avoids touching anything outside
    /// the materials section so the rest of the template (headers, title
    /// blocks, page setup) survives untouched. Steps 2 and 4 live in
    /// <see cref="TableFiller"/>; <see cref="WriteDocument"/> (copy → fill →
    /// atomic replace, lock detection) is shared with InsulationListWriter.
    /// </summary>
    public static class SpecTemplateWriter
    {
        /// <param name="reviewRows">
        /// Rows of the trailing «Позиции, требующие проверки» section
        /// (ReviewSection.BuildRows). Written AFTER every regular section,
        /// without position numbers; Qty/Weight of 0 mean "unknown" and
        /// render blank. null/empty → the section is not emitted at all.
        /// </param>
        public static void Write(string templatePath, string outputPath,
                                 IList<SpecRow> rows, bool groupByLine = false,
                                 IList<SpecRow> reviewRows = null)
        {
            foreach (var row in rows)
            {
                var decimals = row.Unit == "м" ? 1 : 0;
                if (!double.IsFinite(row.Qty) || row.Qty <= 0 ||
                    Math.Abs(Math.Round(row.Qty, decimals) - row.Qty) > 1e-9 ||
                    !double.IsFinite(row.Weight) || row.Weight < 0)
                    throw new InvalidOperationException($"Некорректное количество или масса в строке «{row.Name}».");
            }
            // Review rows are informational: quantity may be unknown (0), but
            // a NaN/negative value is still a bug upstream, not "unknown".
            foreach (var row in reviewRows ?? Array.Empty<SpecRow>())
                if (!double.IsFinite(row.Qty) || row.Qty < 0 ||
                    !double.IsFinite(row.Weight) || row.Weight < 0)
                    throw new InvalidOperationException($"Некорректное количество или масса в позиции для проверки «{row.Name}».");
            WriteDocument(templatePath, outputPath, body => FillSpec(body, rows, groupByLine, reviewRows));
        }

        /// <summary>
        /// Copy the template next to the output, let <paramref name="fill"/>
        /// populate the copy's body, then move it over the output in one
        /// step. The template itself is never modified. Shared by every
        /// document the plug-in writes from a user template.
        /// </summary>
        internal static void WriteDocument(string templatePath, string outputPath, Action<Body> fill)
        {
            var template = Path.GetFullPath(templatePath);
            var output = Path.GetFullPath(outputPath);
            if (string.Equals(template, output, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Шаблон и выходной документ должны быть разными файлами.");
            var temporary = Path.Combine(Path.GetDirectoryName(output), $".{Guid.NewGuid():N}.docx");
            try
            {
                try { File.Copy(template, temporary); }
                catch (IOException ex) when (IsSharingViolation(ex))
                {
                    // Word keeps an open .docx deny-write, so copying the
                    // TEMPLATE fails too — that used to surface as "close the
                    // OUTPUT file", pointing the user at the wrong document.
                    throw new SpecFileLockedException(template, isTemplate: true, ex);
                }
                // File.Copy carries the source attributes over. A read-only
                // template (shared network folder, file from an archive) gave
                // a read-only temporary → UnauthorizedAccessException in
                // Populate and again in the cleanup below.
                ClearReadOnly(temporary);
                Populate(temporary, fill);
                try { File.Move(temporary, output, overwrite: true); }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException)
                                           && IsHeldOpenByAnotherProcess(output))
                {
                    // Replacing a file another process holds open (Word opens
                    // without FILE_SHARE_DELETE) fails with ACCESS_DENIED →
                    // UnauthorizedAccessException, not a sharing-violation
                    // IOException — so the caller's "close it in Word" branch
                    // never fired. Probe the file to tell a lock from a real
                    // permission / read-only problem.
                    throw new SpecFileLockedException(output, isTemplate: false, ex);
                }
            }
            finally
            {
                // Cleanup must never replace the real exception (a throwing
                // finally discards the one in flight); a leftover hidden
                // ".<guid>.docx" is harmless by comparison.
                try
                {
                    if (File.Exists(temporary))
                    {
                        ClearReadOnly(temporary);
                        File.Delete(temporary);
                    }
                }
                catch { }
            }
        }

        static void ClearReadOnly(string path)
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        }

        static bool IsHeldOpenByAnotherProcess(string path)
        {
            if (!File.Exists(path)) return false;
            try
            {
                using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return false;
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return false; }   // ACL, not a lock
        }

        // 32 = ERROR_SHARING_VIOLATION, 33 = ERROR_LOCK_VIOLATION.
        internal static bool IsSharingViolation(IOException ex)
        {
            var code = ex.HResult & 0xFFFF;
            return code == 32 || code == 33;
        }

        static void Populate(string outputPath, Action<Body> fill)
        {
            using var stream = new FileStream(outputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var doc = WordprocessingDocument.Open(stream, isEditable: true);
            // A valid .docx always has a main document part with a body. If
            // the template is corrupt or not really a Word document, these
            // are null — surface a clear message instead of a bare
            // NullReferenceException the user can't act on.
            var body = doc.MainDocumentPart?.Document?.Body
                ?? throw new InvalidOperationException(
                    "Файл шаблона повреждён или не является корректным документом Word (.docx): "
                  + "в нём отсутствует основная часть документа.");
            fill(body);
            doc.MainDocumentPart.Document.Save();
        }

        static void FillSpec(Body body, IList<SpecRow> rows, bool groupByLine, IList<SpecRow> reviewRows)
        {
            var (table, headerIdx) = FindMaterialsTable(body)
                ?? throw new InvalidOperationException(
                    $"В шаблоне не найдена таблица спецификации. Убедитесь, что в шаблоне присутствует таблица, "
                  + $"содержащая строку с заголовком '{SpecConstants.MaterialsSectionHeader}'.");

            var filler = new TableFiller(table, headerIdx);
            var pos = 1;
            foreach (var (lineKey, byCat) in GroupRows(rows, groupByLine))
            {
                if (groupByLine)
                    filler.SubHeader(lineKey == SpecConstants.NullLine
                        ? SpecConstants.NullLineLabel
                        : $"Линия {lineKey}");

                foreach (var (cat, items) in byCat)
                {
                    SpecConstants.CategoryLabelRu.TryGetValue(cat, out var catLabel);
                    filler.SubHeader(string.IsNullOrEmpty(catLabel) ? cat : catLabel);
                    foreach (var sr in items)
                        filler.DataRow(SpecRowCells(pos++, sr));
                }
            }

            // Trailing review section — always last, same sub-header style
            // as the category headers. No position numbers: these are not
            // procurement items, and numbering them would make them read as
            // part of the spec (and of any count/total taken from it).
            if (reviewRows != null && reviewRows.Count > 0)
            {
                filler.SubHeader(SpecConstants.ReviewSectionHeader);
                foreach (var sr in reviewRows)
                    filler.DataRow(SpecRowCells(null, sr));
            }
            filler.Finish();
        }

        /// <summary>
        /// The 9 cells of a spec / review row. pos == null → review-section
        /// row: no position number, and a zero Qty means "unknown" and is
        /// left blank instead of printing "0".
        /// Columns: 0 Поз. 1 Наименование 2 Тип/марка 3 Код 4 Поставщик
        /// 5 Ед. изм. 6 Кол. 7 Масса 1 ед., кг 8 Примечание.
        /// </summary>
        internal static string[] SpecRowCells(int? pos, SpecRow r) => new[]
        {
            pos?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            r.Name ?? "",
            r.TypeMark ?? "",
            r.ItemCode ?? "",
            r.Manufacturer ?? "",
            r.Unit ?? "",
            pos == null && !(r.Qty > 0) ? "" : FormatNumber(r.Qty, r.Unit == "м" ? 1 : 0),
            r.Weight > 0 ? FormatNumber(r.Weight, 3) : "",
            r.Note ?? "",
        };

        /// <summary>
        /// Fills the data slice of one template table. The slice reserved for
        /// data sits between the row at <c>headerIdx</c> (-1 = start of the
        /// table) and the next single-cell header row (or end of table); its
        /// rows are filled first, then copies of the stencil are inserted
        /// before the next section. Unused reserved rows are removed by
        /// <see cref="Finish"/>.
        /// </summary>
        internal sealed class TableFiller
        {
            readonly Table _table;
            readonly List<TableRow> _reserved = new();
            readonly TableRow _nextSection;
            readonly TableRow _stencil;
            readonly ColumnStyles _styles;
            int _cursor;

            public TableFiller(Table table, int headerIdx)
            {
                _table = table;
                var trs = table.Elements<TableRow>().ToList();
                var reservedStart = headerIdx + 1;
                var reservedEnd = trs.Count;
                for (var i = reservedStart; i < trs.Count; i++)
                {
                    if (IsHeaderRow(trs[i])) { reservedEnd = i; break; }
                }
                _nextSection = reservedEnd < trs.Count ? trs[reservedEnd] : null;
                for (var i = reservedStart; i < reservedEnd; i++)
                {
                    if (trs[i].Elements<TableCell>().Count() != 9)
                        throw new InvalidOperationException($"Строка {i + 1} раздела материалов должна содержать 9 колонок. Вывод остановлен, чтобы не потерять позиции.");
                    _reserved.Add(trs[i]);
                }

                // Data-row stencil. Prefer the template's own data rows (the
                // reserved slice): the first 9-cell row of the TABLE is normally
                // the column-heading row ("Поз. | Наименование…"), and cloning it
                // gave every row beyond the reserved ones the heading's thick
                // borders and its w:tblHeader flag, and made every data row take
                // the heading's centred paragraph formatting instead of the data
                // rows' own (the name column is left-aligned in the ГОСТ
                // template). The LAST reserved row is the structural stencil (an
                // ordinary "middle" row: thin horizontal borders), the FIRST one
                // the per-column text style. Only a template with zero reserved
                // rows falls back to any 9-cell row of the table.
                var anyNineCellRow = trs.FirstOrDefault(r => r.Elements<TableCell>().Count() == 9
                                                             && !IsHeaderRow(r));
                var stencilSrc = _reserved.Count > 0 ? _reserved[_reserved.Count - 1] : anyNineCellRow;
                var styleSrc = _reserved.Count > 0 ? _reserved[0] : anyNineCellRow;
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
                _stencil = (TableRow)stencilSrc.CloneNode(deep: true);
                NormalizeStencilCells(_stencil);
                NormalizeStencilHeight(_stencil);
                // A data row must never be a "repeat as header row" row: the
                // flag came along when the column-heading row was the stencil.
                foreach (var trPr in _stencil.Elements<TableRowProperties>())
                    foreach (var th in trPr.Elements<TableHeader>().ToList()) th.Remove();

                // Capture the per-column styling (font, size, paragraph
                // alignment, ...) from the template's first data row *before*
                // the build loop starts writing into it. The reference cells
                // remember the original template formatting; every data row
                // we write will inherit pPr+rPr from these so the output
                // looks identical to the template's first data row regardless
                // of how many rows we append. Empty template rows carry their
                // font only on the paragraph mark (no runs) — SetCellText reads
                // it from there; fallbackCells (the old heading-row source) are
                // used only when a column has no run formatting at all.
                var styleRef = (TableRow)styleSrc.CloneNode(deep: true);
                NormalizeStencilCells(styleRef);
                NormalizeStencilHeight(styleRef);
                List<TableCell> fallbackCells = null;
                if (anyNineCellRow != null && !ReferenceEquals(anyNineCellRow, styleSrc))
                {
                    var fallbackRef = (TableRow)anyNineCellRow.CloneNode(deep: true);
                    NormalizeStencilCells(fallbackRef);
                    fallbackCells = fallbackRef.Elements<TableCell>().ToList();
                }
                _styles = new ColumnStyles(styleRef.Elements<TableCell>().ToList(), fallbackCells);
            }

            /// <summary>A bold sub-header spanning all 9 columns.</summary>
            public void SubHeader(string text)
                => WriteSubHeader(NextRow(_table, _reserved, ref _cursor, _stencil, _nextSection), text, _styles);

            /// <summary>A data row of 9 cells.</summary>
            public void DataRow(IReadOnlyList<string> cells)
                => WriteCells(NextRow(_table, _reserved, ref _cursor, _stencil, _nextSection), cells, _styles);

            /// <summary>Drop reserved rows we didn't consume (would otherwise stay blank).</summary>
            public void Finish()
            {
                for (var i = _cursor; i < _reserved.Count; i++)
                    _reserved[i]?.Remove();
            }
        }

        // --- table walk --------------------------------------------------

        internal static (Table table, int headerIdx)? FindMaterialsTable(Body body)
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

        internal static bool IsHeaderRow(TableRow row)
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

        internal static string RowText(TableRow row)
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
        static TableRow NextRow(Table table, List<TableRow> reserved, ref int cursor, TableRow stencil, TableRow nextSection)
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
            if (nextSection != null) table.InsertBefore(clone, nextSection);
            else table.AppendChild(clone);
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

        /// <summary>
        /// Per-column formatting sources for written cells. Primary = the
        /// template's first data row (what the template author designed for
        /// data). Fallback = the first 9-cell row of the table (normally the
        /// column headings — the only source before 1.3.17), consulted only
        /// for run formatting a primary cell does not carry at all, so a
        /// template whose data rows are completely unformatted keeps the
        /// font it used to get.
        /// </summary>
        sealed class ColumnStyles
        {
            readonly List<TableCell> _primary, _fallback;
            public ColumnStyles(List<TableCell> primary, List<TableCell> fallback)
            { _primary = primary; _fallback = fallback; }
            public TableCell Primary(int i) => _primary != null && i < _primary.Count ? _primary[i] : null;
            public TableCell Fallback(int i) => _fallback != null && i < _fallback.Count ? _fallback[i] : null;
        }

        static void WriteSubHeader(TableRow row, string text, ColumnStyles styles = null)
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
                // Typed setters, not AppendChild: CT_TcPr is a sequence
                // (tcW, gridSpan, hMerge, vMerge, tcBorders, …, vAlign) and
                // the ГОСТ template's cells already carry tcBorders + vAlign.
                // An appended hMerge landed after vAlign — 9 schema errors per
                // sub-header row ("unexpected child element hMerge"); the
                // typed property inserts it at its schema position.
                var tcPr = cells[i].TableCellProperties;
                if (tcPr == null) cells[i].TableCellProperties = tcPr = new TableCellProperties();
                foreach (var hm in tcPr.Elements<HorizontalMerge>().ToList()) hm.Remove();
                tcPr.HorizontalMerge = new HorizontalMerge
                {
                    Val = i == 0
                        ? MergedCellValues.Restart
                        : MergedCellValues.Continue,
                };
            }
            SetCellText(cells[0], text, bold: true, styleCell: styles?.Primary(0), fallbackStyleCell: styles?.Fallback(0));
            for (var i = 1; i < cells.Count; i++)
                SetCellText(cells[i], "", bold: false, styleCell: styles?.Primary(i), fallbackStyleCell: styles?.Fallback(i));
        }

        static void WriteCells(TableRow row, IReadOnlyList<string> values, ColumnStyles styles)
        {
            var cells = row.Elements<TableCell>().ToList();
            if (cells.Count != 9 || values == null || values.Count != 9)
                throw new InvalidOperationException("Строка спецификации должна содержать 9 колонок.");
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
            for (var i = 0; i < 9; i++)
                SetCellText(cells[i], values[i] ?? "", styleCell: styles?.Primary(i), fallbackStyleCell: styles?.Fallback(i));
        }

        /// <summary>
        /// Replace the cell's contents with one paragraph/run containing
        /// <paramref name="text"/>. Paragraph + run formatting (font,
        /// size, alignment, color) is copied from <paramref name="styleCell"/>
        /// when provided — the template's first data row, so every output
        /// row inherits the look the template defines for data. An empty
        /// template cell has no run: its font lives on the paragraph mark,
        /// which is what Word itself applies to text typed there, so that is
        /// the next source; <paramref name="fallbackStyleCell"/>'s run is the
        /// last. Falls back to the cell's own formatting when no styleCell
        /// is supplied. The text is made XML-safe and single-line first
        /// (see <see cref="XmlText"/>).
        /// </summary>
        static void SetCellText(TableCell cell, string text,
                                 bool bold = false, TableCell styleCell = null,
                                 TableCell fallbackStyleCell = null)
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
                originalRPr = FirstRunProperties(source)
                           ?? FromParagraphMark(pPr?.ParagraphMarkRunProperties);
            }
            if (originalRPr == null)
            {
                var fallback = fallbackStyleCell?.Elements<Paragraph>().FirstOrDefault();
                if (fallback != null) originalRPr = FirstRunProperties(fallback);
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
                    // Typed setter keeps CT_RPr order (b right after
                    // rStyle/rFonts); AppendChild put it after sz.
                    if (originalRPr.Bold == null) originalRPr.Bold = new Bold();
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

            run.AppendChild(new Text(XmlText.SingleLine(text)) { Space = SpaceProcessingModeValues.Preserve });
            p.AppendChild(run);
            cell.AppendChild(p);
        }

        static RunProperties FirstRunProperties(Paragraph paragraph)
        {
            var rPr = paragraph.Elements<Run>().FirstOrDefault()?.Elements<RunProperties>().FirstOrDefault();
            return rPr != null ? (RunProperties)rPr.CloneNode(true) : null;
        }

        /// <summary>
        /// Run formatting from a paragraph mark (w:pPr/w:rPr). CT_ParaRPr is
        /// CT_RPr plus revision marks for the mark itself (w:ins, w:del,
        /// w:moveFrom, w:moveTo, w:rPrChange) — those must not land on a run;
        /// everything else is the same element set in the same order.
        /// </summary>
        static RunProperties FromParagraphMark(ParagraphMarkRunProperties mark)
        {
            if (mark == null || !mark.HasChildren) return null;
            var rPr = new RunProperties();
            foreach (var child in mark.ChildElements)
            {
                switch (child.LocalName)
                {
                    case "ins": case "del": case "moveFrom": case "moveTo": case "rPrChange":
                        continue;
                }
                rPr.AppendChild(child.CloneNode(true));
            }
            return rPr.HasChildren ? rPr : null;
        }

        internal static string FormatNumber(double v, int decimals)
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

            // Same natural order as SpecRowBuilder.CompareRows: "Л-2" before
            // "Л-10" (ordinal put "Л-10" first). The two must stay in sync —
            // the builder's order is what a reader of DropReasons/the log
            // sees, the writer's is what the document shows.
            var lineOrder = byLine.Keys
                .OrderBy(t => t == SpecConstants.NullLine ? 1 : 0)
                .ThenBy(t => t, Comparer<string>.Create(SpecRowBuilder.CompareText))
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
