using System;
using System.IO;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;
using SynchronizationContext = System.Threading.SynchronizationContext;

namespace PipingSpecPlugin.Mcp
{
    /// <summary>
    /// The MCP server of the Plant 3D tools, embedded in the plug-in: a local HTTP endpoint (127.0.0.1:18765) that runs the tool
    /// calls of Claude on the AutoCAD main thread (<see cref="Plant3dMcp.Tools.Run"/>). Every request must carry the bearer token of the
    /// session (%LOCALAPPDATA%\Plant3dMcp\token); browsers (Origin), foreign Host headers and other content types are refused.
    ///
    /// It is OFF by default: nothing listens until the user starts it with PSMCP, or opts in to the automatic start with the
    /// environment variable PLANT3D_MCP_AUTOSTART=1 or the file %LOCALAPPDATA%\Plant3dMcp\autostart. Nothing reaches the network,
    /// and send_command stays disabled unless PLANT3D_MCP_ALLOW_COMMANDS=1.
    /// </summary>
    public static class McpServer
    {
        const string HostHeader = "127.0.0.1:18765";
        const string Prefix = "http://" + HostHeader + "/";
        const int MaxBody = 1 << 20;
        static readonly TimeSpan UiWait = TimeSpan.FromSeconds(60);
        static readonly object Lifecycle = new object();
        static HttpListener _listener;
        static SynchronizationContext _ui;
        static CancellationTokenSource _stopping;
        static readonly JsonElement EmptyArgs = JsonDocument.Parse("{}").RootElement.Clone();

        static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Plant3dMcp");
        public static string TokenPath => Path.Combine(Dir, "token");
        public static string AutostartPath => Path.Combine(Dir, "autostart");
        public static bool IsRunning { get { lock (Lifecycle) return _listener?.IsListening == true; } }

        /// <summary>Called when the plug-in loads (on the UI thread): starts the server only when the user has opted in.</summary>
        public static void AutoStart()
        {
            lock (Lifecycle) _ui = SynchronizationContext.Current;
            bool on = Environment.GetEnvironmentVariable("PLANT3D_MCP_AUTOSTART") == "1" || File.Exists(AutostartPath);
            if (!on) return;
            try { Start(); }
            catch (Exception ex)
            {
                AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[PipingSpecPlugin] Сервер MCP не запущен: " + ex.Message + "\n");
            }
        }

        public static void Start()
        {
            lock (Lifecycle)
            {
                if (_listener != null) return;
                var ui = _ui ?? SynchronizationContext.Current
                    ?? throw new InvalidOperationException("контекст интерфейса недоступен: запустите PSMCP из Plant 3D");
                var listener = new HttpListener();
                listener.Prefixes.Add(Prefix);
                var token = RandomNumberGenerator.GetBytes(32);
                var temp = TokenPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    // Keep the existing token intact if another instance owns the port.
                    listener.Start();
                    Directory.CreateDirectory(Dir);
                    File.WriteAllText(temp, Convert.ToHexString(token));
                    File.Move(temp, TokenPath, overwrite: true);
                }
                catch
                {
                    listener.Close();
                    try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    throw;
                }
                _ui = ui;
                _listener = listener;
                _stopping = new CancellationTokenSource();
                var stopping = _stopping.Token;
                _ = Task.Run(() => Loop(listener, ui, token, stopping));
            }
            AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[PipingSpecPlugin] Сервер MCP запущен: " + Prefix + "\n");
        }

