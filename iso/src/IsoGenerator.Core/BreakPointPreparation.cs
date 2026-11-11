using System.Globalization;
namespace IsoGenerator.Core;

public static class BreakPointPreparation
{
    // Plant's native PnP3dInfo Xrecord value for the PLANTISOADDINFO break marker.
    public const int BreakPointInformationType=4;
    public static PipelineSnapshot Apply(PipelineSnapshot original,bool ignoreBreakPoints)
    {
        var markers=original.IsoMarkers??Array.Empty<ModelIsoMarker>();
        if(markers.Count==0)return ignoreBreakPoints?original with{RequiredCuts=Array.Empty<Connection>()}:original;
        var markerHandles=markers.Select(m=>m.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var symbolic=markers.Select(m=>m.HostHandle+":"+m.HostPort).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parts=original.Parts.Where(p=>!markerHandles.Contains(p.Handle)).Select(p=>p with{Ports=p.Ports.Where(port=>!symbolic.Contains(p.Handle+":"+port.Name)).ToArray()}).ToArray();
        var connections=original.Connections.Where(c=>!markerHandles.Contains(c.FirstHandle)&&!markerHandles.Contains(c.SecondHandle)).ToList();
        if(ignoreBreakPoints)return original with{Parts=parts,Connections=connections,IsoMarkers=Array.Empty<ModelIsoMarker>(),RequiredCuts=Array.Empty<Connection>()};
        var byHost=markers.Where(m=>m.InformationType==BreakPointInformationType).GroupBy(m=>m.HostHandle,StringComparer.OrdinalIgnoreCase);
        var replacement=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var result=parts.ToList();var cuts=new List<Connection>();
        foreach(var group in byHost)
        {
            var host=parts.FirstOrDefault(p=>p.Handle.Equals(group.Key,StringComparison.OrdinalIgnoreCase));if(host is null)continue;
            var ends=host.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();if(ends.Length==0)throw new InvalidDataException($"{host.Handle}: не найдены порты компонента с точкой разрыва.");
            var internalMarkers=new List<(ModelIsoMarker Marker,double T,ModelPoint At)>();
            foreach(var marker in group)
            {
                var nearest=ends.MinBy(p=>p.Position.DistanceTo(marker.Position))!;
                if(nearest.Position.DistanceTo(marker.Position)<=1)
                {
                    var link=connections.FirstOrDefault(c=>c.FirstHandle==host.Handle&&c.FirstPort==nearest.Name||c.SecondHandle==host.Handle&&c.SecondPort==nearest.Name);
                    if(link is not null)cuts.Add(link);continue;
                }
                if(host.Properties.GetValueOrDefault("PnPClassName")!="Pipe"||ends.Length!=2)throw new InvalidDataException($"{marker.Handle}: точка разрыва находится внутри фитинга; перенесите её на соединение.");
                var a=ends[0].Position;var b=ends[1].Position;double dx=b.X-a.X,dy=b.Y-a.Y,dz=b.Z-a.Z,l2=dx*dx+dy*dy+dz*dz;
                double t=((marker.Position.X-a.X)*dx+(marker.Position.Y-a.Y)*dy+(marker.Position.Z-a.Z)*dz)/l2;var at=new ModelPoint(a.X+dx*t,a.Y+dy*t,a.Z+dz*t);
                if(t<=0||t>=1||at.DistanceTo(marker.Position)>1)throw new InvalidDataException($"{marker.Handle}: точка разрыва не лежит на оси своей трубы.");
                internalMarkers.Add((marker,t,at));
            }
            if(internalMarkers.Count==0)continue;
            var sorted=internalMarkers.OrderBy(m=>m.T).DistinctBy(m=>Math.Round(m.T*ends[0].Position.DistanceTo(ends[1].Position),3)).ToArray();
            var points=new[]{ends[0].Position}.Concat(sorted.Select(m=>m.At)).Append(ends[1].Position).ToArray();
            result.RemoveAll(p=>p.Handle==host.Handle);var fragments=new List<Part>();
            for(int i=0;i<points.Length-1;i++)
            {
                string handle=host.Handle+"#ISO"+(i+1);var values=new Dictionary<string,string>(host.Properties,StringComparer.OrdinalIgnoreCase){["IsoOriginalHandle"]=host.Properties.GetValueOrDefault("IsoOriginalHandle",host.Handle),["Length"]=points[i].DistanceTo(points[i+1]).ToString(CultureInfo.InvariantCulture)};
                var ports=new List<Port>{new("S1",points[i],ends[0].Direction,i==0?ends[0].EndCondition:"ISO-CONTINUATION"),new("S2",points[i+1],ends[1].Direction,i==points.Length-2?ends[1].EndCondition:"ISO-CONTINUATION")};
                foreach(var port in host.Ports.Except(ends))
                {
                    double t=(port.Position.X-ends[0].Position.X)*(ends[1].Position.X-ends[0].Position.X)+(port.Position.Y-ends[0].Position.Y)*(ends[1].Position.Y-ends[0].Position.Y)+(port.Position.Z-ends[0].Position.Z)*(ends[1].Position.Z-ends[0].Position.Z);
                    t/=Math.Pow(ends[0].Position.DistanceTo(ends[1].Position),2);
                    int index=Array.FindIndex(sorted,m=>t<=m.T);if(index<0)index=points.Length-2;
                    if(index==i){ports.Add(port);replacement[host.Handle+":"+port.Name]=handle+":"+port.Name;}
                }
                fragments.Add(host with{Handle=handle,Ports=ports,Properties=values});
            }
            replacement[host.Handle+":"+ends[0].Name]=fragments[0].Handle+":S1";replacement[host.Handle+":"+ends[1].Name]=fragments[^1].Handle+":S2";
            result.AddRange(fragments);
            for(int i=0;i<fragments.Count-1;i++){var link=new Connection(fragments[i].Handle,"S2",fragments[i+1].Handle,"S1");connections.Add(link);cuts.Add(link);}
        }
        (string Handle,string Port) End(string handle,string port)
        {if(!replacement.TryGetValue(handle+":"+port,out var value))return(handle,port);int colon=value.LastIndexOf(':');return(value[..colon],value[(colon+1)..]);}
        Connection Map(Connection c){var a=End(c.FirstHandle,c.FirstPort);var b=End(c.SecondHandle,c.SecondPort);return new(a.Handle,a.Port,b.Handle,b.Port);}
        var prepared=original with{Parts=result,Connections=connections.Select(Map).ToArray(),RequiredCuts=cuts.Select(Map).Distinct().ToArray(),IsoMarkers=Array.Empty<ModelIsoMarker>()};
        foreach(var host in parts.Where(p=>p.Properties.GetValueOrDefault("PnPClassName")=="Pipe"))
        {
            var fragments=prepared.Parts.Where(p=>p.Handle==host.Handle||p.Properties.GetValueOrDefault("IsoOriginalHandle")==host.Handle).ToArray();
            double Length(Part p){var ends=p.Ports.Where(q=>q.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();return ends[0].Position.DistanceTo(ends[1].Position);}
            if(Math.Abs(fragments.Sum(Length)-Length(host))>.001)throw new InvalidDataException("Нарушена длина трубы при разделении по точке разрыва: "+host.Handle);
        }
        return prepared;
    }
}
