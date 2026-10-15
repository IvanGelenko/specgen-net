using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests;

/// <summary>
/// «Позиции, требующие проверки» — the trailing section that replaced the
/// old strict mode (one unreadable part used to stop the whole project).
/// </summary>
public class ReviewSectionTests
{
    static PartRecord Pipe(int id, string length = "1000") => new()
    {
        PnPID = id, SourcePnPID = id, PartCategory = "Pipe", PnPClassName = "Pipe",
        PartSizeLongDesc = "Труба 108x4", NominalDiameter = "100", NominalUnit = "mm",
        Length = length, LengthUnit = "mm", Material = "Steel", LineNumberTag = "L1",
        LinearWeight = "10", LinearWeightUnit = "kg/m", SourceDwgPath = @"C:\p\A.dwg",
    };
    static PartRecord Valve(int id, string weight = "5") => new()
    {
        PnPID = id, SourcePnPID = id, PartCategory = "Valves", PnPClassName = "Valve",
        PartSizeLongDesc = "Задвижка DN100", NominalDiameter = "100", NominalUnit = "mm",
        Weight = weight, LineNumberTag = "L1", SourceDwgPath = @"C:\p\B.dwg",
    };

    // ---- read-stage policy ------------------------------------------------

    [Fact] public void UnreadPartsNoLongerStopGeneration()
    {
        Assert.False(ReviewSection.ReadStopsGeneration(skippedRows: 5, workingDbSwapFailed: false, pnpReconnectFailed: false));
        Assert.True(ReviewSection.ReadStopsGeneration(0, false, pnpReconnectFailed: true));
        Assert.True(ReviewSection.ReadStopsGeneration(0, workingDbSwapFailed: true, false));
    }

    [Fact] public void UnreadPartsBecomeReviewRowsWithPnPIdAndDrawing()
    {
        var unread = ReviewSection.FromUnreadRows(new[] { (42, "NullReferenceException: x") }, @"C:\p\A.dwg").ToList();
        var builder = new SpecRowBuilder();
        var rows = builder.Build(new[] { Pipe(1) });
        var review = ReviewSection.BuildRows(unread.Concat(builder.ReviewItems));

        Assert.Single(rows);                    // the readable part is still specified
        var r = Assert.Single(review);
        Assert.Equal("PnPID 42", r.Name);
        Assert.Contains("NullReferenceException", r.Note);
        Assert.Contains("A.dwg", r.Note);
        Assert.Equal(0, r.Qty);                 // unknown → blank, never invented
    }

    // ---- builder collects doubts -------------------------------------------

    [Fact] public void ZeroLengthPipeIsCollected()
    {
        var b = new SpecRowBuilder();
        b.Build(new[] { Pipe(1), Pipe(2, "0") });
        var i = Assert.Single(b.ReviewItems);
        Assert.Equal(2, i.PnPID);
        Assert.Contains("нулевой", i.Reason);
        Assert.Equal("Труба 108x4", i.Description);
    }

    [Fact] public void UnknownLinearWeightUnitKeepsPipeAndFlagsMass()
    {
        var p = Pipe(1); p.LinearWeightUnit = "stone/yd";
        var b = new SpecRowBuilder();
        var row = Assert.Single(b.Build(new[] { p }));
        Assert.Equal(1, row.Qty);
        Assert.Equal(0, row.Weight);
        Assert.Contains(b.ReviewItems, i => i.Reason.Contains("погонной массы"));
        Assert.DoesNotContain(b.ReviewItems, i => i.Reason.StartsWith("Масса не задана"));
    }

    [Fact] public void MissingMassIsCollectedPerPart()
    {
        var b = new SpecRowBuilder();
        b.Build(new[] { Valve(1), Valve(2, null) });
        var i = Assert.Single(b.ReviewItems);
        Assert.Equal(2, i.PnPID);
        Assert.StartsWith("Масса не задана", i.Reason);
    }

    [Fact] public void EmptyNameAndReadWarningsAreCollected()
    {
        var v = Valve(1); v.PartSizeLongDesc = null;
        var w = Valve(2); w.ReadWarnings = new List<string> { "Деталь связана с несколькими группами линий (2) — номер линии взят из первой" };
        var b = new SpecRowBuilder();
        Assert.Equal(2, b.Build(new[] { v, w }).Count);   // both still specified
        Assert.Contains(b.ReviewItems, i => i.PnPID == 1 && i.Reason.Contains("наименование"));
        Assert.Contains(b.ReviewItems, i => i.PnPID == 2 && i.Reason.Contains("группами линий"));
    }

    [Fact] public void UnclassifiedPartIsCollectedButFilterExclusionsAreNot()
    {
        var odd = new PartRecord { PnPID = 1, PnPClassName = "Mystery", LineNumberTag = "L1" };
        var otherStatus = Valve(2); otherStatus.Status = "Existing";
        var otherLine = Valve(3); otherLine.LineNumberTag = "L2";
        var nozzle = new PartRecord { PnPID = 4, PartCategory = "Nozzles", PnPClassName = "FlangedNozzle" };
        var b = new SpecRowBuilder(new SpecBuilderOptions { Statuses = new[] { SpecConstants.NullStatus }, Lines = new[] { "L1" } });
        b.Build(new[] { odd, otherStatus, otherLine, nozzle });
        var i = Assert.Single(b.ReviewItems);
        Assert.Equal(1, i.PnPID);
        Assert.Contains("Mystery", i.Description);
    }

