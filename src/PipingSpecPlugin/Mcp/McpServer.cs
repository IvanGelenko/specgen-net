using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
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
        static HttpListener _listener;
        static SynchronizationContext _ui;
        static byte[] _token;
        static bool _ownsToken;
        static readonly JsonElement EmptyArgs = JsonDocument.Parse("{}").RootElement.Clone();

        static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Plant3dMcp");
        public static string TokenPath => Path.Combine(Dir, "token");
        public static string AutostartPath => Path.Combine(Dir, "autostart");
        public static bool IsRunning => _listener != null && _listener.IsListening;

        /// <summary>Called when the plug-in loads (on the UI thread): starts the server only when the user has opted in.</summary>
        public static void AutoStart()
        {
            _ui = SynchronizationContext.Current;
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
            if (_ui == null) _ui = SynchronizationContext.Current;
            if (_ui == null) throw new InvalidOperationException("контекст интерфейса недоступен: запустите PSMCP из Plant 3D");
            if (IsRunning) return;

            // Listen first: when the port is taken (another Plant 3D, or the development host) Start throws and the other token stays intact.
            var listener = new HttpListener();
            listener.Prefixes.Add(Prefix);
            try { listener.Start(); }
            catch (HttpListenerException ex) { throw new InvalidOperationException("порт 18765 занят другой программой или другим Plant 3D (" + ex.Message + ")"); }
            _listener = listener;

            _token = RandomNumberGenerator.GetBytes(32);
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(TokenPath, Convert.ToHexString(_token));
                _ownsToken = true;
            }
            catch
            {
                try { listener.Close(); } catch { }
                _listener = null; _token = null;
                throw;
            }
            var l = _listener;
            Task.Run(() => Loop(l));
            AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[PipingSpecPlugin] Сервер MCP запущен: " + Prefix + "\n");
        }

        public static void Stop()
        {
            try { _listener?.Close(); } catch { }
            _listener = null;
            if (_ownsToken) { try { File.Delete(TokenPath); } catch { } _ownsToken = false; }
        }

        public static void SetAutostart(bool on)
        {
            Directory.CreateDirectory(Dir);
            if (on) File.WriteAllText(AutostartPath, "1");
            else if (File.Exists(AutostartPath)) File.Delete(AutostartPath);
        }

        static async Task Loop(HttpListener l)
        {
            while (l.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await l.GetContextAsync(); } catch { break; }
                _ = Task.Run(() => Handle(ctx));
            }
        }

        static bool Authorized(HttpListenerRequest req)
        {
            if (req.HttpMethod != "POST") return false;
            if (req.Headers["Origin"] != null) return false;                       // browsers always send it cross-site
            if (req.Headers["Host"] != HostHeader) return false;                   // DNS rebinding
            if (req.ContentType == null || !req.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) return false;
            if (req.ContentLength64 > MaxBody) return false;
            string auth = req.Headers["Authorization"];
            if (auth == null || !auth.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
            try
            {
                byte[] given = Convert.FromHexString(auth.Substring(7).Trim());
                return _token != null && CryptographicOperations.FixedTimeEquals(given, _token);
            }
            catch (FormatException) { return false; }
        }

        static void Handle(HttpListenerContext ctx)
        {
            string result; int status = 200;
            try
            {
                if (!Authorized(ctx.Request)) { status = 403; result = JsonSerializer.Serialize(new { ok = false, error = "forbidden" }); }
                else result = Execute(ctx.Request);
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
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        static string Execute(HttpListenerRequest req)
        {
            string body;
            using (var r = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                var buf = new char[8192]; var sb = new StringBuilder(); int n;
                while ((n = r.Read(buf, 0, buf.Length)) > 0)
                {
                    sb.Append(buf, 0, n);
                    if (sb.Length > MaxBody) throw new InvalidOperationException("request body too large");
                }
                body = sb.ToString();
            }
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("tool", out var toolEl) || toolEl.ValueKind != JsonValueKind.String)
                throw new ArgumentException("request must contain a string field 'tool'");
            string tool = toolEl.GetString();
            JsonElement args = doc.RootElement.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Object ? a.Clone() : EmptyArgs;
            if (_ui == null) throw new InvalidOperationException("UI context unavailable: run PSMCP in Plant 3D");

            int state = 0; // 0 queued, 1 started, 2 cancelled
            object value = null; Exception err = null;
            using (var done = new ManualResetEventSlim())
            {
                _ui.Post(_ =>
                {
                    if (Interlocked.CompareExchange(ref state, 1, 0) != 0) return;
                    try { value = Plant3dMcp.Tools.Run(tool, args); }
                    catch (KeyNotFoundException) { err = new ArgumentException("missing required argument for '" + tool + "' (see the tool's input schema)"); }
                    catch (Exception e) { err = e; }
                    finally { done.Set(); }
                }, null);
                if (!done.Wait(UiWait))
                {
                    if (Interlocked.CompareExchange(ref state, 2, 0) == 0)
                        throw new TimeoutException("AutoCAD is busy (modal dialog or running command); the request was cancelled");
                    done.Wait(); // already running: wait for the real result
                }
            }
            if (err != null) throw err;
            return JsonSerializer.Serialize(new { ok = true, result = value });
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
