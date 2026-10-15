using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PipingSpecPlugin.Audit;
using Xunit;

namespace PipingSpecPlugin.Tests;

// Review of the «Аудит» module (1.3.25): one test (or a small group) per fix
// that is verifiable without a live AutoCAD.
public class AuditModuleTests
{
    static AuditPart Pipe(string key, string line = "L1") => new()
    { Key = key, ClassName = "Pipe", Diameter = "100", IsPipeOrElbow = true, WallMm = 4, LineKey = line, LineLabel = line, ConnectionsRead = true };

    static AuditPart Connector(string key, string line = null) => new()
    { Key = key, ClassName = "P3dConnector", IsConnector = true, LineKey = line, ConnectionsRead = true };

    static AuditOptions Enabled(params string[] ids)
    {
        var options = new AuditOptions();
        options.Enabled.UnionWith(ids);
        return options;
    }

    static void Link(AuditPart a, AuditPart b) { a.Neighbours.Add(b.Key); b.Neighbours.Add(a.Key); }

    // ---- wall thickness: catalogue LengthUnit, not drawing INSUNITS -----------

    [Theory]
    [InlineData("4", "mm", 4.0)]
    [InlineData("4", "", 4.0)]          // blank unit = mm, as in the spec
    [InlineData("4", null, 4.0)]
    [InlineData("4,5", " MM ", 4.5)]
    [InlineData("0.25", "in", 6.35)]
    [InlineData("0.4", "cm", 4.0)]
    public void WallIsConvertedByThePartsOwnUnit(string raw, string unit, double expected)
        => Assert.Equal(expected, AuditRules.WallMillimetres(raw, unit).Value, 9);

    [Theory]
    [InlineData("4", "furlong")]
    [InlineData("abc", "mm")]
    [InlineData("0", "mm")]
    [InlineData("", "mm")]
    public void UnknownWallOrUnitGivesNoMillimetres(string raw, string unit)
        => Assert.Null(AuditRules.WallMillimetres(raw, unit));

    [Fact]
    public void InchAndMetricCataloguePartsOnOneLineCompareInMillimetres()
    {
        // 0.25 in = 6.35 mm: equal walls, no remark. Converting both by one
        // drawing unit made them differ by a factor of 25.4.
        var snapshot = new AuditSnapshot();
        var a = Pipe("A"); a.WallRaw = "6.35"; a.LengthUnit = "mm"; a.WallMm = AuditRules.WallMillimetres(a.WallRaw, a.LengthUnit);
        var b = Pipe("B"); b.WallRaw = "0.25"; b.LengthUnit = "in"; b.WallMm = AuditRules.WallMillimetres(b.WallRaw, b.LengthUnit);
        snapshot.Parts.AddRange(new[] { a, b });
        Assert.Empty(AuditRules.Run(snapshot, Enabled("wall")));
    }

    // ---- walls: bends named, tees / reducers against the pipes welded to them ----

    static AuditPart Fitting(string key, string className, params (string Name, string Nd, string Wall)[] ends) => new()
    {
        Key = key, ClassName = className, IsTeeOrReducer = true, LineKey = "L1", LineLabel = "L1", ConnectionsRead = true,
        Description = className, Ends = ends.Select(e => new AuditEnd { Name = e.Name, Diameter = e.Nd, WallRaw = e.Wall, LengthUnit = "mm" }).ToList(),
    };

    static AuditPart PipeOf(string key, string nd, double wall, string line = "L1") => new()
    { Key = key, ClassName = "Pipe", IsPipe = true, IsPipeOrElbow = true, Diameter = nd, WallRaw = wall.ToString(System.Globalization.CultureInfo.InvariantCulture), WallMm = wall, LineKey = line, LineLabel = line, ConnectionsRead = true, Description = "Труба" };

    [Theory]
    [InlineData("PipeBend", "Изгиб")] [InlineData("Elbow", "Отвод")] [InlineData("Pipe", "Труба")]
    [InlineData("Tee", "Тройник")] [InlineData("Reducer", "Переход")] [InlineData("Flange", "Flange")]
    public void PartsAreNamedInRussian(string className, string kind)
        => Assert.Equal(kind, AuditRules.KindOf(new AuditPart { ClassName = className }));

