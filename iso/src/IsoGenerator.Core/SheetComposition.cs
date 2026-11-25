namespace IsoGenerator.Core;

public readonly record struct DrawingBounds(double Left,double Bottom,double Right,double Top)
{
    public double Width=>Right-Left;
    public double Height=>Top-Bottom;
    public SheetPoint Center=>new((Left+Right)/2,(Bottom+Top)/2);
    public DrawingBounds Move(double x,double y)=>new(Left+x,Bottom+y,Right+x,Top+y);
    public bool Overlaps(DrawingBounds b)=>Left<b.Right-1e-6&&Right>b.Left+1e-6&&Bottom<b.Top-1e-6&&Top>b.Bottom+1e-6;
}

// Translate the whole drawing, including dimensions and leaders. Symbols and fonts are never scaled.
// Center in the rectangular diagram field to the left of the title block and BOM.
public static class SheetComposition
{
    public static DrawingBounds Paper=>new(IsoAdvancedSettings.Current.PaperLeft,IsoAdvancedSettings.Current.PaperBottom,
        IsoAdvancedSettings.Current.PaperRight,IsoAdvancedSettings.Current.PaperTop);
    public static IReadOnlyList<DrawingBounds> Reserved(double bomRows)=>new[]{new DrawingBounds(398,Math.Max(60,399-IsoAdvancedSettings.Current.BomRowHeight*bomRows-6),590,420),new DrawingBounds(398,0,590,60)};
    static DrawingBounds Box(IEnumerable<SheetPoint> points,double pad=0)
    {
        var p=points.ToArray();return new(p.Min(v=>v.X)-pad,p.Min(v=>v.Y)-pad,p.Max(v=>v.X)+pad,p.Max(v=>v.Y)+pad);
    }
    public static IReadOnlyList<DrawingBounds> Pieces(SheetScene scene)
    {
        var pieces=new List<DrawingBounds>();
        foreach(var stroke in scene.Strokes)
            foreach(var (a,b) in stroke.Points.Zip(stroke.Points.Skip(1)))pieces.Add(Box(new[]{a,b},.3));
        foreach(var symbol in scene.Symbols??Array.Empty<SymbolGlyph>())pieces.Add(Box(SymbolFootprint.Corners(symbol),.3));
        foreach(var label in scene.Annotations.Where(a=>a.Role is not ("title" or "status")))
            {var box=AnnotationPlacement.Bounds(label);pieces.Add(new(box.Left,box.Bottom,box.Right,box.Top));}
        foreach(var d in scene.Dimensions)
        {
            double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,l=Math.Sqrt(dx*dx+dy*dy);if(l<.001)continue;
            var n=new SheetPoint(-dy/l,dx/l);var ex=d.ExtensionDirection!.Value;var p=d.DimensionLinePoint!.Value;
            SheetPoint Shift(SheetPoint a){double t=((p.X-a.X)*n.X+(p.Y-a.Y)*n.Y)/(ex.X*n.X+ex.Y*n.Y);return new(a.X+ex.X*t,a.Y+ex.Y*t);}
            var a=Shift(d.Start);var b=Shift(d.End);
            pieces.Add(Box(new[]{a,b},.8));
            pieces.Add(Box(new[]{d.Start,new SheetPoint(a.X+ex.X,a.Y+ex.Y)},.2));
            pieces.Add(Box(new[]{d.End,new SheetPoint(b.X+ex.X,b.Y+ex.Y)},.2));
            var ink=AnnotationPlacement.DimensionBox(d);pieces.Add(new(ink.Left,ink.Bottom,ink.Right,ink.Top));
        }
        foreach(var weld in scene.Welds??Array.Empty<WeldMarker>())pieces.Add(new(weld.Position.X-.9,weld.Position.Y-.9,weld.Position.X+.9,weld.Position.Y+.9));
        return pieces;
    }
    // Clear only hatch ink behind text. Geometry and dimensions remain above the hatch.
    public static IEnumerable<Stroke> DrawStrokes(SheetScene scene)
    {
        var hatch=scene.Strokes.Where(stroke=>stroke.Role=="slope-hatch").ToArray();
        if(hatch.Length>0)
        {
            var masks=scene.Annotations.Where(label=>label.Role is not ("title" or "status"))
                .Select(AnnotationPlacement.Bounds)
                .Concat(scene.Dimensions.Where(dimension=>dimension.TextPosition.HasValue)
                    .Select(AnnotationPlacement.DimensionBox)).ToArray();
            const double padding=1.2;
            foreach(var stroke in hatch)
            {
                double y=stroke.Points[0].Y;
                double start=stroke.Points.Min(point=>point.X),end=stroke.Points.Max(point=>point.X);
                foreach(var mask in masks.Where(box=>y>=box.Bottom-padding&&y<=box.Top+padding&&
                    box.Right+padding>start&&box.Left-padding<end).OrderBy(box=>box.Left))
                {
                    double left=Math.Max(start,mask.Left-padding);
                    if(left-start>.05)yield return stroke with{Points=new SheetPoint[]{new(start,y),new(left,y)}};
                    start=Math.Max(start,mask.Right+padding);
                    if(start>=end)break;
                }
                if(end-start>.05)yield return stroke with{Points=new SheetPoint[]{new(start,y),new(end,y)}};
            }
        }
        foreach(var stroke in scene.Strokes)
            if(stroke.Role!="slope-hatch")yield return stroke;
    }

