using System.IO;
using Autodesk.AutoCAD.Runtime;
using PipingSpecPlugin.Settings;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// Lets the user pick or clear the spec template (.docx) once,
    /// persisting the choice in PluginSettings so WRITESPEC doesn't
    /// prompt every run. Type SPECTEMPLATE again to change templates.
    /// </summary>
    public class SpecTemplateCommand
    {
        [CommandMethod("SPECTEMPLATE")]
        public void Run()
        {
            var editor = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            var settings = PluginSettings.Load();

            if (!string.IsNullOrEmpty(settings.TemplatePath))
                editor?.WriteMessage($"\n[SPECTEMPLATE] Current: {settings.TemplatePath}");

            using var dlg = new WinForms.OpenFileDialog
            {
                Title = "Select spec template (.docx)",
                Filter = "Word template (*.docx)|*.docx",
                CheckFileExists = true,
                FileName = settings.TemplatePath ?? "",
            };
            if (dlg.ShowDialog() != WinForms.DialogResult.OK)
            {
                editor?.WriteMessage("\n[SPECTEMPLATE] Unchanged.");
                return;
            }

            settings.TemplatePath = dlg.FileName;
            settings.Save();
            editor?.WriteMessage($"\n[SPECTEMPLATE] Saved: {dlg.FileName}");
        }
    }
}
