using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PipingSpecPlugin.Settings;
using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests;

// Third audit round (1.3.16): one test (or a small group) per fix that is
// verifiable without a live AutoCAD.
public class AuditRound3Tests
{
    static PartRecord Pipe(int id, string length = "1000") => new()
    {
        PnPID = id, SourcePnPID = id, PartCategory = "Pipe", PnPClassName = "Pipe",
        PartSizeLongDesc = "Pipe DN100", NominalDiameter = "100", NominalUnit = "mm",
        Length = length, LengthUnit = "mm", Material = "Steel", LineNumberTag = "L1",
        Status = "New", LinearWeight = "10", LinearWeightUnit = "kg/m", SourceDwgPath = @"C:\p\A.dwg",
    };
    static PartRecord Bend(int id, string length = "500")
    {
        var p = Pipe(id, length); p.PnPClassName = "PipeBend"; p.PartSizeLongDesc = "Bend DN100";
        return p;
    }
    static PartRecord Weld(int id) => new() { PnPID = id, PnPClassName = "P3dConnector" };

    // ---- float noise must not add a phantom 0.1 m -----------------------

    [Theory]
    [InlineData("3000.00000000004")]
    [InlineData("2999.99999999996")]
    [InlineData("3000")]
    public void BinaryNoiseInLengthDoesNotAddADecimetre(string length)
    {
        var row = Assert.Single(new SpecRowBuilder().Build(new[] { Pipe(1, length) }));
        Assert.Equal(3.0, row.Qty);
        Assert.Contains("3.3 м", row.Note);
    }

    [Fact] public void RealExcessOverWholeDecimetreIsStillRoundedUp()
    {
        // 3000.01 mm is a real length — never under-reported.
        Assert.Equal(3.1, Assert.Single(new SpecRowBuilder().Build(new[] { Pipe(1, "3000.01") })).Qty);
    }

    // ---- category synonyms: one section, one row -------------------------

    [Fact] public void SupportCategorySynonymsShareOneSectionAndRow()
    {
        var a = new PartRecord { PnPID = 1, PartCategory = "Support", PartSizeLongDesc = "Опора КП-100", Weight = "2" };
        var b = new PartRecord { PnPID = 2, ContentIsoSymbolDefinition = "TYPE=SUPPORT", PartSizeLongDesc = "Опора КП-100", Weight = "2" };
        var row = Assert.Single(new SpecRowBuilder().Build(new[] { a, b }));
        Assert.Equal("Supports", row.Category);
        Assert.Equal(2, row.Qty);
    }

    [Theory]
    [InlineData("Instrument", "Instruments")]
    [InlineData("PipeSpecialty", "PipeSpecialties")]
    [InlineData("Specialty", "PipeSpecialties")]
    [InlineData("Valves", "Valves")]
    public void CanonicalSectionFoldsSynonyms(string category, string expected)
        => Assert.Equal(expected, CategoryClassifier.CanonicalSection(category));

    [Fact] public void SynonymChipStillFiltersByCatalogueKey()
    {
        // The picker chip is the catalogue key ("Support"); folding happens
        // only for the OUTPUT, after the category filter.
        var a = new PartRecord { PnPID = 1, PartCategory = "Support", PartSizeLongDesc = "Опора" };
        var opts = new SpecBuilderOptions { Categories = new[] { "Support" } };
        Assert.Equal("Supports", Assert.Single(new SpecRowBuilder(opts).Build(new[] { a })).Category);
    }

    // ---- natural, total sort order ----------------------------------------

    [Fact] public void NamesSortNaturallyWithinDiameter()
    {
        var a = Pipe(1); a.PartSizeLongDesc = "Труба 108х10";
        var b = Pipe(2); b.PartSizeLongDesc = "Труба 108х4";
        var rows = new SpecRowBuilder().Build(new[] { a, b });
        Assert.Equal(new[] { "Труба 108х4", "Труба 108х10" }, rows.Select(r => r.Name).ToArray());
    }

    [Fact] public void OrderDoesNotDependOnReadOrder()
    {
        // Same name/ND, different item code → two rows; their order used to
        // follow Dictionary insertion order (= DWG read order).
        var a = Pipe(1); a.ItemCode = "B-2";
        var b = Pipe(2); b.ItemCode = "A-1";
        var first = new SpecRowBuilder().Build(new[] { a, b }).Select(r => r.ItemCode).ToArray();
        var second = new SpecRowBuilder().Build(new[] { b, a }).Select(r => r.ItemCode).ToArray();
        Assert.Equal(new[] { "A-1", "B-2" }, first);
        Assert.Equal(first, second);
    }

    [Fact] public void LinesSortNaturallyInBuilderPickerAndDocument()
    {
        var a = Pipe(1); a.LineNumberTag = "L-10";
        var b = Pipe(2); b.LineNumberTag = "L-2";
        var rows = new SpecRowBuilder(new SpecBuilderOptions { GroupByLine = true }).Build(new[] { a, b });
        Assert.Equal(new[] { "L-2", "L-10" }, rows.Select(r => r.LineTag).ToArray());
        Assert.Equal(new[] { "L-2", "L-10" }, ProjectFacts.From(new[] { a, b }).Lines.Select(l => l.key).ToArray());

        var table = WriteAndRead(rows, null, groupByLine: true);
        var headers = table.Where(r => r.Count > 0 && r[0].StartsWith("Линия")).Select(r => r[0]).ToList();
        Assert.Equal(new[] { "Линия L-2", "Линия L-10" }, headers);
    }

