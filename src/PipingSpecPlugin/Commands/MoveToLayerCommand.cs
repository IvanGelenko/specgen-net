using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.DataObjects;
using PipingSpecPlugin.Layers;
using PipingSpecPlugin.Settings;
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

            // Lock the document — writing entities from a CommandMethod
            // requires it, otherwise eLockViolation. One transaction for the
            // whole pass keeps the move atomic under a single Undo.
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                    // Enumerate EVERY Plant 3D part by its DataLinks rowId —
                    // the same query PartReader uses. Walking Model Space
                    // alone (the old approach) only saw top-level entities and
                    // silently missed valves/fittings nested inside line-group
                    // blocks, so tagged armature never moved. SelectAcPpRowIds
                    // returns rows for nested parts too; we then map each row
                    // back to its AutoCAD entity to change its layer.
                    var rowIds = CollectRowIds(db, dlm, Trace);
                    Say($"[Разнести по слоям] Найдено деталей Plant 3D: {rowIds.Count}.");

                    foreach (int rowId in rowIds)
                    {
                        var tag = ReadTag(dlm, rowId);
                        var layerName = TagLayerMover.SanitizeLayerName(tag);
                        if (layerName == null)
                        {
                            // No usable identifier → leave the part where it
                            // is. (Log kept terse — these dominate the file.)
                            skippedNoTag++;
                            continue;
                        }

                        try
                        {
                            var layerId = EnsureUsableLayer(tr, lt, db, layerName, Trace);
                            if (layerId.IsNull)
                            {
                                skippedError++;
                                continue;
                            }

                            // A row maps to one or more AutoCAD entities (a
                            // part plus, e.g., its annotation). Move them all.
                            bool movedAny = false;
                            foreach (var entId in EntityIdsForRow(dlm, rowId, db))
                            {
                                if (entId.IsNull || entId.IsErased) continue;
                                try
                                {
                                    var ent = (Entity)tr.GetObject(entId, OpenMode.ForWrite);
                                    ent.LayerId = layerId;
                                    movedAny = true;
                                }
                                catch (System.Exception ex)
                                {
                                    Trace($"Сущность строки {rowId} не перенесена: "
                                        + $"{ex.GetType().Name}: {ex.Message}");
                                }
                            }

                            if (movedAny)
                            {
                                moved++;
                                layersTouched.Add(layerName);
                            }
                            else
                            {
                                skippedError++;
                                Trace($"Строка {rowId} (Tag='{tag}'): не найдено сущностей в чертеже.");
                            }
                        }
                        catch (System.Exception ex)
                        {
                            skippedError++;
                            Trace($"Деталь не перенесена (строка {rowId}): {ex.GetType().Name}: {ex.Message}");
                        }
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
        /// Collects the rowId of EVERY Plant 3D part in the drawing — valves,
        /// equipment, fittings, instruments (КИП), supports (опоры) and the
        /// like — including parts nested inside line-group blocks that a plain
        /// Model-Space walk never reaches. Mirrors PartReader: SelectAcPpRowIds
        /// is the primary query; a Model-Space scan supplements it with any
        /// freshly-placed in-memory parts not yet in the row map.
        /// </summary>
        static HashSet<int> CollectRowIds(Database db, DataLinksManager dlm, Action<string> trace)
        {
            var rowIdSet = new HashSet<int>();

            try
            {
                PnPRowIdArray rowIds = dlm.SelectAcPpRowIds(db);
                if (rowIds != null)
                    foreach (int id in rowIds)
                        if (id != 0) rowIdSet.Add(id);
            }
            catch (System.Exception ex)
            {
                trace?.Invoke($"SelectAcPpRowIds не удался — {ex.GetType().Name}: {ex.Message}");
            }

            // Supplement with model-space scan for unsaved/new parts.
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                if (!msId.IsNull)
                {
                    var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);
                    foreach (ObjectId entId in ms)
                    {
                        var className = entId.ObjectClass?.Name;
                        if (className == null
                            || !className.StartsWith("AcPp", StringComparison.OrdinalIgnoreCase))
                            continue;
                        int rowId = 0;
                        try { rowId = dlm.FindAcPpRowId(entId); } catch { }
                        if (rowId != 0) rowIdSet.Add(rowId);
                    }
                }
                tr.Commit();
            }
            catch (System.Exception ex)
            {
                trace?.Invoke($"Сканирование пространства модели не удалось — {ex.GetType().Name}: {ex.Message}");
            }

            return rowIdSet;
        }

        /// <summary>
        /// Reads a row's raw Tag (identifier), or null. Whether that value is a
        /// *usable* identifier (vs. empty/placeholder/separators) is decided by
        /// <see cref="TagLayerMover.SanitizeLayerName"/> — not here — so the
        /// "no identifier → don't move" rule lives in one tested place.
        /// </summary>
        static string ReadTag(DataLinksManager dlm, int rowId)
        {
            try
            {
                var props = dlm.GetAllProperties(rowId, true);
                if (props != null)
                    foreach (var kv in props)
                        if (string.Equals(kv.Key, "Tag", StringComparison.OrdinalIgnoreCase))
                            return kv.Value;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Resolves a DataLinks rowId to the AutoCAD entity ObjectId(s) that
        /// represent it in this drawing, so we can change their layer. A row
        /// can map to several AcDb objects (the part plus annotation); the
        /// caller moves each one it can open.
        /// </summary>
        static IEnumerable<ObjectId> EntityIdsForRow(DataLinksManager dlm, int rowId, Database db)
        {
            var result = new List<ObjectId>();
            PpObjectIdArray ppIds;
            try
            {
                var rowArr = new PnPRowIdArray();
                rowArr.AddLast(rowId);
                ppIds = dlm.SelectAcPpObjectIds(rowArr);
            }
            catch { return result; }

            if (ppIds == null) return result;
            foreach (var ppId in ppIds)
            {
                try
                {
                    var ids = dlm.MakeAcDbObjectIds(ppId);
                    if (ids != null)
                        foreach (ObjectId id in ids)
                            result.Add(id);
                }
                catch { }
            }
            return result;
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
