namespace IsoGenerator.Core;
internal sealed record DimensionRunEnds(int Start,int End,SheetPoint StartPoint,SheetPoint EndPoint);
internal static class DimensionCorners
{
    public static IReadOnlyList<PipeDimension> Align(IReadOnlyList<PipeDimension> input,IReadOnlyDictionary<string,DimensionRunEnds> ends,IReadOnlyList<SheetPoint> axes,IReadOnlySet<string>? constrainedChains=null)
    {
        var result=input.ToArray();var ids=ends.Keys.ToArray();var locked=new Dictionary<string,SheetPoint>();
        static double Dot(SheetPoint a,SheetPoint b)=>a.X*b.X+a.Y*b.Y;
        static SheetPoint Normal(PipeDimension d){double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,l=Math.Sqrt(dx*dx+dy*dy);var n=new SheetPoint(-dy/l,dx/l);return Dot(n,d.ExtensionDirection!.Value)<0?new(-n.X,-n.Y):n;}
        PipeDimension Inner(string id)=>result.FirstOrDefault(d=>d.ChainId==id&&d.Role=="chain")??result.First(d=>d.ChainId==id);
        for(int i=0;i<ids.Length;i++)for(int j=i+1;j<ids.Length;j++)
        {
            if(constrainedChains?.Contains(ids[i])==true||constrainedChains?.Contains(ids[j])==true)continue;
            var ea=ends[ids[i]];var eb=ends[ids[j]];
            int shared=ea.Start==eb.Start||ea.Start==eb.End?ea.Start:ea.End==eb.Start||ea.End==eb.End?ea.End:-1;if(shared<0)continue;
            var a=Inner(ids[i]);var b=Inner(ids[j]);var na=Normal(a);var nb=Normal(b);if(Math.Abs(Dot(na,nb))>.999)continue;
            var point=ea.Start==shared?ea.StartPoint:ea.EndPoint;
            double oa=Dot(new(a.DimensionLinePoint!.Value.X-point.X,a.DimensionLinePoint.Value.Y-point.Y),na),ob=Dot(new(b.DimensionLinePoint!.Value.X-point.X,b.DimensionLinePoint.Value.Y-point.Y),nb);
            SheetPoint? chosen=null;double da=0,db=0,best=double.MaxValue;
            foreach(var axis in axes.SelectMany(v=>new[]{v,new SheetPoint(-v.X,-v.Y)}))
            {
                if(Dot(axis,a.ExtensionDirection!.Value)<.999||Dot(axis,b.ExtensionDirection!.Value)<.999)continue;
                if(locked.TryGetValue(ids[i],out var la)&&Dot(la,axis)<.999||locked.TryGetValue(ids[j],out var lb)&&Dot(lb,axis)<.999)continue;
                double ca=Dot(axis,na),cb=Dot(axis,nb);if(ca<.2||cb<.2)continue;
                double t=Math.Max(oa/ca,ob/cb),ma=t*ca-oa,mb=t*cb-ob;
                if(ma>7||mb>7)continue;
                if(ma+mb<best){best=ma+mb;da=ma;db=mb;chosen=axis;}
            }
            if(chosen is not {} direction)continue;
            for(int k=0;k<result.Length;k++)if(result[k].ChainId==ids[i]||result[k].ChainId==ids[j])
            {
                bool first=result[k].ChainId==ids[i];var n=first?na:nb;double delta=first?da:db;var p=result[k].DimensionLinePoint!.Value;
                result[k]=result[k] with{ExtensionDirection=direction,DimensionLinePoint=new(p.X+n.X*delta,p.Y+n.Y*delta),TextPosition=null};
            }
            locked[ids[i]]=direction;locked[ids[j]]=direction;
        }
        return result;
    }
}
