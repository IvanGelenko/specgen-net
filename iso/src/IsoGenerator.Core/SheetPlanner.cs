using System.Globalization;
namespace IsoGenerator.Core;

public sealed record Stroke(string SourceId, string Role, IReadOnlyList<SheetPoint> Points);
public sealed record Annotation(string SourceId, string Role, SheetPoint Position, string Text, double Height = 3.5, SheetPoint? Anchor = null, SheetPoint? LeaderKnee = null, IReadOnlyList<SheetPoint>? NearbyAnchors = null);
public sealed record PipeDimension(string SourceId, SheetPoint Start, SheetPoint End, double Millimeters,
    string Role = "pipe", string ChainId = "", SheetPoint? DimensionLinePoint = null, SheetPoint? ExtensionDirection = null,
    ModelPoint? ModelStart = null, ModelPoint? ModelEnd = null, IReadOnlyList<string>? SourceIds = null, SheetPoint? TextPosition = null, double? DisplayMillimeters = null)
{
    public string Label => Math.Ceiling(Millimeters-1e-8).ToString("0.#",CultureInfo.InvariantCulture);
}
public sealed record BomRow(int Position, string Kind, string Description, string Standard, double Quantity, string Unit, IReadOnlyList<string> Handles,string Identifier = "",BomCategory Category=BomCategory.Miscellaneous)
{
    public string QuantityText => Unit.Trim()=="м" ? (Quantity>0?Math.Max(.1,Math.Ceiling(Quantity*10-1e-9)/10):0).ToString("0.0",CultureInfo.InvariantCulture) : Quantity.ToString("0.###",CultureInfo.InvariantCulture);
    public string Designation => string.IsNullOrWhiteSpace(Identifier)?Standard:Identifier;
}
public sealed record SheetScene(string Line, IReadOnlyList<Stroke> Strokes, IReadOnlyList<Annotation> Annotations,
    IReadOnlyList<PipeDimension> Dimensions, IReadOnlyList<BomRow> BillOfMaterials, IReadOnlyList<string> Warnings, int SourcePartCount, IReadOnlyList<SymbolGlyph>? Symbols = null, string SourceDrawing = "", IReadOnlyDictionary<string,string>? SourceGuids = null, IReadOnlyList<WeldMarker>? Welds = null,int LayoutLevel = 0,IReadOnlyDictionary<string,double>? PipeExtensions = null,int QuarterTurns = 0,bool ExcludeFlangeHardware = false,bool BomOnly = false,IReadOnlyList<string>? FittingSourceIds = null,IReadOnlyDictionary<string,string>? OriginalHandles = null);
public sealed record ValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, int ConnectedComponents);

