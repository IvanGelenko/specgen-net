using System.Globalization;
namespace IsoGenerator.Core;
internal static class SlopeNotation
{
    public static void Add(PipelineSnapshot snapshot,Func<ModelPoint,SheetPoint> project,int turns,List<Stroke> strokes,List<Annotation> labels,Func<ModelPoint,string,SheetPoint>? projectSource=null)
    {
        foreach(var part in snapshot.Parts.Where(p=>p.Properties.GetValueOrDefault("PnPClassName")=="Pipe"))
        {
            SheetPoint At(ModelPoint point)=>projectSource is null?project(point):projectSource(point,part.Handle);
            var ports=part.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();if(ports.Length!=2)continue;
            var a=ports[0].Position;var b=ports[1].Position;
            double dx=b.X-a.X,dy=b.Y-a.Y,dz=b.Z-a.Z,length=a.DistanceTo(b),horizontal=Math.Sqrt(dx*dx+dy*dy);
            if(length<.001||Math.Max(Math.Abs(dx),Math.Max(Math.Abs(dy),Math.Abs(dz)))/length>Math.Cos(.01*Math.PI/180))continue;
            // Reference plane is horizontal for a rise, XY for a horizontal skew.
            var reference=Math.Abs(dz)>.001?new ModelPoint(dx,dy,0):Math.Abs(dx)>=Math.Abs(dy)?new ModelPoint(dx,0,0):new ModelPoint(0,dy,0);
            double refLength=Math.Sqrt(reference.X*reference.X+reference.Y*reference.Y);
            double angle=Math.Atan2(Math.Abs(dz)>.001?Math.Abs(dz):Math.Min(Math.Abs(dx),Math.Abs(dy)),refLength)*180/Math.PI;
            var pa=At(a);var pb=At(b);var modelVector=IsometricProjection.Project(new(dx,dy,dz),quarterTurns:turns);
            double projectedLength=Math.Sqrt(modelVector.X*modelVector.X+modelVector.Y*modelVector.Y);
            double scale=Math.Sqrt(Math.Pow(pb.X-pa.X,2)+Math.Pow(pb.Y-pa.Y,2))/projectedLength;
            const double fraction=1;
            var start=pa;var tip=pb;
            var r=IsometricProjection.Project(reference,quarterTurns:turns);
            var corner=new SheetPoint(start.X+r.X*scale*fraction,start.Y+r.Y*scale*fraction);
            if(Math.Abs((corner.X-start.X)*(tip.Y-start.Y)-(corner.Y-start.Y)*(tip.X-start.X))<.1)continue;
            strokes.Add(new(part.Handle,"slope-triangle",new[]{start,corner,tip}));
            var legA=new SheetPoint(start.X-corner.X,start.Y-corner.Y);var legB=new SheetPoint(tip.X-corner.X,tip.Y-corner.Y);
            double la=Math.Sqrt(legA.X*legA.X+legA.Y*legA.Y),lb=Math.Sqrt(legB.X*legB.X+legB.Y*legB.Y);
            double square=Math.Min(2.5,Math.Min(la,lb)*.2);
            var qa=new SheetPoint(corner.X+legA.X/la*square,corner.Y+legA.Y/la*square);
            var qb=new SheetPoint(corner.X+legB.X/lb*square,corner.Y+legB.Y/lb*square);
            strokes.Add(new(part.Handle,"slope-right-angle",new[]{qa,new SheetPoint(qa.X+legB.X/lb*square,qa.Y+legB.Y/lb*square),qb}));
            // The angular dimension is a projected circular arc in the actual 3D slope plane.
            var rise=new ModelPoint(dx-reference.X,dy-reference.Y,dz);
            double riseLength=Math.Sqrt(rise.X*rise.X+rise.Y*rise.Y+rise.Z*rise.Z);
            double alpha=angle*Math.PI/180;
            double radius=Math.Min(10,Math.Min(la,lb)*.5);
            SheetPoint Arc(double theta)
            {
                var vector=new ModelPoint(reference.X/refLength*Math.Cos(theta)+rise.X/riseLength*Math.Sin(theta),reference.Y/refLength*Math.Cos(theta)+rise.Y/riseLength*Math.Sin(theta),rise.Z/riseLength*Math.Sin(theta));
                var v=IsometricProjection.Project(vector,quarterTurns:turns);return new(start.X+v.X*radius,start.Y+v.Y*radius);
            }
            var arc=Enumerable.Range(0,33).Select(i=>Arc(alpha*i/32)).ToArray();
            strokes.Add(new(part.Handle,"slope-angle-arc",arc));
            void Arrow(SheetPoint point,SheetPoint inside)
            {
                double tx=point.X-inside.X,ty=point.Y-inside.Y,l=Math.Sqrt(tx*tx+ty*ty);tx/=l;ty/=l;
                const double arrowLength=2.5;double half=arrowLength*Math.Tan(10*Math.PI/180);
                var a1=new SheetPoint(point.X-tx*arrowLength-ty*half,point.Y-ty*arrowLength+tx*half);
                var a2=new SheetPoint(point.X-tx*arrowLength+ty*half,point.Y-ty*arrowLength-tx*half);
                strokes.Add(new(part.Handle,"slope-angle-arrow",new[]{point,a1,a2,point}));
            }
            Arrow(arc[0],arc[1]);Arrow(arc[^1],arc[^2]);
            double hatchSpan=Math.Abs((corner.X-start.X)*legB.Y-(corner.Y-start.Y)*legB.X)/lb;
            int hatchCount=Math.Max(1,(int)Math.Floor(hatchSpan/5));
            for(int i=1;i<=hatchCount;i++)
            {
                double t=(double)i/(hatchCount+1);
                var u=new SheetPoint(start.X+(corner.X-start.X)*t,start.Y+(corner.Y-start.Y)*t);
                var v=new SheetPoint(start.X+(tip.X-start.X)*t,start.Y+(tip.Y-start.Y)*t);
                strokes.Add(new(part.Handle,"slope-hatch",new[]{u,v}));
            }
            var labelAnchor=Arc(alpha/2);
            labels.Add(new(part.Handle,"slope-angle",new(labelAnchor.X+2,labelAnchor.Y+3),angle.ToString("0.#",CultureInfo.InvariantCulture)+"°"));
        }
    }
}
