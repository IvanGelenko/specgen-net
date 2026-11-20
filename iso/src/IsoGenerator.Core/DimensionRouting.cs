namespace IsoGenerator.Core;
// Route whole dimension chains together; never change engineering values or their model anchors.
public static class DimensionRouting
{
    public readonly record struct Segment(SheetPoint A,SheetPoint B,bool Baseline);
    public sealed record Choice(PipeDimension[] Rows,Segment[] Segments,double Cost);
    sealed record State(List<Choice> Choices,double Cost,double Left=double.PositiveInfinity,double Bottom=double.PositiveInfinity,double Right=double.NegativeInfinity,double Top=double.NegativeInfinity);
    internal static IReadOnlyList<Stroke> TextObstacles(IReadOnlyList<Stroke> strokes,IReadOnlyList<SymbolGlyph> symbols)=>strokes.Concat(symbols.SelectMany(g=>
    {
        if(g.LocalContours is {Count:>0})return g.LocalContours.Select(contour=>new Stroke(g.SourceId,"symbol-contour",contour.Select(p=>new SheetPoint(g.Origin.X+g.XAxis.X*p.X+g.YAxis.X*p.Y,g.Origin.Y+g.XAxis.Y*p.X+g.YAxis.Y*p.Y)).ToArray()));
        var corners=SymbolFootprint.Corners(g).ToArray();return new[]{new Stroke(g.SourceId,"symbol-contour",corners.Append(corners[0]).ToArray())};
    })).ToArray();
    static double Dot(SheetPoint a,SheetPoint b)=>a.X*b.X+a.Y*b.Y;
    static SheetPoint Sub(SheetPoint a,SheetPoint b)=>new(a.X-b.X,a.Y-b.Y);
    static SheetPoint Unit(SheetPoint v){double l=Math.Sqrt(Dot(v,v));return new(v.X/l,v.Y/l);}
    public static bool CheckCross(Segment a,Segment b)=>Cross(a,b);
    static bool Cross(Segment a,Segment b)
    {
        var u=Sub(a.B,a.A);var v=Sub(b.B,b.A);double den=u.X*v.Y-u.Y*v.X;if(Math.Abs(den)<1e-8)return false;
        var w=Sub(b.A,a.A);double t=(w.X*v.Y-w.Y*v.X)/den,s=(w.X*u.Y-w.Y*u.X)/den;
        // Contact at arrow tips and shared extension origins is intentional.
        return t>1e-5&&t<1-1e-5&&s>1e-5&&s<1-1e-5;
    }
    static (SheetPoint A,SheetPoint B) Line(PipeDimension d)
    {
        var axis=Unit(Sub(d.End,d.Start));var n=new SheetPoint(-axis.Y,axis.X);var ex=d.ExtensionDirection!.Value;var p=d.DimensionLinePoint!.Value;
        SheetPoint At(SheetPoint point){double t=Dot(Sub(p,point),n)/Dot(ex,n);return new(point.X+ex.X*t,point.Y+ex.Y*t);}
        return (At(d.Start),At(d.End));
    }
    public static Segment[] GetSegments(IEnumerable<PipeDimension> rows)=>Segments(rows);

    static (string Source,Segment Axis)[] MainRuns(IReadOnlyList<Stroke> strokes)=>strokes
        .Where(stroke=>stroke.Role=="pipe"&&stroke.Points.Count>=2)
        .Select(stroke=>(stroke.SourceId,new Segment(stroke.Points[0],stroke.Points[^1],false))).ToArray();

    internal static PipeDimension? LongWitnessThroughParallelRun(IReadOnlyList<PipeDimension> rows,IReadOnlyList<Stroke> strokes)
    {
        var runs=MainRuns(strokes);
        foreach(var chain in rows.GroupBy(row=>row.ChainId))
        {
            var dimensions=chain.ToArray();
            var basis=dimensions.FirstOrDefault(row=>row.Role=="overall")??dimensions[0];
            var sources=dimensions.SelectMany(row=>row.SourceIds??new[]{row.SourceId}).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var obstacles=ParallelRuns(runs,Unit(Sub(basis.End,basis.Start)),sources);
            var blocked=dimensions.FirstOrDefault(row=>CrossesParallelRun(row,obstacles));
            if(blocked is not null)return blocked;
        }
        return null;
    }

