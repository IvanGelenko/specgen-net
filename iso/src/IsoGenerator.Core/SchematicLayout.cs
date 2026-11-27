namespace IsoGenerator.Core;

// Schematic positions are independent of real model coordinates. Short edges are enlarged
// along their projected direction; shared topological nodes retain one drawing position.
public sealed class SchematicLayout
{
    readonly PipelineTopology graph;
    internal PipelineTopology Topology=>graph;
    readonly Dictionary<int,SheetPoint> positions;
    readonly Dictionary<(long,long,long),List<int>> index=new();
    readonly SheetPoint shift;
    static (long,long,long) Key(ModelPoint p)=>( (long)Math.Round(p.X*100), (long)Math.Round(p.Y*100),(long)Math.Round(p.Z*100));
    internal static string PipeEdgeKey(string source,ModelPoint a,ModelPoint b)=>source+"|"+string.Join("|",new[]{FormattableString.Invariant($"{a.X:R},{a.Y:R},{a.Z:R}"),FormattableString.Invariant($"{b.X:R},{b.Y:R},{b.Z:R}")}.OrderBy(p=>p,StringComparer.Ordinal));
    public double BaseScale {get;}
    public int EnlargedEdges {get;}
    public int ClosedLoops {get;}
    internal IReadOnlyList<string> NonGridPipes {get;}
    public SchematicLayout(PipelineSnapshot snapshot,int quarterTurns=0,int compactLevel=0,IReadOnlyDictionary<string,double>? pipeExtensions=null,IsoSymbolMap? symbolMap=null,double paperMargin=0,double? fixedScale=null)
        :this(snapshot,PipelineTopology.Build(snapshot),quarterTurns,compactLevel,pipeExtensions,
            symbolMap is null?null:symbolMap.ResolveGeometry,paperMargin,fixedScale)
    {}

