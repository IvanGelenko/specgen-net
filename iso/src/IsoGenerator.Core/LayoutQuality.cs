namespace IsoGenerator.Core;

internal static class LayoutQuality
{
    public static double Footprint(SheetScene scene)
    {
        var geometry=scene.Strokes.Where(stroke=>stroke.Role is "pipe" or "component" or "elbow"
            or "branch" or "valve" or "flange" or "flange-neck").SelectMany(stroke=>stroke.Points)
            .Concat((scene.Symbols??Array.Empty<SymbolGlyph>()).Where(symbol=>!symbol.Context)
                .SelectMany(SymbolFootprint.Corners)).ToArray();
        if(geometry.Length==0)return 0;
        var width=geometry.Max(point=>point.X)-geometry.Min(point=>point.X);
        var height=geometry.Max(point=>point.Y)-geometry.Min(point=>point.Y);
        // Thin lines still benefit from using the long available axis.
        return (width+1)*(height+1);
    }
    public static (int Clearance,int FittingCrossings,int Crossings,int LongExtensions,int NonGrid) Dimensions(SheetScene scene)
    {
        var segments=DimensionRouting.GetSegments(scene.Dimensions);
        // Beyond two ordinary dimension lanes, a long extension makes it harder
        // to see which run a dimension belongs to.
        double longExtension=IsoAdvancedSettings.Current.LongExtensionThreshold;
        int longExtensions=segments.Count(segment=>!segment.Baseline&&
            Math.Pow(segment.B.X-segment.A.X,2)+Math.Pow(segment.B.Y-segment.A.Y,2)>longExtension*longExtension);
        return (DimensionRouting.PipeClearanceViolations(scene.Dimensions,scene.Strokes),DimensionRouting.FittingCrossings(scene),DimensionRouting.CrossingCount(scene.Dimensions),longExtensions,scene.NonGridPipeCount);
    }
    public static (int Crossings,int Overlong,int Reach) Leaders(SheetScene scene)
    {
        var strokes=scene.Strokes.Where(stroke=>stroke.Role is "elevation-leader" or "elevation-shelf")
            .SelectMany(stroke=>stroke.Points.Zip(stroke.Points.Skip(1),(a,b)=>new DimensionRouting.Segment(a,b,false))).ToArray();
        int crossings=0;
        for(int i=0;i<strokes.Length;i++)
            for(int j=0;j<i;j++)
                if(DimensionRouting.CheckCross(strokes[i],strokes[j]))crossings++;
        var reaches=scene.Annotations.Where(label=>label.Anchor.HasValue&&label.LeaderKnee.HasValue)
            .Select(label=>
            {
                var a=label.Anchor!.Value;var b=label.LeaderKnee!.Value;
                return Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));
            }).ToArray();
        return (crossings,reaches.Count(length=>length>30.01),
            (int)Math.Round(reaches.Sum(length=>Math.Max(0,length-20))/5));
    }

    public static int Compare(IReadOnlyList<PlannedSheet> first,IReadOnlyList<PlannedSheet> second)
    {
        return Score(first).CompareTo(Score(second));
    }

    static (int Clearance,int Fittings,int LeaderCrossings,int Sheets,int Pages,int DimensionCrossings,
        int LongExtensions,int LongLeaders,int Reach,int NonGrid,double Unused) Score(IReadOnlyList<PlannedSheet> sheets)
    {
        int clearance=0,fittings=0,crossings=0,extensions=0,nonGrid=0,count=0,leaderCrossings=0,longLeaders=0,reach=0;
        double area=0;
        foreach(var sheet in sheets)
        {
            if(sheet.Scene.BomOnly)continue;
            var dimension=Dimensions(sheet.Scene);
            var leader=Leaders(sheet.Scene);
            clearance+=dimension.Clearance;
            fittings+=dimension.FittingCrossings;
            crossings+=dimension.Crossings;
            extensions+=dimension.LongExtensions;
            nonGrid+=dimension.NonGrid;
            count++;
            leaderCrossings+=leader.Crossings;
            longLeaders+=leader.Overlong;
            reach+=leader.Reach;
            area+=Footprint(sheet.Scene);
        }
        return (clearance,fittings,leaderCrossings,count,sheets.Count,crossings,extensions,longLeaders,reach,nonGrid,-Math.Round(area));
    }
}
