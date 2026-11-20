namespace IsoGenerator.Core;
internal static class InsulationJunctions
{
    internal static void Trim(PipelineSnapshot snapshot,List<Stroke> strokes,IReadOnlyList<(string Source,SheetPoint A,SheetPoint B)> axes,double offset)
    {
        var parts=snapshot.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
        var neighbours=parts.Keys.ToDictionary(h=>h,_=>new HashSet<string>(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
        foreach(var c in snapshot.Connections)if(neighbours.ContainsKey(c.FirstHandle)&&neighbours.ContainsKey(c.SecondHandle)){neighbours[c.FirstHandle].Add(c.SecondHandle);neighbours[c.SecondHandle].Add(c.FirstHandle);}
        var connected=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach(string source in axes.Select(a=>a.Source).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var allowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if(parts[source].Component==ComponentKind.Tee)allowed.Add(source);
            var visited=new HashSet<(string,bool)>();var queue=new Queue<(string Handle,bool Junction)>();queue.Enqueue((source,false));
            bool Junction(string handle,string port)=>parts.TryGetValue(handle,out var p)&&(p.Component==ComponentKind.Tee||p.Ports.Any(end=>end.Name==port&&end.EndCondition.Trim().Equals("TAP",StringComparison.OrdinalIgnoreCase)));
            while(queue.Count>0)
            {
                var current=queue.Dequeue();if(!visited.Add((current.Handle,current.Junction)))continue;
                foreach(var link in snapshot.Connections.Where(c=>c.FirstHandle==current.Handle||c.SecondHandle==current.Handle))
                {
                    string next=link.FirstHandle==current.Handle?link.SecondHandle:link.FirstHandle;if(!parts.ContainsKey(next)||next==source)continue;
                    bool junction=current.Junction||Junction(link.FirstHandle,link.FirstPort)||Junction(link.SecondHandle,link.SecondPort);
                    if(parts[next].Kind=="AcPpDb3dConnector")queue.Enqueue((next,junction));else if(junction)allowed.Add(next);
                }
            }
            connected[source]=allowed;
        }
        var output=new List<Stroke>();
        foreach(var stroke in strokes)
        {
            if(stroke.Role!="insulation"){output.Add(stroke);continue;}
            var pieces=new List<List<SheetPoint>>();
            void AddSegment(SheetPoint a,SheetPoint b){if(pieces.Count>0&&Math.Abs(pieces[^1][^1].X-a.X)<1e-8&&Math.Abs(pieces[^1][^1].Y-a.Y)<1e-8)pieces[^1].Add(b);else pieces.Add(new(){a,b});}
            var covers=axes.Where(a=>connected[stroke.SourceId].Contains(a.Source)&&(a.Source!=stroke.SourceId||parts[stroke.SourceId].Component==ComponentKind.Tee)).ToArray();
            foreach(var (a,b) in stroke.Points.Zip(stroke.Points.Skip(1)))
            {
                var cuts=new List<(double Low,double High)>();
                foreach(var cover in covers)
                {
                    double dx=cover.B.X-cover.A.X,dy=cover.B.Y-cover.A.Y,l=Math.Sqrt(dx*dx+dy*dy);if(l<1e-8)continue;
                    double ux=dx/l,uy=dy/l;
                    double x=(a.X-cover.A.X)*ux+(a.Y-cover.A.Y)*uy,y=-(a.X-cover.A.X)*uy+(a.Y-cover.A.Y)*ux;
                    double vx=(b.X-a.X)*ux+(b.Y-a.Y)*uy,vy=-(b.X-a.X)*uy+(b.Y-a.Y)*ux;
                    double low=0,high=1;
                    bool Clip(double p,double q){if(Math.Abs(p)<1e-10)return q>=0;double t=q/p;if(p<0)low=Math.Max(low,t);else high=Math.Min(high,t);return low<=high;}
                    if(Clip(-vx,x)&&Clip(vx,l-x)&&Clip(-vy,y+offset-.001)&&Clip(vy,offset-.001-y)&&high-low>1e-8)cuts.Add((low,high));
                }
                double cursor=0;SheetPoint At(double t)=>new(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);
                foreach(var cut in cuts.OrderBy(c=>c.Low)){if(cut.Low>cursor+1e-8)AddSegment(At(cursor),At(cut.Low));cursor=Math.Max(cursor,cut.High);}
                if(cursor<1-1e-8)AddSegment(At(cursor),b);
            }
            output.AddRange(pieces.Select(points=>new Stroke(stroke.SourceId,"insulation",points)));
        }
        strokes.Clear();strokes.AddRange(output);
    }
}
