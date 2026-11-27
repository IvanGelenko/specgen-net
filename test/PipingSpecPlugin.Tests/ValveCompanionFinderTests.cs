using System.Collections.Generic;
using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    public class ValveCompanionFinderTests
    {
        static PartRecord Make(int pid, string category, string pnpClass,
            string desc = "", double x = 0, double y = 0, double z = 0)
            => new PartRecord
            {
                PnPID = pid,
                PartCategory = category,
                PnPClassName = pnpClass,
                ShortDescription = desc,
                PartFamilyLongDesc = desc,
                NominalDiameter = "4",
                Spec = "CS150",
                LengthUnit = "mm",
                PositionX = x.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PositionY = y.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PositionZ = z.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };

        [Fact]
        public void Walks_Valve_To_Connector_To_Flange()
        {
            // Valve(1) — Connector(2) — Flange(3); flange should be flagged.
            var v = Make(1, "Valves", "Valve");
            var c = Make(2, "", "P3dConnector");
            var f = Make(3, "Flanges", "Flange");
            var topology = new Dictionary<int, List<int>>
            {
                { 1, new List<int> { 2 } },
                { 2, new List<int> { 1, 3 } },
                { 3, new List<int> { 2 } },
            };
            var drop = ValveCompanionFinder.Discover(new[] { v, c, f }, topology);
            Assert.Contains(3, drop);
        }

        [Fact]
        public void Never_Drops_A_Pipe_Even_If_Wrongly_Suspected()
        {
            // Edge case from a bug we hit on live data: a Pipe sneaked into
            // the bolt fallback bucket and the whole run vanished. The
            // final safety pass must filter out Pipe / Fittings / Valves
            // / Equipment regardless of how they landed in rawDrop.
            var v = Make(1, "Valves", "Valve");
            var c = Make(2, "", "P3dConnector");
            var f = Make(3, "Flanges", "Flange");
            var pipe = Make(4, "Pipe", "Pipe");
            // Topology has pipe as a neighbour of the connector too —
            // simulates the false-positive walk.
            var topology = new Dictionary<int, List<int>>
            {
                { 1, new List<int> { 2 } },
                { 2, new List<int> { 1, 3, 4 } },
                { 3, new List<int> { 2 } },
                { 4, new List<int> { 2 } },
            };
            var drop = ValveCompanionFinder.Discover(new[] { v, c, f, pipe }, topology);
            Assert.Contains(3, drop);
            Assert.DoesNotContain(4, drop);
        }

        [Fact]
        public void Excludes_Only_Gasket_And_Bolt_Of_Valve_Joint()
        {
            var v = Make(1, "Valves", "Valve");
            var c = Make(2, "", "P3dConnector");
            var f = Make(3, "Flanges", "Flange", x: 0, y: 0, z: 0);
            var nearGasket = Make(10, "Fasteners", "Gasket", desc: "Gasket near",
                                  x: 5000, y: 0, z: 0);
            var farGasket  = Make(11, "Fasteners", "Gasket", desc: "Gasket far",
                                  x: 5, y: 0, z: 0);
            var nearBolt = Make(20, "Fasteners", "BoltSet", desc: "BoltSet near",
                                x: 5000, y: 0, z: 0);
            var farBolt  = Make(21, "Fasteners", "BoltSet", desc: "BoltSet far",
                                x: 5, y: 0, z: 0);
            var otherJoint = Make(30, "", "P3dConnector");
            var topology = new Dictionary<int, List<int>>
            {
                { 1, new List<int> { 2 } },
                { 2, new List<int> { 1, 3, 10, 20 } },
                { 3, new List<int> { 2 } },
                { 30, new List<int> { 11, 21 } },
            };
            var drop = ValveCompanionFinder.Discover(
                new[] { v, c, f, nearGasket, farGasket, nearBolt, farBolt, otherJoint }, topology);
            Assert.Contains(3, drop);
            Assert.Contains(10, drop);
            Assert.Contains(20, drop);
            Assert.DoesNotContain(11, drop);
            Assert.DoesNotContain(21, drop);
        }

        [Fact]
        public void Keeps_Equidistant_Gaskets_Without_Joint_Links()
        {
            PartRecord[] Scene(bool lowFirst)
            {
                var v = Make(1, "Valves", "Valve");
                var c = Make(2, "", "P3dConnector");
                var f = Make(3, "Flanges", "Flange", x: 0, y: 0, z: 0);
                // Both gaskets exactly 5 units away (opposite sides).
                var gLow  = Make(10, "Fasteners", "Gasket", desc: "Gasket", x: 5, y: 0, z: 0);
                var gHigh = Make(11, "Fasteners", "Gasket", desc: "Gasket", x: -5, y: 0, z: 0);
                return lowFirst
                    ? new[] { v, c, f, gLow, gHigh }
                    : new[] { v, c, f, gHigh, gLow };
            }
            var topo = new Dictionary<int, List<int>>
            {
                { 1, new List<int> { 2 } },
                { 2, new List<int> { 1, 3 } },
                { 3, new List<int> { 2 } },
            };
            var dropA = ValveCompanionFinder.Discover(Scene(lowFirst: true), topo);
            var dropB = ValveCompanionFinder.Discover(Scene(lowFirst: false), topo);
            Assert.DoesNotContain(10, dropA);
            Assert.DoesNotContain(10, dropB);
            Assert.DoesNotContain(11, dropA);
            Assert.DoesNotContain(11, dropB);
        }

        [Fact]
        public void No_Valves_Returns_Empty_Set()
        {
            var f = Make(1, "Flanges", "Flange");
            var p = Make(2, "Pipe", "Pipe");
            var topology = new Dictionary<int, List<int>>();
            var drop = ValveCompanionFinder.Discover(new[] { f, p }, topology);
            Assert.Empty(drop);
        }
    }
}
