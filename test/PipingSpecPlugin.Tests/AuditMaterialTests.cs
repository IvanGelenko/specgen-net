using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PipingSpecPlugin.Audit;
using Xunit;

namespace PipingSpecPlugin.Tests;

// MaterialRule and the collision tolerance (1.3.31).
public class AuditMaterialTests
{
    static AuditPart Part(string key, string className, string material, string line = "L1", string code = null) =>
        new() { Key = key, ClassName = className, Material = material, MaterialCode = code, LineKey = line, LineLabel = line };

    static List<AuditIssue> Run(AuditSnapshot snapshot, HashSet<string> lines = null)
    {
        var options = new AuditOptions { SelectedLines = lines };
        options.Enabled.Add("material");
        return AuditRules.Run(snapshot, options);
    }

    static AuditSnapshot Of(params AuditPart[] parts)
    {
        var snapshot = new AuditSnapshot();
        snapshot.Parts.AddRange(parts);
        return snapshot;
    }

    [Fact] public void OneMaterialOnTheLinePasses()
        => Assert.Empty(Run(Of(Part("1", "Pipe", "09Г2С"), Part("2", "Elbow", "09Г2С"), Part("3", "Flange", "09г2с"))));

    [Fact] public void TheOddPartIsNamedAgainstTheLinesMaterial()
    {
        // 150-P22002/2-LS025C-HN in audit-model.dwg: 09Г2С and 20 on one line.
        var issue = Assert.Single(Run(Of(Part("1", "Pipe", "09Г2С"), Part("2", "Pipe", "09Г2С"), Part("3", "Elbow", "20"))));
        Assert.Equal("Несоответствие", issue.Severity);
        Assert.Contains("Материал линии — 09Г2С (2 дет.)", issue.Message);
        Assert.Equal(new[] { "3" }, issue.Keys);
    }

    [Fact] public void WithoutAMajorityAllPartsAreListed()
    {
        var issue = Assert.Single(Run(Of(Part("1", "Pipe", "09Г2С"), Part("2", "Tee", "20"))));
        Assert.Equal(new[] { "1", "2" }, issue.Keys.OrderBy(k => k));
    }

    [Theory]
    [InlineData("Сталь 20", "20")]
    [InlineData("сталь  20", "20")]
    [InlineData("09Г2C", "09Г2С")]          // Latin C in a Russian grade
    [InlineData(" 12х18н10т ", "12Х18Н10Т")]
    [InlineData("AISI 321", "AISI321")]     // no Cyrillic: Latin kept
    [InlineData("20 ГОСТ 1050-2013", "20")]  // the standard is not the grade
    [InlineData("Сталь 09Г2С ГОСТ 19281-2014", "09Г2С")]
    [InlineData("Ст.20", "20")]               // quality steel 20
    [InlineData("Ст20", "20")]
    [InlineData("Ст3сп", "СТ3СП")]            // ordinary-quality grade kept
    [InlineData("ВСт3пс", "ВСТ3ПС")]
    public void MaterialsAreNormalised(string raw, string expected)
        => Assert.Equal(expected, MaterialRule.Normalize(raw));

    [Fact] public void Steel20WrittenTwoWaysIsOneMaterial()
        => Assert.Empty(Run(Of(Part("1", "Pipe", "Сталь 20"), Part("2", "Pipe", "20"))));

    [Fact] public void ValvesSupportsAndConnectorsAreNotCompared()
    {
        var connector = Part("4", "P3dConnector", "");
        connector.IsConnector = true;
        Assert.Empty(Run(Of(Part("1", "Pipe", "09Г2С"), Part("2", "Valve", "хладостойкая сталь"), Part("3", "Support", "ВСт3пс"), connector,
            Part("5", "GateValve", "Ст20"))));
    }

