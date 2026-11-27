namespace IsoGenerator.Core;

internal sealed record SheetGeometry(int Level,double Margin,IReadOnlyDictionary<string,double>? Extensions,double? Scale)
{
    public static SheetGeometry From(SheetScene scene)=>
        new(scene.LayoutLevel,scene.PaperMargin,scene.PipeExtensions,scene.GeometryScale);
}

// A candidate partition owns its continuations, projections, and weld assignments.
// Replacing one projection produces another candidate while the feasible package is retained.
internal sealed class PartitionLayout
{
    readonly PipelineSnapshot source;
    readonly SheetScene[] raw;
    readonly SchematicLayout[] projections;
    readonly Func<PipelineSnapshot,PreparedSheetInput> prepare;
    readonly IsoSymbolMap? symbols;
    readonly int turns;
    readonly bool excludeHardware;
    readonly bool instrumentsOnly;
    readonly PlanningExecution execution;
    readonly IReadOnlyDictionary<string,int> owner;
    readonly IReadOnlyDictionary<string,Part> parts;
    readonly List<SheetContinuation>[] continuations;
    readonly List<WeldMarker>[] welds;

    public IReadOnlyList<PipelineSnapshot> Snapshots {get;}
    public int Count=>Snapshots.Count;

    public PartitionLayout(PipelineSnapshot source,IReadOnlyList<PipelineSnapshot> ordered,SheetScene[] raw,
        IReadOnlyList<WeldMarker> allWelds,Func<PipelineSnapshot,PreparedSheetInput> prepare,IsoSymbolMap? symbols,
        int turns,bool excludeHardware,bool instrumentsOnly,PlanningExecution execution)
    {
        this.source=source;
        this.raw=raw;
        this.prepare=prepare;
        this.symbols=symbols;
        this.turns=turns;
        this.excludeHardware=excludeHardware;
        this.instrumentsOnly=instrumentsOnly;
        this.execution=execution;
        Snapshots=ordered;
        owner=ordered.SelectMany((sheet,index)=>sheet.Parts.Select(part=>(part.Handle,index)))
            .ToDictionary(part=>part.Handle,part=>part.index,StringComparer.OrdinalIgnoreCase);
        parts=source.Parts.ToDictionary(part=>part.Handle,StringComparer.OrdinalIgnoreCase);
        continuations=ordered.Select(_=>new List<SheetContinuation>()).ToArray();
        var boundaries=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var connection in source.Connections)
        {
            if(!owner.TryGetValue(connection.FirstHandle,out int first)||
               !owner.TryGetValue(connection.SecondHandle,out int second)||first==second||
               !boundaries.Add(CutPreferences.Key(connection)))continue;
            string mark="П"+boundaries.Count;
            continuations[first].Add(new(connection.FirstHandle,connection.FirstPort,mark,second+1));
            continuations[second].Add(new(connection.SecondHandle,connection.SecondPort,mark,first+1));
        }
        welds=ordered.Select(_=>new List<WeldMarker>()).ToArray();
        foreach(var weld in allWelds)
            welds[owner.GetValueOrDefault(weld.SourceId,owner[weld.FirstHandle])].Add(weld);
        projections=ordered.Select((_,i)=>Project(i,SheetGeometry.From(raw[i]))).ToArray();
    }

    PartitionLayout(PartitionLayout previous,int index,SheetGeometry geometry)
    {
        source=previous.source;
        raw=previous.raw.ToArray();
        projections=previous.projections.ToArray();
        prepare=previous.prepare;
        symbols=previous.symbols;
        turns=previous.turns;
        excludeHardware=previous.excludeHardware;
        instrumentsOnly=previous.instrumentsOnly;
        execution=previous.execution;
        owner=previous.owner;
        parts=previous.parts;
        continuations=previous.continuations;
        welds=previous.welds;
        Snapshots=previous.Snapshots;
        var snapshot=Snapshots[index];
        raw[index]=SheetPlanner.Build(prepare(snapshot),turns,symbols,compactLevel:geometry.Level,
            pipeExtensions:geometry.Extensions,excludeFlangeHardware:excludeHardware,
            instrumentCalloutsOnly:instrumentsOnly,paperMargin:geometry.Margin,fixedScale:geometry.Scale,execution:execution);
        projections[index]=Project(index,geometry);
    }

    public IEnumerable<int> Neighbors(int index)=>
        continuations[index].Select(continuation=>continuation.OtherSheet-1).Distinct();

    public IEnumerable<(int First,int Second)> NeighborPairs()=>
        from first in Enumerable.Range(0,Count)
        from second in Neighbors(first)
        where second>first
        orderby Snapshots[first].Parts.Count+Snapshots[second].Parts.Count,first,second
        select (first,second);

    public (PartitionLayout Layout,PlannedSheet[] Sheets) TryExpand(int index,PlannedSheet[] sheets,
        Func<PipelineSnapshot,SheetScene,Func<int,double,IReadOnlyDictionary<string,double>?,double?,SheetScene>,SheetScene> fill)
    {
        var baseline=sheets[index].Scene;
        var expanded=fill(Snapshots[index],baseline,(level,margin,extensions,scale)=>
            ComposeScene(index,new(level,margin,extensions,scale)));
        if(ReferenceEquals(baseline,expanded))return (this,sheets);

        var geometry=SheetGeometry.From(expanded);
        var affected=Neighbors(index).Append(index).ToArray();
        try
        {
            execution.Check();
            var trial=new PartitionLayout(this,index,geometry);
            var updated=sheets.ToArray();
            foreach(int member in affected)
            {
                var candidate=trial.Compose(member);
                if(LayoutQuality.Dimensions(candidate.Scene).CompareTo(LayoutQuality.Dimensions(sheets[member].Scene))>0)
                    return (this,sheets);
                updated[member]=candidate;
            }
            if(LayoutQuality.Compare(updated,sheets)<0)return (trial,updated);
        }
        catch(SheetCapacityException failure)
        {
            execution.Reject(failure,Snapshots[index].Parts.Count,geometry.Level,geometry.Margin,geometry.Extensions,geometry.Scale);
        }
        catch(PlanningLimitException){}
        return (this,sheets);
    }

    public ContinuationContext Context(int index)=>
        ContinuationContextPlanner.Build(index,Snapshots,raw,projections,source,owner);

    public IReadOnlyList<WeldMarker> Welds(int index)=>welds[index];

    public SheetScene BaseScene(int index)=>raw[index];

    public PartitionLayout WithGeometry(int index,SheetGeometry geometry)=>new(this,index,geometry);

    public PlannedSheet Compose(int index)=>
        new(index+1,Count,Snapshots[index],SheetComposition.Center(ComposeScene(index)),raw[index]);

    public SheetScene ComposeScene(int index,SheetGeometry? trial=null)
    {
        var snapshot=Snapshots[index];
        var geometry=trial??SheetGeometry.From(raw[index]);
        var layouts=projections;
        if(trial is not null)
        {
            layouts=projections.ToArray();
            layouts[index]=Project(index,geometry);
        }
        var context=ContinuationContextPlanner.Build(index,Snapshots,raw,layouts,source,owner);
        var scene=SheetPlanner.Build(prepare(snapshot),turns,symbols,continuations[index],index+1,Count,
            context.Strokes,context.Symbols,geometry.Level,geometry.Extensions,excludeHardware,
            instrumentsOnly,geometry.Margin,geometry.Scale,execution,checkValveClearance:trial is not null);
        var owned=welds[index].Select(weld=>
        {
            bool first=owner[weld.FirstHandle]==index;
            string handle=first?weld.FirstHandle:weld.SecondHandle;
            string port=first?weld.FirstPort:weld.SecondPort;
            var point=parts[handle].Ports.Single(p=>p.Name==port).Position;
            string tap=weld.FirstEndCondition=="TAP"?weld.FirstHandle:weld.SecondHandle;
            if(weld.JointType=="Tap"&&owner[tap]==index&&weld.ModelPosition is {} axis)
            {
                point=axis;
                handle=tap;
            }
            return weld with{Position=layouts[index].Project(point,handle)};
        }).ToArray();
        return scene with{Welds=owned};
    }

    public void Reject(int index,SheetCapacityException failure)
    {
        var geometry=SheetGeometry.From(raw[index]);
        execution.Reject(failure,Snapshots[index].Parts.Count,geometry.Level,geometry.Margin,geometry.Extensions,geometry.Scale);
    }

    SchematicLayout Project(int index,SheetGeometry geometry)=>
        new(Snapshots[index],prepare(Snapshots[index]).Topology,turns,geometry.Level,geometry.Extensions,
            symbols is null?null:symbols.ResolveGeometry,geometry.Margin,geometry.Scale,execution);
}
