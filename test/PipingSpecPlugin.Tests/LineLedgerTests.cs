using System.Collections.Generic;
using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    public class LineLedgerTests
    {
        static LineGroupInfo Line(int rowId, string tag = "150-KSN001-LS016C-HN", string pressure = "1.6")
        {
            var line = new LineGroupInfo { RowId = rowId, Id = "1", Tag = tag };
            line.AllProperties["Pwork"] = pressure;
            return line;
        }

        [Fact]
        public void RefreshCollapsesEqualRowsWithoutChangingSourceOrResolvingDuplicates()
        {
            var lines = new List<LineGroupInfo> { Line(10), Line(20) };
            Assert.Equal(2, LineLedgerProjection.BuildLedger(lines).Count);
            var ledger = LineLedgerProjection.BuildLedger(lines, true);
            Assert.Single(ledger);
            Assert.DoesNotContain("дубль", ledger[0].DisplayTag);
            Assert.Equal(2, lines.Count);
            Assert.Equal("150-KSN001-LS016C-HN", lines[0].Tag);
            Assert.Equal(2, LineLedgerProjection.BuildLedger(lines, false).Count);
            Assert.Single(LineLedgerProjection.BuildLedger(lines, true));
            var plan = LineSyncPlanner.BuildPlan(
                new List<VtRowData> { new VtRowData { NN = "1", WorkPressure = "2.0" } }, lines);
            Assert.Empty(plan.Actions);
        }

        [Fact]
        public void HiddenFieldsAndEdgeWhitespaceDoNotPreventCollapsingVisibleDuplicates()
        {
            var first = Line(10);
            var second = Line(20, tag: " 150-KSN001-LS016C-HN ", pressure: " 1.6 ");
            first.AllProperties["Tag"] = "old tag";
            second.AllProperties["Tag"] = "different hidden tag";
            first.AllProperties["Номер"] = "1";
            second.AllProperties["Номер"] = "2";
            second.Id = " 1 ";
            var lines = new List<LineGroupInfo> { first, second };
            Assert.Single(LineLedgerProjection.BuildLedger(lines, true));
            Assert.Equal(2, LineLedgerProjection.BuildLedger(lines).Count);
            Assert.Equal("2", second.AllProperties["Номер"]);
        }

        [Fact]
        public void MergesSameIdAndTagShowingConflictsButKeepsDifferentTags()
        {
            var lines = new List<LineGroupInfo> { Line(10), Line(20, pressure: "2.0"), Line(30, tag: "other") };
            var ledger = LineLedgerProjection.BuildLedger(lines, true);
            Assert.Equal(2, ledger.Count);
            Assert.Equal("⚠ 1.6 | 2.0", ledger[0].Values["Pwork"]);
            Assert.All(ledger, row => Assert.Contains("дубль ID", row.DisplayTag));
        }

        [Fact]
        public void MergePreservesEmptyConflictsAndDoesNotChangeSyncTargets()
        {
            var lines = new List<LineGroupInfo> { Line(10), Line(20, pressure: ""), Line(30) };
            var row = Assert.Single(LineLedgerProjection.BuildLedger(lines, true));
            Assert.Equal("⚠ 1.6 | (пусто)", row.Values["Pwork"]);
            Assert.Equal("", lines[1].AllProperties["Pwork"]);
            Assert.Empty(LineSyncPlanner.BuildPlan(new List<VtRowData> { new VtRowData { NN = "1", WorkPressure = "3.0" } }, lines).Actions);
        }

        [Fact]
        public void KeepsDistinctIdsAndHandlesEmptyLedger()
        {
            var first = Line(10);
            var second = Line(20);
            second.Id = "2";
            Assert.Equal(2, LineLedgerProjection.BuildLedger(new List<LineGroupInfo> { first, second }, true).Count);
            Assert.Empty(LineLedgerProjection.BuildLedger(new List<LineGroupInfo>(), true));
        }

        [Fact]
        public void Ledger_Keeps_Original_Tag_And_Recognizes_Duplicate_Outside_Drawing()
        {
            var line = Line(10, tag: "⚠ дубль ID · исходный тег");
            var otherDrawing = Line(20);
            var row = Assert.Single(LineLedgerProjection.BuildLedger(new List<LineGroupInfo> { line },
                projectLines: new List<LineGroupInfo> { line, otherDrawing }));

            Assert.True(row.HasProjectDuplicate);
            Assert.Equal(line.Tag, row.Tag);
            Assert.Equal(line.Tag, row.DisplayTag);
            Assert.Equal(10, Assert.Single(row.AllRowIds));
        }

        [Fact]
        public void BuildLedgerPopulatesRowIdsWhenUncollapsed()
        {
            var lines = new List<LineGroupInfo> { Line(101), Line(102) };
            var ledger = LineLedgerProjection.BuildLedger(lines, false);
            Assert.Equal(2, ledger.Count);
            Assert.Equal(new[] { 101 }, ledger[0].AllRowIds);
            Assert.Equal(new[] { 102 }, ledger[1].AllRowIds);
        }

        [Fact]
        public void BuildLedgerAggregatesRowIdsWhenCollapsed()
        {
            var lines = new List<LineGroupInfo> { Line(101), Line(102), Line(201, tag: "other") };
            var ledger = LineLedgerProjection.BuildLedger(lines, true);
            Assert.Equal(2, ledger.Count);
            var merged = ledger[0];
            Assert.Equal(2, merged.RowIds.Count);
            Assert.Contains(101, merged.RowIds);
            Assert.Contains(102, merged.RowIds);
            Assert.Equal(new[] { 201 }, ledger[1].AllRowIds);
        }

        [Fact]
        public void Proposes_Id_From_Nn_For_An_Exact_Drawing_Tag_Match()
        {
            var line = Line(10, tag: "50-KSN003-LS016C-HN");
            line.Id = " ";
            var existing = Line(20, tag: "EXISTING");
            var outside = new LineGroupInfo { RowId = 30, Tag = "OUTSIDE" };
            var rows = new List<VtRowData>
            {
                new() { NN = " 51 ", Id = "old-id", Designation = "OTHER\r\n 50-ksn003-LS016C-HN " },
                new() { NN = "52", Designation = "EXISTING" },
                new() { NN = "53", Designation = "OUTSIDE" },
            };
            var plan = LineSyncPlanner.BuildIdFillPlan(rows, new[] { line, existing }, new[] { line, existing, outside });
            var action = Assert.Single(plan.Actions);
            Assert.Same(line, action.Line);
            var change = Assert.Single(action.Changes);
            Assert.Equal("ID", change.PropertyName);
            Assert.Equal("51", change.NewValue);
            Assert.Equal(" ", line.Id);

            line.Id = change.NewValue;
            var sync = LineSyncPlanner.BuildPlan(rows, new List<LineGroupInfo> { line, existing, outside });
            var syncAction = Assert.Single(sync.Actions.FindAll(action => action.Line.RowId == line.RowId));
            Assert.DoesNotContain(syncAction.Changes, item => item.PropertyName == "Tag");
            Assert.Contains(syncAction.Changes, item => item.PropertyName == "Номер" && item.NewValue == "003");
        }

        [Fact]
        public void Skips_Ambiguous_Designation_Matches_And_Occupied_Ids()
        {
            var lines = new[]
            {
                new LineGroupInfo { RowId = 10, Tag = "DUP-EXCEL" },
                new LineGroupInfo { RowId = 20, Tag = "OCCUPIED" },
                new LineGroupInfo { RowId = 30, Tag = "MULTI-A" },
                new LineGroupInfo { RowId = 40, Tag = "MULTI-B" },
                new LineGroupInfo { RowId = 50, Tag = "DUP-NN" },
                new LineGroupInfo { RowId = 60, Tag = "DUP-TAG" },
                new LineGroupInfo { RowId = 70, Tag = "DUP-TAG" },
            };
            var project = new List<LineGroupInfo>(lines) { new() { RowId = 80, Id = "3", Tag = "OUTSIDE" } };
            var rows = new List<VtRowData>
            {
                new() { NN = "1", Designation = "DUP-EXCEL" },
                new() { NN = "2", Designation = "DUP-EXCEL" },
                new() { NN = "3", Designation = "OCCUPIED" },
                new() { NN = "4", Designation = "MULTI-A\nMULTI-B" },
                new() { NN = "5", Designation = "DUP-NN" },
                new() { NN = "5", Designation = "OTHER" },
                new() { NN = "6", Designation = "DUP-TAG" },
            };
            var plan = LineSyncPlanner.BuildIdFillPlan(rows, lines, project);
            Assert.Empty(plan.Actions);
            Assert.Equal(5, plan.IdFillSkips.Count);
        }

        [Fact]
        public void Exact_Tag_Uses_The_Real_Ksn009_When_Its_Id_Is_Occupied_Elsewhere()
        {
            const string tag = "150-KSN009-LS016C-HN";
            var real = new LineGroupInfo { RowId = 114854, Tag = tag };
            var stale = new LineGroupInfo { RowId = 46723, Id = "3", Tag = tag + "?" };
            var branch = new LineGroupInfo { RowId = 165708, Tag = tag + " (врезка №4)" };
            var rows = new List<VtRowData> { new() { NN = "3", Designation = tag, WorkPressure = "2" } };
            var project = new List<LineGroupInfo> { stale, real, branch };

            var plan = LineSyncPlanner.BuildPlan(rows, project);
            var action = Assert.Single(plan.Actions);
            Assert.Equal(real.RowId, action.Line.RowId);
            Assert.DoesNotContain(action.Changes, change => change.PropertyName is "ID" or "Tag");
            var fill = LineSyncPlanner.BuildIdFillPlan(rows, new[] { real, branch }, project);
            Assert.Empty(fill.Actions);
            Assert.Contains(fill.IdFillSkips, reason => reason.Contains("46723"));
            Assert.Contains(LineLedgerProjection.BuildLedger(project, matchedRowIds: plan.MatchedRowIds), row => row.RowId == real.RowId);

            var transfer = LineSyncPlanner.BuildIdFillPlan(rows, new[] { real, branch }, project,
                canReleaseId: line => line.RowId == stale.RowId);
            Assert.Empty(transfer.IdFillSkips);
            Assert.Collection(transfer.Actions,
                release => { Assert.Equal(stale.RowId, release.Line.RowId); Assert.Equal("", Assert.Single(release.Changes).NewValue); },
                assign => { Assert.Equal(real.RowId, assign.Line.RowId); Assert.Equal("3", Assert.Single(assign.Changes).NewValue); });
            var ordered = LineSyncPlanner.ValidateIdWrites(transfer.Actions, project, line => line.RowId == stale.RowId);
            Assert.Equal(stale.RowId, ordered[0].Line.RowId);
            Assert.Throws<System.InvalidOperationException>(() => LineSyncPlanner.ValidateIdWrites(transfer.Actions, project, _ => false));
            stale.Tag = "OTHER";
            Assert.Throws<System.InvalidOperationException>(() => LineSyncPlanner.ValidateIdWrites(transfer.Actions, project, _ => true));
        }

        [Fact]
        public void Exact_Tag_Outside_Drawing_Never_Falls_Back_To_A_Local_Branch()
        {
            const string tag = "150-KSN009-LS016C-HN";
            var main = new LineGroupInfo { RowId = 10, Tag = tag };
            var branch = new LineGroupInfo { RowId = 20, Tag = tag + " (врезка №4)" };
            var rows = new List<VtRowData> { new() { NN = "3", Designation = tag, WorkPressure = "2" } };
            var project = new List<LineGroupInfo> { main, branch };
            var drawing = new HashSet<int> { branch.RowId };

            var plan = LineSyncPlanner.BuildPlan(rows, project, drawing).FilterForLine(drawing);
            Assert.Empty(plan.Actions);
            Assert.Empty(LineSyncPlanner.BuildIdFillPlan(rows, new[] { branch }, project).Actions);

            rows[0].Designation = branch.Tag;
            var branchAction = Assert.Single(LineSyncPlanner.BuildPlan(rows, project, drawing).Actions);
            Assert.Equal(branch.RowId, branchAction.Line.RowId);
            Assert.DoesNotContain(branchAction.Changes, change => change.PropertyName == "Tag");
        }

        [Fact]
        public void Repeated_Nn_Can_Sync_Two_Unique_Tags_And_An_Ambiguous_Workbook_Tag_Is_Blocked()
        {
            var first = new LineGroupInfo { RowId = 10, Tag = "FIRST" };
            var second = new LineGroupInfo { RowId = 20, Tag = "SECOND" };
            var project = new List<LineGroupInfo> { first, second };
            var rows = new List<VtRowData>
            {
                new() { RowNumber = 3, NN = "1", Designation = "FIRST", WorkPressure = "2" },
                new() { RowNumber = 4, NN = "1", Designation = "SECOND", WorkPressure = "3" },
            };

            Assert.Equal(2, LineSyncPlanner.BuildPlan(rows, project).Actions.Count);
            Assert.Empty(LineSyncPlanner.BuildIdFillPlan(rows, project, project).Actions);

            rows.Add(new VtRowData { RowNumber = 5, NN = "2", Designation = "FIRST", WorkPressure = "4" });
            var plan = LineSyncPlanner.BuildPlan(rows, project);
            Assert.Equal(second.RowId, Assert.Single(plan.Actions).Line.RowId);
            Assert.Contains("3, 5", plan.BlockedLines[first.RowId]);
        }

        [Fact]
        public void Sync_Uses_Unique_Designation_Among_Duplicate_Ids()
        {
            var matching = Line(10);
            var wrong = Line(20, "50-KSN003-LS016C-HN");
            var rows = new List<VtRowData> { new() { NN = "1", Designation = matching.Tag, WorkPressure = "2" } };
            var plan = LineSyncPlanner.BuildPlan(rows, new List<LineGroupInfo> { wrong, matching });
            Assert.Same(matching, Assert.Single(plan.Actions).Line);
            Assert.Empty(LineSyncPlanner.BuildPlan(rows, new List<LineGroupInfo> { wrong }).Actions);
            Assert.Empty(LineSyncPlanner.BuildPlan(rows, new List<LineGroupInfo> { matching, Line(30) }).Actions);
        }

        [Fact]
        public void LineSyncPlanFilterForLineIsolatesSingleLine()
        {
            var lines = new List<LineGroupInfo>
            {
                Line(10, tag: "150-LINE-1", pressure: "1.0"),
                Line(20, tag: "150-LINE-2", pressure: "1.0")
            };
            lines[1].Id = "2";

            var vtRows = new List<VtRowData>
            {
                new() { NN = "1", WorkPressure = "2.0" },
                new() { NN = "2", WorkPressure = "3.0" }
            };

            var plan = LineSyncPlanner.BuildPlan(vtRows, lines);
            Assert.Equal(2, plan.Actions.Count);
            Assert.Equal(2, plan.TotalChangedFields());

            var filtered10 = plan.FilterForLine(10);
            Assert.Single(filtered10.Actions);
            Assert.Equal(10, filtered10.Actions[0].Line.RowId);
            Assert.Equal("1", filtered10.Actions[0].Line.Id);
            Assert.Single(filtered10.Actions[0].Changes);
            Assert.Equal(1, filtered10.TotalChangedFields());

            var filtered20 = plan.FilterForLine(20);
            Assert.Single(filtered20.Actions);
            Assert.Equal(20, filtered20.Actions[0].Line.RowId);
            Assert.Equal("2", filtered20.Actions[0].Line.Id);
            Assert.Equal(1, filtered20.TotalChangedFields());

            // Original plan remains unchanged
            Assert.Equal(2, plan.Actions.Count);
        }

        [Fact]
        public void LineSyncPlanFilterForLineHandlesMultipleRowIdsAndEmpty()
        {
            var lines = new List<LineGroupInfo>
            {
                Line(10, pressure: "1.0"),
                Line(20, pressure: "1.0"),
                Line(30, pressure: "1.0")
            };
            lines[1].Id = "2";
            lines[2].Id = "3";

            var vtRows = new List<VtRowData>
            {
                new() { NN = "1", WorkPressure = "2.0" },
                new() { NN = "2", WorkPressure = "2.0" },
                new() { NN = "3", WorkPressure = "2.0" }
            };

            var plan = LineSyncPlanner.BuildPlan(vtRows, lines);
            Assert.Equal(3, plan.Actions.Count);

            var multi = plan.FilterForLine(new[] { 10, 30 });
            Assert.Equal(2, multi.Actions.Count);
            Assert.Contains(multi.Actions, a => a.Line.RowId == 10);
            Assert.Contains(multi.Actions, a => a.Line.RowId == 30);

            var empty = plan.FilterForLine(999);
            Assert.Empty(empty.Actions);
            Assert.Equal(0, empty.TotalChangedFields());
        }

        [Fact]
        public void BuildLedger_Computes_Corrosion_When_Empty_From_CorrosionSpeed_And_Srok()
        {
            var line = Line(10);
            line.AllProperties["CorrosionSpeed"] = "0.1";
            line.AllProperties["srok"] = "20";
            // line.AllProperties["Corrosion"] is empty

            var rows = LineLedgerProjection.BuildLedger(new List<LineGroupInfo> { line });
            var row = Assert.Single(rows);
            Assert.Equal("2", row.Values["Corrosion"]);
        }

        [Fact]
        public void BuildLedger_Computes_Srok_When_Empty_From_Corrosion_And_CorrosionSpeed()
        {
            var line = Line(10);
            line.AllProperties["Corrosion"] = "2";
            line.AllProperties["CorrosionSpeed"] = "0.1";
            // line.AllProperties["srok"] is empty

            var rows = LineLedgerProjection.BuildLedger(new List<LineGroupInfo> { line });
            var row = Assert.Single(rows);
            Assert.Equal("20", row.Values["srok"]);
        }

        [Fact]
        public void Pressure_Only_Does_Not_Recalculate_Corrosion_Or_Override_Explicit_Test_Note()
        {
            var line = Line(10);
            line.AllProperties["Corrosion"] = "3";
            line.AllProperties["CorrosionSpeed"] = "0.1";
            line.AllProperties["srok"] = "20";
            var vt = new VtRowData { NN = "1", WorkPressure = "2", TestStrength = "2.0/1.6", TestTightness = "не требуется" };
            var plan = LineSyncPlanner.BuildPlan(new List<VtRowData> { vt }, new List<LineGroupInfo> { line });
            var action = Assert.Single(plan.Actions);
            Assert.Contains(action.Changes, change => change.PropertyName == "Pwork");
            Assert.DoesNotContain(action.Changes, change => change.PropertyName == "Corrosion" || change.PropertyName == "TestW");
        }

        [Fact]
        public void Write_Check_Detects_Stale_Identity_Dependency_And_Missing_Readback()
        {
            var line = Line(10);
            line.AllProperties["srok"] = "20";
            var action = new LineSyncAction { Line = line };
            action.Changes.Add(new FieldChange { PropertyName = "Corrosion", OldValue = "3", NewValue = "2" });
            var current = new Dictionary<string, string> { ["ID"] = "1", ["Tag"] = line.Tag, ["srok"] = "20.0", ["Corrosion"] = "3,0" };
            Assert.Empty(LineSyncPlanner.FindWriteMismatches(action, current, afterWrite: false));
            Assert.Equal(new[] { "Corrosion" }, LineSyncPlanner.FindWriteMismatches(action, current, afterWrite: true));
            current["ID"] = "2";
            current["srok"] = "21";
            Assert.Equal(new[] { "ID", "srok" }, LineSyncPlanner.FindWriteMismatches(action, current, afterWrite: false));
        }

        [Fact]
        public void IdFill_Matches_Model_Tag_With_A_Parenthesised_Remark()
        {
            var line = new LineGroupInfo { RowId = 10, Tag = "TAG-A (примечание)" };
            var rows = new[] { new VtRowData { NN = "1", Designation = "TAG-A" } };
            Assert.Single(LineSyncPlanner.BuildIdFillPlan(rows, new[] { line }, new[] { line }).Actions);
        }

        [Fact]
        public void BuildPlan_Computes_Corrosion_When_CorrosionRate_In_Vt_And_Srok_In_Line()
        {
            var line = Line(10);
            line.AllProperties["srok"] = "20";

            var vtRows = new List<VtRowData>
            {
                new() { NN = "1", CorrosionRate = "0.1" }
            };

            var plan = LineSyncPlanner.BuildPlan(vtRows, new List<LineGroupInfo> { line });
            var action = Assert.Single(plan.Actions);
            var change = action.Changes.Find(c => c.PropertyName == "Corrosion");
            Assert.NotNull(change);
            Assert.Equal("2", change.NewValue);
        }

        [Fact]
        public void BuildPlan_Computes_Corrosion_When_ServiceLife_In_Vt_And_CorrosionSpeed_In_Line()
        {
            var line = Line(10);
            line.AllProperties["CorrosionSpeed"] = "0.05";

            var vtRows = new List<VtRowData>
            {
                new() { NN = "1", ServiceLife = "20" }
            };

            var plan = LineSyncPlanner.BuildPlan(vtRows, new List<LineGroupInfo> { line });
            var action = Assert.Single(plan.Actions);
            var change = action.Changes.Find(c => c.PropertyName == "Corrosion");
            Assert.NotNull(change);
            Assert.Equal("1", change.NewValue);
        }

        [Fact]
        public void BuildPlan_Computes_Corrosion_When_Both_In_Vt()
        {
            var line = Line(10);

            var vtRows = new List<VtRowData>
            {
                new() { NN = "1", CorrosionRate = "0.1", ServiceLife = "20" }
            };

            var plan = LineSyncPlanner.BuildPlan(vtRows, new List<LineGroupInfo> { line });
            var action = Assert.Single(plan.Actions);
            var change = action.Changes.Find(c => c.PropertyName == "Corrosion");
            Assert.NotNull(change);
            Assert.Equal("2", change.NewValue);

            var srokChange = action.Changes.Find(c => c.PropertyName == "srok");
            Assert.NotNull(srokChange);
            Assert.Equal("20", srokChange.NewValue);
        }
    }
}
