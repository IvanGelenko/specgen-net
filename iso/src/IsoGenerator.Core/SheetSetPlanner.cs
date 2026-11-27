namespace IsoGenerator.Core;

public enum LayoutConflict
{
    Capacity,
    PaperBounds,
    DimensionClearance,
    DimensionText,
    Annotation,
    AirVentClearance,
    DimensionCongestion,
    ComponentCollision
}

public class SheetCapacityException(string message,string? sourceId=null,ModelPoint? modelStart=null,
    ModelPoint? modelEnd=null,double paperWidthOverflow=0,double paperHeightOverflow=0,
    LayoutConflict conflict=LayoutConflict.Capacity) : IOException(message)
{
    public LayoutConflict Conflict {get;}=conflict;
    public double PaperWidthOverflow {get;}=paperWidthOverflow;
    public double PaperHeightOverflow {get;}=paperHeightOverflow;
    public string? SourceId {get;}=sourceId;
    public ModelPoint? ModelStart {get;}=modelStart;
    public ModelPoint? ModelEnd {get;}=modelEnd;
}
public sealed record SheetContinuation(string Handle,string Port,string Mark,int OtherSheet);
public sealed record PlannedSheet(int Number,int Total,PipelineSnapshot Snapshot,SheetScene Scene,SheetScene? BaseScene=null);