    public static DrawingBounds Bounds(SheetScene scene)=>TryBounds(scene)??throw new InvalidOperationException("На листе нет графики.");

    internal static DrawingBounds? TryBounds(SheetScene scene)
    {
        var pieces=Pieces(scene);
        return pieces.Count==0?null:Bounds(pieces);
    }

    static DrawingBounds Bounds(IReadOnlyList<DrawingBounds> pieces)=>
        new(pieces.Min(p=>p.Left),pieces.Min(p=>p.Bottom),pieces.Max(p=>p.Right),pieces.Max(p=>p.Top));
    public static SheetScene Center(SheetScene scene)
    {
        var pieces=Pieces(scene);if(pieces.Count==0)return scene;
        var bounds=Bounds(pieces);double minX=Paper.Left-bounds.Left,maxX=Paper.Right-bounds.Right,minY=Paper.Bottom-bounds.Bottom,maxY=Paper.Top-bounds.Top;
        if(minX>maxX||minY>maxY)throw new SheetCapacityException($"Чертёж с размерами и выносками {bounds.Width:F1} x {bounds.Height:F1} мм не помещается в рабочую область A2 {Paper.Width:F1} x {Paper.Height:F1} мм.",paperWidthOverflow:Math.Max(0,bounds.Width-Paper.Width),paperHeightOverflow:Math.Max(0,bounds.Height-Paper.Height),conflict:LayoutConflict.PaperBounds);
        double desiredX=Paper.Center.X-bounds.Center.X,desiredY=Paper.Center.Y-bounds.Center.Y;
        var reserved=Reserved(BomCategories.BodyHeight(scene.BillOfMaterials)/IsoAdvancedSettings.Current.BomRowHeight);
        var forbidden=(from box in pieces from block in reserved select new DrawingBounds(block.Left-box.Right,block.Bottom-box.Top,block.Right-box.Left,block.Top-box.Bottom)).ToArray();
        var ys=new[]{Math.Clamp(desiredY,minY,maxY),minY,maxY}.Concat(forbidden.SelectMany(f=>new[]{f.Bottom-.01,f.Top+.01}).Where(y=>y>=minY&&y<=maxY)).Distinct().ToArray();
        SheetPoint? best=null;double bestScore=double.MaxValue;
        foreach(double y in ys)
        {
            var blocked=forbidden.Where(f=>y>f.Bottom&&y<f.Top).OrderBy(f=>f.Left).ToArray();
            var intervals=new List<(double Left,double Right)>();double start=minX;
            foreach(var f in blocked)
            {
                if(f.Right<=start||f.Left>maxX)continue;
                if(f.Left>start)intervals.Add((start,Math.Min(maxX,f.Left-.01)));
                start=Math.Max(start,f.Right+.01);if(start>maxX)break;
            }
            if(start<=maxX)intervals.Add((start,maxX));
            foreach(var interval in intervals.Where(v=>v.Left<=v.Right))
            {
                double x=Math.Clamp(desiredX,interval.Left,interval.Right),score=Math.Pow(x-desiredX,2)+Math.Pow(y-desiredY,2);
                if(score<bestScore){bestScore=score;best=new(x,y);}
            }
        }
        if(best is not {} move)throw new SheetCapacityException("Не найдено свободное положение чертежа между спецификацией и основной надписью.");
        SheetPoint P(SheetPoint p)=>new(p.X+move.X,p.Y+move.Y);
        return scene with
        {
            Strokes=scene.Strokes.Select(s=>s with{Points=s.Points.Select(P).ToArray()}).ToArray(),
            Symbols=scene.Symbols?.Select(g=>g with{Origin=P(g.Origin)}).ToArray(),
            Annotations=scene.Annotations.Select(a=>a.Role is "title" or "status"?a:a with
            {
                Position=P(a.Position),Anchor=a.Anchor is {} p?P(p):null,LeaderKnee=a.LeaderKnee is {} k?P(k):null,
                NearbyAnchors=a.NearbyAnchors?.Select(P).ToArray(),
                AngularDimension=a.AngularDimension is {} angle?new(P(angle.Vertex),P(angle.FirstRay),P(angle.SecondRay),P(angle.ArcPoint)):null
            }).ToArray(),
            Dimensions=scene.Dimensions.Select(d=>d with{Start=P(d.Start),End=P(d.End),DimensionLinePoint=d.DimensionLinePoint is {} l?P(l):null,TextPosition=d.TextPosition is {} t?P(t):null}).ToArray(),
            Welds=scene.Welds?.Select(w=>w with{Position=P(w.Position)}).ToArray()
        };
    }
}
