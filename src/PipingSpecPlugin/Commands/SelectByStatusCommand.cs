using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.DataLinks;
using PipingSpecPlugin.Settings;
using PipingSpecPlugin.Spec;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// SELECTBYSTATUS — выбирает на чертеже все детали Plant 3D с заданным
    /// статусом строительства (например, «Существующее», «Новое», «Под снос»).
    /// Команда показывает список статусов, реально встречающихся в чертеже, с
    /// количеством деталей; после выбора формирует штатное выделение AutoCAD
    /// (pickfirst), как будто пользователь выделил детали рамкой.
    ///
    /// Статусы читаются тем же путём, что и генерация спецификации: чертёж
    /// сохраняется, затем читается заново С ДИСКА (PartReader.Read) — кэш DLM
    /// в памяти остаётся устаревшим даже после сохранения, поэтому чтение из
    /// активной БД не отражает правки. Это и есть причина «не подхватывает
    /// изменения»; теперь команда видит правки динамически, как спецификация.
    ///
    /// Команда НИЧЕГО не меняет в модели (кроме автосохранения чертежа) —
    /// только выделяет. Детали, вложенные в блоки линий (арматура, фитинги),
    /// тоже попадают в выборку.
    /// </summary>
    public class SelectByStatusCommand
    {
        const string Title = "Выбрать по статусу";

        [CommandMethod("SELECTBYSTATUS")]
        public void Run()
        {
            using var logger = new RunLogger("SELECTBYSTATUS");

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                WinForms.MessageBox.Show(
                    "Нет открытого чертежа. Откройте чертёж Plant 3D перед запуском.",
                    Title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            var ed = doc.Editor;
            void Say(string line)
            {
                logger.Log(line);
                try { ed.WriteMessage("\n" + line); } catch { }
            }
            void Trace(string line) => logger.Log(line);

            Say($"[{Title}] Запуск, лог: {logger.Path}");

            var db = doc.Database;
            DataLinksManager dlm;
            try { dlm = DataLinksManager.GetManager(db); }
            catch (System.Exception ex)
            {
                Say($"[{Title}] Не удалось получить DataLinksManager: " + ex.Message);
                return;
            }
            if (dlm == null)
            {
                Say($"[{Title}] Это не чертёж Plant 3D (нет DataLinks).");
                WinForms.MessageBox.Show(
                    "Активный чертёж не содержит данных Plant 3D.",
                    Title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            // ---- Read statuses the SAME way the spec generator does --------
            // The spec picks up live edits because WriteSpecCommand (1) saves
            // the active drawing, then (2) reads it FRESH FROM DISK as a side
            // database — the in-memory DLM cache stays stale even after a save,
            // so reading activeDoc.Database directly never reflects edits. We
            // reuse exactly that path: save → PartReader.Read(diskDb). The
            // returned PartRecord carries Status and PnPID (the drawing-local
            // row id), which is identical between the saved disk copy and the
            // in-memory active drawing — so we group statuses from the fresh
            // records, then resolve PnPID → entity in the ACTIVE db to select.

            if (string.IsNullOrEmpty(doc.Name))
            {
                Say($"[{Title}] Чертёж не сохранён на диск — сохраните его и повторите.");
                WinForms.MessageBox.Show(
                    "Чертёж ещё не сохранён на диск. Сохраните его и запустите команду снова.",
                    Title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }
            var dwgPath = doc.Name;

            // 1. Save the active drawing if it has unsaved edits, so disk holds
            //    the current state. Needs the document locked (writes the DWG).
            using (doc.LockDocument())
                PartEntityIndex.SaveAndRefresh(doc, db, dlm, Trace);

            // 2. Read the saved DWG fresh from disk as a side database — the
            //    proven, edit-aware path (mirrors WriteSpecCommand.ReadOneDrawing).
            //    Scope is strictly the ACTIVE drawing: we load only its own file
            //    and pass projectDlm = null, so PartReader enumerates only this
            //    drawing's rows (SelectAcPpRowIds(diskDb)) — never project-wide.
            PartReadResult read;
            try
            {
                using var diskDb = new Database(false, true);
                diskDb.ReadDwgFile(dwgPath, System.IO.FileShare.ReadWrite, true, "");
                diskDb.CloseInput(true);
                read = PartReader.Read(diskDb, null, Trace);
            }
            catch (System.Exception ex)
            {
                Say($"[{Title}] Не удалось прочитать чертёж с диска: {ex.GetType().Name}: {ex.Message}");
                WinForms.MessageBox.Show(
                    "Не удалось прочитать данные чертежа. Подробности в логе:\n" + logger.Path,
                    Title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
                return;
            }

            var records = read?.Records ?? new List<PartRecord>();
            Say($"[{Title}] Прочитано деталей: {records.Count}.");

            // Group PnPIDs (row ids) by Status. Empty/missing status →
            // "Без статуса" bucket.
            var idsByStatus = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var countByStatus = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var rec in records)
            {
                if (rec == null) continue;
                var key = string.IsNullOrWhiteSpace(rec.Status)
                    ? SpecConstants.NullStatus
                    : rec.Status.Trim();
                if (!idsByStatus.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    idsByStatus[key] = list;
                }
                list.Add(rec.PnPID);
                countByStatus[key] = (countByStatus.TryGetValue(key, out var c) ? c : 0) + 1;
            }

            if (idsByStatus.Count == 0)
            {
                Say($"[{Title}] В чертеже не найдено деталей Plant 3D.");
                WinForms.MessageBox.Show(
                    "В чертеже не найдено деталей Plant 3D.",
                    Title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }

            // Build the picker list: real statuses first (alpha), "Без
            // статуса" last. The display label carries the part count.
            var options = idsByStatus
                .OrderBy(kv => kv.Key == SpecConstants.NullStatus ? 1 : 0)
                .ThenBy(kv => kv.Key, StringComparer.CurrentCulture)
                .Select(kv => new StatusOption(
                    kv.Key,
                    kv.Key == SpecConstants.NullStatus ? SpecConstants.NullStatusLabel : kv.Key,
                    countByStatus[kv.Key]))
                .ToList();

            var chosen = ShowPicker(options);
            if (chosen == null)
            {
                Say($"[{Title}] Отменено пользователем.");
                return;
            }

            // Resolve the chosen status's row ids → entities in the ACTIVE
            // drawing (PnPID is drawing-local and identical to the disk copy we
            // just read, since we saved first). Re-sync the active db's link
            // map so the row→entity resolution is current.
            PartEntityIndex.RefreshLinkMap(db, dlm, Trace);

            var ids = new List<ObjectId>();
            var seen = new HashSet<ObjectId>();
            foreach (int rowId in idsByStatus[chosen.Key])
            {
                foreach (var entId in PartEntityIndex.EntityIdsForRow(dlm, rowId, db))
                {
                    if (entId.IsNull || entId.IsErased) continue;
                    if (seen.Add(entId)) ids.Add(entId);
                }
            }

            if (ids.Count == 0)
            {
                Say($"[{Title}] Для статуса «{chosen.Label}» нет видимых деталей в чертеже.");
                WinForms.MessageBox.Show(
                    $"Для статуса «{chosen.Label}» не найдено деталей в чертеже.",
                    Title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }

            try
            {
                // SetImpliedSelection is the "pickfirst" grip selection — the
                // same state as if the user had window-selected the parts. The
                // user can then move/erase/edit them with any AutoCAD command.
                ed.SetImpliedSelection(ids.ToArray());
                Say($"[{Title}] Выбрано деталей: {ids.Count} (статус «{chosen.Label}»).");
            }
            catch (System.Exception ex)
            {
                Say($"[{Title}] Не удалось выделить детали: {ex.GetType().Name}: {ex.Message}");
                WinForms.MessageBox.Show(
                    "Не удалось сформировать выделение. Подробности в логе.",
                    Title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            }
        }

        sealed class StatusOption
        {
            public string Key { get; }
            public string Label { get; }
            public int Count { get; }
            public StatusOption(string key, string label, int count)
            {
                Key = key; Label = label; Count = count;
            }
            public override string ToString() => $"{Label}  ({Count})";
        }

        /// <summary>
        /// Small modal list dialog. Returns the chosen status, or null on
        /// cancel. WinForms (not WPF) keeps it dependency-free and handles
        /// Cyrillic status text without extra setup.
        /// </summary>
        static StatusOption ShowPicker(IList<StatusOption> options)
        {
            using var form = new WinForms.Form
            {
                Text = Title,
                FormBorderStyle = WinForms.FormBorderStyle.FixedDialog,
                StartPosition = WinForms.FormStartPosition.CenterScreen,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new System.Drawing.Size(340, 300),
            };

            var label = new WinForms.Label
            {
                Text = "Выберите статус — будут выделены все детали с ним:",
                Dock = WinForms.DockStyle.Top,
                Height = 40,
                Padding = new WinForms.Padding(8, 8, 8, 0),
            };

            var list = new WinForms.ListBox
            {
                Dock = WinForms.DockStyle.Fill,
                IntegralHeight = false,
            };
            foreach (var o in options) list.Items.Add(o);
            if (list.Items.Count > 0) list.SelectedIndex = 0;

            var ok = new WinForms.Button
            {
                Text = "Выбрать",
                DialogResult = WinForms.DialogResult.OK,
                Dock = WinForms.DockStyle.Right,
                Width = 100,
            };
            var cancel = new WinForms.Button
            {
                Text = "Отмена",
                DialogResult = WinForms.DialogResult.Cancel,
                Dock = WinForms.DockStyle.Right,
                Width = 100,
            };
            var buttons = new WinForms.Panel { Dock = WinForms.DockStyle.Bottom, Height = 40 };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);

            // Double-click a row = pick it.
            list.DoubleClick += (s, e) =>
            {
                if (list.SelectedItem != null)
                {
                    form.DialogResult = WinForms.DialogResult.OK;
                    form.Close();
                }
            };

            form.Controls.Add(list);
            form.Controls.Add(buttons);
            form.Controls.Add(label);
            form.AcceptButton = ok;
            form.CancelButton = cancel;

            var result = form.ShowDialog();
            if (result != WinForms.DialogResult.OK) return null;
            return list.SelectedItem as StatusOption;
        }
    }
}
