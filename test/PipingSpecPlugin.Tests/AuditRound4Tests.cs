using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using PipingSpecPlugin.Audit;
using PipingSpecPlugin.Settings;
using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests;

// Fourth audit round (1.3.17): one test (or a small group) per fix that is
// verifiable without a live AutoCAD.
public class AuditRound4Tests
{
    // ---- XML-invalid characters ---------------------------------------------

    [Theory]
    [InlineData("Труба\u0001 57х3", "Труба 57х3")]
    [InlineData("a\u000Bb\u000Cc\u001Fd", "abcd")]
    [InlineData("tab\there\r\nnew", "tab\there\r\nnew")]          // legal in XML, kept by Clean
    [InlineData("x\uFFFEy\uFFFFz", "xyz")]
    [InlineData("ok °Ø±² м³", "ok °Ø±² м³")]
    public void XmlTextCleanDropsOnlyCharactersXmlCannotCarry(string input, string expected)
        => Assert.Equal(expected, XmlText.Clean(input));

    [Fact] public void XmlTextKeepsSurrogatePairsAndDropsLoneSurrogates()
    {
        var pair = char.ConvertFromUtf32(0x1F527);              // wrench, outside the BMP
        Assert.Equal("a" + pair + "b", XmlText.Clean("a" + pair + "b"));
        Assert.Equal("ab", XmlText.Clean("a\uD83Db"));           // high surrogate alone
        Assert.Equal("ab", XmlText.Clean("a\uDD27b"));           // low surrogate alone
        Assert.Equal("a", XmlText.Clean("a\uD83D"));             // high surrogate at the end
    }

    [Theory]
    [InlineData("a\tb", "a b")]
    [InlineData("a\r\nb", "a b")]
    [InlineData("a \n b", "a  b")]
    [InlineData("plain", "plain")]
    public void SingleLineTurnsBreaksIntoOneSpace(string input, string expected)
        => Assert.Equal(expected, XmlText.SingleLine(input));

    [Fact] public void ControlCharacterInAnyFieldNoLongerAbortsTheDocument()
    {
        var rows = new List<SpecRow>
        {
            new() { Category = "Pipe", Name = "Труба\u0001 57х3\u000B", TypeMark = "ГОСТ\u0002 8732", ItemCode = "К\u001F1",
                    Manufacturer = "Завод\u0007", Unit = "м", Qty = 1.5, Weight = 4.5, Note = "прим\u0008ечание" },
        };
        var review = new List<SpecRow> { new() { Category = SpecConstants.ReviewSectionHeader, Name = "Гиб\u0003", Note = "x\u0004" } };
        var (table, errors) = WriteGost(rows, review, reservedRows: 3);
        Assert.Empty(errors);
        var dataRow = table.Single(r => r[0] == "1");
        Assert.Equal("Труба 57х3", dataRow[1]);
        Assert.Equal("ГОСТ 8732", dataRow[2]);
        Assert.DoesNotContain(table.SelectMany(r => r), s => s.Any(c => c < ' '));
    }

    // ---- ГОСТ template: structure, stencil and styles -------------------------

    [Fact] public void OutputIsSchemaValidWithFormattedTemplateCells()
    {
        // The real template's cells carry tcBorders + vAlign; an appended
        // hMerge landed after vAlign → 9 validator errors per sub-header.
        var rows = Enumerable.Range(1, 6).Select(i => new SpecRow
        {
            Category = i <= 3 ? "Pipe" : "Fittings", Name = "Деталь " + i, Unit = i <= 3 ? "м" : "шт.",
            Qty = i <= 3 ? 1.5 : 2, Weight = 1.25, LineTag = i % 2 == 0 ? "L-2" : "L-10",
        }).ToList();
        var review = ReviewSection.BuildRows(new[] { new ReviewItem { Reason = "Деталь не прочитана (x)", PnPID = 7, DrawingPath = "A.dwg" } });
        var (_, errors) = WriteGost(rows, review, reservedRows: 2, groupByLine: true);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact] public void RowsBeyondTheReservedOnesCopyADataRowNotTheHeadingRow()
    {
        var rows = Enumerable.Range(1, 8).Select(i => new SpecRow
            { Category = "Fittings", Name = "Отвод " + i, Unit = "шт.", Qty = i, Weight = 1 }).ToList();
        WithGostDocument(rows, null, reservedRows: 2, doc =>
        {
            var trs = doc.MainDocumentPart.Document.Body.Descendants<TableRow>().ToList();
            // Heading row keeps its own repeat-header flag …
            Assert.NotNull(trs[0].TableRowProperties?.GetFirstChild<TableHeader>());
            // … but no data row (reserved or appended) is a header row, and
            // every data row has the thin (sz 4) horizontal border of the
            // template's data rows, not the heading's sz 12.
            var dataRows = trs.Where(tr => tr.Elements<TableCell>().First().InnerText is var t && int.TryParse(t, out _)).ToList();
            Assert.Equal(8, dataRows.Count);
            Assert.All(dataRows, tr => Assert.Null(tr.TableRowProperties?.GetFirstChild<TableHeader>()));
            Assert.All(dataRows.Skip(1), tr =>
                Assert.Equal(4u, tr.Elements<TableCell>().First().TableCellProperties.TableCellBorders.TopBorder.Size.Value));
        });
    }

