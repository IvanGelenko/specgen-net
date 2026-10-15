using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PipingSpecPlugin.Settings;
using PipingSpecPlugin.Spec;
using PipingSpecPlugin.Update;
using Xunit;

namespace PipingSpecPlugin.Tests;

// Second audit round: one test (or a small group) per fix that is
// verifiable without a live AutoCAD.
public class AuditRound2Tests
{
    static PartRecord Pipe(int id, string length = "1000") => new()
    {
        PnPID = id, PartCategory = "Pipe", PnPClassName = "Pipe",
        PartSizeLongDesc = "Pipe DN100", NominalDiameter = "100", NominalUnit = "mm",
        Length = length, LengthUnit = "mm", Material = "Steel", LineNumberTag = "L1",
        Status = "New", LinearWeight = "10", WeightUnit = "KG", LinearWeightUnit = "KG/M"
    };

    // ---- 7: ProjectFacts counts connectors -------------------------------

    [Fact] public void ConnectorOnlyStatusAndNoLineGetPickerChips()
    {
        var weld = new PartRecord { PnPID = 2, PnPClassName = "Buttweld", Status = "Demolish" };
        var facts = ProjectFacts.From(new[] { Pipe(1), weld });
        Assert.Contains(facts.Statuses, s => s.key == "Demolish");
        Assert.Contains(facts.Lines, l => l.key == SpecConstants.NullLine && l.count == 1);
        // The chips really let the weld through when connectors are kept.
        var opts = new SpecBuilderOptions
        {
            ExcludeConnectors = false,
            Statuses = facts.Statuses.Select(s => s.key).ToList(),
            Lines = facts.Lines.Select(l => l.key).ToList(),
        };
        var rows = new SpecRowBuilder(opts).Build(new[] { Pipe(1), weld });
        Assert.Contains(rows, r => r.Category == "P3dConnector");
    }

    // ---- fresh audit: ON-mode orphan bend without length -----------------

    [Theory] [InlineData(null)] [InlineData("")] [InlineData("0")]
    public void OrphanBendWithoutLengthIsCountedInPiecesNotLost(string length)
    {
        var bend = Pipe(2); bend.PnPClassName = "PipeBend"; bend.PartSizeLongDesc = "Bend"; bend.Length = length;
        var opts = new SpecBuilderOptions { IncludePipeBendsAsPipe = true, Topology = new Dictionary<int, List<int>>() };
        var row = Assert.Single(new SpecRowBuilder(opts).Build(new[] { bend }));
        Assert.Equal("Fittings", row.Category);
        Assert.Equal("шт.", row.Unit);
        Assert.Equal(1, row.Qty);
    }

    // ---- 13: one decimal separator in the document -----------------------

    [Fact] public void NoteAndWeightWarningUseInvariantSeparatorUnderRussianCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            var a = Pipe(1, "12300");
            var b = Pipe(2, "1000"); b.LinearWeight = "12.5";
            var row = Assert.Single(new SpecRowBuilder().Build(new[] { a, b }));
            Assert.Contains("с учетом 10% запаса: 14.7 м", row.Note);
            Assert.Contains("разный вес 10..12.5 кг", row.Note);
            Assert.DoesNotContain(",", row.Note.Replace("; ", ""));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    // ---- 11: proximity radius in drawing units ---------------------------

    static (PartRecord[] parts, Dictionary<int, List<int>> graph) CompanionJoint(
        string gasketX, string catalogueUnit, double? mmPerUnit)
    {
        var valve = new PartRecord { PnPID = 1, PartCategory = "Valves" };
        var connector = new PartRecord { PnPID = 2, PnPClassName = "P3dConnector" };
        var flange = new PartRecord { PnPID = 3, PartCategory = "Flanges", NominalDiameter = "100",
            PositionX = "0", PositionY = "0", PositionZ = "0", LengthUnit = catalogueUnit, DrawingMmPerUnit = mmPerUnit };
        var gasket = new PartRecord { PnPID = 4, PartCategory = "Fasteners", ShortDescription = "gasket",
            NominalDiameter = "100", PositionX = gasketX, PositionY = "0", PositionZ = "0",
            LengthUnit = catalogueUnit, DrawingMmPerUnit = mmPerUnit };
        return (new[] { valve, connector, flange, gasket },
                new Dictionary<int, List<int>> { [1] = new() { 2 }, [2] = new() { 1, 3 } });
    }

