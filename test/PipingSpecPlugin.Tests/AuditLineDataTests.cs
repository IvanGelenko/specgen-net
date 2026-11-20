using System;
using System.Collections.Generic;
using System.Linq;
using PipingSpecPlugin.Audit;
using Xunit;

namespace PipingSpecPlugin.Tests;

// Slope, status, placeholder spec and valve identifiers (shared with the
// Navisworks audit).
public class AuditLineDataTests
{
    static List<AuditIssue> Run(AuditSnapshot snapshot, string rule, HashSet<string> lines = null)
    {
        var options = new AuditOptions { SelectedLines = lines };
        options.Enabled.Add(rule);
        return AuditRules.Run(snapshot, options);
    }

    static AuditSnapshot Of(params AuditPart[] parts)
    {
        var snapshot = new AuditSnapshot();
        snapshot.Parts.AddRange(parts);
        return snapshot;
    }

    static AuditPart Part(string key, string cls, string line = "L1") =>
        new() { Key = key, ClassName = cls, LineKey = line, LineLabel = line ?? AuditOptions.NoLineLabel, ConnectionsRead = true };

    // A pipe 1000 long in plan along X with the given fall.
    static AuditPart Pipe(string key, double fall, string line = "L1", double x = 0)
    {
        var pipe = Part(key, "Pipe", line);
        pipe.Axis = new List<double[]> { new[] { x, 0, 0 }, new[] { x + 1000, 0, -fall } };
        return pipe;
    }

    // ---- slope -------------------------------------------------------------

    [Fact] public void OneSlopeOrNoneOnTheLinePasses()
    {
        Assert.Empty(Run(Of(Pipe("A", 2), Pipe("B", 2), Pipe("C", -2.05)), "slope"));   // i = 0,002 both ways
        Assert.Empty(Run(Of(Pipe("A", 0), Pipe("B", 0.05)), "slope"));                  // level
    }

    [Fact] public void ALevelPipeOnASlopedLineIsNamed()
    {
        var issue = Assert.Single(Run(Of(Pipe("A", 2), Pipe("B", 2), Pipe("C", 0)), "slope"));
        Assert.Equal("Несоответствие", issue.Severity);
        Assert.Equal(new[] { "C" }, issue.Keys);
        Assert.Contains("Уклон линии — i = 0,002 (2 труб)", issue.Message);
        Assert.Contains("без уклона", issue.Message);
    }

    [Fact] public void DifferentSlopesAreNamed()
    {
        var issue = Assert.Single(Run(Of(Pipe("A", 3), Pipe("B", 3), Pipe("C", 5)), "slope"));
        Assert.Equal(new[] { "C" }, issue.Keys);
        Assert.Contains("i = 0,005", issue.Message);
    }

    [Fact] public void UniformSlopeDifferingFromStandard2MmPerMeterIsReported()
    {
        var issue = Assert.Single(Run(Of(Pipe("A", 5), Pipe("B", 5), Pipe("C", 5)), "slope"));
        Assert.Equal("Несоответствие", issue.Severity);
        Assert.Equal(new[] { "A", "B", "C" }, issue.Keys);
        Assert.Contains("0,005", issue.Message);
        Assert.Contains("0,002", issue.Message);
    }

    [Fact] public void SlopeOnPartOfLineReportsAllLevelPipes()
    {
        var issue = Assert.Single(Run(Of(Pipe("S", 2), Pipe("L1", 0), Pipe("L2", 0), Pipe("L3", 0)), "slope"));
        Assert.Equal("Несоответствие", issue.Severity);
        Assert.Equal(new[] { "L1", "L2", "L3" }, issue.Keys.OrderBy(k => k).ToArray());
        Assert.Contains("без уклона", issue.Message);
        Assert.Contains("0,002", issue.Message);
    }