    [Fact]
    public void BendIsComparedAndNamedInTheWallRemark()
    {
        // temp.dwg: elbows 4 mm, pipes 5 mm, a pipe bend made of 108x6 — 6 mm.
        var snapshot = new AuditSnapshot();
        var pipe = PipeOf("4B27", "100", 5); var bend = PipeOf("4BA4", "100", 6); bend.ClassName = "PipeBend";
        Link(pipe, bend);
        snapshot.Parts.AddRange(new[] { pipe, bend });
        var issue = Assert.Single(AuditRules.Run(snapshot, Enabled("wall")));
        Assert.Equal("Несоответствие", issue.Severity);
        Assert.Contains("Труба Ду 100 [4B27] — 5 мм ↔ Изгиб Ду 100 [4BA4] — 6 мм", issue.Message);
    }

    // ---- walls: one remark per place where the wall changes ------------------

    [Fact]
    public void EachJointWhereTheWallChangesIsItsOwnRemark()
    {
        // 4 — 4 — [3.5 — 3.5] — 4, the second change through a weld connector.
        var snapshot = new AuditSnapshot();
        var a = PipeOf("A", "50", 4); var b = PipeOf("B", "50", 4); var c = PipeOf("C", "50", 3.5); var d = PipeOf("D", "50", 3.5);
        var e = PipeOf("E", "50", 4); e.ClassName = "Elbow";
        var weld = Connector("W");
        Link(a, b); Link(b, c); Link(c, d); Link(d, weld); Link(weld, e);
        snapshot.Parts.AddRange(new[] { a, b, c, d, e, weld });
        var issues = AuditRules.Run(snapshot, Enabled("wall"));
        Assert.Equal(2, issues.Count);
        // Thinner first: «Труба Ду 50 [C] — 3,5 мм ↔ Труба Ду 50 [B] — 4 мм».
        Assert.Contains(issues, i => i.Keys.SequenceEqual(new[] { "C", "B" }) && i.Message.IndexOf("[C]") < i.Message.IndexOf("[B]"));
        Assert.Contains(issues, i => i.Keys.SequenceEqual(new[] { "D", "E" }));
        Assert.All(issues, i => Assert.Equal(0.5, i.Magnitude));
    }

    [Fact]
    public void ASeparatePieceOfAnotherWallIsOneRemarkAgainstTheLinesWall()
    {
        var snapshot = new AuditSnapshot();
        var a = PipeOf("A", "50", 4); var b = PipeOf("B", "50", 4); var c = PipeOf("C", "50", 4);
        var x = PipeOf("X", "50", 3.5); var y = PipeOf("Y", "50", 3.5);
        Link(a, b); Link(b, c); Link(x, y);
        snapshot.Parts.AddRange(new[] { a, b, c, x, y });
        var issue = Assert.Single(AuditRules.Run(snapshot, Enabled("wall")));
        Assert.Equal(new[] { "X", "Y" }, issue.Keys);
        Assert.Contains("Основная стенка линии — 4 мм (3 дет.)", issue.Message);
    }

    [Fact]
    public void ReducerEndThinnerThanItsPipeIsReported()
    {
        // Reducer 159x8-89x6: the 89 end (6 mm) is welded to a 89x8 pipe.
        var snapshot = new AuditSnapshot();
        var reducer = Fitting("R", "Reducer", ("S1", "150", "8"), ("S2", "80", "6"));
        var big = PipeOf("P1", "150", 8); var small = PipeOf("P2", "80", 8);
        var w1 = Connector("W1", "L1"); var w2 = Connector("W2", "L1");
        Link(big, w1); Link(w1, reducer); Link(reducer, w2); Link(w2, small);
        snapshot.Parts.AddRange(new[] { reducer, big, small, w1, w2 });
        var issue = Assert.Single(AuditRules.Run(snapshot, Enabled("wall")), i => i.Severity == "Несоответствие");
        Assert.Equal(new[] { "R", "P2" }, issue.Keys);
        Assert.Contains("стенка 6 мм на конце Ду 80 тоньше соединённой трубы — 8 мм", issue.Message);
        // A 89x6 pipe on that end is fine; a thicker fitting is always fine.
        small.WallMm = 6;
        Assert.DoesNotContain(AuditRules.Run(snapshot, Enabled("wall")), i => i.Keys.Contains("R"));
    }

    [Fact]
    public void TeeThinnerThanTheRunPipeIsReportedThickerIsNot()
    {
        var snapshot = new AuditSnapshot();
        var tee = Fitting("T", "Tee", ("S1", "150", "6"), ("S2", "150", "6"), ("S3", "150", "6"));
        var pipe = PipeOf("P", "150", 8);
        Link(tee, pipe);   // also without a connection object between them
        snapshot.Parts.AddRange(new[] { tee, pipe });
        Assert.Single(AuditRules.Run(snapshot, Enabled("wall")), i => i.Severity == "Несоответствие" && i.Keys.Contains("T"));
        pipe.WallMm = 6;
        Assert.DoesNotContain(AuditRules.Run(snapshot, Enabled("wall")), i => i.Keys.Contains("T"));
    }

