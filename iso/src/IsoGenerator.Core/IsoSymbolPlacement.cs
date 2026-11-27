namespace IsoGenerator.Core;
internal static class IsoSymbolPlacement
{
    static ModelPoint Unit(ModelPoint v){double l=Math.Sqrt(v.X*v.X+v.Y*v.Y+v.Z*v.Z);if(l<1e-9)throw new InvalidDataException("У компонента не определено направление порта.");return new(v.X/l,v.Y/l,v.Z/l);}
    public static SheetPoint Transverse(ModelPoint axis,int turns)
    {
        axis=Unit(axis);var seed=Math.Abs(axis.Z)<.95?new ModelPoint(0,0,1):new ModelPoint(1,0,0);
        double dot=axis.X*seed.X+axis.Y*seed.Y+axis.Z*seed.Z;
        var v=Unit(new(seed.X-axis.X*dot,seed.Y-axis.Y*dot,seed.Z-axis.Z*dot));var p=IsometricProjection.Project(v,quarterTurns:turns);double l=Math.Sqrt(p.X*p.X+p.Y*p.Y);return new(p.X/l,p.Y/l);
    }
    internal static SheetPoint FlangeTransverse(PipelineSnapshot snapshot,Part flange,ModelPoint axis,int turns) => FlangeTransverse(snapshot,flange,axis,turns,out _);

