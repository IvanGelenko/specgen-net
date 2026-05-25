using System;
using System.IO;
using System.Text;

namespace PipingSpecPlugin.Settings
{
    /// <summary>
    /// Lightweight session logger. Captures the same lines we write to
    /// AutoCAD's editor into a rolling file under
    /// <c>%APPDATA%\PipingSpecPlugin\last-run.log</c> so support
    /// requests can always include the full trail of one WRITESPEC run
    /// without copy-pasting from the command line.
    ///
    /// One file per run — we truncate on Start so the user can email the
    /// log alongside their .docx and know it is the run they care about.
    /// </summary>
    public class RunLogger : IDisposable
    {
        readonly StreamWriter _writer;
        readonly string _path;
        public string Path => _path;

        public RunLogger()
        {
            _path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PipingSpecPlugin", "last-run.log");
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path));
                _writer = new StreamWriter(_path, append: false, Encoding.UTF8)
                {
                    AutoFlush = true,
                };
                _writer.WriteLine($"=== PipingSpecPlugin {ThisAssemblyVersion()} @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            }
            catch
            {
                // Logging is never allowed to break the run. Leave the
                // writer null and the Log methods will short-circuit.
            }
        }

        public void Log(string line)
        {
            if (_writer == null) return;
            try { _writer.WriteLine(line); } catch { }
        }

        public void Dispose()
        {
            try { _writer?.Dispose(); } catch { }
        }

        static string ThisAssemblyVersion()
        {
            try
            {
                return typeof(RunLogger).Assembly.GetName().Version?.ToString() ?? "?";
            }
            catch { return "?"; }
        }
    }
}
