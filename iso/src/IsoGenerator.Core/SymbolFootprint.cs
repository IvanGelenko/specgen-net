namespace IsoGenerator.Core;
internal static class SymbolFootprint
{
    public static SheetPoint[] Corners(SymbolGlyph g)
    {
        if(g.LocalBounds is {} bounds)return bounds.Select(p=>new SheetPoint(g.Origin.X+g.XAxis.X*p.X+g.YAxis.X*p.Y,g.Origin.Y+g.XAxis.Y*p.X+g.YAxis.Y*p.Y)).ToArray();
        (double x0,double x1,double y0,double y1)=g.Variant.StartsWith("end-",StringComparison.Ordinal)?(-.027,0,-.25,.25):
            g.Variant=="operator"?(-.25,.25,0,.625):g.Block=="ФланецWN"?(-.15625,.15625,-.25,.25):
            g.Block=="Колено"?(-.5,0,0,.5):g.Block=="Тройник"?(-.5,.5,0,.5):(-.5,.5,-.25,.25);
        return (from x in new[]{x0,x1} from y in new[]{y0,y1} select new SheetPoint(g.Origin.X+g.XAxis.X*x+g.YAxis.X*y,g.Origin.Y+g.XAxis.Y*x+g.YAxis.Y*y)).ToArray();
    }
}
