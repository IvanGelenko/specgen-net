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
    /// Команда НИЧЕГО не меняет в модели — только выделяет. Перебор идёт по
    /// rowId через <see cref="PartEntityIndex"/>, поэтому детали, вложенные в
    /// блоки линий (арматура, фитинги), тоже попадают в выборку.
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

            // Save the drawing if it has unsaved edits, then re-sync DataLinks
            // — the same thing the spec generator does. Plant 3D only writes an
            // edited property (e.g. a part's Status) into the DataLinks store on
            // SAVE, so without this the status list reflects the pre-edit state.
            // Needs the document locked (SaveAs writes the DWG).
            using (doc.LockDocument())
                PartEntityIndex.SaveAndRefresh(doc, db, dlm, Trace);

            // Group every part's entities by its Status. The empty/missing
            // status collapses into one "Без статуса" bucket so the user can
            // still grab those parts.
            var byStatus = new Dictionary<string, List<ObjectId>>(StringComparer.Ordinal);

            var rowIds = PartEntityIndex.CollectRowIds(db, dlm, Trace);
            Say($"[{Title}] Найдено деталей Plant 3D: {rowIds.Count}.");

            foreach (int rowId in rowIds)
            {
                var status = PartEntityIndex.ReadProperty(dlm, rowId, "Status");
                var key = string.IsNullOrWhiteSpace(status)
                    ? SpecConstants.NullStatus
                    : status.Trim();

                if (!byStatus.TryGetValue(key, out var list))
                {
                    list = new List<ObjectId>();
                    byStatus[key] = list;
                }
                foreach (var entId in PartEntityIndex.EntityIdsForRow(dlm, rowId, db))
                {
                    if (!entId.IsNull && !entId.IsErased)
                        list.Add(entId);
                }
            }

            if (byStatus.Count == 0)
            {
                Say($"[{Title}] В чертеже не найдено деталей Plant 3D.");
                WinForms.MessageBox.Show(
                    "В чертеже не найдено деталей Plant 3D.",
                    Title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }

            // Build the picker list: real statuses first (alpha), "Без
            // статуса" last. The display label carries the part count.
            var options = byStatus
                .OrderBy(kv => kv.Key == SpecConstants.NullStatus ? 1 : 0)
                .ThenBy(kv => kv.Key, StringComparer.CurrentCulture)
                .Select(kv => new StatusOption(
                    kv.Key,
                    kv.Key == SpecConstants.NullStatus ? SpecConstants.NullStatusLabel : kv.Key,
                    kv.Value.Count))
                .ToList();

            var chosen = ShowPicker(options);
            if (chosen == null)
            {
                Say($"[{Title}] Отменено пользователем.");
                return;
            }

            var ids = byStatus[chosen.Key]
                .Where(id => !id.IsNull && !id.IsErased)
                .Distinct()
                .ToArray();

            if (ids.Length == 0)
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
                ed.SetImpliedSelection(ids);
                Say($"[{Title}] Выбрано деталей: {ids.Length} (статус «{chosen.Label}»).");
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
