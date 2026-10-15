using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    /// <summary>
    /// Builds minimal .xlsx fixtures in memory (no shared-string table —
    /// inline strings are simpler to construct and VtReader.CellText
    /// handles both) to exercise VtReader's header matching and row
    /// filtering without depending on a real ВТ workbook on disk.
    /// </summary>
    public class VtReaderTests
    {
        static string WriteWorkbook(string[] header1, string[] header2, string[][] dataRows, string[] mergeCellRefs = null)
        {
            var path = Path.Combine(Path.GetTempPath(), $"vt-test-{System.Guid.NewGuid():N}.xlsx");
            using (var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
            {
                var wbPart = doc.AddWorkbookPart();
                wbPart.Workbook = new Workbook();
                var wsPart = wbPart.AddNewPart<WorksheetPart>();
                var sheetData = new SheetData();
                wsPart.Worksheet = new Worksheet(sheetData);

                void AddRow(uint rowIndex, string[] cells)
                {
                    if (cells == null) return;
                    var row = new Row { RowIndex = rowIndex };
                    for (int i = 0; i < cells.Length; i++)
                    {
                        if (cells[i] == null) continue;
                        var colLetter = ColumnLetter(i);
                        var cell = new Cell
                        {
                            CellReference = $"{colLetter}{rowIndex}",
                            DataType = CellValues.InlineString,
                            InlineString = new InlineString(new Text(cells[i])),
                        };
                        row.Append(cell);
                    }
                    sheetData.Append(row);
                }

                AddRow(1, header1);
                AddRow(2, header2);
                uint r = 3;
                foreach (var dataRow in dataRows) AddRow(r++, dataRow);

                if (mergeCellRefs != null && mergeCellRefs.Length > 0)
                {
                    var mergeCells = new MergeCells();
                    foreach (var refText in mergeCellRefs)
                        mergeCells.Append(new MergeCell { Reference = refText });
                    // MergeCells must appear after SheetData per the OOXML schema.
                    wsPart.Worksheet.InsertAfter(mergeCells, sheetData);
                }

                var sheets = wbPart.Workbook.AppendChild(new Sheets());
                sheets.Append(new Sheet
                {
                    Id = wbPart.GetIdOfPart(wsPart),
                    SheetId = 1,
                    Name = "Лист1",
                });
                wbPart.Workbook.Save();
            }
            return path;
        }

        static string ColumnLetter(int zeroBasedIndex)
        {
            int n = zeroBasedIndex + 1;
            string s = "";
            while (n > 0)
            {
                int rem = (n - 1) % 26;
                s = (char)('A' + rem) + s;
                n = (n - 1) / 26;
            }
            return s;
        }

        [Fact]
        public void Reads_NN_And_Simple_Columns_By_Header_Text()
        {
            var header1 = new[] { "n/n", "ИД трубопровода", "Шифр\nпроекта", "Обозначение" };
            var path = WriteWorkbook(header1, null, new[]
            {
                new[] { "1", "asdr", "873.РД-001-ТХМ", "150-P22002/2-LS025C" },
            });
            try
            {
                var result = VtReader.Read(path);
                Assert.Null(result.Error);
                var row = Assert.Single(result.Rows);
                Assert.Equal("1", row.NN);
                Assert.Equal("asdr", row.Id);
                Assert.Equal("873.РД-001-ТХМ", row.ProjectCode);
                Assert.Equal("150-P22002/2-LS025C", row.Designation);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Reads_Grouped_Header_Columns_Working_And_Design_Conditions()
        {
            var header1 = new[]
            {
                "n/n", "Обозначение",
                "Рабочие условия трубопровода", null,
                "Расчётные условия трубопровода", null,
            };
            var header2 = new[]
            {
                null, null,
                "Температура, °С", "Давление, МПа(изб.)",
                "Температура, °С", "Давление, МПа(изб.)",
            };
            var path = WriteWorkbook(header1, header2, new[]
            {
                new[] { "1", "TAG-1", "80", "1.6", "100", "2.0" },
            });
            try
            {
                var result = VtReader.Read(path);
                Assert.Null(result.Error);
                var row = Assert.Single(result.Rows);
                Assert.Equal("80", row.WorkTemperature);
                Assert.Equal("1.6", row.WorkPressure);
                Assert.Equal("100", row.DesignTemperature);
                Assert.Equal("2.0", row.DesignPressure);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Skips_Section_Title_Rows_And_Blank_Rows()
        {
            var header1 = new[] { "n/n", "Обозначение" };
            var path = WriteWorkbook(header1, null, new[]
            {
                (string[])null,                                  // blank spacer row
                new[] { null, "873.РД-001-ТХМ.ВТ (Цех №1)" },     // section title (no n/n)
                new[] { "1", "TAG-1" },
                new[] { "2", "TAG-2" },
            });
            try
            {
                var result = VtReader.Read(path);
                Assert.Null(result.Error);
                Assert.Equal(2, result.Rows.Count);
                Assert.Equal("1", result.Rows[0].NN);
                Assert.Equal("2", result.Rows[1].NN);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Missing_NN_Column_Reports_Error()
        {
            var header1 = new[] { "Обозначение", "Материал трубопровода" };
            var path = WriteWorkbook(header1, null, new[]
            {
                new[] { "TAG-1", "Ст.20" },
            });
            try
            {
                var result = VtReader.Read(path);
                Assert.NotNull(result.Error);
                Assert.Empty(result.Rows);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Trims_Whitespace_From_NN()
        {
            var header1 = new[] { "n/n", "Обозначение" };
            var path = WriteWorkbook(header1, null, new[]
            {
                new[] { "  1  ", "TAG-1" },
            });
            try
            {
                var result = VtReader.Read(path);
                var row = Assert.Single(result.Rows);
                Assert.Equal("1", row.NN);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Reads_Decimal_NN_Value()
        {
            // Real ВТ workbooks use dotted sub-item numbers like "3.1".
            var header1 = new[] { "n/n", "Обозначение" };
            var path = WriteWorkbook(header1, null, new[]
            {
                new[] { "3.1", "TAG-1" },
            });
            try
            {
                var result = VtReader.Read(path);
                var row = Assert.Single(result.Rows);
                Assert.Equal("3.1", row.NN);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Recognizes_Section_Title_Row_By_Wide_Merge_Even_With_A_Stray_Second_Cell()
        {
            // Real ВТ section-title rows ("873.РД-001-ТХМ.ВТ (Цех №1)")
            // are authored as one wide merged cell. A stray formatted-
            // but-non-empty neighbour cell (e.g. leftover formatting from
            // editing) would defeat the old "exactly one non-empty cell"
            // heuristic — the merge-range check must catch it instead.
            var header1 = new[] { "n/n", "Обозначение", "Шифр\nпроекта" };
            var dataRows = new[]
            {
                new[] { null, "873.РД-001-ТХМ.ВТ (Цех №1)", " " }, // section title + stray non-empty cell
                new[] { "1", "TAG-1", "873.РД-001-ТХМ" },
            };
            // Row 3 (the section title) spans columns A:O (index 0-14, 15
            // columns >= the 10-column "wide" threshold).
            var path = WriteWorkbook(header1, null, dataRows, mergeCellRefs: new[] { "A3:O3" });
            try
            {
                var result = VtReader.Read(path);
                Assert.Null(result.Error);
                var row = Assert.Single(result.Rows);
                Assert.Equal("1", row.NN);
                Assert.Equal("TAG-1", row.Designation);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Narrow_Merge_Does_Not_Suppress_A_Real_Data_Row()
        {
            // A merge narrower than the 10-column "wide" threshold (e.g.
            // the two-row group headers use small merges) must not be
            // mistaken for a section title.
            var header1 = new[] { "n/n", "Обозначение" };
            var path = WriteWorkbook(header1, null, new[]
            {
                new[] { "1", "TAG-1" },
            }, mergeCellRefs: new[] { "A3:B3" }); // only 2 columns — narrow.
            try
            {
                var result = VtReader.Read(path);
                Assert.Null(result.Error);
                var row = Assert.Single(result.Rows);
                Assert.Equal("1", row.NN);
            }
            finally { File.Delete(path); }
        }
    }
}
