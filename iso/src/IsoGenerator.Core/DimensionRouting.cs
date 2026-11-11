namespace IsoGenerator.Core;
// Route whole dimension chains together; never change engineering values or their model anchors.
public static class DimensionRouting
{
    readonly record struct Segment(SheetPoint A,SheetPoint B,bool Baseline);
    sealed record Choice(PipeDimension[] Rows,Segment[] Segments,double Cost);
    sealed record State(List<Choice> Choices,double Cost,double Left=double.PositiveInfinity,double Bottom=double.PositiveInfinity,double Right=double.NegativeInfinity,double Top=double.NegativeInfinity);
    static double Dot(SheetPoint a,SheetPoint b)=>a.X*b.X+a.Y*b.Y;
    static SheetPoint Sub(SheetPoint a,SheetPoint b)=>new(a.X-b.X,a.Y-b.Y);
    static SheetPoint Unit(SheetPoint v){double l=Math.Sqrt(Dot(v,v));return new(v.X/l,v.Y/l);}
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
    static Segment[] Segments(IEnumerable<PipeDimension> rows)=>rows.SelectMany(d=>{var (a,b)=Line(d);return new[]{new Segment(a,b,true),new Segment(d.Start,a,false),new Segment(d.End,b,false)};}).Distinct().ToArray();
    static int Conflicts(Segment[] a,Segment[] b)=>a.Sum(x=>b.Count(y=>Cross(x,y)));
    public static int CrossingCount(IReadOnlyList<PipeDimension> dimensions)
    {
        var groups=dimensions.GroupBy(d=>d.ChainId).Select(g=>Segments(g)).ToArray();int result=0;
        for(int i=0;i<groups.Length;i++)for(int j=i+1;j<groups.Length;j++)result+=Conflicts(groups[i],groups[j]);
        return result;
    }
    public static IReadOnlyList<PipeDimension> Route(IReadOnlyList<PipeDimension> input,IReadOnlyList<Stroke> strokes,IReadOnlyList<SymbolGlyph> symbols,int turns)
    {
        if(input.Count==0)return input;
        var geometry=strokes.Where(s=>s.Role is "pipe" or "elbow" or "branch" or "component" or "flange-neck" or "slope-triangle")
            .SelectMany(s=>s.Points.Zip(s.Points.Skip(1),(a,b)=>new Segment(a,b,true))).ToArray();
        bool crossesGeometry=input.Any(d=>{var (a,b)=Line(d);return geometry.Any(g=>Cross(new Segment(a,b,true),g));});
        if(CrossingCount(input)==0&&!crossesGeometry&&DimensionTextPlacement.CollisionCount(input,strokes)==0)return input;
        var groups=input.GroupBy(d=>d.ChainId).Select(g=>g.ToArray()).ToArray();
        double Extent(PipeDimension[] rows){var d=rows.FirstOrDefault(x=>x.Role=="overall")??rows[0];return Dot(Sub(d.End,d.Start),Sub(d.End,d.Start));}
        Choice[] Choices(PipeDimension[] rows)
        {
            var overall=rows.FirstOrDefault(d=>d.Role=="overall")??rows[0];var axis=Unit(Sub(overall.End,overall.Start));var n=new SheetPoint(-axis.Y,axis.X);
            if(Dot(n,overall.ExtensionDirection!.Value)<0)n=new(-n.X,-n.Y);
            bool hasChain=rows.Any(d=>d.Role=="chain");
            double current=rows.Min(d=>Math.Abs(Dot(Sub(d.DimensionLinePoint!.Value,overall.Start),n)));
            var exs=new List<SheetPoint>{overall.ExtensionDirection!.Value};
            // The plane is part of the dimension definition; route sides/lanes only.
            var source=rows.SelectMany(d=>d.SourceIds??new[]{d.SourceId}).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var contour=strokes.Where(s=>s.Role!="insulation"&&source.Contains(s.SourceId)).SelectMany(s=>s.Points).Concat(symbols.Where(g=>source.Contains(g.SourceId)).SelectMany(SymbolFootprint.Corners)).ToArray();
            var choices=new List<Choice>();
            foreach(int side in new[]{1,-1})
            {
                double oppositeEnvelope=contour.Select(p=>Dot(Sub(p,overall.Start),n)*side).Where(v=>v>=0&&v<=25).DefaultIfEmpty(0).Max();
                double first=side==1?Math.Max(10,current):10+oppositeEnvelope;
                foreach(var ex in exs)for(int lane=0;lane<=8;lane++)
                {
                    var placed=new List<PipeDimension>();
                    foreach(var d in rows)
                    {
                        double offset=side*(first+7*lane+(hasChain&&d.Role=="overall"?7:0));
                        var linePoint=new SheetPoint(overall.Start.X+n.X*offset,overall.Start.Y+n.Y*offset);
                        var moved=d with{DimensionLinePoint=linePoint,ExtensionDirection=ex};var (a,b)=Line(moved);
                        var ownAxis=Unit(Sub(d.End,d.Start));var readableNormal=new SheetPoint(-ownAxis.Y,ownAxis.X);
                        if(ownAxis.X<-.0001||(Math.Abs(ownAxis.X)<.0001&&ownAxis.Y<0))readableNormal=new(-readableNormal.X,-readableNormal.Y);
                        placed.Add(moved with{TextPosition=new((a.X+b.X)/2+readableNormal.X*2.8,(a.Y+b.Y)/2+readableNormal.Y*2.8)});
                    }
                    var lines=Segments(placed);int modelCross=lines.Where(l=>l.Baseline).Sum(l=>geometry.Count(g=>Cross(l,g)));
                    double cost=DimensionTextPlacement.CollisionCount(placed,strokes)*1000000+modelCross*5000+lane*7+first*.1+(side==-1?.5:0)+(!exs[0].Equals(ex)?1:0);
                    choices.Add(new(placed.ToArray(),lines,cost));
                }
            }
            return choices.OrderBy(c=>c.Cost).ToArray();
        }
        var options=groups.ToDictionary(g=>g[0].ChainId,Choices);
        var choiceBounds=options.Values.SelectMany(v=>v).ToDictionary(c=>c,c=>(Left:c.Segments.Min(s=>Math.Min(s.A.X,s.B.X)),Bottom:c.Segments.Min(s=>Math.Min(s.A.Y,s.B.Y)),Right:c.Segments.Max(s=>Math.Max(s.A.X,s.B.X)),Top:c.Segments.Max(s=>Math.Max(s.A.Y,s.B.Y))));
        var conflictCache=new Dictionary<(Choice,Choice),int>();
        int CachedConflicts(Choice a,Choice b){if(!conflictCache.TryGetValue((a,b),out int value)){value=Conflicts(a.Segments,b.Segments);conflictCache[(a,b)]=value;}return value;}
        State? best=null;
        foreach(var order in new[]{groups.OrderByDescending(Extent).ToArray(),groups.OrderBy(Extent).ToArray(),groups.OrderByDescending(g=>g.Length).ToArray()})
        {
            var beam=new[]{new State(new List<Choice>(),0.0)};
            foreach(var group in order)
            {
                PlanningExecution.Check();
                var expanded=new List<State>();
                foreach(var state in beam)foreach(var choice in options[group[0].ChainId])
                {
                    int crossings=state.Choices.Sum(old=>CachedConflicts(old,choice));
                    var bounds=choiceBounds[choice];
                    double left=Math.Min(state.Left,bounds.Left),bottom=Math.Min(state.Bottom,bounds.Bottom),right=Math.Max(state.Right,bounds.Right),top=Math.Max(state.Top,bounds.Top);
                    double width=right-left,height=top-bottom;
                    double overflow=Math.Max(0,width-SheetComposition.Paper.Width)+Math.Max(0,height-SheetComposition.Paper.Height);
                    expanded.Add(new(state.Choices.Append(choice).ToList(),state.Cost+choice.Cost+crossings*1000000+overflow*10000,left,bottom,right,top));
                }
                beam=expanded.OrderBy(s=>s.Cost).Take(24).ToArray();
            }
            if(best is null||beam[0].Cost<best.Cost)best=beam[0];
        }
        var chosen=best!.Choices.SelectMany(c=>c.Rows).ToDictionary(d=>(d.ChainId,d.Role,d.Start,d.End));
        return input.Select(d=>chosen[(d.ChainId,d.Role,d.Start,d.End)]).ToArray();
    }
}