    internal static SheetPoint FlangeTransverse(PipelineSnapshot snapshot,Part flange,ModelPoint axis,int turns,out bool hostFound)
    {
        hostFound=false;
        var fallback=Transverse(axis,turns);
        if(Math.Abs(Unit(axis).Z)<.99)return fallback;
        var parts=snapshot.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
        var frontier=new[]{(Handle:flange.Handle,Port:"")};
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase){flange.Handle};
        var inferred=new List<ModelPoint>();
        while(frontier.Length>0)
        {
            var explicitHosts=new List<ModelPoint>();
            var next=new List<(string Handle,string Port)>();
            foreach(var entry in frontier)
            {
                var part=parts[entry.Handle];
                var ends=part.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();
                string kind=part.CatalogClass;
                if(kind=="Pipe"&&ends.Length==2&&entry.Port.Length>0)
                {
                    var run=Unit(new(ends[1].Position.X-ends[0].Position.X,ends[1].Position.Y-ends[0].Position.Y,ends[1].Position.Z-ends[0].Position.Z));
                    if(Math.Abs(run.Z)<.95)
                    {
                        var port=part.Ports.FirstOrDefault(p=>p.Name==entry.Port);
                        if(port?.EndCondition.Equals("TAP",StringComparison.OrdinalIgnoreCase)==true)
                            explicitHosts.Add(run);
                        else if(inferred.Count==0&&PartDiameter.Read(part) is {} hostSize&&PartDiameter.Read(flange) is {} size&&size<hostSize)
                            inferred.Add(run);
                        continue;
                    }
                }
                if(kind=="Tee"&&ends.Length==3&&entry.Port.Length>0)
                {
                    var run=(from first in ends from second in ends
                             where first.Name!=second.Name&&first.Name!=entry.Port&&second.Name!=entry.Port&&
                             first.Direction.X*second.Direction.X+first.Direction.Y*second.Direction.Y+first.Direction.Z*second.Direction.Z<-.99
                             select first.Direction).FirstOrDefault();
                    if(run!=default){explicitHosts.Add(Unit(run));continue;}
                }
                foreach(var link in snapshot.Connections)
                {
                    if(link.FirstHandle==entry.Handle&&parts.ContainsKey(link.SecondHandle)&&seen.Add(link.SecondHandle))
                        next.Add((link.SecondHandle,link.SecondPort));
                    else if(link.SecondHandle==entry.Handle&&parts.ContainsKey(link.FirstHandle)&&seen.Add(link.FirstHandle))
                        next.Add((link.FirstHandle,link.FirstPort));
                }
            }
            if(explicitHosts.Count>0)return ProjectHost(explicitHosts,out hostFound);
            frontier=next.ToArray();
        }
        if(inferred.Count>0)return ProjectHost(inferred,out hostFound);
        SheetPoint ProjectHost(List<ModelPoint> candidates,out bool found)
        {
            found=false;
            var first=candidates[0];
            if(candidates.Any(v=>Math.Abs(v.X*first.X+v.Y*first.Y+v.Z*first.Z)<.99))return fallback;
            if(first.X<-.001||(Math.Abs(first.X)<.001&&first.Y<0))first=new(-first.X,-first.Y,-first.Z);
            var projected=IsometricProjection.Project(first,quarterTurns:turns);
            double length=Math.Sqrt(projected.X*projected.X+projected.Y*projected.Y);
            found=length>1e-8;
            return found?new(projected.X/length,projected.Y/length):fallback;
        }
        return fallback;
    }
    static SymbolGlyph Fit(Part part,string block,IsoBlockDefinition definition,SheetPoint from,SheetPoint to,SheetPoint transverse,double symbolScale)
    {
        var port1=definition.Ports.GetValueOrDefault("Port1",new(definition.Minimum.X,0));
        var port2=definition.Ports.GetValueOrDefault("Port2",new(definition.Maximum.X,0));
        double dx=port2.X-port1.X,dy=port2.Y-port1.Y,span=Math.Sqrt(dx*dx+dy*dy);
        double tx=to.X-from.X,ty=to.Y-from.Y,len=Math.Sqrt(tx*tx+ty*ty);if(len<1e-8)throw new InvalidDataException($"{part.Handle}: совпавшие порты компонента");
        if(span<1e-8)
        {
            // Point-based symbols, such as the documented custom flow meter, keep a fixed body.
            double width=Math.Max(.001,definition.Maximum.X-definition.Minimum.X),height=Math.Max(.001,definition.Maximum.Y-definition.Minimum.Y);
            var x=new SheetPoint(tx/len*symbolScale,ty/len*symbolScale);var y=new SheetPoint(transverse.X*symbolScale,transverse.Y*symbolScale);
            var mid=new SheetPoint((from.X+to.X)/2,(from.Y+to.Y)/2);
            return new(part.Handle,part.Properties.GetValueOrDefault("PnPGuid",""),part.SymbolDefinition,block,new(mid.X-x.X*port1.X-y.X*port1.Y,mid.Y-x.Y*port1.X-y.Y*port1.Y),x,y,LocalBounds:definition.Corners,LocalContours:definition.Contours);
        }
        dx/=span;dy/=span;
        double widthAcross=definition.Corners.Select(p=>-dy*p.X+dx*p.Y).Max()-definition.Corners.Select(p=>-dy*p.X+dx*p.Y).Min();
        double scale=symbolScale;
        var xa=new SheetPoint(tx*dx/span-transverse.X*scale*dy,ty*dx/span-transverse.Y*scale*dy);
        var ya=new SheetPoint(tx*dy/span+transverse.X*scale*dx,ty*dy/span+transverse.Y*scale*dx);
        return new(part.Handle,part.Properties.GetValueOrDefault("PnPGuid",""),part.SymbolDefinition,block,new(from.X-xa.X*port1.X-ya.X*port1.Y,from.Y-xa.Y*port1.X-ya.Y*port1.Y),xa,ya,LocalBounds:definition.Corners,LocalContours:definition.Contours);
    }
    public static SymbolGlyph Corner(Part part,string block,IsoBlockCatalog catalog,SheetPoint a,SheetPoint corner,SheetPoint b)
    {
        var definition=catalog.Find(block)??throw new InvalidDataException($"{part.Handle}: отсутствует блок {block}");
        var ports=definition.Ports.Where(p=>p.Key is "Port1" or "Port2" or "Port3").OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Value).ToArray();
        if(ports.Length<2)throw new InvalidDataException($"{block}: для углового символа нужны два параметра порта");
        var p1=ports[0];var p2=ports[^1];
        SheetPoint localCorner;
        if(Math.Abs(p2.X-p1.X)<=1e-4)
        {
            double apexX=Math.Abs(definition.Maximum.X-p1.X)>=Math.Abs(definition.Minimum.X-p1.X)
                ? definition.Maximum.X
                : definition.Minimum.X;
            localCorner=new SheetPoint(apexX,(p1.Y+p2.Y)/2);
        }
        else if(Math.Abs(p2.Y-p1.Y)<=1e-4)
        {
            double apexY=Math.Abs(definition.Maximum.Y-p1.Y)>=Math.Abs(definition.Minimum.Y-p1.Y)
                ? definition.Maximum.Y
                : definition.Minimum.Y;
            localCorner=new SheetPoint((p1.X+p2.X)/2,apexY);
        }
        else
        {
            localCorner=new SheetPoint(p2.X,p1.Y);
        }
        return Three(part,block,definition,new[]{p1,localCorner,p2},new[]{a,corner,b});
    }
    public static SymbolGlyph Junction(Part part,string block,IsoBlockCatalog catalog,IReadOnlyList<SheetPoint> targets)
    {
        var definition=catalog.Find(block)??throw new InvalidDataException($"{part.Handle}: отсутствует блок {block}");
        var ports=definition.Ports.Where(p=>p.Key.StartsWith("Port",StringComparison.OrdinalIgnoreCase)).OrderBy(p=>int.Parse(p.Key[4..])).Take(targets.Count).Select(p=>p.Value).ToArray();
        if(ports.Length<3)throw new InvalidDataException($"{block}: недостаточно параметров портов для разветвления");
        var placed=Three(part,block,definition,ports.Take(3).ToArray(),targets.Take(3).ToArray());
        for(int i=3;i<Math.Min(ports.Length,targets.Count);i++)
        {
            var actual=new SheetPoint(placed.Origin.X+placed.XAxis.X*ports[i].X+placed.YAxis.X*ports[i].Y,placed.Origin.Y+placed.XAxis.Y*ports[i].X+placed.YAxis.Y*ports[i].Y);
            if(Math.Sqrt(Math.Pow(actual.X-targets[i].X,2)+Math.Pow(actual.Y-targets[i].Y,2))>.1)throw new InvalidDataException($"{part.Handle}: блок {block} не соответствует расположению портов компонента");
        }
        return placed;
    }
    static SymbolGlyph Three(Part part,string block,IsoBlockDefinition definition,IReadOnlyList<SheetPoint> source,IReadOnlyList<SheetPoint> target)
    {
        double ax=source[1].X-source[0].X,ay=source[1].Y-source[0].Y,bx=source[2].X-source[0].X,by=source[2].Y-source[0].Y,det=ax*by-ay*bx;
        if(Math.Abs(det)<1e-9)throw new InvalidDataException($"{block}: параметры портов не задают плоскость символа");
        var u=new SheetPoint(target[1].X-target[0].X,target[1].Y-target[0].Y);var v=new SheetPoint(target[2].X-target[0].X,target[2].Y-target[0].Y);
        var x=new SheetPoint((u.X*by-v.X*ay)/det,(u.Y*by-v.Y*ay)/det);var y=new SheetPoint((v.X*ax-u.X*bx)/det,(v.Y*ax-u.Y*bx)/det);
        return new(part.Handle,part.Properties.GetValueOrDefault("PnPGuid",""),part.SymbolDefinition,block,new(target[0].X-x.X*source[0].X-y.X*source[0].Y,target[0].Y-x.Y*source[0].X-y.Y*source[0].Y),x,y,LocalBounds:definition.Corners,LocalContours:definition.Contours);
    }
    public static SymbolGlyph Inline(Part part,string block,IsoBlockCatalog? catalog,SheetPoint a,SheetPoint b,SheetPoint transverse)
    {
        var definition=catalog?.Find(block)??throw new InvalidDataException($"{part.Handle}: не прочитаны параметры блока {block} из IsoSymbolStyles.dwg");
        return Fit(part,block,definition,a,b,transverse,catalog!.ScaleFor(part));
    }
    public static SymbolGlyph Attachment(Part part,string block,IsoBlockCatalog? catalog,SheetPoint anchor,ModelPoint axis,int turns)
    {
        var definition=catalog?.Find(block)??throw new InvalidDataException($"{part.Handle}: отсутствует блок {block} в IsoSymbolStyles.dwg");
        var along=IsometricProjection.Project(Unit(axis),quarterTurns:turns);double l=Math.Sqrt(along.X*along.X+along.Y*along.Y);
        if(l<1e-8)throw new InvalidDataException($"{part.Handle}: не определено направление символа опоры");
        var transverse=Transverse(axis,turns);
        double width=Math.Max(.001,definition.Maximum.X-definition.Minimum.X),height=Math.Max(.001,definition.Maximum.Y-definition.Minimum.Y);
        var x=new SheetPoint(along.X/l*catalog!.ScaleFor(part),along.Y/l*catalog.ScaleFor(part));var y=new SheetPoint(transverse.X*catalog.ScaleFor(part),transverse.Y*catalog.ScaleFor(part));
        if(IsoDefinition.Parse(part.SymbolDefinition).Skey is "SUPS" or "SUPN"){x=new(-x.X,-x.Y);y=new(-y.X,-y.Y);}
        // Heel plates belong below the elbow attachment, regardless of host axis direction.
        if(IsoDefinition.Parse(part.SymbolDefinition).Skey=="DUCK"&&y.Y>0)y=new(-y.X,-y.Y);
        if(!definition.Ports.TryGetValue("CenterPoint",out var localAnchor))throw new InvalidDataException($"{part.Handle}: в блоке {block} не прочитан параметр CenterPoint опоры.");
        return new(part.Handle,part.Properties.GetValueOrDefault("PnPGuid",""),part.SymbolDefinition,block,new(anchor.X-x.X*localAnchor.X-y.X*localAnchor.Y,anchor.Y-x.Y*localAnchor.X-y.Y*localAnchor.Y),x,y,LocalBounds:definition.Corners,LocalContours:definition.Contours);
    }
    public static SymbolGlyph Terminal(Part part,string block,IsoBlockCatalog? catalog,SheetPoint port,int turns,SheetPoint? transverse=null)
    {
        var modelPort=part.Ports.First(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase));
        var direction=modelPort.Direction;
        return Terminal(part,block,catalog,port,new(-direction.X,-direction.Y,-direction.Z),turns,transverse);
    }
    internal static SymbolGlyph Terminal(Part part,string block,IsoBlockCatalog? catalog,SheetPoint port,
        ModelPoint towardSymbol,int turns,SheetPoint? transverse=null)
    {
        var definition=catalog?.Find(block)??throw new InvalidDataException($"{part.Handle}: не прочитаны параметры блока {block} из IsoSymbolStyles.dwg");
        var axis=Unit(towardSymbol);
        var projected=IsometricProjection.Project(axis,quarterTurns:turns);double l=Math.Sqrt(projected.X*projected.X+projected.Y*projected.Y);
        // A terminal has one model port: its axial thickness comes from the library proportions,
        // rather than an invented second port five millimetres away.
        var p1=definition.Ports.GetValueOrDefault("Port1",new(definition.Minimum.X,0));
        var p2=definition.Ports.GetValueOrDefault("Port2",new(definition.Maximum.X,0));
        double dx=p2.X-p1.X,dy=p2.Y-p1.Y,span=Math.Sqrt(dx*dx+dy*dy);
        double across=span>1e-8?definition.Corners.Max(p=>(-dy*p.X+dx*p.Y)/span)-definition.Corners.Min(p=>(-dy*p.X+dx*p.Y)/span):0;
        double thickness=(span>1e-8?span:definition.Maximum.X-definition.Minimum.X)*catalog!.ScaleFor(part);
        var end=new SheetPoint(port.X+projected.X/l*thickness,port.Y+projected.Y/l*thickness);
        return Fit(part,block,definition,port,end,transverse??Transverse(axis,turns),catalog.ScaleFor(part));
    }
}
