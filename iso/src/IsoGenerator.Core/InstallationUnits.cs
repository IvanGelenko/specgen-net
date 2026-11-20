namespace IsoGenerator.Core;

public sealed record InstallationUnit(string Kind,IReadOnlySet<string> Handles);

/// <summary>Topology-only recognition; a valve-containing cycle is a candidate bypass unit.</summary>
public static class InstallationUnits
{
    public static IReadOnlyList<InstallationUnit> Find(PipelineSnapshot source)=>Find(source,null);

    internal static IReadOnlyList<InstallationUnit> Find(PipelineSnapshot source,PlanningExecution? execution)
    {
        var parts=source.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
        var adjacency=parts.Keys.ToDictionary(h=>h,_=>new List<int>(),StringComparer.OrdinalIgnoreCase);
        var links=source.Connections.Where(c=>parts.ContainsKey(c.FirstHandle)&&parts.ContainsKey(c.SecondHandle)&&c.FirstHandle!=c.SecondHandle)
            .Where(c=>BomCategories.Classify(parts[c.FirstHandle])!=BomCategory.Supports&&BomCategories.Classify(parts[c.SecondHandle])!=BomCategory.Supports)
            .DistinctBy(CutPreferences.Key).OrderBy(CutPreferences.Key,StringComparer.OrdinalIgnoreCase).ToArray();
        for(int i=0;i<links.Length;i++){adjacency[links[i].FirstHandle].Add(i);adjacency[links[i].SecondHandle].Add(i);}
        var discovered=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);var low=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        var stack=new Stack<int>();var result=new List<InstallationUnit>();int clock=0;
        string Other(int edge,string h)=>string.Equals(links[edge].FirstHandle,h,StringComparison.OrdinalIgnoreCase)?links[edge].SecondHandle:links[edge].FirstHandle;
        void Block(int stop)
        {
            var vertices=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int count=0,edge;
            do{edge=stack.Pop();count++;vertices.Add(links[edge].FirstHandle);vertices.Add(links[edge].SecondHandle);}while(edge!=stop);
            // Biconnected blocks separate neighbouring loops at articulation points.
            // A plain pipe ring is not assumed to be a valve bypass.
            if(vertices.Count<3||count<vertices.Count)return;
            if(!vertices.Any(h=>parts[h].Component==ComponentKind.Valve))return;
            if(vertices.Count(h=>adjacency[h].Select(i=>Other(i,h)).Distinct(StringComparer.OrdinalIgnoreCase).Count()>=3)<2)return;
            result.Add(new("BypassCandidate",vertices));
        }
        void Visit(string h,int parent)
        {
            execution?.Check();discovered[h]=low[h]=++clock;
            foreach(int edge in adjacency[h])
            {
                if(edge==parent)continue;string next=Other(edge,h);
                if(!discovered.ContainsKey(next))
                {
                    stack.Push(edge);Visit(next,edge);low[h]=Math.Min(low[h],low[next]);
                    if(low[next]>=discovered[h])Block(edge);
                }
                else if(discovered[next]<discovered[h]){stack.Push(edge);low[h]=Math.Min(low[h],discovered[next]);}
            }
        }
        foreach(string h in parts.Keys.OrderBy(h=>h,StringComparer.OrdinalIgnoreCase))if(!discovered.ContainsKey(h))Visit(h,-1);
        return result;
    }

    // Minimise broken units before balancing sheet population. Explicit cuts still win.
    public static int SplitCount(IEnumerable<InstallationUnit> units,IReadOnlySet<string> first,IReadOnlySet<string> second)=>
        units.Count(unit=>unit.Handles.Any(first.Contains)&&unit.Handles.Any(second.Contains));
}
