namespace IsoGenerator.Core;

// A connected U-shaped fragment can still omit the middle of a straight run.
// Test model coordinates rather than paper positions: each sheet has its own layout.
public sealed class SheetRunContinuity
{
    readonly List<string[]> runs=new();
    public SheetRunContinuity(PipelineSnapshot source):this(source,PipelineTopology.Build(source)){}
    internal SheetRunContinuity(PipelineSnapshot source,PipelineTopology graph)
    {
        var pipes=source.Parts.Where(p=>p.Component==ComponentKind.Pipe)
            .Select(p=>(Part:p,Ends:p.Ports.Where(q=>q.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray()))
            .Where(p=>p.Ends.Length==2&&p.Ends[0].Position.DistanceTo(p.Ends[1].Position)>1).ToArray();
        var pipeNodes=graph.Nodes.Select((node,index)=>(node,index))
            .Where(entry=>entry.node.Component==ComponentKind.Pipe && entry.node.PortName.StartsWith("S",StringComparison.OrdinalIgnoreCase))
            .GroupBy(entry=>entry.node.Source,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group=>group.Key,group=>graph.Root(group.First().index),StringComparer.OrdinalIgnoreCase);
        var seenRuns=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var axis in pipes)
        {
            var a=axis.Ends[0].Position;var b=axis.Ends[1].Position;double length=a.DistanceTo(b);
            double ux=(b.X-a.X)/length,uy=(b.Y-a.Y)/length,uz=(b.Z-a.Z)/length;
            double Station(ModelPoint p)=>(p.X-a.X)*ux+(p.Y-a.Y)*uy+(p.Z-a.Z)*uz;
            bool OnLine(ModelPoint p){double t=Station(p);return p.DistanceTo(new(a.X+ux*t,a.Y+uy*t,a.Z+uz*t))<=1;}
            var straight=pipes.Where(pipe=>pipe.Ends.All(end=>OnLine(end.Position))).ToArray();
            if(straight.Length<3)continue;
            var connected=new DisjointSets();
            for(int i=0;i<graph.Nodes.Count;i++)connected.Add();
            foreach(var edge in graph.Edges)
                if(OnLine(graph.Nodes[edge.A].Point)&&OnLine(graph.Nodes[edge.B].Point))
                    connected.Join(graph.Root(edge.A),graph.Root(edge.B));
            var groups=straight
                .GroupBy(p=>connected.Find(pipeNodes[p.Part.Handle]));
            foreach(var group in groups)
            {
                var run=group.OrderBy(p=>(Station(p.Ends[0].Position)+Station(p.Ends[1].Position))/2)
                    .Select(p=>p.Part.Handle).ToArray();
                if(run.Length>=3 && seenRuns.Add(string.Join("|",run.OrderBy(h=>h,StringComparer.Ordinal))))runs.Add(run);
            }
        }
    }
    public bool HasInteriorGap(IEnumerable<string> handles)
    {
        var included=handles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var run in runs)
        {
            int first=Array.FindIndex(run,included.Contains),last=Array.FindLastIndex(run,included.Contains);
            for(int i=first+1;first>=0&&i<last;i++)if(!included.Contains(run[i]))return true;
        }
        return false;
    }
}