    [Fact]
    public void FittingWithUnknownEndsOrNoMatchingEndIsUnchecked()
    {
        var snapshot = new AuditSnapshot();
        var tee = Fitting("T", "Tee"); tee.Ends = null; tee.EndsError = "PartPort failed";
        var pipe = PipeOf("P", "150", 8);
        Link(tee, pipe);
        snapshot.Parts.AddRange(new[] { tee, pipe });
        Assert.Contains(AuditRules.Run(snapshot, Enabled("wall")), i => i.Severity == "Не проверено" && i.Message.Contains("PartPort failed"));
        tee.Ends = new List<AuditEnd> { new() { Name = "S1", Diameter = "100", WallRaw = "6", LengthUnit = "mm" } };
        Assert.Contains(AuditRules.Run(snapshot, Enabled("wall")), i => i.Severity == "Не проверено" && i.Message.Contains("нет конца под Ду трубы"));
    }

    [Theory]
    [InlineData("Reducer", "Переход К-108x4.0-89x3.5 ГОСТ 17378-2001", "Переход К-108x4.0-89x3.5 ГОСТ 17378-2001 [K]")]
    [InlineData("Tee", "89x3.5 ГОСТ 17376-2001", "Тройник 89x3.5 ГОСТ 17376-2001 [K]")]
    [InlineData("Pipe", "", "Труба [K]")]
    public void NameDoesNotRepeatTheKind(string className, string description, string expected)
        => Assert.Equal(expected, AuditRules.NameOf(new AuditPart { Key = "K", ClassName = className, Description = description }));

    [Fact]
    public void EndWallIsInItsOwnUnit()
        => Assert.Equal(6.35, new AuditEnd { WallRaw = "0.25", LengthUnit = "in" }.WallMm.Value, 9);

    // ---- read issues: an element of unknown line is never hidden ----------------

    [Fact]
    public void ReadIssueOfPartWithUnknownLineIsShownForSelectedLines()
    {
        var snapshot = new AuditSnapshot();
        var unread = new AuditPart { Key = "U", ClassName = "Pipe" };   // properties failed: no line
        snapshot.Parts.AddRange(new[] { Pipe("A", "L1"), unread });
        snapshot.ReadIssues.Add(new AuditIssue { RuleId = "read", Check = "Чтение данных", Severity = "Не проверено", Message = "Свойства: x", Keys = new() { "U" } });
        var options = Enabled("wall"); options.SelectedLines = new HashSet<string> { "L1" };
        Assert.Contains(AuditRules.Run(snapshot, options), i => i.Message == "Свойства: x");
    }

    [Fact]
    public void ReadIssueOfUnopenedEntityOrWithoutElementsIsShown()
    {
        var snapshot = new AuditSnapshot();
        snapshot.Parts.Add(Pipe("A", "L1"));
        snapshot.ReadIssues.Add(new AuditIssue { RuleId = "read", Severity = "Не проверено", Message = "not opened", Keys = new() { "FF" } });
        snapshot.ReadIssues.Add(new AuditIssue { RuleId = "read", Severity = "Не проверено", Message = "row 12", Keys = new() });
        var options = Enabled("wall"); options.SelectedLines = new HashSet<string> { "L1" };
        var messages = AuditRules.Run(snapshot, options).Select(i => i.Message).ToList();
        Assert.Contains("not opened", messages);
        Assert.Contains("row 12", messages);
    }

    [Fact]
    public void UnreadPartGetsOnlyItsReadIssueNotThreeWallRemarks()
    {
        var snapshot = new AuditSnapshot();
        var unread = new AuditPart { Key = "U", ClassName = "Pipe", IsPipeOrElbow = true, LineUnknown = true };
        snapshot.Parts.Add(unread);
        snapshot.ReadIssues.Add(new AuditIssue { RuleId = "read", Severity = "Не проверено", Message = "Свойства: x", Keys = new() { "U" } });
        var options = Enabled("wall"); options.SelectedLines = new HashSet<string> { AuditOptions.NoLine };
        Assert.Equal("Свойства: x", Assert.Single(AuditRules.Run(snapshot, options)).Message);
    }

