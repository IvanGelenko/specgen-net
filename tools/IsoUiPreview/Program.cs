using PipingSpecPlugin.UI;
using System.Drawing;
using System.Windows.Forms;
internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var window=new IsometricDialog(new[]{new IsometricLineChoice("050-N001-LS016B-NN","50",18),new IsometricLineChoice("150-FL007-LS016B-HN","150",59),new IsometricLineChoice("сущ-__-__C-H-test","150, 25",59)},@"C:\project for test\temp.dwg",@"C:\project for test\Изометрии",@"D:\Мои шаблоны\Изометрия А2.dwg",7.5);
        window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-32000,-32000);window.Show();Application.DoEvents();window.PerformLayout();
        if(window.SymbolScale!=7.5||window.TemplatePath!=@"D:\Мои шаблоны\Изометрия А2.dwg")throw new Exception("Template or scale not retained");
        if(window.Controls.OfType<Label>().Any(c=>c.Text.Contains("Стиль проекта")))throw new Exception("Legacy style selector still visible");
        using var bitmap=new Bitmap(window.Width,window.Height);window.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(args[0]);
    }
}
