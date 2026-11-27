namespace IsoGenerator.Core;
public sealed record WeldMarker(string SourceId, SheetPoint Position, string FirstHandle,string FirstPort,
    string SecondHandle,string SecondPort,string FirstEndCondition,string SecondEndCondition,string JointType,ModelPoint? ModelPosition=null);
public sealed record WeldPlan(IReadOnlyList<WeldMarker> Markers,IReadOnlyList<string> Warnings);
public static class WeldPlanner
{
    static bool Weldable(string end)=>end.Trim().ToUpperInvariant() is "PL" or "BV";
    static string EndCondition(Part part,Port port)
    {
        string end=port.EndCondition.Trim().ToUpperInvariant();
        if(end.Length>0||!part.CatalogClass.Equals("Elbow",StringComparison.OrdinalIgnoreCase)||
            !port.Name.Equals("S2",StringComparison.OrdinalIgnoreCase))return end;

        // Some Plant catalogs supply the bevel on S1 only for butt-weld elbows.
        bool bevelOnFirstPort=part.Ports.Any(p=>p.Name.Equals("S1",StringComparison.OrdinalIgnoreCase)&&
            p.EndCondition.Trim().Equals("BV",StringComparison.OrdinalIgnoreCase));
        return bevelOnFirstPort&&IsoDefinition.Parse(part.SymbolDefinition) is {Type:"BEND",Skey:"BEBW"}?"BV":end;
    }
    public static WeldPlan Build(PipelineSnapshot snapshot,Func<ModelPoint,SheetPoint> project,Func<ModelPoint,string,SheetPoint>? projectSource=null)
    {
        var parts=snapshot.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
        var markers=new List<WeldMarker>();var warnings=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool IsJoint(Part p)=>p.IsConnector;
        void Candidate(string source,string ah,string ap,string bh,string bp)
        {
            if(!parts.TryGetValue(ah,out var a)||!parts.TryGetValue(bh,out var b))return;
            if(BomCategories.Classify(a)==BomCategory.Supports||BomCategories.Classify(b)==BomCategory.Supports)return;
            if(a.Properties.ContainsKey("IsoCalloutHost")||b.Properties.ContainsKey("IsoCalloutHost"))return;
            var pa=a.Ports.FirstOrDefault(p=>p.Name==ap);var pb=b.Ports.FirstOrDefault(p=>p.Name==bp);
            if(pa is null||pb is null){warnings.Add($"{source}: cannot read weld endpoints");return;}
            string ea=EndCondition(a,pa),eb=EndCondition(b,pb);
            if(ea.Length==0||eb.Length==0){warnings.Add($"{source}: missing per-port end condition at {ah}:{ap}/{bh}:{bp}; weld not guessed");return;}
            bool butt=Weldable(ea)&&Weldable(eb);
            bool tap=(ea=="TAP"&&Weldable(eb))||(eb=="TAP"&&Weldable(ea));
            if(!butt&&!tap)return;
            if(pa.Position.DistanceTo(pb.Position)>1){warnings.Add($"{source}: welded ports are not coincident; weld not drawn");return;}
            var key=string.Join("|",new[]{ah+":"+ap,bh+":"+bp}.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase));
            if(!seen.Add(key))return;
            var pos=new ModelPoint((pa.Position.X+pb.Position.X)/2,(pa.Position.Y+pb.Position.Y)/2,(pa.Position.Z+pb.Position.Z)/2);
            if(tap)
            {
                // In a schematic the branch weld belongs at the intersection of the pipe axes.
                var run=ea=="TAP"?a:b;var port=ea=="TAP"?pa:pb;
                var ends=run.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();
                if(ends.Length==2)
                {
                    var u=ends[0].Position;var v=ends[1].Position;
                    double dx=v.X-u.X,dy=v.Y-u.Y,dz=v.Z-u.Z,l2=dx*dx+dy*dy+dz*dz;
                    if(l2>1e-10){double t=((port.Position.X-u.X)*dx+(port.Position.Y-u.Y)*dy+(port.Position.Z-u.Z)*dz)/l2;pos=new(u.X+t*dx,u.Y+t*dy,u.Z+t*dz);}
                }
            }
            string owner=tap?(ea=="TAP"?ah:bh):ah;
            var mapped=projectSource is null?project(pos):projectSource(tap?pos:pa.Position,owner);
            markers.Add(new(source,mapped,ah,ap,bh,bp,ea,eb,tap?"Tap":"Buttweld",pos));
        }
        foreach(var joint in snapshot.Parts.Where(IsJoint))
        {
            var neighbors=new List<(string Handle,string Port)>();
            foreach(var link in snapshot.Connections)
            {
                if(link.FirstHandle.Equals(joint.Handle,StringComparison.OrdinalIgnoreCase))neighbors.Add((link.SecondHandle,link.SecondPort));
                else if(link.SecondHandle.Equals(joint.Handle,StringComparison.OrdinalIgnoreCase))neighbors.Add((link.FirstHandle,link.FirstPort));
            }
            var ends=neighbors.Where(e=>parts.TryGetValue(e.Handle,out var p)&&!IsJoint(p)).Distinct().ToArray();
            if(ends.Length==2)Candidate(joint.Handle,ends[0].Handle,ends[0].Port,ends[1].Handle,ends[1].Port);
            else warnings.Add($"{joint.Handle}: expected two connected physical ports, found {ends.Length}");
        }
        foreach(var link in snapshot.Connections)
            if(parts.TryGetValue(link.FirstHandle,out var a)&&parts.TryGetValue(link.SecondHandle,out var b)&&!IsJoint(a)&&!IsJoint(b))
                Candidate(a.Handle,link.FirstHandle,link.FirstPort,link.SecondHandle,link.SecondPort);
        foreach(var context in snapshot.ExternalLineContexts??Array.Empty<ExternalLineContext>())
        {
            var entry=context.Parts.FirstOrDefault(p=>p.Handle==context.EntryHandle);
            if(entry is null||IsJoint(entry))continue;
            parts.TryAdd(entry.Handle,entry);
            foreach(var connection in snapshot.Connections.Where(c=>c.FirstHandle==context.OwnerHandle||c.SecondHandle==context.OwnerHandle))
            {
                string handle=connection.FirstHandle==context.OwnerHandle?connection.SecondHandle:connection.FirstHandle;
                string port=connection.FirstHandle==context.OwnerHandle?connection.SecondPort:connection.FirstPort;
                if(!parts.TryGetValue(handle,out var run)||run.Component!=ComponentKind.Pipe||!run.Ports.Any(p=>p.Name==port&&p.EndCondition.Trim().Equals("TAP",StringComparison.OrdinalIgnoreCase)))continue;
                Candidate(context.OwnerHandle,handle,port,entry.Handle,context.EntryPort);
            }
        }
        return new(markers,warnings);
    }
}
