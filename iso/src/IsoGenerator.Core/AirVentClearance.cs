namespace IsoGenerator.Core;

internal sealed class AirVentSpacingException(string pipe,double extra):SheetCapacityException("Недостаточно места над размерным рядом для воздушника.",pipe,conflict:LayoutConflict.AirVentClearance)
{
    public string Pipe{get;}=pipe;
    public double Extra{get;}=extra;
}
internal static class AirVentClearance
{
    // Move only the paper neck of an upward small-bore terminal valve assembly.
    // Model coordinates, cut lengths and the engineering dimension chain stay intact.
    public static void Check(PipelineSnapshot snapshot,SchematicLayout layout,IReadOnlyList<PipeDimension> dimensions,IReadOnlyList<SymbolGlyph> symbols)
    {
        var parts=snapshot.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
        var adjacent=parts.Keys.ToDictionary(h=>h,_=>new List<(string Other,string Port)>(),StringComparer.OrdinalIgnoreCase);
        foreach(var c in snapshot.Connections)if(parts.ContainsKey(c.FirstHandle)&&parts.ContainsKey(c.SecondHandle))
        {adjacent[c.FirstHandle].Add((c.SecondHandle,c.FirstPort));adjacent[c.SecondHandle].Add((c.FirstHandle,c.SecondPort));}
        foreach(var host in snapshot.Parts.Where(p=>p.Component is ComponentKind.Pipe or ComponentKind.Tee))
        foreach(var root in adjacent[host.Handle])
        {
            if(host.Component==ComponentKind.Pipe&&root.Port.StartsWith("S",StringComparison.OrdinalIgnoreCase))continue;
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase){host.Handle};var queue=new Queue<string>();queue.Enqueue(root.Other);
            var branch=new List<Part>();bool reconnects=false;
            while(queue.Count>0&&branch.Count<=12)
            {
                string h=queue.Dequeue();if(!seen.Add(h))continue;branch.Add(parts[h]);
                foreach(var next in adjacent[h])
                {
                    if(next.Other==host.Handle){if(h!=root.Other)reconnects=true;continue;}
                    if(!seen.Contains(next.Other))queue.Enqueue(next.Other);
                }
            }
            if(reconnects||queue.Count>0)continue;
            var physical=branch.Where(p=>!p.IsConnector&&BomCategories.Classify(p)!=BomCategory.Supports).ToArray();
            var necks=physical.Where(p=>p.Component==ComponentKind.Pipe).ToArray();
            var valves=physical.Where(p=>p.Component==ComponentKind.Valve).ToArray();
            if(necks.Length!=1||valves.Length!=1)continue;
            var neck=necks[0];var ends=neck.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).OrderBy(p=>p.Position.Z).ToArray();if(ends.Length!=2)continue;
            var low=ends[0].Position;var high=ends[1].Position;double rise=high.Z-low.Z;
            if(rise<=1||Math.Abs(high.X-low.X)>.01*rise||Math.Abs(high.Y-low.Y)>.01*rise)continue;
            var hostPort=host.Ports.FirstOrDefault(p=>p.Name==root.Port);if(hostPort is null||valves[0].Ports.Average(p=>p.Position.Z)<=hostPort.Position.Z)continue;
            bool explicitVent=physical.Any(p=>p.Properties.GetValueOrDefault("Service","").Trim().Equals("AV",StringComparison.OrdinalIgnoreCase));
            if(!explicitVent&&(PartDiameter.Read(host) is not {} diameter||physical.Any(p=>PartDiameter.Read(p) is {} size&&size*2>diameter)||PartDiameter.Read(neck) is not {} bore||bore*2>diameter))continue;
            var assembly=physical.Where(p=>p.Handle!=neck.Handle).Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var footprint=symbols.Where(g=>!g.Context&&assembly.Contains(g.SourceId)).SelectMany(SymbolFootprint.Corners)
                .Concat(physical.Where(p=>assembly.Contains(p.Handle)).SelectMany(p=>p.Ports.Where(port=>port.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).Select(port=>layout.Project(port.Position,p.Handle))))
                .ToArray();
            if(footprint.Length==0)continue;
            double left=footprint.Min(p=>p.X)-2,right=footprint.Max(p=>p.X)+2,bottom=footprint.Min(p=>p.Y),top=footprint.Max(p=>p.Y);
            double ceiling=double.NegativeInfinity;
            foreach(var d in dimensions)
            {
                // Only the dimension row of the run carrying the vent gets this dedicated clearance.
                if(!(d.SourceIds?.Contains(host.Handle,StringComparer.OrdinalIgnoreCase)??
                    d.SourceId.Equals(host.Handle,StringComparison.OrdinalIgnoreCase)))continue;
                // Consider dimensions of horizontal model runs, not the vent's own vertical row.
                if(d.ModelStart is not {} ms||d.ModelEnd is not {} me||Math.Abs(ms.Z-me.Z)>1||Math.Abs(d.End.X-d.Start.X)<.1)continue;
                double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,len=Math.Sqrt(dx*dx+dy*dy),nx=-dy/len,ny=dx/len;
                var ex=d.ExtensionDirection!.Value;var line=d.DimensionLinePoint!.Value;double dot=ex.X*nx+ex.Y*ny;if(Math.Abs(dot)<1e-8)continue;
                SheetPoint At(SheetPoint p){double t=((line.X-p.X)*nx+(line.Y-p.Y)*ny)/dot;return new(p.X+ex.X*t,p.Y+ex.Y*t);}
                var a=At(d.Start);var b=At(d.End);if(Math.Max(a.X,b.X)<left||Math.Min(a.X,b.X)>right)continue;
                double Y(double x)=>a.Y+(b.Y-a.Y)*(x-a.X)/(b.X-a.X);
                double y=Math.Max(Y(Math.Max(left,Math.Min(a.X,b.X))),Y(Math.Min(right,Math.Max(a.X,b.X))));
                if(y<bottom-5||y>top+2)continue;
                ceiling=Math.Max(ceiling,y+5); // Include text ink above the dimension baseline.
            }

            double extra=ceiling+5-bottom;
            if(extra>.1)
            {
                var a=layout.Project(low,neck.Handle);var b=layout.Project(high,neck.Handle);
                double length=Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Y-a.Y,2));
                // A remote row must be resolved by dimension routing. Only move a short
                // neck when the required clearance is local to its current paper length.
                if(b.Y-a.Y>.1&&extra<=b.Y-a.Y)
                    throw new AirVentSpacingException(neck.Handle,extra*length/(b.Y-a.Y)+1);
            }
        }
    }
}
