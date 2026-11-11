namespace IsoGenerator.Core;

// Collinear runs are assembled from Plant topology, including fitting tangents and joint thickness.
// Nodes are never merged merely because their coordinates coincide.
public static class DimensionChainPlanner
{
    internal sealed record Node(ModelPoint Point,string Tag,string Source,string PortName = "");
    internal sealed record Edge(int A,int B,string Source,string Kind);
    internal sealed class Sets
    {
        readonly List<int> parent=new();
        public int Add(){int i=parent.Count;parent.Add(i);return i;}
        public int Find(int i){int root=i;while(parent[root]!=root)root=parent[root];while(parent[i]!=i){int next=parent[i];parent[i]=root;i=next;}return root;}
        public void Join(int a,int b){a=Find(a);b=Find(b);if(a!=b)parent[b]=a;}
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
    static IReadOnlyList<PipeDimension> PackDimensionText(List<PipeDimension> dimensions)=>dimensions.Select(d=>
    {
        double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,l=Math.Sqrt(dx*dx+dy*dy),nx=-dy/l,ny=dx/l;
        if(dx<-.0001||(Math.Abs(dx)<.0001&&dy<0)){nx=-nx;ny=-ny;}
        var ex=d.ExtensionDirection!.Value;var line=d.DimensionLinePoint!.Value;var mid=new SheetPoint((d.Start.X+d.End.X)/2,(d.Start.Y+d.End.Y)/2);
        double t=((line.X-mid.X)*nx+(line.Y-mid.Y)*ny)/(ex.X*nx+ex.Y*ny);
        return d with{TextPosition=new(mid.X+ex.X*t+nx*2.8,mid.Y+ex.Y*t+ny*2.8)};
    }).ToArray();
    internal sealed record Graph(List<Node> Nodes,List<Edge> Edges,Sets Sets);
    internal static Graph BuildGraph(PipelineSnapshot snapshot)
    {
        var nodes=new List<Node>();var edges=new List<Edge>();var sets=new Sets();var keys=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        int NodeAt(ModelPoint point,string tag,string source,string portName=""){int id=sets.Add();nodes.Add(new(point,tag,source,portName));return id;}
        void EdgeBetween(int a,int b,string source,string kind){if(nodes[a].Point.DistanceTo(nodes[b].Point)<.01)sets.Join(a,b);else edges.Add(new(a,b,source,kind));}
        foreach(var p in snapshot.Parts)
        {
            string kind=p.Properties.GetValueOrDefault("PnPClassName",p.Kind=="AcPpDb3dConnector"?"Joint":"Unknown");
            foreach(var port in p.Ports)keys[p.Handle+":"+port.Name]=NodeAt(port.Position,kind+"Port",p.Handle,port.Name);
            var ends=p.Ports.Where(q=>q.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();
            int Id(Port port)=>keys[p.Handle+":"+port.Name];
            if(ends.Length<2)continue;
            if(kind=="Elbow")
            {
                var corner=Corner(ends[0],ends[1]);if(corner is null)throw new InvalidDataException($"{p.Handle}: cannot dimension elbow without intersecting port axes");
                int center=NodeAt(corner.Value,"Axis",p.Handle);
                foreach(var end in ends)EdgeBetween(Id(end),center,p.Handle,kind);
            }
            else if(kind=="Tee"&&ends.Length==3)
            {
                var pair=(from i in Enumerable.Range(0,3) from j in Enumerable.Range(i+1,2-i) where Dot(Unit(ends[i].Direction),Unit(ends[j].Direction))<-.999 select (i,j)).FirstOrDefault((-1,-1));
                if(pair.Item1<0)throw new InvalidDataException($"{p.Handle}: tee has no opposite run ports");
                var a=ends[pair.Item1];var b=ends[pair.Item2];var branch=ends[Enumerable.Range(0,3).Single(i=>i!=pair.Item1&&i!=pair.Item2)];
                var dir=Unit(Sub(b.Position,a.Position));
                var center=Add(a.Position,Mul(dir,Dot(Sub(branch.Position,a.Position),dir)));
                int centerId=NodeAt(center,"Axis",p.Handle);
                foreach(var end in ends)EdgeBetween(Id(end),centerId,p.Handle,kind);
            }
            else if(kind=="Pipe")
            {
                var a=ends[0];var b=ends[1];var dir=Unit(Sub(b.Position,a.Position));double length=a.Position.DistanceTo(b.Position);
                var stations=new List<(double T,int Id)>{(0,Id(a)),(length,Id(b))};
                foreach(var dynamicPort in p.Ports.Except(ends))
                {
                    double t=Dot(Sub(dynamicPort.Position,a.Position),dir);
                    if(t<-.01||t>length+.01)throw new InvalidDataException($"{p.Handle}: dynamic port outside pipe length");
                    var axisPoint=Add(a.Position,Mul(dir,Math.Clamp(t,0,length)));
                    int center=NodeAt(axisPoint,"TapAxis",p.Handle,dynamicPort.Name);stations.Add((Math.Clamp(t,0,length),center));
                    EdgeBetween(center,Id(dynamicPort),p.Handle,"TapOffset");
                }
                var sorted=stations.OrderBy(s=>s.T).ToArray();
                for(int i=1;i<sorted.Length;i++)EdgeBetween(sorted[i-1].Id,sorted[i].Id,p.Handle,kind);
            }
            else if(ends.Length>2)
            {
                var center=new ModelPoint(ends.Average(e=>e.Position.X),ends.Average(e=>e.Position.Y),ends.Average(e=>e.Position.Z));
                int centerId=NodeAt(center,"Axis",p.Handle);foreach(var end in ends)EdgeBetween(Id(end),centerId,p.Handle,kind);
            }
            else if(ends.Length==2)EdgeBetween(Id(ends[0]),Id(ends[1]),p.Handle,kind);
        }
        foreach(var c in snapshot.Connections)
            if(keys.TryGetValue(c.FirstHandle+":"+c.FirstPort,out int a)&&keys.TryGetValue(c.SecondHandle+":"+c.SecondPort,out int b))sets.Join(a,b);
        return new(nodes,edges,sets);
    }
    public static IReadOnlyList<PipeDimension> Build(PipelineSnapshot snapshot,Func<ModelPoint,SheetPoint> project,int quarterTurns=0,IReadOnlyList<SymbolGlyph>? symbols=null,IReadOnlyList<Stroke>? obstacles=null,Func<ModelPoint,string,SheetPoint>? projectSource=null)
    {
        var graph=BuildGraph(snapshot);var nodes=graph.Nodes;var edges=graph.Edges;var sets=graph.Sets;
        var groups=new Sets();foreach(var edge in edges)groups.Add();
        var incident=new Dictionary<int,List<int>>();
        for(int i=0;i<edges.Count;i++)
        {
            foreach(var n in new[]{sets.Find(edges[i].A),sets.Find(edges[i].B)})
            {
                if(!incident.TryGetValue(n,out var list))incident[n]=list=new();list.Add(i);
            }
        }
        ModelPoint Direction(Edge e)=>Unit(Sub(nodes[e.B].Point,nodes[e.A].Point));
        foreach(var list in incident.Values)
            for(int i=0;i<list.Count;i++)for(int j=i+1;j<list.Count;j++)
                            {
                var a=edges[list[i]];var b=edges[list[j]];
                // Small port/tangent discrepancies near a fitting are modelling tolerance, not a new dimension run.
                double tolerance=a.Kind is "Elbow" or "Tee"||b.Kind is "Elbow" or "Tee"?.5:.1;
                if(Math.Abs(Dot(Direction(a),Direction(b)))>Math.Cos(tolerance*Math.PI/180))groups.Join(list[i],list[j]);
            }
        var rootNodes=Enumerable.Range(0,nodes.Count).GroupBy(sets.Find).ToDictionary(g=>g.Key,g=>g.ToArray());
        SheetPoint ProjectNode(int node)=>projectSource is null?project(nodes[node].Point):projectSource(nodes[node].Point,nodes[node].Source);
        var allProjected=Enumerable.Range(0,nodes.Count).Select(ProjectNode).ToArray();
        var sceneCenter=new SheetPoint((allProjected.Min(p=>p.X)+allProjected.Max(p=>p.X))/2,(allProjected.Min(p=>p.Y)+allProjected.Max(p=>p.Y))/2);
        var includedPorts=FlangedJointPolicy.HiddenDimensionPorts(snapshot);
        var dimensions=new List<PipeDimension>();int chainNumber=0;
        var runEnds=new Dictionary<string,DimensionRunEnds>();
        foreach(var group in Enumerable.Range(0,edges.Count).GroupBy(groups.Find))
        {
            var indices=group.ToArray();if(indices.All(i=>edges[i].Kind=="Elbow"))continue;
            var direction=Direction(edges[indices[0]]);
            // Canonical ordering keeps dimensions stable when connection enumeration changes.
            if(Math.Abs(direction.X)>1e-6?direction.X<0:Math.Abs(direction.Y)>1e-6?direction.Y<0:direction.Z<0)direction=Mul(direction,-1);
            var roots=indices.SelectMany(i=>new[]{sets.Find(edges[i].A),sets.Find(edges[i].B)}).Distinct().ToArray();
            var origin=nodes[roots[0]].Point;
            var stations=new List<(double T,int Root,ModelPoint Point)>();
            foreach(var root in roots)
            {
                int degree=indices.Count(i=>sets.Find(edges[i].A)==root||sets.Find(edges[i].B)==root);
                var tags=rootNodes[root].Select(i=>nodes[i].Tag).ToHashSet();
                bool anchor=degree!=2||tags.Contains("Axis")||tags.Contains("TapAxis")||tags.Contains("ValvePort")||tags.Contains("FlangePort")||(!tags.Contains("ElbowPort")&&!tags.Contains("TeePort")&&tags.Contains("PipePort"));
                if(rootNodes[root].Any(n=>includedPorts.Contains(nodes[n].Source+":"+nodes[n].PortName)))anchor=false;
                bool tapSurface=rootNodes[root].Any(n=>snapshot.Parts.Any(p=>p.Handle==nodes[n].Source&&p.Ports.Any(port=>port.Name==nodes[n].PortName&&port.EndCondition=="TAP")));
                if(degree==2&&tapSurface&&!tags.Contains("TapAxis"))anchor=false;
                if(!anchor)continue;
                double t=Dot(Sub(nodes[root].Point,origin),direction);
                // Keep the actual graph station: almost-collinear runs may be slightly skewed in the model.
                stations.Add((t,root,nodes[root].Point));
            }
            var ordered=stations.OrderBy(s=>s.T).ToList();
            for(int i=ordered.Count-1;i>0;i--)if(Math.Abs(ordered[i].T-ordered[i-1].T)<.01)ordered.RemoveAt(i);
            if(ordered.Count<2)continue;
            string chainId=$"chain-{++chainNumber:D3}";
            var a2=ProjectNode(ordered[0].Root);var b2=ProjectNode(ordered[^1].Root);double dx=b2.X-a2.X,dy=b2.Y-a2.Y,screenLength=Math.Sqrt(dx*dx+dy*dy);
            runEnds[chainId]=new(ordered[0].Root,ordered[^1].Root,a2,b2);
            if(screenLength<.1)
            {
                if(indices.All(i=>edges[i].Kind=="TapOffset")){runEnds.Remove(chainId);continue;}
                throw new InvalidDataException($"{chainId}: run projects to an unreadable point ({string.Join(",",indices.Select(i=>edges[i].Source+":"+edges[i].Kind))})");
            }
            double nx=-dy/screenLength,ny=dx/screenLength;
            var mid=new SheetPoint((a2.X+b2.X)/2,(a2.Y+b2.Y)/2);
            double SideScore(double sx,double sy)
            {
                double score=-.01*((mid.X-sceneCenter.X)*sx+(mid.Y-sceneCenter.Y)*sy);
                var inside=indices.ToHashSet();
                foreach(int root in roots)
                {
                    bool tap=rootNodes[root].Any(n=>nodes[n].Tag=="TapAxis");
                    bool axis=rootNodes[root].Any(n=>nodes[n].Tag=="Axis");
                    if(!tap&&!axis)continue;
                    var at=ProjectNode(root);
                    foreach(int edgeIndex in incident[root].Where(i=>!inside.Contains(i)))
                    {
                        var e=edges[edgeIndex];int other=sets.Find(e.A)==root?sets.Find(e.B):sets.Find(e.A);
                        var to=ProjectNode(other);
                        // Follow hidden zero-length offsets to the first visible branch edge.
                        var pending=new Queue<int>();pending.Enqueue(other);var visited=new HashSet<int>{root};
                        while(pending.Count>0)
                        {
                            int next=pending.Dequeue();if(!visited.Add(next))continue;
                            var point=ProjectNode(next);
                            if(Math.Sqrt(Math.Pow(point.X-at.X,2)+Math.Pow(point.Y-at.Y,2))>.1)
                            {
                                if((point.X-at.X)*sx+(point.Y-at.Y)*sy>.1)score+=tap?1000000:100;
                                continue;
                            }
                            if(incident.TryGetValue(next,out var adjacent))foreach(int index in adjacent.Where(i=>!inside.Contains(i)))
                            {
                                var edge=edges[index];pending.Enqueue(sets.Find(edge.A)==next?sets.Find(edge.B):sets.Find(edge.A));
                            }
                        }
                    }
                }
                bool Cross(SheetPoint a,SheetPoint b,SheetPoint c,SheetPoint d)
                {
                    double cross(double x,double y,double u,double v)=>x*v-y*u;
                    double den=cross(b.X-a.X,b.Y-a.Y,d.X-c.X,d.Y-c.Y);if(Math.Abs(den)<1e-8)return false;
                    double t=cross(c.X-a.X,c.Y-a.Y,d.X-c.X,d.Y-c.Y)/den;
                    double u=cross(c.X-a.X,c.Y-a.Y,b.X-a.X,b.Y-a.Y)/den;
                    return t>.001&&t<.999&&u>.001&&u<.999;
                }
                foreach(double offset in new[]{10.0,20.0})
                {
                    var start=new SheetPoint(a2.X+sx*offset,a2.Y+sy*offset);var end=new SheetPoint(b2.X+sx*offset,b2.Y+sy*offset);
                    foreach(int i in Enumerable.Range(0,edges.Count).Where(i=>!inside.Contains(i)))
                        if(Cross(start,end,ProjectNode(edges[i].A),ProjectNode(edges[i].B)))score+=1000;
                }
                return score;
            }
            if(SideScore(-nx,-ny)<SideScore(nx,ny)){nx=-nx;ny=-ny;}
            var axisCandidates=new[]{new ModelPoint(1,0,0),new ModelPoint(0,1,0),new ModelPoint(0,0,1)}
                .Select(v=>IsometricProjection.Project(v,quarterTurns:quarterTurns))
                .Select(v=>{double l=Math.Sqrt(v.X*v.X+v.Y*v.Y);return new SheetPoint(v.X/l,v.Y/l);})
                .OrderByDescending(v=>Math.Abs(v.X*nx+v.Y*ny)).ToArray();
            var extension=axisCandidates[0];
            // For a non-vertical run, use paper Z as the extension direction:
            // local dimension X follows the pipe, local dimension Z stays upright.
            if(Math.Abs(ny)>.2)
            {if(ny<0){nx=-nx;ny=-ny;}extension=new(0,1);}
            else if(extension.X*nx+extension.Y*ny<0)extension=new(-extension.X,-extension.Y);
            double dot=extension.X*nx+extension.Y*ny;
            var sources=indices.Select(i=>edges[i].Source).Distinct().ToArray();
            var sourceSet=sources.ToHashSet(StringComparer.OrdinalIgnoreCase);
            // A tap's source handle also owns the entire main pipe. Only the actual run and its
            // component/slope contours may determine clearance, not unrelated strokes of that handle.
            var runPipeSources=indices.Where(i=>edges[i].Kind=="Pipe").Select(i=>edges[i].Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
            double envelope=(symbols??Array.Empty<SymbolGlyph>()).Where(g=>sourceSet.Contains(g.SourceId)).SelectMany(SymbolFootprint.Corners)
                .Concat((obstacles??Array.Empty<Stroke>()).Where(s=>runPipeSources.Contains(s.SourceId)&&s.Role.StartsWith("slope-",StringComparison.Ordinal)).SelectMany(s=>s.Points))
                .Select(p=>(p.X-a2.X)*nx+(p.Y-a2.Y)*ny).DefaultIfEmpty(0).Max();
            double firstOffset=Math.Max(0,envelope)+10;
            PipeDimension Dimension(int i,int j,string role,double offset)
            {
                var start=ProjectNode(ordered[i].Root);var end=ProjectNode(ordered[j].Root);
                var linePoint=new SheetPoint(mid.X+extension.X*offset/dot,mid.Y+extension.Y*offset/dot);
                string source=nodes[rootNodes[ordered[i].Root][0]].Source;
                var localPipe=indices.Where(k=>edges[k].Kind=="Pipe").Select(k=>
                {
                    var edge=edges[k];double ta=Dot(Sub(nodes[edge.A].Point,origin),direction),tb=Dot(Sub(nodes[edge.B].Point,origin),direction);
                    return (edge.Source,Low:Math.Min(ta,tb),High:Math.Max(ta,tb));
                }).FirstOrDefault(e=>e.High>ordered[i].T+.001&&e.Low<ordered[j].T-.001);
                if(localPipe.Source is not null)source=localPipe.Source;
                return new(source,start,end,ordered[j].T-ordered[i].T,role,chainId,linePoint,extension,ordered[i].Point,ordered[j].Point,sources,DisplayMillimeters:Math.Ceiling(ordered[j].T-ordered[i].T-1e-8));
            }
            // For a single interval, the overall is the dimension; never duplicate its label.
            if(ordered.Count>2)for(int i=1;i<ordered.Count;i++)
            {
                var kinds=indices.Select(index=>edges[index]).Where(edge=>
                {
                    double ta=Dot(Sub(nodes[edge.A].Point,origin),direction),tb=Dot(Sub(nodes[edge.B].Point,origin),direction);
                    return Math.Max(ta,tb)>ordered[i-1].T+.001&&Math.Min(ta,tb)<ordered[i].T-.001;
                }).Select(edge=>edge.Kind).ToArray();
                if(kinds.Length>0&&kinds.All(kind=>kind is "Flange" or "Joint" or "Tee" or "Elbow"))continue;
                dimensions.Add(Dimension(i-1,i,"chain",firstOffset));
            }
            dimensions.Add(Dimension(0,ordered.Count-1,"overall",ordered.Count>2?firstOffset+7:firstOffset));
        }
        return PackDimensionText(DimensionCorners.Align(dimensions,runEnds,new[]{new ModelPoint(1,0,0),new ModelPoint(0,1,0),new ModelPoint(0,0,1)}.Select(v=>IsometricProjection.Project(v,quarterTurns:quarterTurns)).Select(v=>{double l=Math.Sqrt(v.X*v.X+v.Y*v.Y);return new SheetPoint(v.X/l,v.Y/l);}).ToArray(),dimensions.Where(d=>(d.SourceIds??Array.Empty<string>()).Any(h=>snapshot.Parts.Any(p=>p.Handle==h&&p.Properties.GetValueOrDefault("PnPClassName")=="Valve"))).Select(d=>d.ChainId).ToHashSet()).ToList());
    }
}
