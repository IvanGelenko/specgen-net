using System;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api.Automation;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("NavisAuditSmoke <model> <NavisAudit.dll|-> <out.json> [lines=all|A;B] [checks=…] [tolerance=…] [structures=1] [parts=1] [docx=…]");
            return 2;
        }
        var app = new NavisworksApplication();
        try
        {
            app.DisableProgress();
            // «-»: the installed bundle (ApplicationPlugins) loads it.
            if (args[1] != "-") app.AddPluginAssembly(Path.GetFullPath(args[1]));
            app.OpenFile(Path.GetFullPath(args[0]));
            var parameters = new[] { Path.GetFullPath(args[2]) }.Concat(args.Skip(3)).ToArray();
            var started = DateTime.Now;
            // NAVIS_PLUGIN=NavisAudit.PSPG runs the ribbon command (opens the window).
            var plugin = Environment.GetEnvironmentVariable("NAVIS_PLUGIN");
            var code = app.ExecuteAddInPlugin(string.IsNullOrEmpty(plugin) ? "NavisAuditJson.PSPG" : plugin, parameters);
            Console.WriteLine($"plugin returned {code} in {(DateTime.Now - started).TotalSeconds:0.0} s");
            if (File.Exists(args[2] + ".error.txt")) Console.WriteLine(File.ReadAllText(args[2] + ".error.txt"));
            if (File.Exists(args[2] + ".log")) Console.WriteLine(File.ReadAllText(args[2] + ".log"));
            return code is int n ? n : 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { app.Dispose(); }
    }
}