    [Fact] public void FeetCatalogueInMillimetreDrawingStillFindsNearbyGasket()
    {
        // 50 drawing units = 50 mm. The old code multiplied by the catalogue
        // unit (ft → 304.8) and saw 15 m.
        var (parts, graph) = CompanionJoint("50", "ft", 1.0);
        Assert.Contains(4, ValveCompanionFinder.Discover(parts, graph));
    }

    [Fact] public void InchDrawingConvertsTheRadius()
    {
        // 3 in = 76.2 mm → inside 100 mm; 5 in = 127 mm → outside.
        var near = CompanionJoint("3", "mm", 25.4);
        Assert.Contains(4, ValveCompanionFinder.Discover(near.parts, near.graph));
        var far = CompanionJoint("5", "mm", 25.4);
        Assert.DoesNotContain(4, ValveCompanionFinder.Discover(far.parts, far.graph));
    }

    // ---- 12: template copy / read-only / locks ---------------------------

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

    static readonly SpecRow[] OneRow = { new SpecRow { Name = "Pipe", Category = "Pipe", Unit = "м", Qty = 1 } };

    static void InTempDir(Action<string> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "spec-r2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { body(dir); }
        finally
        {
            foreach (var f in Directory.GetFiles(dir)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }
    }

    [Fact] public void ReadOnlyTemplateIsAccepted()
    {
        InTempDir(dir =>
        {
            var template = MakeTemplate(dir);
            File.SetAttributes(template, FileAttributes.ReadOnly);
            var output = Path.Combine(dir, "out.docx");
            SpecTemplateWriter.Write(template, output, OneRow);
            Assert.True(File.Exists(output));
            Assert.Equal(0, (int)(File.GetAttributes(output) & FileAttributes.ReadOnly));
            Assert.Equal(2, Directory.GetFiles(dir).Length);   // no temp left
        });
    }

    [Fact] public void LockedTemplateIsReportedAsTemplate()
    {
        InTempDir(dir =>
        {
            var template = MakeTemplate(dir);
            using var hold = new FileStream(template, FileMode.Open, FileAccess.Read, FileShare.None);
            var ex = Assert.Throws<SpecFileLockedException>(() =>
                SpecTemplateWriter.Write(template, Path.Combine(dir, "out.docx"), OneRow));
            Assert.True(ex.IsTemplate);
        });
    }

    [Fact] public void LockedOutputIsReportedAsOutputAndTempIsCleanedUp()
    {
        InTempDir(dir =>
        {
            var template = MakeTemplate(dir);
            var output = Path.Combine(dir, "out.docx");
            File.WriteAllText(output, "previous");
            using (var hold = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var ex = Assert.Throws<SpecFileLockedException>(() =>
                    SpecTemplateWriter.Write(template, output, OneRow));
                Assert.False(ex.IsTemplate);
            }
            Assert.Equal("previous", File.ReadAllText(output));
            Assert.Equal(2, Directory.GetFiles(dir).Length);
        });
    }

    // ---- 17: settings / logs ---------------------------------------------

    [Fact] public void CorruptSettingsAreFlaggedAndPreservedOnSave()
    {
        InTempDir(dir =>
        {
            var path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "{ not json");
            var s = PluginSettings.Load(path);
            Assert.True(s.LoadFailed);
            s.Save(path);
            Assert.Equal("{ not json", File.ReadAllText(path + ".unreadable"));
            Assert.False(File.Exists(path + ".tmp"));
            var reloaded = PluginSettings.Load(path);
            Assert.False(reloaded.LoadFailed);
        });
    }

    [Fact] public void MissingSettingsFileIsNotAFailure()
    {
        InTempDir(dir =>
        {
            var s = PluginSettings.Load(Path.Combine(dir, "settings.json"));
            Assert.False(s.LoadFailed);
        });
    }

    [Fact] public void SaveReplacesExistingFileWithoutLeavingTemp()
    {
        InTempDir(dir =>
        {
            var path = Path.Combine(dir, "settings.json");
            new PluginSettings { LastOutputDir = "A" }.Save(path);
            new PluginSettings { LastOutputDir = "B" }.Save(path);
            Assert.Equal("B", PluginSettings.Load(path).LastOutputDir);
            Assert.Single(Directory.GetFiles(dir));
        });
    }

    [Theory]
    [InlineData("WRITESPEC", "last-run.log")]
    [InlineData("WRITESPECPROJ", "last-run.log")]
    [InlineData(null, "last-run.log")]
    [InlineData("SELECTBYLINE", "last-run-selectbyline.log")]
    [InlineData("MOVEBYTAG", "last-run-movebytag.log")]
    [InlineData("INSULSPEC", "last-run-insulspec.log")]
    [InlineData("INSULSPECPROJ", "last-run-insulspec.log")]
    [InlineData("INSULSPECDWG", "last-run-insulspec.log")]
    public void EachCommandFamilyHasItsOwnLog(string command, string expected)
        => Assert.Equal(expected, RunLogger.LogFileNames(command).last);

