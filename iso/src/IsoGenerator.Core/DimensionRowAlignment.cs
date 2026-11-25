namespace IsoGenerator.Core;

public static class DimensionRowAlignment
{
    // Adjacent, nearly collinear runs share their chain and overall dimension lanes.
    // Parallel runs elsewhere on the sheet retain independent lanes.
    public static IReadOnlyDictionary<string,string> Families(IReadOnlyList<PipeDimension> input)
    {
        var representatives=input.GroupBy(d=>d.ChainId).Select(g=>g.FirstOrDefault(d=>d.Role=="overall")??g.First()).ToArray();
        var parent=Enumerable.Range(0,representatives.Length).ToArray();
        int Find(int i){while(parent[i]!=i)i=parent[i];return i;}
        for(int i=0;i<representatives.Length;i++)for(int j=i+1;j<representatives.Length;j++)
        {
            var a=representatives[i];var b=representatives[j];double dx=a.End.X-a.Start.X,dy=a.End.Y-a.Start.Y,l=Math.Sqrt(dx*dx+dy*dy);double ex=b.End.X-b.Start.X,ey=b.End.Y-b.Start.Y,m=Math.Sqrt(ex*ex+ey*ey);
            if(l<.1||m<.1||Math.Abs((dx*ex+dy*ey)/(l*m))<Math.Cos(Math.PI/360))continue;
            if(a.ExtensionDirection.HasValue&&b.ExtensionDirection.HasValue)
            {
                var ea=a.ExtensionDirection.Value;var eb=b.ExtensionDirection.Value;
                if(ea.X*eb.X+ea.Y*eb.Y<.5)continue;
            }
            double ux=dx/l,uy=dy/l;
            double Offset(SheetPoint p)=>-(p.X-a.Start.X)*uy+(p.Y-a.Start.Y)*ux;
            if(Math.Min(Math.Abs(Offset(b.Start)),Math.Abs(Offset(b.End)))>3)continue;
            double t1=(b.Start.X-a.Start.X)*ux+(b.Start.Y-a.Start.Y)*uy,t2=(b.End.X-a.Start.X)*ux+(b.End.Y-a.Start.Y)*uy;
            if(Math.Max(t1,t2)<-20||Math.Min(t1,t2)>l+20)continue;
            parent[Find(j)]=Find(i);
        }
        return representatives.Select((d,i)=>(d.ChainId,Family:representatives[Find(i)].ChainId)).ToDictionary(p=>p.ChainId,p=>p.Family);
    }
    public static IReadOnlyList<PipeDimension> EnforceChainOrder(IReadOnlyList<PipeDimension> input)
    {
        var result=input.ToArray();
        foreach(var group in Enumerable.Range(0,result.Length).GroupBy(i=>result[i].ChainId))
        {
            var chain=group.Where(i=>result[i].Role=="chain").ToArray();if(chain.Length==0)continue;
            foreach(int index in group.Where(i=>result[i].Role=="overall"))
            {
                var overall=result[index];double dx=overall.End.X-overall.Start.X,dy=overall.End.Y-overall.Start.Y,length=Math.Sqrt(dx*dx+dy*dy);if(length<1e-8)continue;
                var n=new SheetPoint(-dy/length,dx/length);
                double Offset(PipeDimension d)=>(d.DimensionLinePoint!.Value.X-overall.Start.X)*n.X+(d.DimensionLinePoint.Value.Y-overall.Start.Y)*n.Y;
                double side=Math.Sign(Offset(result[chain[0]]));if(side==0)continue;
                double target=side*Math.Max(chain.Max(i=>side*Offset(result[i]))+IsoAdvancedSettings.Current.DimensionRowStep,side*Offset(overall));
                var ex=overall.ExtensionDirection!.Value;double dot=ex.X*n.X+ex.Y*n.Y;if(Math.Abs(dot)<1e-8)continue;
                double shift=(target-Offset(overall))/dot;var line=overall.DimensionLinePoint!.Value;
                result[index]=overall with{DimensionLinePoint=new(line.X+ex.X*shift,line.Y+ex.Y*shift),TextPosition=overall.TextPosition is {} text?new(text.X+ex.X*shift,text.Y+ex.Y*shift):null};
            }
        }
        return result;
    }

