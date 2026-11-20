namespace IsoGenerator.Core;

// Layout-only support attachments; never infer a process connection between pipes.
internal static class SheetConnectivity
{
    // Annotation ownership only. This never repairs or writes a Plant process connection.
    public static PipelineSnapshot AttachInstrumentCallouts(PipelineSnapshot source,PlanningExecution? execution=null)
    {
        var links=source.Connections.ToList();var warnings=source.Warnings.ToList();
        var updated=source.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
        var occupied=links.SelectMany(c=>new[]{c.FirstHandle+":"+c.FirstPort,c.SecondHandle+":"+c.SecondPort}).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var instrument in source.Parts.Where(p=>BomCategories.Classify(p)==BomCategory.Instruments))
        {
            execution?.Check();
            if(links.Any(c=>c.FirstHandle==instrument.Handle||c.SecondHandle==instrument.Handle))continue;
            var ends=instrument.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(ends.Length!=1)throw new InvalidDataException($"{instrument.Handle}: не удалось определить узел для выноски несоединённого КИП. Проверьте подключение в Plant.");
            var at=ends[0];
            var candidates=(from host in source.Parts
                where host.Handle!=instrument.Handle&&host.Line==instrument.Line&&!host.IsConnector&&BomCategories.Classify(host) is not (BomCategory.Instruments or BomCategory.Supports)
                from port in host.Ports
                where port.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)&&!occupied.Contains(host.Handle+":"+port.Name)&&port.Position.DistanceTo(at.Position)<=.01
                let u=at.Direction let v=port.Direction
                let length=Math.Sqrt((u.X*u.X+u.Y*u.Y+u.Z*u.Z)*(v.X*v.X+v.Y*v.Y+v.Z*v.Z))
                where length>1e-10&&(u.X*v.X+u.Y*v.Y+u.Z*v.Z)/length<=-.99
                select (Host:host,Port:port)).ToArray();
            if(candidates.Length!=1)throw new InvalidDataException($"{instrument.Handle}: для выноски КИП найдено {candidates.Length} однозначных подключений. Проверьте соединение в Plant; отдельный пустой лист не создаётся.");
            var owner=candidates[0];
            updated[instrument.Handle]=instrument with{Properties=new Dictionary<string,string>(instrument.Properties){["IsoCalloutHost"]=owner.Host.Handle}};
            links.Add(new(owner.Host.Handle,owner.Port.Name,instrument.Handle,at.Name));
            occupied.Add(owner.Host.Handle+":"+owner.Port.Name);occupied.Add(instrument.Handle+":"+at.Name);
            warnings.Add($"Нет соединения: {instrument.Handle}:{at.Name} / {owner.Host.Handle}:{owner.Port.Name}. Выноска КИП привязана к совпадающему порту только для компоновки; модель Plant не изменена.");
        }
        return source with{Parts=source.Parts.Select(p=>updated[p.Handle]).ToArray(),Connections=links,Warnings=warnings};
    }
    public static PipelineSnapshot AttachSupports(PipelineSnapshot source)
    {
        var parts=source.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
        var links=source.Connections.ToList();var warnings=source.Warnings.ToList();
        foreach(var support in source.Parts.Where(p=>BomCategories.Classify(p)==BomCategory.Supports))
        {
            if(links.Any(c=>c.FirstHandle==support.Handle||c.SecondHandle==support.Handle)||support.Ports.Count==0)continue;
            var cp=support.Ports[0].Position;
            var hosts=new List<(Part Part,Port A,Port B)>();
            foreach(var pipe in parts.Values.Where(p=>p.Component==ComponentKind.Pipe))
            {
                var ends=pipe.Ports.Where(p=>p.Name is "S1" or "S2").ToArray();if(ends.Length!=2)continue;
                var a=ends[0].Position;var b=ends[1].Position;double dx=b.X-a.X,dy=b.Y-a.Y,dz=b.Z-a.Z,l2=dx*dx+dy*dy+dz*dz;
                if(l2<1e-8)continue;double t=((cp.X-a.X)*dx+(cp.Y-a.Y)*dy+(cp.Z-a.Z)*dz)/l2;
                if(t<0||t>1||cp.DistanceTo(new(a.X+t*dx,a.Y+t*dy,a.Z+t*dz))>.01)continue;
                hosts.Add((pipe,ends[0],ends[1]));
            }
            if(hosts.Count!=1)throw new InvalidDataException($"{support.Handle}: опора без связи с трубой; не удалось однозначно определить трубу по CP.");
            var host=hosts[0];string name="ISO-SUPPORT-"+support.Handle;
            var port=host.Part.Ports.FirstOrDefault(p=>p.Position.DistanceTo(cp)<.001);
            if(port is null){port=new Port(name,cp,host.A.Direction,"SUPPORT");parts[host.Part.Handle]=host.Part with{Ports=host.Part.Ports.Append(port).ToArray()};}
            links.Add(new(host.Part.Handle,port.Name,support.Handle,support.Ports[0].Name));
            warnings.Add($"{support.Handle}: опора привязана к трубе {host.Part.Handle} по CP только для компоновки; модель Plant не изменена.");
        }
        return source with{Parts=source.Parts.Select(p=>parts[p.Handle]).ToArray(),Connections=links,Warnings=warnings};
    }
    public static IReadOnlyList<HashSet<string>> Components(PipelineSnapshot snapshot)
    {
        var remaining=snapshot.Parts.Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var adjacency=remaining.ToDictionary(h=>h,_=>new List<string>(),StringComparer.OrdinalIgnoreCase);
        foreach(var c in snapshot.Connections)if(adjacency.TryGetValue(c.FirstHandle,out var a)&&adjacency.TryGetValue(c.SecondHandle,out var b)){a.Add(c.SecondHandle);b.Add(c.FirstHandle);}
        var result=new List<HashSet<string>>();
        while(remaining.Count>0){string root=remaining.First();remaining.Remove(root);var group=new HashSet<string>(StringComparer.OrdinalIgnoreCase){root};var q=new Queue<string>();q.Enqueue(root);while(q.Count>0)foreach(var next in adjacency[q.Dequeue()])if(remaining.Remove(next)){group.Add(next);q.Enqueue(next);}result.Add(group);}
        return result;
    }
}