    static Segment[] ParallelRuns((string Source,Segment Axis)[] runs,SheetPoint direction,HashSet<string> sources)=>
        runs.Where(run=>!sources.Contains(run.Source)&&Math.Abs(Dot(Unit(Sub(run.Axis.B,run.Axis.A)),direction))>=.999)
            .Select(run=>run.Axis).ToArray();

    static bool CrossesParallelRun(PipeDimension row,Segment[] obstacles)
    {
        if(obstacles.Length==0)return false;
        var (a,b)=Line(row);
        var first=new Segment(row.Start,a,false);
        var last=new Segment(row.End,b,false);
        double threshold=Math.Min(35,IsoAdvancedSettings.Current.LongExtensionThreshold);
        bool Through(Segment segment)=>Dot(Sub(segment.B,segment.A),Sub(segment.B,segment.A))>threshold*threshold&&
            obstacles.Any(pipe=>Cross(segment,pipe));
        return Through(first)||Through(last);
    }
    public static Choice[] GetChoices(PipeDimension[] rows, IReadOnlyList<Stroke> strokes, IReadOnlyList<SymbolGlyph> symbols, int turns)
    {
        var isoAxes = new[] { new ModelPoint(1, 0, 0), new ModelPoint(0, 1, 0), new ModelPoint(0, 0, 1) }
            .Select(v => IsometricProjection.Project(v, quarterTurns: turns))
            .Select(v => { double l = Math.Sqrt(v.X * v.X + v.Y * v.Y); return new SheetPoint(v.X / l, v.Y / l); })
            .ToArray();
        var geometry=strokes.Where(s=>s.Role is "pipe" or "elbow" or "branch" or "component" or "flange-neck" or "slope-triangle")
            .SelectMany(s=>s.Points.Zip(s.Points.Skip(1),(a,b)=>new Segment(a,b,true))).ToArray();
        return MakeChoices(rows, isoAxes, TextObstacles(strokes,symbols), symbols, geometry, FittingEdges(symbols));
    }
    static Choice[] MakeChoices(PipeDimension[] rows, SheetPoint[] isoAxes, IReadOnlyList<Stroke> strokes, IReadOnlyList<SymbolGlyph> symbols, Segment[] geometry, Segment[] fittings,PlanningExecution? execution=null)
    {
        var overall=rows.FirstOrDefault(d=>d.Role=="overall")??rows[0];var axis=Unit(Sub(overall.End,overall.Start));var n=new SheetPoint(-axis.Y,axis.X);
        if(Dot(n,overall.ExtensionDirection!.Value)<0)n=new(-n.X,-n.Y);
        bool hasChain=rows.Any(d=>d.Role=="chain");
        var exs=new List<SheetPoint>{overall.ExtensionDirection!.Value};
        bool vertical=Math.Abs(axis.X)<.05;
        if(vertical)
            exs.AddRange(isoAxes.Where(ex=>Math.Abs(Dot(ex,n))>.5&&Math.Abs(Dot(ex,exs[0]))<.999));
        // A standing run can use either vertical isometric plane.
        var source=rows.SelectMany(d=>d.SourceIds??new[]{d.SourceId}).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var contour=strokes.Where(s=>s.Role!="insulation"&&source.Contains(s.SourceId)).SelectMany(s=>s.Points).Concat(symbols.Where(g=>source.Contains(g.SourceId)).SelectMany(SymbolFootprint.Corners)).ToArray();
        var contextSegments=strokes.Where(s=>s.Role.StartsWith("context-",StringComparison.Ordinal)).SelectMany(s=>s.Points.Zip(s.Points.Skip(1),(a,b)=>new Segment(a,b,false))).ToArray();
        var symbolEdges=TextObstacles(Array.Empty<Stroke>(),symbols).SelectMany(s=>s.Points.Zip(s.Points.Skip(1),(a,b)=>new Segment(a,b,false))).ToArray();
        var choices=new List<Choice>();
        var pipes=PipeSegments(strokes);
        var parallel=ParallelRuns(MainRuns(strokes),axis,source);
        var textSegments=DimensionTextPlacement.ObstacleSegments(strokes);
        foreach(int side in new[]{1,-1})
        {
            double envelope=contour.Select(p=>Dot(Sub(p,overall.Start),n)*side).Where(v=>v>=0&&v<=25).DefaultIfEmpty(0).Max();
            double first=MinimumPipeClearance+envelope;
            foreach(var ex in exs)for(int lane=0;lane<=2*IsoAdvancedSettings.Current.DimensionLaneCount;lane++)
            {
                execution?.Check();
                var placed=new List<PipeDimension>();
                foreach(var d in rows)
                {
                    double offset=side*(first+IsoAdvancedSettings.Current.DimensionRowStep*(lane*.5+(hasChain&&d.Role=="overall"?1:0)));
                    var linePoint=new SheetPoint(overall.Start.X+n.X*offset,overall.Start.Y+n.Y*offset);
                    var moved=d with{DimensionLinePoint=linePoint,ExtensionDirection=ex};var (a,b)=Line(moved);
                    var ownAxis=Unit(Sub(d.End,d.Start));var readableNormal=new SheetPoint(-ownAxis.Y,ownAxis.X);
                    if(PipeDimension.ReverseTextAxis(ownAxis.X,ownAxis.Y))readableNormal=new(-readableNormal.X,-readableNormal.Y);
                    placed.Add(moved with{TextPosition=new((a.X+b.X)/2+readableNormal.X*IsoAdvancedSettings.Current.TextLift,(a.Y+b.Y)/2+readableNormal.Y*IsoAdvancedSettings.Current.TextLift)});
                }
                if(placed.Any(row=>CrossesParallelRun(row,parallel)))continue;
                var lines=Segments(placed);
                if(lines.Any(line=>line.Baseline&&TooCloseToFitting(line,fittings)))continue;
                if(PipeClearanceViolations(lines,pipes)>0)continue;
                int modelCross=lines.Where(l=>l.Baseline||vertical).Sum(l=>geometry.Count(g=>Cross(l,g)));
                double corridorPenalty=0;
                foreach(var pipe in strokes.Where(st=>st.Role=="pipe"&&!source.Contains(st.SourceId)&&st.Points.Count>=2))
                {
                    var p=pipe.Points[0];var q=pipe.Points[^1];double px=q.X-p.X,py=q.Y-p.Y,pl=Math.Sqrt(px*px+py*py);
                    if(pl<1||Math.Abs((px*axis.X+py*axis.Y)/pl)<.999)continue;
                    double otherOffset=Dot(Sub(p,overall.Start),n);
                    if(Math.Abs(otherOffset)<5||Math.Abs(otherOffset)>60)continue;
                    double overlap=Math.Min(Dot(Sub(q,overall.Start),axis)>Dot(Sub(p,overall.Start),axis)?Dot(Sub(q,overall.Start),axis):Dot(Sub(p,overall.Start),axis),Math.Sqrt(Math.Pow(overall.End.X-overall.Start.X,2)+Math.Pow(overall.End.Y-overall.Start.Y,2)))-Math.Max(0,Math.Min(Dot(Sub(p,overall.Start),axis),Dot(Sub(q,overall.Start),axis)));
                    if(overlap<5)continue;
                    double offset=Dot(Sub(placed[0].DimensionLinePoint!.Value,overall.Start),n);
                    if(offset*otherOffset>0&&Math.Abs(offset)<Math.Abs(otherOffset)+3)corridorPenalty+=5000;
                }
                // Continuation dashes on a witness line are a local visual cost.
                // Give the dimension baseline and its readable text priority over that contact.
                double contextCost=lines.Sum(line=>contextSegments.Sum(context=>
                    OverlapsContext(line,context)?(line.Baseline?50000d:200d):
                    Cross(line,context)?(line.Baseline?1000d:30d):0d));
                double witnessLength=lines.Where(line=>!line.Baseline)
                    .Select(line=>Math.Sqrt(Dot(Sub(line.B,line.A),Sub(line.B,line.A)))).DefaultIfEmpty().Max();
                double cost=corridorPenalty+contextCost+
                    DimensionTextPlacement.CollisionCount(placed,strokes,textSegments)*1000000+
                    lines.Where(line=>line.Baseline).Sum(line=>symbolEdges.Count(edge=>Cross(line,edge)))*5000+
                    modelCross*5000+witnessLength+(side==-1?.5:0)+(!exs[0].Equals(ex)?1:0);
                choices.Add(new(placed.ToArray(),lines,cost));
            }
        }
        return choices.OrderBy(c=>c.Cost).ToArray();
    }
    static Segment[] FittingEdges(IReadOnlyList<SymbolGlyph> symbols)
    {
        var fittings=symbols.Where(symbol=>
        {
            string type=IsoDefinition.Parse(symbol.Definition).Type;
            return type is "VALVE" or "INSTRUMENT" or "FLANGE" or "FLANGE-BLIND" or "FLANGE-LAPJOINT";
        });
        return fittings.SelectMany(symbol=>
        {
            if(symbol.LocalContours is {Count:>0})
                return TextObstacles(Array.Empty<Stroke>(),new[]{symbol}).SelectMany(contour=>
                    contour.Points.Zip(contour.Points.Skip(1),(a,b)=>new Segment(a,b,false)));
            var corners=SymbolFootprint.Corners(symbol);
            double x=corners.Average(p=>p.X),y=corners.Average(p=>p.Y);
            var boundary=corners.OrderBy(p=>Math.Atan2(p.Y-y,p.X-x)).ToArray();
            return boundary.Zip(boundary.Skip(1).Append(boundary[0]),(a,b)=>new Segment(a,b,false));
        }).ToArray();
    }

