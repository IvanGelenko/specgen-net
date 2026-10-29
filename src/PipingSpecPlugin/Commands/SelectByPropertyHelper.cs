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

            // 1. Make sure the disk copy is current (shared policy: ask before
            //    saving, original DWG version, stop if the save fails), then
            //    re-sync the live DataLinks caches.
            if (!DrawingSaveGuard.EnsureSavedForDiskRead(doc, db, title, Trace))
            {
                Say($"[{title}] Команда остановлена: чертёж не сохранён (см. лог).");
                return;
            }
            var dwgPath = doc.Name;
            using (doc.LockDocument())
                PartEntityIndex.RefreshLinkMap(db, dlm, Trace);

            // 2. Read the saved DWG fresh from disk as a side database — the
            //    proven, edit-aware path. Active drawing only (projectDlm null).
            //    The live in-memory DLM keeps a stale value cache, so the
            //    grouping (which line numbers / statuses exist) MUST come from
            //    disk. While the side DB is still open we also resolve each
            //    part's entity HANDLES from it: handles are stable across
            //    databases, so they bridge the disk read to the live drawing.
            //    Matching by PnPID instead silently dropped parts whenever an
            //    edit shifted the in-memory row ids out of step with disk —
            //    that is the "часто не подхватывается если изменено" report.
            // Grouped by ENTITY (EntityValueGroups): a weld's P3dConnector row
            // has no Status — its Buttweld / Gasket row does.
            var rows = new List<(string Value, IReadOnlyCollection<long> Handles)>();
            try
            {
                using var diskDb = new Database(false, true);
                diskDb.ReadDwgFile(dwgPath, System.IO.FileShare.ReadWrite, true, "");
                diskDb.CloseInput(true);

                var diskDlm = DataLinksManager.GetManager(diskDb);
                var read = PartReader.Read(diskDb, null, Trace);
                var records = read?.Records ?? new List<PartRecord>();
                Say($"[{title}] Прочитано деталей: {records.Count}.");
                if (read != null && read.PnpReconnectFailed)
                    Say($"[{title}] ВНИМАНИЕ: база PnP не переподключилась после Disconnect() — данные могут быть недостоверны, перезапустите AutoCAD.");

                // GetRelatedRowIds / AcPp resolution keys off the working DB —
                // point it at the side DB while we resolve handles from it.
                var prevWorking = HostApplicationServices.WorkingDatabase;
                bool swapped = false;
                try
                {
                    try { HostApplicationServices.WorkingDatabase = diskDb; swapped = true; } catch { }

                    foreach (var rec in records)
                    {
                        if (rec == null) continue;
                        var handles = new List<long>();
                        if (diskDlm != null)
                            foreach (var h in PartEntityIndex.EntityHandlesForRow(diskDlm, rec.PnPID, diskDb))
                                handles.Add(h.Value);
                        rows.Add((valueOf(rec), handles));
                    }
                }
                finally
                {
                    if (swapped)
                        try { HostApplicationServices.WorkingDatabase = prevWorking; } catch { }
                }
            }
            catch (System.Exception ex)
            {
                Say($"[{title}] Не удалось прочитать чертёж с диска: {ex.GetType().Name}: {ex.Message}");
                WinForms.MessageBox.Show(
                    "Не удалось прочитать данные чертежа. Подробности в логе:\n" + logger.Path,
                    title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
                return;
            }

            var groups = EntityValueGroups.Build(rows, nullKey);
            if (groups.Count == 0)
            {
                Say($"[{title}] В чертеже не найдено деталей Plant 3D.");
                WinForms.MessageBox.Show(
                    "В чертеже не найдено деталей Plant 3D.",
                    title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }

            // Real values first (natural-ish order), null bucket last.
            var options = groups
                .OrderBy(kv => kv.Key == nullKey ? 1 : 0)
                .ThenBy(kv => kv.Key, StringComparer.CurrentCulture)
                .Select(kv => new Option(
                    kv.Key,
                    kv.Key == nullKey ? nullLabel : kv.Key,
                    kv.Value.Count))
                .ToList();

            var chosen = ShowPicker(title, promptText, options);
            if (chosen == null)
            {
                Say($"[{title}] Отменено пользователем.");
                return;
            }

            // Resolve the chosen value's entity HANDLES (captured from the disk
            // read) against the ACTIVE drawing. Handles are stable across
            // databases, so a part edited since the last reopen still resolves
            // — unlike the old PnPID round-trip through the live DLM, which
            // dropped parts whose in-memory row ids had drifted from disk.
            var ids = new List<ObjectId>();
            var seen = new HashSet<ObjectId>();
            foreach (var handle in groups[chosen.Key].Handles.Select(v => new Handle(v)))
            {
                ObjectId entId;
                try { entId = db.GetObjectId(false, handle, 0); }
                catch (System.Exception ex)
                {
                    Trace($"Handle {handle} не найден в активном чертеже: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
                if (entId.IsNull || entId.IsErased) continue;
                if (seen.Add(entId)) ids.Add(entId);
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