    [Fact] public void VerticalPipesStepRunsAndOtherLinesAreLeftOut()
    {
        var riser = Part("V", "Pipe"); riser.Axis = new List<double[]> { new[] { 0.0, 0, 0 }, new[] { 0.0, 0, 3000 } };
        var steep = Pipe("S", 500);                       // 1:2 — a run at an angle, not a slope
        var bend = Part("BEND", "PipeBend"); bend.Axis = new List<double[]> { new[] { 0.0, 0, 0 }, new[] { 1000.0, 0, 0 } };
        Assert.Empty(Run(Of(Pipe("A", 2), Pipe("B", 2), riser, steep, bend, Pipe("X", 0, "L2")), "slope"));
    }

    [Fact] public void PortPositionsStandInForTheAxisAndUnknownEndsAreNotChecked()
    {
        var a = Part("A", "Pipe");
        a.Ports.Add(new AuditPort { Name = "S1", Position = new[] { 0.0, 0, 10 } });
        a.Ports.Add(new AuditPort { Name = "S2", Position = new[] { 0.0, 1000, 8 } });
        var b = Part("B", "Pipe");                         // no positions
        var issues = Run(Of(a, Pipe("C", 2), b), "slope");
        var unknown = Assert.Single(issues);
        Assert.Equal("Не проверено", unknown.Severity);
        Assert.Equal(new[] { "B" }, unknown.Keys);
    }

    [Fact] public void SlopeLabelIsRussian() => Assert.Equal("i = 0,002", SlopeRule.Label(0.002));

    [Fact] public void APipeWithAStubInIsMeasuredBetweenItsEnds()
    {
        // audit-model.dwg: a pipe with a branch has a third port on its axis.
        var tapped = Part("T", "Pipe");
        tapped.Ports.Add(new AuditPort { Name = "S1", Position = new[] { 0.0, 0, 0 } });
        tapped.Ports.Add(new AuditPort { Name = "S3", Position = new[] { 400.0, 0, 0 } });
        tapped.Ports.Add(new AuditPort { Name = "S2", Position = new[] { 1000.0, 0, 0 } });
        Assert.Equal(0, SlopeRule.SlopeOf(tapped, out var known));
        Assert.True(known);
        Assert.Empty(Run(Of(tapped, Pipe("A", 0)), "slope"));
    }

    // ---- pipe length -------------------------------------------------------

    static AuditSnapshot Mm(params AuditPart[] parts)
    {
        var snapshot = Of(parts);
        snapshot.DrawingMmPerUnit = 1;
        return snapshot;
    }

    static AuditPart Straight(string key, double length, string line = "L1")
    {
        var pipe = Part(key, "Pipe", line);
        pipe.Axis = new List<double[]> { new[] { 0.0, 0, 0 }, new[] { length, 0, 0 } };
        return pipe;
    }

    [Fact] public void APipeShorterThan100MmIsNamed()
    {
        var issue = Assert.Single(Run(Mm(Straight("A", 60), Straight("B", 100), Straight("C", 2500)), "pipelength"));
        Assert.Equal("Несоответствие", issue.Severity);
        Assert.Equal(new[] { "A" }, issue.Keys);
        Assert.Contains("длина 60 мм", issue.Message);
        Assert.Equal(40, issue.Magnitude);
    }

    [Fact] public void PipeLengthNeedsTheDrawingUnits()
    {
        var snapshot = Of(Straight("A", 60));       // INSUNITS unknown
        Assert.Equal("Не проверено", Assert.Single(Run(snapshot, "pipelength")).Severity);
        var inches = Of(Straight("B", 3)); inches.DrawingMmPerUnit = 25.4;   // 76.2 mm
        Assert.Single(Run(inches, "pipelength"));
    }

    // ---- pipe XY offset (multiples of 15 degrees) ---------------------------

    static AuditPart PipeAt(string key, double[] p1, double[] p2, string line = "L1")
    {
        var pipe = Part(key, "Pipe", line);
        pipe.Axis = new List<double[]> { p1, p2 };
        return pipe;
    }

