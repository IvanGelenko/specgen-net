namespace IsoGenerator.Core;

// Collinear runs are assembled from Plant topology, including fitting tangents and joint thickness.
// Nodes are never merged merely because their coordinates coincide.
public static class DimensionChainPlanner
{
    static ModelPoint Sub(ModelPoint a,ModelPoint b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    static ModelPoint Add(ModelPoint a,ModelPoint b)=>new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
    static ModelPoint Mul(ModelPoint a,double k)=>new(a.X*k,a.Y*k,a.Z*k);
    static double Dot(ModelPoint a,ModelPoint b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    static ModelPoint Unit(ModelPoint a)=>Mul(a,1/Math.Sqrt(Dot(a,a)));
    static IReadOnlyList<PipeDimension> PackDimensionText(List<PipeDimension> dimensions)=>dimensions.Select(d=>
    {
        double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,l=Math.Sqrt(dx*dx+dy*dy),nx=-dy/l,ny=dx/l;
        if(PipeDimension.ReverseTextAxis(dx,dy)){nx=-nx;ny=-ny;}
        var ex=d.ExtensionDirection!.Value;var line=d.DimensionLinePoint!.Value;var mid=new SheetPoint((d.Start.X+d.End.X)/2,(d.Start.Y+d.End.Y)/2);
        double t=((line.X-mid.X)*nx+(line.Y-mid.Y)*ny)/(ex.X*nx+ex.Y*ny);
        return d with{TextPosition=new(mid.X+ex.X*t+nx*2.8,mid.Y+ex.Y*t+ny*2.8)};
    }).ToArray();
    public static IReadOnlyList<PipeDimension> Build(PipelineSnapshot snapshot,Func<ModelPoint,SheetPoint> project,int quarterTurns=0,IReadOnlyList<SymbolGlyph>? symbols=null,IReadOnlyList<Stroke>? obstacles=null,Func<ModelPoint,string,SheetPoint>? projectSource=null)
        => Build(snapshot,PipelineTopology.Build(snapshot),project,quarterTurns,symbols,obstacles,projectSource);

    internal static IReadOnlyList<PipeDimension> Build(PipelineSnapshot snapshot,PipelineTopology graph,
        Func<ModelPoint,SheetPoint> project,int quarterTurns,IReadOnlyList<SymbolGlyph>? symbols,
        IReadOnlyList<Stroke>? obstacles,Func<ModelPoint,string,SheetPoint>? projectSource)
    {
        var nodes=graph.Nodes;var edges=graph.Edges;
        var groups=new DisjointSets();foreach(var edge in edges)groups.Add();
        var incident=new Dictionary<int,List<int>>();
        for(int i=0;i<edges.Count;i++)
        {
            foreach(var n in new[]{graph.Root(edges[i].A),graph.Root(edges[i].B)})
            {
                if(!incident.TryGetValue(n,out var list))incident[n]=list=new();list.Add(i);
            }
        }
        ModelPoint Direction(PipelineTopology.Edge e)=>Unit(Sub(nodes[e.B].Point,nodes[e.A].Point));
        bool IsVerticalPipe(PipelineTopology.Edge edge)=>
            edge.Kind==ComponentKind.Pipe&&Math.Abs(Direction(edge).Z)>1-1e-10;
        foreach(var (root,list) in incident)
            for(int i=0;i<list.Count;i++)for(int j=i+1;j<list.Count;j++)
                            {
                var a=edges[list[i]];var b=edges[list[j]];
                // Small port/tangent discrepancies near a fitting are modelling tolerance, not a new dimension run.
                double tolerance=a.Kind is ComponentKind.Elbow or ComponentKind.Tee||b.Kind is ComponentKind.Elbow or ComponentKind.Tee?.5:.1;
                // A directly attached elbow tangent may be slightly rotated in Plant.
                // Measure its contribution along the vertical pipe axis.
                if(IsVerticalPipe(b)&&(a.Kind==ComponentKind.Elbow))(a,b)=(b,a);
                if(IsVerticalPipe(a)&&b.Kind==ComponentKind.Elbow&&
                    new[]{b.A,b.B}.Any(index=>graph.Root(index)==root&&nodes[index].Kind==RouteNodeKind.Port))
                    tolerance=2;
                if(Math.Abs(Dot(Direction(a),Direction(b)))>Math.Cos(tolerance*Math.PI/180))groups.Join(list[i],list[j]);
            }
        var rootNodes=Enumerable.Range(0,nodes.Count).GroupBy(graph.Root).ToDictionary(g=>g.Key,g=>g.ToArray());
        SheetPoint ProjectNode(int node)=>projectSource is null?project(nodes[node].Point):projectSource(nodes[node].Point,nodes[node].Source);
        var allProjected=Enumerable.Range(0,nodes.Count).Select(ProjectNode).ToArray();
        var sceneCenter=new SheetPoint((allProjected.Min(p=>p.X)+allProjected.Max(p=>p.X))/2,(allProjected.Min(p=>p.Y)+allProjected.Max(p=>p.Y))/2);
        var includedPorts=FlangedJointPolicy.HiddenDimensionPorts(snapshot);
        var bentPipes=snapshot.Parts.Where(part=>part.CatalogClass=="PipeBend")
            .Select(part=>part.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var returnBends=nodes.Where(node=>node.Kind==RouteNodeKind.Axis&&bentPipes.Contains(node.Source))
            .GroupBy(node=>node.Source,StringComparer.OrdinalIgnoreCase)
            .Where(group=>group.Count()==2).Select(group=>group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Return bends have no common tangent intersection. Only their transverse span
        // is suitable as a standalone linear size.
        bool IsBentPipe(PipelineTopology.Edge edge)=>edge.Kind==ComponentKind.Elbow&&bentPipes.Contains(edge.Source)&&
            (!returnBends.Contains(edge.Source)||(nodes[edge.A].Kind==RouteNodeKind.Axis&&nodes[edge.B].Kind==RouteNodeKind.Axis));
        var dimensions=new List<PipeDimension>();int chainNumber=0;
        var runEnds=new Dictionary<string,DimensionRunEnds>();
        foreach(var group in Enumerable.Range(0,edges.Count).GroupBy(groups.Find))
        {
            var indices=group.ToArray();
            if(indices.All(i=>edges[i].Kind==ComponentKind.Elbow&&!IsBentPipe(edges[i])))continue;
            var direction=Direction(edges[indices.FirstOrDefault(i=>IsVerticalPipe(edges[i]),indices[0])]);
            // Canonical ordering keeps dimensions stable when connection enumeration changes.
            if(Math.Abs(direction.X)>1e-6?direction.X<0:Math.Abs(direction.Y)>1e-6?direction.Y<0:direction.Z<0)direction=Mul(direction,-1);
            var roots=indices.SelectMany(i=>new[]{graph.Root(edges[i].A),graph.Root(edges[i].B)}).Distinct().ToArray();
            var origin=nodes[roots[0]].Point;
            var stations=new List<(double T,int Root,ModelPoint Point)>();
            foreach(var root in roots)
            {
                int degree=indices.Count(i=>graph.Root(edges[i].A)==root||graph.Root(edges[i].B)==root);
                var stationsAtRoot=rootNodes[root].Select(i=>nodes[i]).ToArray();
                bool HasPort(ComponentKind kind)=>stationsAtRoot.Any(n=>n.Kind==RouteNodeKind.Port&&n.Component==kind);
                bool HasNode(RouteNodeKind kind)=>stationsAtRoot.Any(n=>n.Kind==kind);
                bool anchor=degree!=2||HasNode(RouteNodeKind.Axis)||HasNode(RouteNodeKind.TapAxis)||
                    HasPort(ComponentKind.Valve)||HasPort(ComponentKind.Flange)||
                    (!HasPort(ComponentKind.Elbow)&&!HasPort(ComponentKind.Tee)&&HasPort(ComponentKind.Pipe));
                if(rootNodes[root].Any(n=>includedPorts.Contains(nodes[n].Source+":"+nodes[n].PortName)))anchor=false;
                bool boundaryTapSurface=(snapshot.BoundaryTapAxes??Array.Empty<BoundaryTapAxis>()).Any(r=>rootNodes[root].Any(n=>nodes[n].Source.Equals(r.Handle,StringComparison.OrdinalIgnoreCase)&&nodes[n].PortName==r.Port));
                bool tapSurface=boundaryTapSurface||rootNodes[root].Any(n=>snapshot.Parts.Any(p=>p.Handle==nodes[n].Source&&p.Ports.Any(port=>port.Name==nodes[n].PortName&&port.EndCondition=="TAP")));
                if(degree==2&&tapSurface&&!HasNode(RouteNodeKind.TapAxis))anchor=false;
                if(!anchor)continue;
                double t=Dot(Sub(nodes[root].Point,origin),direction);
                // Keep the actual graph station: almost-collinear runs may be slightly skewed in the model.
                stations.Add((t,root,nodes[root].Point));
            }
            var ordered=stations.OrderBy(s=>s.T).ToList();
            for(int i=ordered.Count-1;i>0;i--)if(Math.Abs(ordered[i].T-ordered[i-1].T)<.01)ordered.RemoveAt(i);
            // A flange/joint interval must contribute to a labelled chain dimension.
            // Merge it into its neighbour instead of silently skipping its installation length
            // or forcing a separate number over a narrow library symbol.
            for(int i=1;i<ordered.Count&&ordered.Count>2;i++)
            {
                var intervalKinds=indices.Select(index=>edges[index]).Where(edge=>
                {
                    double ta=Dot(Sub(nodes[edge.A].Point,origin),direction),tb=Dot(Sub(nodes[edge.B].Point,origin),direction);
                    return Math.Max(ta,tb)>ordered[i-1].T+.001&&Math.Min(ta,tb)<ordered[i].T-.001;
                }).Select(edge=>edge.Kind).ToArray();
                if(!intervalKinds.Contains(ComponentKind.Flange)||!intervalKinds.All(kind=>kind is ComponentKind.Flange or ComponentKind.Joint))continue;
                ordered.RemoveAt(i==1?i:i-1);i=Math.Max(0,i-2);
            }
            if(ordered.Count<2)continue;
            string chainId=$"chain-{++chainNumber:D3}";
            var a2=ProjectNode(ordered[0].Root);var b2=ProjectNode(ordered[^1].Root);double dx=b2.X-a2.X,dy=b2.Y-a2.Y,screenLength=Math.Sqrt(dx*dx+dy*dy);
            runEnds[chainId]=new(ordered[0].Root,ordered[^1].Root,a2,b2);
            if(screenLength<.1)
            {
                if(indices.All(i=>edges[i].Kind==ComponentKind.TapOffset)){runEnds.Remove(chainId);continue;}
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
                    bool tap=rootNodes[root].Any(n=>nodes[n].Kind==RouteNodeKind.TapAxis);
                    bool axis=rootNodes[root].Any(n=>nodes[n].Kind==RouteNodeKind.Axis);
                    if(!tap&&!axis)continue;
                    var at=ProjectNode(root);
                    foreach(int edgeIndex in incident[root].Where(i=>!inside.Contains(i)))
                    {
                        var e=edges[edgeIndex];int other=graph.Root(e.A)==root?graph.Root(e.B):graph.Root(e.A);
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
                                var edge=edges[index];pending.Enqueue(graph.Root(edge.A)==next?graph.Root(edge.B):graph.Root(edge.A));
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
            var runPipeSources=indices.Where(i=>edges[i].Kind==ComponentKind.Pipe).Select(i=>edges[i].Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
            double envelope=(symbols??Array.Empty<SymbolGlyph>()).Where(g=>sourceSet.Contains(g.SourceId)).SelectMany(SymbolFootprint.Corners)
                .Concat((obstacles??Array.Empty<Stroke>()).Where(s=>runPipeSources.Contains(s.SourceId)&&s.Role.StartsWith("slope-",StringComparison.Ordinal)).SelectMany(s=>s.Points))
                .Select(p=>(p.X-a2.X)*nx+(p.Y-a2.Y)*ny).DefaultIfEmpty(0).Max();
            double firstOffset=Math.Max(0,envelope)+10;
            PipeDimension Dimension(int i,int j,string role,double offset)
            {
                var start=ProjectNode(ordered[i].Root);var end=ProjectNode(ordered[j].Root);
                var linePoint=new SheetPoint(mid.X+extension.X*offset/dot,mid.Y+extension.Y*offset/dot);
                string source=nodes[rootNodes[ordered[i].Root][0]].Source;
                var localPipe=indices.Where(k=>edges[k].Kind==ComponentKind.Pipe).Select(k=>
                {
                    var edge=edges[k];double ta=Dot(Sub(nodes[edge.A].Point,origin),direction),tb=Dot(Sub(nodes[edge.B].Point,origin),direction);
                    return (edge.Source,Low:Math.Min(ta,tb),High:Math.Max(ta,tb));
                }).FirstOrDefault(e=>e.High>ordered[i].T+.001&&e.Low<ordered[j].T-.001);
                if(localPipe.Source is not null)source=localPipe.Source;
                var intervalKinds=indices.Select(k=>edges[k]).Where(edge=>{double ta=Dot(Sub(nodes[edge.A].Point,origin),direction),tb=Dot(Sub(nodes[edge.B].Point,origin),direction);return Math.Max(ta,tb)>ordered[i].T+.001&&Math.Min(ta,tb)<ordered[j].T-.001;}).Select(e=>e.Kind).ToArray();
                bool reference=intervalKinds.Contains(ComponentKind.Valve)&&(role=="overall"||!intervalKinds.Contains(ComponentKind.Pipe));
                return new(source,start,end,ordered[j].T-ordered[i].T,role,chainId,linePoint,extension,ordered[i].Point,ordered[j].Point,sources,DisplayMillimeters:Math.Ceiling(ordered[j].T-ordered[i].T-1e-8),ReferenceOnly:reference);
            }
            // For a single interval, the overall is the dimension; never duplicate its label.
            if(ordered.Count>2)for(int i=1;i<ordered.Count;i++)
            {
                var interval=indices.Select(index=>edges[index]).Where(edge=>
                {
                    double ta=Dot(Sub(nodes[edge.A].Point,origin),direction),tb=Dot(Sub(nodes[edge.B].Point,origin),direction);
                    return Math.Max(ta,tb)>ordered[i-1].T+.001&&Math.Min(ta,tb)<ordered[i].T-.001;
                }).ToArray();
                // Flange-only intervals still have a real installation length. Omitting
                // them leaves a hole between pipe dimensions even when hardware is hidden in BOM.
                if(interval.Length>0&&interval.All(edge=>(edge.Kind is ComponentKind.Joint or ComponentKind.Tee or ComponentKind.Elbow)&&
                    !IsBentPipe(edge)))continue;
                dimensions.Add(Dimension(i-1,i,"chain",firstOffset));
            }
            dimensions.Add(Dimension(0,ordered.Count-1,"overall",ordered.Count>2?firstOffset+7:firstOffset));
        }
        return PackDimensionText(DimensionCorners.Align(dimensions,runEnds,new[]{new ModelPoint(1,0,0),new ModelPoint(0,1,0),new ModelPoint(0,0,1)}.Select(v=>IsometricProjection.Project(v,quarterTurns:quarterTurns)).Select(v=>{double l=Math.Sqrt(v.X*v.X+v.Y*v.Y);return new SheetPoint(v.X/l,v.Y/l);}).ToArray(),dimensions.Where(d=>(d.SourceIds??Array.Empty<string>()).Any(h=>snapshot.Parts.Any(p=>p.Handle==h&&p.Component==ComponentKind.Valve))).Select(d=>d.ChainId).ToHashSet()).ToList());
    }
}
