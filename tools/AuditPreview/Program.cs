using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PipingSpecPlugin.Audit;
using PipingSpecPlugin.UI;

// AuditPreview <parts.json> <out.png> [catalog.json] [run]
// Renders the audit window outside AutoCAD; «run» presses «Проверить» on the
// parts and selects the first result row.
class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var parts = JsonSerializer.Deserialize<List<AuditPart>>(File.ReadAllText(args[0]));
        var lines = parts.GroupBy(p => p.LineKey ?? AuditOptions.NoLine).Select(g => new AuditLineChoice
            { Key = g.Key, Label = g.Key == AuditOptions.NoLine ? AuditOptions.NoLineLabel : g.Key, Count = g.Count() }).ToList();
        lines.Sort((a, b) => AuditLineOrder.Compare(a.Key, b.Key));
        if (args.Length > 2 && File.Exists(args[2]))
        {
            using var catalog = JsonDocument.Parse(File.ReadAllText(args[2]));
            lines = JsonSerializer.Deserialize<List<AuditLineChoice>>(catalog.RootElement.GetProperty("Lines").GetRawText());
        }
        var snapshot = new AuditSnapshot(); snapshot.Parts.AddRange(parts);
        // Optional: the collisions of a PSAUDITJSON / AUDITSMOKE report, so
        // the preview shows that group too (the parts file has no geometry).
        var report = Environment.GetEnvironmentVariable("AUDIT_PREVIEW_REPORT");
        if (!string.IsNullOrEmpty(report) && File.Exists(report))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(report));
            snapshot.GeometryIssues.AddRange(JsonSerializer.Deserialize<List<AuditIssue>>(json.RootElement.GetProperty("Issues").GetRawText())
                .Where(i => i.RuleId == "collisions"));
        }
        var window = new AuditWindow("0873-003-ТХМ.dwg", lines, _ => snapshot, (_, __) => { });
        var content = (FrameworkElement)window.Content;
        // A real (off-screen) window and an idle dispatcher: the grid sizes
        // its star column and regroups asynchronously, a bare Measure/Arrange
        // rendered every column at its minimum width.
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000; window.Top = 0; window.Width = 1152; window.Height = 900; window.ShowInTaskbar = false;
        window.Show();
        Idle();
        if (args.Contains("run"))
        {
            Find<Button>(content, b => System.Windows.Automation.AutomationProperties.GetName(b) == "Проверить")
                .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Idle();
            var grid = Find<DataGrid>(content, _ => true);
            if (grid.Items.Count > 0) grid.SelectedIndex = 0;
            Idle();
        }
        int w = (int)content.ActualWidth, h = (int)content.ActualHeight;
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(args[1]); encoder.Save(output);
        window.Close();
    }

    static void Idle()
    {
        for (int i = 0; i < 3; i++)
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }

    static T Find<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T hit && match(hit)) return hit;
            var deeper = Find(child, match);
            if (deeper != null) return deeper;
        }
        return null;
    }
}