    [Fact] public void PipeOffsetStandardAnglesAndOrthogonalPass()
    {
        double Rad(double deg) => deg * Math.PI / 180.0;
        var pX = PipeAt("X", new[] { 0.0, 0, 0 }, new[] { 2000.0, 0, 0 });
        var pY = PipeAt("Y", new[] { 0.0, 0, 0 }, new[] { 0.0, 3000, 0 });
        var pZ = PipeAt("Z", new[] { 0.0, 0, 0 }, new[] { 0.0, 0, 2500 });
        var p15 = PipeAt("P15", new[] { 0.0, 0, 0 }, new[] { 1000 * Math.Cos(Rad(15)), 1000 * Math.Sin(Rad(15)), 0 });
        var p30 = PipeAt("P30", new[] { 0.0, 0, 0 }, new[] { 1000 * Math.Cos(Rad(30)), 1000 * Math.Sin(Rad(30)), 0 });
        var p45 = PipeAt("P45", new[] { 0.0, 0, 0 }, new[] { 1000.0, 1000, 0 });
        var p60 = PipeAt("P60", new[] { 0.0, 0, 0 }, new[] { 1000 * Math.Cos(Rad(60)), 1000 * Math.Sin(Rad(60)), 0 });
        var p75 = PipeAt("P75", new[] { 0.0, 0, 0 }, new[] { 1000 * Math.Cos(Rad(75)), 1000 * Math.Sin(Rad(75)), 0 });
        var pSlope = PipeAt("SLOPE", new[] { 0.0, 0, 0 }, new[] { 5000.0, 0, -10 }); // horizontal in plan, sloped in Z
        Assert.Empty(Run(Mm(pX, pY, pZ, p15, p30, p45, p60, p75, pSlope), "pipeoffset"));
    }

    [Fact] public void PipeOffsetHorizontalSkewIsReported()
    {
        var skewed = PipeAt("SKEW", new[] { 0.0, 0, 0 }, new[] { 3000.0, 3, 0 });
        var issue = Assert.Single(Run(Mm(skewed), "pipeoffset"));
        Assert.Equal("Несоответствие", issue.Severity);
        Assert.Equal(new[] { "SKEW" }, issue.Keys);
        Assert.True(issue.Magnitude >= 2.99 && issue.Magnitude <= 3.01);
        Assert.Contains("смещение по XY", issue.Message);
    }

    [Fact] public void PipeOffsetVerticalRiserSkewIsReported()
    {
        var riserSkew = PipeAt("RISER", new[] { 0.0, 0, 0 }, new[] { 2.0, 1, 3000 });
        var issue = Assert.Single(Run(Mm(riserSkew), "pipeoffset"));
        Assert.Equal("Несоответствие", issue.Severity);
        Assert.Equal(new[] { "RISER" }, issue.Keys);
        Assert.True(issue.Magnitude >= 2.23 && issue.Magnitude <= 2.25);
        Assert.Contains("стояк смещён в плане XY", issue.Message);
    }

    [Fact] public void PipeOffsetArbitraryAngleIsReported()
    {
        double rad = 12.0 * Math.PI / 180.0;
        var p12 = PipeAt("P12", new[] { 0.0, 0, 0 }, new[] { 1000 * Math.Cos(rad), 1000 * Math.Sin(rad), 0 });
        var issue = Assert.Single(Run(Mm(p12), "pipeoffset"));
        Assert.Equal("Несоответствие", issue.Severity);
        Assert.Equal(new[] { "P12" }, issue.Keys);
        Assert.Contains("12", issue.Message);
    }

    [Fact] public void PipeOffsetFloatingPointNoiseIsIgnored()
    {
        var noise = PipeAt("NOISE", new[] { 0.0, 0, 0 }, new[] { 3000.0, 1e-6, 0 });
        Assert.Empty(Run(Mm(noise), "pipeoffset"));
    }

    [Fact] public void PipeOffsetNeedsDrawingUnits()
    {
        var uncalibrated = Of(PipeAt("A", new[] { 0.0, 0, 0 }, new[] { 3000.0, 5, 0 }));
        Assert.Equal("Не проверено", Assert.Single(Run(uncalibrated, "pipeoffset")).Severity);
    }

    // ---- weld spacing ------------------------------------------------------

