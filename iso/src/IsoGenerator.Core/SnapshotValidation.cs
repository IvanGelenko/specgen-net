namespace IsoGenerator.Core;

public sealed record ValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, int ConnectedComponents);

public static class SnapshotValidation
{
    public static ValidationResult Validate(PipelineSnapshot snapshot)
    {
        var errors=new List<string>(); var warnings=new List<string>();
        if(snapshot.SchemaVersion!=1) errors.Add("Unsupported snapshot schema");
        if(snapshot.Parts.Count==0) errors.Add("Empty snapshot");
        var duplicates=snapshot.Parts.GroupBy(p=>p.Handle,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1);
        foreach(var d in duplicates) errors.Add($"Duplicate handle {d.Key}");
        if(errors.Count>0) return new(errors,warnings,0);
        var map=snapshot.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
        var adjacency=map.Keys.ToDictionary(k=>k,_=>new HashSet<string>(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
        foreach(var p in snapshot.Parts)
        {
            if(!string.Equals(p.SourceDrawing,snapshot.SourceDrawing,StringComparison.OrdinalIgnoreCase)) errors.Add($"{p.Handle}: different source drawing");
            foreach(var port in p.Ports)
                if(!double.IsFinite(port.Position.X)||!double.IsFinite(port.Position.Y)||!double.IsFinite(port.Position.Z)||!double.IsFinite(port.Direction.X)||!double.IsFinite(port.Direction.Y)||!double.IsFinite(port.Direction.Z)) errors.Add($"{p.Handle}: nonfinite port coordinates or direction");
            if(p.Ports.GroupBy(x=>x.Name).Any(g=>g.Count()>1)) errors.Add($"{p.Handle}: duplicate port names");
        }
        foreach(var c in snapshot.Connections)
        {
            if(!map.TryGetValue(c.FirstHandle,out var a)||!map.TryGetValue(c.SecondHandle,out var b)) {warnings.Add($"Boundary connection {c.FirstHandle}:{c.FirstPort} -> {c.SecondHandle}:{c.SecondPort}");continue;}
            var pa=a.Ports.FirstOrDefault(x=>x.Name==c.FirstPort); var pb=b.Ports.FirstOrDefault(x=>x.Name==c.SecondPort);
            if(pa is null||pb is null) {errors.Add($"Unreadable connected port {c.FirstHandle}/{c.SecondHandle}");continue;}
            double gap=pa.Position.DistanceTo(pb.Position);
            if(gap>1) errors.Add($"Connected ports {c.FirstHandle}:{c.FirstPort}/{c.SecondHandle}:{c.SecondPort} differ by {gap:F3} mm");
            adjacency[a.Handle].Add(b.Handle);adjacency[b.Handle].Add(a.Handle);
        }
        var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var componentOf=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase); int components=0;
        foreach(var handle in map.Keys) if(visited.Add(handle))
        {
            components++;componentOf[handle]=components; var queue=new Queue<string>();queue.Enqueue(handle);
            while(queue.Count>0) foreach(var n in adjacency[queue.Dequeue()]) if(visited.Add(n)){componentOf[n]=components;queue.Enqueue(n);}
        }
        if(components>1) warnings.Add($"Line contains {components} disconnected components; no artificial links were created");
        if(components>1)
        {
            var occupied=snapshot.Connections.SelectMany(c=>new[]{c.FirstHandle+":"+c.FirstPort,c.SecondHandle+":"+c.SecondPort}).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var free=snapshot.Parts.Where(p=>!p.IsConnector&&p.Component!=ComponentKind.Support).SelectMany(p=>p.Ports.Where(port=>port.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)&&!occupied.Contains(p.Handle+":"+port.Name)).Select(port=>(Part:p,Port:port))).ToArray();
            for(int i=0;i<free.Length;i++)for(int j=i+1;j<free.Length;j++)
            {
                var a=free[i];var b=free[j];if(componentOf[a.Part.Handle]==componentOf[b.Part.Handle])continue;
                double gap=a.Port.Position.DistanceTo(b.Port.Position);if(gap>1)continue;
                var u=a.Port.Direction;var v=b.Port.Direction;double lengths=Math.Sqrt((u.X*u.X+u.Y*u.Y+u.Z*u.Z)*(v.X*v.X+v.Y*v.Y+v.Z*v.Z));
                if(lengths<1e-10||(u.X*v.X+u.Y*v.Y+u.Z*v.Z)/lengths>-.99)continue;
                warnings.Add(FormattableString.Invariant($"Нет соединения: {a.Part.Handle}:{a.Port.Name} / {b.Part.Handle}:{b.Port.Name}; расстояние {gap:0.####} мм. Проверьте соединение в модели Plant."));
            }
        }
        return new(errors,warnings,components);
    }
}
