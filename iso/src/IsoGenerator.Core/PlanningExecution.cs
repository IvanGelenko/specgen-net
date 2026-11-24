namespace IsoGenerator.Core;

public sealed class PlanningLimitException(string message) : IOException(message);

public sealed record PlanningProgress(string Detail,int Variants)
{
    public PlanningRejection? Rejection {get;init;}
}

public sealed record PlanningRejection(
    [property:System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<LayoutConflict>))]
    LayoutConflict Conflict,string? SourceId,int Parts,int Density,
    double Margin,IReadOnlyDictionary<string,double>? Extensions,double? Scale,
    double PaperWidthOverflow,double PaperHeightOverflow);

internal sealed class PlanningExecution
{
    readonly CancellationToken token;
    readonly IProgress<PlanningProgress>? progress;
    readonly TimeSpan timeLimit;
    readonly int variantLimit;
    readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();
    long last=-1000;
    int variants;

    public double MinimumPipeLength {get;}
    internal bool PreferDimensionCorners {get;set;}

    public PlanningExecution(CancellationToken token,IProgress<PlanningProgress>? progress,TimeSpan timeLimit,
        int variantLimit,double minimumPipeLength)
    {
        if(!double.IsFinite(minimumPipeLength)||minimumPipeLength<1||minimumPipeLength>100000)
            throw new ArgumentOutOfRangeException(nameof(minimumPipeLength));
        token.ThrowIfCancellationRequested();
        this.token=token;
        this.progress=progress;
        this.timeLimit=timeLimit;
        this.variantLimit=variantLimit;
        MinimumPipeLength=minimumPipeLength;
    }

    public bool CanTryOptionalVariant()
    {
        token.ThrowIfCancellationRequested();
        var reserve=TimeSpan.FromSeconds(Math.Min(15,timeLimit.TotalSeconds*.25));
        return clock.Elapsed<timeLimit-reserve&&variants<Math.Max(1,variantLimit-12);
    }

    public void Check()
    {
        token.ThrowIfCancellationRequested();
        if(clock.Elapsed>timeLimit)throw Limit();
    }

    public void Report(string detail,bool variant=false)
    {
        Check();
        if(variant)
        {
            if(variants>=variantLimit)throw Limit();
            variants++;
        }
        if(clock.ElapsedMilliseconds-last<250)return;
        last=clock.ElapsedMilliseconds;
        progress?.Report(new(detail,variants));
    }

    public void Reject(SheetCapacityException failure,int parts,int density,double margin,
        IReadOnlyDictionary<string,double>? extensions,double? scale=null)
    {
        if(progress is null)return;
        progress.Report(new(failure.Message,variants)
        {
            Rejection=new(failure.Conflict,failure.SourceId,parts,density,margin,
                extensions?.ToDictionary(entry=>entry.Key,entry=>entry.Value),scale,
                failure.PaperWidthOverflow,failure.PaperHeightOverflow)
        });
    }

    PlanningLimitException Limit()=>new($"Расчёт компоновки остановлен: достигнут предел поиска ({timeLimit.TotalSeconds:0} с или {variantLimit} вариантов). Проверено вариантов: {variants}. Попробуйте другой ракурс или задайте точки разрыва. Итоговые файлы не изменены.");
}
