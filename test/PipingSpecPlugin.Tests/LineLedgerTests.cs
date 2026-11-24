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
        public void RefreshCollapsesEqualRowsWithoutChangingSourceOrSyncTargets()
        {
            var lines = new List<LineGroupInfo> { Line(10), Line(20) };
            Assert.Equal(2, LineSyncService.BuildLedger(lines).Count);
            var ledger = LineSyncService.BuildLedger(lines, true);
            Assert.Single(ledger);
            Assert.DoesNotContain("дубль", ledger[0].Tag);
            Assert.Equal(2, lines.Count);
            Assert.Equal("150-KSN001-LS016C-HN", lines[0].Tag);
            Assert.Equal(2, LineSyncService.BuildLedger(lines, false).Count);
            Assert.Single(LineSyncService.BuildLedger(lines, true));
            var plan = LineSyncService.BuildPlan(
                new List<VtRowData> { new VtRowData { NN = "1", WorkPressure = "2.0" } }, lines);
            Assert.Equal(2, plan.Actions.Count);
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
            Assert.Single(LineSyncService.BuildLedger(lines, true));
            Assert.Equal(2, LineSyncService.BuildLedger(lines).Count);
            Assert.Equal("2", second.AllProperties["Номер"]);
        }

        [Fact]
        public void MergesSameIdAndTagShowingConflictsButKeepsDifferentTags()
        {
            var lines = new List<LineGroupInfo> { Line(10), Line(20, pressure: "2.0"), Line(30, tag: "other") };
            var ledger = LineSyncService.BuildLedger(lines, true);
            Assert.Equal(2, ledger.Count);
            Assert.Equal("⚠ 1.6 | 2.0", ledger[0].Values["Pwork"]);
            Assert.All(ledger, row => Assert.Contains("дубль ID", row.Tag));
        }

        [Fact]
        public void MergePreservesEmptyConflictsAndDoesNotChangeSyncTargets()
        {
            var lines = new List<LineGroupInfo> { Line(10), Line(20, pressure: ""), Line(30) };
            var row = Assert.Single(LineSyncService.BuildLedger(lines, true));
            Assert.Equal("⚠ 1.6 | (пусто)", row.Values["Pwork"]);
            Assert.Equal("", lines[1].AllProperties["Pwork"]);
            Assert.Equal(3, LineSyncService.BuildPlan(new List<VtRowData> { new VtRowData { NN = "1", WorkPressure = "3.0" } }, lines).Actions.Count);
        }

        [Fact]
        public void KeepsDistinctIdsAndHandlesEmptyLedger()
        {
            var first = Line(10);
            var second = Line(20);
            second.Id = "2";
            Assert.Equal(2, LineSyncService.BuildLedger(new List<LineGroupInfo> { first, second }, true).Count);
            Assert.Empty(LineSyncService.BuildLedger(new List<LineGroupInfo>(), true));
        }

        [Fact]
        public void BuildLedgerPopulatesRowIdsWhenUncollapsed()
        {
            var lines = new List<LineGroupInfo> { Line(101), Line(102) };
            var ledger = LineSyncService.BuildLedger(lines, false);
            Assert.Equal(2, ledger.Count);
            Assert.Equal(new[] { 101 }, ledger[0].AllRowIds);
            Assert.Equal(new[] { 102 }, ledger[1].AllRowIds);
        }

        [Fact]
        public void BuildLedgerAggregatesRowIdsWhenCollapsed()
        {
            var lines = new List<LineGroupInfo> { Line(101), Line(102), Line(201, tag: "other") };
            var ledger = LineSyncService.BuildLedger(lines, true);
            Assert.Equal(2, ledger.Count);
            var merged = ledger[0];
            Assert.Equal(2, merged.RowIds.Count);
            Assert.Contains(101, merged.RowIds);
            Assert.Contains(102, merged.RowIds);
            Assert.Equal(new[] { 201 }, ledger[1].AllRowIds);
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

            var plan = LineSyncService.BuildPlan(vtRows, lines);
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

            var plan = LineSyncService.BuildPlan(vtRows, lines);
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

            var rows = LineSyncService.BuildLedger(new List<LineGroupInfo> { line });
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

            var rows = LineSyncService.BuildLedger(new List<LineGroupInfo> { line });
            var row = Assert.Single(rows);
            Assert.Equal("20", row.Values["srok"]);
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

            var plan = LineSyncService.BuildPlan(vtRows, new List<LineGroupInfo> { line });
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

            var plan = LineSyncService.BuildPlan(vtRows, new List<LineGroupInfo> { line });
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

            var plan = LineSyncService.BuildPlan(vtRows, new List<LineGroupInfo> { line });
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