        public static void Stop()
        {
            lock (Lifecycle)
            {
                if (_listener == null) return;
                _stopping.Cancel();
                // Delete before releasing the port so that a new instance cannot replace the token first.
                try { File.Delete(TokenPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                _listener.Close();
                _listener = null;
                _stopping = null;
            }
        }

        public static void SetAutostart(bool on)
        {
            Directory.CreateDirectory(Dir);
            if (on) File.WriteAllText(AutostartPath, "1");
            else if (File.Exists(AutostartPath)) File.Delete(AutostartPath);
        }

        static async Task Loop(HttpListener l, SynchronizationContext ui, byte[] token, CancellationToken stopping)
        {
            while (!stopping.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await l.GetContextAsync(); } catch { break; }
                _ = Handle(ctx, ui, token, stopping);
            }
        }

        static bool Authorized(HttpListenerRequest req, byte[] token)
        {
            if (req.HttpMethod != "POST") return false;
            if (req.Headers["Origin"] != null) return false;                       // browsers always send it cross-site
            if (req.Headers["Host"] != HostHeader) return false;                   // DNS rebinding
            if (!string.Equals(req.ContentType?.Split(';', 2)[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase)) return false;
            if (req.ContentLength64 > MaxBody) return false;
            string auth = req.Headers["Authorization"];
            if (auth == null || !auth.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
            try
            {
                byte[] given = Convert.FromHexString(auth.Substring(7).Trim());
                return CryptographicOperations.FixedTimeEquals(given, token);
            }
            catch (FormatException) { return false; }
        }

        static async Task Handle(HttpListenerContext ctx, SynchronizationContext ui, byte[] token, CancellationToken stopping)
        {
            string result; int status = 200;
            try
            {
                if (!Authorized(ctx.Request, token)) { status = 403; result = JsonSerializer.Serialize(new { ok = false, error = "forbidden" }); }
                else result = await Execute(ctx.Request, ui, stopping).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                result = JsonSerializer.Serialize(new { ok = false, error = e.GetType().Name + ": " + e.Message });
            }
            try
            {
                var bytes = Encoding.UTF8.GetBytes(result);
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                await ctx.Response.OutputStream.WriteAsync(bytes, stopping).ConfigureAwait(false);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        static async Task<string> Execute(HttpListenerRequest req, SynchronizationContext ui, CancellationToken stopping)
        {
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await req.InputStream.ReadAsync(buffer, stopping).ConfigureAwait(false)) != 0)
            {
                if (body.Length + count > MaxBody) throw new InvalidOperationException("request body too large");
                body.Write(buffer, 0, count);
            }
            var json = body.GetBuffer().AsMemory(0, (int)body.Length);
            if (json.Span.StartsWith(Encoding.UTF8.Preamble)) json = json.Slice(Encoding.UTF8.Preamble.Length);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("tool", out var toolEl) || toolEl.ValueKind != JsonValueKind.String)
                throw new ArgumentException("request must contain a string field 'tool'");
            string tool = toolEl.GetString();
            JsonElement args = EmptyArgs;
            if (doc.RootElement.TryGetProperty("args", out var supplied))
            {
                if (supplied.ValueKind != JsonValueKind.Object) throw new ArgumentException("'args' must be an object");
                args = supplied.Clone();
            }

            int state = 0; // 0 queued, 1 started, 2 cancelled
            var done = new TaskCompletionSource<(object Value, Exception Error)>(TaskCreationOptions.RunContinuationsAsynchronously);
            ui.Post(_ =>
            {
                if (stopping.IsCancellationRequested || Interlocked.CompareExchange(ref state, 1, 0) != 0) return;
                try { done.SetResult((Plant3dMcp.Tools.Run(tool, args), null)); }
                catch (Exception e) { done.SetResult((null, e)); }
            }, null);
            (object Value, Exception Error) outcome;
            try { outcome = await done.Task.WaitAsync(UiWait, stopping).ConfigureAwait(false); }
            catch (TimeoutException)
            {
                if (Interlocked.CompareExchange(ref state, 2, 0) == 0)
                    throw new TimeoutException("AutoCAD is busy; the queued request was cancelled");
                throw new TimeoutException("The operation started in AutoCAD and may still finish. Check the drawing before retrying.");
            }
            if (outcome.Error != null) ExceptionDispatchInfo.Throw(outcome.Error);
            return JsonSerializer.Serialize(new { ok = true, result = outcome.Value });
        }
    }

    /// <summary>PSMCP — start / stop the embedded MCP server, or switch on its automatic start.</summary>
    public class McpCommand
    {
        /// <summary>The ribbon button: one click starts the server, the next one stops it.</summary>
        [CommandMethod("PSMCPTOGGLE")]
        public void Toggle()
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            try
            {
                if (McpServer.IsRunning) { McpServer.Stop(); ed?.WriteMessage("\nСервер MCP остановлен."); }
                else { McpServer.Start(); ed?.WriteMessage("\nСервер MCP включён: Claude может подключаться к этому Plant 3D (127.0.0.1:18765). Нажмите кнопку ещё раз, чтобы выключить."); }
            }
            catch (Exception ex) { ed?.WriteMessage("\nСервер MCP: " + ex.Message); }
        }

        [CommandMethod("PSMCP")]
        public void Run()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            string state = McpServer.IsRunning ? "запущен" : "остановлен";
            string auto = File.Exists(McpServer.AutostartPath) ? "включён" : "выключен";
            ed.WriteMessage($"\nСервер MCP (подключение Claude к Plant 3D): {state}; автозапуск {auto}.");
            var o = new Autodesk.AutoCAD.EditorInput.PromptKeywordOptions("\n[Запустить/Остановить/Автозапуск/Отмена] <Отмена>: ", "Запустить Остановить Автозапуск Отмена");
            o.Keywords.Default = "Отмена"; o.AllowNone = true;
            var r = ed.GetKeywords(o);
            string pick = r.Status == Autodesk.AutoCAD.EditorInput.PromptStatus.None ? "Отмена" : r.StringResult;
            try
            {
                switch (pick)
                {
                    case "Запустить": McpServer.Start(); break;
                    case "Остановить": McpServer.Stop(); ed.WriteMessage("\nСервер MCP остановлен."); break;
                    case "Автозапуск":
                        bool on = !File.Exists(McpServer.AutostartPath);
                        McpServer.SetAutostart(on);
                        ed.WriteMessage(on ? "\nАвтозапуск сервера MCP включён (при следующей загрузке плагина)." : "\nАвтозапуск сервера MCP выключен.");
                        break;
                }
            }
            catch (Exception ex) { ed.WriteMessage("\nСервер MCP: " + ex.Message); }
        }
    }
}
