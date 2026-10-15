using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// One save policy for every command that reads the ACTIVE drawing back
    /// from disk (MOVEBYTAG, SELECTBYSTATUS, SELECTBYLINE, PSAUDIT). Those commands
    /// must read a side database — the live DLM cache is stale even after a
    /// save — so the disk copy has to be current.
    ///
    /// They used to save silently on their own, each with its own copy of
    /// the code: <c>db.SaveAs(…, DwgVersion.Current, …)</c> could UPGRADE an
    /// older-format DWG behind the user's back, the Save() fallback picked
    /// its own format, and a failed save was only traced — the command then
    /// ran on the stale disk copy and reported success. WRITESPEC chose the
    /// opposite extreme (never touch the user's drawings; refuse and ask the
    /// user to save). The shared policy here sits in between and is always
    /// visible to the user:
    ///   • drawing never saved to disk → stop with "save it first";
    ///   • no unsaved changes → continue, nothing is written;
    ///   • unsaved changes → ASK; "No" cancels the command;
    ///   • "Yes" → save in the drawing's ORIGINAL DWG version
    ///     (<see cref="Database.OriginalFileVersion"/>), never upgraded;
    ///   • save fails → explicit message, command stops — it never
    ///     continues on stale data.
    /// </summary>
    static class DrawingSaveGuard
    {
        /// <returns>true when the disk copy of the active drawing is current
        /// and the command may read it.</returns>
        public static bool EnsureSavedForDiskRead(Document doc, Database db, string title,
                                                  Action<string> trace)
        {
            if (doc == null || db == null) return false;
            var path = doc.Name;
            var name = string.IsNullOrEmpty(path) ? "?" : Path.GetFileName(path);

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                trace?.Invoke($"[{title}] Чертёж не сохранён на диск ({path}) — команда остановлена.");
                WinForms.MessageBox.Show(
                    "Чертёж ещё не сохранён на диск. Сохраните его и запустите команду снова.",
                    title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return false;
            }

            if (!IsDirty(doc, trace)) return true;

            var answer = WinForms.MessageBox.Show(
                $"Чертёж {name} содержит несохранённые изменения.\n\n"
              + "Команда читает данные Plant 3D с диска, поэтому изменения нужно сначала сохранить.\n\n"
              + "Сохранить чертёж сейчас (в исходном формате DWG) и продолжить?",
                title, WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Question);
            if (answer != WinForms.DialogResult.Yes)
            {
                trace?.Invoke($"[{title}] Пользователь отказался сохранять чертёж — команда отменена.");
                return false;
            }

            try
            {
                // OriginalFileVersion, NOT DwgVersion.Current: saving must not
                // silently upgrade a drawing that colleagues open in an older
                // release. bBakAndRename=true keeps the usual .bak.
                using (doc.LockDocument())
                    db.SaveAs(path, true, db.OriginalFileVersion, db.SecurityParameters);
                trace?.Invoke($"[{title}] Чертёж сохранён ({db.OriginalFileVersion}): {path}");
                return true;
            }
            catch (System.Exception ex)
            {
                trace?.Invoke($"[{title}] Сохранение не удалось: {ex.GetType().Name}: {ex.Message}");
                WinForms.MessageBox.Show(
                    $"Не удалось сохранить чертёж {name}:\n{ex.Message}\n\n"
                  + "Команда остановлена, чтобы не работать с устаревшими данными. "
                  + "Сохраните чертёж вручную (Ctrl+S) и повторите.",
                    title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return false;
            }
        }

        /// <summary>
        /// Unsaved changes? Same probe WRITESPEC uses (COM Document.Saved),
        /// falling back to DBMOD (valid here: these commands always act on the
        /// ACTIVE document). If neither answers, assume dirty — asking once
        /// too often is harmless, reading a stale copy is not.
        /// </summary>
        internal static bool IsDirty(Document doc, Action<string> trace)
        {
            try
            {
                dynamic com = doc.GetAcadDocument();
                return !(bool)com.Saved;
            }
            catch (System.Exception ex)
            {
                trace?.Invoke($"Document.Saved не прочитан ({ex.GetType().Name}: {ex.Message}); проверяю DBMOD.");
            }
            try { return Convert.ToInt32(AcadApp.GetSystemVariable("DBMOD")) != 0; }
            catch { return true; }
        }
    }
}