    public static IReadOnlyList<PipeDimension> AlignAdjacent(IReadOnlyList<PipeDimension> input,IReadOnlyList<Stroke>? strokes=null,IReadOnlyList<SymbolGlyph>? symbols=null)
    {
        var groups=input.GroupBy(d=>d.ChainId).ToArray();var parent=Enumerable.Range(0,groups.Length).ToArray();
        int Find(int i){while(parent[i]!=i)i=parent[i];return i;}
        var links=new List<(int A,int B,double Gap)>();
        bool Same(ModelPoint? a,ModelPoint? b,SheetPoint p,SheetPoint q)=>a is {} x&&b is {} y&&x.DistanceTo(y)<.001&&Math.Sqrt(Math.Pow(p.X-q.X,2)+Math.Pow(p.Y-q.Y,2))<.01;
        for(int i=0;i<groups.Length;i++)for(int j=i+1;j<groups.Length;j++)
        {
            var a=groups[i].FirstOrDefault(d=>d.Role=="overall")??groups[i].First();var b=groups[j].FirstOrDefault(d=>d.Role=="overall")??groups[j].First();
            if(a.ExtensionDirection is not {} ea||b.ExtensionDirection is not {} eb||ea.X*eb.X+ea.Y*eb.Y<.999)continue;
            // Routing has already selected the clearer side of each run. Adjacent
            // rows on opposite sides must not be pulled through the pipe to align.
            var aChain=groups[i].FirstOrDefault(d=>d.Role=="chain")??a;
            var bChain=groups[j].FirstOrDefault(d=>d.Role=="chain")??b;
            if(Distance(aChain)*Distance(bChain)<=0)continue;
            if(Same(a.ModelStart,b.ModelStart,a.Start,b.Start)||Same(a.ModelStart,b.ModelEnd,a.Start,b.End)||Same(a.ModelEnd,b.ModelStart,a.End,b.Start)||Same(a.ModelEnd,b.ModelEnd,a.End,b.End))
            {
                links.Add((i,j,Math.Abs(Distance(aChain)-Distance(bChain))));
                parent[Find(j)]=Find(i);
            }
        }
        var result=input.ToArray();var index=groups.Select((g,i)=>(g.Key,i)).ToDictionary(p=>p.Key,p=>p.i);
        var fittings=symbols??Array.Empty<SymbolGlyph>();
        double Distance(PipeDimension d)
        {
            var ex=d.ExtensionDirection!.Value;double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y;
            return ((d.DimensionLinePoint!.Value.X-d.Start.X)*(-dy)+(d.DimensionLinePoint.Value.Y-d.Start.Y)*dx)/(-dy*ex.X+dx*ex.Y);
        }
        bool TryAlign(IReadOnlyCollection<int> members)
        {
            var trial=result.ToArray();
            foreach(var role in Enumerable.Range(0,result.Length)
                .Where(i=>members.Contains(index[result[i].ChainId])).GroupBy(i=>result[i].Role))
            {
                double target=role.Select(i=>Distance(result[i])).MaxBy(Math.Abs);
                foreach(int i in role)
                {
                    var d=result[i];double delta=target-Distance(d);var ex=d.ExtensionDirection!.Value;var line=d.DimensionLinePoint!.Value;
                    trial[i]=d with{DimensionLinePoint=new(line.X+ex.X*delta,line.Y+ex.Y*delta),TextPosition=d.TextPosition is {} text?new(text.X+ex.X*delta,text.Y+ex.Y*delta):null};
                }
            }
            if(strokes is not null)
            {
                var oldConflicts=DimensionRouting.AlignmentConflicts(result,strokes,fittings);
                var newConflicts=DimensionRouting.AlignmentConflicts(trial,strokes,fittings);
                if(newConflicts.Text>oldConflicts.Text||newConflicts.Crossings>oldConflicts.Crossings||
                   !DimensionRouting.FitsFittingClearance(trial,fittings)||
                   DimensionRouting.PipeClearanceViolations(trial,strokes)>DimensionRouting.PipeClearanceViolations(result,strokes)||
                   (DimensionRouting.LongWitnessThroughParallelRun(trial,strokes) is not null&&
                    DimensionRouting.LongWitnessThroughParallelRun(result,strokes) is null))return false;
            }
            result=trial;
            return true;
        }
        foreach(var family in Enumerable.Range(0,groups.Length).GroupBy(Find).Where(g=>g.Count()>1))
        {
            var members=family.ToArray();
            if(TryAlign(members)||members.Length==2)continue;

            // If one long witness blocks the family, keep the nearest safe joins.
            var connected=members.ToDictionary(i=>i,i=>new HashSet<int>{i});
            foreach(var link in links.Where(link=>Find(link.A)==family.Key).OrderBy(link=>link.Gap))
            {
                var first=connected[link.A];var second=connected[link.B];
                if(ReferenceEquals(first,second))continue;
                var joined=first.Concat(second).ToHashSet();
                if(TryAlign(joined))foreach(int member in joined)connected[member]=joined;
            }
        }
        return result;
    }

