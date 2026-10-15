using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PipingSpecPlugin.UI;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var commands = new[] { "WRITESPECPROJ", "WRITESPECDWG", "MOVEBYTAG", "PSCOLORBYLAYER", "SYNCVT", "SELECTBYSTATUS", "SELECTBYLINE", "SPECABOUT", "PSAUDIT", "BROWSE", "LOAD", "REFRESH", "APPLY", "CLOSE" };
        var labels = new[] { "Весь проект", "Чертёж", "Слои", "Цвет по слою", "Синхронизация", "По статусу", "По линии", "О программе", "Аудит", "Обзор", "Загрузить", "Обновить", "Применить", "Закрыть" };
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            for (int theme = 0; theme < 2; theme++)
            {
                dc.DrawRectangle(theme == 0 ? Brushes.WhiteSmoke : new SolidColorBrush(Color.FromRgb(35, 43, 54)), null, new Rect(0, theme * 230, 980, 230));
                for (int i = 0; i < commands.Length; i++)
                {
                    double x = (i % 7) * 140 + 42;
                    double y = theme * 230 + (i / 7) * 110 + 18;
                    var icon = CommandIcons.Create(commands[i]);
                    dc.DrawImage(icon, new Rect(x, y, 40, 40));
                    dc.DrawImage(icon, new Rect(x + 48, y + 20, 16, 16));
                    var text = new FormattedText(labels[i], CultureInfo.GetCultureInfo("ru-RU"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13, theme == 0 ? Brushes.Black : Brushes.WhiteSmoke, 1);
                    dc.DrawText(text, new Point(i % 7 * 140 + (140 - text.Width) / 2, y + 52));
                }
            }
        }
        var bitmap = new RenderTargetBitmap(980, 460, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(args[0]);
        encoder.Save(output);
    }
}
