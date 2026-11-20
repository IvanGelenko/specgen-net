using System.IO;
using System.Text;

namespace IsoGenerator.WordWorker;

internal sealed class Journal : IDisposable
{
    readonly string _logPath;
    readonly StreamWriter _writer;
    readonly object _gate = new();

    public Journal(string logPath)
    {
        _logPath = logPath;
        var dir = Path.GetDirectoryName(Path.GetFullPath(logPath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _writer = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
        {
            AutoFlush = true
        };
    }

    public string LogPath => _logPath;

    public void StepStart(string stepName, string? details = null)
    {
        Write("СТАРТ", stepName, details);
    }

    public void StepDone(string stepName, string? details = null)
    {
        Write("ЗАВЕРШЕНО", stepName, details);
    }

    public void Info(string message)
    {
        Write("ИНФО", message, null);
    }

    public void Warning(string stepName, string message)
    {
        Write("ПРЕДУПРЕЖДЕНИЕ", stepName, message);
    }

    public void Error(string stepName, Exception? ex = null)
    {
        string? details = ex is not null ? ex.ToString() : null;
        Write("ОШИБКА", stepName, details);
    }

    void Write(string level, string step, string? details)
    {
        string msg = details != null ? $"{step}: {details}" : step;
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {msg}";
        lock (_gate)
        {
            try
            {
                _writer.WriteLine(line);
                Console.WriteLine(line);
            }
            catch { }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _writer.Dispose(); } catch { }
        }
    }
}
