namespace IsoGenerator.Core;
internal static class AnnotationPlacement
{
    internal readonly record struct Box(double Left,double Bottom,double Right,double Top)
    {
        public bool Overlaps(Box b)=>Left<b.Right&&Right>b.Left&&Bottom<b.Top&&Top>b.Bottom;
    }
    static Box TextBox(SheetPoint p,string text,bool position=false)=>position?new(p.X-1,p.Y-.8,p.X+text.Length*2.1+2,p.Y+5):new(p.X-.8,p.Y-.6,p.X+text.Length*2.1+1.5,p.Y+4.6);
    static bool Hits(SheetPoint a,SheetPoint b,Box box)
    {
        double low=0,high=1,dx=b.X-a.X,dy=b.Y-a.Y;
        bool Clip(double p,double q){if(Math.Abs(p)<1e-10)return q>=0;double r=q/p;if(p<0)low=Math.Max(low,r);else high=Math.Min(high,r);return low<=high;}
        return Clip(-dx,a.X-box.Left)&&Clip(dx,box.Right-a.X)&&Clip(-dy,a.Y-box.Bottom)&&Clip(dy,box.Top-a.Y);
    }
    internal static Box DimensionBox(PipeDimension d)
    {
        double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,l=Math.Sqrt(dx*dx+dy*dy);dx/=l;dy/=l;
        var p=d.TextPosition!.Value;double halfWidth=d.Label.Length*1.05+2.5;
        double x=Math.Abs(dx)*halfWidth+Math.Abs(dy)*3.3,y=Math.Abs(dy)*halfWidth+Math.Abs(dx)*3.3;
        return new(p.X-x,p.Y-y,p.X+x,p.Y+y);
    }
    static (SheetPoint A,SheetPoint B) DimensionLine(PipeDimension d)
    {
        double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,l=Math.Sqrt(dx*dx+dy*dy),nx=-dy/l,ny=dx/l;
        var ex=d.ExtensionDirection!.Value;var line=d.DimensionLinePoint!.Value;
        double dot=ex.X*nx+ex.Y*ny;
        SheetPoint Move(SheetPoint p){double t=((line.X-p.X)*nx+(line.Y-p.Y)*ny)/dot;return new(p.X+ex.X*t,p.Y+ex.Y*t);}
        return (Move(d.Start),Move(d.End));
    }
    public static IReadOnlyList<Annotation> Place(IReadOnlyList<Annotation> labels,List<Stroke> strokes,IReadOnlyList<SymbolGlyph> symbols,IReadOnlyList<PipeDimension> dimensions)
    {
        var occupied=dimensions.Select(DimensionBox).ToList();
        var lines=strokes.Where(s=>s.Role!="insulation").SelectMany(s=>s.Points.Zip(s.Points.Skip(1),(a,b)=>(A:a,B:b))).ToList();
        foreach(var d in dimensions)
        {
            var (a,b)=DimensionLine(d);lines.Add((a,b));lines.Add((d.Start,a));lines.Add((d.End,b));
        }
        foreach(var glyph in symbols.Where(s=>s.LocalContours is {Count:>0}))
            foreach(var contour in glyph.LocalContours!)
            {
                SheetPoint At(SheetPoint p)=>new(glyph.Origin.X+glyph.XAxis.X*p.X+glyph.YAxis.X*p.Y,glyph.Origin.Y+glyph.XAxis.Y*p.X+glyph.YAxis.Y*p.Y);
                foreach(var (a,b) in contour.Zip(contour.Skip(1)))lines.Add((At(a),At(b)));
            }
        foreach(var elbow in symbols.Where(s=>s.LocalContours is null&&s.Block=="Колено"&&s.Variant.Length==0))
        {
            SheetPoint At(double x,double y)=>new(elbow.Origin.X+elbow.XAxis.X*x+elbow.YAxis.X*y,elbow.Origin.Y+elbow.XAxis.Y*x+elbow.YAxis.Y*y);
            lines.Add((At(-.5,0),At(-.125,0)));
            var previous=At(-.125,0);
            for(int i=1;i<=24;i++){double angle=-Math.PI/2+i*Math.PI/48;var next=At(-.125+.125*Math.Cos(angle),.125+.125*Math.Sin(angle));lines.Add((previous,next));previous=next;}
            lines.Add((At(0,.125),At(0,.5)));
        }
        var bodies=symbols.Where(s=>s.LocalContours is not {Count:>0}&&s.Block!="Колено").Select(s=>
        {
            double x=s.Block=="Колено"?Math.Abs(s.XAxis.X)*.5+Math.Abs(s.YAxis.X)*.5:Math.Abs(s.XAxis.X)*(s.Block=="ФланецWN"?.15625:.5)+Math.Abs(s.YAxis.X)*.25;
            double y=s.Block=="Колено"?Math.Abs(s.XAxis.Y)*.5+Math.Abs(s.YAxis.Y)*.5:Math.Abs(s.XAxis.Y)*(s.Block=="ФланецWN"?.15625:.5)+Math.Abs(s.YAxis.Y)*.25;
                        if(s.Variant.StartsWith("end-",StringComparison.Ordinal))
            {
                var points=(from u in new[]{-.027,0.0} from v in new[]{-.25,.25} select new SheetPoint(s.Origin.X+s.XAxis.X*u+s.YAxis.X*v,s.Origin.Y+s.XAxis.Y*u+s.YAxis.Y*v)).ToArray();
                return new Box(points.Min(p=>p.X)-1,points.Min(p=>p.Y)-1,points.Max(p=>p.X)+1,points.Max(p=>p.Y)+1);
            }
            if(s.Variant=="operator")
            {
                var points=(from u in new[]{-.25,.25} from v in new[]{0.0,.625} select new SheetPoint(s.Origin.X+s.XAxis.X*u+s.YAxis.X*v,s.Origin.Y+s.XAxis.Y*u+s.YAxis.Y*v)).ToArray();
                return new Box(points.Min(p=>p.X)-1,points.Min(p=>p.Y)-1,points.Max(p=>p.X)+1,points.Max(p=>p.Y)+1);
            }
            return new Box(s.Origin.X-x-1,s.Origin.Y-y-1,s.Origin.X+x+1,s.Origin.Y+y+1);
        }).ToArray();
        var result=new List<Annotation>();
        foreach(var label in labels.OrderBy(l=>l.Role=="slope-angle"?0:l.Role is "valve-tag" or "support-type"?1:l.Role=="elevation"?2:l.Role is "position" or "position-group"?3:4))
        {
            PlanningExecution.Check();
            if(label.Role is ("elevation" or "valve-tag" or "support-type" or "continuation")&&label.Anchor is {} anchor)
            {
                bool placed=false;double width=label.Text.Length*2.1+3;int rejectedText=0;
                foreach(var anchorCandidate in new[]{anchor}.Concat(label.NearbyAnchors??Array.Empty<SheetPoint>()).Concat(label.Role=="elevation"?symbols.Where(g=>g.Variant=="missing").SelectMany(SymbolFootprint.Corners).Where(p=>Math.Abs(p.Y-anchor.Y)<.01&&Math.Abs(p.X-anchor.X)<=5.01):Array.Empty<SheetPoint>()))
                {
                foreach(double radius in new[]{4.0,6,8,10,12,18,24,30})
                {
                    foreach(var dir in new[]{(1.0,1.0),(-1.0,1.0),(1.0,-1.0),(-1.0,-1.0),(1.0,.5),(-1.0,.5),(1.0,-.5),(-1.0,-.5),(.5,1.0),(-.5,1.0),(.5,-1.0),(-.5,-1.0),(1.0,0.0),(-1.0,0.0),(0.0,1.0),(0.0,-1.0)})
                    {
                        double directionLength=Math.Sqrt(dir.Item1*dir.Item1+dir.Item2*dir.Item2);
                        var knee=new SheetPoint(anchorCandidate.X+dir.Item1/directionLength*radius,anchorCandidate.Y+dir.Item2/directionLength*radius);
                        var left=new SheetPoint(knee.X-(dir.Item1<0?width:0),knee.Y);
                        var right=new SheetPoint(left.X+width,left.Y);
                        var text=new SheetPoint(left.X+1,left.Y+1.5);var box=TextBox(text,label.Text);
                        if(box.Left<20||box.Right>570||box.Bottom<20||box.Top>390)continue;
                        if(occupied.Any(b=>b.Overlaps(box))||bodies.Any(b=>b.Overlaps(box))||lines.Any(l=>Hits(l.A,l.B,box))){rejectedText++;continue;}


                        strokes.Add(new(label.SourceId,"elevation-leader",new[]{anchorCandidate,knee}));
                        strokes.Add(new(label.SourceId,"elevation-shelf",new[]{left,right}));
                        lines.Add((anchorCandidate,knee));lines.Add((left,right));occupied.Add(box);
                        result.Add(label with{Position=text,Anchor=anchorCandidate,LeaderKnee=knee});placed=true;break;
                    }
                    if(placed)break;
                }
                if(placed)break;
                }
                if(!placed&&Environment.GetEnvironmentVariable("ISO_LAYOUT_TRACE")=="1")Console.Error.WriteLine($"leader {label.Text} at {anchor}: text={rejectedText}");
                if(!placed)throw new SheetCapacityException($"Не помещается выноска {label.Text}; требуется больше места",label.SourceId);
                continue;
            }
            if(label.Role is not ("position" or "position-group" or "slope-angle")){result.Add(label);continue;}
            var origin=new SheetPoint(label.Position.X-2,label.Position.Y-3);
            var anchors=new[]{origin}.Concat(label.NearbyAnchors??Array.Empty<SheetPoint>()).ToArray();
            var candidates=new List<(SheetPoint Point,SheetPoint Anchor,double Score)>();
            foreach(var near in anchors)
            foreach(double radius in label.Role=="slope-angle"?new[]{4.0,6,8,10,14,18,24,30}:label.Role=="position-group"?new[]{2.5,3.5,4.5,6,8,10,12,14,18,22,26}:new[]{2.5,3.5,4.5,6,8,10,12,14})
                foreach(var dir in new[]{(1.0,1.0),(-1.0,1.0),(1.0,-1.0),(-1.0,-1.0),(1.0,0.0),(-1.0,0.0),(0.0,1.0),(0.0,-1.0)})
                                    {
                    var point=new SheetPoint(near.X+dir.Item1*radius-(dir.Item1<=0?label.Text.Length*2.1:0),near.Y+dir.Item2*radius);
                    double distance=Math.Pow(point.X+label.Text.Length*1.05-near.X,2)+Math.Pow(point.Y+1.75-near.Y,2);
                    candidates.Add((point,near,distance+.001*(Math.Pow(near.X-origin.X,2)+Math.Pow(near.Y-origin.Y,2))));
                }
            SheetPoint? chosen=null;SheetPoint chosenAnchor=origin;
            foreach(var option in candidates.OrderBy(p=>p.Score))
            {
                var candidate=option.Point;var box=TextBox(candidate,label.Text,label.Role is "position" or "position-group");
                if(box.Left<20||box.Right>570||box.Bottom<20||box.Top>390)continue;
                if(occupied.Any(b=>b.Overlaps(box))||bodies.Any(b=>b.Overlaps(box))||lines.Any(l=>Hits(l.A,l.B,box)))continue;
                chosen=candidate;chosenAnchor=option.Anchor;occupied.Add(box);break;
            }
            if(chosen is null)throw new SheetCapacityException($"No readable annotation position for {label.SourceId} ({label.Role}: {label.Text}); sheet needs more space",label.SourceId);
            result.Add(label with{Position=chosen.Value,Anchor=chosenAnchor});
        }
        return result;
    }
}
