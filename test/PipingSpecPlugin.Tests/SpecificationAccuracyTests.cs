using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests;

public class SpecificationAccuracyTests
{
    [Fact] public void EmptyAndMissingDesignStandardUseSameProcurementGroup()
    {
        var a=Pipe(1,"1001");a.DesignStd=null;a.CompatibleStandard="ГОСТ 8732";
        var b=Pipe(2,"1001");b.DesignStd=" ";b.CompatibleStandard="ГОСТ 8732";
        Assert.Equal(2.1,Assert.Single(new SpecRowBuilder().Build(new[]{a,b})).Qty);
    }
    [Fact] public void ManyFoldedBendsDoNotAddPhantomDecimetre()
    {
        var records=new List<PartRecord>{Pipe(1)};
        var graph=new Dictionary<int,List<int>>();
        for(int id=2;id<=1001;id++)
        {
            var bend=Bend(id);bend.Length="300";
            records.Add(bend);graph[id]=new(){1};
        }
        var options=new SpecBuilderOptions{IncludePipeBendsAsPipe=true,Topology=graph};
        Assert.Equal(301,Assert.Single(new SpecRowBuilder(options).Build(records)).Qty);
    }
    [Theory]
    [InlineData("1000", "1.1")]
    [InlineData("1100", "1.3")]
    [InlineData("12300", "13.6")]
    [InlineData("10000", "11")]
    public void TenPercentReserveRoundsUpToOneDecimal(string millimetres, string expected)
    {
        var row=Assert.Single(new SpecRowBuilder().Build(new[]{Pipe(1,millimetres)}));
        var value=row.Note.Split("с учетом 10% запаса: ")[1].Split(" м")[0];
        // Same separator as the «Кол.» column (InvariantCulture point), whatever the OS culture.
        Assert.DoesNotContain(",",value);
        Assert.Equal(decimal.Parse(expected,CultureInfo.InvariantCulture),decimal.Parse(value,CultureInfo.InvariantCulture));
    }
    [Fact] public void NoStatusPickerOptionSelectsNullAndEmptyStatuses()
    {
        var a=Pipe(1); a.Status=null; var b=Pipe(2); b.Status="";
        var opts=new SpecBuilderOptions{Statuses=new[]{SpecConstants.NullStatus}};
        Assert.Equal(2,Assert.Single(new SpecRowBuilder(opts).Build(new[]{a,b,Pipe(3)})).Qty);
    }
    [Fact] public void BoltSetWithEmptyCategoryKeepsEveryBolt()
    {
        var parts = new[]{new PartRecord{PnPID=1,PnPClassName="BoltSet",Quantity="8"},
                          new PartRecord{PnPID=2,PnPClassName="BoltSet",Quantity="4"}};
        Assert.Equal(12,Assert.Single(new SpecRowBuilder().Build(parts)).Qty);
        Assert.Contains(ProjectFacts.From(parts).Categories,c=>c.key=="Fasteners");
    }

    [Theory] [InlineData("furlong")] [InlineData("unknown")]
    public void UnknownLengthUnitCannotSilentlyBecomeMillimetres(string unit)
    {
        // No longer aborts the spec: the pipe is left out of the main
        // section (never read as mm) and listed for review instead.
        var p=Pipe(1);p.LengthUnit=unit;
        var builder=new SpecRowBuilder();
        Assert.Empty(builder.Build(new[]{p}));
        Assert.Contains(builder.ReviewItems,i=>i.Reason.Contains("единица длины")&&i.PnPID==1);
    }
    static PartRecord Pipe(int id, string length = "1000") => new()
    {
        PnPID = id, PartCategory = "Pipe", PnPClassName = "Pipe",
        PartSizeLongDesc = "Pipe DN100", NominalDiameter = "100", NominalUnit = "mm",
        Length = length, LengthUnit = "mm", Material = "Steel", LineNumberTag = "L1",
        Status = "New", LinearWeight = "10", WeightUnit = "KG", LinearWeightUnit = "KG/M"
    };
    static PartRecord Bend(int id)
    {
        var p = Pipe(id, "500"); p.PnPClassName = "PipeBend"; p.PartSizeLongDesc = "Bend DN100";
        return p;
    }
    static Dictionary<int, List<int>> Graph() => new() { [1] = new() {2}, [2] = new() {1} };

    // The bug: with «Учитывать изгибы как трубу» off, bends whose catalogue
    // PartCategory was "Pipe" landed in «Трубы» in metres, while bends
    // authored as "Fittings" landed in «Детали трубопроводов» in pieces.
    // Now every bend is a piping detail counted in pieces.
    [Theory] [InlineData("Pipe")] [InlineData("Fittings")] [InlineData(null)]
    public void BendsWithoutFoldingAreFittingsCountedInPieces(string catalogueCategory)
    {
        var a=Bend(2); a.PartCategory=catalogueCategory;
        var b=Bend(3); b.PartCategory=catalogueCategory; b.Length="1500";
        var rows=new SpecRowBuilder(new SpecBuilderOptions{Topology=Graph()}).Build(new[]{Pipe(1),a,b});
        var bendRow=Assert.Single(rows,r=>r.Name=="Bend DN100");
        Assert.Equal("Fittings",bendRow.Category);
        Assert.Equal("шт.",bendRow.Unit);
        Assert.Equal(2,bendRow.Qty);
        var pipeRow=Assert.Single(rows,r=>r.Category=="Pipe");
        Assert.Equal(1,pipeRow.Qty);
    }

