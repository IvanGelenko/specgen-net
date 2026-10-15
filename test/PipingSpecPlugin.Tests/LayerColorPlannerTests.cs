using PipingSpecPlugin.Layers;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    public class LayerColorPlannerTests
    {
        static LineLayerPart Part(string key, string kind, string line, string layer, string color) => new()
        { Key = key, ClassName = kind, Line = line, Layer = layer, Color = color };
        [Fact]
        public void ValveUsesItsLinePipeLayerNotAnotherLine()
        {
            var result = LayerColorPlanner.Build(new[] { Part("p", "Pipe", "L1", "pipes", "green"),
                Part("other", "Pipe", "L2", "other", "red"), Part("v", "Valve", "L1", "valves", "red") });
            var choice = Assert.Single(result); Assert.Equal("pipes", choice.SourceLayer); Assert.Null(choice.Warning);
        }
        [Fact]
        public void SameLayerValveStaysByLayer()
        {
            var choice = Assert.Single(LayerColorPlanner.Build(new[] { Part("p", "Pipe", "L", "same", "green"), Part("v", "Valve", "L", "same", "green") }));
            Assert.Equal("same", choice.SourceLayer);
        }
        [Fact]
        public void ConflictingColorsAreNotGuessed()
        {
            var choice = Assert.Single(LayerColorPlanner.Build(new[] { Part("p1", "Pipe", "L", "a", "green"),
                Part("p2", "Pipe", "L", "b", "red"), Part("v", "Valve", "L", "v", "yellow") }));
            Assert.Null(choice.SourceLayer); Assert.NotNull(choice.Warning);
        }
        [Fact]
        public void DifferentPipeLayersWithSameColorAreUnambiguous()
        {
            var choice = Assert.Single(LayerColorPlanner.Build(new[] { Part("p1", "Pipe", "L", "a", "rgb:20,70,130"),
                Part("p2", "PipeBend", "L", "b", "rgb:20,70,130"), Part("v", "Valve", "L", "v", "yellow") }));
            Assert.NotNull(choice.SourceLayer); Assert.Null(choice.Warning);
        }
        [Theory]
        [InlineData(null)][InlineData("no-pipes")]
        public void MissingLineOrPipesLeavesValveByItsOwnLayer(string line)
        {
            var choice = Assert.Single(LayerColorPlanner.Build(new[] { Part("v", "Valve", line, "v", "red") }));
            Assert.Null(choice.SourceLayer); Assert.NotNull(choice.Warning);
        }
    }
}