    [Fact] public void ValveTagsAreListedNaturally()
    {
        PartRecord V(int id, string tag) => new() { PnPID = id, PartCategory = "Valves", PartSizeLongDesc = "Задвижка", EquipTag = tag, Weight = "5" };
        var row = Assert.Single(new SpecRowBuilder().Build(new[] { V(1, "КЛ-10"), V(2, "КЛ-2"), V(3, "КЛ-1") }));
        Assert.Equal("КЛ-1, КЛ-2, КЛ-10", row.TypeMark);
    }

    // ---- review section: one part, one item --------------------------------

    [Fact] public void MultiLinePartRejectedOnceIsReviewedOnce()
    {
        // Same zero-length pipe linked to two lines → two records.
        var a = Pipe(1, "0"); var b = Pipe(1, "0"); b.LineNumberTag = "L2";
        var builder = new SpecRowBuilder();
        builder.Build(new[] { a, b });
        var row = Assert.Single(ReviewSection.BuildRows(builder.ReviewItems));
        Assert.DoesNotContain("деталей:", row.Note);
        Assert.Contains("PnPID 1", row.Note);
    }

    [Fact] public void UnclassifiedMultiLinePartIsReviewedOnce()
    {
        var a = new PartRecord { PnPID = 5, SourcePnPID = 5, PnPClassName = "Mystery", LineNumberTag = "L1", SourceDwgPath = "A.dwg" };
        var b = new PartRecord { PnPID = 5, SourcePnPID = 5, PnPClassName = "Mystery", LineNumberTag = "L2", SourceDwgPath = "A.dwg" };
        var builder = new SpecRowBuilder();
        builder.Build(new[] { a, b });
        var row = Assert.Single(ReviewSection.BuildRows(builder.ReviewItems));
        Assert.DoesNotContain("деталей:", row.Note);
    }

    [Fact] public void SamePnPIdInDifferentDrawingsAreDifferentParts()
    {
        var items = new[]
        {
            new ReviewItem { Reason = "x", PnPID = 7, DrawingPath = @"C:\p\A.dwg" },
            new ReviewItem { Reason = "x", PnPID = 7, DrawingPath = @"C:\p\B.dwg" },
        };
        Assert.Contains("деталей: 2", Assert.Single(ReviewSection.BuildRows(items)).Note);
    }

    [Fact] public void RowLevelItemsAreNotCountedAsParts()
    {
        var items = new[]
        {
            new ReviewItem { Reason = "Разная масса", Description = "Задвижка" },
            new ReviewItem { Reason = "Разная масса", Description = "Задвижка" },
        };
        var row = Assert.Single(ReviewSection.BuildRows(items));
        Assert.Contains("строк спецификации: 2", row.Note);
        Assert.DoesNotContain("деталей", row.Note);
    }

    [Fact] public void MassSpreadIsPrintedWithAtMostThreeDecimals()
    {
        var a = new PartRecord { PnPID = 1, PartCategory = "Valves", PartSizeLongDesc = "Задвижка", Weight = "1.23456789" };
        var b = new PartRecord { PnPID = 2, PartCategory = "Valves", PartSizeLongDesc = "Задвижка", Weight = "2" };
        var builder = new SpecRowBuilder();
        var row = Assert.Single(builder.Build(new[] { a, b }));
        Assert.Contains("1.235..2 кг", row.Note);
        Assert.DoesNotContain("1.23456789", row.Note);
    }

    // ---- stranded bend metres: reroute only to the same purchased pipe ----

    static (List<SpecRow> rows, SpecRowBuilder builder) StrandedScenario(string siblingWall, bool withOrphanBend = false)
    {
        // Pipe 1 has an unknown length unit → it goes to review, the bend's
        // metres (folded into it) are stranded and look for a sibling.
        var target = Pipe(1); target.LengthUnit = "furlong"; target.WallThickness = "4";
        var bend = Bend(2); bend.WallThickness = "4";
        var records = new List<PartRecord> { target, bend };
        var topo = new Dictionary<int, List<int>> { [1] = new() { 2 }, [2] = new() { 1 } };
        if (siblingWall != null)
        {
            var sibling = Pipe(3); sibling.WallThickness = siblingWall;
            records.Add(sibling);
        }
        if (withOrphanBend)
        {
            var orphan = Bend(4, "700"); orphan.WallThickness = "4";
            records.Add(orphan);
        }
        var builder = new SpecRowBuilder(new SpecBuilderOptions { IncludePipeBendsAsPipe = true, Topology = topo });
        return (builder.Build(records), builder);
    }

    [Fact] public void StrandedMetresGoToSiblingOfSameWall()
    {
        var (rows, _) = StrandedScenario(siblingWall: "4");
        Assert.Equal(1.5, Assert.Single(rows).Qty);
    }

