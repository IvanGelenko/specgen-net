namespace IsoGenerator.Core;
internal sealed record ContinuationContext(IReadOnlyList<Stroke> Strokes,IReadOnlyList<SymbolGlyph> Symbols);
internal static class ContinuationContextPlanner
{
    public static ContinuationContext Build(int sheet,IReadOnlyList<PipelineSnapshot> sheets,IReadOnlyList<SheetScene> scenes,IReadOnlyList<SchematicLayout> layouts,PipelineSnapshot source,IReadOnlyDictionary<string,int> owner)
    {
        var strokes=new List<Stroke>();var symbols=new List<SymbolGlyph>();
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts=source.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
        foreach(var boundary in source.Connections)
        {
            if(!owner.TryGetValue(boundary.FirstHandle,out int a)||!owner.TryGetValue(boundary.SecondHandle,out int b))continue;if(a==b||a!=sheet&&b!=sheet)continue;
            string local=a==sheet?boundary.FirstHandle:boundary.SecondHandle,localPort=a==sheet?boundary.FirstPort:boundary.SecondPort;
            string remote=a==sheet?boundary.SecondHandle:boundary.FirstHandle,remotePort=a==sheet?boundary.SecondPort:boundary.FirstPort;int other=a==sheet?b:a;
            string key=local+":"+localPort+"/"+remote+":"+remotePort;if(!seen.Add(key))continue;
            var here=layouts[sheet].Project(parts[local].Ports.Single(p=>p.Name==localPort).Position,local);
            var there=layouts[other].Project(parts[remote].Ports.Single(p=>p.Name==remotePort).Position,remote);
            SheetPoint Move(SheetPoint p)=>new(p.X+here.X-there.X,p.Y+here.Y-there.Y);
            double Distance(SheetPoint p)=>Math.Sqrt(Math.Pow(p.X-there.X,2)+Math.Pow(p.Y-there.Y,2));
            var selected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue=new Queue<string>();queue.Enqueue(remote);bool assembly=false;
            while(queue.Count>0&&selected.Count<3)
            {
                string h=queue.Dequeue();if(!visited.Add(h)||!owner.TryGetValue(h,out var assigned)||assigned!=other)continue;
                var part=parts[h];string kind=part.Properties.GetValueOrDefault("PnPClassName","");
                if(part.Kind!="AcPpDb3dConnector")
                {
                    if(kind=="Pipe"&&selected.Count>0)continue;
                    if(kind=="Pipe"){selected.Add(h);break;}
                    if(selected.Count==0)assembly=kind is "Valve" or "Flange";
                    if(selected.Count>0&&!assembly)continue;
                    selected.Add(h);
                }
                foreach(var c in source.Connections)
                {
                    if(c.FirstHandle==h&&owner.TryGetValue(c.SecondHandle,out var secondOwner)&&secondOwner==other)queue.Enqueue(c.SecondHandle);
                    else if(c.SecondHandle==h&&owner.TryGetValue(c.FirstHandle,out var firstOwner)&&firstOwner==other)queue.Enqueue(c.FirstHandle);
                }
            }
            foreach(var stroke in scenes[other].Strokes.Where(s=>selected.Contains(s.SourceId)&&s.Role is "pipe" or "component" or "flange-neck" or "elbow" or "branch" or "valve" or "flange"))
            {
                var points=stroke.Points.ToArray();
                if(stroke.Role=="pipe")
                {
                    // Show only the first 12 paper millimeters of the pipe on the adjacent sheet.
                    var start=points[0];var end=points[^1];double dx=end.X-start.X,dy=end.Y-start.Y,length2=dx*dx+dy*dy;
                    if(length2<1e-10)continue;
                    double t=Math.Clamp(((there.X-start.X)*dx+(there.Y-start.Y)*dy)/length2,0,1);
                    var at=new SheetPoint(start.X+dx*t,start.Y+dy*t);
                    // A dynamic branch port may be in the middle of the remote pipe.
                    // Show connected context from the actual boundary station, not from the nearest end.
                    foreach(var endpoint in new[]{start,end})
                    {
                        double ex=endpoint.X-at.X,ey=endpoint.Y-at.Y,length=Math.Sqrt(ex*ex+ey*ey);if(length<.001)continue;
                        double fraction=Math.Min(1,12/length);var segment=new[]{at,new SheetPoint(at.X+ex*fraction,at.Y+ey*fraction)};
                        strokes.Add(new(stroke.SourceId,"context-pipe",segment.Select(Move).ToArray()));
                    }
                    continue;
                }
                strokes.Add(new(stroke.SourceId,"context-"+stroke.Role,points.Select(Move).ToArray()));
            }
            foreach(var glyph in scenes[other].Symbols??Array.Empty<SymbolGlyph>())
                if(selected.Contains(glyph.SourceId)&&Distance(glyph.Origin)<45)symbols.Add(glyph with{Origin=Move(glyph.Origin),Context=true});
        }
        return new(strokes,symbols.DistinctBy(g=>(g.SourceId,g.Variant)).ToArray());
    }
}
