using System.Linq;
using PipingSpecPlugin.Tracing;
using Xunit;

namespace PipingSpecPlugin.Tests;

public class TracingTopologyTests
{
    [Theory]
    [InlineData(2.553, 0.531, 337.826, true)]
    [InlineData(2.553, 0.531, -337.826, true)]
    [InlineData(20, 0, 1000, false)]
    public void SlightlyTiltedPlantRiserUsesRiserRouting(double dx, double dy, double dz, bool expected)
    {
        double axisZ = dz / System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        Assert.Equal(expected, TracingTopology.IsRiserAxis(axisZ));
    }

    [Theory]
    [InlineData(127, 155, 275, 127, 138)]
    [InlineData(90, 75, 100, 50, 40)]
    public void ShortBypassRiserKeepsRoomBetweenRightAngleBends(double lower, double upper, double span, double expectedLower, double expectedUpper)
    {
        Assert.True(TracingTopology.TryFitBypassRiser(lower, upper, span, out var first, out var second));
        Assert.Equal(expectedLower, first, 6);
        Assert.Equal(expectedUpper, second, 6);
    }

    [Theory]
    [InlineData(-40, -80, 95, 13.5, -93.5)]
    [InlineData(40, -80, 95, 13.5, 108.5)]
    [InlineData(120, -80, 95, 13.5, 120)]
    public void TracerClearsHorizontalFittingsOnItsOwnSide(double current, double near, double far, double gap, double expected)
    {
        Assert.Equal(expected, TracingTopology.SideOfFitting(current, near, far, gap));
    }

    [Theory]
    [InlineData(52, 52, 80, true)]
    [InlineData(-40, 40, 0, false)]
    [InlineData(-18, 62, 0, true)]
    public void BypassPairMustBeCenteredAcrossTheCarrier(double firstSide, double secondSide, double heightDifference, bool expected)
    {
        Assert.Equal(expected, TracingTopology.NeedsBranchCentering(firstSide, secondSide, heightDifference));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(-1, true)]
    [InlineData(0.96, false)]
    [InlineData(0.002, false)]
    public void OnlyVerticalSectionsAreExemptFromUnderPipeRouting(double axisZ, bool expected)
    {
        Assert.Equal(expected, TracingTopology.IsVertical(axisZ));
    }

    [Theory]
    [InlineData(853, 789, 724, 13.6, 142.6)]
    [InlineData(680, 690, 724, 13.6, 0)]
    public void BothBypassLegsClearTheBottomOfTheFitting(double firstZ, double secondZ, double bottomZ, double clearance, double expected)
    {
        Assert.Equal(expected, TracingTopology.DropBelowFitting(firstZ, secondZ, bottomZ, clearance), 6);
    }

    [Fact]
    public void ReturningBranchIsRecognizedOnlyAtADifferentRunJunction()
    {
        Assert.True(TracingTopology.IsBypassReturn("8772", "87A4", "S3", "S1", "S2"));
        Assert.False(TracingTopology.IsBypassReturn("8772", "8772", "D1", "S1", "S2"));
        Assert.False(TracingTopology.IsBypassReturn("8772", "87A4", "S2", "S1", "S2"));
    }
    [Fact]
    public void Ksn110ShiftExceedsTheAvailableRoomAfterTheValve()
    {
        double room = 562 - 461 - 10;
        Assert.False(TracingTopology.ShiftFits(96.7, 90, System.Math.PI / 12, room));
        Assert.False(TracingTopology.TryCompactShift(96.7, 40, room, out _));
        Assert.True(TracingTopology.TryCompactShift(96.7, 40, 110, out _));
    }
    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    public void AShortRunCanHoldItsOffsetOnlyBetweenCompatibleRisers(bool previousRiser, bool nextRiser, bool compatible, bool expected)
    {
        Assert.Equal(expected, TracingTopology.CanHoldShortRun(previousRiser, nextRiser, compatible));
    }

    [Fact]
    public void RouteKeyIsStableAcrossStartingPipesAndSeparatesDistinctRoutes()
    {
        Assert.Equal(TracingTopology.RouteKey(new[] { "5512", "532D" }), TracingTopology.RouteKey(new[] { "532d", "5512" }));
        Assert.NotEqual(TracingTopology.RouteKey(new[] { "5512", "532D" }), TracingTopology.RouteKey(new[] { "6090", "610D" }));
    }
    [Theory]
    [InlineData(90, 130, 90, false)]
    [InlineData(90, 130, 40, true)]
    public void AdjacentBendsNeedRoomForBothTangents(double firstRadius, double distance, double secondRadius, bool fits)
    {
        var rightAngle = System.Math.PI / 2;
        Assert.Equal(fits, TracingTopology.BendsFit(distance, firstRadius, rightAngle, secondRadius, rightAngle));
    }

    [Fact]
    public void RoutePortsExcludeSupportAndSymbolicPortsOnKsn109()
    {
        var ports = new[] { "S1", "S2", "D1", "D10", "Y1", "Y2" };
        Assert.Equal(new[] { "S1", "S2" }, ports.Where(TracingTopology.IsRoutePort));
    }

    [Fact]
    public void TightRiserUsesLocalRadius40AndFitsWithoutStraightLegs()
    {
        Assert.False(TracingTopology.TryCompactShift(37, 90, 70, out _));
        Assert.True(TracingTopology.TryCompactShift(37, 40, 70, out var angle));
        Assert.InRange(2 * 40 * System.Math.Tan(angle / 2) + 37 / System.Math.Tan(angle), 0, 70);
        Assert.False(TracingTopology.TryCompactShift(37, 40, 65, out _));
    }

    [Theory]
    [InlineData("1111", "0-1,1-2,2-3,3-0")]
    [InlineData("1011", "2-3,3-0")]
    [InlineData("0110", "1-2")]
    [InlineData("1010", "")]
    [InlineData("0000", "")]
    public void OnlyConnectsOriginalNeighborsThatWereCreated(string present, string expected)
    {
        var joints = TracingTopology.Joints(present.Select(p => p == '1').ToArray());
        Assert.Equal(expected, string.Join(",", joints.Select(j => $"{j.From}-{j.To}")));
    }
}
