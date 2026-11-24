namespace IsoGenerator.Core;
internal static class AnnotationPlacement
{
    internal readonly record struct Box(double Left,double Bottom,double Right,double Top)
    {
        public bool Overlaps(Box b)=>Left<b.Right&&Right>b.Left&&Bottom<b.Top&&Top>b.Bottom;
    }
    static int TextLength(string text)=>text.Split('\n').Max(line=>line.Length);
    static Box TextBox(SheetPoint p,string text,bool position=false,double rotation=0)
    {
        int length=TextLength(text),lines=text.Count(c=>c=='\n')+1;
        var local=position?new Box(-1,-.8,length*2.1+2,5):new Box(-.8,-.6-(lines-1)*4.8,length*2.1+1.5,4.6);
        double ratio=IsoAdvancedSettings.Current.LeaderTextHeight/3.5;
        local=new Box(local.Left*ratio,local.Bottom*ratio,local.Right*ratio,local.Top*ratio);
        double cos=Math.Cos(rotation),sin=Math.Sin(rotation);
        var corners=(from x in new[]{local.Left,local.Right} from y in new[]{local.Bottom,local.Top} select new SheetPoint(p.X+x*cos-y*sin,p.Y+x*sin+y*cos)).ToArray();
        return new(corners.Min(v=>v.X),corners.Min(v=>v.Y),corners.Max(v=>v.X),corners.Max(v=>v.Y));
    }
    internal static Box Bounds(Annotation label)=>TextBox(label.Position,label.Text,rotation:label.Rotation);
    static bool Hits(SheetPoint a,SheetPoint b,Box box)
    {
        double low=0,high=1,dx=b.X-a.X,dy=b.Y-a.Y;
        bool Clip(double p,double q){if(Math.Abs(p)<1e-10)return q>=0;double r=q/p;if(p<0)low=Math.Max(low,r);else high=Math.Min(high,r);return low<=high;}
        return Clip(-dx,a.X-box.Left)&&Clip(dx,box.Right-a.X)&&Clip(-dy,a.Y-box.Bottom)&&Clip(dy,box.Top-a.Y);
    }
    internal static Box DimensionBox(PipeDimension d)
    {
        double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,l=Math.Sqrt(dx*dx+dy*dy);dx/=l;dy/=l;
        var p=d.TextPosition!.Value;
        double ratio=IsoAdvancedSettings.Current.DimensionTextHeight/3.5;
        double halfWidth=d.Label.Length*1.05*ratio+2.5,halfHeight=3.3*ratio;
        double x=Math.Abs(dx)*halfWidth+Math.Abs(dy)*halfHeight,y=Math.Abs(dy)*halfWidth+Math.Abs(dx)*halfHeight;
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
    public static IReadOnlyList<Annotation> Place(IReadOnlyList<Annotation> labels,List<Stroke> strokes,IReadOnlyList<SymbolGlyph> symbols,IReadOnlyList<PipeDimension> dimensions,PlanningExecution? execution=null,DrawingBounds? fixedGeometry=null)
    {
        int start=strokes.Count;
        try{return PlaceCore(labels,strokes,symbols,dimensions,execution,compactValves:true,fixedGeometry);}
        catch(SheetCapacityException) when(labels.Any(label=>label.Role=="valve-tag"))
        {
            strokes.RemoveRange(start,strokes.Count-start);
            return PlaceCore(labels,strokes,symbols,dimensions,execution,compactValves:false,fixedGeometry);
        }
    }

    static IReadOnlyList<Annotation> PlaceCore(IReadOnlyList<Annotation> labels,List<Stroke> strokes,
        IReadOnlyList<SymbolGlyph> symbols,IReadOnlyList<PipeDimension> dimensions,PlanningExecution? execution,bool compactValves,DrawingBounds? fixedGeometry)
    {
        var dimensionInk=dimensions.Select(d=>
        {
            double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,l=Math.Sqrt(dx*dx+dy*dy);dx/=l;dy/=l;
            double ratio=IsoAdvancedSettings.Current.DimensionTextHeight/3.5;
            double w=d.Label.Length*1.05*ratio+.2,h=1.77*ratio;
            double x=Math.Abs(dx)*w+Math.Abs(dy)*h,y=Math.Abs(dy)*w+Math.Abs(dx)*h;
            var p=d.TextPosition!.Value;return new Box(p.X-x,p.Y-y,p.X+x,p.Y+y);
        }).ToList();
        var dimensionSegments=DimensionRouting.GetSegments(dimensions);
        var occupied=dimensions.Select(DimensionBox).ToList();
        var insulationLines=strokes.Where(s=>s.Role=="insulation").SelectMany(s=>s.Points.Zip(s.Points.Skip(1),(a,b)=>(A:a,B:b))).ToArray();
        var lines=strokes.Where(s=>s.Role is not ("insulation" or "slope-hatch" or "slope-projection-dimension" or "bend-dimension")).SelectMany(s=>s.Points.Zip(s.Points.Skip(1),(a,b)=>(A:a,B:b))).ToList();
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
        var layoutPoints=lines.SelectMany(line=>new[]{line.A,line.B})
            .Concat(bodies.SelectMany(box=>new[]{new SheetPoint(box.Left,box.Bottom),new SheetPoint(box.Right,box.Top)}))
            .ToArray();
        var field=layoutPoints.Length==0?(Box?)null:new Box(layoutPoints.Min(p=>p.X),layoutPoints.Min(p=>p.Y),
            layoutPoints.Max(p=>p.X),layoutPoints.Max(p=>p.Y));
        if(fixedGeometry is {} geometryBounds)field=new(geometryBounds.Left,geometryBounds.Bottom,geometryBounds.Right,geometryBounds.Top);
        static Box Union(Box a,Box b)=>new(Math.Min(a.Left,b.Left),Math.Min(a.Bottom,b.Bottom),
            Math.Max(a.Right,b.Right),Math.Max(a.Top,b.Top));
        double Overflow(Box box)
        {
            var bounds=field is {} active?Union(active,box):box;
            return Math.Max(0,bounds.Right-bounds.Left-SheetComposition.Paper.Width)+
                Math.Max(0,bounds.Top-bounds.Bottom-SheetComposition.Paper.Height);
        }
        void Occupy(Box box)
        {
            occupied.Add(box);
            field=field is {} active?Union(active,box):box;
        }
        var result=new List<Annotation>();
        var leaderPaths=new List<(SheetPoint A,SheetPoint B)>();
        foreach(var label in labels.OrderBy(l=>l.Role=="bend-angle"?-1:l.Role is "slope-angle" or "slope-value" or "slope-projection"?0:l.Role is "valve-tag" or "instrument-tag" or "external-line" or "support-type"?1:l.Role=="elevation"?2:l.Role is "position" or "position-group"?3:4))
        {
            execution?.Check();
            if(label.Role is ("elevation" or "valve-tag" or "instrument-tag" or "external-line" or "support-type" or "continuation")&&label.Anchor is {} anchor)
            {
                bool placed=false;double width=TextLength(label.Text)*2.1*(IsoAdvancedSettings.Current.LeaderTextHeight/3.5)+3;int rejectedText=0;
                bool remoteLeader=label.Role is "valve-tag" or "elevation" or "continuation" or "instrument-tag" or "external-line";
                (SheetPoint Anchor,SheetPoint Knee,SheetPoint Left,SheetPoint Right,SheetPoint Text,Box Box,double Crowding,int Crossings,int LeaderCrossings,double Score,double Overflow)? bestLeader=null;
                // Keep text clear, but allow the leader and shelf to cross dimension ink as a fallback.
                for(int pass=0;pass<2&&!placed&&bestLeader is null;pass++)
                foreach(var anchorCandidate in new[]{anchor}.Concat(label.NearbyAnchors??Array.Empty<SheetPoint>()))
                {
                    bool compactLeader=remoteLeader&&(label.Role!="valve-tag"||compactValves);
                    double crossingWeight=label.Role=="valve-tag"?IsoAdvancedSettings.Current.ValveLeaderCrossingWeight:IsoAdvancedSettings.Current.LeaderCrossingWeight;
                    foreach(double radius in (compactLeader?new[]{10.0,15,20,24,30,36,42,48,60,72,84,96}:remoteLeader?new[]{24.0,30,36,42,48,60,72,84,96}:new[]{4.0,6,8,10,12,18,24,30,36,42,48}).Append(IsoAdvancedSettings.Current.LeaderTarget).Append(IsoAdvancedSettings.Current.LeaderMaxReach)
                         .Concat(fixedGeometry.HasValue?new[]{4d,6d,8d,12d,18d,22d,27d,33d}:Array.Empty<double>())
                         .Where(reach=>reach<=IsoAdvancedSettings.Current.LeaderMaxReach).Distinct().OrderBy(reach=>label.Role=="support-type"&&reach<10).ThenBy(reach=>reach))
                    {
                        foreach(var dir in new[]{(1.0,1.0),(-1.0,1.0),(1.0,-1.0),(-1.0,-1.0),(1.0,.5),(-1.0,.5),(1.0,-.5),(-1.0,-.5),(.5,1.0),(-.5,1.0),(.5,-1.0),(-.5,-1.0),(1.0,0.0),(-1.0,0.0),(0.0,1.0),(0.0,-1.0)}
                            .Concat(fixedGeometry.HasValue?new[]{(.25,1d),(-.25,1d),(.25,-1d),(-.25,-1d)}:Array.Empty<(double,double)>()))
                        {
                            foreach(double shelfSign in dir.Item1 == 0 || compactLeader ? new[]{-1.0, 1.0} : new[]{dir.Item1 < 0 ? -1.0 : 1.0})
                            {
                                double directionLength=Math.Sqrt(dir.Item1*dir.Item1+dir.Item2*dir.Item2);
                                var knee=new SheetPoint(anchorCandidate.X+dir.Item1/directionLength*radius,anchorCandidate.Y+dir.Item2/directionLength*radius);
                                var left=new SheetPoint(knee.X-(shelfSign<0?width:0),knee.Y);
                                var right=new SheetPoint(left.X+width,left.Y);
                                var text=new SheetPoint(left.X+1,left.Y+1.5);var box=TextBox(text,label.Text);
                                var envelope=fixedGeometry.HasValue?Union(box,new Box(
                                    Math.Min(anchorCandidate.X,left.X)-.3,Math.Min(anchorCandidate.Y,knee.Y)-.3,
                                    Math.Max(anchorCandidate.X,right.X)+.3,Math.Max(anchorCandidate.Y,knee.Y)+.3)):box;
                                if(fixedGeometry.HasValue&&Overflow(envelope)>.000001)continue;
                                if(dimensionInk.Any(ink=>Hits(anchorCandidate,knee,ink)||Hits(left,right,ink)))continue;
                                if(pass==0&&!remoteLeader&&dimensionSegments.Any(segment=>
                                    DimensionRouting.CheckCross(new(anchorCandidate,knee,false),segment)||
                                    DimensionRouting.CheckCross(new(left,right,false),segment)))continue;
                                if(box.Left<20||box.Right>570||box.Bottom<20||box.Top>390)continue;
                                if(occupied.Any(b=>b.Overlaps(box))||bodies.Any(b=>b.Overlaps(box))||lines.Any(l=>Hits(l.A,l.B,box))){rejectedText++;continue;}
                                if(remoteLeader)
                                {
                                    // Score the surrounding empty area, not only the text rectangle.
                                    var neighbourhood=new Box(box.Left-12,box.Bottom-12,box.Right+12,box.Top+12);
                                    double crowding=lines.Count(l=>Hits(l.A,l.B,neighbourhood))*10+occupied.Count(b=>b.Overlaps(neighbourhood))*15+bodies.Count(b=>b.Overlaps(neighbourhood))*15;
                                    var leg=new DimensionRouting.Segment(anchorCandidate,knee,false);
                                    var shelf=new DimensionRouting.Segment(left,right,false);
                                    int crossings=label.Role=="continuation"
                                        ?dimensionSegments.Count(segment=>DimensionRouting.CheckCross(leg,segment)||
                                            DimensionRouting.CheckCross(shelf,segment))
                                        :lines.Count(l=>DimensionRouting.CheckCross(leg,new(l.A,l.B,false)));
                                    int leaderCrossings=leaderPaths.Count(l=>
                                        DimensionRouting.CheckCross(leg,new(l.A,l.B,false))||
                                        DimensionRouting.CheckCross(shelf,new(l.A,l.B,false))||Hits(l.A,l.B,box));
                                    double score=(compactLeader?Math.Abs(radius-IsoAdvancedSettings.Current.LeaderTarget)*.15+Math.Max(0,radius-IsoAdvancedSettings.Current.LeaderSoftLimit)*.5:radius*.15)+(knee.Y>=anchorCandidate.Y?.05:0);
                                    double overflow=Overflow(envelope);
                                    bool better=bestLeader is null||overflow<bestLeader.Value.Overflow-.001;
                                    if(!better&&bestLeader is {} prior&&Math.Abs(overflow-prior.Overflow)<.001)
                                    {
                                        if(compactLeader)
                                        {
                                            double cost=score+crossings*crossingWeight+crowding*.005;
                                            double priorCost=prior.Score+prior.Crossings*crossingWeight+prior.Crowding*.005;
                                            better=label.Role=="continuation"
                                                ?(crossings,leaderCrossings,cost).CompareTo((prior.Crossings,prior.LeaderCrossings,priorCost))<0
                                                :(leaderCrossings,cost).CompareTo((prior.LeaderCrossings,priorCost))<0;
                                        }
                                        else better=(leaderCrossings,crowding,crossings,score)
                                            .CompareTo((prior.LeaderCrossings,prior.Crowding,prior.Crossings,prior.Score))<0;
                                    }
                                    if(better)bestLeader=(anchorCandidate,knee,left,right,text,box,crowding,crossings,leaderCrossings,score,overflow);
                                    continue;
                                }
                                strokes.Add(new(label.SourceId,"elevation-leader",new[]{anchorCandidate,knee}));
                                strokes.Add(new(label.SourceId,"elevation-shelf",new[]{left,right}));
                                lines.Add((anchorCandidate,knee));lines.Add((left,right));leaderPaths.Add((anchorCandidate,knee));leaderPaths.Add((left,right));Occupy(box);
                                result.Add(label with{Position=text,Anchor=anchorCandidate,LeaderKnee=knee});placed=true;break;
                            }
                            if(placed)break;
                        }
                        if(placed||remoteLeader&&bestLeader is {Crowding:0,Crossings:0,LeaderCrossings:0,Overflow:0}&&(!compactLeader||bestLeader.Value.Score<=.05))break;
                    }
                    if(placed)break;
                }
                if(remoteLeader&&bestLeader is {} best)
                {
                    strokes.Add(new(label.SourceId,"elevation-leader",new[]{best.Anchor,best.Knee}));
                    strokes.Add(new(label.SourceId,"elevation-shelf",new[]{best.Left,best.Right}));
                    lines.Add((best.Anchor,best.Knee));lines.Add((best.Left,best.Right));leaderPaths.Add((best.Anchor,best.Knee));leaderPaths.Add((best.Left,best.Right));Occupy(fixedGeometry.HasValue?best.Box:new(best.Box.Left-3,best.Box.Bottom-3,best.Box.Right+3,best.Box.Top+3));
                    if(fixedGeometry.HasValue)field=Union(field!.Value,new Box(
                        Math.Min(best.Anchor.X,best.Left.X)-.3,Math.Min(best.Anchor.Y,best.Knee.Y)-.3,
                        Math.Max(best.Anchor.X,best.Right.X)+.3,Math.Max(best.Anchor.Y,best.Knee.Y)+.3));
                    result.Add(label with{Position=best.Text,Anchor=best.Anchor,LeaderKnee=best.Knee});placed=true;
                }
if(!placed&&Environment.GetEnvironmentVariable("ISO_LAYOUT_TRACE")=="1")Console.Error.WriteLine($"leader {label.Text} at {anchor}: text={rejectedText}");
                if(!placed)throw new SheetCapacityException($"Не помещается выноска {label.Text}; требуется больше места",label.SourceId,conflict:LayoutConflict.Annotation);
                continue;
            }
            if(label.Role is not ("position" or "position-group" or "slope-angle" or "bend-angle" or "operator-angle" or "slope-value" or "slope-projection")){result.Add(label);continue;}
            if(label.Role=="slope-value")
            {
                var axis=new SheetPoint(Math.Cos(label.Rotation),Math.Sin(label.Rotation));
                var normal=new SheetPoint(-axis.Y,axis.X);
                var indicator=strokes.FirstOrDefault(stroke=>stroke.Role=="slope-indicator"&&stroke.SourceId==label.SourceId);
                if(indicator is not null)
                {
                    var first=indicator.Points[0];var second=indicator.Points[1];
                    var center=new SheetPoint((first.X+second.X)/2,(first.Y+second.Y)/2);
                    var run=strokes.FirstOrDefault(stroke=>stroke.Role=="pipe"&&stroke.SourceId==label.SourceId);
                    double span=run is null?0:Math.Abs((run.Points[^1].X-run.Points[0].X)*axis.X+(run.Points[^1].Y-run.Points[0].Y)*axis.Y);
                    double Along(SheetPoint p)=>p.X*axis.X+p.Y*axis.Y;
                    double Across(SheetPoint p)=>p.X*normal.X+p.Y*normal.Y;
                    var baselines=dimensionSegments.Where(segment=>segment.Baseline)
                        .Where(segment=>run is not null&&Math.Abs(Across(segment.B)-Across(segment.A))<=
                            .05*Math.Abs(Along(segment.B)-Along(segment.A)))
                        .Where(segment=>run is not null&&Math.Min(Along(segment.A),Along(segment.B))<Math.Max(Along(run.Points[0]),Along(run.Points[^1]))&&
                            Math.Max(Along(segment.A),Along(segment.B))>Math.Min(Along(run.Points[0]),Along(run.Points[^1])))
                        .ToArray();
                    double AtStation(SheetPoint a,SheetPoint b,double station)=>
                        Across(a)+(Across(b)-Across(a))*(station-Along(a))/(Along(b)-Along(a));
                    var alternatives=from offset in Enumerable.Range(-48,97).Select(i=>i*.5)
                                     from slide in new[]{0d,-.16,.16,-.28,.28,-.4,.4}
                                     orderby Math.Abs(offset)+Math.Abs(slide)*span*.35
                                     select new SheetPoint(label.Position.X+normal.X*offset+axis.X*slide*span,
                                                           label.Position.Y+normal.Y*offset+axis.Y*slide*span);
                    double textHeight=IsoAdvancedSettings.Current.LeaderTextHeight;
                    double halfWidth=Math.Max(TextLength(label.Text)*2.1*textHeight/3.5,Math.Abs(Along(indicator.Points[1])-Along(indicator.Points[0]))/2);
                    bool AbovePipeBelowDimension(SheetPoint point)
                    {
                        if(run is null||span<.01)return false;
                        double station=Along(point);
                        double low=Across(point)+Math.Min(-.6,indicator.Points.Min(p=>Across(p)-Across(center))-2.8);
                        double high=Across(point)+Math.Max(textHeight,indicator.Points.Max(p=>Across(p)-Across(center))-2.8);
                        double left=station-halfWidth,right=station+halfWidth;
                        var ends=run.Points;
                        if(left<Math.Min(Along(ends[0]),Along(ends[^1]))||right>Math.Max(Along(ends[0]),Along(ends[^1])))return false;
                        if(new[]{left,right}.Any(t=>low<AtStation(ends[0],ends[^1],t)+.3))return false;
                        foreach(var line in baselines)
                        {
                            double from=Math.Max(left,Math.Min(Along(line.A),Along(line.B)));
                            double to=Math.Min(right,Math.Max(Along(line.A),Along(line.B)));
                            if(from>=to)continue;
                            double t=(from+to)/2;
                            if(AtStation(line.A,line.B,t)<=AtStation(ends[0],ends[^1],t)+.3)continue;
                            if(high>Math.Min(AtStation(line.A,line.B,from),AtStation(line.A,line.B,to))-.3)return false;
                        }
                        return true;
                    }
                    bool chosenAlongRun=false;
                    foreach(var point in alternatives.Where(AbovePipeBelowDimension))
                    {
                        double halfText=TextLength(label.Text)*1.05*textHeight/3.5+.4;
                        var localText=new Box(-halfText,-.6,halfText,textHeight);
                        SheetPoint ToLocal(SheetPoint p)=>new(Along(p)-Along(point),Across(p)-Across(point));
                        SheetPoint FromLocal(double x,double y)=>new(point.X+axis.X*x+normal.X*y,point.Y+axis.Y*x+normal.Y*y);
                        double moveX=point.X-normal.X*2.8-center.X,moveY=point.Y-normal.Y*2.8-center.Y;
                        var moved=indicator.Points.Select(p=>new SheetPoint(p.X+moveX,p.Y+moveY)).ToArray();
                        var ink=moved.Concat(from x in new[]{localText.Left,localText.Right}
                            from y in new[]{localText.Bottom,localText.Top} select FromLocal(x,y)).ToArray();
                        var bounds=new Box(ink.Min(p=>p.X),ink.Min(p=>p.Y),ink.Max(p=>p.X),ink.Max(p=>p.Y));
                        bool HitsDimensionText(PipeDimension dimension)
                        {
                            var location=ToLocal(dimension.TextPosition!.Value);
                            double dx=Along(dimension.End)-Along(dimension.Start),dy=Across(dimension.End)-Across(dimension.Start);
                            double length=Math.Sqrt(dx*dx+dy*dy);
                            if(length<1e-8)return false;
                            double ratio=IsoAdvancedSettings.Current.DimensionTextHeight/3.5;
                            double width=(dimension.Label.Length*1.05+.3)*ratio,height=1.8*ratio;
                            double x=(Math.Abs(dx)*width+Math.Abs(dy)*height)/length;
                            double y=(Math.Abs(dy)*width+Math.Abs(dx)*height)/length;
                            var inkBox=new Box(location.X-x,location.Y-y,location.X+x,location.Y+y);
                            return inkBox.Overlaps(localText)||moved.Zip(moved.Skip(1),(p,q)=>Hits(ToLocal(p),ToLocal(q),inkBox)).Any(hit=>hit);
                        }
                        if(bounds.Left<20||bounds.Right>570||bounds.Bottom<20||bounds.Top>390||
                           occupied.Skip(dimensions.Count).Any(box=>box.Overlaps(bounds))||bodies.Any(box=>box.Overlaps(bounds))||
                           dimensions.Any(HitsDimensionText)||
                           lines.Any(line=>Hits(ToLocal(line.A),ToLocal(line.B),localText))||
                           insulationLines.Any(line=>Hits(ToLocal(line.A),ToLocal(line.B),localText)||
                              moved.Zip(moved.Skip(1),(a,b)=>new DimensionRouting.Segment(a,b,false))
                                  .Any(segment=>DimensionRouting.CheckCross(segment,new(line.A,line.B,false)))))continue;
                        Occupy(bounds);
                        result.Add(label with{Position=point});
                        chosenAlongRun=true;
                        break;
                    }
                    if(chosenAlongRun)continue;
                    throw new SheetCapacityException($"Нет места для знака уклона над трубой {label.SourceId}.",
                        label.SourceId,conflict:LayoutConflict.Annotation);
                }
            }
            var origin=label.Role=="position"&&label.Anchor is {} attachment?attachment:new SheetPoint(label.Position.X-2,label.Position.Y-3);
            var anchors=new[]{origin}.Concat(label.NearbyAnchors??Array.Empty<SheetPoint>()).ToArray();
            var candidates=new List<(SheetPoint Point,SheetPoint Anchor,double Score)>();
            if(label.Role is ("slope-angle" or "bend-angle")&&label.Anchor is {} angleAnchor)
            {
                double halfText=TextLength(label.Text)*1.05*IsoAdvancedSettings.Current.LeaderTextHeight/3.5;
                foreach(double lift in new[]{2d,3d,4.5,6,8,12,16,22})
                foreach(double slide in new[]{0d,-2,2,-4,4,-6,6})
                    candidates.Add((new(angleAnchor.X-halfText+slide,angleAnchor.Y+lift),angleAnchor,lift+Math.Abs(slide)));
            }
            var positionDirs = label.Role is "position" or "position-group"?new[]{(1.0,1.0),(-1.0,1.0),(1.0,-1.0),(-1.0,-1.0),(1.0,0.0),(-1.0,0.0),(0.0,1.0),(0.0,-1.0),(1.0,.5),(-1.0,.5),(1.0,-.5),(-1.0,-.5),(.5,1.0),(-.5,1.0),(.5,-1.0),(-.5,-1.0)}:new[]{(1.0,1.0),(-1.0,1.0),(1.0,-1.0),(-1.0,-1.0),(1.0,0.0),(-1.0,0.0),(0.0,1.0),(0.0,-1.0)};
            if(label.Role is not ("slope-angle" or "bend-angle")||label.Anchor is null)
            foreach(var near in anchors)
            foreach(double radius in label.Role is "slope-angle" or "bend-angle"?new[]{4.0,6,8,10,14,18,24,30,36,42,48}:label.Role=="slope-value"?new[]{4.0,6,8,10,14,18,24,30}:label.Role=="position-group"?new[]{2.5,3.5,4.5,6,8,10,12,14,18,22,26,30,35,40}:new[]{1.5,2,2.5,3,3.5,4.5,6,8,10,12,14})
                foreach(var dir in positionDirs)
                {
                    double dlen=Math.Sqrt(dir.Item1*dir.Item1+dir.Item2*dir.Item2);
                    var point=new SheetPoint(near.X+dir.Item1/dlen*radius-(dir.Item1<=0?label.Text.Length*2.1:0),near.Y+dir.Item2/dlen*radius);
                    double distance=Math.Pow(point.X+label.Text.Length*1.05-near.X,2)+Math.Pow(point.Y+1.75-near.Y,2);
                    candidates.Add((point,near,distance+.001*(Math.Pow(near.X-origin.X,2)+Math.Pow(near.Y-origin.Y,2))));
                }
            if(label.Role=="slope-projection")
                candidates.Insert(0,(label.Position,origin,-1));
            SheetPoint? chosen=null;SheetPoint chosenAnchor=origin;
            foreach(var option in candidates.OrderBy(option=>Overflow(TextBox(option.Point,label.Text,
                label.Role is "position" or "position-group",label.Rotation))).ThenBy(option=>option.Score))
            {
                var candidate=option.Point;var box=TextBox(candidate,label.Text,label.Role is "position" or "position-group",label.Rotation);
                if(box.Left<20||box.Right>570||box.Bottom<20||box.Top>390)continue;
                if(occupied.Any(b=>b.Overlaps(box))||bodies.Any(b=>b.Overlaps(box))||lines.Any(l=>Hits(l.A,l.B,box))||insulationLines.Any(l=>Hits(l.A,l.B,new Box(box.Left-1,box.Bottom-1,box.Right+1,box.Top+1))))continue;
                if(label.Role is "bend-angle" or "slope-projection")
                    dimensionInk.Add(new(box.Left-2,box.Bottom-2,box.Right+2,box.Top+2));
                chosen=candidate;chosenAnchor=option.Anchor;Occupy(box);break;
            }
            if(chosen is null&&label.Role=="position"&&label.Anchor is {} component)
            {
                // Dense fittings can trap a position number inside the neighboring dimension chains.
                // A short leader preserves the association and gives the text a second chance.
                foreach(double radius in new[]{10d,12d,14d,16d,18d,22d,26d,30d})
                {
                    if(chosen is not null)break;
                    foreach(var dir in positionDirs)
                    {
                        double length=Math.Sqrt(dir.Item1*dir.Item1+dir.Item2*dir.Item2);
                        var tip=new SheetPoint(component.X+radius*dir.Item1/length,component.Y+radius*dir.Item2/length);
                        var candidate=new SheetPoint(tip.X+(dir.Item1<0?-label.Text.Length*2.1-2:2),tip.Y-1.5);
                        var box=TextBox(candidate,label.Text,true);
                        if(box.Left<20||box.Right>570||box.Bottom<20||box.Top>390||
                           occupied.Any(b=>b.Overlaps(box)||Hits(component,tip,b))||
                           bodies.Any(b=>b.Overlaps(box))||lines.Any(l=>Hits(l.A,l.B,box))||
                           insulationLines.Any(l=>Hits(l.A,l.B,box)))continue;
                        strokes.Add(new(label.SourceId,"position-leader",new[]{component,tip}));
                        lines.Add((component,tip));
                        leaderPaths.Add((component,tip));
                        Occupy(box);chosen=candidate;chosenAnchor=component;
                        break;
                    }
                }
            }
            if(chosen is null)throw new SheetCapacityException($"No readable annotation position for {label.SourceId} ({label.Role}: {label.Text}); sheet needs more space",label.SourceId,conflict:LayoutConflict.Annotation);
            result.Add(label with{Position=chosen.Value,Anchor=label.Role=="slope-projection"?label.Anchor:chosenAnchor});
        }
        return result;
    }
}
