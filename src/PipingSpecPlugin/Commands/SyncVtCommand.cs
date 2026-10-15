using System.Linq;
using System.Windows.Interop;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PlantInstance;
using PipingSpecPlugin.Settings;
using PipingSpecPlugin.UI;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// Ribbon entry point for СИНХРОНИЗАЦИЯ ВТ: opens SyncVtWindow, which
    /// lets the user pick a Ведомость трубопроводов workbook, review the
    /// computed diff against the project's P3dLineGroup rows, and confirm
    /// before anything is written. All the matching/read/write logic
    /// lives in Spec/LineSyncService — this command only wires up the
    /// project, the log, and the window.
    /// </summary>
    public class SyncVtCommand
    {
        [CommandMethod("SYNCVT", CommandFlags.Session)]
        public void Run()
        {
            using var logger = new Settings.RunLogger("SYNCVT");
            void Say(string msg)
            {
                logger.Log(msg);
                try { AcadApp.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage("\n" + msg); } catch { }
            }
            void Trace(string msg) { try { logger.Log(msg); } catch { } }

            var project = PlantApplication.CurrentProject;
            if (project == null)
            {
                const string msg = "Не найден открытый проект AutoCAD Plant 3D. Откройте проект перед синхронизацией.";
                Say("[СинхронизацияВТ] " + msg);
                WinForms.MessageBox.Show(msg, "Синхронизация ВТ",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            // P3dLineGroup is owned by the Piping project part — same
            // lookup WriteSpecCommand.ScanSingleDrawing uses to get the
            // project-level DataLinksManager.
            Autodesk.ProcessPower.ProjectManager.Project piping = null;
            foreach (var pp in project.ProjectParts)
                if (pp is Autodesk.ProcessPower.P3dProjectParts.PipingProject) { piping = pp; break; }
            var projectDlm = piping?.DataLinksManager as DataLinksManager;
            if (projectDlm == null)
            {
                const string msg = "Не удалось получить доступ к данным трубопроводного проекта Plant 3D (PipingProject.DataLinksManager).";
                Say("[СинхронизацияВТ] " + msg);
                WinForms.MessageBox.Show(msg, "Синхронизация ВТ",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
                return;
            }

            var settings = PluginSettings.Load();
            if (settings.LoadFailed)
                Trace($"ВНИМАНИЕ: settings.json не прочитан ({settings.LoadError}); используются значения по умолчанию, "
                    + "файл настроек этой командой не перезаписывается.");

            var win = new SyncVtWindow(projectDlm, settings, Trace);
            new WindowInteropHelper(win).Owner = AcadApp.MainWindow.Handle;
            win.ShowDialog();

            // SyncVtWindow saves settings itself right after a successful
            // Apply (so LastVtFilePath / VtPicklistTextToCode persist even
            // if the user closes the window afterwards without further
            // action), but do it again here in case the window was closed
            // before Apply — cheap and harmless either way.
            // Never write back defaults that replaced an unreadable file:
            // SYNCVT only has the last ВТ path to persist, which is not worth
            // wiping the user's spec settings for.
            if (!settings.LoadFailed)
                try { settings.Save(); } catch (System.Exception ex) { Trace($"Не удалось сохранить настройки: {ex.Message}"); }

            Say(win.Applied
                ? "[СинхронизацияВТ] Синхронизация завершена."
                : "[СинхронизацияВТ] Нет подтверждённой успешной синхронизации.");
        }
    }
}