    [Fact] public void DataCellsTakeTheTemplateDataRowFormatting()
    {
        var rows = new List<SpecRow> { new() { Category = "Fittings", Name = "Отвод 90", Unit = "шт.", Qty = 1, Weight = 1 } };
        WithGostDocument(rows, null, reservedRows: 2, doc =>
        {
            var dataRow = doc.MainDocumentPart.Document.Body.Descendants<TableRow>()
                .First(tr => tr.Elements<TableCell>().First().InnerText == "1");
            var cells = dataRow.Elements<TableCell>().ToList();
            // Name column: left-aligned like the template's data row (the
            // heading row centres everything).
            Assert.Null(cells[1].GetFirstChild<Paragraph>().ParagraphProperties?.Justification);
            Assert.Equal(JustificationValues.Center, cells[0].GetFirstChild<Paragraph>().ParagraphProperties.Justification.Val.Value);
            // Font from the empty data row's paragraph mark, not the default.
            var rPr = cells[1].Descendants<RunProperties>().First();
            Assert.Equal("GOST type A", rPr.RunFonts.Ascii.Value);
            Assert.NotNull(rPr.Italic);
            Assert.Null(rPr.Bold);
            // Sub-header: merged and bold.
            var header = doc.MainDocumentPart.Document.Body.Descendants<TableRow>()
                .First(tr => tr.InnerText.StartsWith("Детали трубопроводов"));
            Assert.Equal(MergedCellValues.Restart, header.Elements<TableCell>().First().TableCellProperties.HorizontalMerge.Val.Value);
            Assert.NotNull(header.Descendants<RunProperties>().First().Bold);
        });
    }

    [Fact] public void TemplateWithoutReservedRowsStillUsesAnyNineCellRow()
    {
        var rows = new List<SpecRow> { new() { Category = "Pipe", Name = "Труба", Unit = "м", Qty = 1, Weight = 1 } };
        var (table, errors) = WriteGost(rows, null, reservedRows: 0);
        Assert.Empty(errors);
        Assert.Contains(table, r => r.Count == 9 && r[0] == "1" && r[1] == "Труба");
    }

    // ---- bend mass in pieces --------------------------------------------------

    static PartRecord BendRec(int id, string weight = null, string lw = "10", string lwUnit = "kg/m", string len = "500") => new()
    {
        PnPID = id, SourcePnPID = id, PartCategory = "Pipe", PnPClassName = "PipeBend",
        PartSizeLongDesc = "Изгиб трубы 57х3", NominalDiameter = "50", NominalUnit = "mm",
        Length = len, LengthUnit = "mm", Weight = weight, WeightUnit = "kg",
        LinearWeight = lw, LinearWeightUnit = lwUnit, LineNumberTag = "L1", Material = "Steel",
        SourceDwgPath = @"C:\p\A.dwg",
    };

