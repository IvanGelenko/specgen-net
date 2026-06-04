using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.DataObjects;

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
        /// Forces the DataLinksManager's link map to re-sync with the live
        /// drawing before we query it. Without this, repeated runs in one
        /// session go stale: after a pass mutates entities, the cached link
        /// map still points at the pre-edit state, so SelectAcPpObjectIds /
        /// MakeAcDbObjectIds resolve nothing for some rows ("не найдено
        /// сущностей в чертеже") — which is exactly why closing and reopening
        /// the drawing made the errors shrink or vanish (a reopen rebuilds the
        /// map from scratch). AuditDrawing(db) is the canonical "give me live
        /// state" trigger; the rest are belt-and-braces because different
        /// Plant 3D builds surface the freshness flag through different APIs.
        /// Mirrors the sequence PartReader already relies on.
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
                trace?.Invoke($"RefreshLinkMap: {ex.GetType().Name}: {ex.Message}");
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
    }
}