    static AuditPart Weld(string key, double x, params AuditPart[] joins)
    {
        var weld = Part(key, "P3dConnector", null); weld.IsConnector = true;
        weld.SubParts.Add(new AuditSubPart { ClassName = "Buttweld", Description = "Buttweld" });
        weld.Position = new[] { x, 0, 0 };
        foreach (var part in joins) { weld.Neighbours.Add(part.Key); part.Neighbours.Add(weld.Key); }
        return weld;
    }

    [Fact] public void WeldsOnAShortPipeAreTooClose()
    {
        var pipe = Straight("P", 80);
        var issue = Assert.Single(Run(Mm(pipe, Weld("W1", 0, pipe), Weld("W2", 80, pipe)), "welds"));
        Assert.Equal("Несоответствие", issue.Severity);
        Assert.Equal(new[] { "W1", "P", "W2" }, issue.Keys);
        Assert.Contains("80 мм между центрами", issue.Message);
        Assert.Equal("L1", issue.Line);
    }

    [Fact] public void AStubInNextToAButtWeldIsTooClose()
    {
        var header = Straight("H", 3000);
        var tap = Weld("T", 60, header); tap.SubParts[0].ClassName = "TapWeld";
        var issues = Run(Mm(header, Weld("W1", 0, header), tap, Weld("W2", 3000, header)), "welds");
        Assert.Equal(new[] { "H", "T", "W1" }, Assert.Single(issues).Keys.OrderBy(k => k));
    }

    [Fact] public void WeldsAtTheEndsOfAFittingAreNotChecked()
    {
        // A DN 50 tee: its end welds 67 mm apart by the factory (0873 model).
        var tee = Part("T", "Tee");
        Assert.Empty(Run(Mm(tee, Weld("W1", 0, tee), Weld("W2", 67, tee), Weld("W3", 34, tee)), "welds"));
    }

    [Fact] public void FlangedJointsAreNotWeldsAndFarWeldsPass()
    {
        var pipe = Straight("P", 80);
        var flanged = Weld("F", 80, pipe); flanged.SubParts.Clear();
        flanged.SubParts.Add(new AuditSubPart { ClassName = "Gasket", Description = "Прокладка" });
        Assert.Empty(Run(Mm(pipe, Weld("W1", 0, pipe), flanged), "welds"));
        var longPipe = Straight("L", 500);
        Assert.Empty(Run(Mm(longPipe, Weld("A", 0, longPipe), Weld("B", 500, longPipe)), "welds"));
    }

    // ---- status ------------------------------------------------------------

    [Fact] public void OneStatusOnTheLinePassesWhateverTheLanguage()
    {
        var a = Part("A", "Pipe"); a.Status = "New";
        var b = Part("B", "Valve"); b.Status = "Новое";
        var c = Part("C", "Support"); c.Status = " новое ";
        Assert.Empty(Run(Of(a, b, c), "status"));
    }

    [Fact] public void TheOddStatusIsNamedAndConnectorsJoinTheirLine()
    {
        var a = Part("A", "Pipe"); a.Status = "Существующее";
        var b = Part("B", "Pipe"); b.Status = "Existing";
        var weld = Part("W", "P3dConnector", null); weld.IsConnector = true;
        weld.SubParts.Add(new AuditSubPart { Description = "Сварной шов", Status = "Новое" });
        weld.Neighbours.Add("A"); weld.Neighbours.Add("B");
        var issue = Assert.Single(Run(Of(a, b, weld), "status", new HashSet<string> { "L1" }));
        Assert.Equal(new[] { "W" }, issue.Keys);
        Assert.Contains("Статус линии — Существующее (2 эл.)", issue.Message);
        Assert.Equal("L1", issue.Line);
    }

    [Fact] public void ABlankStatusIsNotAnotherStatus()
    {
        // Navisworks 0873: supports without a status on an «Существующее» line.
        var a = Part("A", "Pipe"); a.Status = "Существующее";
        var b = Part("B", "Pipe"); b.Status = "Существующее";
        var s = Part("S", "Support");
        var issue = Assert.Single(Run(Of(a, b, s), "status"));
        Assert.Equal("Не проверено", issue.Severity);
        Assert.Equal(new[] { "S" }, issue.Keys);
    }

