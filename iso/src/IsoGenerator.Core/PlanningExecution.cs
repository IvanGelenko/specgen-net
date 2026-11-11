namespace IsoGenerator.Core;

public sealed class PlanningLimitException(string message) : IOException(message);

public sealed record PlanningProgress(string Detail,int Variants);

// Per-operation state follows the async execution context, never AutoCAD global state.
internal sealed class PlanningExecution : IDisposable
{
    static readonly AsyncLocal<PlanningExecution?> current=new();
    readonly PlanningExecution? previous;readonly CancellationToken token;readonly IProgress<PlanningProgress>? progress;
    readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();long last=-1000;int variants;
    public PlanningExecution(CancellationToken token,IProgress<PlanningProgress>? progress)
    {previous=current.Value;this.token=token;this.progress=progress;token.ThrowIfCancellationRequested();current.Value=this;}
    public static void Check()
    {
        var scope=current.Value;if(scope is null)return;
        scope.token.ThrowIfCancellationRequested();
        // This is not a capacity failure: candidate-search catch blocks must not swallow it.
        if(scope.clock.Elapsed>TimeSpan.FromSeconds(120)||scope.variants>=512)
            throw new PlanningLimitException($"Расчёт компоновки остановлен: достигнут предел поиска (120 с или 512 вариантов). Проверено вариантов: {scope.variants}. Попробуйте другой ракурс или задайте точки разрыва. Итоговые файлы не изменены.");
    }
    public static void Report(string detail,bool variant=false)
    {
        Check();var scope=current.Value;if(scope is null)return;if(variant)scope.variants++;
        if(scope.clock.ElapsedMilliseconds-scope.last<250)return;scope.last=scope.clock.ElapsedMilliseconds;
        scope.progress?.Report(new(detail,scope.variants));
    }
    public void Dispose()=>current.Value=previous;
}
