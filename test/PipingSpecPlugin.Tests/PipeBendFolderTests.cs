using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        public void Bend_Through_Connector_Folds_To_Pipe_Two_Hops_Away()
        {
            // The realistic Plant 3D wiring: bend touches a P3dConnector
            // (weld stub) which then touches the pipe. The folder must
            // walk through the connector hop to find the pipe.
            var pipe = Pipe(100);
            var connector = new PartRecord
            {
                PnPID = 200,
                PartCategory = "",
                PnPClassName = "P3dConnector",
            };
            var bend = Bend(300, 1500);
            var topology = new Dictionary<int, List<int>>
            {
                { 100, new List<int> { 200 } },
                { 200, new List<int> { 100, 300 } },
                { 300, new List<int> { 200 } },
            };
            var r = PipeBendFolder.Fold(new[] { pipe, connector, bend }, topology);
            Assert.Contains(300, r.BendsConsumed);
            Assert.Equal(1.5, r.ExtraPipeMetres[100], 3);
        }

        [Fact]
        public void Two_Bends_On_One_Line_Through_Connectors_Both_Fold()
        {
            // Mirrors the user's 'азот' test case: pipe — connector —
            // bend — connector — pipe. The bend should fold into the
            // first pipe found in either direction.
            var pipeA = Pipe(1);
            var connA = new PartRecord { PnPID = 2, PnPClassName = "P3dConnector" };
            var bend = Bend(3, 2000);
            var connB = new PartRecord { PnPID = 4, PnPClassName = "P3dConnector" };
            var pipeB = Pipe(5);
            var topology = new Dictionary<int, List<int>>
            {
                { 1, new List<int> { 2 } },
                { 2, new List<int> { 1, 3 } },
                { 3, new List<int> { 2, 4 } },
                { 4, new List<int> { 3, 5 } },
                { 5, new List<int> { 4 } },
            };
            var r = PipeBendFolder.Fold(
                new[] { pipeA, connA, bend, connB, pipeB }, topology);
            Assert.Contains(3, r.BendsConsumed);
            // Folder picks the first pipe it finds via Pass 2; here that's
            // pipeA (PnPID 1) because it's iterated before pipeB. Either
            // pipe absorbing the bend is correct as long as exactly one
            // does so.
            var totalExtra = r.ExtraPipeMetres.Values.Sum();
            Assert.Equal(2.0, totalExtra, 3);
            Assert.Single(r.ExtraPipeMetres);
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
