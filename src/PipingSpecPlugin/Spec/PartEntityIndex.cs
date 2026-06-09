using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.ProcessPower.AcPp3dObjectsUtils;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.DataObjects;
using PipingSpecPlugin.Layers;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Shared bridge between Plant 3D DataLinks rows and the AutoCAD entities
    /// that draw them. Both MOVEBYTAG and SELECTBYSTATUS need the same three
    /// operations — enumerate every part as a rowId, read a property off a
    /// row, and resolve a row back to its drawing entities — so they live in
    /// one tested place rather than being copy-pasted per command.
    ///
    /// No AutoCAD <c>Editor</c> / UI here; callers pass a trace callback for
    /// diagnostics and own the document lock / transaction.
    /// </summary>
    public static class PartEntityIndex
    {
        /// <summary>
        /// Saves the active drawing if it has unsaved edits, then re-syncs the
        /// DataLinks caches. This is the missing piece that made SELECTBYSTATUS
        /// / MOVEBYTAG ignore live edits: Plant 3D keeps an edited part property
        /// (Status, Tag, …) in the document's in-memory blob, and the DataLinks
        /// store is only updated on SAVE. The spec generator works because it
        /// saves the drawing before reading (WriteSpecCommand); cache-flushing
        /// alone can't surface a value that was never written to the store. So
        /// we do the same: save-if-dirty, then RefreshLinkMap.
        ///
        /// DBMOD != 0 means the active document has unsaved changes. We try
        /// SaveAs (preserves the path) then fall back to Save(). Failures are
        /// traced, not thrown — a read-only command must still run, just on
        /// possibly-stale data, and the caller can warn.
        /// </summary>
        public static void SaveAndRefresh(Document doc, Database db, DataLinksManager dlm,
                                          Action<string> trace)
        {
            try
            {
                object dbmod = Autodesk.AutoCAD.ApplicationServices.Application
                    .GetSystemVariable("DBMOD");
                bool modified = System.Convert.ToInt32(dbmod) != 0;
                if (modified && doc != null && !string.IsNullOrEmpty(doc.Name))
                {
                    bool saved = false;
                    try
                    {
                        db.SaveAs(doc.Name, true, DwgVersion.Current, db.SecurityParameters);
                        saved = true;
                    }
                    catch (System.Exception ex)
                    {
                        trace?.Invoke($"SaveAs не удался ({ex.GetType().Name}: {ex.Message}); пробую Save().");
                        try { db.Save(); saved = true; }
                        catch (System.Exception ex2)
                        {
                            trace?.Invoke($"Save() тоже не удался: {ex2.GetType().Name}: {ex2.Message}");
                        }
                    }
                    if (!saved)
                        trace?.Invoke("ВНИМАНИЕ: не удалось сохранить чертёж — данные могут быть устаревшими.");
                }
            }
            catch (System.Exception ex)
            {
                trace?.Invoke($"Проверка изменений чертежа не удалась: {ex.GetType().Name}: {ex.Message}");
            }

            RefreshLinkMap(db, dlm, trace);
        }

        /// <summary>
        /// Forces the DataLinksManager to re-sync with the live drawing before
        /// we query it — BOTH the link map (rowId ↔ entity) AND the cached
        /// property store (the values GetAllProperties returns, e.g. Status,
        /// Tag). Two distinct caches:
        ///   • Link map — AuditDrawing / ReloadLinkMap / RefreshLinkMap. Stale
        ///     here means SelectAcPpObjectIds / MakeAcDbObjectIds resolve
        ///     nothing for some rows ("не найдено сущностей в чертеже").
        ///   • Property store — flushed via GetPnPDatabase().Disconnect /
        ///     Connect / SynchronizeMetadata + each table's RefreshCachedRows.
        ///     Stale here means an edited property (e.g. a part's Status
        ///     changed in the drawing) is read at its OLD value, so
        ///     SELECTBYSTATUS / MOVEBYTAG don't see the change until reopen.
        /// This mirrors the full sequence PartReader uses so these commands
        /// pick up live edits the same way the spec generator does.
        /// AuditDrawing(db) is the canonical "give me live state" trigger; the
        /// rest are belt-and-braces because different Plant 3D builds surface
        /// freshness through different APIs.
        /// </summary>
        public static void RefreshLinkMap(Database db, DataLinksManager dlm, Action<string> trace)
        {
            try { dlm.EnableCaching = false; } catch { }
            try { dlm.AcceptChanges(); } catch { }
            try { dlm.AuditDrawing(db); } catch { }
            try { dlm.ReloadLinkMap(); } catch { }
            try { dlm.AuditLinks(); } catch { }
            try
            {
                int dwgId = dlm.GetDrawingId(db);
                if (dwgId != -1) dlm.RefreshLinkMap(dwgId);
            }
            catch (System.Exception ex)
            {
                trace?.Invoke($"RefreshLinkMap (link map): {ex.GetType().Name}: {ex.Message}");
            }

            // Flush the property-value cache so edited properties (Status, Tag,
            // …) read fresh, not at their pre-edit values.
            try
            {
                var pnpDb = dlm.GetPnPDatabase();
                if (pnpDb != null)
                {
                    try { pnpDb.CacheAlways = false; } catch { }
                    try { pnpDb.Disconnect(); } catch { }
                    try { pnpDb.Connect(); } catch { }
                    try { pnpDb.AcceptChanges(); } catch { }
                    try { pnpDb.SynchronizeMetadata(); } catch { }
                    foreach (PnPTable table in pnpDb.Tables)
                        try { table.RefreshCachedRows(); } catch { }
                }
            }
            catch (System.Exception ex)
            {
                trace?.Invoke($"RefreshLinkMap (property cache): {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Collects the rowId of EVERY Plant 3D part in the drawing — valves,
        /// equipment, fittings, instruments (КИП), supports (опоры) and the
        /// like — including parts nested inside line-group blocks that a plain
        /// Model-Space walk never reaches. Mirrors PartReader: SelectAcPpRowIds
        /// is the primary query; a Model-Space scan supplements it with any
        /// freshly-placed in-memory parts not yet in the row map.
        ///
        /// Re-syncs the link map first so repeated runs in one session don't
        /// resolve stale rows. See <see cref="RefreshLinkMap"/>.
        /// </summary>
        public static HashSet<int> CollectRowIds(Database db, DataLinksManager dlm, Action<string> trace)
        {
            RefreshLinkMap(db, dlm, trace);

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
        /// Reads a single DataLinks property off a row by name
        /// (case-insensitive), or null if absent. Used for "Tag", "Status",
        /// etc. The caller decides what a usable value is.
        /// </summary>
        public static string ReadProperty(DataLinksManager dlm, int rowId, string propertyName)
        {
            try
            {
                var props = dlm.GetAllProperties(rowId, true);
                if (props != null)
                    foreach (var kv in props)
                        if (string.Equals(kv.Key, propertyName, StringComparison.OrdinalIgnoreCase))
                            return kv.Value;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Resolves a DataLinks rowId to the AutoCAD entity ObjectId(s) that
        /// represent it in <paramref name="db"/>. A row can map to several AcDb
        /// objects (the part plus, e.g., its annotation); all valid ones are
        /// returned. Tries the database-scoped MakeAcDbObjectId first (it
        /// resolves against the live drawing and avoids cross-drawing
        /// confusion), then falls back to the collection form. Only ObjectIds
        /// that actually belong to <paramref name="db"/> and are not erased are
        /// kept — a stale link-map entry pointing at another database or an
        /// erased object is silently dropped instead of surfacing as a phantom
        /// "part with no entity".
        /// </summary>
        public static IEnumerable<ObjectId> EntityIdsForRow(DataLinksManager dlm, int rowId, Database db)
        {
            var result = new List<ObjectId>();
            var seen = new HashSet<ObjectId>();

            PpObjectIdArray ppIds;
            try
            {
                var rowArr = new PnPRowIdArray();
                rowArr.AddLast(rowId);
                ppIds = dlm.SelectAcPpObjectIds(rowArr);
            }
            catch { return result; }

            if (ppIds == null) return result;

            void Keep(ObjectId id)
            {
                if (id.IsNull || id.IsErased) return;
                if (id.Database != db) return;        // belongs to another drawing
                if (seen.Add(id)) result.Add(id);
            }

            foreach (var ppId in ppIds)
            {
                bool got = false;
                // Database-scoped singular resolve — most reliable against the
                // live drawing.
                try
                {
                    var id = dlm.MakeAcDbObjectId(ppId, db);
                    if (!id.IsNull) { Keep(id); got = true; }
                }
                catch { }

                // Fall back to the collection form for rows that map to
                // several AcDb objects, or when the singular form failed.
                if (!got)
                {
                    try
                    {
                        var ids = dlm.MakeAcDbObjectIds(ppId);
                        if (ids != null)
                            foreach (ObjectId id in ids)
                                Keep(id);
                    }
                    catch { }
                }
            }
            return result;
        }

        /// <summary>
        /// One entry of a MOVEBYTAG plan: the layer a part belongs on (named
        /// after its Tag) and the AutoCAD Handles of the entities that draw it.
        /// Handles — not ObjectIds — are the bridge, because the plan is built
        /// from a fresh side-database read off disk (so the Tag is current) but
        /// applied to the live drawing; a Handle is stable across save/reopen
        /// and resolves in either database, an ObjectId does not.
        /// </summary>
        public readonly struct MovePlanEntry
        {
            public readonly string LayerName;
            public readonly List<Handle> Handles;
            public MovePlanEntry(string layerName, List<Handle> handles)
            {
                LayerName = layerName;
                Handles = handles;
            }
        }

        /// <summary>
        /// Builds the MOVEBYTAG plan from a FRESH copy of the drawing read off
        /// disk, exactly as the spec generator does (see ReadOneDrawing). The
        /// live in-memory DataLinksManager keeps a stale property cache: after a
        /// user edits a part's Tag in the Properties palette, GetAllProperties
        /// on the live db keeps returning the OLD tag even after save + every
        /// documented cache-flush — so MOVEBYTAG moved parts onto the layer of
        /// their FIRST tag and never picked the new one up. Reading a
        /// just-saved side database sidesteps the cache entirely: the tag on
        /// disk is the current tag.
        ///
        /// The plan carries entity Handles (stable across databases), resolved
        /// against the live drawing by the caller. <paramref name="dwgPath"/>
        /// must be the active document's file, already saved to disk by the
        /// caller. Returns one entry per movable part; tagless parts and
        /// equipment nozzle/sub-part rows are filtered out here so the counters
        /// the caller reports stay correct.
        /// </summary>
        public static List<MovePlanEntry> BuildMovePlanFromDisk(
            string dwgPath, Action<string> trace,
            out int skippedNoTag, out int skippedNoEntity, out int partCount)
        {
            var plan = new List<MovePlanEntry>();
            skippedNoTag = 0;
            skippedNoEntity = 0;
            partCount = 0;

            if (string.IsNullOrEmpty(dwgPath) || !File.Exists(dwgPath))
            {
                trace?.Invoke($"BuildMovePlanFromDisk: файл не найден на диске: {dwgPath}");
                return plan;
            }

            // Open the saved DWG as a side database — never trust the live
            // DLM cache (see summary). The side DB is read-only to us; we only
            // pull tags and handles from it.
            using var sideDb = new Database(false, true);
            try
            {
                sideDb.ReadDwgFile(dwgPath, FileShare.ReadWrite, true, "");
                sideDb.CloseInput(true);
            }
            catch (System.Exception ex)
            {
                trace?.Invoke($"BuildMovePlanFromDisk: не удалось открыть {Path.GetFileName(dwgPath)} как side-DB — {ex.GetType().Name}: {ex.Message}");
                return plan;
            }

            DataLinksManager sideDlm;
            try { sideDlm = DataLinksManager.GetManager(sideDb); }
            catch (System.Exception ex)
            {
                trace?.Invoke($"BuildMovePlanFromDisk: GetManager на side-DB не удался — {ex.GetType().Name}: {ex.Message}");
                return plan;
            }
            if (sideDlm == null)
            {
                trace?.Invoke("BuildMovePlanFromDisk: у копии чертежа нет DataLinks.");
                return plan;
            }

            // GetRelatedRowIds / native AcPp resolution keys off the working
            // database; point it at the side DB for the read, restore after.
            var prevWorking = HostApplicationServices.WorkingDatabase;
            bool swapped = false;
            try
            {
                try { HostApplicationServices.WorkingDatabase = sideDb; swapped = true; }
                catch (System.Exception ex)
                {
                    trace?.Invoke($"BuildMovePlanFromDisk: swap WorkingDatabase не удался — {ex.GetType().Name}: {ex.Message}");
                }

                var rowIds = CollectRowIds(sideDb, sideDlm, trace);
                partCount = rowIds.Count;

                foreach (int rowId in rowIds)
                {
                    var tag = ReadProperty(sideDlm, rowId, "Tag");
                    var layerName = TagLayerMover.SanitizeLayerName(tag);
                    if (layerName == null)
                    {
                        skippedNoTag++;
                        continue;
                    }

                    var ents = new List<ObjectId>();
                    foreach (var id in EntityIdsForRow(sideDlm, rowId, sideDb))
                        if (!id.IsNull && !id.IsErased) ents.Add(id);
                    if (ents.Count == 0)
                    {
                        skippedNoEntity++;
                        trace?.Invoke($"Строка {rowId} (Tag='{tag}'): не найдено сущностей в копии чертежа.");
                        continue;
                    }

                    // Equipment guard — same reasoning as the live path: a
                    // vessel block is shared by the vessel row and every nozzle
                    // row, so skip nozzle/sub-part rows or the block lands on a
                    // nozzle layer. See IsNozzleRowForEquipmentEntity.
                    bool isNozzleRow = false;
                    foreach (var id in ents)
                        if (IsNozzleRowForEquipmentEntity(sideDlm, rowId, id)) { isNozzleRow = true; break; }
                    if (isNozzleRow)
                    {
                        skippedNoTag++;
                        trace?.Invoke($"Строка {rowId} (Tag='{tag}'): штуцер/подэлемент оборудования — "
                            + "перенос по строке оборудования, не по тегу штуцера.");
                        continue;
                    }

                    var handles = new List<Handle>();
                    foreach (var id in ents)
                    {
                        try
                        {
                            var h = id.Handle;
                            if (h.Value != 0) handles.Add(h);
                        }
                        catch { }
                    }
                    if (handles.Count == 0)
                    {
                        skippedNoEntity++;
                        trace?.Invoke($"Строка {rowId} (Tag='{tag}'): не удалось получить Handle сущностей.");
                        continue;
                    }

                    plan.Add(new MovePlanEntry(layerName, handles));
                }
            }
            finally
            {
                if (swapped)
                    try { HostApplicationServices.WorkingDatabase = prevWorking; } catch { }
            }

            return plan;
        }

        /// <summary>
        /// True when <paramref name="entId"/> is an Equipment block whose own
        /// DataLinks row is NOT <paramref name="rowId"/> — i.e. the current row
        /// is a nozzle / sub-part of that equipment (a separate EngineeringItems
        /// row, with its own Tag like "N-1") that merely resolves back to the
        /// same equipment block entity.
        ///
        /// Why MOVEBYTAG needs this: a vessel is a single AcDb block, but Plant
        /// 3D emits separate rows for the vessel ("E-101") and each nozzle
        /// ("N-1", "N-2"), and ALL of those rows resolve — via
        /// SelectAcPpObjectIds / MakeAcDbObjectId — to the SAME equipment block.
        /// Without this guard MOVEBYTAG queues the block onto the vessel layer
        /// AND onto every nozzle layer; whichever the write transaction applies
        /// last wins, so the vessel "moves" onto a nozzle layer (N-1). We move
        /// the equipment block only on its OWN row (where FindAcPpRowId == rowId)
        /// and skip the nozzle rows pointing at it.
        ///
        /// Conservative on failure: if FindAcPpRowId throws or returns 0 we
        /// return false (keep the part) rather than risk dropping a real one.
        /// Integer rowId comparison is used deliberately instead of comparing
        /// sanitized Tags — it is exact and avoids locale/case/sanitize drift.
        /// </summary>
        public static bool IsNozzleRowForEquipmentEntity(DataLinksManager dlm, int rowId,
                                                         ObjectId entId)
        {
            if (entId.IsNull || entId.IsErased) return false;

            bool isEquipment;
            try { isEquipment = Runtime.IsPnP3dEquipment(entId); }
            catch { return false; }
            if (!isEquipment) return false;

            int ownRowId;
            try { ownRowId = dlm.FindAcPpRowId(entId); }
            catch { return false; }
            if (ownRowId == 0) return false;   // no own row — don't drop the block

            return ownRowId != rowId;
        }
    }
}
