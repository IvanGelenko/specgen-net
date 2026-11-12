namespace IsoGenerator.Core;

// A connected U-shaped fragment can still omit the middle of a straight run.
// Test model coordinates rather than paper positions: each sheet has its own layout.
public sealed class SheetRunContinuity
{
    readonly List<string[]> runs=new();
    public SheetRunContinuity(PipelineSnapshot source)
    {
        var pipes=source.Parts.Where(p=>p.Properties.GetValueOrDefault("PnPClassName")=="Pipe")
            .Select(p=>(Part:p,Ends:p.Ports.Where(q=>q.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray()))
            .Where(p=>p.Ends.Length==2&&p.Ends[0].Position.DistanceTo(p.Ends[1].Position)>1).ToArray();
        var keys=new HashSet<string>(StringComparer.Ordinal);
        foreach(var axis in pipes)
        {
            var a=axis.Ends[0].Position;var b=axis.Ends[1].Position;double length=a.DistanceTo(b);
            double ux=(b.X-a.X)/length,uy=(b.Y-a.Y)/length,uz=(b.Z-a.Z)/length;
            double Station(ModelPoint p)=>(p.X-a.X)*ux+(p.Y-a.Y)*uy+(p.Z-a.Z)*uz;
            bool OnLine(ModelPoint p){double t=Station(p);return p.DistanceTo(new(a.X+ux*t,a.Y+uy*t,a.Z+uz*t))<=1;}
            var run=pipes.Where(p=>p.Ends.All(e=>OnLine(e.Position)))
                .OrderBy(p=>(Station(p.Ends[0].Position)+Station(p.Ends[1].Position))/2).Select(p=>p.Part.Handle).ToArray();
            if(run.Length<3)continue;
            string key=string.Join("|",run.OrderBy(h=>h,StringComparer.Ordinal));if(keys.Add(key))runs.Add(run);
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
