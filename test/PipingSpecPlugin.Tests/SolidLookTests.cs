using System.Collections.Generic;
using System.Linq;
using PipingSpecPlugin.Solids;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    // PSTOSOLID: the colour a body's faces get is the one the object was
    // seen with (AutoCAD's ByBlock / ByLayer / layer 0 rules), and the bodies
    // are united pairwise without losing any.
    public class SolidLookTests
    {
        static readonly Dictionary<string, string> Layers = new Dictionary<string, string>
        {
            ["0"] = "white", ["Pipes"] = "green", ["Steel"] = "grey", ["КМ|Балки"] = "brown",
        };
        static string LayerColor(string name) => Layers.TryGetValue(name, out var c) ? c : "?";

        [Fact]
        public void AtTheTopByLayerIsTheLayersColourAndByBlockIsWhite()
        {
            Assert.Equal("green", SolidLook.Resolve(ColorSource.ByLayer, "red", "Pipes", null, LayerColor, "white").Color);
            Assert.Equal("white", SolidLook.Resolve(ColorSource.ByBlock, "red", "Pipes", null, LayerColor, "white").Color);
            Assert.Equal("red", SolidLook.Resolve(ColorSource.Own, "red", "Pipes", null, LayerColor, "white").Color);
            // Layer 0 at the top is just layer 0.
            Assert.Equal("white", SolidLook.Resolve(ColorSource.ByLayer, "red", "0", null, LayerColor, "white").Color);
        }

        [Fact]
        public void InsideABlockByBlockTakesTheInsertionsColour()
        {
            var insert = new Look<string>("Pipes", "magenta");
            Assert.Equal("magenta", SolidLook.Resolve(ColorSource.ByBlock, "x", "Steel", insert, LayerColor, "white").Color);
        }

        [Fact]
        public void OnLayerZeroInsideABlockAnObjectLivesOnTheInsertionsLayer()
        {
            var insert = new Look<string>("Pipes", "magenta");
            var look = SolidLook.Resolve(ColorSource.ByLayer, "x", "0", insert, LayerColor, "white");
            Assert.Equal("Pipes", look.Layer);
            Assert.Equal("green", look.Color);
            // Its own layer otherwise.
            Assert.Equal("grey", SolidLook.Resolve(ColorSource.ByLayer, "x", "Steel", insert, LayerColor, "white").Color);
        }

        [Fact]
        public void NestedBlocksPassByBlockAndLayerZeroDown()
        {
            var outer = SolidLook.Resolve(ColorSource.Own, "magenta", "Pipes", null, LayerColor, "white");
            var inner = SolidLook.Resolve(ColorSource.ByBlock, "x", "0", outer, LayerColor, "white");
            Assert.Equal("magenta", SolidLook.Resolve(ColorSource.ByBlock, "x", "0", inner, LayerColor, "white").Color);
            Assert.Equal("green", SolidLook.Resolve(ColorSource.ByLayer, "x", "0", inner, LayerColor, "white").Color);
        }

        [Fact]
        public void AnXrefsLayerIsLookedUpWithTheXrefsName()
        {
            Assert.Equal("КМ|Балки", SolidLook.HostLayerName("Балки", "КМ"));
            Assert.Equal("0", SolidLook.HostLayerName("0", "КМ"));
            Assert.Equal("Балки", SolidLook.HostLayerName("Балки", null));
        }

        sealed class Body { public List<int> Parts = new List<int>(); public bool Broken; }

        [Fact]
        public void EveryBodyEndsUpInTheUnionOrBesideIt()
        {
            var bodies = Enumerable.Range(1, 11).Select(i => new Body { Parts = { i }, Broken = i == 7 }).ToList();
            var calls = 0;
            var result = SolidLook.Unite(bodies, (into, body) =>
            {
                calls++;
                if (body.Broken || into.Broken) return false;
                into.Parts.AddRange(body.Parts);
                return true;
            });
            Assert.Equal(Enumerable.Range(1, 11), result.SelectMany(b => b.Parts).OrderBy(i => i));
            Assert.Equal(2, result.Count); // the broken one stays beside
            Assert.True(calls < 30);
        }

        [Fact]
        public void OneOrNoBodyIsLeftAsIs()
        {
            Assert.Empty(SolidLook.Unite(new List<Body>(), (a, b) => true));
            var one = new Body();
            Assert.Same(one, Assert.Single(SolidLook.Unite(new List<Body> { one }, (a, b) => true)));
        }
    }
}