    [Fact] public void BendInPiecesGetsMassFromLengthTimesLinearMass()
    {
        var b = new SpecRowBuilder();   // IncludePipeBendsAsPipe off → «Детали», шт.
        var row = Assert.Single(b.Build(new[] { BendRec(1) }));
        Assert.Equal("Fittings", row.Category);
        Assert.Equal("шт.", row.Unit);
        Assert.Equal(5.0, row.Weight, 3);   // 0.5 m × 10 kg/m
        Assert.DoesNotContain(b.ReviewItems, i => i.Reason.StartsWith("Масса не задана"));
    }

    [Fact] public void BendOwnWeightWinsOverTheDerivedMass()
        => Assert.Equal(7.0, Assert.Single(new SpecRowBuilder().Build(new[] { BendRec(1, weight: "7") })).Weight, 3);

    [Theory]
    [InlineData(null, "kg/m", "500")]        // no linear mass
    [InlineData("10", "furlong/m", "500")]   // unknown linear unit — never guessed
    [InlineData("10", "kg/m", "0")]          // no length
    public void BendWithoutUsableDataStaysMassless(string lw, string lwUnit, string len)
    {
        var b = new SpecRowBuilder();
        var row = Assert.Single(b.Build(new[] { BendRec(1, lw: lw, lwUnit: lwUnit, len: len) }));
        Assert.Equal(0, row.Weight);
        Assert.Contains(b.ReviewItems, i => i.Reason.StartsWith("Масса не задана"));
    }

    [Fact] public void LbPerFootLinearMassIsConverted()
    {
        // 1 lb/ft = 1.488 kg/m; 1000 mm → 1.488 kg
        var bend = BendRec(1, lw: "1", lwUnit: "lb/ft", len: "1000");
        Assert.Equal(1.488, SpecRowBuilder.BendMassFromLinearWeight(bend).Value, 3);
    }

    [Fact] public void FoldedBendDoesNotAddMassToThePipeRow()
    {
        // ON mode: the bend's metres join the pipe; the row's «Масса 1 ед.»
        // stays the pipe's kg/m — the bend's derived 5 kg is not added.
        var pipe = new PartRecord
        {
            PnPID = 1, SourcePnPID = 1, PartCategory = "Pipe", PnPClassName = "Pipe", PartSizeLongDesc = "Труба 57х3",
            NominalDiameter = "50", NominalUnit = "mm", Length = "1000", LengthUnit = "mm", LinearWeight = "10",
            LinearWeightUnit = "kg/m", LineNumberTag = "L1", Material = "Steel", SourceDwgPath = @"C:\p\A.dwg",
        };
        var topo = new Dictionary<int, List<int>> { [1] = new() { 2 }, [2] = new() { 1 } };
        var row = Assert.Single(new SpecRowBuilder(new SpecBuilderOptions { IncludePipeBendsAsPipe = true, Topology = topo })
            .Build(new[] { pipe, BendRec(2) }));
        Assert.Equal(1.5, row.Qty);
        Assert.Equal(10.0, row.Weight, 3);
    }

    [Fact] public void ZeroLinearWeightFallsBackToWeightOverLength()
    {
        var pipe = new PartRecord
        {
            PnPID = 1, PartCategory = "Pipe", PnPClassName = "Pipe", PartSizeLongDesc = "Труба 57х3",
            Length = "2000", LengthUnit = "mm", LinearWeight = "0", LinearWeightUnit = "kg/m",
            Weight = "20", WeightUnit = "kg",
        };
        var b = new SpecRowBuilder();
        Assert.Equal(10.0, Assert.Single(b.Build(new[] { pipe })).Weight, 3);
        Assert.DoesNotContain(b.ReviewItems, i => i.Reason.StartsWith("Масса не задана"));
    }

    [Fact] public void OrphanBendInMetresKeepsKgPerMetre()
    {
        var row = Assert.Single(new SpecRowBuilder(new SpecBuilderOptions
            { IncludePipeBendsAsPipe = true, Topology = new Dictionary<int, List<int>>() }).Build(new[] { BendRec(1) }));
        Assert.Equal("м", row.Unit);
        Assert.Equal(10.0, row.Weight, 3);
    }

    // ---- natural order: strictly transitive -----------------------------------

