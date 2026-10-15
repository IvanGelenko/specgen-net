using System.Linq;
using PipingSpecPlugin.Audit;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    public class AuditTests
    {
        static AuditPart Part(string key, double? wall = 4, string line = "ID:1") => new()
        { Key = key, ClassName = "Pipe", Diameter = "100", IsPipeOrElbow = true, WallMm = wall, LineKey = line, LineLabel = line, ConnectionsRead = true };
        static AuditOptions Enabled(params string[] ids)
        {
            var options = new AuditOptions();
            foreach (var id in ids) options.Enabled.Add(id);
            return options;
        }
        [Theory]
        [InlineData("4,5", 4.5)]
        [InlineData(" 4.5 ", 4.5)]
        public void ParsesNumericWall(string raw, double expected) => Assert.Equal(expected, AuditRules.ParsePositive(raw));
        [Theory]
        [InlineData("")][InlineData("NaN")][InlineData("Infinity")][InlineData("-1")][InlineData("0")][InlineData("SCH40")]
        public void RejectsUnknownWall(string raw) => Assert.Null(AuditRules.ParsePositive(raw));
        [Fact]
        public void ReportsDifferentWallsOnSameLineAndDiameter()
        {
            var snapshot = new AuditSnapshot();
            snapshot.Parts.Add(Part("A", 4));
            snapshot.Parts.Add(Part("B", 5));
            snapshot.Parts[1].ClassName = "Elbow";
            snapshot.Parts[1].Diameter = "100";
            var issue = Assert.Single(AuditRules.Run(snapshot, Enabled("wall")));
            Assert.Equal("Несоответствие", issue.Severity);
            Assert.Equal(new[] { "A", "B" }, issue.Keys);
        }
        [Fact]
        public void DifferentDiametersAreComparedInSeparateGroups()
        {
            var snapshot = new AuditSnapshot();
            var a = Part("A", 4); var b = Part("B", 6); b.Diameter = "50";
            snapshot.Parts.AddRange(new[] { a, b });
            Assert.Empty(AuditRules.Run(snapshot, Enabled("wall")));
            var c = Part("C", 5); c.ClassName = "Elbow"; c.Diameter = " 100,0 ";
            snapshot.Parts.Add(c);
            Assert.Equal(new[] { "A", "C" }, Assert.Single(AuditRules.Run(snapshot, Enabled("wall"))).Keys);
        }
        [Theory]
        [InlineData(null)][InlineData("")][InlineData("unknown")][InlineData("0")]
        public void UnknownDiameterIsReportedAndNeverCompared(string diameter)
        {
            var snapshot = new AuditSnapshot();
            var a = Part("A", 4); var b = Part("B", 5); b.Diameter = diameter;
            snapshot.Parts.AddRange(new[] { a, b });
            var issue = Assert.Single(AuditRules.Run(snapshot, Enabled("wall")));
            Assert.Equal("Не проверено", issue.Severity);
            Assert.Equal(new[] { "B" }, issue.Keys);
        }
        [Fact]
        public void RespectsToleranceLineBoundariesAndDisabledChecks()
        {
            var snapshot = new AuditSnapshot();
            snapshot.Parts.Add(Part("A", 4)); snapshot.Parts.Add(Part("B", 4.01)); snapshot.Parts.Add(Part("C", 6, "ID:2"));
            Assert.Empty(AuditRules.Run(snapshot, Enabled("wall")));
            snapshot.Parts[1].WallMm = 5;
            Assert.Empty(AuditRules.Run(snapshot, Enabled()));
        }
        [Fact]
        public void DoesNotTreatMissingPropertiesAsPassing()
        {
            var snapshot = new AuditSnapshot(); snapshot.Parts.Add(Part("A", null, null));
            var issues = AuditRules.Run(snapshot, Enabled("wall"));
            Assert.Equal(2, issues.Count);
            Assert.All(issues, i => Assert.Equal("Не проверено", i.Severity));
        }
        [Fact]
        public void OpenPortAndUnknownConnectionAreDistinct()
        {
            var snapshot = new AuditSnapshot();
            var part = Part("A"); part.ConnectionsRead = false;
            part.Ports.Add(new AuditPort { Name = "S1", Connected = false });
            part.Ports.Add(new AuditPort { Name = "S2", Connected = null }); snapshot.Parts.Add(part);
            var issues = AuditRules.Run(snapshot, Enabled("connections"));
            Assert.Single(issues.Where(i => i.Message.Contains("Свободный порт")));
            Assert.Single(issues.Where(i => i.Severity == "Не проверено"));
        }
        [Fact]
        public void ConnectorBridgesSameLineButDifferentLineDoesNot()
        {
            var snapshot = new AuditSnapshot();
            var a = Part("A"); var b = Part("B"); var bridge = Part("J", line: null);
            a.Ports.Add(new AuditPort { Connected = true }); b.Ports.Add(new AuditPort { Connected = true });
            bridge.IsPipeOrElbow = false; bridge.IsConnector = true;
            a.Neighbours.Add("J"); bridge.Neighbours.UnionWith(new[] { "A", "B" }); b.Neighbours.Add("J");
            snapshot.Parts.AddRange(new[] { a, b, bridge });
            Assert.Empty(AuditRules.Run(snapshot, Enabled("connections")));
            bridge.LineKey = "ID:2";
            Assert.Single(AuditRules.Run(snapshot, Enabled("connections")).Where(i => i.Message.Contains("несвязанных")));
        }
        [Fact]
        public void GeometryCoverageIssuesOnlyAppearWhenSelected()
        {
            var snapshot = new AuditSnapshot();
            snapshot.GeometryIssues.Add(new AuditIssue { RuleId = "collisions", Severity = "Не проверено" });
            Assert.Empty(AuditRules.Run(snapshot, Enabled("wall")));
            Assert.Single(AuditRules.Run(snapshot, Enabled("collisions")));
        }
        [Fact]
        public void OnlyChosenLinesAreCheckedAndEmptySelectionChecksNothing()
        {
            var snapshot = new AuditSnapshot();
            snapshot.Parts.AddRange(new[] { Part("A", 4, "L1"), Part("B", 5, "L1"), Part("C", 4, "L2"), Part("D", 8, "L2") });
            var options = Enabled("wall");
            options.SelectedLines = new System.Collections.Generic.HashSet<string> { "L2" };
            var issue = Assert.Single(AuditRules.Run(snapshot, options));
            Assert.Equal(new[] { "C", "D" }, issue.Keys);
            options.SelectedLines.Clear();
            Assert.Empty(AuditRules.Run(snapshot, options));
        }
        [Fact]
        public void NoLineOptionDoesNotCombineUnassignedPartsAsOnePipeline()
        {
            var snapshot = new AuditSnapshot(); snapshot.Parts.Add(Part("A", line: null)); snapshot.Parts.Add(Part("B", line: null));
            var options = Enabled("wall"); options.SelectedLines = new System.Collections.Generic.HashSet<string> { AuditOptions.NoLine };
            var issues = AuditRules.Run(snapshot, options);
            Assert.Equal(2, issues.Count); Assert.All(issues, i => Assert.Equal("Не проверено", i.Severity));
        }
        [Fact]
        public void SelectedLineClashesWithUnselectedLineAreRetained()
        {
            var snapshot = new AuditSnapshot();
            var a = Part("A", line: "L1"); var b = Part("B", line: "L2"); var c = Part("C", line: "L3");
            snapshot.Parts.AddRange(new[] { a, b, c });
            snapshot.GeometryIssues.Add(AuditRules.Issue("collisions", "Коллизии", "Пересечение", "AB", new[] { a, b }));
            snapshot.GeometryIssues.Add(AuditRules.Issue("collisions", "Коллизии", "Пересечение", "BC", new[] { b, c }));
            var options = Enabled("collisions"); options.SelectedLines = new System.Collections.Generic.HashSet<string> { "L1" };
            Assert.Equal("AB", Assert.Single(AuditRules.Run(snapshot, options)).Message);
            options.SelectedLines.Clear(); Assert.Empty(AuditRules.Run(snapshot, options));
        }
        [Fact]
        public void XrefLeafIdentityIsSeparateFromHostSelectionHandle()
        {
            var local = Part("A", line: "L");
            var remote = new AuditPart { Key = "F0/20/15", SelectionKey = "F0", Source = "equipment.dwg" };
            var issue = AuditRules.Issue("collisions", "Коллизии", "Пересечение", "hit", new[] { local, remote });
            Assert.Equal(new[] { "A", "F0/20/15" }, issue.Keys);
            Assert.Equal(new[] { "A", "F0" }, issue.SelectableKeys);
            Assert.Equal("equipment.dwg", issue.Sources);
        }
    }
}
