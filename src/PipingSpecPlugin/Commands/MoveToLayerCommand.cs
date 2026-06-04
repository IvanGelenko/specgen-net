using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.DataLinks;
using PipingSpecPlugin.Layers;
using PipingSpecPlugin.Settings;
using PipingSpecPlugin.Spec;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// MOVEBYTAG — moves every tagged Plant 3D part in the active drawing
    /// (valves, equipment, fittings, instruments, supports, …) onto a layer
    /// named after its Plant 3D Tag (identifier), e.g. a valve tagged
    /// "gv-001" lands on layer "gv-001". Parts are enumerated by DataLinks
    /// rowId (SelectAcPpRowIds), so parts nested inside line-group blocks are
    /// included — a plain Model-Space walk missed them. Parts with no usable
    /// identifier are left untouched and reported. The whole pass runs in a
    /// single write transaction so one Undo reverts everything.
    ///
    /// This is the plug-in's only DWG-mutating command; everything else is
    /// read-only. Hence the explicit document lock and the defensive
    /// per-part try/catch — a single broken entity must not abort the pass.
    /// </summary>
    public class MoveToLayerCommand
    {
        [CommandMethod("MOVEBYTAG")]
        public void Run()
        {
            using var logger = new RunLogger("MOVEBYTAG");

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                WinForms.MessageBox.Show(
                    "Нет открытого чертежа. Откройте чертёж Plant 3D перед запуском.",
                    "Разнести по слоям",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            var ed = doc.Editor;
            void Say(string line)
            {
                logger.Log(line);
                try { ed.WriteMessage("\n" + line); } catch { }
            }
            void Trace(string line) => logger.Log(line);

            Say($"[Разнести по слоям] Запуск, лог: {logger.Path}");

            var db = doc.Database;
            DataLinksManager dlm;
            try { dlm = DataLinksManager.GetManager(db); }
            catch (System.Exception ex)
            {
                Say("[Разнести по слоям] Не удалось получить DataLinksManager: " + ex.Message);
                return;
            }
            if (dlm == null)
            {
                Say("[Разнести по слоям] Это не чертёж Plant 3D (нет DataLinks).");
                WinForms.MessageBox.Show(
                    "Активный чертёж не содержит данных Plant 3D.",
                    "Разнести по слоям",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            int moved = 0;
            int skippedNoTag = 0;
            int skippedError = 0;
            var layersTouched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // PHASE 1 — read the DataLinks layer (no DWG write). Enumerate
            // every Plant 3D part by rowId (includes valves/fittings nested in
            // line-group blocks that a plain Model-Space walk misses), read its
            // Tag, and resolve the AutoCAD entities to move. This phase
            // re-syncs and queries the DLM link map; doing it OUTSIDE the write
            // transaction is deliberate — re-syncing the link map (AuditDrawing
            // / ReloadLinkMap) while a write transaction is open corrupts the
            // map on repeated runs, which surfaced as growing "не найдено
            // сущностей" errors that a drawing reopen cleared. Build a plan of
            // (layerName → entityIds) here, then mutate the DWG in phase 2.
            var plan = new List<(string layerName, List<ObjectId> ents)>();

            // Save the drawing if it has unsaved edits, then re-sync DataLinks
            // (same as the spec generator) so an edited Tag is read fresh, not
            // at its pre-save value. Needs the doc locked (SaveAs writes DWG).
            using (doc.LockDocument())
                PartEntityIndex.SaveAndRefresh(doc, db, dlm, Trace);

            var rowIds = PartEntityIndex.CollectRowIds(db, dlm, Trace);
            Say($"[Разнести по слоям] Найдено деталей Plant 3D: {rowIds.Count}.");

            foreach (int rowId in rowIds)
            {
                var tag = PartEntityIndex.ReadProperty(dlm, rowId, "Tag");
                var layerName = TagLayerMover.SanitizeLayerName(tag);
                if (layerName == null)
                {
                    // No usable identifier → leave the part where it is.
                    skippedNoTag++;
                    continue;
                }

                var ents = PartEntityIndex.EntityIdsForRow(dlm, rowId, db)
                    .Where(id => !id.IsNull && !id.IsErased)
                    .ToList();
                if (ents.Count == 0)
                {
                    skippedError++;
                    Trace($"Строка {rowId} (Tag='{tag}'): не найдено сущностей в чертеже.");
                    continue;
                }
                plan.Add((layerName, ents));
            }

            // PHASE 2 — apply the plan in one locked write transaction so a
            // single Undo reverts everything. No DLM calls here.
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                    foreach (var (layerName, ents) in plan)
                    {
                        ObjectId layerId;
                        try
                        {
                            layerId = EnsureUsableLayer(tr, lt, db, layerName, Trace);
                            if (layerId.IsNull) { skippedError++; continue; }
                        }
                        catch (System.Exception ex)
                        {
                            skippedError++;
                            Trace($"Слой '{layerName}' не создан: {ex.GetType().Name}: {ex.Message}");
                            continue;
                        }

                        bool movedAny = false;
                        foreach (var entId in ents)
                        {
                            if (entId.IsErased) continue;
                            try
                            {
                                var ent = (Entity)tr.GetObject(entId, OpenMode.ForWrite);
                                ent.LayerId = layerId;
                                movedAny = true;
                            }
                            catch (System.Exception ex)
                            {
                                Trace($"Сущность не перенесена ({layerName}): "
                                    + $"{ex.GetType().Name}: {ex.Message}");
                            }
                        }

                        if (movedAny) { moved++; layersTouched.Add(layerName); }
                        else skippedError++;
                    }

                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    logger.Error("[Разнести по слоям] Ошибка прохода: " + ex.Message);
                    try { ed.WriteMessage("\n[Разнести по слоям] Ошибка прохода: " + ex.Message); } catch { }
                    Trace(ex.StackTrace ?? "");
                    try { tr.Abort(); } catch { }
                    return;
                }
            }

            if (skippedError > 0) logger.Warn($"Пропущено из-за ошибок: {skippedError} (см. строки выше).");
            var summary =
                $"Перенесено деталей: {moved} (слоёв: {layersTouched.Count}). "
              + $"Пропущено без идентификатора: {skippedNoTag}."
              + (skippedError > 0 ? $" Ошибок: {skippedError} (см. лог)." : "");
            Say("[Разнести по слоям] " + summary);
            WinForms.MessageBox.Show(summary, "Разнести по слоям",
                WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
        }

        /// <summary>
        /// Find or create the layer named <paramref name="layerName"/>. If it
        /// already exists but is frozen / off / locked, clear those flags so
        /// the moved part stays visible and editable — except the current
        /// layer, which AutoCAD forbids thawing. Returns ObjectId.Null only
        /// on an unexpected failure.
        /// </summary>
        static ObjectId EnsureUsableLayer(Transaction tr, LayerTable lt, Database db,
                                          string layerName, Action<string> trace)
        {
            if (lt.Has(layerName))
            {
                var id = lt[layerName];
                var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                bool needsThaw = ltr.IsFrozen && id != db.Clayer;
                if (ltr.IsOff || ltr.IsLocked || needsThaw)
                {
                    ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                    if (ltr.IsOff) ltr.IsOff = false;
                    if (ltr.IsLocked) ltr.IsLocked = false;
                    if (needsThaw) ltr.IsFrozen = false;
                    else if (ltr.IsFrozen)
                        trace?.Invoke($"Слой '{layerName}' — текущий и заморожен; "
                                    + "не размораживаю (запрет AutoCAD).");
                }
                return id;
            }

            // Create the layer. Default colour (white/7) and defaults for
            // everything else — the user can restyle later.
            lt.UpgradeOpen();
            var newLtr = new LayerTableRecord { Name = layerName };
            var newId = lt.Add(newLtr);
            tr.AddNewlyCreatedDBObject(newLtr, true);
            return newId;
        }
    }
}