    static bool TooCloseToFitting(Segment line,Segment[] edges)
    {
        double gap=IsoAdvancedSettings.Current.DimensionClearance-1e-6;
        double left=Math.Min(line.A.X,line.B.X)-gap,right=Math.Max(line.A.X,line.B.X)+gap;
        double bottom=Math.Min(line.A.Y,line.B.Y)-gap,top=Math.Max(line.A.Y,line.B.Y)+gap;
        double gapSquared=gap*gap;
        foreach(var edge in edges)
        {
            if(Math.Max(edge.A.X,edge.B.X)<left||Math.Min(edge.A.X,edge.B.X)>right||
               Math.Max(edge.A.Y,edge.B.Y)<bottom||Math.Min(edge.A.Y,edge.B.Y)>top)continue;
            if(Cross(line,edge)||DistanceSquared(line.A,edge)<gapSquared||DistanceSquared(line.B,edge)<gapSquared||
               DistanceSquared(edge.A,line)<gapSquared||DistanceSquared(edge.B,line)<gapSquared)return true;
        }
        return false;
    }

    internal static double DistanceSquared(SheetPoint point,Segment segment)
    {
        var direction=Sub(segment.B,segment.A);var delta=Sub(point,segment.A);
        double t=Math.Clamp(Dot(delta,direction)/Math.Max(double.Epsilon,Dot(direction,direction)),0,1);
        var separation=new SheetPoint(delta.X-t*direction.X,delta.Y-t*direction.Y);
        return Dot(separation,separation);
    }

