namespace IsoGenerator.Core;

internal static class SheetSpaceOptimizer
{
    public static SheetScene Fill(PipelineSnapshot snapshot,SheetScene baseline,
        Func<int,double,IReadOnlyDictionary<string,double>?,double?,SheetScene> rebuild,PipelineTopology topology,IsoSymbolMap? symbolMap,int quarterTurns,PlanningExecution execution,Func<PipeValveOverlapException,string[]> overlapRepairs)
    {
        var best=baseline;
        var valves=snapshot.Parts.Where(part=>BomCategories.Classify(part)==BomCategory.Valves)
            .Select(part=>part.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pipes=snapshot.Parts.Where(part=>part.Component==ComponentKind.Pipe&&!topology.CappedVentDrainPipes.Contains(part.Handle))
            .Select(part=>part.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var risers=baseline.Dimensions.Where(d=>d.Role=="chain"&&pipes.Contains(d.SourceId)&&
                Math.Abs(d.End.Y-d.Start.Y)>5*Math.Abs(d.End.X-d.Start.X)&&
                d.SourceIds?.Any(valves.Contains)==true)
            .OrderBy(d=>Math.Abs(d.End.Y-d.Start.Y)).Select(d=>d.SourceId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();

        var bestQuality=LayoutQuality.Dimensions(best);
        var bestLeaderQuality=LayoutQuality.Leaders(best);
        double? fixedScale=baseline.GeometryScale;
        if(fixedScale is null&&pipes.Count>0&&execution.CanTryOptionalVariant())
        {
            try
            {
                fixedScale=new SchematicLayout(snapshot,topology,quarterTurns,baseline.LayoutLevel,
                    baseline.PipeExtensions,symbolMap,baseline.PaperMargin,execution:execution).BaseScale;
            }
            catch(PlanningLimitException){return best;}
            catch(SheetCapacityException){}
        }
        foreach(var pipe in risers)
        foreach(double extra in new[]{20d,30d,40d,50d,60d,70d})
        {
            if(!execution.CanTryOptionalVariant())return best;
            var extensions=new Dictionary<string,double>(
                baseline.PipeExtensions??new Dictionary<string,double>(),StringComparer.OrdinalIgnoreCase);
            extensions[pipe]=extensions.GetValueOrDefault(pipe)+extra;
            Evaluate(baseline.LayoutLevel,baseline.PaperMargin,extensions,pipe,fixedScale);
        }
        foreach(var trial in new[]{
            (best.LayoutLevel,best.PaperMargin-20),
            (best.LayoutLevel,best.PaperMargin-40),
            (1,best.PaperMargin),
            (0,best.PaperMargin)})
        {
            if(!execution.CanTryOptionalVariant())break;
            if(trial.Item1==best.LayoutLevel&&Math.Abs(trial.Item2-best.PaperMargin)<1e-6&&best.GeometryScale is null)
                continue;
            Evaluate(trial.Item1,trial.Item2,best.PipeExtensions);
        }

        var drawingBounds=SheetComposition.TryBounds(best)??default;
        if(fixedScale is >0&&drawingBounds.Width>SheetComposition.Paper.Width*.8&&
           drawingBounds.Height<SheetComposition.Paper.Height*.8)
        {
            // Uniform growth quickly runs out of width on a long, shallow drawing.
            // Try giving the free height to steep pipes, including one trial funded by a long run.
            var growthBase=best;
            var runs=growthBase.Strokes.Where(stroke=>stroke.Role=="pipe"&&stroke.Points.Count>=2&&pipes.Contains(stroke.SourceId))
                .GroupBy(stroke=>stroke.SourceId,StringComparer.OrdinalIgnoreCase).Select(group=>
                {
                    double x=group.Sum(stroke=>Math.Abs(stroke.Points[^1].X-stroke.Points[0].X));
                    double y=group.Sum(stroke=>Math.Abs(stroke.Points[^1].Y-stroke.Points[0].Y));
                    return (Pipe:group.Key,X:x,Y:y);
                }).ToArray();
            var donor=runs.Where(run=>run.X>run.Y*1.3&&run.X>=45)
                .OrderByDescending(run=>run.X).FirstOrDefault();
            foreach(var run in runs.Where(run=>run.Y>run.X*2.5&&run.Y>=10)
                .OrderByDescending(run=>run.Y).Take(2))
            foreach(double extra in new[]{30d,60d,90d})
            {
                if(!execution.CanTryOptionalVariant())return best;
                var extensions=new Dictionary<string,double>(growthBase.PipeExtensions??
                    new Dictionary<string,double>(),StringComparer.OrdinalIgnoreCase);
                extensions[run.Pipe]=extensions.GetValueOrDefault(run.Pipe)+extra;
                Evaluate(growthBase.LayoutLevel,growthBase.PaperMargin,extensions,scaleOverride:fixedScale);
                if(extra==60&&donor.Pipe is not null&&execution.CanTryOptionalVariant())
                {
                    var redistributed=new Dictionary<string,double>(extensions,StringComparer.OrdinalIgnoreCase)
                    {
                        [donor.Pipe]=extensions.GetValueOrDefault(donor.Pipe)-20
                    };
                    Evaluate(growthBase.LayoutLevel,growthBase.PaperMargin,redistributed,scaleOverride:fixedScale);
                }
            }
            drawingBounds=SheetComposition.Bounds(best);
        }
        bool freeField=drawingBounds.Width<SheetComposition.Paper.Width*.9&&drawingBounds.Height<SheetComposition.Paper.Height*.9;
        if(best.LayoutLevel==3&&freeField)
        {
            var growthBase=best;
            var longRuns=best.Strokes.Where(stroke=>stroke.Role=="pipe"&&stroke.Points.Count>=2&&pipes.Contains(stroke.SourceId))
                .GroupBy(stroke=>stroke.SourceId,StringComparer.OrdinalIgnoreCase)
                .Select(group=>(Pipe:group.Key,Length:group.Sum(stroke=>
                    Math.Sqrt(Math.Pow(stroke.Points[^1].X-stroke.Points[0].X,2)+
                              Math.Pow(stroke.Points[^1].Y-stroke.Points[0].Y,2)))))
                .OrderByDescending(run=>run.Length).Take(2).ToArray();
            foreach(var run in longRuns)
            foreach(double extra in new[]{20d,40d,60d})
            {
                if(!execution.CanTryOptionalVariant())return best;
                var extensions=new Dictionary<string,double>(
                    growthBase.PipeExtensions??new Dictionary<string,double>(),StringComparer.OrdinalIgnoreCase);
                extensions[run.Pipe]=extensions.GetValueOrDefault(run.Pipe)+extra;
                Evaluate(growthBase.LayoutLevel,growthBase.PaperMargin,extensions);
            }
        }
        if(fixedScale is >0&&freeField)
        {
            var growthBase=best;
            foreach(double factor in new[]{1.15,1.3,1.45})
            {
                if(!execution.CanTryOptionalVariant())break;
                Evaluate(growthBase.LayoutLevel,growthBase.PaperMargin,growthBase.PipeExtensions,
                    scaleOverride:fixedScale.Value*factor);
            }
        }

        void Evaluate(int level,double margin,IReadOnlyDictionary<string,double>? extensions,string? localPipe=null,double? scaleOverride=null,bool repairOverlap=true)
        {
            try
            {
                execution.Report("Проверка заполнения рабочего поля",variant:true);
                var candidate=SheetComposition.Center(rebuild(level,margin,extensions,scaleOverride));
                if(DimensionCongestion.PipeToExtend(snapshot,candidate) is {} conflictPipe)
                {
                    execution.Reject(new SheetCapacityException("После удлинения сохраняется теснота размерных рядов.",
                        conflictPipe,conflict:LayoutConflict.DimensionCongestion),snapshot.Parts.Count,level,margin,extensions,scaleOverride);
                    if(localPipe is not null&&Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1")
                        Console.Error.WriteLine($"Стояк {localPipe}, добавка {extensions?.GetValueOrDefault(localPipe):F0} мм: остаётся теснота у {conflictPipe}.");
                    return;
                }
                var quality=LayoutQuality.Dimensions(candidate);
                int priority=quality.CompareTo(bestQuality);
                if(Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1"&&localPipe is not null)
                    Console.Error.WriteLine($"Сравнение стояка {localPipe}: {bestQuality} -> {quality}.");
                var leaderQuality=LayoutQuality.Leaders(candidate);
                int leaderPriority=leaderQuality.CompareTo(bestLeaderQuality);
                if(priority<0||priority==0&&(leaderPriority<0||
                   leaderPriority==0&&LayoutQuality.Footprint(candidate)>LayoutQuality.Footprint(best)*1.02))
                {best=candidate;bestQuality=quality;bestLeaderQuality=leaderQuality;}
            }
            catch(PlanningLimitException){}
            catch(SheetCapacityException ex)
            {
                execution.Reject(ex,snapshot.Parts.Count,level,margin,extensions,scaleOverride);
                if(repairOverlap&&ex is PipeValveOverlapException overlap)
                {
                    string? pipe=overlapRepairs(overlap).FirstOrDefault();
                    if(pipe is not null)
                    {
                        double repairScale;
                        try
                        {
                            repairScale=scaleOverride??new SchematicLayout(snapshot,topology,quarterTurns,level,
                                extensions,symbolMap,margin,execution:execution).BaseScale;
                        }
                        catch(PlanningLimitException){return;}
                        catch(SheetCapacityException){return;}
                        foreach(int steps in new[]{2,4,6,8})
                        {
                            if(!execution.CanTryOptionalVariant())break;
                            var adjusted=new Dictionary<string,double>(extensions??new Dictionary<string,double>(),StringComparer.OrdinalIgnoreCase);
                            adjusted[pipe]=adjusted.GetValueOrDefault(pipe)+steps*IsoAdvancedSettings.Current.PipeAdjustment;
                            Evaluate(level,margin,adjusted,scaleOverride:repairScale,repairOverlap:false);
                            if(steps<=4&&overlap.SourceId is {} crossing&&crossing!=pipe&&execution.CanTryOptionalVariant())
                            {
                                var redistributed=new Dictionary<string,double>(adjusted,StringComparer.OrdinalIgnoreCase)
                                {
                                    [crossing]=adjusted.GetValueOrDefault(crossing)-steps*IsoAdvancedSettings.Current.PipeAdjustment
                                };
                                Evaluate(level,margin,redistributed,scaleOverride:repairScale,repairOverlap:false);
                            }
                        }
                    }
                }
                if(localPipe is not null&&Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1")
                    Console.Error.WriteLine($"Стояк {localPipe}, добавка {extensions?.GetValueOrDefault(localPipe):F0} мм: {ex.Message}");
            }
        }
        return best;
    }
}