public static class SheetSetPlanner
{
    public static IReadOnlyList<PlannedSheet> Build(PipelineSnapshot source,IsoSymbolMap? symbolMap=null,int quarterTurns=0,bool excludeFlangeHardware=false,CancellationToken cancellationToken=default,IProgress<PlanningProgress>? progress=null,bool ignoreBreakPoints=false,bool instrumentCalloutsOnly=true,RememberedLayout? remembered=null,Action<RememberedLayout>? remember=null,TimeSpan? planningTimeLimit=null,double minimumPipeLength=10,IsoAdvancedSettings? advancedSettings=null)
    {
        using var settingsScope=IsoAdvancedSettings.Use(advancedSettings);
        var settings=IsoAdvancedSettings.Current;
        var execution=new PlanningExecution(cancellationToken,progress,
            planningTimeLimit??TimeSpan.FromSeconds(settings.ViewTimeSeconds),settings.PlanningVariants,minimumPipeLength);
        execution.Report("Проверка исходных данных");
        source=SnapshotPreparation.ForSheetPlanning(source,ignoreBreakPoints,instrumentCalloutsOnly,execution);
        var validation=SnapshotValidation.Validate(source);
        if(validation.Errors.Count>0)throw new InvalidDataException(string.Join("\n",validation.Errors));
        var valveKitExclusions=excludeFlangeHardware?ValveKitBomPolicy.Exclusions(source):null;
        var cappedVentDrainPipes=VentDrainPipePolicy.Find(source);
        var prepared=new System.Runtime.CompilerServices.ConditionalWeakTable<PipelineSnapshot,PreparedSheetInput>();
        PreparedSheetInput Prepare(PipelineSnapshot snapshot)=>prepared.GetValue(snapshot,
            fragment=>PreparedSheetInput.Create(fragment,ReferenceEquals(fragment,source)?validation:null,valveKitExclusions,cappedVentDrainPipes));
        // Use the same density search for the whole line and every candidate fragment.
        // A rejected spacious layout is not proof that a fragment needs another sheet.
        var densityOrder=new[]{1,2,4,5,0,3};
        var geometryOrder=new[]{(Level:1,Margin:0d),(Level:2,Margin:0d),(Level:0,Margin:40d),
            (Level:4,Margin:0d),(Level:5,Margin:0d),(Level:0,Margin:0d),(Level:3,Margin:0d)};
        var plans=new Dictionary<string,SheetScene>(StringComparer.Ordinal);
        var rejectedPlans=new HashSet<string>(StringComparer.Ordinal);
        var manualSections=new HashSet<string>(StringComparer.Ordinal);
        var requiredVentExtensions=new Dictionary<string,Dictionary<string,double>>(StringComparer.Ordinal);
        string Key(PipelineSnapshot snapshot)=>string.Join("|",snapshot.Parts.Select(p=>p.Handle).OrderBy(h=>h,StringComparer.Ordinal));
        string? NearestPipe(PipelineSnapshot snapshot,string? sourceId)
        {
            if(string.IsNullOrEmpty(sourceId))return null;
            var parts=snapshot.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase){sourceId};var frontier=new[]{sourceId};
            for(int depth=0;depth<=10;depth++)
            {
                var pipes=frontier.Where(h=>parts.TryGetValue(h,out var p)&&p.Component==ComponentKind.Pipe&&!cappedVentDrainPipes.Contains(h))
                    .OrderBy(h=>{var ports=parts[h].Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();return ports.Length==2?ports[0].Position.DistanceTo(ports[1].Position):double.MaxValue;}).ThenBy(h=>h,StringComparer.Ordinal).ToArray();
                if(pipes.Length>0)return pipes[0];
                var current=frontier.ToHashSet(StringComparer.OrdinalIgnoreCase);
                frontier=snapshot.Connections.SelectMany(c=>current.Contains(c.FirstHandle)?new[]{c.SecondHandle}:current.Contains(c.SecondHandle)?new[]{c.FirstHandle}:Array.Empty<string>()).Where(seen.Add).ToArray();
            }
            return null;
        }
        string[] TransverseRepairs(PipelineSnapshot snapshot,string crossingPipe,string lowerPipe,bool preferNearCrossing=false)
        {
            var byHandle=snapshot.Parts.ToDictionary(part=>part.Handle,StringComparer.OrdinalIgnoreCase);
            var adjacency=byHandle.Keys.ToDictionary(handle=>handle,_=>new List<string>(),StringComparer.OrdinalIgnoreCase);
            foreach(var connection in snapshot.Connections)
            {
                if(adjacency.TryGetValue(connection.FirstHandle,out var first)&&
                   adjacency.TryGetValue(connection.SecondHandle,out var last))
                {
                    first.Add(connection.SecondHandle);
                    last.Add(connection.FirstHandle);
                }
            }
            var queue=new Queue<string>();
            queue.Enqueue(crossingPipe);
            var parent=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase){{crossingPipe,null}};
            while(queue.TryDequeue(out var handle)&&!parent.ContainsKey(lowerPipe))
                foreach(string next in adjacency[handle])
                    if(parent.TryAdd(next,handle))queue.Enqueue(next);
            var path=new List<string>();
            if(parent.ContainsKey(lowerPipe))
                for(string? handle=lowerPipe;handle is not null;handle=parent[handle])path.Add(handle);
            SheetPoint Axis(Part part)
            {
                var ends=part.Ports.Where(port=>port.Name is "S1" or "S2").ToArray();
                if(ends.Length!=2)return default;
                return IsometricProjection.Project(ends[1].Position,ends[0].Position,quarterTurns);
            }
            var axis=Axis(byHandle[crossingPipe]);
            double size=Math.Sqrt(axis.X*axis.X+axis.Y*axis.Y);
            return path.Where(handle=>handle!=crossingPipe&&handle!=lowerPipe&&
                byHandle[handle].Component==ComponentKind.Pipe&&!cappedVentDrainPipes.Contains(handle))
                .Select(handle=>
                {
                    var candidate=Axis(byHandle[handle]);
                    double length=Math.Sqrt(candidate.X*candidate.X+candidate.Y*candidate.Y);
                    return (Handle:handle,Angle:Math.Abs(candidate.X*axis.Y-candidate.Y*axis.X)/Math.Max(.001,length*size));
                })
                .Where(candidate=>candidate.Angle>.3)
                .OrderByDescending(candidate=>preferNearCrossing?Math.Round(candidate.Angle,1):candidate.Angle)
                .ThenByDescending(candidate=>preferNearCrossing?path.IndexOf(candidate.Handle):0).ThenBy(candidate=>candidate.Handle,StringComparer.Ordinal)
                .Select(candidate=>candidate.Handle).Take(4)
                .Concat(cappedVentDrainPipes.Contains(crossingPipe)?Array.Empty<string>():new[]{crossingPipe}).ToArray();
        }
        SheetScene Plan(PipelineSnapshot snapshot,bool quick=false)
        {
            execution.Check();
            var included=snapshot.Parts.Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if((source.RequiredCuts??Array.Empty<Connection>()).Any(c=>included.Contains(c.FirstHandle)&&included.Contains(c.SecondHandle)))throw new SheetCapacityException("На модели задана точка разрыва между листами.");
            if(SheetConnectivity.Components(snapshot).Count>1)throw new SheetCapacityException("На листе допускается только один связный фрагмент трубопровода.");
            string key=Key(snapshot);if(plans.TryGetValue(key,out var cached))return cached;
            if(rejectedPlans.Contains(key))throw new SheetCapacityException("Проверенные варианты компоновки не поместились.");
            SheetCapacityException? failure=null;
            SheetScene? manualBaseline=null;
            foreach(var (level,initialMargin) in quick?geometryOrder.Take(3):geometryOrder)
            {
                var extension=requiredVentExtensions.TryGetValue(key,out var required)
                    ?new Dictionary<string,double>(required,StringComparer.OrdinalIgnoreCase)
                    :new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
                double paperMargin=initialMargin;
                try
                {
                    var congestionRetries=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
                    for(int iteration=0;iteration<(quick?3:12);iteration++)
                    {
                        execution.Report($"Компоновка фрагмента: {snapshot.Parts.Count} объектов",variant:true);
                        SheetScene scene,centered;
                        try{scene=SheetPlanner.Build(Prepare(snapshot),instrumentCalloutsOnly:instrumentCalloutsOnly,quarterTurns:quarterTurns,excludeFlangeHardware:excludeFlangeHardware,symbolMap:symbolMap,compactLevel:level,pipeExtensions:extension,paperMargin:paperMargin,execution:execution);centered=SheetComposition.Center(scene);}
                        catch(AirVentSpacingException ex)
                        {
                            execution.Reject(ex,snapshot.Parts.Count,level,paperMargin,extension);
                            if(Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1")Console.Error.WriteLine($"VENT parts={snapshot.Parts.Count} level={level} neck={ex.Pipe} extra={ex.Extra:F1}");
                            extension[ex.Pipe]=extension.GetValueOrDefault(ex.Pipe)+ex.Extra;continue;
                        }
                        catch(SheetCapacityException ex) when(ex.PaperWidthOverflow>0||ex.PaperHeightOverflow>0)
                        {
                            execution.Reject(ex,snapshot.Parts.Count,level,paperMargin,extension);
                            paperMargin+=Math.Ceiling(Math.Max(ex.PaperWidthOverflow,ex.PaperHeightOverflow))+1;failure=ex;continue;
                        }
                        catch(SheetCapacityException ex) when(ex.SourceId is null&&(paperMargin>0||extension.Count>0))
                        {
                            execution.Reject(ex,snapshot.Parts.Count,level,paperMargin,extension);
                            // Changing pipe spacing can alter routing; try another bounded layout clearance.
                            paperMargin+=7;failure=ex;continue;
                        }
                        catch(SheetCapacityException ex) when(ex.SourceId is not null)
                        {
                            if((Environment.GetEnvironmentVariable("ISO_LAYOUT_TRACE")=="1"||Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1"))Console.Error.WriteLine($"parts={snapshot.Parts.Count}, density={level}, extension={iteration}, source={ex.SourceId}: {ex.Message}");
                            string? other=ex switch
                            {
                                PipeSupportCrossingException collision=>collision.LowerPipe,
                                PipeValveOverlapException overlap=>overlap.Valve,
                                PipeProjectionOverlapException overlap=>overlap.OtherPipe,
                                _=>null
                            };
                            string? local=other is not null&&other!=ex.SourceId
                                ?TransverseRepairs(snapshot,ex.SourceId,other,ex is PipeValveOverlapException or PipeProjectionOverlapException)
                                    .FirstOrDefault(handle=>congestionRetries.GetValueOrDefault(handle)<(ex is PipeValveOverlapException?4:2))
                                :NearestPipe(snapshot,ex.SourceId);
                            if(local is null)throw;
                            execution.Reject(ex,snapshot.Parts.Count,level,paperMargin,extension);
                            string extensionKey=local;
                            if(ex.ModelStart is {} start&&ex.ModelEnd is {} end)
                            {
                                var graph=Prepare(snapshot).Topology;double dx=end.X-start.X,dy=end.Y-start.Y,dz=end.Z-start.Z,l2=dx*dx+dy*dy+dz*dz;
                                var edge=graph.Edges.Where(e=>e.Kind==ComponentKind.Pipe&&e.Source==local).FirstOrDefault(e=>
                                {
                                    var a=graph.Nodes[e.A].Point;var b=graph.Nodes[e.B].Point;var mid=new ModelPoint((a.X+b.X)/2,(a.Y+b.Y)/2,(a.Z+b.Z)/2);
                                    double t=((mid.X-start.X)*dx+(mid.Y-start.Y)*dy+(mid.Z-start.Z)*dz)/Math.Max(l2,1e-12);
                                    return t>=-.001&&t<=1.001&&mid.DistanceTo(new(start.X+dx*t,start.Y+dy*t,start.Z+dz*t))<=1;
                                });
                                if(edge is not null)extensionKey=SchematicLayout.PipeEdgeKey(edge.Source,graph.Nodes[edge.A].Point,graph.Nodes[edge.B].Point);
                            }
                            int localRetries=congestionRetries.GetValueOrDefault(extensionKey);
                            if(localRetries>=4){failure=ex;break;}
                            congestionRetries[extensionKey]=localRetries+1;
                            extension[extensionKey]=extension.GetValueOrDefault(extensionKey)+10;failure=ex;continue;
                        }
                        string? pipe=DimensionCongestion.PipeToExtend(snapshot,centered);
                        if(pipe is null){plans[key]=scene;return scene;}
                        if(!quick&&manualSections.Contains(key)&&manualBaseline is null&&
                           DimensionCongestion.FindRepair(snapshot,centered,includeProximity:false) is null)
                        {
                            manualBaseline=scene;
                            if(Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1")
                                Console.Error.WriteLine($"Ручной участок: пригодно с локальной теснотой {pipe}.");
                        }
                        var congestion=new SheetCapacityException("Сохраняется теснота размерных рядов.",
                            pipe,conflict:LayoutConflict.DimensionCongestion);
                        execution.Reject(congestion,snapshot.Parts.Count,level,paperMargin,extension);
                        if(cappedVentDrainPipes.Contains(pipe))
                        {
                            pipe=NearestPipe(snapshot,pipe);
                            if(pipe is null){failure=congestion;break;}
                        }
                        if(Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1")Console.Error.WriteLine($"CONGESTION parts={snapshot.Parts.Count} level={level} iteration={iteration} pipe={pipe}");
                        // After repeated failures on the same pipe, try another density
                        // instead of spending the whole local budget on one repair.
                        int retries=congestionRetries.GetValueOrDefault(pipe);
                        if(retries>=3)break;
                        congestionRetries[pipe]=retries+1;
                        double adjustment=IsoAdvancedSettings.Current.PipeAdjustment;
                        var runs=centered.Strokes.Where(stroke=>stroke.Role=="pipe"&&stroke.Points.Count>=2)
                            .GroupBy(stroke=>stroke.SourceId,StringComparer.OrdinalIgnoreCase)
                            .Select(group=>(Handle:group.Key,Length:group.Sum(stroke=>
                            {
                                var a=stroke.Points[0];var b=stroke.Points[^1];
                                return Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Y-a.Y,2));
                            }))).ToArray();
                        double median=runs.Select(run=>run.Length).OrderBy(length=>length).ElementAtOrDefault(runs.Length/2);
                        var donor=runs.Where(run=>run.Handle!=pipe&&extension.GetValueOrDefault(run.Handle)<=0&&run.Length>=Math.Max(4*adjustment,2*median))
                            .OrderByDescending(run=>run.Length).FirstOrDefault();
                        if(donor.Handle is not null)
                            extension[donor.Handle]=extension.GetValueOrDefault(donor.Handle)-adjustment;
                        extension[pipe]=extension.GetValueOrDefault(pipe)+adjustment;
                    }
                }
                catch(SheetCapacityException ex){failure=ex;execution.Reject(ex,snapshot.Parts.Count,level,paperMargin,extension);if((Environment.GetEnvironmentVariable("ISO_LAYOUT_TRACE")=="1"||Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1"))Console.Error.WriteLine($"parts={snapshot.Parts.Count}, density={level}: {ex.Message}");}
            }
            if(manualBaseline is not null){plans[key]=manualBaseline;return manualBaseline;}
            if(!quick)rejectedPlans.Add(key);
            throw failure??new SheetCapacityException("Не удалось устранить наложения размеров.");
        }
        void RequireVentExtension(PipelineSnapshot snapshot,AirVentSpacingException failure)
        {
            string key=Key(snapshot);var current=Plan(snapshot);
            if(!requiredVentExtensions.TryGetValue(key,out var required))requiredVentExtensions[key]=required=new(StringComparer.OrdinalIgnoreCase);
            required[failure.Pipe]=Math.Max(required.GetValueOrDefault(failure.Pipe),(current.PipeExtensions?.GetValueOrDefault(failure.Pipe)??0)+failure.Extra);
            plans.Remove(key);rejectedPlans.Remove(key);
        }
        bool AdvanceDensity(PipelineSnapshot snapshot)
        {
            int first=Array.IndexOf(densityOrder,Plan(snapshot).LayoutLevel)+1;
            foreach(int level in densityOrder.Skip(first))try
            {
                var candidate=SheetPlanner.Build(Prepare(snapshot),instrumentCalloutsOnly:instrumentCalloutsOnly,quarterTurns:quarterTurns,excludeFlangeHardware:excludeFlangeHardware,symbolMap:symbolMap,compactLevel:level,pipeExtensions:Plan(snapshot).PipeExtensions,paperMargin:Plan(snapshot).PaperMargin,execution:execution);
                SheetComposition.Center(candidate);
                if(DimensionCongestion.PipeToExtend(snapshot,candidate) is not null)continue;
                plans[Key(snapshot)]=candidate;return true;
            }
            catch(SheetCapacityException){}
            return false;
        }
        SheetScene FillPaper(PipelineSnapshot snapshot,SheetScene baseline,Func<int,double,IReadOnlyDictionary<string,double>?,double?,SheetScene> rebuild)=>
            SheetSpaceOptimizer.Fill(snapshot,baseline,rebuild,Prepare(snapshot).Topology,symbolMap,quarterTurns,execution,
                overlap=>TransverseRepairs(snapshot,overlap.SourceId!,overlap.Valve,preferNearCrossing:true));

        var cutPolicy=CutPreferences.Build(source,symbolMap?.Catalog?.Style);
        var installationUnits=InstallationUnits.Find(source,execution);
        var mandatory=(source.RequiredCuts??Array.Empty<Connection>()).Select(CutPreferences.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool CanCut(Connection c)=>!cutPolicy.Protected.Contains(CutPreferences.Key(c))||mandatory.Contains(CutPreferences.Key(c));
        var all=source.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
        var runContinuity=mandatory.Count==0?new SheetRunContinuity(source,Prepare(source).Topology):null;
        bool HasInteriorGap(IEnumerable<string> handles)=>runContinuity?.HasInteriorGap(handles)==true;
        var leaves=new List<PipelineSnapshot>();
        PipelineSnapshot Subset(HashSet<string> handles)=>TapDimensionReferences.Apply(source,source with
        {
            Parts=source.Parts.Where(p=>handles.Contains(p.Handle)).ToArray(),
            Accessories=source.Accessories?.Where(p=>handles.Contains(p.Properties.GetValueOrDefault("ConnectorHandle",""))).ToArray(),
            Connections=source.Connections.Where(c=>handles.Contains(c.FirstHandle)&&handles.Contains(c.SecondHandle)).ToArray()
        });
        PlannedSheet ImproveDimensionCorners(PlannedSheet baseline,Func<PlannedSheet> rebuild)
        {
            if(!execution.CanTryOptionalVariant()||baseline.Scene.Dimensions.Select(d=>d.ChainId).Distinct().Take(2).Count()<2)
                return baseline;
            execution.PreferDimensionCorners=true;
            try
            {
                execution.Report("Стыковка размерных линий в углах",variant:true);
                var candidate=rebuild();
                if(LayoutQuality.Dimensions(candidate.Scene).CompareTo(LayoutQuality.Dimensions(baseline.Scene))<=0&&
                   LayoutQuality.Leaders(candidate.Scene).CompareTo(LayoutQuality.Leaders(baseline.Scene))<=0&&
                   DimensionCongestion.FindRepair(candidate.Snapshot,candidate.Scene,includeProximity:false) is null)
                    return candidate;
            }
            catch(SheetCapacityException){}
            catch(PlanningLimitException){cancellationToken.ThrowIfCancellationRequested();}
            finally{execution.PreferDimensionCorners=false;}
            return baseline;
        }
        PlannedSheet JoinOverallCorners(PlannedSheet sheet)
        {
            var scene=sheet.Scene;
            if(scene.BomOnly)return sheet;
            var symbols=scene.Symbols??Array.Empty<SymbolGlyph>();
            bool Fits(IReadOnlyList<PipeDimension> trial)
            {
                var obstacles=DimensionRouting.TextObstacles(scene.Strokes,symbols);
                if(DimensionTextPlacement.FinalCollisions(trial,obstacles).Length>0)return false;
                var labels=scene.Annotations.Where(label=>label.Role is not ("title" or "status"))
                    .Select(AnnotationPlacement.Bounds).ToArray();
                foreach(var (before,after) in scene.Dimensions.Zip(trial).Where(pair=>pair.First!=pair.Second))
                {
                    var beforeInk=AnnotationPlacement.DimensionBox(before);
                    var afterInk=AnnotationPlacement.DimensionBox(after);
                    var beforeLines=DimensionRouting.GetSegments(new[]{before});
                    var afterLines=DimensionRouting.GetSegments(new[]{after});
                    bool Hits(DimensionRouting.Segment[] lines,AnnotationPlacement.Box label)=>
                        lines.Any(line=>AnnotationPlacement.Hits(line.A,line.B,label));
                    if(labels.Any(label=>(afterInk.Overlaps(label)&&!beforeInk.Overlaps(label))||
                        (Hits(afterLines,label)&&!Hits(beforeLines,label))))return false;
                }
                var paper=SheetComposition.Paper;
                var reserved=SheetComposition.Reserved(BomCategories.BodyHeight(scene.BillOfMaterials)/IsoAdvancedSettings.Current.BomRowHeight);
                return !SheetComposition.Pieces(scene with{Dimensions=trial}).Any(piece=>piece.Left<paper.Left-.01||
                    piece.Right>paper.Right+.01||piece.Bottom<paper.Bottom-.01||piece.Top>paper.Top+.01||reserved.Any(piece.Overlaps));
            }
            var dimensions=DimensionRowAlignment.AlignAdjacent(scene.Dimensions,scene.Strokes,symbols,finalizeOveralls:true,acceptFinal:Fits);
            return scene.Dimensions.SequenceEqual(dimensions)?sheet:sheet with{Scene=scene with{Dimensions=dimensions}};
        }
        IReadOnlyList<PlannedSheet> Finish(IReadOnlyList<PlannedSheet> sheets)
        {
            var candidates=sheets.Select(JoinOverallCorners).ToArray();
            var finalized=candidates.SequenceEqual(sheets)?sheets:candidates;
            SheetSetIntegrity.Validate(source,finalized,excludeFlangeHardware,valveKitExclusions);
            remember?.Invoke(new RememberedLayout(source,finalized,minimumPipeLength,IsoAdvancedSettings.Signature));
            return finalized;
        }
        if(remembered is not null)
        {
            if(cappedVentDrainPipes.SetEquals(VentDrainPipePolicy.Find(remembered.Source))&&
               (valveKitExclusions is null||valveKitExclusions.SetEquals(ValveKitBomPolicy.Exclusions(remembered.Source))))
            {
                var refreshed=IncrementalSheetPlanner.TryRefresh(source,remembered,Subset,
                    snapshot=>Plan(snapshot),FillPaper,symbolMap,quarterTurns,excludeFlangeHardware,instrumentCalloutsOnly,Prepare,execution);
                if(refreshed is not null)return Finish(refreshed);
            }
            execution.Report("Сохранённая компоновка не подходит: полный пересчёт");
        }
        try
        {
            var raw=Plan(source);
            var baseline=SheetComposition.Center(raw);
            var expanded=FillPaper(source,baseline,(level,margin,extensions,scaleOverride)=>
                SheetPlanner.Build(Prepare(source),quarterTurns,symbolMap,compactLevel:level,
                    pipeExtensions:extensions,excludeFlangeHardware:excludeFlangeHardware,
                    instrumentCalloutsOnly:instrumentCalloutsOnly,paperMargin:margin,fixedScale:scaleOverride,execution:execution,checkValveClearance:true));
            var sheet=new PlannedSheet(1,1,source,expanded,raw);
            var geometry=SheetGeometry.From(expanded);
            sheet=ImproveDimensionCorners(sheet,()=>sheet with{Scene=SheetComposition.Center(SheetPlanner.Build(
                Prepare(source),quarterTurns,symbolMap,compactLevel:geometry.Level,pipeExtensions:geometry.Extensions,
                excludeFlangeHardware:excludeFlangeHardware,instrumentCalloutsOnly:instrumentCalloutsOnly,
                paperMargin:geometry.Margin,fixedScale:geometry.Scale,execution:execution))});
            return Finish(BomPagination.Apply(new[]{sheet}));
        }
        catch(SheetCapacityException){}
        bool Fits(PipelineSnapshot snapshot,bool quick=false)
        {
            try{Plan(snapshot,quick);return true;}
            catch(SheetCapacityException){return false;}
        }
        bool FitsPair(PipelineSnapshot parent,PipelineSnapshot first,PipelineSnapshot second)
        {
            try
            {
                var pair=new[]{first,second};
                var pairOwner=pair.SelectMany((s,i)=>s.Parts.Select(p=>(p.Handle,Sheet:i))).ToDictionary(x=>x.Handle,x=>x.Sheet,StringComparer.OrdinalIgnoreCase);
                var pairScenes=pair.Select(snapshot=>Plan(snapshot)).ToArray();
                var pairLayouts=pair.Select((s,i)=>new SchematicLayout(s,Prepare(s).Topology,quarterTurns,pairScenes[i].LayoutLevel,
                    pairScenes[i].PipeExtensions,symbolMap is null?null:symbolMap.ResolveGeometry,pairScenes[i].PaperMargin,execution:execution)).ToArray();
                for(int i=0;i<2;i++)
                {
                    var marks=new List<SheetContinuation>();int mark=0;
                    foreach(var c in parent.Connections)
                    {
                        if(!pairOwner.TryGetValue(c.FirstHandle,out int a)||!pairOwner.TryGetValue(c.SecondHandle,out int b))continue;if(a==b)continue;
                        string handle=a==i?c.FirstHandle:c.SecondHandle,port=a==i?c.FirstPort:c.SecondPort;
                        marks.Add(new(handle,port,"П"+(++mark),i==0?2:1));
                    }
                    var context=ContinuationContextPlanner.Build(i,pair,pairScenes,pairLayouts,parent,pairOwner);
                    SheetComposition.Center(SheetPlanner.Build(Prepare(pair[i]),instrumentCalloutsOnly:instrumentCalloutsOnly,quarterTurns:quarterTurns,excludeFlangeHardware:excludeFlangeHardware,symbolMap:symbolMap,continuations:marks,sheetNumber:i+1,sheetCount:2,contextStrokes:context.Strokes,contextSymbols:context.Symbols,compactLevel:pairScenes[i].LayoutLevel,pipeExtensions:pairScenes[i].PipeExtensions,paperMargin:pairScenes[i].PaperMargin,execution:execution));
                }
                return true;
            }
            catch(SheetCapacityException){return false;}
        }
        var splitCache=new Dictionary<string,PipelineSnapshot[]>(StringComparer.Ordinal);
        (HashSet<string> A,HashSet<string> B,int ValveDistance,int Balance)[] FindCuts(PipelineSnapshot snapshot)
        {
            var handles=snapshot.Parts.Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var links=snapshot.Connections.Distinct().OrderBy(c=>string.Join("|",new[]{c.FirstHandle+":"+c.FirstPort,c.SecondHandle+":"+c.SecondPort}.OrderBy(h=>h,StringComparer.OrdinalIgnoreCase)),StringComparer.OrdinalIgnoreCase).ToArray();
            var adjacency=handles.ToDictionary(h=>h,_=>new HashSet<string>(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
            foreach(var c in links){if(adjacency.TryGetValue(c.FirstHandle,out var a)&&adjacency.TryGetValue(c.SecondHandle,out var b)){a.Add(c.SecondHandle);b.Add(c.FirstHandle);}}
            var distances=handles.ToDictionary(h=>h,_=>int.MaxValue,StringComparer.OrdinalIgnoreCase);
            var queue=new Queue<string>();
            foreach(var p in snapshot.Parts.Where(p=>p.Component==ComponentKind.Valve)){distances[p.Handle]=0;queue.Enqueue(p.Handle);}
            while(queue.Count>0){var h=queue.Dequeue();foreach(var n in adjacency[h])if(distances[n]>distances[h]+1){distances[n]=distances[h]+1;queue.Enqueue(n);}}
            int Physical(IEnumerable<string> hs)=>hs.Count(h=>all[h].Kind!="AcPpDb3dConnector");
            var candidates=new List<(HashSet<string> A,HashSet<string> B,int ValveDistance,int Balance)>();
            var bridges=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var link in links)
            {
                execution.Check();
                if(!CanCut(link))continue;
                if(!all.ContainsKey(link.FirstHandle)||!all.ContainsKey(link.SecondHandle))continue;
                if(BomCategories.Classify(all[link.FirstHandle])==BomCategory.Supports||BomCategories.Classify(all[link.SecondHandle])==BomCategory.Supports)continue;
                // Cut only at a pipe port: never split the valve / gasket / flange assembly.
                if(all[link.FirstHandle].Component!=ComponentKind.Pipe&&all[link.SecondHandle].Component!=ComponentKind.Pipe)continue;
                var a=new HashSet<string>(StringComparer.OrdinalIgnoreCase){link.FirstHandle};queue.Enqueue(link.FirstHandle);
                while(queue.Count>0){var h=queue.Dequeue();foreach(var n in adjacency[h])
                {
                    if((h==link.FirstHandle&&n==link.SecondHandle)||(h==link.SecondHandle&&n==link.FirstHandle))continue;
                    if(a.Add(n))queue.Enqueue(n);
                }}
                if(a.Contains(link.SecondHandle))continue; // A loop is not a valid single cut.
                bridges.Add(CutPreferences.Key(link));
                var b=handles.Except(a,StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
                int na=Physical(a),nb=Physical(b);if(na==0||nb==0)continue;
                candidates.Add((a,b,Math.Min(distances[link.FirstHandle],distances[link.SecondHandle]),Math.Abs(na-nb)));
            }
            int PreferredRank(HashSet<string> a)=>source.Connections.Any(c=>cutPolicy.Preferred.Contains(CutPreferences.Key(c))&&a.Contains(c.FirstHandle)!=a.Contains(c.SecondHandle))?0:1;
            void AddPairedCuts()
            {
                // A ring has no bridge: split it at two pipe connections, preserving rigid assemblies.
                var cuttable=links.Where(c=>CanCut(c)&&!bridges.Contains(CutPreferences.Key(c))&&handles.Contains(c.FirstHandle)&&handles.Contains(c.SecondHandle)&&BomCategories.Classify(all[c.FirstHandle])!=BomCategory.Supports&&BomCategories.Classify(all[c.SecondHandle])!=BomCategory.Supports&&(all[c.FirstHandle].Component==ComponentKind.Pipe||all[c.SecondHandle].Component==ComponentKind.Pipe)).ToArray();
                var seenPartitions=candidates.Select(c=>string.Join("|",(c.A.Count<=c.B.Count?c.A:c.B).OrderBy(h=>h,StringComparer.Ordinal))).ToHashSet(StringComparer.Ordinal);
                for(int first=0;first<cuttable.Length;first++)for(int second=first+1;second<cuttable.Length;second++)
                {
                    execution.Check();
                    var cutA=cuttable[first];var cutB=cuttable[second];
                    bool Blocked(string a,string b)=>a==cutA.FirstHandle&&b==cutA.SecondHandle||a==cutA.SecondHandle&&b==cutA.FirstHandle||a==cutB.FirstHandle&&b==cutB.SecondHandle||a==cutB.SecondHandle&&b==cutB.FirstHandle;
                    var a=new HashSet<string>(StringComparer.OrdinalIgnoreCase){cutA.FirstHandle};var pending=new Queue<string>();pending.Enqueue(cutA.FirstHandle);
                    while(pending.Count>0){string current=pending.Dequeue();foreach(var next in adjacency[current])if(!Blocked(current,next)&&a.Add(next))pending.Enqueue(next);}
                    if(a.Contains(cutA.SecondHandle)&&a.Contains(cutB.SecondHandle)==a.Contains(cutB.FirstHandle))continue;
                    var b=handles.Except(a,StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);int na=Physical(a),nb=Physical(b);if(na==0||nb==0)continue;
                    string key=string.Join("|",(a.Count<=b.Count?a:b).OrderBy(h=>h,StringComparer.Ordinal));if(!seenPartitions.Add(key))continue;
                    candidates.Add((a,b,Math.Min(Math.Min(distances[cutA.FirstHandle],distances[cutA.SecondHandle]),Math.Min(distances[cutB.FirstHandle],distances[cutB.SecondHandle])),Math.Abs(na-nb)));
                }
            }
            // Removing two edges from a tree cannot produce two connected leaves.
            if(links.Select(CutPreferences.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()>=handles.Count)
                AddPairedCuts();
            // Rank ring and bridge cuts together. Trying every small side branch first
            // consumes the search before any balanced ring split is considered.
            return candidates.Where(c=>!HasInteriorGap(c.A)&&!HasInteriorGap(c.B)).OrderBy(c=>InstallationUnits.SplitCount(installationUnits,c.A,c.B)).ThenBy(c=>c.Balance).ThenBy(c=>PreferredRank(c.A)).ThenBy(c=>c.ValveDistance).ToArray();
        }

        void Split(PipelineSnapshot snapshot,bool force=false)
        {
            execution.Report("Выбор границ листов");
            var components=SheetConnectivity.Components(snapshot);
            if(components.Count>1){foreach(var component in components)Split(Subset(component));return;}
            if(!force&&splitCache.TryGetValue(Key(snapshot),out var known)){leaves.AddRange(known);return;}
            if(!force&&Fits(snapshot)){leaves.Add(snapshot);return;}
            var ordered=FindCuts(snapshot);
            foreach(var c in ordered.Take(6))
            {
                var first=Subset(c.A);
                bool fa=Fits(first,quick:true);
                var second=fa?Subset(c.B):null;
                bool fb=second is not null&&Fits(second,quick:true);
                bool fp=fb&&FitsPair(snapshot,first,second!);
                if((Environment.GetEnvironmentVariable("ISO_LAYOUT_TRACE")=="1"||Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1"))
                    Console.Error.WriteLine($"Cut {c.A.Count} vs {c.B.Count} (bal {c.Balance}): FitsA={fa}, FitsB={(fa?fb.ToString():"skipped")}, FitsPair={fp}");
                if(fp){leaves.Add(first);leaves.Add(second!);return;}
            }
            if(ordered.Length==0)throw new SheetCapacityException("Не удалось разделить линию по соединениям труб. Требуется ручное указание границы листов.");
            // Avoid peeling off single components when more than two sheets are required.
            var balanced=ordered.Where(c=>c.Balance<=snapshot.Parts.Count(p=>!p.IsConnector)*.6).ToArray();
            int minimumUnitSplits=ordered.Min(c=>InstallationUnits.SplitCount(installationUnits,c.A,c.B));
            var intact=ordered.Where(c=>InstallationUnits.SplitCount(installationUnits,c.A,c.B)==minimumUnitSplits).ToArray();
            var pool=intact.Concat(balanced).Distinct().ToArray();
            // Try alternative boundaries when the most balanced cut cannot be laid out.
            // Memoized subproblems keep the search bounded and independent of line identifiers.
            var options=pool.Concat(ordered).Take(12)
                .DistinctBy(c=>string.Join("|",c.A.OrderBy(h=>h,StringComparer.Ordinal))).ToArray();
            PipelineSnapshot[]? best=null;
            foreach(var option in options)
            {
                int start=leaves.Count;
                try
                {
                    Split(Subset(option.A));Split(Subset(option.B));
                    var candidate=leaves.Skip(start).ToArray();
                    if(best is null||candidate.Length<best.Length)best=candidate;
                    break; // Establish a feasible connected partition before optional merges.
                }
                catch(SheetCapacityException){}
                finally{leaves.RemoveRange(start,leaves.Count-start);}
            }
            if(best is null)throw new SheetCapacityException("Не найдено допустимое разбиение по соединениям труб.");
            if(!force)splitCache[Key(snapshot)]=best;
            leaves.AddRange(best);
        }
        var explicitCuts=(source.RequiredCuts??Array.Empty<Connection>()).Select(CutPreferences.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(explicitCuts.Count>0)
        {
            var remaining=source.Parts.Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var adjacency=remaining.ToDictionary(h=>h,_=>new HashSet<string>(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
            foreach(var c in source.Connections)if(!explicitCuts.Contains(CutPreferences.Key(c))&&adjacency.ContainsKey(c.FirstHandle)&&adjacency.ContainsKey(c.SecondHandle)){adjacency[c.FirstHandle].Add(c.SecondHandle);adjacency[c.SecondHandle].Add(c.FirstHandle);}
            var groups=new List<HashSet<string>>();
            while(remaining.Count>0){string root=remaining.First();var group=new HashSet<string>(StringComparer.OrdinalIgnoreCase){root};remaining.Remove(root);var q=new Queue<string>();q.Enqueue(root);while(q.Count>0)foreach(var next in adjacency[q.Dequeue()])if(remaining.Remove(next)){group.Add(next);q.Enqueue(next);}groups.Add(group);}
            if(groups.Count>1)
            {
                var sections=groups.Select(Subset).ToArray();
                manualSections.UnionWith(sections.Select(Key));
                foreach(var section in sections)Split(section);
            }
            else Split(source);
        }
        else Split(source);
        var startMarkers=(source.IsoMarkers??Array.Empty<ModelIsoMarker>()).Where(m=>m.InformationType==5).ToArray();
        if(startMarkers.Length>1)throw new InvalidDataException("На линии несколько точек начала изометрии. Оставьте одну точку начала.");
        PipelineSnapshot[] OrderFromStart(IReadOnlyList<PipelineSnapshot> partition)
        {
            var sheetOf=partition.SelectMany((sheet,index)=>sheet.Parts.Select(part=>(part.Handle,index)))
                .ToDictionary(part=>part.Handle,part=>part.index,StringComparer.OrdinalIgnoreCase);
            var neighbors=partition.Select(_=>new HashSet<int>()).ToArray();
            foreach(var connection in source.Connections)
                if(sheetOf.TryGetValue(connection.FirstHandle,out int first)&&
                   sheetOf.TryGetValue(connection.SecondHandle,out int last)&&first!=last)
                {
                    neighbors[first].Add(last);
                    neighbors[last].Add(first);
                }

            int start=Array.FindIndex(neighbors,adjacent=>adjacent.Count==1);
            if(startMarkers.Length==1)
            {
                string handle=startMarkers[0].HostHandle;
                start=Enumerable.Range(0,partition.Count).FirstOrDefault(index=>partition[index].Parts.Any(part=>part.Handle==handle||
                    part.Properties.GetValueOrDefault("IsoOriginalHandle")==handle),-1);
                if(start<0)throw new InvalidDataException("Не найден элемент точки начала изометрии: "+handle);
            }

            var ordered=new List<PipelineSnapshot>(partition.Count);
            var seen=new HashSet<int>();
            foreach(int root in new[]{Math.Max(0,start)}.Concat(Enumerable.Range(0,partition.Count)))
            {
                var pending=new Queue<int>();
                pending.Enqueue(root);
                while(pending.TryDequeue(out int index))
                {
                    execution.Check();
                    if(!seen.Add(index))continue;
                    ordered.Add(partition[index]);
                    // Nearby continuations receive nearby sheet numbers, even when the start is on a branch.
                    foreach(int next in neighbors[index].Where(next=>!seen.Contains(next))
                        .OrderByDescending(ContinuationDepth).ThenBy(next=>next))
                        pending.Enqueue(next);
                }
            }
            return ordered.ToArray();

            int ContinuationDepth(int first)
            {
                if(neighbors[first].Count<=1)return 0;
                var visited=new HashSet<int>(seen){first};
                var queue=new Queue<(int Sheet,int Distance)>();
                queue.Enqueue((first,0));
                int depth=0;
                while(queue.TryDequeue(out var current))
                {
                    depth=current.Distance;
                    foreach(int next in neighbors[current.Sheet])
                        if(visited.Add(next))queue.Enqueue((next,depth+1));
                }
                return depth;
            }
        }
        var visitedContinuationStates=new HashSet<string>(StringComparer.Ordinal);
        var welds=WeldPlanner.Build(source,p=>new(p.X,p.Y)).Markers;
        var requiredConnections=source.RequiredCuts??Array.Empty<Connection>();
        var initial=CompletePartition();
        return Finish(OptimizePartition(initial));

        PartitionLayout CreatePartition(IReadOnlyList<PipelineSnapshot> partition,bool quick)
        {
            var ordered=OrderFromStart(partition);
            var raw=ordered.Select(snapshot=>Plan(snapshot,quick)).ToArray();
            return new PartitionLayout(source,ordered,raw,welds,Prepare,symbolMap,quarterTurns,
                excludeFlangeHardware,instrumentCalloutsOnly,execution);
        }

        (PartitionLayout Layout,PlannedSheet[] Sheets) CompletePartition()
        {
            while(true)
            {
                execution.Report("Согласование листов и переходов");
                string GeometryState(PipelineSnapshot snapshot)
                {
                    var scene=Plan(snapshot);
                    return Key(snapshot)+":"+scene.LayoutLevel+":"+scene.PaperMargin.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+":"+string.Join(",",(scene.PipeExtensions??new Dictionary<string,double>()).OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+"="+p.Value.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
                }
                string state=string.Join(";",leaves.Select(GeometryState).OrderBy(k=>k,StringComparer.Ordinal));
                if(!visitedContinuationStates.Add(state))
                    throw new PlanningLimitException("Повторилась компоновка листов при размещении переходов. Задайте другую границу листов или ракурс.");
                var partition=CreatePartition(leaves,quick:false);
                var sheets=new List<PlannedSheet>(partition.Count);
                for(int i=0;i<partition.Count;i++)
                {
                    var snapshot=partition.Snapshots[i];
                    try
                    {
                        sheets.Add(partition.Compose(i));
                    }
                    catch(SheetCapacityException failure)
                    {
                        partition.Reject(i,failure);
                        if(failure is AirVentSpacingException vent)RequireVentExtension(snapshot,vent);
                        else if(!TryRepairContinuation(partition,i,failure)&&!AdvanceDensity(snapshot))
                        {
                            leaves.Remove(snapshot);
                            Split(snapshot,force:true);
                        }
                        break;
                    }
                }
                if(sheets.Count!=partition.Count)continue;
                SheetSetIntegrity.Validate(source,BomPagination.Apply(sheets),excludeFlangeHardware,valveKitExclusions);
                return (partition,sheets.ToArray());
            }
        }

        bool TryRepairContinuation(PartitionLayout partition,int index,SheetCapacityException failure)
        {
            var snapshot=partition.Snapshots[index];
            var pipe=NearestPipe(snapshot,failure.SourceId);
            if(pipe is null)return false;
            var original=SheetGeometry.From(partition.BaseScene(index));
            foreach(double adjustment in new[]{settings.PipeAdjustment,settings.PipeAdjustment*2})
            {
                if(!execution.CanTryOptionalVariant())return false;
                var extensions=new Dictionary<string,double>(original.Extensions??new Dictionary<string,double>(),StringComparer.OrdinalIgnoreCase);
                extensions[pipe]=extensions.GetValueOrDefault(pipe)+adjustment;
                var geometry=original with{Extensions=extensions};
                try
                {
                    execution.Report("Удлинение участка для размещения перехода",variant:true);
                    var trial=partition.WithGeometry(index,geometry);
                    var updated=trial.Compose(index);
                    if(DimensionCongestion.PipeToExtend(snapshot,updated.Scene) is not null)continue;
                    plans[Key(snapshot)]=trial.BaseScene(index);
                    return true;
                }
                catch(SheetCapacityException rejected)
                {
                    execution.Reject(rejected,snapshot.Parts.Count,geometry.Level,geometry.Margin,geometry.Extensions,geometry.Scale);
                }
                catch(PlanningLimitException){return false;}
            }
            return false;
        }

        IReadOnlyList<PlannedSheet> OptimizePartition((PartitionLayout Layout,PlannedSheet[] Sheets) initial)
        {
            var best=initial;
            try
            {
                // Compare boundaries before spending the remaining search on filling the paper.
                int remainingTrials=settings.PartitionTrials;
                var tested=new HashSet<string>(StringComparer.Ordinal);
                while(remainingTrials>0&&execution.CanTryOptionalVariant())
                {
                    bool improved=false;
                    foreach(bool mergeOnly in new[]{true,false})
                    foreach(var (first,second) in best.Layout.NeighborPairs())
                    {
                        if(improved||remainingTrials==0||!execution.CanTryOptionalVariant())break;
                        var snapshots=best.Layout.Snapshots;
                        var members=snapshots[first].Parts.Concat(snapshots[second].Parts)
                            .Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        if(requiredConnections.Any(c=>members.Contains(c.FirstHandle)&&members.Contains(c.SecondHandle))||
                           HasInteriorGap(members))continue;
                        var union=Subset(members);
                        var unchanged=snapshots.Where((_,index)=>index!=first&&index!=second).ToArray();
                        var options=mergeOnly?new[]{new[]{union}}:ShiftBoundary();
                        foreach(var option in options)
                        {
                            if(remainingTrials==0||!execution.CanTryOptionalVariant())break;
                            var candidate=unchanged.Concat(option).ToArray();
                            string key=string.Join(";",candidate.Select(Key).OrderBy(k=>k,StringComparer.Ordinal));
                            if(!tested.Add(key))continue;
                            remainingTrials--;
                            try
                            {
                                execution.Report("Проверка альтернативной границы листов",variant:true);
                                var layout=CreatePartition(candidate,quick:true);
                                var sheets=Enumerable.Range(0,layout.Count).Select(layout.Compose).ToArray();
                                var paginated=BomPagination.Apply(sheets);
                                SheetSetIntegrity.Validate(source,paginated,excludeFlangeHardware,valveKitExclusions);
                                int currentPages=BomPagination.Apply(best.Sheets).Count;
                                if(paginated.Count<currentPages||
                                   paginated.Count==currentPages&&LayoutQuality.Compare(sheets,best.Sheets)<0)
                                {
                                    best=(layout,sheets);
                                    improved=true;
                                    break;
                                }
                            }
                            catch(SheetCapacityException){}
                        }
                        if(improved)break;

                        IEnumerable<PipelineSnapshot[]> ShiftBoundary()
                        {
                            string original=Key(snapshots[first]);
                            foreach(var cut in FindCuts(union).Where(c=>InstallationUnits.SplitCount(installationUnits,c.A,c.B)==0))
                            {
                                var a=Subset(cut.A);
                                var b=Subset(cut.B);
                                if(Key(a)!=original&&Key(b)!=original)yield return new[]{a,b};
                            }
                        }
                    }
                    if(!improved)
                    {
                        for(int index=0;index<best.Sheets.Length&&!improved&&remainingTrials>0;index++)
                        {
                            var scene=best.Sheets[index].Scene;
                            var quality=LayoutQuality.Dimensions(scene);
                            var leaders=LayoutQuality.Leaders(scene);
                            if(quality.Clearance==0&&quality.FittingCrossings==0&&
                               leaders.Crossings==0)continue;
                            var snapshots=best.Layout.Snapshots;
                            var current=snapshots[index];
                            var unchanged=snapshots.Where((_,other)=>other!=index).ToArray();
                            foreach(var cut in FindCuts(current).Where(c=>InstallationUnits.SplitCount(installationUnits,c.A,c.B)==0))
                            {
                                if(remainingTrials==0||!execution.CanTryOptionalVariant())break;
                                var candidate=unchanged.Concat(new[]{Subset(cut.A),Subset(cut.B)}).ToArray();
                                string key=string.Join(";",candidate.Select(Key).OrderBy(k=>k,StringComparer.Ordinal));
                                if(!tested.Add(key))continue;
                                remainingTrials--;
                                try
                                {
                                    execution.Report("Проверка разбиения для читаемости",variant:true);
                                    var layout=CreatePartition(candidate,quick:true);
                                    var trial=(Layout:layout,Sheets:Enumerable.Range(0,layout.Count).Select(layout.Compose).ToArray());
                                    SheetSetIntegrity.Validate(source,BomPagination.Apply(trial.Sheets),excludeFlangeHardware,valveKitExclusions);
                                    if(LayoutQuality.Compare(trial.Sheets,best.Sheets)>=0)continue;
                                    best=trial;
                                    improved=true;
                                    break;
                                }
                                catch(SheetCapacityException){}
                            }
                        }
                    }
                    if(!improved)break;
                }
                best=Expand(best);
            }
            catch(PlanningLimitException){cancellationToken.ThrowIfCancellationRequested();}
            var finalized=best.Sheets.Select((sheet,index)=>ImproveDimensionCorners(sheet,()=>best.Layout.Compose(index))).ToArray();
            return BomPagination.Apply(finalized);
        }

        (PartitionLayout Layout,PlannedSheet[] Sheets) Expand((PartitionLayout Layout,PlannedSheet[] Sheets) candidate)
        {
            for(int index=0;index<candidate.Layout.Count&&execution.CanTryOptionalVariant();index++)
                candidate=candidate.Layout.TryExpand(index,candidate.Sheets,FillPaper);
            return candidate;
        }
    }
}
