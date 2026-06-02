using Autodesk.AutoCAD.Runtime;
using PipingSpecPlugin.UI;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// SPECABOUT — shows the "О программе" dialog with the plug-in name,
    /// version and a short feature list. Bound to the ribbon's
    /// "О программе" button.
    /// </summary>
    public class AboutCommand
    {
        [CommandMethod("SPECABOUT")]
        public void Run()
        {
            var win = new AboutWindow();
            new System.Windows.Interop.WindowInteropHelper(win).Owner =
                AcadApp.MainWindow.Handle;
            win.ShowDialog();
        }
    }
}
