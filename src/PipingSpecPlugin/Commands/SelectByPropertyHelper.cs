using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.ProcessPower.DataLinks;
using PipingSpecPlugin.Settings;
using PipingSpecPlugin.Spec;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// Shared engine behind the "select all parts that share a value" commands
    /// (SELECTBYSTATUS, SELECTBYLINE, …). Each command only supplies which
    /// PartRecord field to group by and the UI strings; the edit-aware read,
    /// grouping, picker dialog and selection live here once.
    ///
    /// Why it reads the way it does: the spec generator picks up live edits
    /// because it (1) saves the active drawing, then (2) reads it FRESH FROM
    /// DISK as a side database — the in-memory DLM cache stays stale even after
    /// a save. We reuse exactly that path so these commands reflect drawing
    /// edits without a reopen. Scope is strictly the ACTIVE drawing: only its
    /// own file is loaded and projectDlm = null, so PartReader enumerates only
    /// this drawing's rows; PnPID (drawing-local row id) is identical between
    /// the saved disk copy and the in-memory active drawing, so we resolve
    /// PnPID → entity in the ACTIVE db to build the selection.
    /// </summary>
    static class SelectByPropertyHelper
    {
        /// <param name="title">Window / log caption.</param>
        /// <param name="logName">RunLogger command name.</param>
        /// <param name="valueOf">Extracts the field to group by from a record.</param>
        /// <param name="nullKey">Sentinel for empty/missing value.</param>
        /// <param name="nullLabel">Label shown for the empty bucket.</param>
        /// <param name="promptText">Instruction line in the picker dialog.</param>
        public static void Run(
            string title, string logName,
            Func<PartRecord, string> valueOf,
            string nullKey, string nullLabel, string promptText)
        {
            using var logger = new RunLogger(logName);

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                WinForms.MessageBox.Show(
                    "Нет открытого чертежа. Откройте чертёж Plant 3D перед запуском.",
                    title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            var ed = doc.Editor;
            void Say(string line)
            {
                logger.Log(line);
                try { ed.WriteMessage("\n" + line); } catch { }
            }
            void Trace(string line) => logger.Log(line);

            Say($"[{title}] Запуск, лог: {logger.Path}");

            var db = doc.Database;
            DataLinksManager dlm;
            try { dlm = DataLinksManager.GetManager(db); }
            catch (System.Exception ex)
            {
                Say($"[{title}] Не удалось получить DataLinksManager: " + ex.Message);
                return;
            }
            if (dlm == null)
            {
                Say($"[{title}] Это не чертёж Plant 3D (нет DataLinks).");
                WinForms.MessageBox.Show(
                    "Активный чертёж не содержит данных Plant 3D.",
                    title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(doc.Name))
            {
                Say($"[{title}] Чертёж не сохранён на диск — сохраните его и повторите.");
                WinForms.MessageBox.Show(
                    "Чертёж ещё не сохранён на диск. Сохраните его и запустите команду снова.",
                    title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }
            var dwgPath = doc.Name;

            // 1. Save the active drawing if it has unsaved edits (writes the
            //    DWG, so needs a document lock), then re-sync DataLinks.
            using (doc.LockDocument())
                PartEntityIndex.SaveAndRefresh(doc, db, dlm, Trace);

            // 2. Read the saved DWG fresh from disk as a side database — the
            //    proven, edit-aware path. Active drawing only (projectDlm null).
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
                Say($"[{title}] Не удалось прочитать чертёж с диска: {ex.GetType().Name}: {ex.Message}");
                WinForms.MessageBox.Show(
                    "Не удалось прочитать данные чертежа. Подробности в логе:\n" + logger.Path,
                    title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
                return;
            }

            var records = read?.Records ?? new List<PartRecord>();
            Say($"[{title}] Прочитано деталей: {records.Count}.");

            // Group PnPIDs by the chosen value. Empty/missing → null bucket.
            var idsByKey = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var countByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var rec in records)
            {
                if (rec == null) continue;
                var raw = valueOf(rec);
                var key = string.IsNullOrWhiteSpace(raw) ? nullKey : raw.Trim();
                if (!idsByKey.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    idsByKey[key] = list;
                }
                list.Add(rec.PnPID);
                countByKey[key] = (countByKey.TryGetValue(key, out var c) ? c : 0) + 1;
            }

            if (idsByKey.Count == 0)
            {
                Say($"[{title}] В чертеже не найдено деталей Plant 3D.");
                WinForms.MessageBox.Show(
                    "В чертеже не найдено деталей Plant 3D.",
                    title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }

            // Real values first (natural-ish order), null bucket last.
            var options = idsByKey
                .OrderBy(kv => kv.Key == nullKey ? 1 : 0)
                .ThenBy(kv => kv.Key, StringComparer.CurrentCulture)
                .Select(kv => new Option(
                    kv.Key,
                    kv.Key == nullKey ? nullLabel : kv.Key,
                    countByKey[kv.Key]))
                .ToList();

            var chosen = ShowPicker(title, promptText, options);
            if (chosen == null)
            {
                Say($"[{title}] Отменено пользователем.");
                return;
            }

            // Resolve the chosen value's row ids → entities in the ACTIVE
            // drawing. Re-sync the active db's link map first so row→entity is
            // current.
            PartEntityIndex.RefreshLinkMap(db, dlm, Trace);

            var ids = new List<ObjectId>();
            var seen = new HashSet<ObjectId>();
            foreach (int rowId in idsByKey[chosen.Key])
            {
                foreach (var entId in PartEntityIndex.EntityIdsForRow(dlm, rowId, db))
                {
                    if (entId.IsNull || entId.IsErased) continue;
                    if (seen.Add(entId)) ids.Add(entId);
                }
            }

            if (ids.Count == 0)
            {
                Say($"[{title}] Для «{chosen.Label}» нет видимых деталей в чертеже.");
                WinForms.MessageBox.Show(
                    $"Для «{chosen.Label}» не найдено деталей в чертеже.",
                    title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }

            try
            {
                ed.SetImpliedSelection(ids.ToArray());
                Say($"[{title}] Выбрано деталей: {ids.Count} («{chosen.Label}»).");
            }
            catch (System.Exception ex)
            {
                Say($"[{title}] Не удалось выделить детали: {ex.GetType().Name}: {ex.Message}");
                WinForms.MessageBox.Show(
                    "Не удалось сформировать выделение. Подробности в логе.",
                    title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            }
        }

        sealed class Option
        {
            public string Key { get; }
            public string Label { get; }
            public int Count { get; }
            public Option(string key, string label, int count)
            {
                Key = key; Label = label; Count = count;
            }
            public override string ToString() => $"{Label}  ({Count})";
        }

        /// <summary>
        /// Modal list dialog. Returns the chosen option, or null on cancel.
        /// WinForms keeps it dependency-free and handles Cyrillic text.
        /// </summary>
        static Option ShowPicker(string title, string promptText, IList<Option> options)
        {
            using var form = new WinForms.Form
            {
                Text = title,
                FormBorderStyle = WinForms.FormBorderStyle.FixedDialog,
                StartPosition = WinForms.FormStartPosition.CenterScreen,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new System.Drawing.Size(360, 320),
            };

            var label = new WinForms.Label
            {
                Text = promptText,
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
            return list.SelectedItem as Option;
        }
    }
}
