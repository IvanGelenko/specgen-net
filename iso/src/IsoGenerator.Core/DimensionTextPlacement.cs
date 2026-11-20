namespace IsoGenerator.Core;
// A second layout pass: dimension baselines are fixed; text centers avoid all extension
// lines and other text. This is intentionally separate from baseline side/offset selection.
internal static class DimensionTextPlacement
{
    readonly record struct Ink(SheetPoint Center,SheetPoint Axis,double HalfWidth,double HalfHeight)
    {
        public bool Hits(SheetPoint a,SheetPoint b)
        {
            double ax=(a.X-Center.X)*Axis.X+(a.Y-Center.Y)*Axis.Y,ay=-(a.X-Center.X)*Axis.Y+(a.Y-Center.Y)*Axis.X;
            double bx=(b.X-Center.X)*Axis.X+(b.Y-Center.Y)*Axis.Y,by=-(b.X-Center.X)*Axis.Y+(b.Y-Center.Y)*Axis.X;
            double low=0,high=1,dx=bx-ax,dy=by-ay;
            bool Clip(double p,double q){if(Math.Abs(p)<1e-10)return q>=0;double t=q/p;if(p<0)low=Math.Max(low,t);else high=Math.Min(high,t);return low<=high;}
            return Clip(-dx,ax+HalfWidth)&&Clip(dx,HalfWidth-ax)&&Clip(-dy,ay+HalfHeight)&&Clip(dy,HalfHeight-ay);
        }
        public bool Overlaps(Ink b)
        {
            foreach(var axis in new[]{Axis,new(-Axis.Y,Axis.X),b.Axis,new(-b.Axis.Y,b.Axis.X)})
            {
                double separation=Math.Abs((b.Center.X-Center.X)*axis.X+(b.Center.Y-Center.Y)*axis.Y);
                double ra=HalfWidth*Math.Abs(Axis.X*axis.X+Axis.Y*axis.Y)+HalfHeight*Math.Abs(-Axis.Y*axis.X+Axis.X*axis.Y);
                double rb=b.HalfWidth*Math.Abs(b.Axis.X*axis.X+b.Axis.Y*axis.Y)+b.HalfHeight*Math.Abs(-b.Axis.Y*axis.X+b.Axis.X*axis.Y);
                if(separation>=ra+rb+.6)return false;
            }
            return true;
        }
    }
    static Ink Box(PipeDimension d)
    {
        double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,len=Math.Sqrt(dx*dx+dy*dy);
        double ratio=IsoAdvancedSettings.Current.DimensionTextHeight/3.5;
        return new(d.TextPosition!.Value,new(dx/len,dy/len),d.Label.Length*1.05*ratio+.2,1.77*ratio);
    }
    internal static (SheetPoint A,SheetPoint B)[] ObstacleSegments(IReadOnlyList<Stroke> strokes)=>
        strokes.Where(stroke=>stroke.Role is not ("insulation" or "slope-hatch"))
            .SelectMany(stroke=>stroke.Points.Zip(stroke.Points.Skip(1),(a,b)=>(a,b))).ToArray();

