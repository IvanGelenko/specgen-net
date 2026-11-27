using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PipingSpecPlugin.Tracing;

internal static class TracingTopology
{
    internal static bool IsRoutePort(string name) =>
        !string.IsNullOrEmpty(name) && !Regex.IsMatch(name, @"^[DY]\d+$");

    internal static bool CanHoldShortRun(bool previousRiser, bool nextRiser, bool compatible) => previousRiser && nextRiser && compatible;

    internal static bool IsBypassReturn(string source, string target, string port, string throughIn, string throughOut) =>
        source != target && !string.IsNullOrEmpty(port) && port != throughIn && port != throughOut;

    internal static string RouteKey(IEnumerable<string> handles) =>
        Hash(string.Join(",", handles.Select(h => h.ToUpperInvariant()).Distinct().OrderBy(h => h, StringComparer.Ordinal)));

    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static double DropBelowFitting(double firstZ, double secondZ, double bottomZ, double clearance) =>
        Math.Max(0, Math.Max(firstZ, secondZ) + clearance - bottomZ);

    internal static bool IsVertical(double axisZ) => Math.Abs(axisZ) >= 1 - 1e-8;

    // Allow up to 10 mm of horizontal drift per metre of rise.
    internal static bool IsRiserAxis(double axisZ) =>
        Math.Abs(axisZ) >= 1 / Math.Sqrt(1 + 0.01 * 0.01);

    internal static bool NeedsBranchCentering(double firstSide, double secondSide, double heightDifference) =>
        Math.Abs((firstSide + secondSide) * 0.5) > 0.5 || Math.Abs(heightDifference) > 0.5;

    internal static double SideOfFitting(double current, double left, double right, double clearance) =>
        current < 0 ? Math.Min(current, left - clearance) : Math.Max(current, right + clearance);

    internal static bool TryFitBypassRiser(double lower, double upper, double span, out double first, out double second)
    {
        const double minimumRadius = 40;
        const double straightBetween = 10;
        first = Math.Min(lower, span - straightBetween - minimumRadius);
        second = Math.Min(upper, span - straightBetween - first);
        return first >= minimumRadius && second >= minimumRadius;
    }

    internal static bool TryCompactShift(double offset, double radius, double room, out double angle)
    {
        // The tightest shift uses two touching arcs, up to a 60-degree turn.
        angle = Math.Acos(Math.Max(0.5, 1 - offset / (2 * radius)));
        if (angle <= 0) return false;
        return ShiftFits(offset, radius, angle, room);
    }

    internal static bool ShiftFits(double offset, double radius, double angle, double room) =>
        offset / Math.Tan(angle) + 2 * TangentLength(radius, angle) <= room + 1e-6;

    internal static double TangentLength(double radius, double angle) => radius * Math.Tan(angle / 2);

    internal static bool BendsFit(double distance, double firstRadius, double firstAngle, double secondRadius, double secondAngle) =>
        TangentLength(firstRadius, firstAngle) + TangentLength(secondRadius, secondAngle) <= distance + 1e-6;

    internal static IEnumerable<(int From, int To)> Joints(IReadOnlyList<bool> present)
    {
        for (int from = 0; from < present.Count; from++)
        {
            int to = (from + 1) % present.Count;
            if (from != to && present[from] && present[to]) yield return (from, to);
        }
    }
}
