using System.Globalization;
namespace IsoGenerator.Core;
internal static class InsulationPlanner
{
    static double Offset=>IsoAdvancedSettings.Current.InsulationOffset;
    static double Dot(ModelPoint a,ModelPoint b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    static double? Thickness(Part part)=>double.TryParse(part.Properties.GetValueOrDefault("InsulationThickness","").Trim().Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out double n)&&double.IsFinite(n)?n:null;
    static double? Radius(Part part)
    {
        if(double.TryParse(part.Properties.GetValueOrDefault("CurveRadius","").Trim().Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out double r)&&r>0)return r;
        var desc=part.Properties.GetValueOrDefault("PartSizeLongDesc","");
        var m=System.Text.RegularExpressions.Regex.Match(desc,@"(?<!\p{L})(?:Р|R|радиус(?:\s+гиба)?)\s*=\s*(\d+(?:[.,]\d+)?)",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if(m.Success&&double.TryParse(m.Groups[1].Value.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out double rDesc)&&rDesc>0)return rDesc;
        return null;
    }
    public static void Add(PipelineSnapshot snapshot,Func<ModelPoint,SheetPoint> project,IReadOnlyList<SymbolGlyph> symbols,List<Stroke> strokes,Func<ModelPoint,string,SheetPoint>? projectSource=null)
    {
        double inherited=snapshot.Parts.Select(Thickness).Where(n=>n>0).Select(n=>n!.Value).DefaultIfEmpty(0).Max();
        var axes=new List<(string Source,SheetPoint A,SheetPoint B)>();
        foreach(var part in snapshot.Parts)
        {
            SheetPoint At(ModelPoint point)=>projectSource is null?project(point):projectSource(point,part.Handle);
            string kind=part.CatalogClass;
            if(kind is not ("Pipe" or "Elbow" or "PipeBend" or "Tee")||(Thickness(part)??inherited)<=0)continue;
            var ends=part.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();if(ends.Length<2)continue;
            var bendGlyph=kind is "Elbow" or "PipeBend"?symbols.FirstOrDefault(g=>g.SourceId==part.Handle&&g.Variant.Length==0):null;
            var paths=new List<SheetPoint[]>();
            ModelPoint? elbowCorner=null;
            if(kind=="Pipe")paths.Add(new[]{At(ends[0].Position),At(ends[1].Position)});
            else if(kind is "Elbow" or "PipeBend")
            {
                // Library ports are not necessarily at +/-0.5. Use the same model
                // port axes as the schematic, so offsets join the neighbouring pipe.
                var a=ends[0];var b=ends[1];
                elbowCorner=PipelineTopology.ElbowCorner(part,a,b);
                if(elbowCorner is null)
                {
                    List<SheetPoint>? chained=null;
                    if(bendGlyph?.LocalContours is {} contours&&contours.Count>0)
                    {
                        SheetPoint AtCurve(SheetPoint p)=>new(bendGlyph.Origin.X+bendGlyph.XAxis.X*p.X+bendGlyph.YAxis.X*p.Y,bendGlyph.Origin.Y+bendGlyph.XAxis.Y*p.X+bendGlyph.YAxis.Y*p.Y);
                        var pa=At(a.Position);var pb=At(b.Position);
                        var remaining=contours.Select(c=>c.Select(AtCurve).ToList()).ToList();
                        var current=pa;
                        var pathPoints=new List<SheetPoint>{current};
                        while(remaining.Count>0)
                        {
                            int bestIdx=-1;bool reverse=false;double bestDist=double.MaxValue;
                            for(int k=0;k<remaining.Count;k++)
                            {
                                double d0=Math.Sqrt(Math.Pow(remaining[k][0].X-current.X,2)+Math.Pow(remaining[k][0].Y-current.Y,2));
                                double d1=Math.Sqrt(Math.Pow(remaining[k][^1].X-current.X,2)+Math.Pow(remaining[k][^1].Y-current.Y,2));
                                if(d0<bestDist){bestDist=d0;bestIdx=k;reverse=false;}
                                if(d1<bestDist){bestDist=d1;bestIdx=k;reverse=true;}
                            }
                            if(bestDist>5.0)break;
                            var seg=remaining[bestIdx];
                            remaining.RemoveAt(bestIdx);
                            if(reverse)seg.Reverse();
                            pathPoints.AddRange(seg.Skip(1));
                            current=pathPoints[^1];
                        }
                        if(pathPoints.Count>3&&Math.Sqrt(Math.Pow(pathPoints[^1].X-pb.X,2)+Math.Pow(pathPoints[^1].Y-pb.Y,2))<5.0)
                        {
                            pathPoints[0]=pa;
                            pathPoints[^1]=pb;
                            chained=pathPoints;
                        }
                    }
                    if(chained is not null)
                    {
                        paths.Add(chained.ToArray());
                    }
                    else
                    {
                        double r=a.Position.DistanceTo(b.Position)/2;
                        double l=Math.Sqrt(Dot(a.Direction,a.Direction));
                        var dir=l<1e-9?a.Direction:new ModelPoint(a.Direction.X/l,a.Direction.Y/l,a.Direction.Z/l);
                        var mid=new ModelPoint((a.Position.X+b.Position.X)/2,(a.Position.Y+b.Position.Y)/2,(a.Position.Z+b.Position.Z)/2);
                        var uTrans=r<1e-9?new ModelPoint(0,1,0):new ModelPoint((b.Position.X-a.Position.X)/(2*r),(b.Position.Y-a.Position.Y)/(2*r),(b.Position.Z-a.Position.Z)/(2*r));
                        var arc=new List<SheetPoint>();
                        for(int step=0;step<=16;step++)
                        {
                            double phi=Math.PI*step/16.0;
                            var pt=new ModelPoint(
                                mid.X-uTrans.X*r*Math.Cos(phi)-dir.X*r*Math.Sin(phi),
                                mid.Y-uTrans.Y*r*Math.Cos(phi)-dir.Y*r*Math.Sin(phi),
                                mid.Z-uTrans.Z*r*Math.Cos(phi)-dir.Z*r*Math.Sin(phi));
                            arc.Add(At(pt));
                        }
                        paths.Add(arc.ToArray());
                    }
                }
                else
                {
                    paths.Add(new[]{At(a.Position),At(elbowCorner.Value),At(b.Position)});
                }
            }
            else
            {
                var pair=(from i in Enumerable.Range(0,ends.Length) from j in Enumerable.Range(i+1,ends.Length-i-1)
                          where Dot(ends[i].Direction,ends[j].Direction)<-.999 select(i,j)).FirstOrDefault((-1,-1));
                if(pair.Item1<0)throw new InvalidDataException($"{part.Handle}: не определена ось тройника для изоляции");
                var a=ends[pair.Item1];var b=ends[pair.Item2];
                paths.Add(new[]{At(a.Position),At(b.Position)});
                var axis=new ModelPoint(b.Position.X-a.Position.X,b.Position.Y-a.Position.Y,b.Position.Z-a.Position.Z);
                foreach(var branch in ends.Where((_,i)=>i!=pair.Item1&&i!=pair.Item2))
                {
                    var delta=new ModelPoint(branch.Position.X-a.Position.X,branch.Position.Y-a.Position.Y,branch.Position.Z-a.Position.Z);
                    double t=Dot(delta,axis)/Dot(axis,axis);
                    var center=new ModelPoint(a.Position.X+axis.X*t,a.Position.Y+axis.Y*t,a.Position.Z+axis.Z*t);
                    paths.Add(new[]{At(center),At(branch.Position)});
                }
            }
            foreach(var path in paths)axes.AddRange(path.Zip(path.Skip(1),(a,b)=>(part.Handle,a,b)));
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
                    else if(Radius(part) is {} rad&&rad>0&&elbowCorner is not null)
                    {
                        double modelTangent=rad*Math.Tan(turn/2);
                        double modelLeg=(ends[0].Position.DistanceTo(elbowCorner.Value)+ends[1].Position.DistanceTo(elbowCorner.Value))/2;
                        if(modelLeg>1e-3)
                        {
                            double sheetLeg=(il+ol)/2;
                            tangent=sheetLeg*Math.Clamp(modelTangent/modelLeg,0.05,0.9);
                        }
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
        InsulationJunctions.Trim(snapshot,strokes,axes,Offset);
    }
}