    [Fact]
    public void ReadIssueOfPartOnUnselectedLineStaysHidden()
    {
        var snapshot = new AuditSnapshot();
        snapshot.Parts.AddRange(new[] { Pipe("A", "L1"), Pipe("B", "L2") });
        snapshot.ReadIssues.Add(new AuditIssue { RuleId = "read", Severity = "Не проверено", Message = "row of B", Keys = new() { "B" } });
        var options = Enabled("wall"); options.SelectedLines = new HashSet<string> { "L1" };
        Assert.DoesNotContain(AuditRules.Run(snapshot, options), i => i.Message == "row of B");
        options.SelectedLines.Add("L2");
        Assert.Contains(AuditRules.Run(snapshot, options), i => i.Message == "row of B");
    }

    // ---- connectivity ------------------------------------------------------------

    [Fact]
    public void UnreadConnectorElsewhereNoLongerHidesASplitLine()
    {
        var snapshot = new AuditSnapshot();
        var a = Pipe("A"); var b = Pipe("B");
        a.Ports.Add(new AuditPort { Name = "S1", Connected = true }); b.Ports.Add(new AuditPort { Name = "S1", Connected = true });
        // A connector without a line somewhere else, its connections unread.
        var far = Connector("FAR"); far.ConnectionsRead = false;
        snapshot.Parts.AddRange(new[] { a, b, far });
        var options = Enabled("connections"); options.SelectedLines = new HashSet<string> { "L1" };
        var split = Assert.Single(AuditRules.Run(snapshot, options), i => i.Message.Contains("несвязанных"));
        Assert.Equal("Проверить", split.Severity);
    }

    [Fact]
    public void SplitThatUnreadElementsMayCloseIsReportedAsUnchecked()
    {
        // A – J1(unread) … J2(unread) – B: the J1–J2 edge may be missing from
        // the data (neither side was read), so the split is not certain. It
        // used to be skipped silently.
        var snapshot = new AuditSnapshot();
        var a = Pipe("A"); var b = Pipe("B");
        var j1 = Connector("J1"); j1.ConnectionsRead = false;
        var j2 = Connector("J2"); j2.ConnectionsRead = false;
        a.Ports.Add(new AuditPort { Connected = true }); b.Ports.Add(new AuditPort { Connected = true });
        Link(a, j1); Link(j2, b);
        snapshot.Parts.AddRange(new[] { a, b, j1, j2 });
        var issue = Assert.Single(AuditRules.Run(snapshot, Enabled("connections")), i => i.Message.Contains("несвязанных"));
        Assert.Equal("Не проверено", issue.Severity);
        Assert.Contains("(J1, J2)", issue.Message);
        Assert.Equal(new[] { "A", "B" }, issue.Keys.OrderBy(k => k));
    }

    [Fact]
    public void UnreadElementTouchingOnePieceKeepsTheSplitCertain()
    {
        // J's unknown edges can only lead to other unread elements; B was read
        // and does not list J, so nothing can join A and B.
        var snapshot = new AuditSnapshot();
        var a = Pipe("A"); var b = Pipe("B"); var j = Connector("J"); j.ConnectionsRead = false;
        a.Ports.Add(new AuditPort { Connected = true }); b.Ports.Add(new AuditPort { Connected = true });
        Link(a, j);
        snapshot.Parts.AddRange(new[] { a, b, j });
        var issue = Assert.Single(AuditRules.Run(snapshot, Enabled("connections")), i => i.Message.Contains("несвязанных"));
        Assert.Equal("Проверить", issue.Severity);
    }

    [Fact]
    public void UnreadSupportOfTheLineDoesNotBridgeButMakesTheSplitUncertain()
    {
        // S may be a support (symbolic attachment to both pipes, not a joint)
        // or a valve: its ports are unknown, so neither answer is certain.
        var snapshot = new AuditSnapshot();
        var a = Pipe("A"); var b = Pipe("B");
        var s = new AuditPart { Key = "S", ClassName = "Support", LineKey = "L1", LineLabel = "L1", ConnectionsRead = false };
        a.Ports.Add(new AuditPort { Connected = true }); b.Ports.Add(new AuditPort { Connected = true });
        Link(a, s); Link(s, b);
        snapshot.Parts.AddRange(new[] { a, b, s });
        var issue = Assert.Single(AuditRules.Run(snapshot, Enabled("connections")), i => i.Message.Contains("несвязанных"));
        Assert.Equal("Не проверено", issue.Severity);
        Assert.Contains("(S)", issue.Message);
    }