public static class SnapshotValidation
{
    public static ValidationResult Validate(PipelineSnapshot snapshot)
    {
        var errors=new List<string>(); var warnings=new List<string>();
        if(snapshot.SchemaVersion!=1) errors.Add("Unsupported snapshot schema");
        if(snapshot.Parts.Count==0) errors.Add("Empty snapshot");
        var duplicates=snapshot.Parts.GroupBy(p=>p.Handle,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1);
        foreach(var d in duplicates) errors.Add($"Duplicate handle {d.Key}");
        if(errors.Count>0) return new(errors,warnings,0);
        var map=snapshot.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
        var adjacency=map.Keys.ToDictionary(k=>k,_=>new HashSet<string>(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
        foreach(var p in snapshot.Parts)
        {
            if(!string.Equals(p.SourceDrawing,snapshot.SourceDrawing,StringComparison.OrdinalIgnoreCase)) errors.Add($"{p.Handle}: different source drawing");
            foreach(var port in p.Ports)
                if(!double.IsFinite(port.Position.X)||!double.IsFinite(port.Position.Y)||!double.IsFinite(port.Position.Z)||!double.IsFinite(port.Direction.X)||!double.IsFinite(port.Direction.Y)||!double.IsFinite(port.Direction.Z)) errors.Add($"{p.Handle}: nonfinite port coordinates or direction");
            if(p.Ports.GroupBy(x=>x.Name).Any(g=>g.Count()>1)) errors.Add($"{p.Handle}: duplicate port names");
        }
        foreach(var c in snapshot.Connections)
        {
            if(!map.TryGetValue(c.FirstHandle,out var a)||!map.TryGetValue(c.SecondHandle,out var b)) {warnings.Add($"Boundary connection {c.FirstHandle}:{c.FirstPort} -> {c.SecondHandle}:{c.SecondPort}");continue;}
            var pa=a.Ports.FirstOrDefault(x=>x.Name==c.FirstPort); var pb=b.Ports.FirstOrDefault(x=>x.Name==c.SecondPort);
            if(pa is null||pb is null) {errors.Add($"Unreadable connected port {c.FirstHandle}/{c.SecondHandle}");continue;}
            double gap=pa.Position.DistanceTo(pb.Position);
            if(gap>1) errors.Add($"Connected ports {c.FirstHandle}:{c.FirstPort}/{c.SecondHandle}:{c.SecondPort} differ by {gap:F3} mm");
            adjacency[a.Handle].Add(b.Handle);adjacency[b.Handle].Add(a.Handle);
        }
        var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var componentOf=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase); int components=0;
        foreach(var handle in map.Keys) if(visited.Add(handle))
        {
            components++;componentOf[handle]=components; var queue=new Queue<string>();queue.Enqueue(handle);
            while(queue.Count>0) foreach(var n in adjacency[queue.Dequeue()]) if(visited.Add(n)){componentOf[n]=components;queue.Enqueue(n);}
        }
        if(components>1) warnings.Add($"Line contains {components} disconnected components; no artificial links were created");
        if(components>1)
        {
            var occupied=snapshot.Connections.SelectMany(c=>new[]{c.FirstHandle+":"+c.FirstPort,c.SecondHandle+":"+c.SecondPort}).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var free=snapshot.Parts.Where(p=>p.Kind!="AcPpDb3dConnector"&&p.Properties.GetValueOrDefault("PnPClassName")!="Support").SelectMany(p=>p.Ports.Where(port=>port.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)&&!occupied.Contains(p.Handle+":"+port.Name)).Select(port=>(Part:p,Port:port))).ToArray();
            for(int i=0;i<free.Length;i++)for(int j=i+1;j<free.Length;j++)
            {
                var a=free[i];var b=free[j];if(componentOf[a.Part.Handle]==componentOf[b.Part.Handle])continue;
                double gap=a.Port.Position.DistanceTo(b.Port.Position);if(gap>1)continue;
                var u=a.Port.Direction;var v=b.Port.Direction;double lengths=Math.Sqrt((u.X*u.X+u.Y*u.Y+u.Z*u.Z)*(v.X*v.X+v.Y*v.Y+v.Z*v.Z));
                if(lengths<1e-10||(u.X*v.X+u.Y*v.Y+u.Z*v.Z)/lengths>-.99)continue;
                warnings.Add(FormattableString.Invariant($"Нет соединения: {a.Part.Handle}:{a.Port.Name} / {b.Part.Handle}:{b.Port.Name}; расстояние {gap:0.####} мм. Проверьте соединение в модели Plant."));
            }
        }
        return new(errors,warnings,components);
    }
}

