using System.Globalization;
namespace IsoGenerator.Core;
internal static class ElevationNotation
{
    public static void Add(PipelineSnapshot snapshot,Func<ModelPoint,SheetPoint> project,List<Annotation> labels,Func<ModelPoint,string,SheetPoint>? projectSource=null)
    {
        var graph=DimensionChainPlanner.BuildGraph(snapshot);
        var degree=new Dictionary<int,int>();
        foreach(var edge in graph.Edges)foreach(int root in new[]{graph.Sets.Find(edge.A),graph.Sets.Find(edge.B)})degree[root]=degree.GetValueOrDefault(root)+1;
        var supports=snapshot.Parts.Where(p=>BomCategories.Classify(p)==BomCategory.Supports).Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool SupportStation(DimensionChainPlanner.Node node)=>node.Tag=="TapAxis"&&snapshot.Connections.Any(c=>
            c.FirstHandle==node.Source&&c.FirstPort==node.PortName&&supports.Contains(c.SecondHandle)||c.SecondHandle==node.Source&&c.SecondPort==node.PortName&&supports.Contains(c.FirstHandle));
        var candidates=graph.Nodes.Select((n,i)=>(Node:n,Root:graph.Sets.Find(i))).Where(x=>x.Node.Tag is "Axis" or "TapAxis"||degree.GetValueOrDefault(x.Root)==1)
            .Where(x=>!supports.Contains(x.Node.Source)&&!SupportStation(x.Node)&&snapshot.Parts.Any(p=>p.Handle==x.Node.Source&&p.Kind!="AcPpDb3dConnector"))
            .OrderBy(x=>degree.GetValueOrDefault(x.Root)==1?0:x.Node.Tag=="Axis"?1:2).ThenBy(x=>x.Node.Source,StringComparer.Ordinal);
        foreach(var level in candidates.GroupBy(x=>Math.Round(x.Node.Point.Z,0)))
        {
            SheetPoint At(ModelPoint point,string source)=>projectSource is null?project(point):projectSource(point,source);
            var node=level.First().Node;var anchor=At(node.Point,node.Source);
            double meters=node.Point.Z/1000;
            string text="ОТМ. "+meters.ToString("+0.000;-0.000;0.000",CultureInfo.InvariantCulture)+"*";
                        var alternatives=snapshot.Parts.SelectMany(p=>p.Ports.Select(port=>(Part:p,Port:port))).Where(p=>Math.Abs(p.Port.Position.Z-node.Point.Z)<.001).Select(p=>At(p.Port.Position,p.Part.Handle)).ToList();
            foreach(var part in snapshot.Parts.Where(p=>p.Properties.GetValueOrDefault("PnPClassName")=="Pipe"))
            {
                var ends=part.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();
                if(ends.Length!=2||ends.Any(p=>Math.Abs(p.Position.Z-node.Point.Z)>.001))continue;
                var a=At(ends[0].Position,part.Handle);var b=At(ends[1].Position,part.Handle);
                foreach(double t in new[]{.2,.5,.8})alternatives.Add(new(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t));
            }
            labels.Add(new(node.Source,"elevation",anchor,text,Anchor:anchor,NearbyAnchors:alternatives.Distinct().ToArray()));
        }
    }
}