    [Fact]
    public void PartWithUnknownLineBetweenPiecesMakesTheSplitUncertain()
    {
        // X's properties were not read: it may belong to L1 and join A and B.
        var snapshot = new AuditSnapshot();
        var a = Pipe("A"); var b = Pipe("B");
        var x = new AuditPart { Key = "X", ClassName = "Pipe", LineUnknown = true, ConnectionsRead = true };
        x.Ports.Add(new AuditPort { Connected = true }); x.Ports.Add(new AuditPort { Connected = true });
        a.Ports.Add(new AuditPort { Connected = true }); b.Ports.Add(new AuditPort { Connected = true });
        Link(a, x); Link(x, b);
        snapshot.Parts.AddRange(new[] { a, b, x });
        var options = Enabled("connections"); options.SelectedLines = new HashSet<string> { "L1" };
        var issue = Assert.Single(AuditRules.Run(snapshot, options), i => i.Message.Contains("несвязанных"));
        Assert.Equal("Не проверено", issue.Severity);
        Assert.Contains("(X)", issue.Message);
        // A part known to have no line between the pieces is a real break of the line.
        x.LineUnknown = false;
        Assert.Equal("Проверить", Assert.Single(AuditRules.Run(snapshot, options), i => i.Message.Contains("несвязанных")).Severity);
    }

    [Fact]
    public void UnreadPartOfTheSameLineBridgesItsNeighbours()
    {
        // M's own ports failed to read, but A and B each report M as their
        // neighbour: A–M–B is connected, not two pieces.
        var snapshot = new AuditSnapshot();
        var a = Pipe("A"); var b = Pipe("B"); var m = Pipe("M"); m.ConnectionsRead = false; m.ConnectionsError = "GetPorts failed";
        a.Ports.Add(new AuditPort { Connected = true }); b.Ports.Add(new AuditPort { Connected = true });
        Link(a, m); Link(m, b);
        snapshot.Parts.AddRange(new[] { a, b, m });
        var issues = AuditRules.Run(snapshot, Enabled("connections"));
        Assert.DoesNotContain(issues, i => i.Message.Contains("несвязанных"));
        var unread = Assert.Single(issues);
        Assert.Equal("Не проверено", unread.Severity);
        Assert.Contains("GetPorts failed", unread.Message);
    }

    [Fact]
    public void ConnectedLineThroughSameLineConnectorHasNoRemark()
    {
        var snapshot = new AuditSnapshot();
        var a = Pipe("A"); var b = Pipe("B"); var weld = Connector("W", "l1");   // case of the tag ignored
        a.Ports.Add(new AuditPort { Connected = true }); b.Ports.Add(new AuditPort { Connected = true });
        Link(a, weld); Link(weld, b);
        snapshot.Parts.AddRange(new[] { a, b, weld });
        Assert.Empty(AuditRules.Run(snapshot, Enabled("connections")));
    }

    // ---- collisions: parts joined through a connection object --------------------

    static Dictionary<string, AuditPart> Index(params AuditPart[] parts) => parts.ToDictionary(p => p.Key);

    [Fact]
    public void StubInBranchAndHeaderJoinedByConnectionObjectAreNotAClash()
    {
        // Header 159x8 — stub-in connector — branch 32x4 (as in audit-model.dwg).
        var header = Pipe("A26"); var branch = Pipe("ABF"); var stubIn = Connector("B92", "L1");
        Link(header, stubIn); Link(stubIn, branch);
        var parts = Index(header, branch, stubIn);
        Assert.True(ConnectedJointPolicy.IgnoreOverlap(header, branch, parts));
        Assert.True(ConnectedJointPolicy.IgnoreOverlap(branch, header, parts));
        // The connection object itself against a part it joins.
        Assert.True(ConnectedJointPolicy.IgnoreOverlap(stubIn, header, parts));
        Assert.True(ConnectedJointPolicy.IgnoreOverlap(branch, stubIn, parts));
    }

    [Fact]
    public void OneSidedConnectionDataStillCounts()
    {
        // Native connections may be listed on one side only.
        var a = Pipe("A"); var b = Pipe("B"); var weld = Connector("W");
        a.Neighbours.Add("W"); weld.Neighbours.Add("B");
        Assert.True(ConnectedJointPolicy.IgnoreOverlap(a, b, Index(a, b, weld)));
        Assert.True(ConnectedJointPolicy.IgnoreOverlap(b, a, Index(a, b, weld)));
    }