    internal static int CollisionCount(IReadOnlyList<PipeDimension> rows,IReadOnlyList<Stroke> strokes,
        (SheetPoint A,SheetPoint B)[]? obstacles=null)
    {PlaceText(rows,strokes,out var blocked,obstacles:obstacles);return blocked.Length;}
    public static IReadOnlyList<PipeDimension> Place(IReadOnlyList<PipeDimension> input,IReadOnlyList<Stroke> strokes,PlanningExecution? execution=null)
    {
        var rows=DimensionRowAlignment.EnforceChainOrder(input).ToArray();int[] blocked=Array.Empty<int>();
        for(int attempt=0;attempt<=40;attempt++)
        {
            execution?.Check();
            rows=DimensionRowAlignment.EnforceChainOrder(rows).ToArray();
            var placed=PlaceText(rows,strokes,out blocked);
            if(blocked.Length==0)return placed;
            if(attempt==40)break;
            // Preserve the isometric dimension plane. Text congestion cannot replace
            // the selected Z/other isometric axis with a screen-space perpendicular.
            // Routing scores line crossings, but a chain's opposite side may be much
            // clearer for its text. Test the whole reflected row, keeping centered text.
            if(attempt==1)
            {
                foreach(string chain in blocked.Select(i=>rows[i].ChainId).Distinct().ToArray())
                {
                    var trial=rows.ToArray();
                    for(int i=0;i<trial.Length;i++)if(trial[i].ChainId==chain)
                    {
                        var d=trial[i];double tx=d.End.X-d.Start.X,ty=d.End.Y-d.Start.Y,len=Math.Sqrt(tx*tx+ty*ty);
                        var n=new SheetPoint(-ty/len,tx/len);var p=d.DimensionLinePoint!.Value;
                        double offset=(p.X-d.Start.X)*n.X+(p.Y-d.Start.Y)*n.Y;
                        trial[i]=d with{DimensionLinePoint=new(p.X-2*offset*n.X,p.Y-2*offset*n.Y)};
                    }
                    PlaceText(trial,strokes,out var reflectedBlocked);
                    PlaceText(rows,strokes,out var currentBlocked);
                    if(reflectedBlocked.Length<currentBlocked.Length&&DimensionRouting.CrossingCount(trial)<=DimensionRouting.CrossingCount(rows))rows=trial;
                }
                placed=PlaceText(rows,strokes,out blocked);if(blocked.Length==0)return placed;
            }
            for(int i=0;i<placed.Count;i++)for(int j=i+1;j<placed.Count;j++)
                if(placed[i].ChainId==placed[j].ChainId&&placed[i].Role=="chain"&&placed[j].Role=="chain"&&Box(placed[i]).Overlaps(Box(placed[j])))
                {
                    var shortRow=placed[i].Millimeters<=placed[j].Millimeters?placed[i]:placed[j];
                    throw new SheetCapacityException("Недостаточно места между соседними размерными числами; требуется увеличить участок трубы.",shortRow.SourceId,shortRow.ModelStart,shortRow.ModelEnd,conflict:LayoutConflict.DimensionText);
                }
            var offending=blocked.Select(i=>rows[i]).OrderBy(d=>d.Role=="overall"?0:1).First();
            double dx=offending.End.X-offending.Start.X,dy=offending.End.Y-offending.Start.Y,l=Math.Sqrt(dx*dx+dy*dy);
            var normal=new SheetPoint(-dy/l,dx/l);var ex=offending.ExtensionDirection!.Value;
            var point=offending.DimensionLinePoint!.Value;
            double side=Math.Sign((point.X-offending.Start.X)*normal.X+(point.Y-offending.Start.Y)*normal.Y);
            double step=(side==0?1:side)/(ex.X*normal.X+ex.Y*normal.Y);
            var shift=new SheetPoint(ex.X*step,ex.Y*step);
            // Move the chain row only when necessary, retaining at least 7 mm to its overall row.
            for(int i=0;i<rows.Length;i++)if(rows[i].ChainId==offending.ChainId&&(offending.Role=="chain"||rows[i].Role==offending.Role))
            {
                var d=rows[i];var line=d.DimensionLinePoint!.Value;var text=d.TextPosition!.Value;
                rows[i]=d with{DimensionLinePoint=new(line.X+shift.X,line.Y+shift.Y),TextPosition=new(text.X+shift.X,text.Y+shift.Y)};
            }
        }
        throw new SheetCapacityException("Не удалось разместить размерные подписи без пересечений: "+string.Join(", ",blocked.Select(i=>rows[i].ChainId+":"+rows[i].Label)),blocked.Length>0?rows[blocked[0]].SourceId:null,blocked.Length>0?rows[blocked[0]].ModelStart:null,blocked.Length>0?rows[blocked[0]].ModelEnd:null,conflict:LayoutConflict.DimensionText);
    }
    internal static int[] FinalCollisions(IReadOnlyList<PipeDimension> rows,IReadOnlyList<Stroke> strokes)
    {
        PlaceText(rows,strokes,out var blocked,measureOnly:true);
        return blocked;
    }
    static IReadOnlyList<PipeDimension> PlaceText(IReadOnlyList<PipeDimension> input,IReadOnlyList<Stroke> strokes,out int[] blocked,bool measureOnly=false,(SheetPoint A,SheetPoint B)[]? obstacles=null)
    {
        var result=input.ToArray();
        var segments=new List<(SheetPoint A,SheetPoint B)>(obstacles??ObstacleSegments(strokes));
        foreach(var d in input)
        {
            double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,len=Math.Sqrt(dx*dx+dy*dy),nx=-dy/len,ny=dx/len;
            var ex=d.ExtensionDirection!.Value;var p=d.DimensionLinePoint!.Value;double dot=ex.X*nx+ex.Y*ny;
            SheetPoint Shift(SheetPoint a){double t=((p.X-a.X)*nx+(p.Y-a.Y)*ny)/dot;return new(a.X+ex.X*t,a.Y+ex.Y*t);}
            var a=Shift(d.Start);var b=Shift(d.End);
                        segments.Add((a,b));segments.Add((d.Start,new(a.X+ex.X,a.Y+ex.Y)));segments.Add((d.End,new(b.X+ex.X,b.Y+ex.Y)));
            var along=new SheetPoint(dx/len,dy/len);
            foreach(var (tip,sign) in new[]{(a,1.0),(b,-1.0)})
                foreach(double side in new[]{-.7,0,.7})
                    segments.Add((new(tip.X+nx*side,tip.Y+ny*side),new(tip.X+along.X*sign*(IsoAdvancedSettings.Current.ArrowSize+1)+nx*side,tip.Y+along.Y*sign*(IsoAdvancedSettings.Current.ArrowSize+1)+ny*side)));
        }
        // Text is centered above its own baseline. Resolve congestion by moving rows or
        // changing pipe spacing, never by sliding a number along/away from that baseline.
        for(int i=0;!measureOnly&&i<result.Length;i++)
        {
            var d=result[i];double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,l=Math.Sqrt(dx*dx+dy*dy);
            var n=new SheetPoint(-dy/l,dx/l);if(PipeDimension.ReverseTextAxis(dx,dy))n=new(-n.X,-n.Y);
            var ex=d.ExtensionDirection!.Value;var p=d.DimensionLinePoint!.Value;var mid=new SheetPoint((d.Start.X+d.End.X)/2,(d.Start.Y+d.End.Y)/2);
            double t=((p.X-mid.X)*n.X+(p.Y-mid.Y)*n.Y)/(ex.X*n.X+ex.Y*n.Y);
            result[i]=d with{TextPosition=new(mid.X+ex.X*t+n.X*IsoAdvancedSettings.Current.TextLift,mid.Y+ex.Y*t+n.Y*IsoAdvancedSettings.Current.TextLift)};
        }
        var boxes=result.Select(Box).ToArray();
        HashSet<int> FindConflicts()
        {
            var conflicts=Enumerable.Range(0,result.Length).Where(i=>segments.Any(s=>boxes[i].Hits(s.A,s.B))).ToHashSet();
            for(int i=0;i<result.Length;i++)
                for(int j=i+1;j<result.Length;j++)
                    if(boxes[i].Overlaps(boxes[j])){conflicts.Add(i);conflicts.Add(j);}
            return conflicts;
        }
        var bad=FindConflicts();
        if(!measureOnly&&bad.Count>0)
        {
            foreach(int i in bad.ToArray())
            {
                var d=result[i];double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,l=Math.Sqrt(dx*dx+dy*dy);
                var along=new SheetPoint(dx/l,dy/l);var orig=d.TextPosition!.Value;
                foreach(double slide in new[]{2.0,-2.0,3.5,-3.5,5.0,-5.0,7.0,-7.0})
                {
                    var cand=d with{TextPosition=new(orig.X+along.X*slide,orig.Y+along.Y*slide)};
                    var box=Box(cand);
                    if(segments.Any(s=>box.Hits(s.A,s.B)))continue;
                    bool overlap=false;
                    for(int j=0;j<result.Length;j++)if(i!=j&&box.Overlaps(boxes[j])){overlap=true;break;}
                    if(!overlap){result[i]=cand;boxes[i]=box;bad.Remove(i);break;}
                }
            }
            bad=FindConflicts();
        }
        blocked=bad.OrderBy(i=>i).ToArray();
        if(Environment.GetEnvironmentVariable("ISO_LAYOUT_TRACE")=="1"&&blocked.Length>0)
        {
            foreach(int bi in blocked)
            {
                var box=Box(result[bi]);
                Console.Error.WriteLine($"[TRACE-BAD] {result[bi].ChainId}:{result[bi].Label} (source={result[bi].SourceId}) center=({box.Center.X:F1},{box.Center.Y:F1})");
                foreach(var s in segments)
                    if(box.Hits(s.A,s.B)) Console.Error.WriteLine($"    hits segment ({s.A.X:F1},{s.A.Y:F1})-({s.B.X:F1},{s.B.Y:F1})");
                for(int bj=0;bj<result.Length;bj++)
                    if(bi!=bj&&box.Overlaps(Box(result[bj]))) Console.Error.WriteLine($"    overlaps {result[bj].ChainId}:{result[bj].Label}");
            }
        }
        return result;
    }
}
