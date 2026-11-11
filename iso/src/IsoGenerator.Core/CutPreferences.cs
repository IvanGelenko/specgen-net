using System.Globalization;
using System.Text.RegularExpressions;
namespace IsoGenerator.Core;
public static class PartDiameter
{
    public static double? Read(Part part)
    {
        foreach(string key in new[]{"NominalDiameter","NominalSize","Size","BoltSize"})
        {
            string text=part.Properties.GetValueOrDefault(key,"").Trim().Replace(',','.');
            var match=Regex.Match(text,@"\d+(?:\.\d+)?");
            if(match.Success&&double.TryParse(match.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out double value)&&value>0)return value;
        }
        return null;
    }
    public static double[] ReducerSizes(Part part)=>Regex.Matches(part.Properties.GetValueOrDefault("Size","").Replace(',','.'),@"\d+(?:\.\d+)?").Select(m=>double.Parse(m.Value,CultureInfo.InvariantCulture)).Where(n=>n>0).ToArray();
}
public sealed record CutPreferences(IReadOnlySet<string> Protected,IReadOnlySet<string> Preferred)
{
    public static string Key(Connection c)=>string.Join("|",new[]{c.FirstHandle+":"+c.FirstPort,c.SecondHandle+":"+c.SecondPort}.OrderBy(s=>s,StringComparer.OrdinalIgnoreCase));
    public static CutPreferences Build(PipelineSnapshot source,IsoStyleSettings? style)
    {
        var parts=source.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
        var adjacent=parts.Keys.ToDictionary(h=>h,_=>new List<(string Other,Connection Link)>(),StringComparer.OrdinalIgnoreCase);
        foreach(var c in source.Connections)if(adjacent.ContainsKey(c.FirstHandle)&&adjacent.ContainsKey(c.SecondHandle)){adjacent[c.FirstHandle].Add((c.SecondHandle,c));adjacent[c.SecondHandle].Add((c.FirstHandle,c));}
        bool Joint(Part p)=>p.Kind=="AcPpDb3dConnector";
        bool IsSupport(Part p)=>p.Properties.GetValueOrDefault("PnPClassName")=="Support";
        double Length(Part p){var ports=p.Ports.Where(q=>q.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();return ports.Length==2?ports[0].Position.DistanceTo(ports[1].Position):0;}
        var protectedLinks=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var preferred=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var host in source.Parts)
        {
            string kind=host.Properties.GetValueOrDefault("PnPClassName","");var branchPorts=new List<string>();
            if(kind=="Pipe")branchPorts.AddRange(host.Ports.Where(p=>!p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).Select(p=>p.Name));
            if(kind=="Tee")
            {
                var ends=host.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();
                if(ends.Length==3)
                {
                    var pair=(from i in Enumerable.Range(0,3) from j in Enumerable.Range(i+1,2-i) let a=ends[i].Direction let b=ends[j].Direction where a.X*b.X+a.Y*b.Y+a.Z*b.Z<-.99 select(i,j)).FirstOrDefault((-1,-1));
                    if(pair.Item1>=0)branchPorts.Add(ends[Enumerable.Range(0,3).Single(i=>i!=pair.Item1&&i!=pair.Item2)].Name);
                }
            }
            foreach(string portName in branchPorts)
            foreach(var root in adjacent[host.Handle].Where(e=>e.Link.FirstHandle==host.Handle?e.Link.FirstPort==portName:e.Link.SecondPort==portName))
            {
                var branch=new HashSet<string>(StringComparer.OrdinalIgnoreCase){root.Other};var queue=new Queue<string>();queue.Enqueue(root.Other);bool reconnects=false;
                while(queue.Count>0)
                {
                    string h=queue.Dequeue();foreach(var next in adjacent[h])
                    {
                        if(next.Other==host.Handle){string hostPort=next.Link.FirstHandle==host.Handle?next.Link.FirstPort:next.Link.SecondPort;if(hostPort!=portName)reconnects=true;continue;}
                        if(branch.Add(next.Other))queue.Enqueue(next.Other);
                    }
                }
                if(reconnects)continue;
                var physical=branch.Select(h=>parts[h]).Where(p=>!Joint(p)&&!IsSupport(p)).ToArray();
                bool service=physical.Any(p=>new[]{"DR","AV","CV","RV"}.Contains(p.Properties.GetValueOrDefault("Service","").Trim().ToUpperInvariant()));
                double hostDiameter=PartDiameter.Read(host)??0;
                var branchPipes=physical.Where(p=>p.Properties.GetValueOrDefault("PnPClassName")=="Pipe").ToArray();
                // Identify a short small-bore end branch from its topology and model sizes,
                // independent of any project IsoConfig theme or named line.
                bool pattern=branchPipes.Length==1&&physical.Count(p=>p.Properties.GetValueOrDefault("PnPClassName")=="Valve")==1&&hostDiameter>0&&physical.Any(p=>PartDiameter.Read(p) is {} size&&size*2<=hostDiameter)&&physical.All(p=>PartDiameter.Read(p) is not {} size||size*2<=hostDiameter);
                if(!service&&!pattern)continue;
                foreach(var c in source.Connections)if((branch.Contains(c.FirstHandle)&&branch.Contains(c.SecondHandle))||Key(c)==Key(root.Link))protectedLinks.Add(Key(c));
            }
        }
        foreach(var reducer in source.Parts.Where(p=>p.Properties.GetValueOrDefault("PnPClassName")=="Reducer"))
        {
            var sizes=PartDiameter.ReducerSizes(reducer);if(sizes.Length<2)continue;double small=sizes.Min(),large=sizes.Max();if(large/small<2)continue;
            foreach(var start in adjacent[reducer.Handle])
            {
                var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase){reducer.Handle};var pending=new Queue<(string Handle,Connection Link)>();pending.Enqueue((start.Other,start.Link));
                while(pending.Count>0)
                {
                    var current=pending.Dequeue();if(!visited.Add(current.Handle))continue;var part=parts[current.Handle];
                    if(Joint(part)){foreach(var next in adjacent[current.Handle])if(!visited.Contains(next.Other))pending.Enqueue((next.Other,next.Link));continue;}
                    if(part.Properties.GetValueOrDefault("PnPClassName")!="Pipe"||PartDiameter.Read(part) is not {} diameter||Math.Abs(diameter-small)>.001)break;
                    if(protectedLinks.Contains(Key(current.Link)))break;
                    var run=new HashSet<string>(StringComparer.OrdinalIgnoreCase){reducer.Handle};var q=new Queue<string>();q.Enqueue(part.Handle);double length=0;
                    while(q.Count>0)
                    {
                        string h=q.Dequeue();if(!run.Add(h))continue;var p=parts[h];string kind=p.Properties.GetValueOrDefault("PnPClassName","");
                        if(IsSupport(p)||kind is "Tee" or "Reducer")continue;
                        if(kind=="Pipe"){if(PartDiameter.Read(p) is not {} d||Math.Abs(d-small)>.001)continue;length+=Length(p);}
                        foreach(var next in adjacent[h])if(!run.Contains(next.Other))q.Enqueue(next.Other);
                    }
                    if(length>500)preferred.Add(Key(current.Link));break;
                }
            }
        }
        return new(protectedLinks,preferred);
    }
}