    [Fact] public void BendWithoutFoldingPassesPickerChipOfItsCatalogueCategory()
    {
        // Picker offers only the "Pipe" chip (no real fittings in project);
        // the bend must still reach the spec, as a fitting.
        var opts=new SpecBuilderOptions{Categories=new[]{"Pipe"}};
        var row=Assert.Single(new SpecRowBuilder(opts).Build(new[]{Bend(2)}));
        Assert.Equal("Fittings",row.Category); Assert.Equal(1,row.Qty);
    }

    [Fact] public void UnfoldedBendWithFoldingOnKeepsPipeBehaviour()
    {
        // No topology neighbour → nothing to fold into; option ON keeps the
        // historical behaviour (catalogue "Pipe" → metres).
        var opts=new SpecBuilderOptions{IncludePipeBendsAsPipe=true,Topology=new Dictionary<int,List<int>>()};
        var row=Assert.Single(new SpecRowBuilder(opts).Build(new[]{Bend(2)}));
        Assert.Equal("Pipe",row.Category); Assert.Equal("м",row.Unit); Assert.Equal(.5,row.Qty);
    }

    [Fact] public void RepeatBuildPreservesInputAndResult()
    {
        var extras = new Dictionary<int, double> { [1] = .5 };
        var builder = new SpecRowBuilder(new() {ExtraPipeMetres = extras});
        Assert.Equal(1.5, Assert.Single(builder.Build(new[]{Pipe(1)})).Qty);
        Assert.Equal(1.5, Assert.Single(builder.Build(new[]{Pipe(1)})).Qty);
        Assert.Equal(.5, extras[1]);
    }

    [Theory] [InlineData("0,3", "m", .3)] [InlineData("0.3", "m", .3)]
    [InlineData("12", "in", .4)] [InlineData("1", "ft", .4)]
    public void LengthUnitsAndDecimalSeparators(string length, string unit, double expected)
    {
        var p = Pipe(1, length); p.LengthUnit = unit;
        Assert.Equal(expected, Assert.Single(new SpecRowBuilder().Build(new[]{p})).Qty);
    }

    [Fact] public void ManySegmentsDoNotAddPhantomDecimetre()
    {
        var rows = Enumerable.Range(1, 10000).Select(id => Pipe(id, "100"));
        Assert.Equal(1000, Assert.Single(new SpecRowBuilder().Build(rows)).Qty);
    }

    [Fact] public void ImperialLinearWeightUsesItsOwnUnit()
    {
        var p = Pipe(1); p.LinearWeight = "10"; p.LinearWeightUnit = "LB/FT";
        Assert.Equal(14.882, Assert.Single(new SpecRowBuilder().Build(new[]{p})).Weight);
    }

    [Fact] public void PipeMassIsWeightedByLength()
    {
        var a = Pipe(1); var b = Pipe(2, "3000"); b.LinearWeight = "20";
        Assert.Equal(17.5, Assert.Single(new SpecRowBuilder().Build(new[]{a,b})).Weight);
    }

    [Fact] public void BoltMassIsWeightedByPieces()
    {
        var a = new PartRecord {PnPID=1, PartCategory="Fasteners", PartSizeLongDesc="Bolt", Quantity="4", Weight="4"};
        var b = new PartRecord {PnPID=2, PartCategory="Fasteners", PartSizeLongDesc="Bolt", Quantity="12", Weight="24"};
        var row = Assert.Single(new SpecRowBuilder().Build(new[]{a,b}));
        Assert.Equal(16, row.Qty); Assert.Equal(1.75, row.Weight);
    }

    [Theory] [InlineData("Infinity")] [InlineData("NaN")] [InlineData("1.5")] [InlineData("-2")] [InlineData("0")]
    public void InvalidPieceCountCannotBecomeSuccessfulDocument(string quantity)
    {
        var p = new PartRecord {PnPID=1,PartCategory="Fasteners",Quantity=quantity};
        var builder = new SpecRowBuilder();
        Assert.Empty(builder.Build(new[]{p}));
        Assert.Contains(builder.ReviewItems, i => i.Reason.Contains("Некорректное количество"));
    }

    [Fact] public void FilteredBendDoesNotDonateLength()
    {
        var bend = Bend(2); bend.Status = "Existing";
        var opts = new SpecBuilderOptions {Statuses=new[]{"New"}, IncludePipeBendsAsPipe=true, Topology=Graph()};
        Assert.Equal(1, Assert.Single(new SpecRowBuilder(opts).Build(new[]{Pipe(1),bend})).Qty);
    }

