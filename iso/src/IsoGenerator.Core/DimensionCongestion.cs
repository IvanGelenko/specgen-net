namespace IsoGenerator.Core;
internal static class DimensionCongestion
{
    sealed record Row(PipeDimension Dimension,SheetPoint Start,SheetPoint End,SheetPoint Axis,SheetPoint Normal);
    static double Dot(SheetPoint a,SheetPoint b)=>a.X*b.X+a.Y*b.Y;
    public static string? PipeToExtend(PipelineSnapshot snapshot,SheetScene scene)
    {
        var rows=scene.Dimensions.Select(d=>
        {
            double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,l=Math.Sqrt(dx*dx+dy*dy);var u=new SheetPoint(dx/l,dy/l);var n=new SheetPoint(-u.Y,u.X);var ex=d.ExtensionDirection!.Value;var p=d.DimensionLinePoint!.Value;
            SheetPoint Move(SheetPoint a){double t=Dot(new(p.X-a.X,p.Y-a.Y),n)/Dot(ex,n);return new(a.X+ex.X*t,a.Y+ex.Y*t);}
            return new Row(d,Move(d.Start),Move(d.End),u,n);
        }).ToArray();
        var parts=snapshot.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
        var adjacency=parts.Keys.ToDictionary(h=>h,_=>new HashSet<string>(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
        foreach(var c in snapshot.Connections)if(adjacency.ContainsKey(c.FirstHandle)&&adjacency.ContainsKey(c.SecondHandle)){adjacency[c.FirstHandle].Add(c.SecondHandle);adjacency[c.SecondHandle].Add(c.FirstHandle);}
        var conflicts=new List<(Row A,Row B,double Gap)>();
        for(int i=0;i<rows.Length;i++)for(int j=i+1;j<rows.Length;j++)
        {
            var a=rows[i];var b=rows[j];if(a.Dimension.ChainId==b.Dimension.ChainId||Math.Abs(Dot(a.Axis,b.Axis))<.999)continue;
            double gap=Math.Abs(Dot(new(b.Start.X-a.Start.X,b.Start.Y-a.Start.Y),a.Normal));
            double overlap=Math.Min(Math.Max(Dot(a.Start,a.Axis),Dot(a.End,a.Axis)),Math.Max(Dot(b.Start,a.Axis),Dot(b.End,a.Axis)))-Math.Max(Math.Min(Dot(a.Start,a.Axis),Dot(a.End,a.Axis)),Math.Min(Dot(b.Start,a.Axis),Dot(b.End,a.Axis)));
            if(gap<7-1e-6&&overlap>3)conflicts.Add((a,b,gap));
        }
        foreach(var conflict in conflicts.OrderBy(c=>c.Gap))
        {
            var from=(conflict.A.Dimension.SourceIds??new[]{conflict.A.Dimension.SourceId}).Where(parts.ContainsKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var to=(conflict.B.Dimension.SourceIds??new[]{conflict.B.Dimension.SourceId}).Where(parts.ContainsKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(from.Overlaps(to))continue;
            var queue=new Queue<string>(from);var previous=from.ToDictionary(h=>h,_=>(string?)null,StringComparer.OrdinalIgnoreCase);string? reached=null;
            while(queue.Count>0){var h=queue.Dequeue();if(to.Contains(h)){reached=h;break;}foreach(var next in adjacency[h])if(previous.TryAdd(next,h))queue.Enqueue(next);}
            if(reached is null)continue;
            var path=new List<string>();for(string? h=reached;h is not null;h=previous[h])path.Add(h);
            var candidates=path.Where(h=>parts[h].Properties.GetValueOrDefault("PnPClassName")=="Pipe").Select(h=>
            {
                var stroke=scene.Strokes.FirstOrDefault(s=>s.SourceId==h&&s.Role=="pipe");
                if(stroke is null)return (Handle:h,Score:0.0);
                var a=stroke.Points[0];var b=stroke.Points[^1];double dx=b.X-a.X,dy=b.Y-a.Y,l=Math.Sqrt(dx*dx+dy*dy);
                return (Handle:h,Score:Math.Abs(dx*conflict.A.Normal.X+dy*conflict.A.Normal.Y)/Math.Max(.001,l));
            }).Where(c=>c.Score>.25).OrderByDescending(c=>c.Score).ToArray();
            if(candidates.Length>0)return candidates[0].Handle;
        }
        return null;
    }
}
