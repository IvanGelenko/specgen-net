using System;
using System.Collections.Generic;
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

            // PHASE 1 — build the plan from a FRESH copy of the drawing read
            // off disk, NOT from the live in-memory DataLinksManager. The live
            // DLM keeps a stale property cache: after the user edits a part's
            // Tag in the Properties palette, GetAllProperties on the live db
            // keeps returning the OLD tag even after save + every documented
            // cache-flush — so MOVEBYTAG used to move parts onto the layer of
            // their FIRST tag and never pick the new one up. The spec generator
            // never hit this because it reads a just-saved side database
            // (ReadOneDrawing); we now do the same. The plan carries entity
            // Handles (stable across save/reopen and across databases), which
            // phase 2 resolves against the live drawing to actually move them.

            // The disk copy must carry the current tags. Shared policy with
            // SELECTBY*: ask before saving, keep the original DWG version,
            // stop (never continue on stale data) if the save fails.
            if (!DrawingSaveGuard.EnsureSavedForDiskRead(doc, db, "Разнести по слоям", Trace))
            {
                Say("[Разнести по слоям] Команда остановлена: чертёж не сохранён (см. лог).");
                return;
            }
            string dwgPath = doc.Name;

            int partCount;
            var plan = PartEntityIndex.BuildMovePlanFromDisk(
                dwgPath, Trace,
                out int planSkippedNoTag, out int planSkippedNoEntity, out partCount);
            skippedNoTag += planSkippedNoTag;
            skippedError += planSkippedNoEntity;
            Say($"[Разнести по слоям] Найдено деталей Plant 3D: {partCount}.");

            // PHASE 2 — apply the plan in one locked write transaction so a
            // single Undo reverts everything. Resolve each Handle against the
            // LIVE drawing; the plan came from a side DB, so its ObjectIds are
            // meaningless here — handles are the bridge.
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                    foreach (var entry in plan)
                    {
                        string layerName = entry.LayerName;
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
                        foreach (var handle in entry.Handles)
                        {
                            ObjectId entId;
                            try { entId = db.GetObjectId(false, handle, 0); }
                            catch (System.Exception ex)
                            {
                                Trace($"Handle {handle} не найден в чертеже ({layerName}): "
                                    + $"{ex.GetType().Name}: {ex.Message}");
                                continue;
                            }
                            if (entId.IsNull || entId.IsErased) continue;
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