    [Fact] public void FilteredPipeDoesNotLoseSelectedBend()
    {
        var pipe = Pipe(1); pipe.Status = "Existing";
        var opts = new SpecBuilderOptions {Statuses=new[]{"New"}, IncludePipeBendsAsPipe=true, Topology=Graph()};
        var row = Assert.Single(new SpecRowBuilder(opts).Build(new[]{pipe,Bend(2)}));
        Assert.Equal(.5, row.Qty); Assert.Equal("Bend DN100",row.Name);
    }

    [Fact] public void ConnectorCanBeTraversedEvenWhenCategoryIsFiltered()
    {
        var connector = new PartRecord {PnPID=3,PnPClassName="P3dConnector"};
        var opts = new SpecBuilderOptions {Categories=new[]{"Pipe"},IncludePipeBendsAsPipe=true,
            Topology=new Dictionary<int,List<int>>{[2]=new(){3},[3]=new(){1,2}}};
        Assert.Equal(1.5, Assert.Single(new SpecRowBuilder(opts).Build(new[]{Pipe(1),Bend(2),connector})).Qty);
    }

    [Theory] [InlineData("diameter")] [InlineData("line")] [InlineData("material")]
    [InlineData("drawing")] [InlineData("wall")]
    public void BendCannotBecomeAnotherProcurementItem(string difference)
    {
        var pipe=Pipe(1); var bend=Bend(2);
        if(difference=="diameter") bend.NominalDiameter="50";
        if(difference=="line") bend.LineNumberTag="L2";
        if(difference=="material") bend.Material="Stainless";
        if(difference=="drawing") bend.SourceDwgPath="other.dwg";
        if(difference=="wall") bend.WallThickness="8";
        Assert.Empty(PipeBendFolder.Fold(new[]{pipe,bend},Graph()).BendsConsumed);
        var opts=new SpecBuilderOptions {IncludePipeBendsAsPipe=true,Topology=Graph()};
        Assert.Equal(1.5,new SpecRowBuilder(opts).Build(new[]{pipe,bend}).Sum(r=>r.Qty));
    }

    [Theory] [InlineData("NaN")] [InlineData("Infinity")]
    public void InvalidBendDoesNotPoisonPipe(string length)
    {
        var bend=Bend(2); bend.Length=length;
        Assert.Empty(PipeBendFolder.Fold(new[]{Pipe(1),bend},Graph()).BendsConsumed);
    }

    [Theory] [InlineData("wall")] [InlineData("pressure")] [InlineData("facing")]
    public void DifferentTechnicalPropertiesDoNotMerge(string difference)
    {
        var a=Pipe(1); var b=Pipe(2);
        if(difference=="wall") b.WallThickness="8";
        if(difference=="pressure") b.PressureClass="PN40";
        if(difference=="facing") b.Facing="RF";
        Assert.Equal(2,new SpecRowBuilder().Build(new[]{a,b}).Count);
    }

    [Fact] public void DrawingIdsPreservePartsAndIsolateTopologyAndExclusions()
    {
        var mapper=new DrawingPartIds(); var a=Pipe(1); var b=Pipe(1);
        var ownedA=new HashSet<int>{1}; var ownedB=new HashSet<int>();
        var graphA=Graph(); var graphB=Graph();
        mapper.Remap("A.dwg",new[]{a},ownedA,graphA);
        mapper.Remap("B.dwg",new[]{b},ownedB,graphB);
        Assert.NotEqual(a.PnPID,b.PnPID); Assert.Equal(1,a.SourcePnPID);
        Assert.Empty(graphA.Keys.Intersect(graphB.Keys));
        Assert.Equal(2,Assert.Single(new SpecRowBuilder().Build(new[]{a,b})).Qty);
        Assert.Equal(1,Assert.Single(new SpecRowBuilder(new(){EquipmentOwnedPnPIds=ownedA}).Build(new[]{a,b})).Qty);
    }

    [Theory] [InlineData("drawing")] [InlineData("line")] [InlineData("far")] [InlineData("feet")]
    public void CompanionSearchDoesNotDeleteUnrelatedGasket(string difference)
    {
        var valve=new PartRecord{PnPID=1,PartCategory="Valves"};
        var connector=new PartRecord{PnPID=2,PnPClassName="P3dConnector"};
        var flange=new PartRecord{PnPID=3,PartCategory="Flanges",NominalDiameter="100",PositionX="0",PositionY="0",PositionZ="0"};
        var gasket=new PartRecord{PnPID=4,PartCategory="Fasteners",ShortDescription="gasket",NominalDiameter="100",PositionX="0",PositionY="0",PositionZ="0"};
        if(difference=="drawing") gasket.SourceDwgPath="B.dwg";
        if(difference=="line") gasket.LineNumberTag="L2";
        if(difference=="far") gasket.PositionX="10000";
        // Drawing in feet: 1 unit apart is 304.8 mm — far beyond the 100 mm radius.
        if(difference=="feet") {gasket.DrawingMmPerUnit=304.8;flange.DrawingMmPerUnit=304.8;gasket.PositionX="1";}
        var graph=new Dictionary<int,List<int>>{[1]=new(){2},[2]=new(){1,3}};
        Assert.DoesNotContain(4,ValveCompanionFinder.Discover(new[]{valve,connector,flange,gasket},graph));
    }
}