    internal static bool FitsClearance(IEnumerable<PipeDimension> rows,IReadOnlyList<Stroke> strokes,IReadOnlyList<SymbolGlyph> symbols)
    {
        var segments=Segments(rows);
        var fittings=FittingEdges(symbols);
        return PipeClearanceViolations(segments,PipeSegments(strokes))==0&&
            !segments.Any(line=>line.Baseline&&TooCloseToFitting(line,fittings));
    }

    internal static bool FitsFittingClearance(IEnumerable<PipeDimension> rows,IReadOnlyList<SymbolGlyph> symbols)
    {
        var edges=FittingEdges(symbols);
        return edges.Length==0||!Segments(rows).Any(line=>line.Baseline&&TooCloseToFitting(line,edges));
    }

    internal static int FittingCrossings(SheetScene scene)
    {
        var pipes=PipeSegments(scene.Strokes);
        var sizes=Segments(scene.Dimensions).Where(segment=>segment.Baseline).ToArray();
        var contours=TextObstacles(Array.Empty<Stroke>(),(scene.Symbols??Array.Empty<SymbolGlyph>())
            .Where(symbol=>symbol.Variant!="missing"&&!symbol.Context&&
                IsoDefinition.Parse(symbol.Definition).Type!="SUPPORT").ToArray());
        return contours.GroupBy(stroke=>stroke.SourceId).Sum(group=>
        {
            var edges=group.SelectMany(stroke=>stroke.Points.Zip(stroke.Points.Skip(1),(a,b)=>new Segment(a,b,false))).ToArray();
            return pipes.Count(pipe=>edges.Any(edge=>Cross(pipe,edge)))+
                   sizes.Count(size=>edges.Any(edge=>Cross(size,edge)));
        });
    }