    [Fact]
    public void PartsNotJoinedThroughAConnectionObjectRemainClashes()
    {
        var a = Pipe("A"); var b = Pipe("B"); var elbow = Pipe("E"); elbow.ClassName = "Elbow";
        // Through an ordinary part (two joints apart), not a connection object.
        Link(a, elbow); Link(elbow, b);
        Assert.False(ConnectedJointPolicy.IgnoreOverlap(a, b, Index(a, b, elbow)));
        // Touching without any connection object (e.g. a tee laid over a pipe).
        var tee = Pipe("T"); tee.ClassName = "Tee"; Link(a, tee);
        Assert.False(ConnectedJointPolicy.IgnoreOverlap(a, tee, Index(a, tee)));
        // Unrelated parts; a connector that joins only one of them.
        var c = Pipe("C"); var weld = Connector("W"); Link(a, weld);
        Assert.False(ConnectedJointPolicy.IgnoreOverlap(a, c, Index(a, c, weld)));
        Assert.False(ConnectedJointPolicy.IgnoreOverlap(weld, c, Index(a, c, weld)));
    }

    [Fact]
    public void JsonCarriesTheClashPlace()
    {
        // «Показать в модели» zooms to Bounds; scripts get the same place.
        var issue = new AuditIssue { RuleId = "collisions", Severity = "Пересечение", Bounds = new[] { 1.0, 2, 3, 4, 5, 6 } };
        using var json = JsonDocument.Parse(AuditReport.Serialize(new AuditSnapshot(), Enabled("collisions"), new List<AuditIssue> { issue }));
        var bounds = json.RootElement.GetProperty("Issues")[0].GetProperty("Bounds").EnumerateArray().Select(e => e.GetDouble());
        Assert.Equal(new[] { 1.0, 2, 3, 4, 5, 6 }, bounds);
    }

    [Fact]
    public void JsonCarriesTheIgnoredJointCount()
    {
        var snapshot = new AuditSnapshot { ConnectedJointsIgnored = 4 };
        using var json = JsonDocument.Parse(AuditReport.Serialize(snapshot, Enabled("collisions"), new List<AuditIssue>()));
        Assert.Equal(4, json.RootElement.GetProperty("ConnectedJointsIgnored").GetInt32());
    }

    // ---- line order and report ----------------------------------------------------

    [Fact]
    public void LinesAreOrderedNaturallyWithNoLineLast()
    {
        var lines = new List<string> { AuditOptions.NoLine, "L10", "L2", "A-1" };
        lines.Sort(AuditLineOrder.Compare);
        Assert.Equal(new[] { "A-1", "L2", "L10", AuditOptions.NoLine }, lines);
        Assert.Equal(0, AuditLineOrder.Compare(AuditOptions.NoLine, AuditOptions.NoLine));
    }

    [Fact]
    public void ReportListsLinesInDialogOrderAndDashForRemarkWithoutLine()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".docx");
        var snapshot = new AuditSnapshot { Drawing = "a.dwg" };
        snapshot.Parts.AddRange(new[] { Pipe("A", "L10"), Pipe("B", "L2") });
        var options = Enabled("collisions");
        options.SelectedLines = new HashSet<string> { "L10", AuditOptions.NoLine, "L2" };
        var issues = new List<AuditIssue> { new() { RuleId = "collisions", Check = "Коллизии", Severity = "Не проверено", Message = "Ссылка недоступна" } };
        try
        {
            AuditDocxReport.Save(path, snapshot, options, issues);
            using var document = WordprocessingDocument.Open(path, false);
            var text = document.MainDocumentPart.Document.InnerText;
            Assert.Contains("L2; L10; Без номера линии", text);
            var row = document.MainDocumentPart.Document.Body.Elements<Table>().Last().Elements<TableRow>().Last();
            Assert.Equal("—", row.Elements<TableCell>().ElementAt(1).InnerText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void JsonScopeSaysAllPartsForScriptedAudit()
    {
        var snapshot = new AuditSnapshot();
        var all = Enabled("wall");
        Assert.Contains("Все детали текущего чертежа", Scope(AuditReport.Serialize(snapshot, all, new List<AuditIssue>())));
        var some = Enabled("wall"); some.SelectedLines = new HashSet<string> { "L1" };
        Assert.Contains("Выбранные линии", Scope(AuditReport.Serialize(snapshot, some, new List<AuditIssue>())));

        static string Scope(string json) => JsonDocument.Parse(json).RootElement.GetProperty("Scope").GetString();
    }
}