    [Fact] public void NaturalCompareIsTransitiveWithMixedDigitScripts()
    {
        var items = new[] { "1\u0662", "\u0669", "z", "10", "9", "a1", "a01", "Л-2", "Л-10", "", "\uFF11", "08", "8" };
        foreach (var a in items)
            foreach (var b in items)
            {
                Assert.Equal(Math.Sign(SpecRowBuilder.CompareText(a, b)), -Math.Sign(SpecRowBuilder.CompareText(b, a)));
                foreach (var c in items)
                    if (SpecRowBuilder.CompareText(a, b) <= 0 && SpecRowBuilder.CompareText(b, c) <= 0)
                        Assert.True(SpecRowBuilder.CompareText(a, c) <= 0, $"{a} ≤ {b} ≤ {c}");
            }
        // ASCII digit runs still compare by value.
        Assert.True(VtValueParsing.NaturalCompare("Л-2", "Л-10") < 0);
        Assert.True(VtValueParsing.NaturalCompare("1.9", "1.10") < 0);
    }

    // ---- Miscellaneous gets a Russian section --------------------------------

    [Fact] public void MiscellaneousCategoryHasARussianSectionAfterFasteners()
    {
        Assert.Equal("Прочие изделия", SpecConstants.CategoryLabelRu["Miscellaneous"]);
        var disk = new PartRecord { PnPID = 1, PartCategory = "Miscellaneous", PnPClassName = "BlindDisk", PartSizeLongDesc = "Заглушка поворотная", Weight = "3.5" };
        var bolt = new PartRecord { PnPID = 2, PartCategory = "Fasteners", PnPClassName = "Gasket", PartSizeLongDesc = "Прокладка", Weight = "0.1" };
        var rows = new SpecRowBuilder().Build(new[] { disk, bolt });
        Assert.Equal(new[] { "Fasteners", "Miscellaneous" }, rows.Select(r => r.Category).ToArray());
        var (table, _) = WriteGost(rows, null, reservedRows: 2);
        Assert.Contains(table, r => r.Count > 0 && r[0] == "Прочие изделия");
        Assert.DoesNotContain(table, r => r.Count > 0 && r[0] == "Miscellaneous");
    }

    // ---- picker memory across scopes ------------------------------------------

    [Fact] public void FullProjectViewReplacesTheMemory()
    {
        var (sel, known) = PickerSelectionMemory.Merge(new[] { "A" }, new[] { "A", "B", "X" },
            shown: new[] { "A", "B" }, picked: new[] { "A" }, partialView: false);
        Assert.Equal(new[] { "A" }, sel);
        Assert.Equal(new[] { "A", "B" }, known);   // X (another project) forgotten, as before
    }

    [Fact] public void DrawingViewKeepsTheStateOfKeysItDidNotShow()
    {
        // C was ticked, D unticked in an earlier project run; this drawing
        // shows only A and B.
        var (sel, known) = PickerSelectionMemory.Merge(new[] { "A", "C" }, new[] { "A", "B", "C", "D" },
            shown: new[] { "A", "B" }, picked: new[] { "B" }, partialView: true);
        Assert.Equal(new[] { "B", "C" }, sel);
        Assert.Equal(new[] { "A", "B", "C", "D" }, known);
    }

    [Fact] public void KeysNeverShownStayUnknownSoTheyStartChecked()
    {
        // Line Z exists elsewhere in the project but was never shown: it must
        // not be stored as "known but unchecked".
        var (sel, known) = PickerSelectionMemory.Merge(null, null,
            shown: new[] { "A" }, picked: new[] { "A" }, partialView: true);
        Assert.Equal(new[] { "A" }, sel);
        Assert.DoesNotContain("Z", known);
        // "everything selected" (no list yet) carries over as selected
        var (sel2, _) = PickerSelectionMemory.Merge(null, new[] { "A", "Q" },
            shown: new[] { "A" }, picked: new[] { "A" }, partialView: true);
        Assert.Contains("Q", sel2);
    }

    // ---- stranded bend metres: 1 µm like the pipe rows -------------------------

