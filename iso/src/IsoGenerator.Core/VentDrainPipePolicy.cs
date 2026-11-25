namespace IsoGenerator.Core;

internal static class VentDrainPipePolicy
{
    public const double MaxPaperLength=20;
    const double MaxVerticalModelLength=200;

    public static HashSet<string> Find(PipelineSnapshot snapshot)
    {
        var comparer=StringComparer.OrdinalIgnoreCase;
        var capped=new HashSet<string>(comparer);
        var candidates=snapshot.Parts.Where(part=>part.Component==ComponentKind.Pipe)
            .Select(part=>(Part:part,Ports:part.Ports.Where(port=>IsPipeEnd(port.Name)).ToArray()))
            .Where(candidate=>candidate.Ports.Length==2&&candidate.Ports[0].Position.DistanceTo(candidate.Ports[1].Position)>1)
            .ToArray();
        if(candidates.Length==0)return capped;

        var contexts=snapshot.ExternalLineContexts??Array.Empty<ExternalLineContext>();
        var parts=snapshot.Parts.Concat(contexts.SelectMany(context=>context.Parts))
            .DistinctBy(part=>part.Handle,comparer).ToDictionary(part=>part.Handle,comparer);
        var adjacency=parts.Keys.ToDictionary(handle=>handle,_=>new List<(string Port,string Other,string OtherPort)>(),comparer);
        foreach(var connection in snapshot.Connections.Concat(contexts.SelectMany(context=>context.Connections)))
        {
            if(adjacency.TryGetValue(connection.FirstHandle,out var first))
                first.Add((connection.FirstPort,connection.SecondHandle,connection.SecondPort));
            if(adjacency.TryGetValue(connection.SecondHandle,out var second))
                second.Add((connection.SecondPort,connection.FirstHandle,connection.FirstPort));
        }
        foreach(var (pipe,ports) in candidates)
        {
            bool vertical=IsVertical(ports);
            if(vertical&&ports[0].Position.DistanceTo(ports[1].Position)>MaxVerticalModelLength)continue;
            if(adjacency[pipe.Handle].All(link=>IsPipeEnd(link.Port)||
                (parts.TryGetValue(link.Other,out var attachment)&&BomCategories.Classify(attachment)==BomCategory.Supports))&&
                Enumerable.Range(0,2).Any(end=>(vertical||StartsAtSideBranch(pipe.Handle,ports[1-end].Name))&&
                    EndsInValve(pipe.Handle,ports[end].Name)))capped.Add(pipe.Handle);
        }
        return capped;

        bool StartsAtSideBranch(string pipe,string end)
        {
            var pending=new Queue<(string Handle,string Port)>(adjacency[pipe]
                .Where(link=>comparer.Equals(link.Port,end)).Select(link=>(link.Other,link.OtherPort)));
            var seen=new HashSet<string>(comparer){pipe};
            while(pending.TryDequeue(out var current))
            {
                if(!seen.Add(current.Handle)||!parts.TryGetValue(current.Handle,out var part))continue;
                if(part.Component==ComponentKind.Pipe&&!IsPipeEnd(current.Port))return true;
                if(part.Component==ComponentKind.Tee&&IsTeeBranch(part,current.Port))return true;
                if(part.Component==ComponentKind.Reducer&&IsPipeEnd(current.Port)&&
                   part.Ports.Count(port=>IsPipeEnd(port.Name))==2)
                {
                    foreach(var link in adjacency[current.Handle])
                        if(IsPipeEnd(link.Port)&&!comparer.Equals(link.Port,current.Port)&&!seen.Contains(link.Other))
                            pending.Enqueue((link.Other,link.OtherPort));
                    continue;
                }
                if(part.IsConnector)
                    foreach(var link in adjacency[current.Handle])
                        if(!seen.Contains(link.Other))pending.Enqueue((link.Other,link.OtherPort));
            }
            return false;
        }

        bool EndsInValve(string pipe,string end)
        {
            var pending=new Queue<string>(adjacency[pipe].Where(link=>comparer.Equals(link.Port,end)).Select(link=>link.Other));
            var seen=new HashSet<string>(comparer){pipe};
            bool valveFound=false,terminalFound=false;
            while(pending.TryDequeue(out string? handle))
            {
                if(!seen.Add(handle))continue;
                if(!parts.TryGetValue(handle,out var part))return false;
                var category=BomCategories.Classify(part);
                if(category==BomCategory.Supports)continue;
                if(category==BomCategory.Valves)valveFound=true;
                else if(!part.IsConnector&&part.Component is not (ComponentKind.Reducer or ComponentKind.Elbow)&&
                        category is not (BomCategory.Flanges or BomCategory.Gaskets or BomCategory.Fasteners))return false;
                var links=adjacency[handle];
                if(part.Component is ComponentKind.BlindFlange or ComponentKind.Cap||
                   ((category is BomCategory.Valves or BomCategory.Flanges)&&part.Ports.Any(port=>
                       IsPipeEnd(port.Name)&&links.All(link=>!comparer.Equals(port.Name,link.Port)))))
                    terminalFound=true;
                foreach(var link in links)
                {
                    if(comparer.Equals(link.Other,pipe)&&!comparer.Equals(link.OtherPort,end))return false;
                    if(!seen.Contains(link.Other))pending.Enqueue(link.Other);
                }
            }
            return valveFound&&terminalFound;
        }
    }

    static bool IsPipeEnd(string port)=>port.Equals("S1",StringComparison.OrdinalIgnoreCase)||
        port.Equals("S2",StringComparison.OrdinalIgnoreCase);

    static bool IsTeeBranch(Part tee,string portName)
    {
        var ports=tee.Ports.Where(port=>port.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(ports.Length!=3||!ports.Any(port=>port.Name.Equals(portName,StringComparison.OrdinalIgnoreCase)))return false;
        var run=ports.Where(port=>!port.Name.Equals(portName,StringComparison.OrdinalIgnoreCase)).ToArray();
        var a=run[0].Direction;var b=run[1].Direction;
        double dot=a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        double lengths=a.DistanceTo(default)*b.DistanceTo(default);
        return lengths>0&&dot<-.99*lengths;
    }

    static bool IsVertical(Port[] ports)
    {
        var a=ports[0].Position;var b=ports[1].Position;
        double rise=Math.Abs(a.Z-b.Z);
        return rise>1&&Math.Abs(a.X-b.X)<=.01*rise&&Math.Abs(a.Y-b.Y)<=.01*rise;
    }
}
