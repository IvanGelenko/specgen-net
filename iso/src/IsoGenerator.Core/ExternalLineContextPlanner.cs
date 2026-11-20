using System.Globalization;
namespace IsoGenerator.Core;

public sealed record ExternalLineContext(string OwnerHandle,string OwnerPort,string Line,string EntryHandle,string EntryPort,IReadOnlyList<Part> Parts,IReadOnlyList<Connection> Connections);

internal static class ExternalLineContextPlanner
{
    static ModelPoint Sub(ModelPoint a,ModelPoint b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    static double Dot(ModelPoint a,ModelPoint b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    static ModelPoint? Corner(Port a,Port b)
    {
        var w=Sub(a.Position,b.Position);var u=a.Direction;var v=b.Direction;
        double aa=Dot(u,u),bb=Dot(u,v),cc=Dot(v,v),dd=Dot(u,w),ee=Dot(v,w),den=aa*cc-bb*bb;
        if(Math.Abs(den)<1e-10)return null;
        double t=(bb*ee-cc*dd)/den;return new(a.Position.X+u.X*t,a.Position.Y+u.Y*t,a.Position.Z+u.Z*t);
    }
    public static void Add(PipelineSnapshot snapshot,SchematicLayout layout,int turns,IsoSymbolMap? map,List<Stroke> strokes,List<SymbolGlyph> symbols,List<Annotation> labels)
    {
        foreach(var context in snapshot.ExternalLineContexts??Array.Empty<ExternalLineContext>())
        {
            var owner=snapshot.Parts.FirstOrDefault(p=>p.Handle==context.OwnerHandle);
            var ownerPort=owner?.Ports.FirstOrDefault(p=>p.Name==context.OwnerPort);if(ownerPort is null)continue;
            var anchor=layout.Project(ownerPort.Position,context.OwnerHandle);var boundaryPoint=ownerPort.Position;
            // A tapping connector lies on the pipe surface. Reference its receiving axis,
            // not BOP/TOP, for the continuation dot and elevation.
            foreach(var link in snapshot.Connections.Where(c=>c.FirstHandle==context.OwnerHandle||c.SecondHandle==context.OwnerHandle))
            {
                string handle=link.FirstHandle==context.OwnerHandle?link.SecondHandle:link.FirstHandle;
                string portName=link.FirstHandle==context.OwnerHandle?link.SecondPort:link.FirstPort;
                var pipe=snapshot.Parts.FirstOrDefault(p=>p.Handle==handle&&p.Component==ComponentKind.Pipe);
                var port=pipe?.Ports.FirstOrDefault(p=>p.Name==portName);
                if(pipe is null||port is null||!port.EndCondition.Trim().Equals("TAP",StringComparison.OrdinalIgnoreCase))continue;
                var ends=pipe.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();if(ends.Length!=2)continue;
                var axis=Sub(ends[1].Position,ends[0].Position);double l2=Dot(axis,axis);if(l2<1e-9)continue;
                double t=Math.Clamp(Dot(Sub(port.Position,ends[0].Position),axis)/l2,0,1);
                boundaryPoint=new(ends[0].Position.X+t*axis.X,ends[0].Position.Y+t*axis.Y,ends[0].Position.Z+t*axis.Z);
                anchor=layout.Project(boundaryPoint,pipe.Handle);break;
            }
            string elevation=(boundaryPoint.Z/1000).ToString("+0.000;-0.000;0.000",CultureInfo.InvariantCulture);
            (SheetPoint End, ModelPoint Direction)? terminal=null;
            var parts=context.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
            var pending=new Queue<(string Handle,string Port,SheetPoint At)>();pending.Enqueue((context.EntryHandle,context.EntryPort,anchor));
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int physical=0;
            while(pending.Count>0&&physical<2)
            {
                var entry=pending.Dequeue();if(!seen.Add(entry.Handle)||!parts.TryGetValue(entry.Handle,out var part))continue;
                var inlet=part.Ports.FirstOrDefault(p=>p.Name==entry.Port);if(inlet is null)continue;
                var located=new Dictionary<string,SheetPoint>(StringComparer.OrdinalIgnoreCase){[entry.Port]=entry.At};
                var ends=part.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();
                if(part.IsConnector)foreach(var port in part.Ports)located[port.Name]=entry.At;
                else
                {
                    physical++;
                    string kind=part.CatalogClass;
                    var definition=IsoDefinition.Parse(part.SymbolDefinition);
                    var block=map?.Resolve(definition.Skey);var library=block is null?null:map?.Catalog?.Find(block);
                    double scale=map?.Catalog?.ScaleFor(part)??10;
                    SheetPoint Along(SheetPoint from,ModelPoint vector,double length)
                    {var v=IsometricProjection.Project(vector,quarterTurns:turns);double l=Math.Sqrt(v.X*v.X+v.Y*v.Y);return l<1e-8?from:new(from.X+v.X/l*length,from.Y+v.Y/l*length);}
                    if(kind=="Elbow"&&ends.Length==2&&Corner(ends[0],ends[1]) is {} corner)
                    {
                        int i=Array.FindIndex(ends,p=>p.Name==entry.Port);if(i<0)i=0;int j=1-i;
                        double leg=library is null?5:Math.Max(.1,Math.Max(library.Maximum.X-library.Minimum.X,library.Maximum.Y-library.Minimum.Y)*scale/2);
                        var atCorner=Along(entry.At,Sub(corner,ends[i].Position),leg);
                        located[ends[i].Name]=entry.At;located[ends[j].Name]=Along(atCorner,Sub(ends[j].Position,corner),leg);
                        if(library is not null)symbols.Add(IsoSymbolPlacement.Corner(part,block!,map!.Catalog!,located[ends[0].Name],atCorner,located[ends[1].Name]) with{Context=true});
                        else strokes.Add(new(part.Handle,"context-external-elbow",new[]{entry.At,atCorner,located[ends[j].Name]}));
                    }
                    else
                    {
                        double length=12;
                        if(kind!="Pipe"&&library is not null)
                        {var a=library.Ports.GetValueOrDefault("Port1",library.Minimum);var b=library.Ports.GetValueOrDefault("Port2",library.Maximum);length=Math.Max(.1,Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Y-a.Y,2))*scale);}
                        bool drawInline=kind!="Pipe"&&library is not null&&ends.Length==2&&ends.Any(port=>port.Name==entry.Port);
                        foreach(var port in ends)if(port.Name!=entry.Port)
                        {
                            var point=Along(entry.At,Sub(port.Position,inlet.Position),length);
                            located[port.Name]=point;
                            if(!drawInline)strokes.Add(new(part.Handle,"context-external-pipe",new[]{entry.At,point}));
                        }
                        if(drawInline)
                            symbols.Add(IsoSymbolPlacement.Inline(part,block!,map!.Catalog,located[ends[0].Name],located[ends[1].Name],IsoSymbolPlacement.Transverse(Sub(ends[1].Position,ends[0].Position),turns)) with{Context=true});
                    }
                }
                if(!part.IsConnector)
                    foreach(var port in ends)
                        if(port.Name!=entry.Port&&located.TryGetValue(port.Name,out var endpoint)&&endpoint!=entry.At)
                            terminal=(endpoint,port.Direction);
                foreach(var link in context.Connections)
                {
                    if(link.FirstHandle==entry.Handle&&located.TryGetValue(link.FirstPort,out var a))pending.Enqueue((link.SecondHandle,link.SecondPort,a));
                    else if(link.SecondHandle==entry.Handle&&located.TryGetValue(link.SecondPort,out var b))pending.Enqueue((link.FirstHandle,link.FirstPort,b));
                }
            }
            if(terminal is {} end)
            {
                var projected=IsometricProjection.Project(end.Direction,quarterTurns:turns);
                double length=Math.Sqrt(projected.X*projected.X+projected.Y*projected.Y);
                if(length>1e-8)
                {
                    var across=IsoSymbolPlacement.Transverse(end.Direction,turns);
                    SheetPoint At(double width,double depth)=>new(end.End.X+across.X*width+projected.X/length*depth,end.End.Y+across.Y*width+projected.Y/length*depth);
                    strokes.Add(new(context.OwnerHandle,"continuation-break",new[]{At(-4,0),At(-1.5,0),At(-.7,1.5),At(.7,-1.5),At(1.5,0),At(4,0)}));
                    anchor=end.End;
                }
            }
            labels.Add(new(context.OwnerHandle,"external-line",anchor,"см. "+context.Line+"\nОТМ. "+elevation+"*",Anchor:anchor,
                ExternalLink:new(context.Line,context.OwnerPort,context.EntryHandle,context.EntryPort)));
        }
    }
}
