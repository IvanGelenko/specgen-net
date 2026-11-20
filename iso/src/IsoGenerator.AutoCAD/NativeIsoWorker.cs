using System.Diagnostics;
using System.IO;
using System.Text;

namespace IsoGenerator.AutoCAD;

internal static class NativeIsoWorker
{
    public static ProcessStartInfo Configure(string engine,string assembly,string template,string work,
        params string[] commands)
    {
        string working=Path.Combine(work,"working.dwg"),script=Path.Combine(work,"generate.scr");
        File.Copy(template,working);
        File.WriteAllLines(script,new[]{"SECURELOAD","0","_NETLOAD","\""+assembly+"\""}
            .Concat(commands).Concat(new[]{"_QUIT","_Y"}),new UTF8Encoding(true));
        var start=new ProcessStartInfo(engine)
        {
            UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=work,
            RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.Unicode,StandardErrorEncoding=Encoding.Unicode
        };
        foreach(string arg in new[]{"/i",working,"/s",script,"/l","en-US"})start.ArgumentList.Add(arg);
        return start;
    }

    public static async Task Run(ProcessStartInfo start,TimeSpan limit,string stage,CancellationToken token,params string[] expected)
    {
        token.ThrowIfCancellationRequested();
        string log=Path.Combine(start.WorkingDirectory,"console.log");
        using var process=Process.Start(start)??throw new InvalidOperationException("Не удалось запустить "+stage+".");
        var stdout=process.StandardOutput.ReadToEndAsync();
        var stderr=process.StandardError.ReadToEndAsync();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(limit);
        try{await process.WaitForExitAsync(timeout.Token);}
        catch(OperationCanceledException)
        {
            try{if(!process.HasExited)process.Kill(entireProcessTree:true);}
            catch(InvalidOperationException){}
            // Do not wait indefinitely if a renderer or a descendant still owns a pipe.
            try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}catch(TimeoutException){}
            await WriteLog();
            token.ThrowIfCancellationRequested();
            throw new IOException($"Превышено время этапа «{stage}» ({limit.TotalSeconds:0} с). Журнал: {log}");
        }
        await WriteLog();
        token.ThrowIfCancellationRequested();
        var missing=expected.Where(path=>!File.Exists(path)).Select(Path.GetFileName).ToArray();
        if(process.ExitCode!=0||missing.Length>0)
            throw new IOException($"Сбой этапа «{stage}». Код Core Console: {process.ExitCode}."+
                (missing.Length>0?$" Отсутствуют: {string.Join(", ",missing)}.":"")+$" Журнал: {log}");

        async Task WriteLog()
        {
            string output;
            try
            {
                await Task.WhenAll(stdout,stderr).WaitAsync(TimeSpan.FromSeconds(3));
                output=stdout.Result+"\n"+stderr.Result;
            }
            catch(TimeoutException){output="Поток журнала не завершился.";}
            await File.WriteAllTextAsync(log,output);
        }
    }
}

