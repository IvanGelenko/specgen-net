using System.Collections.Generic;
using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests;

// Bends whose Д1/Д2 tails are swapped (same bend routed the other way) and
// whose computed arc length differs only by float noise are ONE procurement
// item and must land on one spec row.
public class BendGroupingTests
{
    const string Forward  = "Изгиб трубы, радиус гиба = 40 мм, угол гиба = 180°, Д1=0 мм, Д2=100мм, длина=325.7 мм, труба 25,0x4,0 ст.20";
    const string Backward = "Изгиб трубы, радиус гиба = 40 мм, угол гиба = 180°, Д1=100 мм, Д2=0мм, длина=325.7 мм, труба 25,0x4,0 ст.20";

    static PartRecord Bend(int id, string desc, string length = "325.7") => new()
    {
        PnPID = id, PartCategory = "Pipe", PnPClassName = "PipeBend",
        PartSizeLongDesc = desc, NominalDiameter = "20", NominalUnit = "mm",
        Length = length, LengthUnit = "mm", Material = "ст.20", LineNumberTag = "L1",
        Status = "New", Weight = "0.7", WeightUnit = "kg",
    };

    [Fact] public void SwappedTailsGroupIntoOneRowWithSummedQuantity()
    {
        var rows = new SpecRowBuilder().Build(new[] { Bend(1, Forward), Bend(2, Backward), Bend(3, Backward) });
        var row = Assert.Single(rows);
        Assert.Equal(3, row.Qty);
        Assert.Equal("шт.", row.Unit);
        Assert.Equal("Fittings", row.Category);
        // Display is the sorted (smaller-first) form — deterministic,
        // independent of which instance was read first.
        Assert.Equal(Forward, row.Name);
    }

    [Fact] public void DisplayIsSortedEvenWhenOnlyBackwardInstancesExist()
    {
        var row = Assert.Single(new SpecRowBuilder().Build(new[] { Bend(1, Backward) }));
        Assert.Contains("Д1=0 мм, Д2=100мм", row.Name);
    }

    [Fact] public void DifferentTailsStaySeparate()
    {
        var other = Forward.Replace("Д2=100мм", "Д2=150мм");
        Assert.Equal(2, new SpecRowBuilder().Build(new[] { Bend(1, Forward), Bend(2, other) }).Count);
    }

    [Theory]
    [InlineData("325.70", "325.7")]
    [InlineData("325.7000001", "325.7")]
    [InlineData("325.66", "325.7")]
    [InlineData("325.75", "325.8")]
    [InlineData("325.0", "325")]
    [InlineData("325,74", "325,7")]
    public void LengthInNameIsRoundedToOneDecimal(string raw, string expected)
    {
        Assert.Equal($"Изгиб трубы, Д1=0 мм, Д2=100мм, длина={expected} мм",
            BendNameNormalizer.Normalize($"Изгиб трубы, Д1=0 мм, Д2=100мм, длина={raw} мм"));
    }

    // Real project template (from the user's screenshot): uppercase,
    // two-word length token "ДЛИНА ТРУБЫ", spaces around every "=".
    const string Real = "ИЗГИБ ТРУБЫ: Р = {0}, У = {1} град, Д1 = {2}, Д2 = {3}, ДЛИНА ТРУБЫ = {4}, Труба 25х4 ГОСТ 8734-75/В 20 ГОСТ 8733-74";
    static string R(string r, string u, string d1, string d2, string len) => string.Format(Real, r, u, d1, d2, len);

    [Theory]
    [InlineData("130", "180", "100", "100", "608.407",  "608.4")]
    [InlineData("40",  "180", "0",   "100", "325.6637", "325.7")]
    [InlineData("90",  "90",  "0",   "100", "241.3717", "241.4")]
    [InlineData("90",  "180", "0",   "100", "570.7079", "570.7")]
    public void RealTemplateLengthIsRounded(string r, string u, string d1, string d2, string raw, string expected)
    {
        Assert.Equal(R(r, u, d1, d2, expected), BendNameNormalizer.Normalize(R(r, u, d1, d2, raw)));
    }

    [Fact] public void RealTemplateSwappedTailsAreSorted()
    {
        Assert.Equal(R("90", "90", "0", "100", "241.4"),
            BendNameNormalizer.Normalize(R("90", "90", "100", "0", "241.3717")));
    }

    [Fact] public void RealTemplateSwappedTailsGroupIntoOneRow()
    {
        var rows = new SpecRowBuilder().Build(new[]
        {
            Bend(1, R("90", "90", "0", "100", "241.3717"), "241.3717"),
            Bend(2, R("90", "90", "100", "0", "241.3717"), "241.3717"),
        });
        var row = Assert.Single(rows);
        Assert.Equal(2, row.Qty);
        Assert.Equal(R("90", "90", "0", "100", "241.4"), row.Name);
    }

    [Theory]
    [InlineData("длина=325.66 мм",         "длина=325.7 мм")]
    [InlineData("ДЛИНА ТРУБЫ=325.66",      "ДЛИНА ТРУБЫ=325.7")]
    [InlineData("Длина  трубы  =  325.66", "Длина  трубы  =  325.7")]
    public void LengthTokenVariantsAreAllRecognised(string raw, string expected)
    {
        Assert.Equal("Изгиб, " + expected, BendNameNormalizer.Normalize("Изгиб, " + raw));
    }

    [Fact] public void LengthNoiseDoesNotSplitTheGroup()
    {
        var a = Bend(1, Forward.Replace("325.7", "325.68"));
        var b = Bend(2, Backward.Replace("325.7", "325.71"));
        var row = Assert.Single(new SpecRowBuilder().Build(new[] { a, b }));
        Assert.Equal(2, row.Qty);
        Assert.Equal(Forward, row.Name);
    }

    [Fact] public void NonBendNamesAreNotTouched()
    {
        // Same text on a non-bend must not be rewritten — normalisation is
        // scoped to PipeBend only.
        var p = Bend(1, Backward); p.PnPClassName = "Elbow"; p.PartCategory = "Fittings";
        Assert.Equal(Backward, Assert.Single(new SpecRowBuilder().Build(new[] { p })).Name);
    }

    [Fact] public void DescriptionsWithoutTailsPassThrough()
    {
        Assert.Equal("Bend DN100", BendNameNormalizer.Normalize("Bend DN100"));
        Assert.Null(BendNameNormalizer.Normalize(null));
    }

    [Fact] public void SwappedTailsAlsoGroupWhenFoldingIsOnButBendsAreOrphans()
    {
        // ON mode, no neighbour pipe → bends stay as their own pipe rows in
        // metres; swapped tails must still merge.
        var opts = new SpecBuilderOptions { IncludePipeBendsAsPipe = true, Topology = new Dictionary<int, List<int>>() };
        var row = Assert.Single(new SpecRowBuilder(opts).Build(new[] { Bend(1, Forward, "500"), Bend(2, Backward, "500") }));
        Assert.Equal("Pipe", row.Category);
        Assert.Equal(1, row.Qty);
    }
}