    internal static double MinimumPipeClearance=>IsoAdvancedSettings.Current.DimensionClearance;
    static Segment[] PipeSegments(IReadOnlyList<Stroke> strokes)=>strokes.Where(stroke=>stroke.Role=="pipe")
        .SelectMany(stroke=>stroke.Points.Zip(stroke.Points.Skip(1),(a,b)=>new Segment(a,b,false))).ToArray();

    internal static int PipeClearanceViolations(IEnumerable<PipeDimension> rows,IReadOnlyList<Stroke> strokes)
        =>PipeClearanceViolations(Segments(rows),PipeSegments(strokes));

    static int PipeClearanceViolations(Segment[] segments,Segment[] pipes)
    {
        int violations=0;
        foreach(var size in segments.Where(segment=>segment.Baseline))
        {
            var vector=Sub(size.B,size.A);
            double length=Math.Sqrt(Dot(vector,vector));
            if(length<.01)continue;
            var axis=Unit(vector);var normal=new SheetPoint(-axis.Y,axis.X);
            foreach(var pipe in pipes)
            {
                var direction=Sub(pipe.B,pipe.A);double span=Math.Sqrt(Dot(direction,direction));
                if(span<.01||Math.Abs(Dot(direction,axis))/span<.999)continue;
                var first=Sub(pipe.A,size.A);var last=Sub(pipe.B,size.A);
                double overlapStart=Math.Max(0,Math.Min(Dot(first,axis),Dot(last,axis)));
                double overlapEnd=Math.Min(length,Math.Max(Dot(first,axis),Dot(last,axis)));
                if(overlapEnd-overlapStart<.1)continue;
                double along=Dot(direction,axis),slope=Dot(direction,normal)/along;
                double gapA=Dot(first,normal)+(overlapStart-Dot(first,axis))*slope;
                double gapB=Dot(first,normal)+(overlapEnd-Dot(first,axis))*slope;
                double gap=gapA*gapB<=0?0:Math.Min(Math.Abs(gapA),Math.Abs(gapB));
                if(gap<MinimumPipeClearance-.05)violations++;
            }
        }
        return violations;
    }