    public static IReadOnlyList<PipeDimension> Align(IReadOnlyList<PipeDimension> input)
    {
        var families=Families(input);var result=input.ToArray();
        foreach(var group in Enumerable.Range(0,input.Count).GroupBy(i=>families[input[i].ChainId]))
        {
            var indices=group.ToArray();if(indices.Select(i=>input[i].ChainId).Distinct().Count()<2)continue;
            var reference=indices.Select(i=>input[i]).OrderByDescending(d=>Math.Pow(d.End.X-d.Start.X,2)+Math.Pow(d.End.Y-d.Start.Y,2)).First();
            double dx=reference.End.X-reference.Start.X,dy=reference.End.Y-reference.Start.Y,l=Math.Sqrt(dx*dx+dy*dy);
            var normal=new SheetPoint(-dy/l,dx/l);
            var refEx=reference.ExtensionDirection!.Value;
            if(normal.X*refEx.X+normal.Y*refEx.Y<0)normal=new(-normal.X,-normal.Y);
            double Offset(PipeDimension d){var p=d.DimensionLinePoint!.Value;return (p.X-d.Start.X)*normal.X+(p.Y-d.Start.Y)*normal.Y;}
            bool hasChain=indices.Any(i=>input[i].Role=="chain");
            double inner=indices.Where(i=>input[i].Role=="chain"||!hasChain).Select(i=>Offset(input[i])).DefaultIfEmpty(double.NegativeInfinity).Max();
            double outer=Math.Max(inner+(hasChain?IsoAdvancedSettings.Current.DimensionRowStep:0),indices.Where(i=>input[i].Role=="overall").Select(i=>Offset(input[i])).DefaultIfEmpty(inner).Max());
            foreach(int i in indices)
            {
                var d=input[i];var p=d.DimensionLinePoint!.Value;double target=d.Role=="overall"?outer:inner;
                var ex=d.ExtensionDirection!.Value;double dot=ex.X*normal.X+ex.Y*normal.Y;if(Math.Abs(dot)<1e-8)continue;
                double t=(target-Offset(d))/dot;
                result[i]=d with{DimensionLinePoint=new(p.X+ex.X*t,p.Y+ex.Y*t),TextPosition=d.TextPosition.HasValue?new(d.TextPosition.Value.X+ex.X*t,d.TextPosition.Value.Y+ex.Y*t):null};
            }
        }
        return result;
    }
}
