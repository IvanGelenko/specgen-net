using System.Text.Json;

namespace IsoGenerator.Core;

public sealed record RememberedLayout(PipelineSnapshot Source,IReadOnlyList<PlannedSheet> Sheets,double MinimumPipeLength=10,string? AdvancedSettingsSignature=null);

internal static class IncrementalSheetPlanner
{
    public static IReadOnlyList<PlannedSheet>? TryRefresh(
        PipelineSnapshot source,RememberedLayout previous,Func<HashSet<string>,PipelineSnapshot> subset,
        Func<PipelineSnapshot,SheetScene> plan,
        Func<PipelineSnapshot,SheetScene,Func<int,double,IReadOnlyDictionary<string,double>?,double?,SheetScene>,SheetScene> fillPaper,
        IsoSymbolMap? map,int turns,bool excludeHardware,bool instrumentsOnly,
        Func<PipelineSnapshot,PreparedSheetInput> prepare,PlanningExecution execution)
    {
        try
        {
            return Refresh();
        }
        catch(Exception ex) when(ex is SheetCapacityException or InvalidDataException or KeyNotFoundException)
        {
            if(Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1")
                Console.Error.WriteLine("Сохранённая граница: "+ex.Message);
            return null;
        }

        IReadOnlyList<PlannedSheet>? Refresh()
        {
            if(previous.MinimumPipeLength!=execution.MinimumPipeLength||
                previous.AdvancedSettingsSignature!=IsoAdvancedSettings.Signature)return null;
            var firstScene=previous.Sheets.FirstOrDefault()?.Scene;
            if(firstScene is null||firstScene.QuarterTurns!=turns||
               firstScene.ExcludeFlangeHardware!=excludeHardware||firstScene.InstrumentCalloutsOnly!=instrumentsOnly)return null;
            if(previous.Sheets.Any(sheet=>sheet is null||sheet.Snapshot?.Parts is null||
                sheet.Scene?.Annotations is null||sheet.Scene.Strokes is null||
                sheet.Scene.Dimensions is null||sheet.Scene.BillOfMaterials is null))return null;
            if(Fingerprint(source)==Fingerprint(previous.Source))
            {
                SheetSetIntegrity.Validate(source,previous.Sheets,excludeHardware);
                return previous.Sheets;
            }
            var old=RestoreDiagramNumbers(previous.Sheets);
            if(old.Length==0||old.Any(sheet=>sheet.BaseScene is null))return null;
            var oldOwner=old.SelectMany((sheet,index)=>sheet.Snapshot.Parts.Select(part=>(part.Handle,index)))
                .ToDictionary(item=>item.Handle,item=>item.index,StringComparer.OrdinalIgnoreCase);
            var active=source.Parts.Select(part=>part.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var owner=oldOwner.Where(item=>active.Contains(item.Key)).ToDictionary(item=>item.Key,item=>item.Value,StringComparer.OrdinalIgnoreCase);
            var adjacency=active.ToDictionary(handle=>handle,_=>new List<string>(),StringComparer.OrdinalIgnoreCase);
            foreach(var connection in source.Connections)
            {
                if(!adjacency.TryGetValue(connection.FirstHandle,out var first)||
                   !adjacency.TryGetValue(connection.SecondHandle,out var second))continue;
                first.Add(connection.SecondHandle);
                second.Add(connection.FirstHandle);
            }
            var pending=active.Where(handle=>!owner.ContainsKey(handle)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            while(pending.Count>0)
            {
                var first=pending.First();pending.Remove(first);
                var group=new List<string>{first};
                var neighbors=new HashSet<int>();
                for(int i=0;i<group.Count;i++)
                    foreach(var next in adjacency[group[i]])
                    {
                        if(owner.TryGetValue(next,out int index))neighbors.Add(index);
                        else if(pending.Remove(next))group.Add(next);
                    }
                if(neighbors.Count!=1)return null;
                foreach(var handle in group)owner.Add(handle,neighbors.Single());
            }
            var rememberedCuts=(previous.Source.RequiredCuts??Array.Empty<Connection>()).Select(CutPreferences.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(!rememberedCuts.SetEquals((source.RequiredCuts??Array.Empty<Connection>()).Select(CutPreferences.Key)))return null;
            var oldCuts=Boundaries(previous.Source,oldOwner);
            var newCuts=Boundaries(source,owner);
            if(!oldCuts.Select(CutPreferences.Key).ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(newCuts.Select(CutPreferences.Key)))return null;
            if((source.RequiredCuts??Array.Empty<Connection>()).Any(c=>!owner.TryGetValue(c.FirstHandle,out int a)||
                !owner.TryGetValue(c.SecondHandle,out int b)||a==b))return null;
            var starts=(source.IsoMarkers??Array.Empty<ModelIsoMarker>()).Where(marker=>marker.InformationType==5).ToArray();
            if(starts.Length>1||starts.Length==1&&(!owner.TryGetValue(starts[0].HostHandle,out int start)||start!=0))return null;
            foreach(var unit in InstallationUnits.Find(source,execution))
            {
                int pages=unit.Handles.Select(handle=>owner[handle]).Distinct().Count();
                if(pages>1)
                {
                    int priorPages=unit.Handles.Where(oldOwner.ContainsKey).Select(handle=>oldOwner[handle]).Distinct().Count();
                    if(pages>priorPages||unit.Handles.Any(handle=>!oldOwner.ContainsKey(handle)))return null;
                }
            }
            var leaves=Enumerable.Range(0,old.Length).Select(index=>subset(owner.Where(item=>item.Value==index)
                .Select(item=>item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase))).ToArray();
            if(leaves.Any(leaf=>leaf.Parts.Count==0||SheetConnectivity.Components(leaf).Count!=1))return null;
            var changed=leaves.Select((leaf,i)=>Fingerprint(leaf)!=Fingerprint(old[i].Snapshot)).ToArray();
            var raw=leaves.Select((leaf,i)=>changed[i]?plan(leaf):old[i].BaseScene!).ToArray();
            var welds=WeldPlanner.Build(source,p=>new(p.X,p.Y)).Markers;
            var partition=new PartitionLayout(source,leaves,raw,welds,prepare,map,turns,excludeHardware,instrumentsOnly,execution);
            var oldLeaves=old.Select(sheet=>sheet.Snapshot).ToArray();
            var oldRaw=old.Select(sheet=>sheet.BaseScene!).ToArray();
            var oldWelds=WeldPlanner.Build(previous.Source,p=>new(p.X,p.Y)).Markers;
            var oldPartition=new PartitionLayout(previous.Source,oldLeaves,oldRaw,oldWelds,prepare,map,turns,excludeHardware,instrumentsOnly,execution);
            var result=new PlannedSheet[leaves.Length];
            var rebuilt=new List<int>();
            static string WeldKey(IEnumerable<WeldMarker> markers)=>JsonSerializer.Serialize(markers
                .Select(marker=>marker with{Position=default})
                .OrderBy(marker=>marker.SourceId,StringComparer.Ordinal)
                .ThenBy(marker=>marker.FirstHandle,StringComparer.Ordinal)
                .ThenBy(marker=>marker.FirstPort,StringComparer.Ordinal)
                .ThenBy(marker=>marker.SecondHandle,StringComparer.Ordinal)
                .ThenBy(marker=>marker.SecondPort,StringComparer.Ordinal));
            for(int i=0;i<leaves.Length;i++)
            {
                var leaf=leaves[i];
                bool weldsChanged=WeldKey(partition.Welds(i))!=WeldKey(old[i].Scene.Welds??Array.Empty<WeldMarker>());
                bool neighborChanged=partition.Neighbors(i).Any(index=>changed[index]);
                if(!changed[i]&&!weldsChanged&&(!neighborChanged||
                    ContextKey(partition.Context(i))==ContextKey(oldPartition.Context(i))))
                {
                    result[i]=old[i] with{Snapshot=leaf};
                    continue;
                }

                if(Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1")
                    Console.Error.WriteLine($"Обновление листа {i+1}: модель={changed[i]}, сосед={neighborChanged}.");
                result[i]=partition.Compose(i);
                if(DimensionCongestion.PipeToExtend(leaf,result[i].Scene) is not null)return null;
                rebuilt.Add(i);
            }
            foreach(int index in rebuilt)
            {
                if(!execution.CanTryOptionalVariant())break;
                (partition,result)=partition.TryExpand(index,result,fillPaper);
            }
            var paginated=BomPagination.Apply(result);
            SheetSetIntegrity.Validate(source,paginated,excludeHardware);
            return paginated;
        }


    }

    static PlannedSheet[] RestoreDiagramNumbers(IReadOnlyList<PlannedSheet> pages)
    {
        var geometry=pages.Where(page=>!page.Scene.BomOnly).ToArray();
        var numbers=geometry.Select((page,index)=>(page.Number,Diagram:index+1))
            .ToDictionary(item=>item.Number,item=>item.Diagram);
        return geometry.Select((page,index)=>
        {
            int next=index+1<geometry.Length?geometry[index+1].Number:pages.Count+1;
            var rows=pages.Where(item=>item.Number>=page.Number&&item.Number<next)
                .SelectMany(item=>item.Scene.BillOfMaterials).ToArray();
            var annotations=page.Scene.Annotations.Select(annotation=>annotation.Role=="continuation"
                ?DocumentSheetReferences.RenumberLocal(annotation,numbers)
                :annotation).ToArray();
            return page with{Number=index+1,Total=geometry.Length,
                Scene=page.Scene with{BillOfMaterials=rows,Annotations=annotations}};
        }).ToArray();
    }

    // Translation of two independently projected origins can differ by a few ULPs.
    static string ContextKey(ContinuationContext context)
    {
        static SheetPoint Round(SheetPoint point)=>new(Math.Round(point.X,6)+0,Math.Round(point.Y,6)+0);
        return JsonSerializer.Serialize(new ContinuationContext(
            context.Strokes.Select(stroke=>stroke with{Points=stroke.Points.Select(Round).ToArray()}).ToArray(),
            context.Symbols.Select(symbol=>symbol with{Origin=Round(symbol.Origin),XAxis=Round(symbol.XAxis),YAxis=Round(symbol.YAxis)}).ToArray()));
    }

    static Connection[] Boundaries(PipelineSnapshot snapshot,IReadOnlyDictionary<string,int> owner)=>
        snapshot.Connections.Where(c=>owner.TryGetValue(c.FirstHandle,out int a)&&owner.TryGetValue(c.SecondHandle,out int b)&&a!=b)
            .DistinctBy(CutPreferences.Key,StringComparer.OrdinalIgnoreCase).ToArray();

    static string Fingerprint(PipelineSnapshot snapshot)
    {
        var handles=snapshot.Parts.Select(part=>part.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return JsonSerializer.Serialize(new
        {
            Parts=Normalize(snapshot.Parts),
            Connections=snapshot.Connections.Select(CutPreferences.Key).OrderBy(key=>key,StringComparer.Ordinal),
            Accessories=Normalize(snapshot.Accessories??Array.Empty<Part>()),
            Markers=(snapshot.IsoMarkers??Array.Empty<ModelIsoMarker>()).Where(marker=>handles.Contains(marker.HostHandle)).OrderBy(marker=>marker.Handle,StringComparer.Ordinal),
            Cuts=(snapshot.RequiredCuts??Array.Empty<Connection>()).Select(CutPreferences.Key).OrderBy(key=>key,StringComparer.Ordinal),
            TapAxes=(snapshot.BoundaryTapAxes??Array.Empty<BoundaryTapAxis>()).Where(axis=>handles.Contains(axis.Handle)).OrderBy(axis=>axis.Handle,StringComparer.Ordinal).ThenBy(axis=>axis.Port,StringComparer.Ordinal),
            Context=(snapshot.ExternalLineContexts??Array.Empty<ExternalLineContext>()).Where(item=>handles.Contains(item.OwnerHandle))
                .OrderBy(item=>item.OwnerHandle,StringComparer.Ordinal).ThenBy(item=>item.OwnerPort,StringComparer.Ordinal)
        });
    }

    static IEnumerable<Part> Normalize(IEnumerable<Part> parts)=>parts.OrderBy(part=>part.Handle,StringComparer.Ordinal)
        .Select(part=>part with
        {
            Ports=part.Ports.OrderBy(port=>port.Name,StringComparer.Ordinal).ToArray(),
            Properties=part.Properties.OrderBy(item=>item.Key,StringComparer.Ordinal).ToDictionary(item=>item.Key,item=>item.Value)
        });
}
