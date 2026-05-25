using System.Collections.Generic;
using System.Globalization;
using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    public class PipeBendFolderTests
    {
        static PartRecord Pipe(int pid, double mm = 5000) => new PartRecord
        {
            PnPID = pid,
            PartCategory = "Pipe",
            PnPClassName = "Pipe",
            Length = mm.ToString(CultureInfo.InvariantCulture),
            LengthUnit = "mm",
        };

        static PartRecord Bend(int pid, double mm = 1500) => new PartRecord
        {
            PnPID = pid,
            PartCategory = "Pipe",
            PnPClassName = "PipeBend",
            Length = mm.ToString(CultureInfo.InvariantCulture),
            LengthUnit = "mm",
        };

        [Fact]
        public void Bend_With_Adjacent_Pipe_Folds_In_Metres()
        {
            var p = Pipe(1);
            var b = Bend(2, 1500);  // 1.5 m
            var topology = new Dictionary<int, List<int>>
            {
                { 1, new List<int> { 2 } },
                { 2, new List<int> { 1 } },
            };
            var r = PipeBendFolder.Fold(new[] { p, b }, topology);
            Assert.Single(r.BendsConsumed);
            Assert.Contains(2, r.BendsConsumed);
            Assert.Equal(1.5, r.ExtraPipeMetres[1], 3);
        }

        [Fact]
        public void Orphan_Bend_Without_Pipe_Neighbour_Stays()
        {
            // Two bends back-to-back, no straight pipe in sight. Neither
            // should fold — they fall through to the normal aggregation
            // path and the bends become their own spec rows.
            var b1 = Bend(1, 1000);
            var b2 = Bend(2, 1000);
            var topology = new Dictionary<int, List<int>>
            {
                { 1, new List<int> { 2 } },
                { 2, new List<int> { 1 } },
            };
            var r = PipeBendFolder.Fold(new[] { b1, b2 }, topology);
            Assert.Empty(r.BendsConsumed);
            Assert.Empty(r.ExtraPipeMetres);
        }

        [Fact]
        public void Multiple_Bends_On_Same_Pipe_Sum()
        {
            var p = Pipe(1);
            var b1 = Bend(2, 1000);
            var b2 = Bend(3, 500);
            var topology = new Dictionary<int, List<int>>
            {
                { 1, new List<int> { 2, 3 } },
                { 2, new List<int> { 1 } },
                { 3, new List<int> { 1 } },
            };
            var r = PipeBendFolder.Fold(new[] { p, b1, b2 }, topology);
            Assert.Equal(2, r.BendsConsumed.Count);
            Assert.Equal(1.5, r.ExtraPipeMetres[1], 3);
        }

        [Fact]
        public void Zero_Length_Bend_Is_Skipped()
        {
            var p = Pipe(1);
            var b = Bend(2, 0);
            var topology = new Dictionary<int, List<int>>
            {
                { 1, new List<int> { 2 } },
                { 2, new List<int> { 1 } },
            };
            var r = PipeBendFolder.Fold(new[] { p, b }, topology);
            Assert.Empty(r.BendsConsumed);
        }

        [Fact]
        public void Topology_Spans_Drawings_So_Bend_Folds_Across_Scope()
        {
            // Simulates the user's scenario: bend in drawing A, pipe in
            // drawing B. Topology covers both because PartReader builds
            // it project-wide. Folder should still match the pair.
            var p = Pipe(100, 4000);  // imagine this lives in DWG_B
            var b = Bend(200, 800);   // imagine this lives in DWG_A
            var topology = new Dictionary<int, List<int>>
            {
                { 100, new List<int> { 200 } },
                { 200, new List<int> { 100 } },
            };
            var r = PipeBendFolder.Fold(new[] { p, b }, topology);
            Assert.Contains(200, r.BendsConsumed);
            Assert.Equal(0.8, r.ExtraPipeMetres[100], 3);
        }
    }
}
