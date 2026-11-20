using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Autodesk.AutoCAD.Runtime;
using IsoGenerator.Publishing;

[assembly: CommandClass(typeof(IsoGenerator.AutoCAD.IsometricReportCommands))]
namespace IsoGenerator.AutoCAD;

public sealed class IsometricReportCommands
{
    [CommandMethod("ISOREPORTFRONT", CommandFlags.Modal)]
    public void Export()
    {
        string input = Environment.GetEnvironmentVariable("ISO_REPORT_REQUEST")
            ?? throw new InvalidOperationException("Нет задания Word.");
        string work = Path.GetDirectoryName(Path.GetFullPath(input))!;
        string result = Path.Combine(work, "result.json");
        var (workerPath, isExe) = IsometricReport.ResolveWorker();
        var start = IsometricReport.CreateWorkerStartInfo(workerPath, isExe, work, input, result);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Не удалось запустить внешний процесс Word.");
        process.WaitForExit();
        if (process.ExitCode != 0 || !File.Exists(result))
            throw new IOException($"Сбой внешнего процесса Word (код: {process.ExitCode}).");
    }
}

public static class IsometricReport
{
    public sealed record Pages(string CoverPdf, string ContentsPdf, string TitleDocx, string ContentsDocx, int ContentsPageCount);

    sealed record Request(IsometricReportDetails Details, string Number, string[] Titles);

    public static Task<Pages> RenderInWorker(IsometricReportDetails details, string number, IReadOnlyList<string> titles,
        string job, string engine, string assembly, string template, TimeSpan limit, CancellationToken token)
        => RenderInWorker(details, number, titles, job, limit, token);

    public static async Task<Pages> RenderInWorker(IsometricReportDetails details, string number, IReadOnlyList<string> titles,
        string job, TimeSpan limit, CancellationToken token)
    {
        string work = Path.Combine(job, "report");
        Directory.CreateDirectory(work);
        string request = Path.Combine(work, "request.json");
        string result = Path.Combine(work, "result.json");
        string log = Path.Combine(work, "report.log");

        File.WriteAllText(request, JsonSerializer.Serialize(new Request(details, number, titles.ToArray())));

        var (workerPath, isExe) = ResolveWorker();
        var start = CreateWorkerStartInfo(workerPath, isExe, work, request, result);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Не удалось запустить процесс выпуска Word/PDF.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(limit);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            token.ThrowIfCancellationRequested();
            throw new IOException($"Превышено время этапа выпуска титульного листа и содержания ({limit.TotalSeconds:0} с). Журнал: {log}");
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        if (process.ExitCode != 0 || !File.Exists(result))
        {
            const string prefix = "[IsoWordWorker] Ошибка: ";
            var workerError = stderr.Split('\n').LastOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
            string reason = workerError is null ? "Сбой выпуска титульного листа и содержания." : workerError[prefix.Length..].Trim();
            throw new IOException($"{reason}\nКод процесса: {process.ExitCode}. Журнал: {log}");
        }

        return JsonSerializer.Deserialize<Pages>(File.ReadAllText(result))
            ?? throw new InvalidDataException("Пустой результат Word.");
    }

    internal static (string Path, bool IsExe) ResolveWorker()
    {
        var candidates = new List<string>();

        string? assemblyDir = Path.GetDirectoryName(typeof(IsometricReport).Assembly.Location);
        if (!string.IsNullOrEmpty(assemblyDir))
        {
            candidates.Add(Path.Combine(assemblyDir, "IsoWordWorker.exe"));
            candidates.Add(Path.Combine(assemblyDir, "IsoWordWorker.dll"));
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            candidates.Add(Path.Combine(baseDir, "IsoWordWorker.exe"));
            candidates.Add(Path.Combine(baseDir, "IsoWordWorker.dll"));
            candidates.Add(Path.Combine(baseDir, "Contents", "Windows", "IsoWordWorker.exe"));
            candidates.Add(Path.Combine(baseDir, "Contents", "Windows", "IsoWordWorker.dll"));
        }

        // Relative development search paths
        string[] searchBases = { assemblyDir ?? "", baseDir };
        foreach (var sBase in searchBases.Where(b => !string.IsNullOrEmpty(b)))
        {
            string root = sBase;
            while (!string.IsNullOrEmpty(root) && !File.Exists(Path.Combine(root, "VERSION")))
            {
                string? parent = Path.GetDirectoryName(root);
                if (parent == root) break;
                root = parent!;
            }
            if (!string.IsNullOrEmpty(root) && File.Exists(Path.Combine(root, "VERSION")))
            {
                candidates.Add(Path.Combine(root, "iso", "src", "IsoGenerator.WordWorker", "bin", "Debug", "net8.0-windows", "IsoWordWorker.exe"));
                candidates.Add(Path.Combine(root, "iso", "src", "IsoGenerator.WordWorker", "bin", "Release", "net8.0-windows", "IsoWordWorker.exe"));
                candidates.Add(Path.Combine(root, "iso", "src", "IsoGenerator.WordWorker", "bin", "Debug", "net8.0-windows", "IsoWordWorker.dll"));
                candidates.Add(Path.Combine(root, "iso", "src", "IsoGenerator.WordWorker", "bin", "Release", "net8.0-windows", "IsoWordWorker.dll"));
                candidates.Add(Path.Combine(root, "src", "PipingSpecPlugin", "bin", "Release", "IsoWordWorker.exe"));
                candidates.Add(Path.Combine(root, "src", "PipingSpecPlugin", "bin", "Release", "IsoWordWorker.dll"));
            }
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                bool isExe = Path.GetExtension(candidate).Equals(".exe", StringComparison.OrdinalIgnoreCase);
                return (candidate, isExe);
            }
        }

        throw new FileNotFoundException(
            $"Не найден исполняемый модуль выпуска Word/PDF (IsoWordWorker.exe или IsoWordWorker.dll). Проверены пути: {string.Join("; ", candidates.Take(4))}");
    }

    internal static ProcessStartInfo CreateWorkerStartInfo(string workerPath, bool isExe, string work, string request, string result)
    {
        ProcessStartInfo start;
        if (isExe)
        {
            start = new ProcessStartInfo(workerPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = work,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add("front-matter");
            start.ArgumentList.Add(request);
            start.ArgumentList.Add(result);
        }
        else
        {
            start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = work,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add(workerPath);
            start.ArgumentList.Add("front-matter");
            start.ArgumentList.Add(request);
            start.ArgumentList.Add(result);
        }
        return start;
    }
}
