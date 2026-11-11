namespace IsoGenerator.Core;
public sealed record IsoBlockDefinition(string Name,IReadOnlyDictionary<string,SheetPoint> Ports,SheetPoint Minimum,SheetPoint Maximum,IReadOnlyList<IReadOnlyList<SheetPoint>>? Contours=null)
{
    public SheetPoint[] Corners=>new[]{Minimum,new SheetPoint(Minimum.X,Maximum.Y),Maximum,new SheetPoint(Maximum.X,Minimum.Y)};
}
public sealed record IsoBlockCatalog(IReadOnlyList<IsoBlockDefinition> Blocks,double SymbolScale=1,IsoStyleSettings? Style=null)
{
    // Legacy cached Style data never overrides the explicit user scale.
    public double ScaleFor(Part part)=>double.IsFinite(SymbolScale)&&SymbolScale>0?SymbolScale:throw new InvalidDataException("Масштаб элементов должен быть положительным конечным числом.");
    public IsoBlockDefinition? Find(string name)=>Blocks.FirstOrDefault(b=>b.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
}
