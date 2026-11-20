namespace IsoGenerator.Core;

public sealed class LayoutProjectionException(string message):IOException(message);

/// <summary>Ranks projections using the complete line, before sheet partitioning.</summary>
public static class AutomaticView
{
    public static int Select(PipelineSnapshot source,CancellationToken cancellationToken=default)=>
        Rank(source,cancellationToken)[0];

    public static IReadOnlyList<int> Rank(PipelineSnapshot source,CancellationToken cancellationToken=default)
    {
        var graph=PipelineTopology.Build(source);
        var edges=graph.Edges.Where(e=>graph.Nodes[e.A].Point.DistanceTo(graph.Nodes[e.B].Point)>1e-6).ToArray();
        var scores=new List<(int Turn,int Collapsed,double Score)>();
        for(int turn=0;turn<4;turn++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var points=graph.Nodes.Select(n=>IsometricProjection.Project(n.Point,quarterTurns:turn)).ToArray();
            double score=0;int collapsed=0;
            for(int i=0;i<edges.Length;i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var edge=edges[i];var a=points[edge.A];var b=points[edge.B];
                double length=Distance(a,b),actual=graph.Nodes[edge.A].Point.DistanceTo(graph.Nodes[edge.B].Point);
                if(length<.001)collapsed++;
                score+=Math.Max(0,1-length/actual); // Prefer visible lengths for sloping runs.
                for(int j=0;j<i;j++)
                {
                    var other=edges[j];
                    // Shared physical endpoints are intended junctions, not crossings.
                    if(new[]{edge.A,edge.B}.Any(n=>new[]{other.A,other.B}.Any(m=>graph.Nodes[n].Point.DistanceTo(graph.Nodes[m].Point)<1)))continue;
                    var c=points[other.A];var d=points[other.B];
                    double cross=Cross(a,b,c),cross2=Cross(a,b,d);
                    if(Math.Abs(cross)<1e-6*Math.Max(1,length)&&Math.Abs(cross2)<1e-6*Math.Max(1,length)&&length>1e-6)
                    {
                        double ux=(b.X-a.X)/length,uy=(b.Y-a.Y)/length;
                        double t1=(c.X-a.X)*ux+(c.Y-a.Y)*uy,t2=(d.X-a.X)*ux+(d.Y-a.Y)*uy;
                        if(Math.Min(length,Math.Max(t1,t2))-Math.Max(0,Math.Min(t1,t2))>1)score+=10000;
                    }
                    else if(cross*cross2<0&&Cross(c,d,a)*Cross(c,d,b)<0)score+=100;
                }
            }
            scores.Add((turn,collapsed,score));
        }
        return scores.OrderBy(view=>view.Collapsed).ThenBy(view=>Math.Round(view.Score,8))
            .ThenBy(view=>view.Turn).Select(view=>view.Turn).ToArray();
    }
    static double Distance(SheetPoint a,SheetPoint b)=>Math.Sqrt((b.X-a.X)*(b.X-a.X)+(b.Y-a.Y)*(b.Y-a.Y));
    static double Cross(SheetPoint a,SheetPoint b,SheetPoint c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
}
