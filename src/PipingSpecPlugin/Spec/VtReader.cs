using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace PipingSpecPlugin.Spec
{
    public class VtReadResult
    {
        public List<VtRowData> Rows = new();
        // Column letter -> header text actually found in row 1 of the
        // workbook, for diagnostics when a required column is missing.
        public Dictionary<string, string> HeaderRow1 = new();
        public string Error; // null on success.
    }

    /// <summary>
    /// Reads the "Ведомость трубопроводов" (ВТ) Excel workbook. The sheet
    /// has a two-row header (row 1 = merged group headers, row 2 = sub-
    /// headers for the "Рабочие условия" / "Расчётные условия" / "Условия
    /// испытаний" groups) followed by data rows, interspersed with section
    /// title rows (a single non-empty cell spanning the row, e.g.
    /// "873.РД-001-ТХМ.ВТ (Цех №1)") and blank spacer rows. We locate
    /// columns by matching row-1 header text rather than hard-coding
    /// letters, because the sheet has been edited before (a column was
    /// inserted) and will likely be edited again.
    /// </summary>
    public static class VtReader
    {
        // The "n/n" column is the sync key (see VtRowData.NN) — named
        // separately so the required-column check below can reference it
        // by identity instead of a fragile array index.
        static readonly Action<VtRowData, string> NNAssign = (r, v) => r.NN = v;

        // Row-1 header text -> VtRowData field. Matched by trimmed,
        // whitespace-collapsed equality (headers wrap onto two lines in
        // the workbook, e.g. "Шифр\nпроекта").
        static readonly (string Header, Action<VtRowData, string> Assign)[] SingleColumnFields =
        {
            ("ИД трубопровода",  (r, v) => r.Id = v),
            ("n/n",              NNAssign),
            ("Шифр проекта",     (r, v) => r.ProjectCode = v),
            ("Обозначение",      (r, v) => r.Designation = v),
            ("Трубопровод (наружный диаметр)", (r, v) => r.PipeSize = v),
            ("Материал трубопровода",          (r, v) => r.Material = v),
            ("Наименование транспортируемого продукта", (r, v) => r.Product = v),
            ("Классификация по ГОСТ 32569",     (r, v) => r.GostGroup = v),
            ("Классификация по ТР ТС 032-2013", (r, v) => r.TrTsGroup = v),
            ("Плотность, кг/м3", (r, v) => r.Density = v),
            ("t°С",              (r, v) => r.DensityTemperature = v),
            ("Скорость коррозии, мм/год", (r, v) => r.CorrosionRate = v),
            ("Срок службы трубопровода/отбраковочная толщина стенки, лет/мм", (r, v) => r.ServiceLife = v),
            ("Неразрушающий контроль сварных швов, %", (r, v) => r.NdControl = v),
            ("Термообработка сварных соединений",      (r, v) => r.HeatTreatment = v),
            ("Дополнительные указания",  (r, v) => r.Additional = v),
            ("Дополнительныеуказания",   (r, v) => r.Additional = v), // header has no space in some revisions
            ("Толщина изоляции, мм",     (r, v) => r.InsulationThickness = v),
        };

        // Two-level group headers: row 1 gives the group ("Рабочие условия
        // трубопровода"), row 2 gives the sub-column ("Давление, МПа(изб.)"
        // / "Температура, °С"). We match on (group, subheader) so the
        // parser doesn't depend on fixed column letters within the group.
        static readonly (string Group, string SubHeader, Action<VtRowData, string> Assign)[] GroupedFields =
        {
            ("Рабочие условия трубопровода", "Давление, МПа(изб.)", (r, v) => r.WorkPressure = v),
            ("Рабочие условия трубопровода", "Температура, °С",     (r, v) => r.WorkTemperature = v),
            ("Расчётные условия трубопровода", "Давление, МПа(изб.)", (r, v) => r.DesignPressure = v),
            ("Расчётные условия трубопровода", "Температура, °С",     (r, v) => r.DesignTemperature = v),
            ("Условия испытаний", "На прочность/плотность, МПа(изб.)", (r, v) => r.TestStrength = v),
            ("Условия испытаний", "На герметичность, МПа(изб.)",       (r, v) => r.TestTightness = v),
        };

        public static VtReadResult Read(string path)
        {
            var result = new VtReadResult();
            try
            {
                using var doc = SpreadsheetDocument.Open(path, false);
                var wbPart = doc.WorkbookPart;
                var sheet = wbPart.Workbook.Descendants<Sheet>().FirstOrDefault();
                if (sheet == null) { result.Error = "В книге не найден ни один лист."; return result; }

                var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id);
                var sheetData = wsPart.Worksheet.Elements<SheetData>().FirstOrDefault();
                if (sheetData == null) { result.Error = "Лист пуст."; return result; }

                var sst = wbPart.SharedStringTablePart?.SharedStringTable;

                // Section-title rows ("873.РД-001-ТХМ.ВТ (Цех №1)") are
                // authored as a wide merged cell in the workbook — collect
                // every MergeCells range so such rows can be recognized
                // structurally (a row containing the top-left cell of a
                // range spanning many columns) rather than only inferred
                // from "exactly one non-empty cell", which a stray
                // formatted-but-empty neighbour cell could defeat.
                var wideMergeRowStartCols = new HashSet<(uint Row, string Col)>();
                var mergeCells = wsPart.Worksheet.Elements<MergeCells>().FirstOrDefault();
                if (mergeCells != null)
                {
                    foreach (var mc in mergeCells.Elements<MergeCell>())
                    {
                        var refText = mc.Reference?.Value;
                        if (string.IsNullOrEmpty(refText)) continue;
                        var refParts = refText.Split(':');
                        if (refParts.Length != 2) continue;
                        var (startCol, startRow) = SplitCellRef(refParts[0]);
                        var (endCol, endRow) = SplitCellRef(refParts[1]);
                        if (startRow == 0 || endRow == 0) continue;
                        // "Wide" = spans at least 10 columns — the actual
                        // ВТ section-title merges span the full sheet
                        // width (20+ columns); 10 is a conservative floor
                        // that still excludes small merges like the two-
                        // row group headers (which span 1-2 columns).
                        if (ColumnToIndex(endCol) - ColumnToIndex(startCol) >= 10)
                            wideMergeRowStartCols.Add((startRow, startCol));
                    }
                }

                string CellText(Cell cell)
                {
                    if (cell == null) return "";
                    // Inline strings (CellValues.InlineString) store text in
                    // <is><t>, not <v> — most real-world workbooks use the
                    // shared-string table instead, but both are valid OOXML.
                    if (cell.DataType != null && cell.DataType.Value == CellValues.InlineString)
                        return cell.InlineString?.Text?.Text ?? cell.InlineString?.InnerText ?? "";

                    if (cell.CellValue == null) return "";
                    var raw = cell.CellValue.InnerText;
                    if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
                    {
                        if (sst == null) return raw;
                        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)
                            && idx >= 0 && idx < sst.ChildElements.Count)
                            return sst.ElementAt(idx).InnerText;
                        return raw;
                    }
                    // Unmarked / numeric cells store the value in Excel's
                    // general number format, which uses scientific notation
                    // for small numbers (0.002 -> "2E-3"). Re-render as a
                    // plain decimal so a value copied straight into a
                    // Plant 3D text field doesn't confuse the reader.
                    if ((cell.DataType == null || cell.DataType.Value == CellValues.Number)
                        && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                        return num.ToString("0.####################", CultureInfo.InvariantCulture);
                    return raw;
                }

                string ColumnOf(string cellRef)
                {
                    int i = 0;
                    while (i < cellRef.Length && !char.IsDigit(cellRef[i])) i++;
                    return cellRef.Substring(0, i);
                }

                (string Col, uint Row) SplitCellRef(string cellRef)
                {
                    var col = ColumnOf(cellRef);
                    var rowPart = cellRef.Substring(col.Length);
                    return (col, uint.TryParse(rowPart, out var r) ? r : 0);
                }

                var rows = sheetData.Elements<Row>().ToList();
                if (rows.Count == 0) { result.Error = "На листе нет строк."; return result; }

                // Row 1: column letter -> header text.
                var row1 = rows[0];
                var col2Header = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var cell in row1.Elements<Cell>())
                {
                    var col = ColumnOf(cell.CellReference?.Value ?? "");
                    if (col == "") continue;
                    var text = Normalize(CellText(cell));
                    if (text != "") col2Header[col] = text;
                }
                result.HeaderRow1 = col2Header;

                // Row 2 (if present): column letter -> sub-header text, only
                // used for the grouped fields above.
                var col2SubHeader = new Dictionary<string, string>(StringComparer.Ordinal);
                if (rows.Count > 1)
                {
                    // The workbook's row 2 sub-headers live in actual row
                    // index 2 (r="2"); row objects aren't guaranteed to be
                    // contiguous/sorted so look up by RowIndex, not offset.
                    var physicalRow2 = rows.FirstOrDefault(r => r.RowIndex?.Value == 2);
                    if (physicalRow2 != null)
                        foreach (var cell in physicalRow2.Elements<Cell>())
                        {
                            var col = ColumnOf(cell.CellReference?.Value ?? "");
                            if (col == "") continue;
                            var text = Normalize(CellText(cell));
                            if (text != "") col2SubHeader[col] = text;
                        }
                }

                // Because merged group header cells only populate the
                // left-most column, forward-fill row1 group headers across
                // the columns that row 2 actually has sub-headers for.
                var sortedCols = col2Header.Keys.Concat(col2SubHeader.Keys).Distinct()
                    .OrderBy(c => c.Length).ThenBy(c => c, StringComparer.Ordinal).ToList();
                string lastGroup = null;
                var col2Group = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var col in sortedCols)
                {
                    if (col2Header.TryGetValue(col, out var h) && h != "") lastGroup = h;
                    if (col2SubHeader.ContainsKey(col)) col2Group[col] = lastGroup;
                }

                // Build column -> assign-to-field map.
                var singleAssign = new Dictionary<string, Action<VtRowData, string>>(StringComparer.Ordinal);
                foreach (var kv in col2Header)
                {
                    var match = SingleColumnFields.FirstOrDefault(f =>
                        string.Equals(f.Header, kv.Value, StringComparison.OrdinalIgnoreCase));
                    if (match.Assign != null) singleAssign[kv.Key] = match.Assign;
                }
                var groupedAssign = new Dictionary<string, Action<VtRowData, string>>(StringComparer.Ordinal);
                foreach (var kv in col2SubHeader)
                {
                    if (!col2Group.TryGetValue(kv.Key, out var group) || group == null) continue;
                    var match = GroupedFields.FirstOrDefault(f =>
                        string.Equals(f.Group, group, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(f.SubHeader, kv.Value, StringComparison.OrdinalIgnoreCase));
                    if (match.Assign != null) groupedAssign[kv.Key] = match.Assign;
                }

                if (!singleAssign.Values.Any(a => ReferenceEquals(a, NNAssign)))
                {
                    result.Error = "В файле не найдена колонка «n/n» — "
                        + "убедитесь, что она есть в первой строке заголовков.";
                    return result;
                }

                // Data rows start after the header rows (skip physical rows
                // 1 and 2 regardless of how many header rows the sheet
                // actually uses — row 2 might be sub-headers or might
                // already be data if a future revision drops the group
                // header row entirely; we detect that by checking whether
                // row 2 supplied any sub-headers at all).
                int firstDataRowIndex = col2SubHeader.Count > 0 ? 3 : 2;

                foreach (var row in rows)
                {
                    var idx = (int)(row.RowIndex?.Value ?? 0);
                    if (idx < firstDataRowIndex) continue;

                    var cellsByCol = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var cell in row.Elements<Cell>())
                    {
                        var col = ColumnOf(cell.CellReference?.Value ?? "");
                        if (col == "") continue;
                        cellsByCol[col] = CellText(cell);
                    }
                    if (cellsByCol.Count == 0) continue;

                    // Section-title rows (e.g. "873.РД-001-ТХМ.ВТ (Цех
                    // №1)") are a single merged cell spanning the whole
                    // row width. Primary check: a wide MergeCells range
                    // (>=10 columns) starting on this row — structural,
                    // survives a stray formatted-but-empty neighbour cell
                    // that would otherwise make "exactly one non-empty
                    // cell" miss the title row (or worse, treat a real
                    // data row with only one populated column as a
                    // title). Fallback for workbooks with no merge info:
                    // exactly one non-empty cell in the row.
                    var isSectionTitleRow = cellsByCol.Keys.Any(col => wideMergeRowStartCols.Contains(((uint)idx, col)));
                    if (isSectionTitleRow) continue;

                    var nonEmpty = cellsByCol.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToList();
                    if (nonEmpty.Count == 0) continue;   // fully blank spacer row.
                    if (nonEmpty.Count == 1) continue;   // section-title row (no merge info available).

                    var data = new VtRowData { RowNumber = idx };
                    foreach (var kv in cellsByCol)
                    {
                        if (singleAssign.TryGetValue(kv.Key, out var assign1)) assign1(data, kv.Value);
                        else if (groupedAssign.TryGetValue(kv.Key, out var assign2)) assign2(data, kv.Value);
                    }

                    // A genuine data row that somehow still has no n/n —
                    // not synchronizable, skip it.
                    if (string.IsNullOrWhiteSpace(data.NN)) continue;

                    data.NN = data.NN.Trim();
                    if (data.Id != null) data.Id = data.Id.Trim();
                    result.Rows.Add(data);
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Error = $"Не удалось прочитать файл: {ex.GetType().Name}: {ex.Message}";
                return result;
            }
        }

        static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // Collapse internal whitespace/newlines ("Шифр\nпроекта" ->
            // "Шифр проекта") so header matching doesn't depend on the
            // exact line-wrap the workbook happens to use.
            var parts = s.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts).Trim();
        }

        /// <summary>Converts a spreadsheet column letter ("A", "AB") to a 0-based index.</summary>
        static int ColumnToIndex(string col)
        {
            int n = 0;
            foreach (var c in col) n = n * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
            return n - 1;
        }
    }
}
