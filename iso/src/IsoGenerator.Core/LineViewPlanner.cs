using System.Diagnostics;

namespace IsoGenerator.Core;

public sealed record LineLayoutOptions(bool AutomaticView,int QuarterTurns,bool ExcludeFlangeHardware,
    bool IgnoreBreakPoints,bool InstrumentCalloutsOnly,double MinimumPipeLength,IsoAdvancedSettings Advanced);
public sealed record LineLayoutResult(int QuarterTurns,IReadOnlyList<PlannedSheet> Sheets,RememberedLayout? Remembered);

public static class LineViewPlanner
{
    public static LineLayoutResult Plan(PipelineSnapshot snapshot,IsoSymbolMap map,LineLayoutOptions options,
        RememberedLayout? remembered=null,int? rememberedView=null,bool saveLayout=false,
        CancellationToken token=default,IProgress<PlanningProgress>? progress=null,
        Action<int>? tryingView=null,Action<int,string>? failedView=null)
    {
        var clock=Stopwatch.StartNew();
        var views=(options.AutomaticView?AutomaticView.Rank(snapshot,token):new[]{options.QuarterTurns}).ToList();
        if(rememberedView is int previous&&views.Remove(previous))views.Insert(0,previous);

        var total=TimeSpan.FromSeconds(options.Advanced.TotalViewTimeSeconds);
        var perView=TimeSpan.FromSeconds(options.Advanced.ViewTimeSeconds);
        var failures=new List<string>();
        LineLayoutResult? best=null;
        (int Diagrams,int Pages,int DimensionCrossings) bestScore=(int.MaxValue,int.MaxValue,int.MaxValue);
        foreach(int turn in views)
        {
            token.ThrowIfCancellationRequested();
            var remaining=total-clock.Elapsed;
            if(remaining<=TimeSpan.Zero)break;
            tryingView?.Invoke(turn);
            try
            {
                RememberedLayout? cache=null;
                var sheets=SheetSetPlanner.Build(snapshot,map,turn,options.ExcludeFlangeHardware,token,progress,
                    options.IgnoreBreakPoints,options.InstrumentCalloutsOnly,
                    rememberedView==turn?remembered:null,saveLayout?value=>cache=value:null,
                    remaining<perView?remaining:perView,options.MinimumPipeLength,options.Advanced);

                var score=(Diagrams:sheets.Count(sheet=>!sheet.Scene.BomOnly),Pages:sheets.Count,
                    DimensionCrossings:sheets.Sum(sheet=>DimensionRouting.CrossingCount(sheet.Scene.Dimensions)));
                if(score.CompareTo(bestScore)<0)
                {
                    best=new(turn,sheets,cache);
                    bestScore=score;
                }
                // A clean single sheet already satisfies the primary criteria.
                if(!options.AutomaticView||rememberedView==turn||score is (1,1,0))break;
            }
            catch(Exception error) when(options.AutomaticView&&error is
                (SheetCapacityException or PlanningLimitException or LayoutProjectionException))
            {
                string message=$"Ракурс {turn+1}: {error.Message}";
                failures.Add(message);
                failedView?.Invoke(turn,message);
            }
        }
        return best??throw new InvalidDataException("Не удалось подобрать ракурс."+
            (failures.Count==0?"":Environment.NewLine+string.Join(Environment.NewLine,failures)));
    }
}

