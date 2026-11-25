using IsoGenerator.Core;

namespace IsoGenerator.AutoCAD;

internal sealed record ArchivedLayoutOptions(int QuarterTurns,bool IgnoreBreakPoints,bool InstrumentCalloutsOnly,
    double MinimumPipeLength,double SymbolScale);

internal sealed record ArchivedLine(string Line,PipelineSnapshot Snapshot,IReadOnlyList<PlannedSheet> Sheets,
    ArchivedLayoutOptions? Options=null);

internal sealed record AppendArchive(int Version,string Profile,string PdfHash,ArchivedLine[] Lines,
    int ValveKitBomVersion=0,int LayoutVersion=0,string? MappingHash=null)
{
    public bool CanReuseLayout(int layoutVersion,string mappingHash,bool excludeHardware,int valveKitBomVersion)=>
        LayoutVersion==layoutVersion&&StringComparer.Ordinal.Equals(MappingHash,mappingHash)&&
        (!excludeHardware||ValveKitBomVersion==valveKitBomVersion);
}
