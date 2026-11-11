using System.Windows;using System.Windows.Media;using System.Windows.Media.Imaging;
internal static class Program
{
 [STAThread]static void Main(string[] args)
 {
  var app=new Application();var window=new PipingSpecPlugin.UI.AboutWindow();window.Left=-30000;window.Top=-30000;window.Show();window.UpdateLayout();
  var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=System.IO.File.Create(args[0]);encoder.Save(stream);window.Close();
 }
}
namespace PipingSpecPlugin.UI{public sealed class UpdateWindow:Window{public UpdateWindow(PipingSpecPlugin.Update.UpdateInfo info){}}}
namespace Autodesk.AutoCAD.ApplicationServices{public static class Application{public static MainWindowHandle MainWindow=>new();}public sealed class MainWindowHandle{public IntPtr Handle=>IntPtr.Zero;}}