    [Fact] public void MaterialCodeStandsInForABlankMaterial()
    {
        Assert.Empty(Run(Of(Part("1", "Pipe", "09Г2С"), Part("2", "Flange", "", code: "09Г2С"))));
        Assert.Single(Run(Of(Part("1", "Pipe", "09Г2С"), Part("2", "Flange", "", code: "20"))));
    }

    [Fact] public void BlankMaterialIsNotChecked()
    {
        var issue = Assert.Single(Run(Of(Part("1", "Pipe", "09Г2С"), Part("2", "Cap", ""))));
        Assert.Equal("Не проверено", issue.Severity);
        Assert.Equal(new[] { "2" }, issue.Keys);
    }

    [Fact] public void LinesAreComparedSeparatelyAndPartsWithoutALineAreSkipped()
    {
        var snapshot = Of(Part("1", "Pipe", "09Г2С", "L1"), Part("2", "Pipe", "20", "L2"), Part("3", "Pipe", "12Х18Н10Т", null));
        Assert.Empty(Run(snapshot));
        snapshot.Parts.Add(Part("4", "Elbow", "20", "L1"));
        snapshot.Parts.Add(Part("5", "Elbow", "09Г2С", "L1"));
        Assert.Single(Run(snapshot));
        Assert.Empty(Run(snapshot, new HashSet<string> { "L2" }));
    }

    [Theory]
    [InlineData("2", true, 2.0)]
    [InlineData("0,5", true, 0.5)]
    [InlineData(" 0.5 ", true, 0.5)]
    [InlineData("", true, 0.0)]
    [InlineData("-1", false, 0.0)]
    [InlineData("abc", false, 0.0)]
    public void ToleranceIsParsed(string text, bool ok, double mm)
    {
        Assert.Equal(ok, AuditOptions.TryParseTolerance(text, out var value));
        if (ok) Assert.Equal(mm, value, 6);
    }

    [Fact] public void IssuesAreGroupedByCheckAndTheWorstComeFirst()
    {
        static AuditIssue I(string rule, string severity, double? magnitude, string line = "L1") =>
            new() { RuleId = rule, Severity = severity, Magnitude = magnitude, Line = line, Message = rule + magnitude };
        var ordered = AuditRules.Order(new[]
        {
            I("read", "Не проверено", null), I("connections", "Проверить", 2), I("wall", "Несоответствие", 1),
            I("collisions", "Пересечение", 0.7), I("collisions", "Не проверено", null), I("collisions", "Пересечение", 45),
            I("bolts", "Проверить", 5.4), I("bolts", "Несоответствие", 1.2), I("material", "Несоответствие", 3),
            I("collisions", "Пересечение", 5.5),
        });
        Assert.Equal(new[] { "collisions", "collisions", "collisions", "collisions", "bolts", "bolts", "wall", "material", "connections", "read" },
            ordered.Select(i => i.RuleId));
        // Clashes by depth, deepest first; the unchecked pair after them.
        Assert.Equal(new double?[] { 45, 5.5, 0.7, null }, ordered.Take(4).Select(i => i.Magnitude));
        // A violation before «Проверить», whatever the size.
        Assert.Equal(new[] { "Несоответствие", "Проверить" }, ordered.Skip(4).Take(2).Select(i => i.Severity));
        Assert.Equal("Коллизии с другими элементами", ordered[0].Group);
        Assert.Equal("Чтение данных", ordered[^1].Group);
    }

    [Fact] public void RunReturnsTheOrderedList()
    {
        var snapshot = new AuditSnapshot();
        snapshot.GeometryIssues.Add(new AuditIssue { RuleId = "collisions", Severity = "Пересечение", Magnitude = 1 });
        snapshot.GeometryIssues.Add(new AuditIssue { RuleId = "collisions", Severity = "Пересечение", Magnitude = 9 });
        snapshot.Parts.AddRange(new[] { Part("1", "Pipe", "09Г2С"), Part("2", "Pipe", "09Г2С"), Part("3", "Pipe", "20") });
        var options = new AuditOptions();
        options.Enabled.UnionWith(new[] { "material", "collisions" });
        var issues = AuditRules.Run(snapshot, options);
        Assert.Equal(new double?[] { 9, 1, 1 }, issues.Select(i => i.Magnitude));
        Assert.Equal("material", issues[^1].RuleId);
    }

