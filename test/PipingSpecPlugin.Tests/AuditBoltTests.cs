using System.Collections.Generic;
using System.Linq;
using PipingSpecPlugin.Audit;
using Xunit;

namespace PipingSpecPlugin.Tests;

// BoltLengthRule: the bolt set against the clamped stack of the joint.
public class AuditBoltTests
{
    static AuditPort Port(string name, double x, string endType = null, string flange = null, string unit = "mm") =>
        new() { Name = name, Position = new[] { x, 0, 0 }, EndType = endType, FlangeThicknessRaw = flange, LengthUnit = unit };

    static AuditPart Flange(string key, string line, double faceX, string thickness = "20", string endType = "FL")
    {
        var part = new AuditPart { Key = key, ClassName = "Flange", LineKey = line, LineLabel = line, ConnectionsRead = true };
        part.Ports.Add(Port("S1", faceX, endType, thickness));
        part.Ports.Add(Port("S2", faceX + 50, "BV"));
        return part;
    }

    // Two flanges 20 mm thick, a 3 mm gasket gap between their faces.
    static (AuditSnapshot Snapshot, AuditPart Joint) FlangedJoint(string length = "100", string size = "M16",
        string description = "Bolt set, Stud Bolt DIN 976", string thickness = "20", string boltUnit = "mm")
    {
        var snapshot = new AuditSnapshot { DrawingMmPerUnit = 1 };
        var a = Flange("A", "L1", 0, thickness);
        var b = Flange("B", "L1", 3, thickness);
        var joint = new AuditPart { Key = "C", ClassName = "P3dConnector", IsConnector = true, ConnectionsRead = true };
        joint.Ports.Add(Port("S1", 0));
        joint.Ports.Add(Port("S2", 3));
        joint.Links.Add(new AuditLink { Port = "S1", OtherKey = "A", OtherPort = "S1" });
        // The other side is stored on the flange only.
        b.Links.Add(new AuditLink { Port = "S1", OtherKey = "C", OtherPort = "S2" });
        joint.BoltSets.Add(new AuditBoltSet { LengthRaw = length, LengthUnit = boltUnit, Size = size, Count = "8", Description = description });
        snapshot.Parts.AddRange(new[] { a, b, joint });
        return (snapshot, joint);
    }

    static List<AuditIssue> Run(AuditSnapshot snapshot, HashSet<string> lines = null)
    {
        var options = new AuditOptions { SelectedLines = lines };
        options.Enabled.Add("bolts");
        return AuditRules.Run(snapshot, options);
    }

    [Fact] public void LongEnoughStudPasses()
    {
        // 20 + 3 + 20 + 2 × 12.8 = 68.6 mm.
        Assert.Empty(Run(FlangedJoint("70").Snapshot));
    }

    [Fact] public void StudShorterThanStackPlusTwoNutsIsReported()
    {
        var issue = Assert.Single(Run(FlangedJoint("65").Snapshot));
        Assert.Equal("Несоответствие", issue.Severity);
        Assert.Contains("68,6", issue.Message.Replace('.', ','));
        Assert.Contains("2 × гайка", issue.Message);
        Assert.Equal(new[] { "C", "A", "B" }, issue.Keys);
    }

    [Fact] public void BoltNeedsOneNutOnly()
    {
        // 43 + 12.8 = 55.8: a bolt of 60 fits, a stud of 60 does not.
        Assert.Empty(Run(FlangedJoint("60", description: "Machine bolt ГОСТ 7798").Snapshot));
        Assert.Single(Run(FlangedJoint("60", description: "Шпилька ГОСТ 9066").Snapshot));
    }

    [Fact] public void UnknownFastenerIsTreatedAsBoltAndSaysSo()
    {
        Assert.Empty(Run(FlangedJoint("60", description: "Комплект крепежа").Snapshot));
        var issue = Assert.Single(Run(FlangedJoint("50", description: "Комплект крепежа").Snapshot));
        Assert.Contains("тип крепежа не определён", issue.Message);
    }

    [Fact] public void ShorterThanTheStackItselfIsReportedEvenWithoutThreadSize()
    {
        var issue = Assert.Single(Run(FlangedJoint("40", size: "?").Snapshot));
        Assert.Equal("Несоответствие", issue.Severity);
        Assert.Contains("меньше толщины пакета", issue.Message);
        var unknown = Assert.Single(Run(FlangedJoint("80", size: "?").Snapshot));
        Assert.Equal("Не проверено", unknown.Severity);
    }

    [Fact] public void BoltLengthInInchesIsConverted()
    {
        // 2.5 in = 63.5 mm < 68.6.
        Assert.Single(Run(FlangedJoint("2.5", boltUnit: "in").Snapshot));
        Assert.Empty(Run(FlangedJoint("3", boltUnit: "in").Snapshot));
    }