    [Fact] public void StrandedBendLengthIsNotTruncatedToMillimetres()
    {
        // Target pipe has an unknown length unit → it goes to review and the
        // bend's metres folded into it are stranded (no sibling pipe).
        var target = new PartRecord
        {
            PnPID = 1, SourcePnPID = 1, PartCategory = "Pipe", PnPClassName = "Pipe", PartSizeLongDesc = "Труба",
            NominalDiameter = "50", NominalUnit = "mm", Length = "1000", LengthUnit = "furlong", LineNumberTag = "L1",
            Material = "Steel", SourceDwgPath = @"C:\p\A.dwg",
        };
        var bend = BendRec(2, len: "300.04");
        var topo = new Dictionary<int, List<int>> { [1] = new() { 2 }, [2] = new() { 1 } };
        var b = new SpecRowBuilder(new SpecBuilderOptions { IncludePipeBendsAsPipe = true, Topology = topo });
        b.Build(new[] { target, bend });
        var stranded = Assert.Single(b.ReviewItems, i => i.Reason.StartsWith("Длину гиба не удалось отнести"));
        Assert.Equal(0.30004, stranded.Qty.Value, 6);
        var row = ReviewSection.BuildRows(new[] { stranded }).Single();
        Assert.Equal(0.4, row.Qty);
    }

    // ---- audit: an unknown unit of the wall is named as such --------------------

    [Fact] public void WallReadableButUnitUnknownSaysSo()
    {
        // The wall is in the part's catalogue LengthUnit (not INSUNITS).
        var snapshot = new AuditSnapshot();
        snapshot.Parts.Add(new AuditPart { Key = "A1", IsPipeOrElbow = true, LineKey = "L1", Diameter = "50", WallRaw = "4", LengthUnit = "furlong",
            WallMm = AuditRules.WallMillimetres("4", "furlong") });
        snapshot.Parts.Add(new AuditPart { Key = "A2", IsPipeOrElbow = true, LineKey = "L1", Diameter = "50", WallRaw = "abc", WallMm = null });
        var options = new AuditOptions(); options.Enabled.Add("wall");
        var issues = AuditRules.Run(snapshot, options);
        Assert.Contains(issues, i => i.Keys.Contains("A1") && i.Message.StartsWith("Неизвестная единица длины детали «furlong»"));
        Assert.Contains(issues, i => i.Keys.Contains("A2") && i.Message.StartsWith("Не удалось определить толщину стенки"));
        Assert.DoesNotContain(issues, i => i.Message.Contains("INSUNITS"));
    }