    [Fact] public void StrandedBendMetresAreCollectedWithLength()
    {
        // Bend folds into pipe 1, but pipe 1 is on an unselected status;
        // no same-bore sibling → metres stranded.
        var pipe = Pipe(1); pipe.Status = "Existing";
        var bend = new PartRecord
        {
            PnPID = 2, SourcePnPID = 2, PartCategory = "Pipe", PnPClassName = "PipeBend",
            PartSizeLongDesc = "Гиб 108", NominalDiameter = "100", NominalUnit = "mm",
            Length = "500", LengthUnit = "mm", Material = "Steel", LineNumberTag = "L1",
            Status = "New", SourceDwgPath = @"C:\p\A.dwg",
        };
        var topo = new Dictionary<int, List<int>> { [1] = new() { 2 }, [2] = new() { 1 } };
        // Legacy precomputed fold: the stranding happens inside Build().
        var b = new SpecRowBuilder(new SpecBuilderOptions
        {
            Statuses = new[] { "New" },
            BendsConsumed = new HashSet<int> { 2 },
            ExtraPipeMetres = new Dictionary<int, double> { [1] = 0.5 },
            ExtraPipeLineTags = new Dictionary<int, string> { [1] = "L1" },
            Topology = topo,
        });
        Assert.Empty(b.Build(new[] { pipe, bend }));
        var i = Assert.Single(b.ReviewItems);
        Assert.Equal(0.5, i.Qty);
        Assert.Equal("м", i.Unit);
    }

    // ---- grouping -----------------------------------------------------------

    [Fact] public void IdenticalProblemsGroupIntoOneRowWithCount()
    {
        var b = new SpecRowBuilder();
        b.Build(Enumerable.Range(1, 30).Select(id => Pipe(id, "0")).ToList());
        var row = Assert.Single(ReviewSection.BuildRows(b.ReviewItems));
        Assert.Equal("Труба 108x4", row.Name);
        Assert.Contains("деталей: 30", row.Note);
        Assert.Contains("и ещё 20", row.Note);   // PnPID list is capped
        Assert.Equal(SpecConstants.ReviewSectionHeader, row.Category);
    }

    [Fact] public void ReviewItemsDoNotLeakIntoMainRows()
    {
        var b = new SpecRowBuilder();
        var rows = b.Build(new[] { Pipe(1), Pipe(2, "0"), new PartRecord { PnPID = 3, PnPClassName = "BoltSet", PartSizeLongDesc = "Болты" } });
        var main = Assert.Single(rows);
        Assert.Equal(1, main.Qty);               // only the good 1 m pipe
        Assert.Equal(2, b.ReviewItems.Count);
        Assert.DoesNotContain(rows, r => r.Category == SpecConstants.ReviewSectionHeader);
    }

    // ---- docx --------------------------------------------------------------

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
        var dir = Path.Combine(Path.GetTempPath(), "spec-review-" + Guid.NewGuid().ToString("N"));
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

    [Fact] public void ReviewSectionIsWrittenLastWithoutPositionNumbers()
    {
        var main = new List<SpecRow>
        {
            new() { Category = "Pipe", Name = "Труба", Unit = "м", Qty = 1, LineTag = "L1" },
            new() { Category = "Valves", Name = "Задвижка", Unit = "шт.", Qty = 2, LineTag = "L2" },
        };
        var review = ReviewSection.BuildRows(new[]
        {
            new ReviewItem { Reason = "Деталь не прочитана (x)", PnPID = 7, DrawingPath = @"C:\p\A.dwg" },
            new ReviewItem { Reason = "Длину гиба не удалось отнести", Description = "Гиб", Qty = 0.5, Unit = "м" },
        });
        var table = WriteAndRead(main, review, groupByLine: true);
        var texts = table.Select(r => r[0]).ToList();

        var hdr = texts.IndexOf(SpecConstants.ReviewSectionHeader);
        Assert.True(hdr > 0);
        Assert.Equal(table.Count - 3, hdr);             // header + 2 rows at the very end
        var unread = table[hdr + 1];
        Assert.Equal("", unread[0]);                    // no position number
        Assert.Equal("PnPID 7", unread[1]);
        Assert.Equal("", unread[6]);                    // unknown qty stays blank
        Assert.Contains("A.dwg", unread[8]);
        var bend = table[hdr + 2];
        Assert.Equal("0.5", bend[6]);
        Assert.Equal("м", bend[5]);
        // Main numbering is untouched by the review rows: 1, 2 and nothing more.
        Assert.Equal(new[] { "1", "2" }, texts.Where(t => int.TryParse(t, out _)).ToArray());
    }

    [Fact] public void NoDoubtsNoSection()
    {
        var b = new SpecRowBuilder();
        var rows = b.Build(new[] { Pipe(1) });
        var review = ReviewSection.BuildRows(b.ReviewItems);
        Assert.Empty(review);
        var table = WriteAndRead(rows, review);
        Assert.DoesNotContain(table, r => r.Contains(SpecConstants.ReviewSectionHeader));
    }
}