    [Fact] public void MissingFlangeThicknessIsNotChecked()
    {
        var issue = Assert.Single(Run(FlangedJoint("10", thickness: "0").Snapshot));
        Assert.Equal("Не проверено", issue.Severity);
        Assert.Contains("толщина фланца", issue.Message);
    }

    [Fact] public void ValveFlangeWithoutThicknessTakesItsMatesAndOnlyAsksToCheck()
    {
        // audit-model.dwg: the valves' FlangeThickness is blank in the catalogue.
        var (snapshot, _) = FlangedJoint("65");
        snapshot.Parts.Single(p => p.Key == "B").Ports[0].FlangeThicknessRaw = "";
        var issue = Assert.Single(Run(snapshot));
        Assert.Equal("Проверить", issue.Severity);
        Assert.Contains("принята равной ответному фланцу", issue.Message);
        Assert.Contains("68,6", issue.Message.Replace('.', ','));
        snapshot.Parts.Single(p => p.Key == "C").BoltSets[0].LengthRaw = "70";
        Assert.Empty(Run(snapshot));
    }

    [Fact] public void SpectacleBlindIsClampedLikeAWaferBody()
    {
        // Flange A (20) | gap 3 | blind 22 | gap 3 | flange B (20), FL ends.
        var snapshot = new AuditSnapshot { DrawingMmPerUnit = 1 };
        var a = Flange("A", "L1", 0);
        var blind = new AuditPart { Key = "D", ClassName = "BlindDisk", LineKey = "L1", LineLabel = "L1", ConnectionsRead = true };
        blind.Ports.Add(Port("S1", 3, "FL", ""));
        blind.Ports.Add(Port("S2", 25, "FL", ""));
        var b = Flange("B", "L1", 28);
        var c1 = new AuditPart { Key = "C1", IsConnector = true, ConnectionsRead = true };
        c1.Ports.Add(Port("S1", 0)); c1.Ports.Add(Port("S2", 3));
        c1.Links.Add(new AuditLink { Port = "S1", OtherKey = "A", OtherPort = "S1" });
        c1.Links.Add(new AuditLink { Port = "S2", OtherKey = "D", OtherPort = "S1" });
        var c2 = new AuditPart { Key = "C2", IsConnector = true, ConnectionsRead = true };
        c2.Ports.Add(Port("S1", 25)); c2.Ports.Add(Port("S2", 28));
        c2.Links.Add(new AuditLink { Port = "S1", OtherKey = "D", OtherPort = "S2" });
        c2.Links.Add(new AuditLink { Port = "S2", OtherKey = "B", OtherPort = "S1" });
        c1.BoltSets.Add(new AuditBoltSet { LengthRaw = "100", Size = "М16", Description = "Крепежный комплект ОСТ 26-2040-96" });
        snapshot.Parts.AddRange(new[] { a, blind, b, c1, c2 });
        // 68 + 2 × 12.8 = 93.6.
        Assert.Empty(Run(snapshot));
        c1.BoltSets[0].LengthRaw = "90";
        Assert.Equal("Несоответствие", Assert.Single(Run(snapshot)).Severity);
        // Plant 3D placed a set on each side (audit-model.dwg): each is held
        // against the whole stack, a shortfall only asks to check the scheme.
        c2.BoltSets.Add(new AuditBoltSet { LengthRaw = "90", Size = "М16", Description = "Шпилька" });
        var issues = Run(snapshot);
        Assert.Equal(2, issues.Count);
        Assert.All(issues, i => Assert.Equal("Проверить", i.Severity));
        Assert.All(issues, i => Assert.Contains("по обе стороны", i.Message));
        c1.BoltSets[0].LengthRaw = c2.BoltSets[0].LengthRaw = "100";
        Assert.Empty(Run(snapshot));
        // A valve beyond the blind without FlangeThickness: flange A stands in.
        b.Ports[0].FlangeThicknessRaw = "";
        Assert.Empty(Run(snapshot));
        c1.BoltSets[0].LengthRaw = c2.BoltSets[0].LengthRaw = "90";
        Assert.All(Run(snapshot), i => Assert.Contains("принята равной ответному фланцу", i.Message));
    }

    [Fact] public void UnknownDrawingUnitsAreNotGuessed()
    {
        var (snapshot, _) = FlangedJoint("10");
        snapshot.DrawingMmPerUnit = null;
        Assert.Equal("Не проверено", Assert.Single(Run(snapshot)).Severity);
    }

