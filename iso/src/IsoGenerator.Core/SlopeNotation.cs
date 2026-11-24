using System.Globalization;
namespace IsoGenerator.Core;
internal static class SlopeNotation
{
    public static void Add(PipelineSnapshot snapshot,Func<ModelPoint,SheetPoint> project,int turns,List<Stroke> strokes,List<Annotation> labels,IReadOnlyList<PipeDimension> dimensions,Func<ModelPoint,string,SheetPoint>? projectSource=null)
    {
        var dimensionedBends=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var fitting in snapshot.Parts.Where(part=>part.Component==ComponentKind.Elbow||part.CatalogClass=="PipeBend"))
        {
            var ports=fitting.Ports.Where(port=>port.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(ports.Length!=2)continue;
            var a=ports[0].Direction;var b=ports[1].Direction;
            double lengths=a.DistanceTo(default)*b.DistanceTo(default);
            if(!double.IsFinite(lengths)||lengths<1e-10)continue;
            double cosine=(a.X*b.X+a.Y*b.Y+a.Z*b.Z)/lengths;
            double turn=Math.Acos(Math.Clamp(-cosine,-1d,1d))*180/Math.PI;
            if(Math.Abs(turn-90)<.01)continue;
            dimensionedBends.Add(fitting.Handle);
            var first=projectSource?.Invoke(ports[0].Position,fitting.Handle)??project(ports[0].Position);
            var last=projectSource?.Invoke(ports[1].Position,fitting.Handle)??project(ports[1].Position);
            var center=new SheetPoint((first.X+last.X)/2,(first.Y+last.Y)/2);
            SheetPoint? angularAnchor=null;
            if(PipelineTopology.ElbowCorner(fitting,ports[0],ports[1]) is {} axisCorner)
            {
                var vertex=projectSource?.Invoke(axisCorner,fitting.Handle)??project(axisCorner);
                var incoming=new SheetPoint(vertex.X-first.X,vertex.Y-first.Y);
                var outgoing=new SheetPoint(last.X-vertex.X,last.Y-vertex.Y);
                double u=Math.Sqrt(incoming.X*incoming.X+incoming.Y*incoming.Y);
                double v=Math.Sqrt(outgoing.X*outgoing.X+outgoing.Y*outgoing.Y);
                if(u>.01&&v>.01&&Math.Abs(incoming.X*outgoing.Y-incoming.Y*outgoing.X)>.001)
                {
                    double bx=incoming.X/u+outgoing.X/v,by=incoming.Y/u+outgoing.Y/v;
                    double length=Math.Sqrt(bx*bx+by*by);
                    if(length>.001)
                    {
                        const double reach=9;
                        center=new(vertex.X+reach*bx/length,vertex.Y+reach*by/length);
                        var ray=new SheetPoint(vertex.X+incoming.X,vertex.Y+incoming.Y);
                        strokes.Add(new(fitting.Handle,"bend-dimension",new[]{vertex,ray,last,center}));
                        angularAnchor=center;
                    }
                }
            }
            labels.Add(new(fitting.Handle,"bend-angle",new(center.X+2,center.Y+3),
                turn.ToString("0.##",CultureInfo.InvariantCulture)+"°",Anchor:angularAnchor));
        }
        var pipes=snapshot.Parts.Where(p=>p.Component==ComponentKind.Pipe)
            .Select(part=>(Part:part,Ports:part.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray()))
            .Where(pipe=>pipe.Ports.Length==2).ToArray();
        var linesWithSlope=pipes.Where(pipe=>
        {
            var (run,rise)=RunAndRise(pipe.Ports[0].Position,pipe.Ports[1].Position);
            return IsGrade(run,rise);
        }).Select(pipe=>pipe.Part.Line).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var runs=HatchRuns(dimensions)
            .OrderByDescending(row=>row.ModelStart!.Value.DistanceTo(row.ModelEnd!.Value))
            .SelectMany(row=>(row.SourceIds??new[]{row.SourceId}).Select(handle=>(Handle:handle,Row:row)))
            .ToLookup(item=>item.Handle,item=>item.Row,StringComparer.OrdinalIgnoreCase);
        var drawnRuns=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var partsByHandle=snapshot.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
        var redundantBends=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var neighbors=snapshot.Connections.SelectMany(c=>new[]{(From:c.FirstHandle,To:c.SecondHandle),(From:c.SecondHandle,To:c.FirstHandle)})
            .ToLookup(link=>link.From,link=>link.To,StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> ConnectedBends(IEnumerable<string> sources)
        {
            var seen=sources.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(string source in seen)
                if(dimensionedBends.Contains(source))yield return source;
            var queue=new Queue<string>(seen);
            while(queue.TryDequeue(out string? handle))
                foreach(var other in neighbors[handle])
                {
                    if(!seen.Add(other)||!partsByHandle.TryGetValue(other,out var neighbor))continue;
                    if(neighbor.IsConnector)queue.Enqueue(other);
                    else if(dimensionedBends.Contains(other))yield return other;
                }
        }
        foreach(var (part,ports) in pipes)
        {
            SheetPoint At(ModelPoint point)=>projectSource is null?project(point):projectSource(point,part.Handle);
            var a=ports[0].Position;var b=ports[1].Position;
            double dx=b.X-a.X,dy=b.Y-a.Y,dz=b.Z-a.Z,length=a.DistanceTo(b),horizontal=Math.Sqrt(dx*dx+dy*dy);
            bool grade=IsGrade(horizontal,Math.Abs(dz));
            double planAngle=Math.Atan2(Math.Min(Math.Abs(dx),Math.Abs(dy)),Math.Max(Math.Abs(dx),Math.Abs(dy)));
            bool gradeWithPlanSkew=grade&&planAngle>Math.PI/180;
            if(grade)
            {
                var high=dz<0?At(a):At(b);var low=dz<0?At(b):At(a);
                double ux=low.X-high.X,uy=low.Y-high.Y,span=Math.Sqrt(ux*ux+uy*uy);if(span<.01)continue;ux/=span;uy/=span;
                double nx=-uy,ny=ux;if(ny<0){nx=-nx;ny=-ny;}
                var center=new SheetPoint((high.X+low.X)/2+nx*6,(high.Y+low.Y)/2+ny*6);
                double half=Math.Min(6,span/4);
                var tail=new SheetPoint(center.X-ux*half,center.Y-uy*half);var slopeTip=new SheetPoint(center.X+ux*half,center.Y+uy*half);
                var upperTail=new SheetPoint(tail.X+nx*1.8,tail.Y+ny*1.8);
                strokes.Add(new(part.Handle,"slope-indicator",new[]{tail,slopeTip,upperTail}));
                double rotation=Math.Atan2(uy,ux);if(rotation>Math.PI/2)rotation-=Math.PI;if(rotation<=-Math.PI/2)rotation+=Math.PI;
                labels.Add(new(part.Handle,"slope-value",new(center.X+nx*2.8,center.Y+ny*2.8),(Math.Abs(dz)/horizontal).ToString("0.######",CultureInfo.GetCultureInfo("ru-RU")),Rotation:rotation));
                if(!gradeWithPlanSkew)continue;
            }
            if(length<.001||Math.Max(Math.Abs(dx),Math.Max(Math.Abs(dy),Math.Abs(dz)))/length>Math.Cos(.01*Math.PI/180))continue;
            // A slope-marked run can carry small deviations into adjacent near-vertical legs.
            if(linesWithSlope.Contains(part.Line)&&horizontal<Math.Abs(dz)*Math.Tan(Math.PI/180))continue;
            var pa=At(a);var pb=At(b);
            var overall=runs[part.Handle].FirstOrDefault(row=>
            {
                var delta=Delta(row.ModelStart!.Value,row.ModelEnd!.Value);
                double runLength=delta.DistanceTo(default);
                return runLength>=length-.01&&Math.Abs(dx*delta.X+dy*delta.Y+dz*delta.Z)>=
                    runLength*length*Math.Cos(Math.PI/180);
            });
            if(overall is not null)
            {
                if(drawnRuns.Contains(overall.ChainId))continue;
                var delta=Delta(overall.ModelStart!.Value,overall.ModelEnd!.Value);
                (dx,dy,dz)=(delta.X,delta.Y,delta.Z);
                (pa,pb)=(overall.Start,overall.End);
                planAngle=Math.Atan2(Math.Min(Math.Abs(dx),Math.Abs(dy)),Math.Max(Math.Abs(dx),Math.Abs(dy)));
            }
            // Reference plane is horizontal for a rise, XY for a horizontal skew.
            double run=Math.Sqrt(dx*dx+dy*dy);
            bool elevationPlane=Math.Abs(dz)>.001&&!(IsGrade(run,Math.Abs(dz))&&planAngle>Math.PI/180);
            var reference=elevationPlane?new ModelPoint(dx,dy,0):Math.Abs(dx)>=Math.Abs(dy)?new ModelPoint(dx,0,0):new ModelPoint(0,dy,0);
            double refLength=Math.Sqrt(reference.X*reference.X+reference.Y*reference.Y);
            double angle=(elevationPlane?Math.Atan2(Math.Abs(dz),refLength):planAngle)*180/Math.PI;
            var modelVector=IsometricProjection.Project(new(dx,dy,dz),quarterTurns:turns);
            double projectedLength=Math.Sqrt(modelVector.X*modelVector.X+modelVector.Y*modelVector.Y);
            double scale=Math.Sqrt(Math.Pow(pb.X-pa.X,2)+Math.Pow(pb.Y-pa.Y,2))/projectedLength;
            var start=pa;var tip=pb;
            var r=IsometricProjection.Project(reference,quarterTurns:turns);
            var corner=new SheetPoint(start.X+r.X*scale,start.Y+r.Y*scale);
            if(Math.Abs((corner.X-start.X)*(tip.Y-start.Y)-(corner.Y-start.Y)*(tip.X-start.X))<.1)continue;
            if(overall is not null)drawnRuns.Add(overall.ChainId);
            strokes.Add(new(part.Handle,"slope-triangle",new[]{start,corner,tip}));
            Hatch(part.Handle,start,corner,tip,strokes);
            var legA=new SheetPoint(start.X-corner.X,start.Y-corner.Y);var legB=new SheetPoint(tip.X-corner.X,tip.Y-corner.Y);
            double la=Math.Sqrt(legA.X*legA.X+legA.Y*legA.Y),lb=Math.Sqrt(legB.X*legB.X+legB.Y*legB.Y);
            double square=Math.Min(2.5,Math.Min(la,lb)*.2);
            var qa=new SheetPoint(corner.X+legA.X/la*square,corner.Y+legA.Y/la*square);
            var qb=new SheetPoint(corner.X+legB.X/lb*square,corner.Y+legB.Y/lb*square);
            strokes.Add(new(part.Handle,"slope-right-angle",new[]{qa,new SheetPoint(qa.X+legB.X/lb*square,qa.Y+legB.Y/lb*square),qb}));
            // The angular dimension is a projected circular arc in the actual 3D slope plane.
            var rise=new ModelPoint(dx-reference.X,dy-reference.Y,elevationPlane?dz:0);
            double riseLength=Math.Sqrt(rise.X*rise.X+rise.Y*rise.Y+rise.Z*rise.Z);
            AddProjectionLength(part.Handle,start,corner,tip,refLength,labels,strokes);
            AddProjectionLength(part.Handle,corner,tip,start,riseLength,labels,strokes);
            double alpha=angle*Math.PI/180;
            double radius=Math.Min(15,Math.Min(la,lb)*.65);
            SheetPoint Arc(double theta)
            {
                var vector=new ModelPoint(reference.X/refLength*Math.Cos(theta)+rise.X/riseLength*Math.Sin(theta),reference.Y/refLength*Math.Cos(theta)+rise.Y/riseLength*Math.Sin(theta),rise.Z/riseLength*Math.Sin(theta));
                var v=IsometricProjection.Project(vector,quarterTurns:turns);return new(start.X+v.X*radius,start.Y+v.Y*radius);
            }
            var arc=Enumerable.Range(0,33).Select(i=>Arc(alpha*i/32)).ToArray();
            strokes.Add(new(part.Handle,"slope-angle-arc",arc));
            void Arrow(SheetPoint point,SheetPoint inside)
            {
                double tx=point.X-inside.X,ty=point.Y-inside.Y,l=Math.Sqrt(tx*tx+ty*ty);tx/=l;ty/=l;
                const double arrowLength=2.5;double half=arrowLength*Math.Tan(10*Math.PI/180);
                var a1=new SheetPoint(point.X-tx*arrowLength-ty*half,point.Y-ty*arrowLength+tx*half);
                var a2=new SheetPoint(point.X-tx*arrowLength+ty*half,point.Y-ty*arrowLength-tx*half);
                strokes.Add(new(part.Handle,"slope-angle-arrow",new[]{point,a1,a2,point}));
            }
            Arrow(arc[0],arc[1]);Arrow(arc[^1],arc[^2]);
            var labelAnchor=Arc(alpha/2);
            labels.Add(new(part.Handle,"slope-angle",new(labelAnchor.X-3,labelAnchor.Y+2),
                angle.ToString("0.#",CultureInfo.InvariantCulture)+"°",Anchor:labelAnchor));
            redundantBends.UnionWith(ConnectedBends(overall?.SourceIds??new[]{part.Handle}));
        }
        labels.RemoveAll(label=>label.Role=="bend-angle"&&redundantBends.Contains(label.SourceId));
        strokes.RemoveAll(stroke=>stroke.Role=="bend-dimension"&&redundantBends.Contains(stroke.SourceId));
    }
    static void AddProjectionLength(string source,SheetPoint a,SheetPoint b,SheetPoint opposite,double length,List<Annotation> labels,List<Stroke> strokes)
    {
        if(length<.5)return;
        double dx=b.X-a.X,dy=b.Y-a.Y,span=Math.Sqrt(dx*dx+dy*dy);
        if(span<3)return;
        double ux=dx/span,uy=dy/span;
        if(PipeDimension.ReverseTextAxis(dx,dy)){ux=-ux;uy=-uy;}
        double nx=-uy,ny=ux;
        if((opposite.X-a.X)*nx+(opposite.Y-a.Y)*ny>0){nx=-nx;ny=-ny;}
        string text=Math.Round(length,MidpointRounding.AwayFromZero).ToString("0",CultureInfo.InvariantCulture);
        var point=new SheetPoint((a.X+b.X)/2+nx*4-ux*text.Length,(a.Y+b.Y)/2+ny*4-uy*text.Length);
        var midpoint=new SheetPoint((a.X+b.X)/2,(a.Y+b.Y)/2);
        strokes.Add(new(source,"slope-projection-dimension",new[]{a,b}));
        labels.Add(new(source,"slope-projection",point,text,Anchor:midpoint,Rotation:Math.Atan2(uy,ux)));
    }

    static void Hatch(string source,SheetPoint a,SheetPoint b,SheetPoint c,List<Stroke> strokes)
    {
        var triangle=new[]{a,b,c};
        double top=triangle.Max(point=>point.Y),bottom=triangle.Min(point=>point.Y);
        if(top-bottom<1)return;
        int count=Math.Clamp((int)Math.Floor((top-bottom)/4.0),1,32);
        for(int row=1;row<=count;row++)
        {
            double y=bottom+(top-bottom)*row/(count+1);
            var intersections=triangle.Zip(triangle.Skip(1).Append(a))
                .Where(edge=>(edge.First.Y<=y&&edge.Second.Y>y)||(edge.Second.Y<=y&&edge.First.Y>y))
                .Select(edge=>edge.First.X+(y-edge.First.Y)*(edge.Second.X-edge.First.X)/(edge.Second.Y-edge.First.Y))
                .Order().ToArray();
            if(intersections.Length==2&&intersections[1]-intersections[0]>.5)
                strokes.Add(new(source,"slope-hatch",new SheetPoint[]{new(intersections[0],y),new(intersections[1],y)}));
        }
    }

    static IReadOnlyList<PipeDimension> HatchRuns(IReadOnlyList<PipeDimension> dimensions)
    {
        var runs=dimensions.Where(row=>row.Role=="overall"&&row.ModelStart.HasValue&&row.ModelEnd.HasValue).ToList();
        double straight=Math.Cos(Math.PI/180);
        for(int i=0;i<runs.Count;i++)
        {
            for(int j=i+1;j<runs.Count;j++)
            {
                var a=runs[i];var b=runs[j];
                var left=new[]{(Model:a.ModelStart!.Value,Paper:a.Start),(Model:a.ModelEnd!.Value,Paper:a.End)};
                var right=new[]{(Model:b.ModelStart!.Value,Paper:b.Start),(Model:b.ModelEnd!.Value,Paper:b.End)};
                bool joined=false;
                for(int s=0;s<2&&!joined;s++)
                for(int t=0;t<2&&!joined;t++)
                {
                    if(left[s].Model.DistanceTo(right[t].Model)>1)continue;
                    var u=Delta(left[s].Model,left[1-s].Model);
                    var v=Delta(right[t].Model,right[1-t].Model);
                    double length=u.DistanceTo(default)*v.DistanceTo(default);
                    if(length<1e-6||u.X*v.X+u.Y*v.Y+u.Z*v.Z>-length*straight)continue;
                    var first=left[1-s];var last=right[1-t];
                    runs[i]=a with
                    {
                        ModelStart=first.Model,ModelEnd=last.Model,Start=first.Paper,End=last.Paper,
                        SourceIds=(a.SourceIds??new[]{a.SourceId}).Concat(b.SourceIds??new[]{b.SourceId})
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                    };
                    runs.RemoveAt(j);
                    j=i;
                    joined=true;
                }
            }
        }
        return runs;
    }

    static ModelPoint Delta(ModelPoint a,ModelPoint b)=>new(b.X-a.X,b.Y-a.Y,b.Z-a.Z);

    static (double Run,double Rise) RunAndRise(ModelPoint a,ModelPoint b)
    {
        double dx=b.X-a.X,dy=b.Y-a.Y;
        return (Math.Sqrt(dx*dx+dy*dy),Math.Abs(b.Z-a.Z));
    }

    static bool IsGrade(double run,double rise)=>run>1&&rise>.001&&rise/run<=.1;
}