    static bool InCorridor(PipeDimension d,IReadOnlyList<Stroke> strokes)
    {
        var sources=(d.SourceIds??new[]{d.SourceId}).ToHashSet(StringComparer.OrdinalIgnoreCase);
        double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,length=Math.Sqrt(dx*dx+dy*dy);if(length<.01)return false;
        var u=new SheetPoint(dx/length,dy/length);var n=new SheetPoint(-u.Y,u.X);
        double offset=Dot(Sub(d.DimensionLinePoint!.Value,d.Start),n);
        return strokes.Where(st=>st.Role=="pipe"&&!sources.Contains(st.SourceId)&&st.Points.Count>=2).Any(st=>
        {
            var a=st.Points[0];var b=st.Points[^1];var v=Sub(b,a);double l=Math.Sqrt(Dot(v,v));if(l<1||Math.Abs(Dot(v,u)/l)<.999)return false;
            double other=Dot(Sub(a,d.Start),n),first=Dot(Sub(a,d.Start),u),last=Dot(Sub(b,d.Start),u);
            return Math.Abs(other)>=5&&Math.Abs(other)<=60&&other*offset>0&&Math.Abs(offset)<Math.Abs(other)+3&&Math.Min(length,Math.Max(first,last))-Math.Max(0,Math.Min(first,last))>5;
        });
    }    static bool OverlapsContext(Segment a,Segment b)
    {
        double dx=a.B.X-a.A.X,dy=a.B.Y-a.A.Y,l=Math.Sqrt(dx*dx+dy*dy);if(l<.001)return false;
        double ux=dx/l,uy=dy/l;
        double h1=Math.Abs((b.A.X-a.A.X)*uy-(b.A.Y-a.A.Y)*ux),h2=Math.Abs((b.B.X-a.A.X)*uy-(b.B.Y-a.A.Y)*ux);
        if(Math.Max(h1,h2)>.8)return false;
        double t1=(b.A.X-a.A.X)*ux+(b.A.Y-a.A.Y)*uy,t2=(b.B.X-a.A.X)*ux+(b.B.Y-a.A.Y)*uy;
        return Math.Min(l,Math.Max(t1,t2))-Math.Max(0,Math.Min(t1,t2))>1;
    }
    static Segment[] Segments(IEnumerable<PipeDimension> rows)=>rows.SelectMany(d=>{var (a,b)=Line(d);return new[]{new Segment(a,b,true),new Segment(d.Start,a,false),new Segment(d.End,b,false)};}).Distinct().ToArray();
    static int Conflicts(Segment[] a,Segment[] b)=>a.Sum(x=>b.Count(y=>Cross(x,y)||((x.Baseline||y.Baseline)&&OverlapsContext(x,y))));
    public static int CrossingCount(IReadOnlyList<PipeDimension> dimensions)
    {
        var groups=dimensions.GroupBy(d=>d.ChainId).Select(g=>Segments(g)).ToArray();int result=0;
        for(int i=0;i<groups.Length;i++)for(int j=i+1;j<groups.Length;j++)result+=Conflicts(groups[i],groups[j]);
        return result;
    }
    // Alignment is cosmetic: it must never trade readable text or clean lines for
    // a common lane. Leaders and insulation do not require dimension breaks.
    internal static (int Text, int Crossings) AlignmentConflicts(IReadOnlyList<PipeDimension> rows,IReadOnlyList<Stroke> strokes,IReadOnlyList<SymbolGlyph> symbols)
    {
        var obstacles=TextObstacles(strokes,symbols);
        var geometry=strokes.Where(s=>s.Role is "pipe" or "elbow" or "branch" or "component" or "flange-neck" or "slope-triangle" || s.Role.StartsWith("context-",StringComparison.Ordinal))
            .Concat(obstacles.Where(s=>s.Role=="symbol-contour"))
            .SelectMany(s=>s.Points.Zip(s.Points.Skip(1),(a,b)=>new Segment(a,b,true))).ToArray();
        int crossings=CrossingCount(rows)+Segments(rows).Where(s=>s.Baseline).Sum(line=>geometry.Count(g=>Cross(line,g)||OverlapsContext(line,g)));
        return (DimensionTextPlacement.CollisionCount(rows,obstacles),crossings);
    }
    public static IReadOnlyList<PipeDimension> Route(IReadOnlyList<PipeDimension> input,IReadOnlyList<Stroke> strokes,IReadOnlyList<SymbolGlyph> symbols,int turns)=>
        Route(input,strokes,symbols,turns,null);

