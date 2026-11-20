namespace IsoGenerator.Core;

internal enum RouteNodeKind {Port,Axis,TapAxis}

// Physical stations and connections, in model millimeters. Drawing layout only reads this graph.
internal sealed class PipelineTopology
{
    internal sealed record Node(ModelPoint Point,RouteNodeKind Kind,ComponentKind Component,string Source,string PortName = "");
    internal sealed record Edge(int A,int B,string Source,ComponentKind Kind);
    readonly record struct PortKey(string Handle,string Name);

    sealed class PortKeyComparer:IEqualityComparer<PortKey>
    {
        public static readonly PortKeyComparer Instance=new();
        public bool Equals(PortKey a,PortKey b)=>StringComparer.OrdinalIgnoreCase.Equals(a.Handle,b.Handle)&&
            StringComparer.OrdinalIgnoreCase.Equals(a.Name,b.Name);
        public int GetHashCode(PortKey key)=>HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Handle),
            StringComparer.OrdinalIgnoreCase.GetHashCode(key.Name));
    }

    static ModelPoint Sub(ModelPoint a,ModelPoint b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    static ModelPoint Add(ModelPoint a,ModelPoint b)=>new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
    static ModelPoint Mul(ModelPoint a,double k)=>new(a.X*k,a.Y*k,a.Z*k);
    static double Dot(ModelPoint a,ModelPoint b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    static ModelPoint Unit(ModelPoint a)=>Mul(a,1/Math.Sqrt(Dot(a,a)));
    static ModelPoint? Corner(Port a,Port b)
    {
        var w=Sub(a.Position,b.Position);var u=a.Direction;var v=b.Direction;
        double aa=Dot(u,u),bb=Dot(u,v),cc=Dot(v,v),dd=Dot(u,w),ee=Dot(v,w),den=aa*cc-bb*bb;
        if(Math.Abs(den)<1e-10)return null;
        var p=Add(a.Position,Mul(u,(bb*ee-cc*dd)/den));var q=Add(b.Position,Mul(v,(aa*ee-bb*dd)/den));
        return p.DistanceTo(q)<1?Mul(Add(p,q),.5):null;
    }
    public IReadOnlyList<Node> Nodes {get;}
    public IReadOnlyList<Edge> Edges {get;}
    readonly int[] roots;
    PipelineTopology(IReadOnlyList<Node> nodes,IReadOnlyList<Edge> edges,int[] roots)
    {
        Nodes=nodes;
        Edges=edges;
        this.roots=roots;
    }

    public int Root(int node)=>roots[node];

    public static PipelineTopology Build(PipelineSnapshot snapshot)
    {
        var nodes=new List<Node>();var edges=new List<Edge>();var sets=new DisjointSets();var keys=new Dictionary<PortKey,int>(PortKeyComparer.Instance);
        var supportHandles=snapshot.Parts.Where(part=>BomCategories.Classify(part)==BomCategory.Supports)
            .Select(part=>part.Handle).ToHashSet(StringComparer.Ordinal);
        var supportPorts=new HashSet<PortKey>();
        foreach(var connection in snapshot.Connections)
        {
            if(supportHandles.Contains(connection.FirstHandle))supportPorts.Add(new(connection.SecondHandle,connection.SecondPort));
            if(supportHandles.Contains(connection.SecondHandle))supportPorts.Add(new(connection.FirstHandle,connection.FirstPort));
        }
        int NodeAt(ModelPoint point,RouteNodeKind tag,string source,string portName="",ComponentKind component=ComponentKind.Other){int id=sets.Add();nodes.Add(new(point,tag,component,source,portName));return id;}
        void EdgeBetween(int a,int b,string source,ComponentKind kind){if(nodes[a].Point.DistanceTo(nodes[b].Point)<.01)sets.Join(a,b);else edges.Add(new(a,b,source,kind));}
        foreach(var p in snapshot.Parts)
        {
            var kind=p.Component;
            foreach(var port in p.Ports)keys[new(p.Handle,port.Name)]=NodeAt(port.Position,RouteNodeKind.Port,p.Handle,port.Name,kind);
            var ends=p.Ports.Where(q=>q.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();
            int Id(Port port)=>keys[new(p.Handle,port.Name)];
            if(ends.Length<2)continue;
            if(kind==ComponentKind.Elbow)
            {
                var corner=Corner(ends[0],ends[1]);if(corner is null)throw new InvalidDataException($"{p.Handle}: cannot dimension elbow without intersecting port axes");
                int center=NodeAt(corner.Value,RouteNodeKind.Axis,p.Handle);
                foreach(var end in ends)EdgeBetween(Id(end),center,p.Handle,kind);
                // Support ports on an elbow attach to its axis corner in the schematic.
                // Without this join their isolated graph nodes are packed away from the elbow.
                foreach(var port in p.Ports.Except(ends))
                    if(supportPorts.Contains(new(p.Handle,port.Name)))
                        sets.Join(center,Id(port));
            }
            else if(kind==ComponentKind.Tee&&ends.Length==3)
            {
                var pair=(from i in Enumerable.Range(0,3) from j in Enumerable.Range(i+1,2-i) where Dot(Unit(ends[i].Direction),Unit(ends[j].Direction))<-.999 select (i,j)).FirstOrDefault((-1,-1));
                if(pair.Item1<0)throw new InvalidDataException($"{p.Handle}: tee has no opposite run ports");
                var a=ends[pair.Item1];var b=ends[pair.Item2];var branch=ends[Enumerable.Range(0,3).Single(i=>i!=pair.Item1&&i!=pair.Item2)];
                var dir=Unit(Sub(b.Position,a.Position));
                var center=Add(a.Position,Mul(dir,Dot(Sub(branch.Position,a.Position),dir)));
                int centerId=NodeAt(center,RouteNodeKind.Axis,p.Handle);
                foreach(var end in ends)EdgeBetween(Id(end),centerId,p.Handle,kind);
            }
            else if(kind==ComponentKind.Pipe)
            {
                var a=ends[0];var b=ends[1];var dir=Unit(Sub(b.Position,a.Position));double length=a.Position.DistanceTo(b.Position);
                var stations=new List<(double T,int Id)>{(0,Id(a)),(length,Id(b))};
                foreach(var dynamicPort in p.Ports.Except(ends))
                {
                    double t=Dot(Sub(dynamicPort.Position,a.Position),dir);
                    if(t<-.01||t>length+.01)throw new InvalidDataException($"{p.Handle}: dynamic port outside pipe length");
                    var axisPoint=Add(a.Position,Mul(dir,Math.Clamp(t,0,length)));
                    int center=NodeAt(axisPoint,RouteNodeKind.TapAxis,p.Handle,dynamicPort.Name);stations.Add((Math.Clamp(t,0,length),center));
                    EdgeBetween(center,Id(dynamicPort),p.Handle,ComponentKind.TapOffset);
                }
                var sorted=stations.OrderBy(s=>s.T).ToArray();
                for(int i=1;i<sorted.Length;i++)EdgeBetween(sorted[i-1].Id,sorted[i].Id,p.Handle,kind);
            }
            else if(ends.Length>2)
            {
                var center=new ModelPoint(ends.Average(e=>e.Position.X),ends.Average(e=>e.Position.Y),ends.Average(e=>e.Position.Z));
                int centerId=NodeAt(center,RouteNodeKind.Axis,p.Handle);foreach(var end in ends)EdgeBetween(Id(end),centerId,p.Handle,kind);
            }
            else if(ends.Length==2)EdgeBetween(Id(ends[0]),Id(ends[1]),p.Handle,kind);
        }
        foreach(var reference in snapshot.BoundaryTapAxes??Array.Empty<BoundaryTapAxis>())
            if(keys.TryGetValue(new(reference.Handle,reference.Port),out int surface))
            {
                int axis=NodeAt(reference.Axis,RouteNodeKind.TapAxis,reference.Handle);
                EdgeBetween(axis,surface,reference.Handle,ComponentKind.TapOffset);
            }
        foreach(var c in snapshot.Connections)
            if(keys.TryGetValue(new(c.FirstHandle,c.FirstPort),out int a)&&keys.TryGetValue(new(c.SecondHandle,c.SecondPort),out int b))
            {
                if(supportHandles.Contains(c.FirstHandle))sets.Join(b,a);
                else sets.Join(a,b);
            }
        return new(nodes.ToArray(),edges.ToArray(),Enumerable.Range(0,nodes.Count).Select(sets.Find).ToArray());
    }
}