    [Fact] public void AuditReportSurvivesControlCharactersInDescriptions()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".docx");
        var snapshot = new AuditSnapshot { Drawing = "Чертёж\u0001.dwg" };
        snapshot.Parts.Add(new AuditPart { Key = "AE4", LineKey = "L1" });
        var options = new AuditOptions(); options.Enabled.Add("connections");
        var issues = new List<AuditIssue> { new() { RuleId = "connections", Check = "Соединения", Severity = "Проверить",
            Line = "L1", Message = "Свободный порт\u0002 S1\nвторая строка", Keys = new List<string> { "AE4" } } };
        try
        {
            AuditDocxReport.Save(path, snapshot, options, issues);
            using var document = WordprocessingDocument.Open(path, false);
            var text = document.MainDocumentPart.Document.InnerText;
            Assert.Contains("Свободный порт S1", text);
            // Char check, ordinal: xUnit's string overload compares with the
            // current culture, where a control character is ignorable and
            // "found" in every string.
            Assert.DoesNotContain(text, c => c == '\u0001' || c == '\u0002');
        }
        finally { File.Delete(path); }
    }

    // ---- bundle: AutoCAD 2025+ loads the build compiled against 2025 ---------

    [Fact] public void BundleSeriesMinMatchesTheReferencedAutoCadRelease()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "bundle", "PackageContents.xml"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var req = XDocument.Load(Path.Combine(dir.FullName, "bundle", "PackageContents.xml"))
            .Descendants("RuntimeRequirements").Single();
        Assert.Equal("R25.0", (string)req.Attribute("SeriesMin"));
        // R25.0 is only valid while the DLL is compiled against the 2025 wrappers:
        // .NET cannot bind a 2026 (25.1) reference to 2025's 25.0 assemblies.
        var csproj = File.ReadAllText(Path.Combine(dir.FullName, "src", "PipingSpecPlugin", "PipingSpecPlugin.csproj"));
        Assert.Contains(@"<AcadInstallDir Condition="" '$(AcadInstallDir)' == '' "">C:\Program Files\Autodesk\AutoCAD 2025</AcadInstallDir>", csproj);
    }

    // ---- helpers ------------------------------------------------------------

    // A template shaped like the user's ГОСТ form: a 9-cell column-heading
    // row (repeat-header, thick borders, bold centred text), the
    // «Изделия и материалы» row, then reserved empty data rows whose cells
    // carry tcBorders + vAlign and their font only on the paragraph mark.
    static string MakeGostTemplate(string directory, int reservedRows)
    {
        var template = Path.Combine(directory, "template.docx");
        TableCellBorders Borders(uint topBottom) => new(
            new TopBorder { Val = BorderValues.Single, Size = topBottom },
            new LeftBorder { Val = BorderValues.Single, Size = 12 },
            new BottomBorder { Val = BorderValues.Single, Size = topBottom },
            new RightBorder { Val = BorderValues.Single, Size = 12 });
        TableCell HeadingCell(string text) => new(
            new TableCellProperties(new TableCellWidth { Width = "1000", Type = TableWidthUnitValues.Dxa }, Borders(12),
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }),
            new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                new Run(new RunProperties(new RunFonts { Ascii = "GOST type A" }, new Bold(), new Italic()), new Text(text))));
        TableCell DataCell(int column)
        {
            var pPr = new ParagraphProperties();
            if (column != 1) pPr.AppendChild(new Justification { Val = JustificationValues.Center });
            pPr.AppendChild(new ParagraphMarkRunProperties(new RunFonts { Ascii = "GOST type A" }, new Italic()));
            return new TableCell(
                new TableCellProperties(new TableCellWidth { Width = "1000", Type = TableWidthUnitValues.Dxa }, Borders(4),
                    new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }),
                new Paragraph(pPr));
        }
        TableRow Row(TableRowProperties trPr, IEnumerable<TableCell> cells)
        {
            var tr = new TableRow(trPr);
            foreach (var c in cells) tr.AppendChild(c);
            return tr;
        }
        using (var doc = WordprocessingDocument.Create(template, WordprocessingDocumentType.Document))
        {
            var part = doc.AddMainDocumentPart();
            var table = new Table(
                new TableProperties(new TableWidth { Width = "9000", Type = TableWidthUnitValues.Dxa }),
                new TableGrid(Enumerable.Range(0, 9).Select(_ => new GridColumn { Width = "1000" })),
                Row(new TableRowProperties(new CantSplit(), new TableHeader()),
                    Enumerable.Range(0, 9).Select(i => HeadingCell("H" + i))),
                new TableRow(new TableCell(
                    new TableCellProperties(new GridSpan { Val = 9 }),
                    new Paragraph(new Run(new Text(SpecConstants.MaterialsSectionHeader))))));
            for (var r = 0; r < reservedRows; r++)
                table.AppendChild(Row(new TableRowProperties(new CantSplit()),
                    Enumerable.Range(0, 9).Select(DataCell)));
            part.Document = new Document(new Body(table, new Paragraph()));
            part.Document.Save();
        }
        return template;
    }

    static void WithGostDocument(IList<SpecRow> rows, IList<SpecRow> review, int reservedRows,
                                 Action<WordprocessingDocument> inspect, bool groupByLine = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "spec-r4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var output = Path.Combine(dir, "out.docx");
            SpecTemplateWriter.Write(MakeGostTemplate(dir, reservedRows), output, rows, groupByLine, review);
            using var doc = WordprocessingDocument.Open(output, false);
            inspect(doc);
        }
        finally { Directory.Delete(dir, true); }
    }

    static (List<List<string>> table, List<string> errors) WriteGost(IList<SpecRow> rows, IList<SpecRow> review,
                                                                    int reservedRows, bool groupByLine = false)
    {
        List<List<string>> table = null;
        List<string> errors = null;
        WithGostDocument(rows, review, reservedRows, doc =>
        {
            table = doc.MainDocumentPart.Document.Body.Descendants<TableRow>()
                .Select(tr => tr.Elements<TableCell>().Select(c => c.InnerText).ToList()).ToList();
            errors = new OpenXmlValidator(FileFormatVersions.Office2010).Validate(doc)
                .Select(e => e.Description + " @ " + e.Path?.XPath).ToList();
        }, groupByLine);
        return (table, errors);
    }
}