    [Theory]
    [InlineData("New", "Новое")]
    [InlineData("Existing", "существующее")]
    [InlineData("Demolish", "Демонтируемое")]
    public void StatusesMatchAcrossLanguages(string a, string b) => Assert.Equal(StatusRule.Normalize(a), StatusRule.Normalize(b));

    // ---- placeholder spec --------------------------------------------------

    [Fact] public void PlaceholderPartsAndFastenersAreNamedPerLine()
    {
        // 0873 «Врезки 8 9 10»: a tee and flanged-joint bolts from «PlaceHolder Metric».
        var tee = Part("T", "Tee"); tee.Spec = "PlaceHolder Metric";
        var pipe = Part("P", "Pipe"); pipe.Spec = "PN016_RUS";
        var joint = Part("J", "P3dConnector", null); joint.IsConnector = true; joint.Neighbours.Add("P");
        joint.SubParts.Add(new AuditSubPart { Description = "Bolt set M16", Spec = "PlaceHolder Metric" });
        joint.SubParts.Add(new AuditSubPart { Description = "Прокладка", Spec = "PN016_RUS" });
        var issue = Assert.Single(Run(Of(tee, pipe, joint), "placeholder", new HashSet<string> { "L1" }));
        Assert.Equal(new[] { "T", "J" }, issue.Keys);
        Assert.Contains("Bolt set M16 — «PlaceHolder Metric»", issue.Message);
        Assert.Equal(2, issue.Magnitude);
    }

    [Fact] public void AJointsPlaceholderGasketAndBoltsAreOneEntry()
    {
        var pipe = Part("P", "Pipe");
        var joint = Part("J", "P3dConnector", null); joint.IsConnector = true; joint.Neighbours.Add("P");
        joint.SubParts.Add(new AuditSubPart { Description = "PH METRIC Gasket ND 25", Spec = "PlaceHolder Metric" });
        joint.SubParts.Add(new AuditSubPart { Description = "PH METRIC Stud Bolt M12x70", Spec = "PlaceHolder Metric" });
        var issue = Assert.Single(Run(Of(pipe, joint), "placeholder"));
        Assert.Contains("соединение [J]: PH METRIC Gasket ND 25, PH METRIC Stud Bolt M12x70 — «PlaceHolder Metric»", issue.Message);
        Assert.Equal(1, issue.Magnitude);
    }

    [Theory]
    [InlineData("PlaceHolder Metric", true)]
    [InlineData("Placeholder Imperial", true)]
    [InlineData("PN016_RUS", false)]
    [InlineData(null, false)]
    public void PlaceholderSpecIsRecognised(string spec, bool placeholder) => Assert.Equal(placeholder, PlaceholderSpecRule.IsPlaceholder(spec));

    // ---- valve identifiers -------------------------------------------------

    [Fact] public void ValvesWithoutAnIdentifierAreNamed()
    {
        var a = Part("A", "Valve"); a.Tag = "GV-012";
        var b = Part("B", "Valve");                 // no Tag at all
        var c = Part("C", "GateValve"); c.Tag = "?-?";
        var d = Part("D", "Valve"); d.Tag = "GV-?";
        var pipe = Part("P", "Pipe");
        var issues = Run(Of(a, b, c, d, pipe), "tags");
        var missing = Assert.Single(issues, i => i.Severity == "Несоответствие");
        Assert.Equal(new[] { "B", "C" }, missing.Keys);
        Assert.Contains("(«?-?»)", missing.Message);
        var partial = Assert.Single(issues, i => i.Severity == "Проверить");
        Assert.Equal(new[] { "D" }, partial.Keys);
    }

    [Theory]
    [InlineData(null, true)] [InlineData("", true)] [InlineData("?-?-?", true)] [InlineData(" - ", true)]
    [InlineData("GV-012", false)] [InlineData("GV-?", false)]
    public void MissingTagIsRecognised(string tag, bool missing) => Assert.Equal(missing, ValveTagRule.Missing(tag));
}