    [Fact] public void PipesWithoutALineNumberAreOneRemarkNotOnePerPipe()
    {
        var snapshot = new AuditSnapshot();
        for (int i = 0; i < 5; i++)
            snapshot.Parts.Add(new AuditPart { Key = "N" + i, ClassName = "Pipe", IsPipeOrElbow = true, Diameter = "50", WallRaw = "4", WallMm = 4, LineLabel = AuditOptions.NoLineLabel });
        var options = new AuditOptions();
        options.Enabled.Add("wall");
        var issue = Assert.Single(AuditRules.Run(snapshot, options));
        Assert.Equal("Не проверено", issue.Severity);
        Assert.Equal(5, issue.Keys.Count);
        Assert.Contains("5 труб / отводов", issue.Message);
    }

    // ---- structures with each other ----------------------------------------

    [Fact] public void BeamRunningIntoAColumnAtItsNodeIsTheFrame()
    {
        // Column 200×200 around x = 0; beam axis from the column axis to x = 6000.
        var overlap = new[] { -100.0, -100, 2900, 100, 100, 3100 };
        var beamEnds = new[] { new[] { 0.0, 0, 3000 }, new[] { 6000.0, 0, 3000 } };
        Assert.True(StructureJointPolicy.IsNode(overlap, beamEnds, 1));
        // Trimmed to the column face: the end lies on the region's face.
        Assert.True(StructureJointPolicy.IsNode(overlap, new[] { new[] { 100.5, 0, 3000 } }, 1));
    }

    [Fact] public void MemberPassingThroughMidSpanIsAClash()
    {
        // Two beams crossing at mid-span: no axis end in the overlap.
        var overlap = new[] { 2900.0, -100, 2900, 3100, 100, 3100 };
        var ends = new[] { new[] { 0.0, 0, 3000 }, new[] { 6000.0, 0, 3000 }, new[] { 3000.0, -3000, 3000 }, new[] { 3000.0, 3000, 3000 } };
        Assert.False(StructureJointPolicy.IsNode(overlap, ends, 1));
        Assert.False(StructureJointPolicy.IsNode(overlap, System.Array.Empty<double[]>(), 1));
        Assert.False(StructureJointPolicy.IsNode(null, ends, 1));
    }

    [Fact] public void StructureClashesAreShownForSelectedLinesOnlyWhenSwitchedOn()
    {
        var snapshot = new AuditSnapshot();
        snapshot.Parts.Add(Part("P", "Pipe", "09Г2С"));
        snapshot.GeometryIssues.Add(new AuditIssue { RuleId = "collisions", Severity = "Пересечение", Keys = { "B1", "B2" }, BetweenStructures = true });
        var options = new AuditOptions { SelectedLines = new HashSet<string> { "L1" } };
        options.Enabled.Add("collisions");
        Assert.Single(AuditRules.Run(snapshot, options));
        options.StructureCollisions = false;
        Assert.Empty(AuditRules.Run(snapshot, options));
    }

    [Fact] public void JsonReportCarriesTheTolerance()
    {
        var options = new AuditOptions { CollisionToleranceMm = 2.5 };
        options.Enabled.Add("collisions");
        var snapshot = new AuditSnapshot { CollisionsWithinTolerance = 3 };
        using var json = JsonDocument.Parse(AuditReport.Serialize(snapshot, options, new List<AuditIssue>()));
        Assert.Equal(2.5, json.RootElement.GetProperty("CollisionToleranceMm").GetDouble());
        Assert.Equal(3, json.RootElement.GetProperty("CollisionsWithinTolerance").GetInt32());
    }
}