    // ---- 18: SYNCVT numeric comparison -----------------------------------

    [Theory]
    [InlineData("Pwork", "1,6", "1.6", true)]
    [InlineData("Pwork", "1,6", "1.60", true)]
    [InlineData("TestD", "2,0", "2", true)]
    [InlineData("Pwork", "1,6", "1.7", false)]
    [InlineData("Pwork", "1,6", "", false)]
    [InlineData("Номер", "001", "1", false)]          // identifier, not a number
    [InlineData("GostGroup", "1,0", "1", false)]      // text field stays textual
    [InlineData("Additional", "abc", " abc ", true)]
    public void SyncComparesNumbersByValue(string property, string vt, string current, bool same)
        => Assert.Equal(same, LineSyncService.SameFieldValue(property, vt, current));

    [Fact] public void PlanDoesNotReportSeparatorOnlyDifference()
    {
        var line = new LineGroupInfo { RowId = 1, Id = "1", Tag = "T" };
        line.AllProperties["Pwork"] = "1.6";
        var plan = LineSyncService.BuildPlan(
            new List<VtRowData> { new VtRowData { NN = "1", WorkPressure = "1,6" } },
            new List<LineGroupInfo> { line });
        Assert.DoesNotContain(plan.Actions.SelectMany(a => a.Changes), c => c.PropertyName == "Pwork");
    }

    // ---- 16: DrawingPartIds.Remap is idempotent --------------------------

    [Fact] public void RemapTwiceKeepsIdsAndSource()
    {
        var mapper = new DrawingPartIds();
        var rec = Pipe(7);
        var owned = new HashSet<int> { 7 };
        var graph = new Dictionary<int, List<int>> { [7] = new() { 8 } };
        mapper.Remap("A.dwg", new[] { rec }, owned, graph);
        var first = rec.PnPID;
        var firstOwned = owned.ToArray();
        var firstGraph = graph.ToDictionary(k => k.Key, v => v.Value.ToArray());
        mapper.Remap("A.dwg", new[] { rec }, owned, graph);
        Assert.Equal(first, rec.PnPID);
        Assert.Equal(7, rec.SourcePnPID);
        Assert.Equal(firstOwned, owned.ToArray());
        Assert.Equal(firstGraph.Keys, graph.Keys);
        Assert.Equal(firstGraph[first], graph[first].ToArray());
    }

    // ---- 19: installer digest --------------------------------------------

    [Fact] public void ReleaseDigestAndSizeAreParsed()
    {
        var hex = new string('a', 64);
        var json = "{\"tag_name\":\"v9.0.0\",\"assets\":[{\"name\":\"PipingSpecPlugin-Setup.exe\","
                 + "\"browser_download_url\":\"https://x/y.exe\",\"size\":123,\"digest\":\"sha256:" + hex.ToUpperInvariant() + "\"}]}";
        var info = UpdateChecker.Evaluate(json, "1.0.0");
        Assert.Equal(hex, info.InstallerSha256);
        Assert.Equal(123, info.InstallerSize);
    }

    [Theory] [InlineData(null)] [InlineData("md5:abc")] [InlineData("sha256:xyz")]
    public void UnknownDigestFormatIsIgnored(string digest)
        => Assert.Null(UpdateChecker.ParseSha256Digest(digest));

    [Fact] public void DownloadVerification()
    {
        var bytes = Encoding.UTF8.GetBytes("installer");
        var good = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.True(UpdateChecker.VerifyDownload(new MemoryStream(bytes), good, bytes.Length));
        Assert.True(UpdateChecker.VerifyDownload(new MemoryStream(bytes), null, null));
        Assert.False(UpdateChecker.VerifyDownload(new MemoryStream(bytes), new string('0', 64), null));
        Assert.False(UpdateChecker.VerifyDownload(new MemoryStream(bytes), good, bytes.Length + 1));
    }

    // ---- fresh audit: bend name normaliser leaves other "длина" alone -----

    [Theory]
    [InlineData("Изгиб трубы, длина прямого участка = 100.123 мм")]
    [InlineData("Изгиб трубы, длина L1 = 100.123 мм")]
    public void NormalizerDoesNotTouchOtherLengthFields(string text)
        => Assert.Equal(text, BendNameNormalizer.Normalize(text));
}
