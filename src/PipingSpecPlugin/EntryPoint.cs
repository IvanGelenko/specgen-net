using Autodesk.AutoCAD.Runtime;
// Both AutoCAD and WinForms expose a type called `Application`; alias to keep
// the call sites unambiguous and the diff small if we ever drop WinForms.
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace PipingSpecPlugin
{
    /// <summary>
    /// Bootstrap entry point — registers the user-facing AutoCAD commands.
    /// At this stage we only expose <c>PSPING</c> to prove the plug-in loads.
    /// Real functionality (GENSPEC) lands in the next iteration once the
    /// command pipeline is verified end-to-end on a live Plant 3D install.
    /// </summary>
    public class EntryPoint
    {
        [CommandMethod("PSPING")]
        public void Ping()
        {
            var version = System.Reflection.Assembly
                .GetExecutingAssembly().GetName().Version.ToString();
            var msg = "PipingSpecPlugin alive (.NET build, v" + version + ")";

            var editor = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            editor?.WriteMessage("\n" + msg);

            // The message box is the visible heartbeat: if it pops, the
            // command dispatcher reached our code and AcMgd resolved correctly.
            WinForms.MessageBox.Show(msg, "PipingSpecPlugin",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Information);
        }
    }
}
