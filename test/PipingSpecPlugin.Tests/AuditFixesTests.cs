using System;
using System.Collections.Generic;
using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests;

// Regressions for the post-audit fixes in Spec/ (the PartReader fixes need
// a live AutoCAD and are covered only by the shared CategoryClassifier gate).
public class AuditFixesTests
{
    // ---- equipment-ownership guard ------------------------------------
    [Theory]
    [InlineData("Pipe", true)] [InlineData("Fittings", true)]
    [InlineData("Valves", true)] [InlineData("flanges", true)]
    [InlineData("Nozzles", false)] [InlineData("Equipment", false)]
    [InlineData("P3dConnector", false)] [InlineData("Supports", false)]
    [InlineData("", false)] [InlineData(null, false)]
    public void ProcurementCategoriesAreNeverEquipmentOwned(string category, bool keep)
        => Assert.Equal(keep, CategoryClassifier.IsProcurementCategory(category));

    // ---- lazy unit factors --------------------------------------------
    [Fact] public void UnknownLengthUnitOnValveDoesNotAbortSpec()
    {
        var v = new PartRecord { PnPID = 1, PartCategory = "Valves", PartSizeLongDesc = "Valve", LengthUnit = "furlong" };
        Assert.Equal(1, Assert.Single(new SpecRowBuilder().Build(new[] { v })).Qty);
    }

    [Fact] public void UnknownWeightUnitWithoutWeightDoesNotAbortSpec()
    {
        var v = new PartRecord { PnPID = 1, PartCategory = "Valves", PartSizeLongDesc = "Valve", WeightUnit = "stone" };
        Assert.Equal(1, Assert.Single(new SpecRowBuilder().Build(new[] { v })).Qty);
    }

    [Fact] public void UnknownWeightUnitWithWeightKeepsPartWithoutMassAndFlagsIt()
    {
        // The quantity is reliable, only the mass is not: the valve stays in
        // its section without mass, and the unit problem is flagged once
        // (not a second time as a generic "no mass").
        var v = new PartRecord { PnPID = 1, PartCategory = "Valves", PartSizeLongDesc = "Valve", WeightUnit = "stone", Weight = "5" };
        var builder = new SpecRowBuilder();
        var row = Assert.Single(builder.Build(new[] { v }));
        Assert.Equal(1, row.Qty);
        Assert.Equal(0, row.Weight);
        var item = Assert.Single(builder.ReviewItems);
        Assert.Contains("«stone»", item.Reason);
    }

    // ---- piece-count multiplier only for fasteners ---------------------
    [Fact] public void FlangeCountAttributeIsNotAPieceMultiplier()
    {
        var f = PartPropertyReader.Build(1, new Dictionary<string, string>
            { { "PnPClassName", "Flange" }, { "PartCategory", "Flanges" }, { "PartSizeLongDesc", "Flange DN100" }, { "Count", "8" } });
        Assert.Equal(1, Assert.Single(new SpecRowBuilder().Build(new[] { f })).Qty);
    }

    [Fact] public void FlangeWithNonIntegerCountDoesNotAbortSpec()
    {
        var f = new PartRecord { PnPID = 1, PartCategory = "Flanges", PartSizeLongDesc = "Flange", Quantity = "4.5" };
        Assert.Equal(1, Assert.Single(new SpecRowBuilder().Build(new[] { f })).Qty);
    }

    [Fact] public void BoltSetCountStillMultiplies()
    {
        var b = PartPropertyReader.Build(1, new Dictionary<string, string>
            { { "PnPClassName", "BoltSet" }, { "PartCategory", "Fasteners" }, { "NumberInSet", "8" } });
        Assert.Equal(8, Assert.Single(new SpecRowBuilder().Build(new[] { b })).Qty);
    }

    // ---- ON-mode orphan bend section ----------------------------------
    [Theory] [InlineData("Pipe")] [InlineData("Fittings")] [InlineData(null)]
    public void OrphanBendWithFoldingOnIsPipeInMetresWhateverItsCatalogueCategory(string catalogueCategory)
    {
        var bend = new PartRecord { PnPID = 2, PartCategory = catalogueCategory, PnPClassName = "PipeBend",
            PartSizeLongDesc = "Bend DN100", NominalDiameter = "100", Length = "500", LengthUnit = "mm" };
        var opts = new SpecBuilderOptions { IncludePipeBendsAsPipe = true, Topology = new Dictionary<int, List<int>>() };
        var row = Assert.Single(new SpecRowBuilder(opts).Build(new[] { bend }));
        Assert.Equal("Pipe", row.Category); Assert.Equal("м", row.Unit); Assert.Equal(.5, row.Qty);
    }

    // ---- audit #15: orphan reroute key rounding ------------------------
    [Fact] public void OrphanRerouteMatchesDespiteNdFloatNoise()
    {
        var pipe = new PartRecord { PnPID = 1, PartCategory = "Pipe", PnPClassName = "Pipe", PartSizeLongDesc = "Pipe",
            NominalDiameter = "4.0", Length = "1000", LengthUnit = "mm", LineNumberTag = "L1" };
        var opts = new SpecBuilderOptions
        {
            ExtraPipeMetres = new Dictionary<int, double> { [99] = .5 },
            ExtraPipeLineTags = new Dictionary<int, string> { [99] = "L1" },
            ExtraPipeOrphanInfo = new Dictionary<int, BendOrphanInfo>
                { [99] = new BendOrphanInfo { LineTag = "L1", NominalDiameter = 3.9999, Spec = "", Material = "" } },
        };
        Assert.Equal(1.5, Assert.Single(new SpecRowBuilder(opts).Build(new[] { pipe })).Qty);
    }
}