public static class SheetPlanner
{
    static string Get(Part p,string name) => p.Properties.TryGetValue(name,out var value)?value:"";
    static string Kind(Part p)=>Get(p,"PnPClassName");
    static Port[] Ends(Part p)=>p.Ports.Where(q=>q.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();
    static ModelPoint Sub(ModelPoint a,ModelPoint b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    static ModelPoint Add(ModelPoint a,ModelPoint b)=>new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
    static ModelPoint Mul(ModelPoint a,double k)=>new(a.X*k,a.Y*k,a.Z*k);
    static double Dot(ModelPoint a,ModelPoint b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    static ModelPoint Mid(ModelPoint a,ModelPoint b)=>Mul(Add(a,b),.5);
    static ModelPoint? Corner(Port a,Port b)
    {
        var u=a.Direction;var v=b.Direction;var w=Sub(a.Position,b.Position);
        double aa=Dot(u,u),bb=Dot(u,v),cc=Dot(v,v),dd=Dot(u,w),ee=Dot(v,w),den=aa*cc-bb*bb;
        if(Math.Abs(den)<1e-9)return null;
        var p=Add(a.Position,Mul(u,(bb*ee-cc*dd)/den));
        var q=Add(b.Position,Mul(v,(aa*ee-bb*dd)/den));
        return p.DistanceTo(q)<1?Mid(p,q):null;
    }
    public static SheetScene Build(PipelineSnapshot snapshot,int quarterTurns=0, IsoSymbolMap? symbolMap=null, IReadOnlyList<SheetContinuation>? continuations=null, int sheetNumber=1, int sheetCount=1, IReadOnlyList<Stroke>? contextStrokes=null, IReadOnlyList<SymbolGlyph>? contextSymbols=null,int compactLevel=0,IReadOnlyDictionary<string,double>? pipeExtensions=null,bool excludeFlangeHardware=false)
    {
        var validation=SnapshotValidation.Validate(snapshot);
        if(validation.Errors.Count>0)throw new InvalidDataException(string.Join("\n",validation.Errors));
        var warnings=snapshot.Warnings.Concat(validation.Warnings).ToList();
        var physical=snapshot.Parts.Where(p=>p.Kind!="AcPpDb3dConnector").OrderBy(p=>p.Handle,StringComparer.Ordinal).ToArray();
        if(physical.Length==0)throw new InvalidDataException("No physical parts");
        var lineNames=physical.Select(p=>p.Line).Distinct().ToArray();
        if(lineNames.Length!=1)throw new InvalidDataException("Prototype supports one line per sheet");
        var ports=physical.SelectMany(p=>p.Ports).ToArray();
        if(ports.Length==0)throw new InvalidDataException("No geometry");
        var layout=new SchematicLayout(snapshot,quarterTurns,compactLevel,pipeExtensions,symbolMap);
        string? projectionSource=null;
        SheetPoint Project(ModelPoint p)=>layout.Project(p,projectionSource);
        warnings.Add($"Schematic layout: {layout.EnlargedEdges} short edges enlarged; true dimensions unchanged");
        if(layout.ClosedLoops>0)warnings.Add($"Совместно замкнуто контуров: {layout.ClosedLoops}; размеры элементов и реальные координаты сохранены.");
        var symbols=new List<SymbolGlyph>();var strokes=new List<Stroke>();var labels=new List<Annotation>();var dimensions=new List<PipeDimension>();
        var rows=new List<BomRow>();var positions=new Dictionary<string,int>();
        foreach(var group in physical.Concat(snapshot.Accessories??Array.Empty<Part>()).Where(p=>!excludeFlangeHardware||(BomCategories.Classify(p) is not (BomCategory.Gaskets or BomCategory.Fasteners)&&Kind(p)!="Flange"&&!IsoDefinition.Parse(Get(p,"ContentIsoSymbolDefinition")).Type.StartsWith("FLANGE",StringComparison.Ordinal))).GroupBy(p=>string.Join("\u001f",Kind(p),Get(p,"PartSizeLongDesc"),Get(p,"DesignStd"),Get(p,"Size"),Get(p,"WallThickness"),Get(p,"Spec"),Get(p,"Material"),BomCategories.Classify(p)==BomCategory.Fasteners?Get(p,"Length"):"",Get(p,"BoltSize"),Get(p,"IsoQuantityUnit"),PartIdentifier.Read(p))).OrderBy(g=>BomCategories.Classify(g.First())).ThenBy(g=>PartDiameter.Read(g.First())??double.MaxValue).ThenBy(g=>BomCategories.FittingOrder(g.First())).ThenBy(g=>g.Key,StringComparer.Ordinal))
        {
            var first=group.First(); bool pipe=Kind(first)=="Pipe"; double qty=0;
            foreach(var p in group)
            {
                if(pipe){var e=Ends(p);if(e.Length!=2)throw new InvalidDataException($"{p.Handle}: pipe requires S1/S2");qty+=e[0].Position.DistanceTo(e[1].Position)/1000;}
                else if(Get(p,"IsoQuantity").Length>0){if(!double.TryParse(Get(p,"IsoQuantity"),NumberStyles.Float,CultureInfo.InvariantCulture,out var amount)||!double.IsFinite(amount)||amount<=0)throw new InvalidDataException($"{p.Handle}: неверное количество в спецификации");qty+=amount;}
                else qty++;
            }
            int position=rows.Count+1;
            var description=Get(first,"PartSizeLongDesc");if(string.IsNullOrWhiteSpace(description))description=Get(first,"PartFamilyLongDesc");
            if(BomCategories.Classify(first)==BomCategory.Fasteners&&Get(first,"Length").Length>0)description+=" (L="+Get(first,"Length")+" мм)";
            rows.Add(new(position,Kind(first),description,Get(first,"DesignStd"),qty,pipe?"м":Get(first,"IsoQuantityUnit").Length>0?Get(first,"IsoQuantityUnit"):"шт.",group.Select(p=>p.Handle).ToArray(),PartIdentifier.Read(first),BomCategories.Classify(first)));
            foreach(var p in group)positions[p.Handle]=position;
        }
        foreach(var p in physical)
        {
            projectionSource=p.Handle;
            var ends=Ends(p);string kind=Kind(p);
            if(symbolMap is not null&&kind!="Pipe"&&ends.Length>0)
            {
                var definition=IsoDefinition.Parse(Get(p,"ContentIsoSymbolDefinition"));var block=symbolMap.Resolve(definition.Skey);
                if(block is null||(symbolMap.Catalog is {} catalog&&catalog.Find(block) is null))
                {
                    var placeholder=MissingIsoSymbol.Create(p,ends.Select(e=>Project(e.Position)).ToArray(),quarterTurns);symbols.Add(placeholder);
                    var placeholderCenter=placeholder.Origin;
                    foreach(var port in ends)
                    {
                        var point=Project(port.Position);double dx=point.X-placeholderCenter.X,dy=point.Y-placeholderCenter.Y,reach=Math.Max(Math.Abs(dx),Math.Abs(dy));
                        if(reach>5.01)strokes.Add(new(p.Handle,"missing-connection",new[]{point,new SheetPoint(placeholderCenter.X+dx*5/reach,placeholderCenter.Y+dy*5/reach)}));
                    }
                    if(positions.ContainsKey(p.Handle))labels.Add(new(p.Handle,"position",new(placeholderCenter.X+7,placeholderCenter.Y+3),positions[p.Handle].ToString(CultureInfo.InvariantCulture),Anchor:placeholderCenter,NearbyAnchors:ends.Select(e=>Project(e.Position)).ToArray()));
                    if(BomCategories.Classify(p)==BomCategory.Supports)
                    {string type=Get(p,"underSupportType").Trim();if(type.Any(char.IsLetterOrDigit)){var anchor=Project(ends[0].Position);labels.Add(new(p.Handle,"support-type",anchor,type,Anchor:anchor));}}
                    warnings.Add($"{p.Handle}: {Get(p,"ContentIsoSymbolDefinition")}: "+(block is null?"не найдено сопоставление SKEY":"в библиотеке проекта отсутствует блок "+block)+"; установлен резервный символ 10×10 мм с крестом.");
                    continue;
                }
            }
            if(ends.Length==1)
            {
                if(symbolMap is null)continue;
                var definition=IsoDefinition.Parse(Get(p,"ContentIsoSymbolDefinition"));var mapped=symbolMap.Resolve(definition.Skey);
                if(mapped is null)throw new InvalidDataException($"{p.Handle}: не найден блок для ISO {Get(p,"ContentIsoSymbolDefinition")}");
                SymbolGlyph terminal;
                if(definition.Type=="SUPPORT"||BomCategories.Classify(p)==BomCategory.Supports)
                {
                    var linked=snapshot.Connections.Where(c=>c.FirstHandle==p.Handle||c.SecondHandle==p.Handle).Select(c=>c.FirstHandle==p.Handle?c.SecondHandle:c.FirstHandle).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var host=physical.FirstOrDefault(h=>linked.Contains(h.Handle)&&Kind(h)=="Pipe");
                    var hostEnds=host is null?Array.Empty<Port>():Ends(host);
                    var axis=hostEnds.Length==2?Sub(hostEnds[1].Position,hostEnds[0].Position):ends[0].Direction;
                    terminal=IsoSymbolPlacement.Attachment(p,mapped,symbolMap.Catalog,Project(ends[0].Position),axis,quarterTurns);
                    string supportType=Get(p,"underSupportType").Trim();
                    if(supportType.Any(char.IsLetterOrDigit))
                    {var anchor=Project(ends[0].Position);labels.Add(new(p.Handle,"support-type",anchor,supportType,Anchor:anchor));}
                }
                else terminal=IsoSymbolPlacement.Terminal(p,mapped,symbolMap.Catalog,Project(ends[0].Position),quarterTurns);
                symbols.Add(terminal);
                var origin=terminal.Origin;if(positions.ContainsKey(p.Handle))labels.Add(new(p.Handle,"position",new(origin.X+2,origin.Y+3),positions[p.Handle].ToString(CultureInfo.InvariantCulture),Anchor:origin));
                continue;
            }
            if(ends.Length<2)throw new InvalidDataException($"{p.Handle}: у компонента нет читаемых портов");
            if(kind=="Tee"&&ends.Length==3)
            {
                var run=(from i in Enumerable.Range(0,3) from j in Enumerable.Range(i+1,2-i) where Dot(ends[i].Direction,ends[j].Direction)<-.99 select (i,j)).FirstOrDefault((-1,-1));
                if(run.Item1>=0)ends=new[]{ends[run.Item1],ends[run.Item2],ends[Enumerable.Range(0,3).Single(k=>k!=run.Item1&&k!=run.Item2)]};
            }
            var a=ends[0].Position;var b=ends[1].Position;
            var center=Mid(a,b);
            if(kind=="Elbow")
            {
                var corner=Corner(ends[0],ends[1]);
                if(corner is null){warnings.Add($"{p.Handle}: elbow axes do not intersect");strokes.Add(new(p.Handle,"unsupported",new[]{Project(a),Project(b)}));}
                else {center=corner.Value;strokes.Add(new(p.Handle,"elbow",new[]{Project(a),Project(center),Project(b)}));}
            }
            else
            {
                strokes.Add(new(p.Handle,kind=="Pipe"?"pipe":"component",new[]{Project(a),Project(b)}));
                foreach(var end in ends.Skip(2))strokes.Add(new(p.Handle,"branch",new[]{Project(center),Project(end.Position)}));
            }
            var c=Project(center);
            bool librarySymbol=false;
            var iso=IsoDefinition.Parse(Get(p,"ContentIsoSymbolDefinition"));
            if(symbolMap?.Catalog is {} blockCatalog&&kind!="Pipe")
            {
                var block=symbolMap.Resolve(iso.Skey)??throw new InvalidDataException($"{p.Handle}: нет сопоставления ISO {iso.Skey}");
                var start=Project(a);var finish=Project(b);SymbolGlyph fitted;
                if(kind=="Elbow")fitted=IsoSymbolPlacement.Corner(p,block,blockCatalog,start,Project(center),finish);
                else if(ends.Length>=3)fitted=IsoSymbolPlacement.Junction(p,block,blockCatalog,ends.Select(e=>Project(e.Position)).ToArray());
                else
                {
                    var axis=Sub(b,a);var transverse=IsoSymbolPlacement.Transverse(axis,quarterTurns);
                    if(kind=="Flange"&&ends[1].EndCondition=="FL")(start,finish)=(finish,start);
                    fitted=IsoSymbolPlacement.Inline(p,block,blockCatalog,start,finish,transverse);
                }
                symbols.Add(fitted);var localCenter=blockCatalog.Find(block)!.Ports.GetValueOrDefault("CenterPoint",default);
                c=new(fitted.Origin.X+fitted.XAxis.X*localCenter.X+fitted.YAxis.X*localCenter.Y,fitted.Origin.Y+fitted.XAxis.Y*localCenter.X+fitted.YAxis.Y*localCenter.Y);
                if(kind=="Valve")
                {
                    var pa=Project(a);var pb=Project(b);double dx=pb.X-pa.X,dy=pb.Y-pa.Y,len=Math.Sqrt(dx*dx+dy*dy);var xaxis=new SheetPoint(dx/len,dy/len);
                    var axis=Sub(b,a);var across=IsoSymbolPlacement.Transverse(axis,quarterTurns);double scale=blockCatalog.ScaleFor(p);
                    SymbolGlyph Accessory(string name,SheetPoint origin,SheetPoint x,SheetPoint y,string variant)
                    {
                        var definition=blockCatalog.Find(name)??throw new InvalidDataException($"{p.Handle}: нет дополнительного блока {name}");var anchor=definition.Ports.GetValueOrDefault("CenterPoint",default);
                        var xa=new SheetPoint(x.X*scale,x.Y*scale);var ya=new SheetPoint(y.X*scale,y.Y*scale);
                        return new(p.Handle,Get(p,"PnPGuid"),Get(p,"ContentIsoSymbolDefinition"),name,new(origin.X-xa.X*anchor.X-ya.X*anchor.Y,origin.Y-xa.Y*anchor.X-ya.Y*anchor.Y),xa,ya,variant,LocalBounds:definition.Corners,LocalContours:definition.Contours);
                    }
                    foreach(var (port,point,sign) in new[]{(ends[0],pa,-1.0),(ends[1],pb,1.0)})
                    {
                        var endBlock=symbolMap.ResolveEnd(port.EndCondition);
                        if(endBlock is not null&&blockCatalog.Find(endBlock) is not null)symbols.Add(Accessory(endBlock,point,new(xaxis.X*sign,xaxis.Y*sign),across,"end-"+port.Name));
                    }
                    var spindle=symbolMap.ResolveSpindle(iso.Skey);
                    if(spindle is not null&&blockCatalog.Find(spindle) is not null)
                    {
                        var unit=Mul(axis,1/Math.Sqrt(Dot(axis,axis)));var stem=p.OperatorDirection??new ModelPoint(0,0,1);var normal=Sub(stem,Mul(unit,Dot(stem,unit)));
                        if(Dot(normal,normal)<1e-10)normal=Math.Abs(unit.Z)<.95?new(0,0,1):new(1,0,0);
                        var plane=IsometricProjection.Project(normal,quarterTurns:quarterTurns);double length=Math.Sqrt(plane.X*plane.X+plane.Y*plane.Y);
                        symbols.Add(Accessory(spindle,c,xaxis,new(plane.X/length,plane.Y/length),"operator"));
                    }
                }
                strokes.RemoveAll(stroke=>stroke.SourceId==p.Handle);librarySymbol=true;
            }
            if(!librarySymbol&&kind!="Pipe")warnings.Add($"{p.Handle}: не прочитана библиотека символов; показаны только оси портов.");
            if(kind=="Pipe")
            {
                double length=a.DistanceTo(b);
                if(double.TryParse(Get(p,"Length"),NumberStyles.Float,CultureInfo.InvariantCulture,out double declared)&&Math.Abs(declared-length)>.1)
                    warnings.Add($"{p.Handle}: live port length {length:F3} differs from stored Length {declared:F3}; live geometry used");
                // Dimensions are assembled across connected components after geometry planning.
            }
            if(positions.ContainsKey(p.Handle))labels.Add(new(p.Handle,"position",new(c.X+2,c.Y+3),positions[p.Handle].ToString(CultureInfo.InvariantCulture),Anchor:c,NearbyAnchors:kind=="Pipe"?new[]{.1,.2,.35,.65,.8,.9}.Select(t=>{var pa=Project(a);var pb=Project(b);return new SheetPoint(pa.X+(pb.X-pa.X)*t,pa.Y+(pb.Y-pa.Y)*t);}).ToArray():ends.Select(e=>Project(e.Position)).SelectMany(e=>new[]{e,new SheetPoint((e.X+c.X)/2,(e.Y+c.Y)/2)}).ToArray()));
        }
        foreach(var valve in physical.Where(p=>Kind(p)=="Valve"))
        {
            projectionSource=valve.Handle;
            string tag=PartIdentifier.Read(valve);if(tag.Length==0)continue;
            var ends=Ends(valve);if(ends.Length<2)continue;
            var anchor=Project(Mid(ends[0].Position,ends[1].Position));
            // Identifier leaders attach to the body center, independently of the operator direction.
            labels.Add(new(valve.Handle,"valve-tag",anchor,tag,Anchor:anchor));
        }
        // F/B/G positions share one annotation at each actual Plant joint.
        foreach(var jointHardware in (snapshot.Accessories??Array.Empty<Part>()).Where(p=>positions.ContainsKey(p.Handle)&&BomCategories.Classify(p) is BomCategory.Gaskets or BomCategory.Fasteners).GroupBy(p=>Get(p,"ConnectorHandle")))
        {
            if(!snapshot.Parts.Any(p=>p.Handle==jointHardware.Key))continue;
            var joint=snapshot.Parts.Single(p=>p.Handle==jointHardware.Key);
            var neighbors=snapshot.Connections.Where(c=>c.FirstHandle==joint.Handle||c.SecondHandle==joint.Handle).Select(c=>c.FirstHandle==joint.Handle?c.SecondHandle:c.FirstHandle).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var flanges=physical.Where(p=>neighbors.Contains(p.Handle)&&positions.ContainsKey(p.Handle)&&(Kind(p) is "Flange" or "BlindFlange"||IsoDefinition.Parse(Get(p,"ContentIsoSymbolDefinition")).Type.StartsWith("FLANGE",StringComparison.Ordinal))).OrderBy(p=>Kind(p)=="Flange"?0:1).ThenBy(p=>p.Handle,StringComparer.Ordinal).ToArray();
            var bolts=jointHardware.Where(p=>BomCategories.Classify(p)==BomCategory.Fasteners).Select(p=>positions[p.Handle]).Distinct().OrderBy(n=>n);
            var gaskets=jointHardware.Where(p=>BomCategories.Classify(p)==BomCategory.Gaskets).Select(p=>positions[p.Handle]).Distinct().OrderBy(n=>n);
            string text=string.Join(",",flanges.Select(p=>positions[p.Handle]).Distinct().Concat(bolts).Concat(gaskets));
            if(text.Length==0)continue;
            var owner=flanges.FirstOrDefault()??joint;projectionSource=owner.Handle;
            var points=owner.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).Select(p=>Project(p.Position)).ToArray();
            if(points.Length==0)continue;
            var anchor=new SheetPoint(points.Average(p=>p.X),points.Average(p=>p.Y));
            var flangeGlyph=symbols.FirstOrDefault(g=>g.SourceId==owner.Handle&&g.Variant.Length==0);
            if(flangeGlyph is not null){var bounds=SymbolFootprint.Corners(flangeGlyph);anchor=new((bounds.Min(p=>p.X)+bounds.Max(p=>p.X))/2,(bounds.Min(p=>p.Y)+bounds.Max(p=>p.Y))/2);}
            var flangeHandles=flanges.Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
            labels.RemoveAll(a=>a.Role=="position"&&flangeHandles.Contains(a.SourceId));
            labels.Add(new(owner.Handle,"position-group",new(anchor.X+2,anchor.Y+3),text,Anchor:anchor,NearbyAnchors:points));
        }
        projectionSource=null;
        InsulationPlanner.Add(snapshot,Project,symbols,strokes,layout.Project);
        SlopeNotation.Add(snapshot,Project,quarterTurns,strokes,labels,layout.Project);
        var route=DimensionChainPlanner.BuildGraph(snapshot);
        var hiddenGaskets=FlangedJointPolicy.Find(snapshot).Select(j=>j.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Tap offsets remain in the model graph for dimensions and welds, but have no visible body.
        foreach(var edge in route.Edges.Where(e=>e.Kind=="Joint"&&!hiddenGaskets.Contains(e.Source)))
            strokes.Add(new(edge.Source,"joint",new[]{layout.Project(route.Nodes[edge.A].Point,route.Nodes[edge.A].Source),layout.Project(route.Nodes[edge.B].Point,route.Nodes[edge.B].Source)}));
                foreach(var continuation in continuations??Array.Empty<SheetContinuation>())
        {
            var part=snapshot.Parts.Single(p=>p.Handle==continuation.Handle);
            var point=layout.Project(part.Ports.Single(p=>p.Name==continuation.Port).Position,part.Handle);
            strokes.Add(new(part.Handle,"continuation",new[]{new SheetPoint(point.X-2,point.Y),new SheetPoint(point.X,point.Y+2),new SheetPoint(point.X+2,point.Y),new SheetPoint(point.X,point.Y-2),new SheetPoint(point.X-2,point.Y)}));
            labels.Add(new(part.Handle,"continuation",point,$"{continuation.Mark} / лист {continuation.OtherSheet}",Anchor:point));
        }
        strokes.AddRange(contextStrokes??Array.Empty<Stroke>());
        symbols.AddRange(contextSymbols??Array.Empty<SymbolGlyph>());
        dimensions.AddRange(DimensionChainPlanner.Build(snapshot,Project,quarterTurns,symbols,strokes,layout.Project));
        dimensions=DimensionRouting.Route(dimensions,strokes,symbols,quarterTurns).ToList();
        // Real and continuation blocks are obstacles too, not just missing-symbol squares.
        var symbolObstacles=symbols.SelectMany(g=>
        {
            if(g.LocalContours is {Count:>0})return g.LocalContours.Select(contour=>new Stroke(g.SourceId,"symbol-contour",contour.Select(p=>new SheetPoint(g.Origin.X+g.XAxis.X*p.X+g.YAxis.X*p.Y,g.Origin.Y+g.XAxis.Y*p.X+g.YAxis.Y*p.Y)).ToArray()));
            var corners=SymbolFootprint.Corners(g).ToArray();
            return new[]{new Stroke(g.SourceId,"symbol-contour",corners.Append(corners[0]).ToArray())};
        }).ToArray();
        dimensions=DimensionTextPlacement.Place(dimensions,strokes.Concat(symbolObstacles).ToArray()).ToList();
        if(DimensionRouting.CrossingCount(dimensions)>0)
        {
            dimensions=DimensionRouting.Route(dimensions,strokes,symbols,quarterTurns).ToList();
            dimensions=DimensionTextPlacement.Place(dimensions,strokes.Concat(symbolObstacles).ToArray()).ToList();
            if(DimensionRouting.CrossingCount(dimensions)>0)throw new SheetCapacityException("Не найдено размещение размерных и выносных линий без пересечений; требуется больше места на листе.");
        }
        ElevationNotation.Add(snapshot,Project,labels,layout.Project);
        labels=AnnotationPlacement.Place(labels,strokes,symbols,dimensions).ToList();
        var welds=WeldPlanner.Build(snapshot,Project,layout.Project);warnings.AddRange(welds.Warnings);
        warnings.Add("CONTROL PREVIEW: simplified symbols; gaskets and fasteners included in BOM");
        labels.Add(new("","title",new(75,385),lineNames[0]+$" — лист {sheetNumber} из {sheetCount}",4));
        labels.Add(new("","status",new(75,375),"КОНТРОЛЬНЫЙ ЛИСТ — НЕ ДЛЯ МОНТАЖА",3));
        return new(lineNames[0],strokes,labels,dimensions,rows,warnings,snapshot.Parts.Count,symbols,snapshot.SourceDrawing,snapshot.Parts.ToDictionary(p=>p.Handle,p=>Get(p,"PnPGuid")),welds.Markers,compactLevel,pipeExtensions,quarterTurns,excludeFlangeHardware,OriginalHandles:snapshot.Parts.ToDictionary(p=>p.Handle,p=>Get(p,"IsoOriginalHandle").Length>0?Get(p,"IsoOriginalHandle"):p.Handle));
    }
}
