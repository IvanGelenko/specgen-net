using System.Globalization;
namespace IsoGenerator.Core;
internal static class InsulationPlanner
{
    const double Offset=3;
    static double? Thickness(Part part)=>double.TryParse(part.Properties.GetValueOrDefault("InsulationThickness","").Trim().Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out double n)&&double.IsFinite(n)?n:null;
    public static void Add(PipelineSnapshot snapshot,Func<ModelPoint,SheetPoint> project,IReadOnlyList<SymbolGlyph> symbols,List<Stroke> strokes,Func<ModelPoint,string,SheetPoint>? projectSource=null)
    {
        double inherited=snapshot.Parts.Select(Thickness).Where(n=>n>0).Select(n=>n!.Value).DefaultIfEmpty(0).Max();
        foreach(var part in snapshot.Parts)
        {
            SheetPoint At(ModelPoint point)=>projectSource is null?project(point):projectSource(point,part.Handle);
            string kind=part.Properties.GetValueOrDefault("PnPClassName","");
            if(kind is not ("Pipe" or "Elbow" or "Tee")||(Thickness(part)??inherited)<=0)continue;
            var ends=part.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();if(ends.Length<2)continue;
            var bendGlyph=kind=="Elbow"?symbols.FirstOrDefault(g=>g.SourceId==part.Handle&&g.Variant.Length==0):null;
            var paths=new List<SheetPoint[]>();
            if(kind=="Pipe")paths.Add(new[]{At(ends[0].Position),At(ends[1].Position)});
            else
            {
                var glyph=symbols.FirstOrDefault(g=>g.SourceId==part.Handle&&g.Variant.Length==0);
                if(glyph is not null)
                {
                    if(kind=="Elbow")paths.Add(new[]{new SheetPoint(glyph.Origin.X-glyph.XAxis.X*.5,glyph.Origin.Y-glyph.XAxis.Y*.5),glyph.Origin,new SheetPoint(glyph.Origin.X+glyph.YAxis.X*.5,glyph.Origin.Y+glyph.YAxis.Y*.5)});
                    else
                    {
                        paths.Add(new[]{new SheetPoint(glyph.Origin.X-glyph.XAxis.X*.5,glyph.Origin.Y-glyph.XAxis.Y*.5),new SheetPoint(glyph.Origin.X+glyph.XAxis.X*.5,glyph.Origin.Y+glyph.XAxis.Y*.5)});
                        paths.Add(new[]{glyph.Origin,new SheetPoint(glyph.Origin.X+glyph.YAxis.X*.5,glyph.Origin.Y+glyph.YAxis.Y*.5)});
                    }
                }
                else paths.AddRange(strokes.Where(s=>s.SourceId==part.Handle&&s.Role is "elbow" or "branch" or "component").Select(s=>s.Points.ToArray()).ToArray());
            }
            foreach(var path in paths)foreach(double side in new[]{-1.0,1.0})
            {
                var result=new List<SheetPoint>();
                for(int i=0;i<path.Length;i++)
                {
                    var before=path[Math.Max(0,i-1)];var at=path[i];var after=path[Math.Min(path.Length-1,i+1)];
                    var a=i==0?at:before;var b=i==0?after:at;double dx=b.X-a.X,dy=b.Y-a.Y,l=Math.Sqrt(dx*dx+dy*dy);if(l<1e-6)continue;
                    var point=new SheetPoint(at.X-dy/l*Offset*side,at.Y+dx/l*Offset*side);
                    if(i>0&&i<path.Length-1)
                    {
                        double ex=after.X-at.X,ey=after.Y-at.Y,el=Math.Sqrt(ex*ex+ey*ey),den=dx*ey-dy*ex;
                        if(el>1e-6&&Math.Abs(den)>1e-8)
                        {
                            var q=new SheetPoint(at.X-ey/el*Offset*side,at.Y+ex/el*Offset*side);
                            double t=((q.X-point.X)*ey-(q.Y-point.Y)*ex)/den;point=new(point.X+dx*t,point.Y+dy*t);
                        }
                    }
                    result.Add(point);
                }
                if(result.Count==3)
                {
                    var a=result[0];var c=result[1];var b=result[2];double l1=Math.Sqrt(Math.Pow(a.X-c.X,2)+Math.Pow(a.Y-c.Y,2)),l2=Math.Sqrt(Math.Pow(b.X-c.X,2)+Math.Pow(b.Y-c.Y,2));
                    var incoming=new SheetPoint(path[1].X-path[0].X,path[1].Y-path[0].Y);var outgoing=new SheetPoint(path[2].X-path[1].X,path[2].Y-path[1].Y);
                    double il=Math.Sqrt(incoming.X*incoming.X+incoming.Y*incoming.Y),ol=Math.Sqrt(outgoing.X*outgoing.X+outgoing.Y*outgoing.Y);
                    double dot=Math.Clamp((incoming.X*outgoing.X+incoming.Y*outgoing.Y)/(il*ol),-1,1),turn=Math.Acos(dot);
                    double sign=Math.Sign(incoming.X*outgoing.Y-incoming.Y*outgoing.X);
                    double tangent=Math.Min(il,ol)*.25;
                    // Obtain the bend's own tangent distance from its library curve, when available.
                    var curve=bendGlyph?.LocalContours?.Where(points=>points.Count>2).OrderByDescending(points=>points.Count).FirstOrDefault();
                    if(curve is not null&&bendGlyph is not null)
                    {
                        SheetPoint AtCurve(SheetPoint p)=>new(bendGlyph.Origin.X+bendGlyph.XAxis.X*p.X+bendGlyph.YAxis.X*p.Y,bendGlyph.Origin.Y+bendGlyph.XAxis.Y*p.X+bendGlyph.YAxis.Y*p.Y);
                        double Distance(SheetPoint p)=>Math.Sqrt(Math.Pow(p.X-path[1].X,2)+Math.Pow(p.Y-path[1].Y,2));
                        tangent=(Distance(AtCurve(curve[0]))+Distance(AtCurve(curve[^1])))/2;
                    }
                    // Offset bends need different inner/outer radii. A small inside radius avoids a cusp
                    // when the paper offset exceeds the radius of a tightly drawn elbow.
                    double available=Math.Min(l1,l2)*.8;
                    double minimumTrim=Math.Min(available,1.25*Math.Tan(turn/2));
                    double trim=Math.Clamp(tangent-side*sign*Offset*Math.Tan(turn/2),minimumTrim,available);
                    var u=new SheetPoint(c.X+(a.X-c.X)*trim/l1,c.Y+(a.Y-c.Y)*trim/l1);var v=new SheetPoint(c.X+(b.X-c.X)*trim/l2,c.Y+(b.Y-c.Y)*trim/l2);
                    double weight=Math.Cos(turn/2);
                    result=new(){a,u};
                    for(int i=1;i<32;i++)
                    {
                        double t=i/32.0,s=1-t,den=s*s+2*weight*s*t+t*t;
                        result.Add(new((s*s*u.X+2*weight*s*t*c.X+t*t*v.X)/den,(s*s*u.Y+2*weight*s*t*c.Y+t*t*v.Y)/den));
                    }
                    result.Add(v);result.Add(b);
                }
                if(result.Count>1)strokes.Add(new(part.Handle,"insulation",result));
            }
        }
    }
}