    [Fact] public void LugJointIsNotChecked()
    {
        var (snapshot, _) = FlangedJoint("10");
        snapshot.Parts.Single(p => p.Key == "B").Ports[0].EndType = "LUG";
        var issue = Assert.Single(Run(snapshot));
        Assert.Equal("Не проверено", issue.Severity);
        Assert.Contains("LUG", issue.Message);
    }

    [Fact] public void JointOnUnselectedLinesIsSkippedButConnectorWithoutLineCounts()
    {
        var (snapshot, _) = FlangedJoint("10");
        Assert.Empty(Run(snapshot, new HashSet<string> { "L2" }));
        Assert.Single(Run(snapshot, new HashSet<string> { "L1" }));
    }

    [Fact] public void WaferBodyAndBothGasketsAreInTheStack()
    {
        // Flange A (20) | gap 3 | wafer body 50 | gap 3 | flange B (20): 96 + 25.6.
        var snapshot = new AuditSnapshot { DrawingMmPerUnit = 1 };
        var a = Flange("A", "L1", 0);
        var wafer = new AuditPart { Key = "W", ClassName = "ButterflyValve", LineKey = "L1", LineLabel = "L1", ConnectionsRead = true };
        wafer.Ports.Add(Port("S1", 3, "WF"));
        wafer.Ports.Add(Port("S2", 53, "WF"));
        var b = Flange("B", "L1", 56);
        var c1 = new AuditPart { Key = "C1", ClassName = "P3dConnector", IsConnector = true, ConnectionsRead = true };
        c1.Ports.Add(Port("S1", 0)); c1.Ports.Add(Port("S2", 3));
        c1.Links.Add(new AuditLink { Port = "S1", OtherKey = "A", OtherPort = "S1" });
        c1.Links.Add(new AuditLink { Port = "S2", OtherKey = "W", OtherPort = "S1" });
        var c2 = new AuditPart { Key = "C2", ClassName = "P3dConnector", IsConnector = true, ConnectionsRead = true };
        c2.Ports.Add(Port("S1", 53)); c2.Ports.Add(Port("S2", 56));
        c2.Links.Add(new AuditLink { Port = "S1", OtherKey = "W", OtherPort = "S2" });
        c2.Links.Add(new AuditLink { Port = "S2", OtherKey = "B", OtherPort = "S1" });
        c1.BoltSets.Add(new AuditBoltSet { LengthRaw = "110", Size = "M16", Description = "Stud Bolt" });
        snapshot.Parts.AddRange(new[] { a, wafer, b, c1, c2 });
        var issue = Assert.Single(Run(snapshot));
        Assert.Contains("121,6", issue.Message.Replace('.', ','));
        Assert.Contains("корпус [W]", issue.Message);
        c1.BoltSets[0].LengthRaw = "130";
        Assert.Empty(Run(snapshot));
    }

    [Fact] public void UnreadBoltSetIsReported()
    {
        var (snapshot, joint) = FlangedJoint();
        joint.BoltSets.Clear();
        joint.BoltsError = "eNotApplicable";
        var issue = Assert.Single(Run(snapshot));
        Assert.Equal("Не проверено", issue.Severity);
        Assert.Contains("eNotApplicable", issue.Message);
    }

    [Theory]
    [InlineData("M16", 16.0)]
    [InlineData("М20x1,5", 20.0)]   // Cyrillic М
    [InlineData("m 24", 24.0)]
    [InlineData("5/8\"", 15.875)]
    [InlineData("1-1/8 in", 28.575)]
    [InlineData("0.75\"", 19.05)]
    [InlineData("16", 16.0)]
    [InlineData("?", null)]
    [InlineData("", null)]
    public void ThreadDiameterIsParsed(string size, double? mm)
    {
        var parsed = BoltLengthRule.ThreadDiameterMm(size);
        if (mm == null) Assert.Null(parsed);
        else Assert.Equal(mm.Value, parsed.Value, 3);
    }

    [Theory]
    [InlineData("Bolt set, C, 10, Stud Bolt, DIN 2501", true)]
    [InlineData("Шпилька М16х120 ГОСТ 9066", true)]
    [InlineData("Болт М16х60 ГОСТ 7798", false)]
    [InlineData("Machine Bolt ISO 4014", false)]
    [InlineData("Крепежный комплект ОСТ 26-2040-96, исполнение 1, тип 1", true)]
    [InlineData("PH METRIC Stud Bolt M12x70.0 Lg  w/2 Hex. Nut M12", true)]
    [InlineData("Комплект крепежа", null)]
    public void StudOrBoltIsRecognised(string description, bool? stud)
        => Assert.Equal(stud, BoltLengthRule.IsStud(description));
}
