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
