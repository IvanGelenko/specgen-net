namespace IsoGenerator.Core;
internal sealed record FlangedJoint(string Handle,string ReferenceHandle,string ReferencePort,string IncludedHandle,string IncludedPort);
internal static class FlangedJointPolicy
{
    // Suppress both gasket faces and the FL face of the companion flange.
    // Keep its weld neck as the assembly dimension boundary.
    public static HashSet<string> HiddenDimensionPorts(PipelineSnapshot snapshot)
    {
        var parts=snapshot.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
        var hidden=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var joint in Find(snapshot))
        {
            hidden.Add(joint.IncludedHandle+":"+joint.IncludedPort);
            hidden.Add(joint.ReferenceHandle+":"+joint.ReferencePort);
        }
        // A boundary gasket still belongs to the adjacent assembly even when its other
        // flange belongs to a different line and is absent from this snapshot.
        foreach(var joint in snapshot.Parts.Where(p=>p.Kind=="AcPpDb3dConnector"))
        foreach(var connection in snapshot.Connections.Where(c=>c.FirstHandle==joint.Handle||c.SecondHandle==joint.Handle))
        {
            string handle=connection.FirstHandle==joint.Handle?connection.SecondHandle:connection.FirstHandle;
            string portName=connection.FirstHandle==joint.Handle?connection.SecondPort:connection.FirstPort;
            if(parts.TryGetValue(handle,out var component)&&component.Kind!="AcPpDb3dConnector"&&component.Ports.Any(p=>p.Name==portName&&p.EndCondition.Trim().Equals("FL",StringComparison.OrdinalIgnoreCase)))hidden.Add(handle+":"+portName);
        }
        // Direct flange/valve connections without an explicit gasket connector.
        foreach(var c in snapshot.Connections)
        {
            if(!parts.TryGetValue(c.FirstHandle,out var a)||!parts.TryGetValue(c.SecondHandle,out var b))continue;
            bool Pair(Part f,string fp,Part v,string vp)=>f.Properties.GetValueOrDefault("PnPClassName")=="Flange"&&v.Properties.GetValueOrDefault("PnPClassName")=="Valve"&&f.Ports.Any(p=>p.Name==fp&&p.EndCondition=="FL")&&v.Ports.Any(p=>p.Name==vp&&p.EndCondition=="FL");
            if(Pair(a,c.FirstPort,b,c.SecondPort)||Pair(b,c.SecondPort,a,c.FirstPort))
            {hidden.Add(a.Handle+":"+c.FirstPort);hidden.Add(b.Handle+":"+c.SecondPort);}
        }
        return hidden;
    }
    // Include the gasket in the valve/adjacent-component dimension. Its faces coincide only on paper.
    public static IReadOnlyList<FlangedJoint> Find(PipelineSnapshot snapshot)
    {
        var parts=snapshot.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);var result=new List<FlangedJoint>();
        foreach(var joint in snapshot.Parts.Where(p=>p.Kind=="AcPpDb3dConnector"))
        {
            var ends=snapshot.Connections.SelectMany(c=>c.FirstHandle==joint.Handle?new[]{(Handle:c.SecondHandle,Port:c.SecondPort)}:c.SecondHandle==joint.Handle?new[]{(Handle:c.FirstHandle,Port:c.FirstPort)}:Array.Empty<(string Handle,string Port)>())
                .Distinct().Where(e=>parts.TryGetValue(e.Handle,out var p)&&p.Kind!="AcPpDb3dConnector").ToArray();
            if(ends.Length!=2)continue;
            if(ends.Any(e=>parts[e.Handle].Ports.FirstOrDefault(p=>p.Name==e.Port)?.EndCondition.Trim().ToUpperInvariant()!="FL"))continue;
            var ordered=ends.OrderBy(e=>parts[e.Handle].Properties.GetValueOrDefault("PnPClassName")=="Flange"?0:1).ThenBy(e=>e.Handle,StringComparer.Ordinal).ToArray();
            result.Add(new(joint.Handle,ordered[0].Handle,ordered[0].Port,ordered[1].Handle,ordered[1].Port));
        }
        return result;
    }
}