    [Fact] public void StrandedMetresDoNotGoToSiblingOfDifferentWall()
    {
        var (rows, builder) = StrandedScenario(siblingWall: "8");
        Assert.Equal(1.0, Assert.Single(rows).Qty);
        Assert.Contains(builder.ReviewItems, i => i.Reason.StartsWith("Длину гиба не удалось отнести") && i.Qty == 0.5);
    }

    [Fact] public void StrandedMetresDoNotLandOnAnOrphanBendRow()
    {
        var (rows, builder) = StrandedScenario(siblingWall: null, withOrphanBend: true);
        var bendRow = Assert.Single(rows);
        Assert.Equal("Bend DN100", bendRow.Name);
        Assert.Equal(0.7, bendRow.Qty);   // its own 700 mm only
        Assert.Contains(builder.ReviewItems, i => i.Reason.StartsWith("Длину гиба не удалось отнести"));
    }

    // ---- consecutive bends fold into the run's pipe ------------------------

    [Fact] public void ConsecutiveBendsFoldThroughEachOther()
    {
        // pipe1 –w10– bend2 –w11– bend3 (bend3 has no pipe within one hop)
        var topo = new Dictionary<int, List<int>>
        {
            [1] = new() { 10 }, [10] = new() { 1, 2 }, [2] = new() { 10, 11 },
            [11] = new() { 2, 3 }, [3] = new() { 11 },
        };
        var builder = new SpecRowBuilder(new SpecBuilderOptions { IncludePipeBendsAsPipe = true, Topology = topo });
        var row = Assert.Single(builder.Build(new[] { Pipe(1), Bend(2), Bend(3), Weld(10), Weld(11) }));
        Assert.Equal("Pipe DN100", row.Name);
        Assert.Equal(2.0, row.Qty);
        Assert.DoesNotContain(builder.ReviewItems, i => i.Reason.StartsWith("Гиб не присоединён"));
    }

    [Fact] public void ChainedFoldStopsAtADifferentItem()
    {
        // bend3 –w11– bend2(other material) –w10– pipe1: bend3 must not pass
        // through a bend of another item to reach the pipe.
        var topo = new Dictionary<int, List<int>>
        {
            [1] = new() { 10 }, [10] = new() { 1, 2 }, [2] = new() { 10, 11 },
            [11] = new() { 2, 3 }, [3] = new() { 11 },
        };
        var other = Bend(2); other.Material = "Stainless";
        var fold = PipeBendFolder.Fold(new[] { Pipe(1), other, Bend(3), Weld(10), Weld(11) }, topo);
        Assert.DoesNotContain(3, fold.BendsConsumed);
        Assert.Contains(3, fold.OrphanBends);
    }

    // ---- writer: review-only document --------------------------------------

    [Fact] public void ReviewOnlyDocumentIsWritten()
    {
        var review = ReviewSection.BuildRows(new[] { new ReviewItem { Reason = "Деталь не прочитана (x)", PnPID = 7, DrawingPath = "A.dwg" } });
        var table = WriteAndRead(new List<SpecRow>(), review);
        Assert.Contains(table, r => r.Count > 0 && r[0] == SpecConstants.ReviewSectionHeader);
        Assert.DoesNotContain(table, r => r.Count == 9 && int.TryParse(r[0], out _));
    }

    // ---- settings: atomic save leaves no temp files -------------------------

    [Fact] public void SettingsSaveCreatesFileAndLeavesNoTemp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "spec-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "settings.json");
            var s = new PluginSettings { LastOutputDir = @"C:\Выход" };
            s.Save(path);   // directory and file do not exist yet
            s.Save(path);   // and replace an existing one
            Assert.Equal(@"C:\Выход", PluginSettings.Load(path).LastOutputDir);
            Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(dir).Select(Path.GetFileName).ToArray());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    // ---- helpers ------------------------------------------------------------

    static string MakeTemplate(string directory)
    {
        var template = Path.Combine(directory, "template.docx");
        TableCell Cell(string text) => new(new Paragraph(new Run(new Text(text))));
        using (var doc = WordprocessingDocument.Create(template, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var part = doc.AddMainDocumentPart();
            part.Document = new Document(new Body(new Table(
                new TableRow(Cell(SpecConstants.MaterialsSectionHeader)),
                new TableRow(Enumerable.Range(0, 9).Select(i => Cell(""))))));
            part.Document.Save();
        }
        return template;
    }

    static List<List<string>> WriteAndRead(IList<SpecRow> rows, IList<SpecRow> review, bool groupByLine = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "spec-r3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var output = Path.Combine(dir, "out.docx");
            SpecTemplateWriter.Write(MakeTemplate(dir), output, rows, groupByLine, review);
            using var doc = WordprocessingDocument.Open(output, false);
            return doc.MainDocumentPart.Document.Body.Descendants<TableRow>()
                .Select(tr => tr.Elements<TableCell>().Select(c => c.InnerText).ToList())
                .ToList();
        }
        finally { Directory.Delete(dir, true); }
    }
}
