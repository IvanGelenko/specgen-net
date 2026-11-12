namespace IsoGenerator.Core;

public sealed class SheetCapacityException(string message,string? sourceId=null,ModelPoint? modelStart=null,ModelPoint? modelEnd=null) : IOException(message) {public string? SourceId {get;}=sourceId;public ModelPoint? ModelStart{get;}=modelStart;public ModelPoint? ModelEnd{get;}=modelEnd;}
public sealed record SheetContinuation(string Handle,string Port,string Mark,int OtherSheet);
public sealed record PlannedSheet(int Number,int Total,PipelineSnapshot Snapshot,SheetScene Scene);

public static class SheetSetPlanner
{
    public static IReadOnlyList<PlannedSheet> Build(PipelineSnapshot source,IsoSymbolMap? symbolMap=null,int quarterTurns=0,bool excludeFlangeHardware=false,CancellationToken cancellationToken=default,IProgress<PlanningProgress>? progress=null,bool ignoreBreakPoints=false)
    {
        using var execution=new PlanningExecution(cancellationToken,progress);
        PlanningExecution.Report("Проверка исходных данных");
        source=SheetConnectivity.AttachSupports(BreakPointPreparation.Apply(source,ignoreBreakPoints));
        var validation=SnapshotValidation.Validate(source);
        if(validation.Errors.Count>0)throw new InvalidDataException(string.Join("\n",validation.Errors));
        // Use the same density search for the whole line and every candidate fragment.
        // A rejected spacious layout is not proof that a fragment needs another sheet.
        var densityOrder=new[]{1,2,4,0,3};
        var plans=new Dictionary<string,SheetScene>(StringComparer.Ordinal);
        var rejectedPlans=new HashSet<string>(StringComparer.Ordinal);
        string Key(PipelineSnapshot snapshot)=>string.Join("|",snapshot.Parts.Select(p=>p.Handle).OrderBy(h=>h,StringComparer.Ordinal));
        string? NearestPipe(PipelineSnapshot snapshot,string? sourceId)
        {
            if(string.IsNullOrEmpty(sourceId))return null;
            var parts=snapshot.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase){sourceId};var frontier=new[]{sourceId};
            for(int depth=0;depth<=4;depth++)
            {
                var pipes=frontier.Where(h=>parts.TryGetValue(h,out var p)&&p.Properties.GetValueOrDefault("PnPClassName")=="Pipe")
                    .OrderBy(h=>{var ports=parts[h].Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();return ports.Length==2?ports[0].Position.DistanceTo(ports[1].Position):double.MaxValue;}).ThenBy(h=>h,StringComparer.Ordinal).ToArray();
                if(pipes.Length>0)return pipes[0];
                var current=frontier.ToHashSet(StringComparer.OrdinalIgnoreCase);
                frontier=snapshot.Connections.SelectMany(c=>current.Contains(c.FirstHandle)?new[]{c.SecondHandle}:current.Contains(c.SecondHandle)?new[]{c.FirstHandle}:Array.Empty<string>()).Where(seen.Add).ToArray();
            }
            return null;
        }
        SheetScene Plan(PipelineSnapshot snapshot)
        {
            PlanningExecution.Check();
            var included=snapshot.Parts.Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if((source.RequiredCuts??Array.Empty<Connection>()).Any(c=>included.Contains(c.FirstHandle)&&included.Contains(c.SecondHandle)))throw new SheetCapacityException("На модели задана точка разрыва между листами.");
            if(SheetConnectivity.Components(snapshot).Count>1)throw new SheetCapacityException("На листе допускается только один связный фрагмент трубопровода.");
            string key=Key(snapshot);if(plans.TryGetValue(key,out var cached))return cached;
            if(rejectedPlans.Contains(key))throw new SheetCapacityException("Проверенные варианты компоновки не поместились.");
            SheetCapacityException? failure=null;
            foreach(int level in densityOrder)
            {
                try
                {
                    var extension=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
                    for(int iteration=0;iteration<4;iteration++)
                    {
                        PlanningExecution.Report($"Компоновка фрагмента: {snapshot.Parts.Count} объектов",variant:true);
                        SheetScene scene;
                        try{scene=SheetPlanner.Build(snapshot,quarterTurns:quarterTurns,excludeFlangeHardware:excludeFlangeHardware,symbolMap:symbolMap,compactLevel:level,pipeExtensions:extension);}
                        catch(SheetCapacityException ex) when(ex.SourceId is not null)
                        {
                            if(Environment.GetEnvironmentVariable("ISO_LAYOUT_TRACE")=="1")Console.Error.WriteLine($"parts={snapshot.Parts.Count}, density={level}, extension={iteration}, source={ex.SourceId}: {ex.Message}");
                            string? local=NearestPipe(snapshot,ex.SourceId);if(local is null)throw;
                            string extensionKey=local;
                            if(ex.ModelStart is {} start&&ex.ModelEnd is {} end)
                            {
                                var graph=DimensionChainPlanner.BuildGraph(snapshot);double dx=end.X-start.X,dy=end.Y-start.Y,dz=end.Z-start.Z,l2=dx*dx+dy*dy+dz*dz;
                                var edge=graph.Edges.Where(e=>e.Kind=="Pipe"&&e.Source==local).FirstOrDefault(e=>
                                {
                                    var a=graph.Nodes[e.A].Point;var b=graph.Nodes[e.B].Point;var mid=new ModelPoint((a.X+b.X)/2,(a.Y+b.Y)/2,(a.Z+b.Z)/2);
                                    double t=((mid.X-start.X)*dx+(mid.Y-start.Y)*dy+(mid.Z-start.Z)*dz)/Math.Max(l2,1e-12);
                                    return t>=-.001&&t<=1.001&&mid.DistanceTo(new(start.X+dx*t,start.Y+dy*t,start.Z+dz*t))<=1;
                                });
                                if(edge is not null)extensionKey=SchematicLayout.PipeEdgeKey(edge.Source,graph.Nodes[edge.A].Point,graph.Nodes[edge.B].Point);
                            }
                            extension[extensionKey]=extension.GetValueOrDefault(extensionKey)+5;failure=ex;continue;
                        }
                        var centered=SheetComposition.Center(scene);
                        string? pipe=DimensionCongestion.PipeToExtend(snapshot,centered);
                        if(pipe is null){plans[key]=scene;return scene;}
                        extension[pipe]=extension.GetValueOrDefault(pipe)+5;
                    }
                }
                catch(SheetCapacityException ex){failure=ex;if(Environment.GetEnvironmentVariable("ISO_LAYOUT_TRACE")=="1")Console.Error.WriteLine($"parts={snapshot.Parts.Count}, density={level}: {ex.Message}");}
            }
            rejectedPlans.Add(key);
            throw failure??new SheetCapacityException("Не удалось устранить наложения размеров.");
        }
        bool AdvanceDensity(PipelineSnapshot snapshot)
        {
            int first=Array.IndexOf(densityOrder,Plan(snapshot).LayoutLevel)+1;
            foreach(int level in densityOrder.Skip(first))try
            {
                var candidate=SheetPlanner.Build(snapshot,quarterTurns:quarterTurns,excludeFlangeHardware:excludeFlangeHardware,symbolMap:symbolMap,compactLevel:level);
                SheetComposition.Center(candidate);
                if(DimensionCongestion.PipeToExtend(snapshot,candidate) is not null)continue;
                plans[Key(snapshot)]=candidate;return true;
            }
            catch(SheetCapacityException){}
            return false;
        }
        try{var single=new[]{new PlannedSheet(1,1,source,SheetComposition.Center(Plan(source)))};var paged=BomPagination.Apply(single);SheetSetIntegrity.Validate(source,paged,excludeFlangeHardware);return paged;}
        catch(SheetCapacityException){}
        var cutPolicy=CutPreferences.Build(source,symbolMap?.Catalog?.Style);
        var mandatory=(source.RequiredCuts??Array.Empty<Connection>()).Select(CutPreferences.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool CanCut(Connection c)=>!cutPolicy.Protected.Contains(CutPreferences.Key(c))||mandatory.Contains(CutPreferences.Key(c));
        var all=source.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
        var runContinuity=new SheetRunContinuity(source);
        bool HasInteriorGap(IEnumerable<string> handles)=>mandatory.Count==0&&runContinuity.HasInteriorGap(handles);
        var leaves=new List<PipelineSnapshot>();
        PipelineSnapshot Subset(HashSet<string> handles)=>TapDimensionReferences.Apply(source,source with
        {
            Parts=source.Parts.Where(p=>handles.Contains(p.Handle)).ToArray(),
            Accessories=source.Accessories?.Where(p=>handles.Contains(p.Properties.GetValueOrDefault("ConnectorHandle",""))).ToArray(),
            Connections=source.Connections.Where(c=>handles.Contains(c.FirstHandle)&&handles.Contains(c.SecondHandle)).ToArray()
        });
        bool Fits(PipelineSnapshot snapshot)
        {
            try{Plan(snapshot);return true;}
            catch(SheetCapacityException){return false;}
        }
        bool FitsPair(PipelineSnapshot parent,PipelineSnapshot first,PipelineSnapshot second)
        {
            try
            {
                var pair=new[]{first,second};
                var pairOwner=pair.SelectMany((s,i)=>s.Parts.Select(p=>(p.Handle,Sheet:i))).ToDictionary(x=>x.Handle,x=>x.Sheet,StringComparer.OrdinalIgnoreCase);
                var pairScenes=pair.Select(Plan).ToArray();
                var pairLayouts=pair.Select((s,i)=>new SchematicLayout(s,quarterTurns,pairScenes[i].LayoutLevel,pairScenes[i].PipeExtensions,symbolMap)).ToArray();
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
                    SheetComposition.Center(SheetPlanner.Build(pair[i],quarterTurns:quarterTurns,excludeFlangeHardware:excludeFlangeHardware,symbolMap:symbolMap,continuations:marks,sheetNumber:i+1,sheetCount:2,contextStrokes:context.Strokes,contextSymbols:context.Symbols,compactLevel:pairScenes[i].LayoutLevel,pipeExtensions:pairScenes[i].PipeExtensions));
                }
                return true;
            }
            catch(SheetCapacityException){return false;}
        }
        var splitCache=new Dictionary<string,PipelineSnapshot[]>(StringComparer.Ordinal);
        void Split(PipelineSnapshot snapshot,bool force=false)
        {
            PlanningExecution.Report("Выбор границ листов");
            var components=SheetConnectivity.Components(snapshot);
            if(components.Count>1){foreach(var component in components)Split(Subset(component));return;}
            if(!force&&splitCache.TryGetValue(Key(snapshot),out var known)){leaves.AddRange(known);return;}
            if(!force&&Fits(snapshot)){leaves.Add(snapshot);return;}
            var handles=snapshot.Parts.Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var links=snapshot.Connections.Distinct().OrderBy(c=>string.Join("|",new[]{c.FirstHandle+":"+c.FirstPort,c.SecondHandle+":"+c.SecondPort}.OrderBy(h=>h,StringComparer.OrdinalIgnoreCase)),StringComparer.OrdinalIgnoreCase).ToArray();
            var adjacency=handles.ToDictionary(h=>h,_=>new HashSet<string>(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
            foreach(var c in links){if(adjacency.TryGetValue(c.FirstHandle,out var a)&&adjacency.TryGetValue(c.SecondHandle,out var b)){a.Add(c.SecondHandle);b.Add(c.FirstHandle);}}
            var distances=handles.ToDictionary(h=>h,_=>int.MaxValue,StringComparer.OrdinalIgnoreCase);
            var queue=new Queue<string>();
            foreach(var p in snapshot.Parts.Where(p=>p.Properties.GetValueOrDefault("PnPClassName")=="Valve")){distances[p.Handle]=0;queue.Enqueue(p.Handle);}
            while(queue.Count>0){var h=queue.Dequeue();foreach(var n in adjacency[h])if(distances[n]>distances[h]+1){distances[n]=distances[h]+1;queue.Enqueue(n);}}
            int Physical(IEnumerable<string> hs)=>hs.Count(h=>all[h].Kind!="AcPpDb3dConnector");
            var candidates=new List<(HashSet<string> A,HashSet<string> B,int ValveDistance,int Balance)>();
            foreach(var link in links)
            {
                PlanningExecution.Check();
                if(!CanCut(link))continue;
                if(!all.ContainsKey(link.FirstHandle)||!all.ContainsKey(link.SecondHandle))continue;
                if(BomCategories.Classify(all[link.FirstHandle])==BomCategory.Supports||BomCategories.Classify(all[link.SecondHandle])==BomCategory.Supports)continue;
                // Cut only at a pipe port: never split the valve / gasket / flange assembly.
                if(all[link.FirstHandle].Properties.GetValueOrDefault("PnPClassName")!="Pipe"&&all[link.SecondHandle].Properties.GetValueOrDefault("PnPClassName")!="Pipe")continue;
                var a=new HashSet<string>(StringComparer.OrdinalIgnoreCase){link.FirstHandle};queue.Enqueue(link.FirstHandle);
                while(queue.Count>0){var h=queue.Dequeue();foreach(var n in adjacency[h])
                {
                    if((h==link.FirstHandle&&n==link.SecondHandle)||(h==link.SecondHandle&&n==link.FirstHandle))continue;
                    if(a.Add(n))queue.Enqueue(n);
                }}
                if(a.Contains(link.SecondHandle))continue; // A loop is not a valid single cut.
                var b=handles.Except(a,StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
                int na=Physical(a),nb=Physical(b);if(na==0||nb==0)continue;
                candidates.Add((a,b,Math.Min(distances[link.FirstHandle],distances[link.SecondHandle]),Math.Abs(na-nb)));
            }
            int PreferredRank(HashSet<string> a)=>source.Connections.Any(c=>cutPolicy.Preferred.Contains(CutPreferences.Key(c))&&a.Contains(c.FirstHandle)!=a.Contains(c.SecondHandle))?0:1;
            var ordered=candidates.OrderBy(c=>PreferredRank(c.A)).ThenBy(c=>c.Balance).ThenBy(c=>c.ValveDistance).ToArray();
            void AddPairedCuts()
            {
                // A ring has no bridge: split it at two pipe connections, preserving rigid assemblies.
                var cuttable=links.Where(c=>CanCut(c)&&handles.Contains(c.FirstHandle)&&handles.Contains(c.SecondHandle)&&BomCategories.Classify(all[c.FirstHandle])!=BomCategory.Supports&&BomCategories.Classify(all[c.SecondHandle])!=BomCategory.Supports&&(all[c.FirstHandle].Properties.GetValueOrDefault("PnPClassName")=="Pipe"||all[c.SecondHandle].Properties.GetValueOrDefault("PnPClassName")=="Pipe")).ToArray();
                var seenPartitions=candidates.Select(c=>string.Join("|",(c.A.Count<=c.B.Count?c.A:c.B).OrderBy(h=>h,StringComparer.Ordinal))).ToHashSet(StringComparer.Ordinal);
                for(int first=0;first<cuttable.Length;first++)for(int second=first+1;second<cuttable.Length;second++)
                {
                    PlanningExecution.Check();
                    var cutA=cuttable[first];var cutB=cuttable[second];
                    bool Blocked(string a,string b)=>a==cutA.FirstHandle&&b==cutA.SecondHandle||a==cutA.SecondHandle&&b==cutA.FirstHandle||a==cutB.FirstHandle&&b==cutB.SecondHandle||a==cutB.SecondHandle&&b==cutB.FirstHandle;
                    var a=new HashSet<string>(StringComparer.OrdinalIgnoreCase){cutA.FirstHandle};var pending=new Queue<string>();pending.Enqueue(cutA.FirstHandle);
                    while(pending.Count>0){string current=pending.Dequeue();foreach(var next in adjacency[current])if(!Blocked(current,next)&&a.Add(next))pending.Enqueue(next);}
                    if(a.Contains(cutA.SecondHandle))continue;
                    var b=handles.Except(a,StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);int na=Physical(a),nb=Physical(b);if(na==0||nb==0)continue;
                    string key=string.Join("|",(a.Count<=b.Count?a:b).OrderBy(h=>h,StringComparer.Ordinal));if(!seenPartitions.Add(key))continue;
                    candidates.Add((a,b,Math.Min(Math.Min(distances[cutA.FirstHandle],distances[cutA.SecondHandle]),Math.Min(distances[cutB.FirstHandle],distances[cutB.SecondHandle])),Math.Abs(na-nb)));
                }
            }
            // First exhaust bridge cuts. A branch is not evidence that the main cycle can
            // be split with one cut: try paired cuts before peeling off further small branches.
            int singleCount=candidates.Count;
            AddPairedCuts();
            var paired=candidates.Skip(singleCount).ToArray();
            // Rank ring and bridge cuts together. Trying every small side branch first
            // consumes the search before any balanced ring split is considered.
            ordered=ordered.Concat(paired).Where(c=>!HasInteriorGap(c.A)&&!HasInteriorGap(c.B)).OrderBy(c=>c.Balance).ThenBy(c=>PreferredRank(c.A)).ThenBy(c=>c.ValveDistance).ToArray();
            foreach(var c in ordered.Take(4))if(Fits(Subset(c.A))&&Fits(Subset(c.B))&&FitsPair(snapshot,Subset(c.A),Subset(c.B))){leaves.Add(Subset(c.A));leaves.Add(Subset(c.B));return;}
            if(ordered.Length==0)throw new SheetCapacityException("Не удалось разделить линию по соединениям труб. Требуется ручное указание границы листов.");
            // Avoid peeling off single components when more than two sheets are required.
            var balanced=ordered.Where(c=>c.Balance<=Physical(handles)*.6).ToArray();
            var pool=balanced.Length>0?balanced:ordered;
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
        var explicitCuts=(source.RequiredCuts??Array.Empty<Connection>()).ToHashSet();
        if(explicitCuts.Count>0)
        {
            var remaining=source.Parts.Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var adjacency=remaining.ToDictionary(h=>h,_=>new HashSet<string>(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
            foreach(var c in source.Connections)if(!explicitCuts.Contains(c)&&adjacency.ContainsKey(c.FirstHandle)&&adjacency.ContainsKey(c.SecondHandle)){adjacency[c.FirstHandle].Add(c.SecondHandle);adjacency[c.SecondHandle].Add(c.FirstHandle);}
            var groups=new List<HashSet<string>>();
            while(remaining.Count>0){string root=remaining.First();var group=new HashSet<string>(StringComparer.OrdinalIgnoreCase){root};remaining.Remove(root);var q=new Queue<string>();q.Enqueue(root);while(q.Count>0)foreach(var next in adjacency[q.Dequeue()])if(remaining.Remove(next)){group.Add(next);q.Enqueue(next);}groups.Add(group);}
            if(groups.Count>1)foreach(var group in groups)Split(Subset(group));else Split(source);
        }
        else Split(source);
        var rejectedMerges=new HashSet<string>(StringComparer.Ordinal);
        var visitedContinuationStates=new HashSet<string>(StringComparer.Ordinal);
        bool allowMerges=true;
        RetryWithContinuations:
        PlanningExecution.Report("Согласование листов и переходов");
        string state=string.Join(";",leaves.Select(s=>Key(s)+":"+Plan(s).LayoutLevel).OrderBy(k=>k,StringComparer.Ordinal));
        if(!visitedContinuationStates.Add(state))throw new PlanningLimitException("Повторилась компоновка листов при размещении переходов. Расчёт остановлен без изменения итоговых файлов. Задайте другую границу листов или ракурс.");
        // Recursive cuts are only a starting partition. Repack adjacent leaves afterwards;
        // otherwise one difficult annotation can leave several almost empty pages behind.
        bool merged;
        do
        {
            merged=false;
            var neighbours=allowMerges?(from a in Enumerable.Range(0,leaves.Count) from b in Enumerable.Range(a+1,leaves.Count-a-1) orderby leaves[a].Parts.Count+leaves[b].Parts.Count select(A:a,B:b)).ToArray():Array.Empty<(int A,int B)>();
            foreach(var pair in neighbours)
            {
                var combined=Subset(leaves[pair.A].Parts.Concat(leaves[pair.B].Parts).Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase));
                if(rejectedMerges.Contains(Key(combined))||HasInteriorGap(combined.Parts.Select(p=>p.Handle))||!Fits(combined))continue;
                leaves[pair.A]=combined;leaves.RemoveAt(pair.B);merged=true;break;
            }
        }while(merged);
        // Continuation repair must strictly refine this partition. Running the packing
        // pass again can rejoin the very pieces that were just split for their callouts.
        allowMerges=false;
        var owner=leaves.SelectMany((s,i)=>s.Parts.Select(p=>(p.Handle,Sheet:i))).ToDictionary(x=>x.Handle,x=>x.Sheet,StringComparer.OrdinalIgnoreCase);
        var continuations=leaves.Select(_=>new List<SheetContinuation>()).ToArray();
        int mark=0;var boundaries=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var c in source.Connections)
        {
            if(!owner.TryGetValue(c.FirstHandle,out int a)||!owner.TryGetValue(c.SecondHandle,out int b)||a==b)continue;
            string key=string.Join("|",new[]{c.FirstHandle+":"+c.FirstPort,c.SecondHandle+":"+c.SecondPort}.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase));
            if(!boundaries.Add(key))continue;
            string label="П"+(++mark);
            continuations[a].Add(new(c.FirstHandle,c.FirstPort,label,b+1));
            continuations[b].Add(new(c.SecondHandle,c.SecondPort,label,a+1));
        }
        // Plan welds from the full topology, then assign each one once, including split boundaries.
        var welds=WeldPlanner.Build(source,p=>new(p.X,p.Y)).Markers;
        var baseScenes=leaves.Select(Plan).ToArray();
        var layouts=leaves.Select((s,i)=>new SchematicLayout(s,quarterTurns,baseScenes[i].LayoutLevel,baseScenes[i].PipeExtensions,symbolMap)).ToArray();
        var result=new List<PlannedSheet>();
        for(int i=0;i<leaves.Count;i++)
        {
            var snapshot=leaves[i];var layout=layouts[i];
            var context=ContinuationContextPlanner.Build(i,leaves,baseScenes,layouts,source,owner);
            SheetScene scene;
            try{scene=SheetPlanner.Build(snapshot,quarterTurns:quarterTurns,excludeFlangeHardware:excludeFlangeHardware,symbolMap:symbolMap,continuations:continuations[i],sheetNumber:i+1,sheetCount:leaves.Count,contextStrokes:context.Strokes,contextSymbols:context.Symbols,compactLevel:baseScenes[i].LayoutLevel,pipeExtensions:baseScenes[i].PipeExtensions);}
            catch(SheetCapacityException)
            {
                // Continuation labels also consume paper space. Repartition instead of allowing overlaps.
                if(AdvanceDensity(snapshot))goto RetryWithContinuations;
                rejectedMerges.Add(Key(snapshot));leaves.RemoveAt(i);Split(snapshot,force:true);goto RetryWithContinuations;
            }
            var owned=welds.Where(w=>owner.GetValueOrDefault(w.SourceId,owner[w.FirstHandle])==i).Select(w=>
            {
                string h=owner[w.FirstHandle]==i?w.FirstHandle:w.SecondHandle;
                string port=h==w.FirstHandle?w.FirstPort:w.SecondPort;
                var point=all[h].Ports.Single(p=>p.Name==port).Position;
                if(w.JointType=="Tap"&&w.ModelPosition is {} modelPosition&&snapshot.Parts.Any(p=>p.Handle==(w.FirstEndCondition=="TAP"?w.FirstHandle:w.SecondHandle))){point=modelPosition;h=w.FirstEndCondition=="TAP"?w.FirstHandle:w.SecondHandle;}
                return w with{Position=layout.Project(point,h)};
            }).ToArray();
            scene=scene with{Welds=owned};
            try{scene=SheetComposition.Center(scene);}
            catch(SheetCapacityException){if(AdvanceDensity(snapshot))goto RetryWithContinuations;
                rejectedMerges.Add(Key(snapshot));leaves.RemoveAt(i);Split(snapshot,force:true);goto RetryWithContinuations;}
            result.Add(new(i+1,leaves.Count,snapshot,scene));
        }
        var paginated=BomPagination.Apply(result);
        SheetSetIntegrity.Validate(source,paginated,excludeFlangeHardware);
        return paginated;
    }
}
