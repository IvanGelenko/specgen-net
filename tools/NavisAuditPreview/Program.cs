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
using NavisAudit.Core;
using NavisAudit.UI;
using PipingSpecPlugin.Audit;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(args[0]));
        var issues = JsonSerializer.Deserialize<List<AuditIssue>>(json.RootElement.GetProperty("Issues").GetRawText());
        var lines = issues.SelectMany(i => (i.Line ?? "").Split("; ")).Where(l => l.Length > 0).GroupBy(l => l)
            .Select(g => new AuditLineChoice { Key = g.Key, Label = g.Key, Count = g.Count() }).ToList();
        lines.Sort((a, b) => AuditLineOrder.Compare(a.Key, b.Key));
        var snapshot = new AuditSnapshot { Drawing = json.RootElement.GetProperty("Drawing").GetString() };
        snapshot.ReadIssues.AddRange(issues);
        var window = new NavisAuditWindow(new NavisAuditWindow.Host
        {
            Model = snapshot.Drawing, Lines = lines, Settings = new NavisSettings(),
            Checks = new[] { "collisions", "wall", "material", "connections" }.Select(id => (id, NavisTexts.Title(id), NavisTexts.Describe(id))).ToList(),
            Run = _ => snapshot, Show = (_, __) => { }, Summary = _ => "Пар объектов с пересечением геометрии: 145. ",
        });
        var content = (FrameworkElement)window.Content;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000; window.Top = 0; window.Width = 1152; window.Height = 900; window.ShowInTaskbar = false;
        window.Show();
        Idle();
        if (args.Contains("run"))
        {
            Find<Button>(content, b => System.Windows.Automation.AutomationProperties.GetName(b) == "Проверить").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
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
