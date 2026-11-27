using System.IO;
using System.Collections.Generic;
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
        public void Combines_Split_Gost_Classification_And_Keeps_TrTs_Separate()
        {
            var path = WriteWorkbook(
                new[] { "n/n", "Классификация по ГОСТ 32569", null, "Классификация по ТР ТС 032-2013" },
                new[] { null, "Категория\nтрубопровода", "Группа\nрабочей среды", null },
                new[]
                {
                    new[] { "1", " II ", " Б(б) ", "2" },
                    new[] { "2", null, "Б(б)", "2" },
                },
                new[] { "B1:C1" });
            try
            {
                var result = VtReader.Read(path);
                Assert.Null(result.Error);
                Assert.Equal(2, result.Rows.Count);
                Assert.Equal("Б(б) - II", result.Rows[0].GostGroup);
                Assert.Equal("2", result.Rows[0].TrTsGroup);
                Assert.True(string.IsNullOrEmpty(result.Rows[1].GostGroup));
            }
            finally { File.Delete(path); }
        }

        [Theory]
        [InlineData("Группа рабочей среды", "Категория трубопровода", "А(а)", "I", "А(а) - I")]
        [InlineData("Классификация по ГОСТ 32569", "Обозначение", "А(а) - I", "TAG-1", "А(а) - I")]
        public void Reads_Flat_Gost_Classification(string firstHeader, string secondHeader,
            string firstValue, string secondValue, string expected)
        {
            var path = WriteWorkbook(new[] { "n/n", firstHeader, secondHeader }, null,
                new[] { new[] { "1", firstValue, secondValue } });
            try
            {
                var result = VtReader.Read(path);
                Assert.Null(result.Error);
                Assert.Equal(expected, Assert.Single(result.Rows).GostGroup);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Reads_First_Data_Row_Immediately_After_Flat_Split_Header()
        {
            var path = WriteWorkbook(new[] { "n/n", "Группа рабочей среды", "Категория трубопровода" },
                new[] { "1", "Б(б)", "II" }, new string[0][]);
            try
            {
                var result = VtReader.Read(path);
                Assert.Null(result.Error);
                Assert.Equal("Б(б) - II", Assert.Single(result.Rows).GostGroup);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Shared_Field_Mapping_Imports_And_Plans_Grouped_Pressure_And_Inspection_Percentage()
        {
            var path = WriteWorkbook(
                new[] { "n/n", "Обозначение", "Рабочие условия трубопровода", "Неразрушающий контроль сварных швов, %" },
                new[] { null, null, "Давление, МПа(изб.)" },
                new[] { new[] { "1", "50-KSN003-LS016C-HN", "1,6", "РД/УЗК 10%" } });
            try
            {
                var read = VtReader.Read(path);
                Assert.Null(read.Error);
                var lines = new List<LineGroupInfo> { new() { RowId = 1, Id = "1", Tag = "50-KSN003-LS016C-HN" } };
                var changes = Assert.Single(LineSyncPlanner.BuildPlan(read.Rows, lines).Actions).Changes;
                Assert.Contains(changes, item => item.PropertyName == "Pwork" && item.NewValue == "1,6");
                Assert.Contains(changes, item => item.PropertyName == "NDControl" && item.NewValue == "10");
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Reads_Row_Two_With_A_Single_Level_Header()
        {
            var path = WriteWorkbook(new[] { "n/n", "Обозначение" }, new[] { "1", "FIRST" },
                new[] { new[] { "2", "SECOND" } });
            try
            {
                var result = VtReader.Read(path);
                Assert.Null(result.Error);
                Assert.Equal(2, result.Rows.Count);
                Assert.Equal("FIRST", result.Rows[0].Designation);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Does_Not_Import_Corrosion_Allowance_As_Rate()
        {
            var path = WriteWorkbook(new[] { "n/n", "Прибавка на коррозию, мм", "Срок службы" }, null,
                new[] { new[] { "1", "3", "20" } });
            try
            {
                var result = VtReader.Read(path);
                Assert.Null(result.Error);
                Assert.Null(Assert.Single(result.Rows).CorrosionRate);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Retains_Nn_Only_Row_For_Duplicate_Detection_And_Rejects_Months_Header()
        {
            var path = WriteWorkbook(new[] { "n/n", "Обозначение", "Срок службы, мес." }, new[] { "51" },
                new[] { new[] { "51", "TAG", "24" } });
            try
            {
                var result = VtReader.Read(path);
                Assert.Null(result.Error);
                Assert.Equal(2, result.Rows.Count);
                Assert.All(result.Rows, row => { Assert.Equal("51", row.NN); Assert.Null(row.ServiceLife); });
            }
            finally { File.Delete(path); }
        }

        [Theory]
        [InlineData(2U, "1.10")]
        [InlineData(14U, null)]
        public void Numeric_Nn_Preserves_Fixed_Precision_Or_Rejects_Date_Format(uint formatId, string expected)
        {
            var path = WriteWorkbook(new[] { "n/n", "Обозначение" }, null,
                new[] { new[] { "1.1", "TAG" } });
            try
            {
                using (var doc = SpreadsheetDocument.Open(path, true))
                {
                    var workbook = doc.WorkbookPart;
                    var styles = workbook.AddNewPart<WorkbookStylesPart>();
                    styles.Stylesheet = new Stylesheet(new CellFormats(new CellFormat { NumberFormatId = formatId }));
                    foreach (var cell in System.Linq.Enumerable.Single(workbook.WorksheetParts).Worksheet.Descendants<Cell>())
                    {
                        if (cell.CellReference.Value != "A3") continue;
                        cell.DataType = CellValues.Number;
                        cell.InlineString = null;
                        cell.CellValue = new CellValue("1.1");
                        cell.StyleIndex = 0U;
                    }
                }
                var result = VtReader.Read(path);
                if (expected == null)
                {
                    Assert.NotNull(result.Error);
                    Assert.Empty(result.Rows);
                }
                else
                {
                    Assert.Null(result.Error);
                    Assert.Equal(expected, Assert.Single(result.Rows).NN);
                }
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

        [Fact]
        public void Can_Read_Workbook_Opened_With_FileShare_ReadWrite()
        {
            var header1 = new[] { "n/n", "Обозначение" };
            var path = WriteWorkbook(header1, null, new[] { new[] { "1", "TAG-1" } });
            try
            {
                // Simulate Excel opening the file with FileShare.ReadWrite
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var result = VtReader.Read(path);
                Assert.Null(result.Error);
                var row = Assert.Single(result.Rows);
                Assert.Equal("1", row.NN);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Reads_ServiceLife_From_Pipeline_Service_Life_Header()
        {
            var header1 = new[] { "n/n", "Обозначение", "Срок службы\nтрубопровода" };
            var path = WriteWorkbook(header1, null, new[] { new[] { "1", "TAG-1", "20" } });
            try
            {
                var result = VtReader.Read(path);
                Assert.Null(result.Error);
                var row = Assert.Single(result.Rows);
                Assert.Equal("1", row.NN);
                Assert.Equal("20", row.ServiceLife);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Reads_ServiceLife_From_Column_T()
        {
            // 20 columns: index 0 is 'A', index 19 is 'T'
            var headers = new string[20];
            headers[0] = "n/n";
            headers[1] = "Обозначение";
            headers[19] = "Срок службы трубопровода"; // column T
            var rowValues = new string[20];
            rowValues[0] = "1";
            rowValues[1] = "TAG-1";
            rowValues[19] = "25";

            var path = WriteWorkbook(headers, null, new[] { rowValues });
            try
            {
                var result = VtReader.Read(path);
                Assert.Null(result.Error);
                var row = Assert.Single(result.Rows);
                Assert.Equal("1", row.NN);
                Assert.Equal("25", row.ServiceLife);
            }
            finally { File.Delete(path); }
        }

        [Theory]
        [InlineData("Скорость коррозии")]
        [InlineData("Скорость коррозии (мм/год)")]
        [InlineData("Скорость\nкоррозии, мм/год")]
        public void Reads_CorrosionRate_From_Various_Header_Formats(string header)
        {
            var header1 = new[] { "n/n", "Обозначение", header };
            var path = WriteWorkbook(header1, null, new[] { new[] { "1", "TAG-1", "0.1" } });
            try
            {
                var result = VtReader.Read(path);
                Assert.Null(result.Error);
                var row = Assert.Single(result.Rows);
                Assert.Equal("1", row.NN);
                Assert.Equal("0.1", row.CorrosionRate);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Reads_CorrosionRate_From_Column_S()
        {
            // 20 columns: index 0 is 'A', index 18 is 'S', index 19 is 'T'
            var headers = new string[20];
            headers[0] = "n/n";
            headers[1] = "Обозначение";
            headers[18] = "Скорость коррозии, мм/год"; // column S
            headers[19] = "Срок службы трубопровода";  // column T
            var rowValues = new string[20];
            rowValues[0] = "1";
            rowValues[1] = "TAG-1";
            rowValues[18] = "0.1";
            rowValues[19] = "20";

            var path = WriteWorkbook(headers, null, new[] { rowValues });
            try
            {
                var result = VtReader.Read(path);
                Assert.Null(result.Error);
                var row = Assert.Single(result.Rows);
                Assert.Equal("1", row.NN);
                Assert.Equal("0.1", row.CorrosionRate);
                Assert.Equal("20", row.ServiceLife);
            }
            finally { File.Delete(path); }
        }
    }
}
