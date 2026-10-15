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
    }
}
