using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PnP3dObjects;
using IsoGenerator.Core;
using Connection=IsoGenerator.Core.Connection;
using ModelPart=IsoGenerator.Core.Part;
using PlantPart=Autodesk.ProcessPower.PnP3dObjects.Part;
using PlantPort=Autodesk.ProcessPower.PnP3dObjects.Port;

namespace IsoGenerator.AutoCAD;

internal static class PlantSnapshotReader
{
    static ModelPoint Point(Autodesk.AutoCAD.Geometry.Point3d p)=>new(p.X,p.Y,p.Z);
    static ModelPoint Vector(Autodesk.AutoCAD.Geometry.Vector3d p)=>new(p.X,p.Y,p.Z);
    internal static PipelineSnapshot Capture(Database db,string filename,ObjectId seedId,string? selectedLine=null,IReadOnlyCollection<string>? selectedHandles=null,IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>>? propertyHints=null,string? projectIsoDirectory=null,CancellationToken cancellationToken=default,Action<int>? report=null,IReadOnlyDictionary<string,string>? drawingLines=null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(db.Insunits!=UnitsValue.Millimeters)throw new InvalidOperationException("Поддерживаются чертежи в миллиметрах.");
        using var tr=db.TransactionManager.StartOpenCloseTransaction();
        if(!seedId.IsNull&&tr.GetObject(seedId,OpenMode.ForRead) is not PlantPart)throw new InvalidOperationException("Выберите компонент Plant 3D.");
        var project=Autodesk.ProcessPower.PlantInstance.PlantApplication.CurrentProject??throw new InvalidOperationException("Откройте проект Plant 3D.");
        // Resolve links against the actual drawing, as the audit reader does.
        // A project-level manager can miss rows for a DWG opened through another path.
        var manager=Autodesk.ProcessPower.DataLinks.DataLinksManager.GetManager(db)??throw new InvalidOperationException("Не найден менеджер данных Plant 3D текущего DWG.");
        var projectManager=(project.ProjectParts["Piping"] as Autodesk.ProcessPower.P3dProjectParts.PipingProject)?.DataLinksManager;
        Dictionary<string,string> ReadProperties(DataLinksManager links,ObjectId id)
        {
            int row;
            try{row=links.FindAcPpRowId(id);}
            catch(System.Exception ex) when(ex.Message.Contains("ObjectDoesNotHaveLink",StringComparison.OrdinalIgnoreCase)){row=0;}
            return row>0?links.GetAllProperties(row,true).ToDictionary(p=>p.Key,p=>p.Value??"",StringComparer.OrdinalIgnoreCase):new(StringComparer.OrdinalIgnoreCase);
        }
        Dictionary<string,string> Properties(ObjectId id)
        {
            var values=ReadProperties(manager,id);
            if(projectManager is not null&&!ReferenceEquals(manager,projectManager)&&
               string.IsNullOrWhiteSpace(values.GetValueOrDefault("PnPClassName")))
            {
                foreach(var pair in ReadProperties(projectManager,id))
                    if(string.IsNullOrWhiteSpace(values.GetValueOrDefault(pair.Key)))values[pair.Key]=pair.Value;
            }
            if(drawingLines?.TryGetValue(id.Handle.ToString(),out var resolved)==true)
                values["LineNumberTag"]=resolved;
            return values;
        }
        var seed=seedId.IsNull?new Dictionary<string,string>():Properties(seedId);
        if(selectedLine is null&&seed.Count==0)throw new InvalidOperationException("Выбранный компонент не связан с базой проекта Plant 3D. Выберите трубу из открытого проекта.");
        string line=selectedLine??seed.GetValueOrDefault("LineNumberTag","");
        var scope=selectedHandles?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(string.IsNullOrWhiteSpace(line))throw new InvalidOperationException("У выбранной детали не заполнен номер линии.");
        var propertyCache=new Dictionary<ObjectId,Dictionary<string,string>>();var queue=new Queue<ObjectId>();var warnings=new List<string>();
        var model=(BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db),OpenMode.ForRead);
        foreach(ObjectId id in model)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(tr.GetObject(id,OpenMode.ForRead) is not PlantPart||id.ObjectClass.Name=="AcPpDb3dConnector")continue;
            if(scope is not null&&!scope.Contains(id.Handle.ToString()))continue;
            Dictionary<string,string> props;
            try{props=Properties(id);}catch(System.Exception ex){throw new InvalidDataException($"Не прочитаны свойства {id.Handle}: {ex.Message}");}
            if(propertyHints?.TryGetValue(id.Handle.ToString(),out var hints)==true)foreach(var pair in hints)props[pair.Key]=pair.Value;
            if(id.ObjectClass.Name=="AcPpDb3dSupport"&&string.IsNullOrWhiteSpace(props.GetValueOrDefault("PnPClassName")))
            {
                try
                {
                    var native=((PlantPart)tr.GetObject(id,OpenMode.ForRead)).PartSizeProperties;
                    if(native is not null)
                        foreach(var name in native.PropNames.OfType<string>())
                        {
                            var value=Convert.ToString(native.PropValue(name),System.Globalization.CultureInfo.InvariantCulture);
                            if(!string.IsNullOrWhiteSpace(value)&&string.IsNullOrWhiteSpace(props.GetValueOrDefault(name)))
                                props[name]=value;
                        }
                }
                catch(System.Exception ex){warnings.Add($"{id.Handle}: не прочитаны встроенные свойства опоры: {ex.Message}");}
            }
            if(scope is not null)props["LineNumberTag"]=line;
            if(string.Equals(props.GetValueOrDefault("LineNumberTag")?.Trim(),line,StringComparison.OrdinalIgnoreCase)){propertyCache[id]=props;queue.Enqueue(id);}
        }
        var seen=new HashSet<ObjectId>();var parts=new List<ModelPart>();var accessories=new List<ModelPart>();var links=new Dictionary<string,Connection>();
        using var connections=new ConnectionManager();
        string LineOf(ObjectId id)
        {
            string handle=id.Handle.ToString();
            if(drawingLines?.TryGetValue(handle,out var resolved)==true)return resolved.Trim();
            if(scope?.Contains(handle)==true)return line;
            return (propertyCache.GetValueOrDefault(id)??Properties(id)).GetValueOrDefault("LineNumberTag","").Trim();
        }
        while(queue.Count>0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id=queue.Dequeue();if(!seen.Add(id))continue;
            if(seen.Count%25==1)report?.Invoke(seen.Count);
            var part=(PlantPart)tr.GetObject(id,OpenMode.ForRead);var ports=part.GetPorts(PortType.All);var readPorts=new List<IsoGenerator.Core.Port>();
            for(int i=0;i<ports.Count;i++)
            {
                PlantPort port=ports[i];string end="";
                try{end=part.PortProperties(port.Name)?.EndCondition??"";}catch(System.Exception ex){warnings.Add($"{id.Handle}:{port.Name}: {ex.Message}");}
                if(end.Length==0)end=port.EndType??"";
                readPorts.Add(new(port.Name,Point(port.Position),Vector(port.Direction),end));
            }
            parts.Add(new(filename,id.Handle.ToString(),id.ObjectClass.Name,line,readPorts,propertyCache.GetValueOrDefault(id)??new(),part is InlineAsset asset?Vector(asset.ZAxis):null));
            var attached=connections.GetConnections(id);
            bool OwnsAccessories()
            {
                string owner=LineOf(id);
                if(owner.Length==0)
                {
                    // Unassigned boundary joints have the same owner whichever line is generated first.
                    var neighborLines=new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    for(int i=0;i<attached.Count;i++)
                    {
                        var link=attached[i];
                        var other=link.ObjectId1==id?link.ObjectId2:link.ObjectId1;
                        if(other.IsNull||other.Database!=db||other.ObjectClass.Name=="AcPpDb3dConnector")continue;
                        string neighborLine=LineOf(other);
                        if(neighborLine.Length>0)neighborLines.Add(neighborLine);
                    }
                    owner=neighborLines.Min??"";
                }
                if(owner.Length==0)warnings.Add($"{id.Handle}: не удалось определить линию соединения для учёта его деталей.");
                return owner.Equals(line,StringComparison.OrdinalIgnoreCase);
            }
            if(part is Connector connector&&connector.NumSubParts>0&&OwnsAccessories())
            {
                for(int index=0;index<connector.NumSubParts;index++)
                {
                    var subpart=connector.GetSubPart(index);if(subpart is WeldSubPart)continue;
                    var properties=subpart.PartSizeProperties;
                    if(properties is null)continue;
                    var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    foreach(var propertyName in properties.PropNames)
                    {if(propertyName is not string key||string.IsNullOrWhiteSpace(key))continue;var value=properties.PropValue(key);if(value is not null&&value is not DBNull)values[key]=Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)??"";}
                    string kind=properties.Type??"";
                    if(kind.Length==0)kind=subpart is BoltSetSubPart?"BoltSet":values.GetValueOrDefault("PnPClassName","");
                    values["PnPClassName"]=kind;values["ConnectorHandle"]=id.Handle.ToString();
                    if(string.IsNullOrWhiteSpace(values.GetValueOrDefault("DesignStd")))values["DesignStd"]=values.GetValueOrDefault("CompatibleStandard","");
                    if(subpart is BoltSetSubPart bolts)
                    {
                        values["Length"]=bolts.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        values["IsoQuantity"]=values.GetValueOrDefault("NumberInSet","1");values["IsoQuantityUnit"]="компл.";
                        if(!values.ContainsKey("NumberInSet"))warnings.Add($"{id.Handle}: у крепёжного комплекта нет NumberInSet; учтён один комплект соединения.");
                    }
                    else {values["IsoQuantity"]="1";values["IsoQuantityUnit"]="шт.";}
                    accessories.Add(new(filename,id.Handle+"#"+index,"ConnectorSubPart",line,Array.Empty<IsoGenerator.Core.Port>(),values));
                }
            }
            for(int i=0;i<attached.Count;i++)
            {
                var c=attached[i];
                if(c.ObjectId1.IsNull||c.ObjectId2.IsNull){warnings.Add($"{id.Handle}: внешняя или неполная связь");continue;}
                if(c.ObjectId1.Database!=db||c.ObjectId2.Database!=db)
                {
                    warnings.Add($"{id.Handle}: соединение с объектом другого DWG, внешний контекст пропущен.");
                    continue;
                }
                var connection=new Connection(c.ObjectId1.Handle.ToString(),c.Port1.Name,c.ObjectId2.Handle.ToString(),c.Port2.Name);
                string key=string.Join("|",new[]{connection.FirstHandle+":"+connection.FirstPort,connection.SecondHandle+":"+connection.SecondPort}.OrderBy(s=>s,StringComparer.Ordinal));links[key]=connection;
                foreach(var other in new[]{c.ObjectId1,c.ObjectId2})if(other.Database==db&&other.ObjectClass.Name=="AcPpDb3dConnector"&&!seen.Contains(other))queue.Enqueue(other);
            }
        }
        var own=parts.Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var drawingMarkers=IsoMarkerReader.Read(model,tr,cancellationToken,warnings);
        var markers=drawingMarkers.Where(marker=>own.Contains(marker.HostHandle)).ToList();
        var orphans=drawingMarkers.Where(marker=>marker.HostHandle.Length==0).ToArray();
        if(orphans.Length>0)
        {
            var pipes=parts.Where(p=>p.Component==ComponentKind.Pipe).ToList();
            var capturedPipes=pipes.Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var nativePipeIds=new Dictionary<string,ObjectId>(StringComparer.OrdinalIgnoreCase);
            var pipeClass=new Dictionary<string,string>{{"PnPClassName","Pipe"}};
            foreach(ObjectId id in model)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string handle=id.Handle.ToString();
                if(id.ObjectClass.Name!="AcPpDb3dPipe"||capturedPipes.Contains(handle))continue;
                var pipe=(PlantPart)tr.GetObject(id,OpenMode.ForRead);
                var nativePorts=pipe.GetPorts(PortType.All);
                var ends=new List<IsoGenerator.Core.Port>();
                for(int i=0;i<nativePorts.Count;i++)
                {
                    var port=nativePorts[i];
                    if(port.Name is "S1" or "S2")ends.Add(new(port.Name,Point(port.Position),Vector(port.Direction)));
                }
                pipes.Add(new(filename,handle,id.ObjectClass.Name,"",ends,pipeClass));
                nativePipeIds.Add(handle,id);
            }
            foreach(var marker in orphans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidates=BreakPointPreparation.HostCandidates(pipes,marker.Position);
                if(!candidates.Any(pipe=>own.Contains(pipe.Handle)))continue;
                string markerName=marker.InformationType==5?"точка начала":"точка разрыва";
                if(candidates.Count==1)
                {
                    markers.Add(marker with{HostHandle=candidates[0].Handle});
                    warnings.Add($"{marker.Handle}: {markerName} привязана к трубе {candidates[0].Handle} по оси.");
                }
                else
                {
                    string hosts=string.Join(", ",candidates.Select(pipe=>
                    {
                        string pipeLine=nativePipeIds.TryGetValue(pipe.Handle,out var nativeId)?LineOf(nativeId):pipe.Line;
                        return $"{pipe.Handle} ({(pipeLine.Length==0?"без номера линии":pipeLine)})";
                    }));
                    warnings.Add($"{marker.Handle}: {markerName} пропущена, найдены несколько труб в текущем DWG: {hosts}.");
                }
            }
        }
        if(!markers.Any(marker=>marker.InformationType==BreakPointPreparation.BreakPointInformationType))
            warnings.Add("В текущем DWG не найдены точки разрыва выбранной линии.");
        // Capture only two physical neighbours across a line boundary. They are context,
        // never members of the selected line or its bill of materials.
        var markerHandles=markers.Select(m=>m.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var externalContexts=new List<ExternalLineContext>();
        foreach(var boundary in links.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool first=own.Contains(boundary.FirstHandle),second=own.Contains(boundary.SecondHandle);if(first==second)continue;
            string local=first?boundary.FirstHandle:boundary.SecondHandle,localPort=first?boundary.FirstPort:boundary.SecondPort;
            string remote=first?boundary.SecondHandle:boundary.FirstHandle,remotePort=first?boundary.SecondPort:boundary.FirstPort;
            if(markerHandles.Contains(remote))continue;
            if(!long.TryParse(remote,System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out long handle)||
               !db.TryGetObjectId(new Handle(handle),out var rootId)||rootId.IsNull||rootId.IsErased||rootId.IsEffectivelyErased)
            {
                warnings.Add($"{local}:{localPort} → {remote}:{remotePort}: внешний объект не найден в текущем DWG, контекст пропущен.");
                continue;
            }
            if(tr.GetObject(rootId,OpenMode.ForRead) is not PlantPart)continue;
            string otherLine=LineOf(rootId);
            if(otherLine.Length==0||otherLine.Equals(line,StringComparison.OrdinalIgnoreCase))continue;
            var remoteParts=new List<ModelPart>();var remoteLinks=new List<Connection>();var pending=new Queue<ObjectId>();pending.Enqueue(rootId);
            var visited=new HashSet<ObjectId>();int physicalCount=0;
            while(pending.Count>0&&physicalCount<2&&visited.Count<24)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id=pending.Dequeue();if(!visited.Add(id)||own.Contains(id.Handle.ToString())||id.Database!=db)continue;
                if(tr.GetObject(id,OpenMode.ForRead) is not PlantPart item)continue;
                bool joint=id.ObjectClass.Name=="AcPpDb3dConnector";
                var values=joint?new Dictionary<string,string>():Properties(id);
                if(!joint&&!values.GetValueOrDefault("LineNumberTag","").Trim().Equals(otherLine,StringComparison.OrdinalIgnoreCase))continue;
                if(!joint&&(values.GetValueOrDefault("PnPClassName","").Contains("Support",StringComparison.OrdinalIgnoreCase)||values.GetValueOrDefault("PnPClassName","").Contains("Instrument",StringComparison.OrdinalIgnoreCase)))continue;
                var itemPorts=item.GetPorts(PortType.All);var portValues=new List<IsoGenerator.Core.Port>();
                for(int k=0;k<itemPorts.Count;k++)
                {PlantPort port=itemPorts[k];string end=port.EndType??"";try{end=item.PortProperties(port.Name)?.EndCondition??end;}catch(System.Exception ex){warnings.Add($"Контекст {id.Handle}:{port.Name}: {ex.Message}");}portValues.Add(new(port.Name,Point(port.Position),Vector(port.Direction),end));}
                remoteParts.Add(new(filename,id.Handle.ToString(),id.ObjectClass.Name,otherLine,portValues,values,item is InlineAsset asset?Vector(asset.ZAxis):null));
                if(!joint)physicalCount++;
                var neighbors=connections.GetConnections(id);
                for(int k=0;k<neighbors.Count;k++)
                {
                    var link=neighbors[k];if(link.ObjectId1.IsNull||link.ObjectId2.IsNull)continue;
                    remoteLinks.Add(new(link.ObjectId1.Handle.ToString(),link.Port1.Name,link.ObjectId2.Handle.ToString(),link.Port2.Name));
                    foreach(var next in new[]{link.ObjectId1,link.ObjectId2})if(next.Database==db&&!visited.Contains(next)&&!own.Contains(next.Handle.ToString()))pending.Enqueue(next);
                }
            }
            if(physicalCount>0)externalContexts.Add(new(local,localPort,otherLine,remote,remotePort,remoteParts,remoteLinks.Distinct().ToArray()));
        }
        return new(1,filename,parts,links.Values.ToArray(),warnings,accessories,markers,ExternalLineContexts:externalContexts);
    }
}