    internal static IReadOnlyList<PipeDimension> Route(IReadOnlyList<PipeDimension> input,IReadOnlyList<Stroke> strokes,
        IReadOnlyList<SymbolGlyph> symbols,int turns,PlanningExecution? execution)
    {
        if(input.Count==0)return input;
        var textStrokes=TextObstacles(strokes,symbols);
        var geometry=strokes.Where(s=>s.Role is "pipe" or "elbow" or "branch" or "component" or "flange-neck" or "slope-triangle")
            .SelectMany(s=>s.Points.Zip(s.Points.Skip(1),(a,b)=>new Segment(a,b,true))).ToArray();
        var symbolSegments=TextObstacles(Array.Empty<Stroke>(),symbols).SelectMany(s=>s.Points.Zip(s.Points.Skip(1),(a,b)=>new Segment(a,b,false))).ToArray();
        var fittings=FittingEdges(symbols);
        bool crossesSymbols=input.Any(d=>{var (a,b)=Line(d);return symbolSegments.Any(g=>Cross(new Segment(a,b,true),g));});
        bool crossesGeometry=input.Any(d=>{var (a,b)=Line(d);var line=new Segment(a,b,true);return geometry.Any(g=>Cross(line,g))||TooCloseToFitting(line,fittings);});
        if(CrossingCount(input)==0&&LongWitnessThroughParallelRun(input,strokes) is null&&PipeClearanceViolations(input,strokes)==0&&!crossesGeometry&&!crossesSymbols&&!input.Any(d=>InCorridor(d,strokes))&&DimensionTextPlacement.CollisionCount(input,textStrokes)==0)return input;
        var groups=input.GroupBy(d=>d.ChainId).Select(g=>g.ToArray()).ToArray();
        double Extent(PipeDimension[] rows){var d=rows.FirstOrDefault(x=>x.Role=="overall")??rows[0];return Dot(Sub(d.End,d.Start),Sub(d.End,d.Start));}
        var isoAxes = new[] { new ModelPoint(1, 0, 0), new ModelPoint(0, 1, 0), new ModelPoint(0, 0, 1) }
            .Select(v => IsometricProjection.Project(v, quarterTurns: turns))
            .Select(v => { double l = Math.Sqrt(v.X * v.X + v.Y * v.Y); return new SheetPoint(v.X / l, v.Y / l); })
            .ToArray();
        Choice[] Choices(PipeDimension[] rows) => MakeChoices(rows, isoAxes, textStrokes, symbols, geometry, fittings,execution);
        var options=groups.ToDictionary(g=>g[0].ChainId,Choices);
        foreach(var group in groups)
            if(options[group[0].ChainId].Length==0)
                throw new SheetCapacityException("Не найден размерный ряд с требуемым отступом от труб и арматуры и без прохода длинной выносной через параллельную трубу.",group[0].SourceId,conflict:LayoutConflict.DimensionClearance);
        var modelPoints=textStrokes.Where(stroke=>stroke.Role!="insulation").SelectMany(stroke=>stroke.Points).ToArray();
        var envelope=modelPoints.Length==0?new State(new(),0):
            new State(new(),0,modelPoints.Min(point=>point.X),modelPoints.Min(point=>point.Y),
                modelPoints.Max(point=>point.X),modelPoints.Max(point=>point.Y));
        var choiceBounds=options.Values.SelectMany(v=>v).ToDictionary(c=>c,c=>(Left:c.Segments.Min(s=>Math.Min(s.A.X,s.B.X)),Bottom:c.Segments.Min(s=>Math.Min(s.A.Y,s.B.Y)),Right:c.Segments.Max(s=>Math.Max(s.A.X,s.B.X)),Top:c.Segments.Max(s=>Math.Max(s.A.Y,s.B.Y))));
        var conflictCache=new Dictionary<(Choice,Choice),int>();
        int CachedConflicts(Choice a,Choice b)
        {
            var x=choiceBounds[a];var y=choiceBounds[b];
            // Nearly collinear ink uses a tolerance of 0.8 mm.
            const double tolerance=.81;
            if(x.Right+tolerance<y.Left||y.Right+tolerance<x.Left||
               x.Top+tolerance<y.Bottom||y.Top+tolerance<x.Bottom)return 0;
            if(conflictCache.TryGetValue((a,b),out int value))return value;
            value=Conflicts(a.Segments,b.Segments);
            conflictCache[(a,b)]=value;
            return value;
        }
        State? best=null;
        foreach(var order in new[]{groups.OrderByDescending(Extent).ToArray(),groups.OrderBy(Extent).ToArray(),groups.OrderByDescending(g=>g.Length).ToArray()})
        {
            var beam=new[]{envelope};
            foreach(var group in order)
            {
                execution?.Check();
                var expanded=new List<(State Parent,Choice Choice,double Cost,double Left,double Bottom,double Right,double Top)>();
                foreach(var state in beam)foreach(var choice in options[group[0].ChainId])
                {
                    int crossings=0;
                    foreach(var old in state.Choices)crossings+=CachedConflicts(old,choice);
                    var bounds=choiceBounds[choice];
                    double left=Math.Min(state.Left,bounds.Left),bottom=Math.Min(state.Bottom,bounds.Bottom),right=Math.Max(state.Right,bounds.Right),top=Math.Max(state.Top,bounds.Top);
                    double width=right-left,height=top-bottom;
                    double overflow=Math.Max(0,width-SheetComposition.Paper.Width)+Math.Max(0,height-SheetComposition.Paper.Height);
                    expanded.Add((state,choice,state.Cost+choice.Cost+crossings*1000000+overflow*10000,left,bottom,right,top));
                }
                beam=expanded.OrderBy(s=>s.Cost).Take(IsoAdvancedSettings.Current.DimensionBeamWidth).Select(s=>new State(s.Parent.Choices.Append(s.Choice).ToList(),s.Cost,s.Left,s.Bottom,s.Right,s.Top)).ToArray();
            }
            if(best is null||beam[0].Cost<best.Cost)best=beam[0];
        }

        // If beam search could not find a zero-crossing solution, use constraint search
        if(best is not null && best.Choices.Sum(c => best.Choices.Sum(c2 => c == c2 ? 0 : CachedConflicts(c, c2))) > 0)
        {
            var cleanOptions = groups.Select(g => {
                var cList = options[g[0].ChainId].Where(c => c.Cost < 1000000).ToArray();
                return cList.Length > 0 ? cList : options[g[0].ChainId].Take(6).ToArray();
            }).ToArray();
            // Sort chains by MRV (fewest choices first) to prune fast
            var chainOrder = Enumerable.Range(0, groups.Length).OrderBy(i => cleanOptions[i].Length).ToArray();
            var currentAssignment = new Choice?[groups.Length];
            Choice[]? zeroBest = null;
            double zeroBestCost = double.PositiveInfinity;
            int backtracks = 0;

            void Search(int stepIndex, double currentCost,State field)
            {
                if(backtracks%32==0)execution?.Check();
                if (zeroBest is not null || ++backtracks > IsoAdvancedSettings.Current.DimensionBacktracking) return;
                if (stepIndex == groups.Length)
                {
                    zeroBest = currentAssignment.Select(c => c!).ToArray();
                    zeroBestCost = currentCost;
                    return;
                }
                int chainIdx = chainOrder[stepIndex];
                foreach (var choice in cleanOptions[chainIdx])
                {
                    bool conflict = false;
                    for (int prev = 0; prev < stepIndex; prev++)
                    {
                        if (CachedConflicts(currentAssignment[chainOrder[prev]]!, choice) > 0)
                        {
                            conflict = true;
                            break;
                        }
                    }
                    if (conflict) continue;
                    var bounds=choiceBounds[choice];
                    var next=field with{Left=Math.Min(field.Left,bounds.Left),Bottom=Math.Min(field.Bottom,bounds.Bottom),
                        Right=Math.Max(field.Right,bounds.Right),Top=Math.Max(field.Top,bounds.Top)};
                    if(next.Right-next.Left>SheetComposition.Paper.Width||next.Top-next.Bottom>SheetComposition.Paper.Height)continue;
                    currentAssignment[chainIdx] = choice;
                    Search(stepIndex + 1, currentCost + choice.Cost,next);
                    currentAssignment[chainIdx] = null;
                    if (zeroBest is not null) return;
                }
            }

            Search(0, 0,envelope);
            if (zeroBest is not null)
            {
                best = new State(zeroBest.ToList(), zeroBestCost);
            }
        }
        var chosen=best!.Choices.SelectMany(c=>c.Rows).ToDictionary(d=>(d.ChainId,d.Role,d.Start,d.End));
        return input.Select(d=>chosen[(d.ChainId,d.Role,d.Start,d.End)]).ToArray();
    }
}
