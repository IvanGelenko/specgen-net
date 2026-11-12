namespace IsoGenerator.Core;

public static class TapDimensionReferences
{
    // Keep the receiving pipe axis as dimension context when it belongs to another sheet.
    // This adds no physical part, length, BOM item, weld or artificial connection.
    public static PipelineSnapshot Apply(PipelineSnapshot source,PipelineSnapshot fragment)
    {
        var included=fragment.Parts.Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var references=(source.BoundaryTapAxes??Array.Empty<BoundaryTapAxis>()).Where(r=>included.Contains(r.Handle)).ToList();
        foreach(var pipe in source.Parts.Where(p=>!included.Contains(p.Handle)&&p.Properties.GetValueOrDefault("PnPClassName")=="Pipe"))
        {
            var ends=pipe.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();if(ends.Length!=2)continue;
            var a=ends[0].Position;var b=ends[1].Position;double dx=b.X-a.X,dy=b.Y-a.Y,dz=b.Z-a.Z,l2=dx*dx+dy*dy+dz*dz;if(l2<1e-10)continue;
            foreach(var port in pipe.Ports.Except(ends).Where(p=>p.EndCondition.Trim().Equals("TAP",StringComparison.OrdinalIgnoreCase)))
            {
                double t=((port.Position.X-a.X)*dx+(port.Position.Y-a.Y)*dy+(port.Position.Z-a.Z)*dz)/l2;
                if(t<0||t>1)continue;var axis=new ModelPoint(a.X+t*dx,a.Y+t*dy,a.Z+t*dz);
                foreach(var link in source.Connections)
                {
                    if(link.FirstHandle.Equals(pipe.Handle,StringComparison.OrdinalIgnoreCase)&&link.FirstPort==port.Name&&included.Contains(link.SecondHandle))references.Add(new(link.SecondHandle,link.SecondPort,axis));
                    else if(link.SecondHandle.Equals(pipe.Handle,StringComparison.OrdinalIgnoreCase)&&link.SecondPort==port.Name&&included.Contains(link.FirstHandle))references.Add(new(link.FirstHandle,link.FirstPort,axis));
                }
            }
        }
        return fragment with{BoundaryTapAxes=references.Distinct().ToArray()};
    }
}
