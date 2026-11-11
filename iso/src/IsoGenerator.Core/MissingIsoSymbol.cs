namespace IsoGenerator.Core;
// A built-in paper symbol; it never depends on the project's DWG block library.
public static class MissingIsoSymbol
{
    public static SymbolGlyph Create(Part part,IReadOnlyList<SheetPoint> ports,int turns)
    {
        var center=new SheetPoint(ports.Average(p=>p.X),ports.Average(p=>p.Y));
        if(ports.Count==1&&BomCategories.Classify(part)!=BomCategory.Supports)
        {
            var direction=part.Ports.First(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).Direction;
            var projected=IsometricProjection.Project(new(-direction.X,-direction.Y,-direction.Z),quarterTurns:turns);
            double reach=Math.Max(Math.Abs(projected.X),Math.Abs(projected.Y));
            center=reach>1e-8?new(center.X+projected.X*5/reach,center.Y+projected.Y*5/reach):new(center.X,center.Y+5);
        }
        IReadOnlyList<IReadOnlyList<SheetPoint>> contours=new IReadOnlyList<SheetPoint>[]
        {
            new SheetPoint[]{new(-5,-5),new(5,-5),new(5,5),new(-5,5),new(-5,-5)},
            new SheetPoint[]{new(-5,-5),new(5,5)},new SheetPoint[]{new(-5,5),new(5,-5)}
        };
        return new(part.Handle,part.Properties.GetValueOrDefault("PnPGuid",""),part.Properties.GetValueOrDefault("ContentIsoSymbolDefinition",""),"SpecGenMissingIsoSymbol",center,new(1,0),new(0,1),"missing",LocalBounds:new SheetPoint[]{new(-5,-5),new(5,-5),new(5,5),new(-5,5)},LocalContours:contours);
    }
}
