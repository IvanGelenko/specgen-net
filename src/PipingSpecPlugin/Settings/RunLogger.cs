using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PipingSpecPlugin.Settings
{
    /// <summary>
    /// Per-run diagnostic logger. Captures the full trail of one command
    /// (WRITESPEC, CONNECTALL, MOVEBYTAG …) into
    /// <c>%APPDATA%\PipingSpecPlugin\last-run.log</c> (spec commands) or
    /// <c>last-run-&lt;command&gt;.log</c> (everything else, see
    /// <see cref="LogFileNames"/>) so a support request
    /// can always include a self-contained record of what happened.
    ///
    /// Every line is stamped with elapsed-since-start (ms) and a level
    /// (INFO/WARN/ERROR), so the log shows BOTH ordering and timing — you
    /// can see which phase was slow or where it stopped. A header block
    /// records the environment (plug-in version, AutoCAD version, OS,
    /// machine) so the reader needn't ask "which build / which machine".
    ///
    /// The previous run's log is rolled to <c>prev-run.log</c> before the
    /// new one truncates, so a crash that produces a short log doesn't
    /// destroy the evidence from the run before it.
    /// </summary>
    public class RunLogger : IDisposable
    {
        readonly StreamWriter _writer;
        readonly string _path;
        readonly Stopwatch _clock = Stopwatch.StartNew();
        public string Path => _path;

        // Counters surfaced in the footer so the tail of the log gives an
        // at-a-glance health summary without reading every line.
        int _warnings;
        int _errors;

        public RunLogger(string command = null)
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PipingSpecPlugin");
            var (lastName, prevName) = LogFileNames(command);
            _path = System.IO.Path.Combine(dir, lastName);
            try
            {
                Directory.CreateDirectory(dir);

                // Roll the previous run aside so two consecutive runs are
                // both recoverable (a crash on run N still leaves run N-1).
                try
                {
                    if (File.Exists(_path))
                        File.Copy(_path, System.IO.Path.Combine(dir, prevName),
                                  overwrite: true);
                }
                catch { /* rolling is best-effort */ }

                _writer = new StreamWriter(_path, append: false, Encoding.UTF8)
                {
                    AutoFlush = true,
                };
                WriteHeader(command);
            }
            catch
            {
                // Logging is never allowed to break the run. Leave the
                // writer null and the Log methods will short-circuit.
            }
        }

        /// <summary>
        /// Log file per command family. Every command used to write the one
        /// <c>last-run.log</c>, so a SELECTBYLINE / MOVEBYTAG click right
        /// after a spec run destroyed the spec trace the user was about to
        /// send. The spec commands (WRITESPEC / WRITESPECPROJ / WRITESPECDWG)
        /// and unnamed runs keep the historical <c>last-run.log</c> — that is
        /// the file the docs and support instructions point at; the insulation
        /// commands (INSULSPEC / INSULSPECPROJ / INSULSPECDWG) share
        /// <c>last-run-insulspec.log</c>; every other command gets
        /// <c>last-run-&lt;command&gt;.log</c>.
        /// </summary>
        internal static (string last, string prev) LogFileNames(string command)
        {
            var cmd = (command ?? "").Trim();
            if (cmd.Length == 0 || cmd.StartsWith("WRITESPEC", StringComparison.OrdinalIgnoreCase))
                return ("last-run.log", "prev-run.log");
            if (cmd.StartsWith("INSULSPEC", StringComparison.OrdinalIgnoreCase))
                return ("last-run-insulspec.log", "prev-run-insulspec.log");
            var sb = new StringBuilder(cmd.Length);
            foreach (var ch in cmd.ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(ch) && ch < 128 ? ch : '-');
            var slug = sb.ToString();
            return ($"last-run-{slug}.log", $"prev-run-{slug}.log");
        }

        void WriteHeader(string command)
        {
            // Raw writes (no timestamp prefix) — this block is metadata, not
            // events. Each line guarded so one failing probe can't abort the
            // header.
            void Raw(string s) { try { _writer.WriteLine(s); } catch { } }

            Raw("================ PipingSpecPlugin run ================");
            Raw($"  command     : {command ?? "(unspecified)"}");
            Raw($"  plugin ver  : {ThisAssemblyVersion()}");
            Raw($"  started     : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} (local)");
            try { Raw($"  acad ver    : {Probe(() => Environment.GetEnvironmentVariable("ACAD") ?? AcadProductInfo())}"); } catch { }
            try { Raw($"  os          : {Environment.OSVersion}"); } catch { }
            try { Raw($"  machine     : {Environment.MachineName} / user {Environment.UserName}"); } catch { }
            try { Raw($"  64-bit proc : {Environment.Is64BitProcess}"); } catch { }
            try { Raw($"  clr         : {Environment.Version}"); } catch { }
            Raw("------------------------------------------------------");
        }

        static string AcadProductInfo()
        {
            // AutoCAD's managed API exposes the product string, but reading
            // it here would pull an AutoCAD reference into this otherwise
            // self-contained logger. Callers that know the version can log
            // it explicitly; we keep the dependency-free fallback.
            return "(см. строку запуска команды)";
        }

        static string Probe(Func<string> f)
        {
            try { return f() ?? ""; } catch { return ""; }
        }

        // ---- event API --------------------------------------------------

        /// <summary>Default level INFO — keeps every existing Log(line) call working.</summary>
        public void Log(string line) => Write("INFO", line);

        public void Info(string line) => Write("INFO", line);
        public void Warn(string line) { _warnings++; Write("WARN", line); }
        public void Error(string line) { _errors++; Write("ERR ", line); }

        void Write(string level, string line)
        {
            if (_writer == null) return;
            try
            {
                // [   1234ms] LEVEL  message — fixed-width so columns align
                // and the elapsed time is easy to scan for slow phases.
                _writer.WriteLine($"[{_clock.ElapsedMilliseconds,7}ms] {level} {line}");
            }
            catch { }
        }

        public void Dispose()
        {
            if (_writer != null)
            {
                try
                {
                    _writer.WriteLine("------------------------------------------------------");
                    _writer.WriteLine(
                        $"  finished    : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} "
                      + $"({_clock.ElapsedMilliseconds} ms total)");
                    _writer.WriteLine($"  warnings    : {_warnings}");
                    _writer.WriteLine($"  errors      : {_errors}");
                    _writer.WriteLine("======================================================");
                }
                catch { }
            }
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
