namespace IsoGenerator.Core;

internal static class ValveKitBomPolicy
{
    enum Member { Other, Valve, Flange, Blind, Joint, Fastener }

    public static HashSet<string> Exclusions(PipelineSnapshot snapshot)
    {
        var comparer=StringComparer.OrdinalIgnoreCase;
        var contexts=snapshot.ExternalLineContexts??Array.Empty<ExternalLineContext>();
        var members=snapshot.Parts.Concat(contexts.SelectMany(context=>context.Parts))
            .DistinctBy(part=>part.Handle,comparer)
            .Select(part=>(Part:part,Kind:Classify(part)))
            .Where(member=>member.Kind!=Member.Other)
            .ToDictionary(member=>member.Part.Handle,comparer);
        var neighbors=members.Keys.ToDictionary(handle=>handle,_=>new List<string>(),comparer);
        foreach(var connection in snapshot.Connections.Concat(contexts.SelectMany(context=>context.Connections)))
        {
            if(!members.TryGetValue(connection.FirstHandle,out var first)||
               !members.TryGetValue(connection.SecondHandle,out var second)||
               !IsFlangedFace(first.Part,first.Kind,connection.FirstPort)||
               !IsFlangedFace(second.Part,second.Kind,connection.SecondPort))continue;
            neighbors[first.Part.Handle].Add(second.Part.Handle);
            neighbors[second.Part.Handle].Add(first.Part.Handle);
        }

        var valves=members.Values.Where(member=>member.Kind==Member.Valve).Select(member=>member.Part.Handle).ToArray();
        var pending=new Queue<string>(valves);
        var visited=valves.ToHashSet(comparer);
        while(pending.TryDequeue(out var handle))
        {
            foreach(string neighbor in neighbors[handle])
            {
                if(!visited.Add(neighbor))continue;
                // A blind sits inside the flanged stack. The first companion flange ends it.
                if(members[neighbor].Kind is Member.Joint or Member.Blind)pending.Enqueue(neighbor);
            }
        }
        visited.ExceptWith(valves);
        foreach(var accessory in snapshot.Accessories??Array.Empty<Part>())
            if(visited.Contains(accessory.Properties.GetValueOrDefault("ConnectorHandle",""))&&
               BomCategories.Classify(accessory) is BomCategory.Gaskets or BomCategory.Fasteners)
                visited.Add(accessory.Handle);
        return visited;
    }

    static Member Classify(Part part)
    {
        if(part.IsConnector)return Member.Joint;
        var definition=IsoDefinition.Parse(part.SymbolDefinition);
        string catalog=part.CatalogClass.ToUpperInvariant();
        if(definition.Skey=="SB"||definition.Type=="FLANGE-BLIND"||
           catalog.Contains("BLIND",StringComparison.Ordinal)||catalog.Contains("SPECTACLE",StringComparison.Ordinal))
            return Member.Blind;
        return BomCategories.Classify(part) switch
        {
            BomCategory.Valves=>Member.Valve,
            BomCategory.Flanges=>Member.Flange,
            BomCategory.Gaskets=>Member.Joint,
            BomCategory.Fasteners=>Member.Fastener,
            _=>Member.Other
        };
    }

    static bool IsFlangedFace(Part part,Member kind,string port)=>
        kind is Member.Joint or Member.Fastener||part.Ports.Any(face=>
            face.Name.Equals(port,StringComparison.OrdinalIgnoreCase)&&IsFlangedEnd(face.EndCondition,kind));

    static bool IsFlangedEnd(string? condition,Member kind)=>
        condition?.Trim().ToUpperInvariant() switch
        {
            null or "" or "FL"=>true,
            "WF"=>kind is Member.Valve or Member.Blind,
            "LUG"=>kind==Member.Valve,
            _=>false
        };
}
