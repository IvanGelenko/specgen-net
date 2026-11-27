using System.Globalization;
namespace IsoGenerator.Core;
internal static class ElevationNotation
{
    internal static string Format(double millimeters)=>
        "ОТМ. "+(millimeters/1000).ToString("+0.000;-0.000;0.000",CultureInfo.InvariantCulture)+"*";

    public static void Add(PipelineSnapshot snapshot,Func<ModelPoint,SheetPoint> project,List<Annotation> labels,Func<ModelPoint,string,SheetPoint>? projectSource=null,PipelineTopology? topology=null)
    {
        var graph=topology??PipelineTopology.Build(snapshot);
        var degree=new Dictionary<int,int>();
        foreach(var edge in graph.Edges)foreach(int root in new[]{graph.Root(edge.A),graph.Root(edge.B)})degree[root]=degree.GetValueOrDefault(root)+1;
        var supports=snapshot.Parts.Where(p=>BomCategories.Classify(p)==BomCategory.Supports).Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool SupportStation(PipelineTopology.Node node)=>node.Kind==RouteNodeKind.TapAxis&&snapshot.Connections.Any(c=>
            c.FirstHandle==node.Source&&c.FirstPort==node.PortName&&supports.Contains(c.SecondHandle)||c.SecondHandle==node.Source&&c.SecondPort==node.PortName&&supports.Contains(c.FirstHandle));
        bool TapSurface(PipelineTopology.Node node)=>node.Kind!=RouteNodeKind.TapAxis&&snapshot.Parts.Any(p=>p.Handle==node.Source&&p.Ports.Any(port=>port.Name==node.PortName&&port.EndCondition.Trim().Equals("TAP",StringComparison.OrdinalIgnoreCase)));
        var candidates=graph.Nodes.Select((n,i)=>(Node:n,Root:graph.Root(i))).Where(x=>x.Node.Kind is RouteNodeKind.Axis or RouteNodeKind.TapAxis||degree.GetValueOrDefault(x.Root)==1)
            .Where(x=>!supports.Contains(x.Node.Source)&&!SupportStation(x.Node)&&!TapSurface(x.Node)&&snapshot.Parts.Any(p=>p.Handle==x.Node.Source&&!p.IsConnector))
            .OrderBy(x=>x.Node.Kind==RouteNodeKind.Axis&&snapshot.Parts.Any(p=>p.Handle==x.Node.Source&&p.Component==ComponentKind.Elbow)
                ?0:degree.GetValueOrDefault(x.Root)==1?1:x.Node.Kind==RouteNodeKind.Axis?2:3).ThenBy(x=>x.Node.Source,StringComparer.Ordinal);
        foreach(var level in candidates.GroupBy(x=>Math.Round(x.Node.Point.Z,0)))
        {
            SheetPoint At(ModelPoint point,string source)=>projectSource is null?project(point):projectSource(point,source);
            var node=level.First().Node;var anchor=At(node.Point,node.Source);
            labels.Add(new(node.Source,"elevation",anchor,Format(node.Point.Z),Anchor:anchor));
        }
    }
}
