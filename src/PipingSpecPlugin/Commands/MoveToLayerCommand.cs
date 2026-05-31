using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.DataLinks;
using PipingSpecPlugin.Layers;
using PipingSpecPlugin.Settings;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;
using P3dRuntime = Autodesk.ProcessPower.AcPp3dObjectsUtils.Runtime;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// MOVEBYTAG — moves every Valve and Equipment part in the active
    /// drawing onto a layer named after its Plant 3D Tag (identifier),
    /// e.g. a valve tagged "gv-001" lands on layer "gv-001". Parts with no
    /// tag are left untouched and reported. The whole pass runs in a single
    /// write transaction so one Undo reverts everything.
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
            using var logger = new RunLogger();

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
                    var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                    if (msId.IsNull)
                    {
                        Say("[Разнести по слоям] Не найдено пространство модели.");
                        tr.Commit();
                        return;
                    }
                    var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

                    foreach (ObjectId entId in ms)
                    {
                        // Fast filter: only Plant 3D entities (class "AcPp*").
                        var className = entId.ObjectClass?.Name;
                        if (className == null
                            || !className.StartsWith("AcPp", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!IsValveOrEquipment(entId, dlm, out var tag))
                            continue;

                        var layerName = TagLayerMover.SanitizeLayerName(tag);
                        if (layerName == null)
                        {
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

                            var ent = (Entity)tr.GetObject(entId, OpenMode.ForWrite);
                            ent.LayerId = layerId;
                            moved++;
                            layersTouched.Add(layerName);
                        }
                        catch (System.Exception ex)
                        {
                            skippedError++;
                            Trace($"Деталь не перенесена ({className}): {ex.GetType().Name}: {ex.Message}");
                        }
                    }

                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    Say("[Разнести по слоям] Ошибка прохода: " + ex.Message);
                    Trace(ex.StackTrace ?? "");
                    try { tr.Abort(); } catch { }
                    return;
                }
            }

            var summary =
                $"Перенесено деталей: {moved} (слоёв: {layersTouched.Count}). "
              + $"Пропущено без идентификатора: {skippedNoTag}."
              + (skippedError > 0 ? $" Ошибок: {skippedError} (см. лог)." : "");
            Say("[Разнести по слоям] " + summary);
            WinForms.MessageBox.Show(summary, "Разнести по слоям",
                WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
        }

        /// <summary>
        /// True when the entity is a Valve (PartCategory == "Valves") or
        /// Equipment (IsPnP3dEquipment or PartCategory == "Equipment").
        /// <paramref name="tag"/> receives the part's Tag property (may be
        /// null/empty — the caller decides what to do with that).
        /// </summary>
        static bool IsValveOrEquipment(ObjectId entId, DataLinksManager dlm, out string tag)
        {
            tag = null;

            bool isEquipment = false;
            try { isEquipment = P3dRuntime.IsPnP3dEquipment(entId); }
            catch { }

            int rowId = 0;
            try { rowId = dlm.FindAcPpRowId(entId); }
            catch { }

            string category = null;
            if (rowId != 0)
            {
                try
                {
                    var props = dlm.GetAllProperties(rowId, true);
                    if (props != null)
                    {
                        foreach (var kv in props)
                        {
                            if (kv.Key == null) continue;
                            if (category == null && kv.Key == "PartCategory") category = kv.Value;
                            else if (tag == null && kv.Key == "Tag") tag = kv.Value;
                            if (category != null && tag != null) break;
                        }
                    }
                }
                catch { }
            }

            bool isValve = string.Equals(category, "Valves", StringComparison.OrdinalIgnoreCase);
            bool isEquipCat = string.Equals(category, "Equipment", StringComparison.OrdinalIgnoreCase);
            return isValve || isEquipCat || isEquipment;
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
