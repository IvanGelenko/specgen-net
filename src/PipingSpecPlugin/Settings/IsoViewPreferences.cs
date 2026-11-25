using System.Collections.Generic;

namespace PipingSpecPlugin.Settings;

public enum IsoViewMode
{
    Automatic,
    Common,
    PerLine
}

public sealed record IsoViewPreferences(IsoViewMode Mode,int CommonQuarterTurns,Dictionary<string,int> LineQuarterTurns);