    internal SchematicLayout(PipelineSnapshot snapshot,PipelineTopology topology,int quarterTurns,int compactLevel,
        IReadOnlyDictionary<string,double>? pipeExtensions,Func<Part,(IsoBlockDefinition Definition,double Scale)?>? symbolGeometry,
        double paperMargin,double? fixedScale=null,PlanningExecution? execution=null)
    {
        graph=topology;
        var hiddenGaskets=FlangedJointPolicy.Find(snapshot).Select(j=>j.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // A flanged gasket keeps a fixed 1 mm paper gap; its real 4.5 mm stays in dimension calculations.
        double pipeGridStep=IsoAdvancedSettings.Current.PipeGridStep;
        double RoundPipeLength(double length)=>Math.Ceiling((length-1e-8)/pipeGridStep)*pipeGridStep;
        var parts=snapshot.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
        double SymbolLength(PipelineTopology.Edge edge)
        {
            if(edge.Kind==ComponentKind.TapOffset)return 0;
            if(edge.Kind==ComponentKind.Joint)return 1; // Requested visible flange-joint gap, not a symbol size.
            if(parts.TryGetValue(edge.Source,out var part)&&symbolGeometry?.Invoke(part) is {} geometry)
            {
                var (definition,scale)=geometry;
                var ports=definition.Ports.Where(p=>p.Key.StartsWith("Port",StringComparison.OrdinalIgnoreCase)).OrderBy(p=>int.TryParse(p.Key[4..],out int number)?number:int.MaxValue).Select(p=>p.Value).ToArray();
                if(edge.Kind is ComponentKind.Elbow or ComponentKind.Tee)
                {
                    var modelPorts=part.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();
                    if(edge.Kind==ComponentKind.Tee&&modelPorts.Length==3)
                    {
                        var pair=(from i in Enumerable.Range(0,3) from j in Enumerable.Range(i+1,2-i) let u=modelPorts[i].Direction let v=modelPorts[j].Direction where u.X*v.X+u.Y*v.Y+u.Z*v.Z<-.99 select (i,j)).FirstOrDefault((-1,-1));
                        if(pair.Item1>=0)modelPorts=new[]{modelPorts[pair.Item1],modelPorts[pair.Item2],modelPorts[Enumerable.Range(0,3).Single(i=>i!=pair.Item1&&i!=pair.Item2)]};
                    }
                    string name=graph.Nodes[edge.A].PortName.Length>0?graph.Nodes[edge.A].PortName:graph.Nodes[edge.B].PortName;
                    int index=Array.FindIndex(modelPorts,p=>p.Name==name);
                    var localCenter=definition.Ports.GetValueOrDefault("CenterPoint",default);
                    if(index>=0&&index<ports.Length)
                    {
                        var port=ports[index];
                        double len;
                        if(ports.Length>=2&&Math.Abs(ports[0].X-ports[^1].X)<=1e-4)
                        {
                            double apexX=Math.Abs(definition.Maximum.X-ports[0].X)>=Math.Abs(definition.Minimum.X-ports[0].X)?definition.Maximum.X:definition.Minimum.X;
                            len=Math.Abs(port.X-apexX);
                        }
                        else len=Math.Sqrt(Math.Pow(port.X-localCenter.X,2)+Math.Pow(port.Y-localCenter.Y,2));
                        return len*scale;
                    }
                }
                var a=ports.Length>0?ports[0]:new SheetPoint(definition.Minimum.X,0);
                var b=ports.Length>1?ports[1]:new SheetPoint(definition.Maximum.X,0);
                double span=Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Y-a.Y,2));
                return (span>1e-8?span:definition.Maximum.X-definition.Minimum.X)*scale;
            }
            return 10; // No library: same explicit missing-symbol policy, never a fitting minimum.

        }
        var incident=new Dictionary<int,List<(int Other,int Edge)>>();
        for(int i=0;i<graph.Edges.Count;i++)
        {
            var edge=graph.Edges[i];int a=graph.Root(edge.A),b=graph.Root(edge.B);
            if(a==b)continue;
            if(!incident.ContainsKey(a))incident[a]=new();if(!incident.ContainsKey(b))incident[b]=new();
            incident[a].Add((b,i));incident[b].Add((a,i));
        }
        // Component geometry remains at the user-selected library scale.
        var cycleSolver=new CycleLengthSolver(graph,quarterTurns);ClosedLoops=cycleSolver.Count;
        var pipeTotals=graph.Edges.Where(e=>e.Kind==ComponentKind.Pipe).GroupBy(e=>e.Source,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.Sum(e=>graph.Nodes[e.A].Point.DistanceTo(graph.Nodes[e.B].Point)),StringComparer.OrdinalIgnoreCase);
        var upperBounds=graph.Edges.Select(edge=>edge.Kind==ComponentKind.Pipe&&graph.CappedVentDrainPipes.Contains(edge.Source)
            ?VentDrainPipePolicy.MaxPaperLength*graph.Nodes[edge.A].Point.DistanceTo(graph.Nodes[edge.B].Point)/pipeTotals[edge.Source]
            :double.PositiveInfinity).ToArray();
        double minimumPipeLength=execution?.MinimumPipeLength??10;
        var dipLengths=new Dictionary<int,double>();
        var dipPairs=new List<(int Left,int Right,double LeftRise,double RightRise,double MinRise,double MaxRise)>();
        for(int i=0;i<graph.Edges.Count;i++)
        {
            var cross=graph.Edges[i];
            var first=graph.Nodes[cross.A];var second=graph.Nodes[cross.B];
            if(cross.Kind!=ComponentKind.Elbow||first.Kind!=RouteNodeKind.Axis||second.Kind!=RouteNodeKind.Axis||
               Math.Abs(first.Point.Z-second.Point.Z)>.01||parts[cross.Source].CatalogClass!="PipeBend")continue;
            var left=DipLeg(cross.A);var right=DipLeg(cross.B);
            if(left is not {} a||right is not {} b||a.Edge==b.Edge||
               Math.Abs(a.Top.Z-b.Top.Z)>.01||Math.Abs(a.Bottom.Z-b.Bottom.Z)>.01||
               Math.Abs(a.OutletElevation-b.OutletElevation)>.01||
               cycleSolver.UsesEdge(a.Edge)||cycleSolver.UsesEdge(b.Edge))continue;
            double modelLength=((a.Top.Z-a.Bottom.Z)+(b.Top.Z-b.Bottom.Z))/2;
            double paperLength=SymbolLength(cross)*modelLength/first.Point.DistanceTo(second.Point);
            double limit=Math.Max(RoundPipeLength(minimumPipeLength),Math.Floor(paperLength/pipeGridStep)*pipeGridStep);
            double minRise=Math.Max(Math.Min(minimumPipeLength,upperBounds[a.Edge])+a.SymbolRise,
                Math.Min(minimumPipeLength,upperBounds[b.Edge])+b.SymbolRise);
            double hardMaxRise=Math.Min(upperBounds[a.Edge]+a.SymbolRise,upperBounds[b.Edge]+b.SymbolRise);
            if(hardMaxRise<minRise)continue;

            // The compact limit follows model proportions. Allow only the extra length
            // required for level outlets when the shorter leg is already at its minimum.
            double maxRise=Math.Min(hardMaxRise,Math.Max(minRise,limit+Math.Min(a.SymbolRise,b.SymbolRise)));
            dipLengths[a.Edge]=dipLengths[b.Edge]=modelLength;
            upperBounds[a.Edge]=maxRise-a.SymbolRise;
            upperBounds[b.Edge]=maxRise-b.SymbolRise;
            dipPairs.Add((a.Edge,b.Edge,a.SymbolRise,b.SymbolRise,minRise,maxRise));

            (int Edge,ModelPoint Bottom,ModelPoint Top,double OutletElevation,double SymbolRise)? DipLeg(int corner)
            {
                var tangent=incident[graph.Root(corner)].Where(link=>link.Edge!=i&&
                    graph.Edges[link.Edge].Source==cross.Source).ToArray();
                if(tangent.Length!=1||!incident.TryGetValue(tangent[0].Other,out var links)||links.Count!=2)return null;
                var next=links.Single(link=>link.Edge!=tangent[0].Edge);
                if(graph.Edges[next.Edge].Kind!=ComponentKind.Pipe)return null;
                var bottom=graph.Nodes[tangent[0].Other].Point;var top=graph.Nodes[next.Other].Point;
                var lowCorner=graph.Nodes[corner].Point;
                if(top.Z<=bottom.Z+.01||bottom.Z<=lowCorner.Z||
                   Math.Abs(bottom.X-lowCorner.X)>.01||Math.Abs(bottom.Y-lowCorner.Y)>.01||
                   Math.Abs(top.X-bottom.X)>.01||Math.Abs(top.Y-bottom.Y)>.01)return null;
                foreach(var rise in incident[next.Other].Where(link=>graph.Edges[link.Edge].Kind==ComponentKind.Elbow))
                {
                    var outletCorner=graph.Nodes[rise.Other].Point;
                    if(Math.Abs(outletCorner.X-top.X)>.01||Math.Abs(outletCorner.Y-top.Y)>.01)continue;
                    if(incident[rise.Other].Any(link=>link.Edge!=rise.Edge&&
                       graph.Edges[link.Edge].Source==graph.Edges[rise.Edge].Source&&
                       Math.Abs(graph.Nodes[link.Other].Point.Z-outletCorner.Z)<.01))
                        return (next.Edge,bottom,top,outletCorner.Z,SymbolLength(graph.Edges[tangent[0].Edge])+
                            Math.Sign(outletCorner.Z-top.Z)*SymbolLength(graph.Edges[rise.Edge]));
                }
                return null;
            }
        }
        var lowerBounds=graph.Edges.Select((edge,i)=>edge.Kind==ComponentKind.Pipe
            ?Math.Min(minimumPipeLength,upperBounds[i]):0).ToArray();
        var pipeReferences=graph.Edges.Where(e=>e.Kind==ComponentKind.Pipe).Select(e=>graph.Nodes[e.A].Point.DistanceTo(graph.Nodes[e.B].Point)).Where(l=>l>.001).OrderBy(l=>l).ToArray();
        double referencePipeLength=pipeReferences.Length>0?pipeReferences[pipeReferences.Length/2]:1;
        double PaperDemand(double length)=>compactLevel is 1 or 2 or 4 or 5?Math.Sqrt(length*referencePipeLength):length;
        double availableWidth=SheetComposition.Paper.Width-2*IsoAdvancedSettings.Current.DimensionClearance-paperMargin;
        var projected=graph.Nodes.Select(node=>IsometricProjection.Project(node.Point,quarterTurns:quarterTurns)).ToArray();
        var demands=new double[graph.Edges.Count];
        var adjustments=new double[graph.Edges.Count];
        var isPipe=new bool[graph.Edges.Count];
        for(int i=0;i<graph.Edges.Count;i++)
        {
            var edge=graph.Edges[i];
            isPipe[i]=edge.Kind==ComponentKind.Pipe;
            if(!isPipe[i]){adjustments[i]=hiddenGaskets.Contains(edge.Source)?1:SymbolLength(edge);continue;}
            var p=projected[graph.Root(edge.A)];var q=projected[graph.Root(edge.B)];
            double dx=q.X-p.X,dy=q.Y-p.Y,length=Math.Sqrt(dx*dx+dy*dy);
            if(dipLengths.TryGetValue(i,out double dipLength)){demands[i]=PaperDemand(dipLength);continue;}
            demands[i]=PaperDemand(length);
            if(Math.Abs(dx)>Math.Abs(dy))demands[i]*=compactLevel==0?3:2;
            if(pipeExtensions is null||pipeExtensions.Count==0)continue;
            var a=graph.Nodes[edge.A].Point;var b=graph.Nodes[edge.B].Point;
            double fraction=a.DistanceTo(b)/Math.Max(.001,pipeTotals[edge.Source]);
            adjustments[i]=pipeExtensions.GetValueOrDefault(edge.Source)*fraction+
                pipeExtensions.GetValueOrDefault(PipeEdgeKey(edge.Source,a,b));
        }
        Dictionary<int,SheetPoint> Arrange(double scale,IReadOnlyDictionary<int,double>? pipeLengths=null)
        {
            execution?.Report("Согласование длин участков");
            var desired=new double[graph.Edges.Count];
            for(int i=0;i<graph.Edges.Count;i++)
            {
                desired[i]=demands[i]*scale+adjustments[i];
                if(pipeLengths is not null&&!dipLengths.ContainsKey(i)&&pipeLengths.TryGetValue(i,out double extended))desired[i]=extended;
                if(isPipe[i])desired[i]=Math.Clamp(RoundPipeLength(desired[i]),lowerBounds[i],upperBounds[i]);
            }
            desired=cycleSolver.Constrain(desired,pipeGridStep,lowerBounds,execution);
            // Match the full vertical runs after loop/grid adjustment. The two legs are
            // outside the loops, and the rigid symbols may require an off-grid pipe length.
            foreach(var dip in dipPairs)
            {
                double preferred=RoundPipeLength(demands[dip.Left]*scale)+Math.Max(dip.LeftRise,dip.RightRise);
                double rise=Math.Clamp(preferred,dip.MinRise,dip.MaxRise);
                desired[dip.Left]=rise-dip.LeftRise;
                desired[dip.Right]=rise-dip.RightRise;
            }
            var result=new Dictionary<int,SheetPoint>();var components=new List<List<int>>();
            foreach(int root in incident.Keys)
            {
                if(result.ContainsKey(root))continue;
                result[root]=new(0,0);var queue=new Queue<(int Node,int Parent)>();queue.Enqueue((root,-1));
                var component=new List<int>{root};
                while(queue.Count>0)
                {
                    var current=queue.Dequeue();var start=result[current.Node];
                    foreach(var neighbor in incident[current.Node])
                    {
                        if(neighbor.Other==current.Parent)continue;
                        var edge=graph.Edges[neighbor.Edge];
                        var a=projected[current.Node];var b=projected[neighbor.Other];
                        double dx=b.X-a.X,dy=b.Y-a.Y,len=Math.Sqrt(dx*dx+dy*dy);
                        if(len<.001)throw new LayoutProjectionException("Участок вырождается в выбранном ракурсе; выберите другое направление севера.");
                        double drawingLength=desired[neighbor.Edge];
                        var target=new SheetPoint(start.X+dx/len*drawingLength,start.Y+dy/len*drawingLength);
                        if(result.TryGetValue(neighbor.Other,out var existing))
                        {
                            if(Math.Sqrt(Math.Pow(existing.X-target.X,2)+Math.Pow(existing.Y-target.Y,2))>.1)
                                throw new InvalidDataException("Не совпали узлы замкнутого контура после совместного расчёта длин труб.");
                            continue;
                        }
                        result[neighbor.Other]=target;component.Add(neighbor.Other);queue.Enqueue((neighbor.Other,current.Node));
                    }
                }
                components.Add(component);
            }
            foreach(int root in Enumerable.Range(0,graph.Nodes.Count).Select(graph.Root).Distinct())
                if(!result.ContainsKey(root)){result[root]=new(0,0);components.Add(new(){root});}
            // Rigidly pack disconnected groups in two dimensions without inventing connections.
            var packed=new List<DrawingBounds>();double heightLimit=SheetComposition.Paper.Height-(compactLevel==0?55:compactLevel==1?75:95)-paperMargin;
            foreach(var component in components.OrderByDescending(c=>(c.Max(n=>result[n].X)-c.Min(n=>result[n].X)+10)*(c.Max(n=>result[n].Y)-c.Min(n=>result[n].Y)+10)))
            {
                double left=component.Min(n=>result[n].X),bottom=component.Min(n=>result[n].Y);
                double width=Math.Max(5,component.Max(n=>result[n].X)-left),height=Math.Max(5,component.Max(n=>result[n].Y)-bottom);
                var xs=new[]{0.0}.Concat(packed.Select(b=>b.Right+20)).Distinct();var ys=new[]{0.0}.Concat(packed.Select(b=>b.Top+20)).Distinct();
                var choices=(from x in xs from y in ys let box=new DrawingBounds(x,y,x+width,y+height)
                             where !packed.Any(b=>box.Overlaps(new(b.Left-19.99,b.Bottom-19.99,b.Right+19.99,b.Top+19.99)))
                             let w=Math.Max(box.Right,packed.Select(b=>b.Right).DefaultIfEmpty(0).Max())
                             let h=Math.Max(box.Top,packed.Select(b=>b.Top).DefaultIfEmpty(0).Max())
                             orderby Math.Max(w/availableWidth,h/heightLimit),w*h,x,y select box).ToArray();
                var selected=choices[0];
                foreach(int node in component)result[node]=new(result[node].X-left+selected.Left,result[node].Y-bottom+selected.Bottom);
                packed.Add(selected);
            }
            return result;
        }
        double maxHeight=SheetComposition.Paper.Height-(compactLevel==3?20:compactLevel==0?55:compactLevel==1?75:95)-paperMargin;
        bool Fits(Dictionary<int,SheetPoint> p)=>p.Count>0&&p.Values.Max(v=>v.X)-p.Values.Min(v=>v.X)<=availableWidth&&p.Values.Max(v=>v.Y)-p.Values.Min(v=>v.Y)<=maxHeight;
        var minimum=Arrange(0);
        if(!Fits(minimum))throw new SheetCapacityException($"Minimum readable geometry does not fit one A2 sheet: {minimum.Values.Max(v=>v.X)-minimum.Values.Min(v=>v.X):F1} x {minimum.Values.Max(v=>v.Y)-minimum.Values.Min(v=>v.Y):F1}");
        double low=0,high=.2;
        // Minimum-density mode uses Arrange(0) unchanged: no scale search is needed.
        // 18 steps resolve less than 0.001 mm over normal drawing extents.
        if(fixedScale is null&&compactLevel!=3)for(int i=0;i<18;i++){execution?.Check();double mid=(low+high)/2;if(Fits(Arrange(mid)))low=mid;else high=mid;}
        if(compactLevel>=2)low*=compactLevel switch{2=>.8,4=>.6,5=>.5,_=>0};
        if(fixedScale is {} scaleOverride)low=scaleOverride;
        BaseScale=low;
        var arranged=compactLevel==3?minimum:Arrange(low);
        var pipeEdges=Enumerable.Range(0,graph.Edges.Count).Where(i=>graph.Edges[i].Kind==ComponentKind.Pipe).ToArray();
        var growablePipes=pipeEdges.Where(i=>double.IsPositiveInfinity(upperBounds[i])).ToArray();
        double EdgeLength(int i,Dictionary<int,SheetPoint> p)
        {
            var edge=graph.Edges[i];var a=p[graph.Root(edge.A)];var b=p[graph.Root(edge.B)];return Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));
        }
        var lengths=pipeEdges.ToDictionary(i=>i,i=>EdgeLength(i,arranged));
        bool Crosses(int i,int j,Dictionary<int,SheetPoint> p)
        {
            var ei=graph.Edges[i];var ej=graph.Edges[j];
            int a=graph.Root(ei.A),b=graph.Root(ei.B),c=graph.Root(ej.A),d=graph.Root(ej.B);
            if(a==c||a==d||b==c||b==d)return false;
            var u=p[a];var v=p[b];var w=p[c];var z=p[d];
            double Cross(double x,double y,double xx,double yy)=>x*yy-y*xx;
            double den=Cross(v.X-u.X,v.Y-u.Y,z.X-w.X,z.Y-w.Y);if(Math.Abs(den)<1e-8)return false;
            double t=Cross(w.X-u.X,w.Y-u.Y,z.X-w.X,z.Y-w.Y)/den,s=Cross(w.X-u.X,w.Y-u.Y,v.X-u.X,v.Y-u.Y)/den;
            return t>.001&&t<.999&&s>.001&&s<.999;
        }
        var edgePairs=(from i in Enumerable.Range(0,graph.Edges.Count) from j in Enumerable.Range(i+1,graph.Edges.Count-i-1) where !Crosses(i,j,arranged) select (i,j)).ToArray();
        // Extend short pipes in balanced pairs where possible: a longer downward leg can free room
        // for a longer upward leg. Component geometry stays fixed at its project-library size.
        for(int iteration=0;iteration<(fixedScale is null&&compactLevel==0&&!cycleSolver.HasCycles?10:0);iteration++)
        {
            var options=(from i in growablePipes from j in growablePipes where i<j orderby lengths[i]+lengths[j] select new[]{i,j})
                .Concat(growablePipes.OrderBy(i=>lengths[i]).Select(i=>new[]{i}));
            bool changed=false;
            // Optional visual growth must not enumerate every pipe pair on every pass.
            // The main scale fit has already filled the available drawing area.
            foreach(var option in options.Take(32))
            {
                foreach(int i in option)lengths[i]+=pipeGridStep;
                var candidate=Arrange(low,lengths);
                if(Fits(candidate)&&!edgePairs.Any(pair=>Crosses(pair.i,pair.j,candidate)))
                {arranged=candidate;foreach(int pipe in pipeEdges)lengths[pipe]=EdgeLength(pipe,arranged);changed=true;break;}
                foreach(int i in option)lengths[i]-=pipeGridStep;
            }
            if(!changed)break;
        }
        NonGridPipes=pipeEdges.Where(i=>
        {
            double length=EdgeLength(i,arranged);
            return Math.Abs(length-Math.Round(length/pipeGridStep)*pipeGridStep)>.01;
        }).Select(i=>graph.Edges[i].Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        positions=arranged;
        shift=new(70-positions.Values.Min(p=>p.X),55-positions.Values.Min(p=>p.Y));
        foreach(int root in positions.Keys.ToArray())positions[root]=new(positions[root].X+shift.X,positions[root].Y+shift.Y);
        EnlargedEdges=graph.Edges.Select((edge,index)=>(edge,index)).Count(item=>
        {
            var e=item.edge;
            var a=IsometricProjection.Project(graph.Nodes[e.A].Point,quarterTurns:quarterTurns);var b=IsometricProjection.Project(graph.Nodes[e.B].Point,quarterTurns:quarterTurns);
            return e.Kind==ComponentKind.Pipe&&!hiddenGaskets.Contains(e.Source)&&Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2))*low<lowerBounds[item.index]-.01;
        });
        for(int i=0;i<graph.Nodes.Count;i++)
        {
            var key=Key(graph.Nodes[i].Point);if(!index.ContainsKey(key))index[key]=new();index[key].Add(i);
        }
    }
    public SheetPoint Project(ModelPoint point,string? source=null)
    {
        if(index.TryGetValue(Key(point),out var matches))
        {
            if(source is not null)matches=matches.Where(n=>graph.Nodes[n].Source.Equals(source,StringComparison.OrdinalIgnoreCase)).ToList();
            if(matches.Count>0)
            {
            var values=matches.Select(n=>positions[graph.Root(n)]).ToArray();var first=values[0];
            if(values.Any(p=>Math.Sqrt(Math.Pow(p.X-first.X,2)+Math.Pow(p.Y-first.Y,2))>.1))throw new InvalidDataException("Coincident unconnected model points need source-specific layout mapping");
            return first;
            }
        }
        // Centers and dimension anchors within an edge are interpolated through the stretched edge.
        double best=double.MaxValue;SheetPoint? result=null;
        foreach(var edge in graph.Edges)
        {
            if(source is not null&&!edge.Source.Equals(source,StringComparison.OrdinalIgnoreCase))continue;
            var a=graph.Nodes[edge.A].Point;var b=graph.Nodes[edge.B].Point;
            double dx=b.X-a.X,dy=b.Y-a.Y,dz=b.Z-a.Z,length2=dx*dx+dy*dy+dz*dz;
            if(length2<1e-12)continue;
            double t=((point.X-a.X)*dx+(point.Y-a.Y)*dy+(point.Z-a.Z)*dz)/length2;
            if(t<-.0001||t>1.0001)continue;
            double distance=point.DistanceTo(new(a.X+dx*t,a.Y+dy*t,a.Z+dz*t));
            if(distance>best)continue;
            best=distance;t=Math.Clamp(t,0,1);var pa=positions[graph.Root(edge.A)];var pb=positions[graph.Root(edge.B)];
            result=new(pa.X+(pb.X-pa.X)*t,pa.Y+(pb.Y-pa.Y)*t);
        }
        if(best>.1||result is null)throw new InvalidDataException($"No schematic mapping for point {point}");
        return result.Value;
    }
}
