using System.Collections.Generic;
using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests;

public class PartPropertyReaderTests
{
    [Fact] public void MissingBoltSetCountCannotBecomeOneBolt()
    {
        var record=PartPropertyReader.Build(1,new Dictionary<string,string>{{"PnPClassName","BoltSet"}});
        var builder=new SpecRowBuilder();
        Assert.Empty(builder.Build(new[]{record}));
        Assert.Contains(builder.ReviewItems,i=>i.Reason.Contains("количество болтов"));
    }
    [Theory] [InlineData(null)] [InlineData("")] [InlineData("  ")]
    public void EmptyLengthFallsBackToCutLength(string length)
    {
        var record=PartPropertyReader.Build(1,new Dictionary<string,string>
            {{"PnPClassName","Pipe"},{"Length",length},{"CutLength","1500"}});
        Assert.Equal("1500",record.Length);
        Assert.Equal(1.5,Assert.Single(new SpecRowBuilder().Build(new[]{record})).Qty);
    }

    [Fact] public void BendUsesArcLengthInsteadOfGenericLengthPlaceholder()
    {
        var record=PartPropertyReader.Build(1,new Dictionary<string,string>
            {{"PnPClassName","PipeBend"},{"Length","0"},{"CutLength","1500"}});
        Assert.Equal("1500",record.Length);
    }

    [Fact] public void EmptyNumberInSetDoesNotHideAlternativeBoltCount()
    {
        var record=PartPropertyReader.Build(1,new Dictionary<string,string>
            {{"PnPClassName","BoltSet"},{"NumberInSet",""},{"Quantity"," "},{"NumBolts","8"}});
        Assert.Equal(8,Assert.Single(new SpecRowBuilder().Build(new[]{record})).Qty);
    }

    [Fact] public void PropertyNamesIgnoreCaseWithoutDiscardingValues()
    {
        var record=PartPropertyReader.Build(1,new Dictionary<string,string>
            {{"pnpclassname","Pipe"},{"length","1200"},{"lengthunit","mm"}});
        Assert.Equal(1.2,Assert.Single(new SpecRowBuilder().Build(new[]{record})).Qty);
    }

    [Fact] public void ExplicitInvalidCountDoesNotSilentlyUseAnotherField()
    {
        var record=PartPropertyReader.Build(1,new Dictionary<string,string>
            {{"PnPClassName","BoltSet"},{"NumberInSet","invalid"},{"Quantity","8"}});
        Assert.Equal("invalid",record.Quantity);
        var builder=new SpecRowBuilder();
        Assert.Empty(builder.Build(new[]{record}));
        Assert.Contains(builder.ReviewItems,i=>i.Reason.Contains("«invalid»"));
    }
}
