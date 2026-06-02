using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.ProcessPower.AcPp3dObjectsUtils;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PnP3dObjects;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// PartReader output: the row records plus a set of PnPIDs that are
    /// owned by Equipment in the same drawing (nozzles, manways, internal
    /// supports, etc.). The owned set is derived from
    /// DataLinksManager.GetRelatedRowIds on Equipment rows.
    /// </summary>
    public class PartReadResult
    {
        public List<PartRecord> Records = new();
        public HashSet<int> EquipmentOwnedPnPIds = new();
        // PnPID -> neighbouring PnPIDs through P3dPartConnection. Built
        // by calling DataLinksManager.GetRelatedRowIds on every row;
        // includes connectors (Buttweld, Universal) as standalone nodes
        // so valve-companion discovery can walk Valve → Connector → Flange.
        public Dictionary<int, List<int>> Topology = new();
        // Diagnostic counts so WRITESPEC can show whether topology /
        // equipment recognition found anything. Updated by BuildTopology.
        public int EquipmentCount;
        public int EquipmentNeighbourCount;
        // Rows we saw in the row-id set but could NOT turn into a record:
        // GetAllProperties threw or returned null, or BuildRecord bailed.
        // Surfaced in last-run.log so "a part went missing" can be traced
        // to the read stage instead of vanishing silently.
        public int SkippedRows;
        // True if the WorkingDatabase swap for a side-DB read failed — the
        // read may then hit RowNotInThisDatabase on some rows.
        public bool WorkingDbSwapFailed;
    }

    /// <summary>
    /// Pulls PartRecord instances out of a Plant 3D drawing's DataLinks
    /// blob. Iterating SelectAcPpRowIds gives us rowIds for every
    /// EngineeringItem in that drawing; GetAllProperties(rowId, true)
    /// returns the inherited property bag we then map onto PartRecord
    /// by name. Skipping a row when properties throw (corrupted link,
    /// stale row) is intentional — one broken part should not kill
    /// the whole spec.
    /// </summary>
    public static class PartReader
    {
        public static PartReadResult Read(Database db, DataLinksManager projectDlm = null,
                                          System.Action<string> trace = null)
        {
            void Trace(string msg) { try { trace?.Invoke(msg); } catch { } }

            var result = new PartReadResult();
            DataLinksManager dlm;
            try { dlm = DataLinksManager.GetManager(db); }
            catch (System.Exception ex)
            {
                Trace($"PartReader: GetManager не удался — {ex.GetType().Name}: {ex.Message}");
                return result;
            }
            if (dlm == null)
            {
                Trace("PartReader: DataLinksManager отсутствует для чертежа.");
                return result;
            }

            var lineGroupCache = new Dictionary<int, string>();

            // GetRelatedRowIds on a side-database resolves its data through
            // the native AcPp layer which keys off HostApplicationServices.
            // WorkingDatabase — if we leave the working DB pointing at the
            // active document we get RowNotInThisDatabase exceptions on the
            // side rows. Swap it for the duration of the read.
            var prevWorking = HostApplicationServices.WorkingDatabase;
            var swapped = false;
            bool prevCaching = true;
            bool prevProjCaching = true;

            try
            {
                if (db != prevWorking)
                {
                    try { HostApplicationServices.WorkingDatabase = db; swapped = true; }
                    catch (System.Exception ex)
                    {
                        // Not always allowed — fall back to per-row try/catch.
                        // Flag it: a failed swap is a common root cause of
                        // rows silently dropping with RowNotInThisDatabase.
                        result.WorkingDbSwapFailed = true;
                        Trace($"PartReader: swap WorkingDatabase не удался — {ex.GetType().Name}: {ex.Message}");
                    }
                }

                try { prevCaching = dlm.EnableCaching; } catch { }
                try { dlm.EnableCaching = false; } catch { }

                // Flush pending in-memory changes to the DataLinks store
                // *before* querying. The active document holds new / edited
                // parts in its Database until the DLM is told to re-sync.
                // AuditDrawing(db) rescans the drawing's PnP blob and is
                // the canonical "give me live state" trigger; the other
                // three calls are belt-and-braces (different Plant 3D
                // versions surface the freshness flag through different
                // APIs).
                try { dlm.AcceptChanges(); } catch { }
                try { dlm.AuditDrawing(db); } catch { }
                try { dlm.ReloadLinkMap(); } catch { }
                try { dlm.AuditLinks(); } catch { }
                try
                {
                    int dwgId = dlm.GetDrawingId(db);
                    if (dwgId != -1) dlm.RefreshLinkMap(dwgId);
                }
                catch { }
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
                        foreach (Autodesk.ProcessPower.DataObjects.PnPTable table in pnpDb.Tables)
                            try { table.RefreshCachedRows(); } catch { }
                    }
                }
                catch { }

                if (projectDlm != null)
                {
                    try { prevProjCaching = projectDlm.EnableCaching; } catch { }
                    try { projectDlm.EnableCaching = false; } catch { }
                    try { projectDlm.AcceptChanges(); } catch { }
                    try { projectDlm.AuditDrawing(db); } catch { }
                    try { projectDlm.ReloadLinkMap(); } catch { }
                    try { projectDlm.AuditLinks(); } catch { }
                    try
                    {
                        int projDwgId = projectDlm.GetDrawingId(db);
                        if (projDwgId != -1) projectDlm.RefreshLinkMap(projDwgId);
                    }
                    catch { }
                    try
                    {
                        var projPnpDb = projectDlm.GetPnPDatabase();
                        if (projPnpDb != null)
                        {
                            try { projPnpDb.CacheAlways = false; } catch { }
                            try { projPnpDb.Disconnect(); } catch { }
                            try { projPnpDb.Connect(); } catch { }
                            try { projPnpDb.AcceptChanges(); } catch { }
                            try { projPnpDb.SynchronizeMetadata(); } catch { }
                            foreach (Autodesk.ProcessPower.DataObjects.PnPTable table in projPnpDb.Tables)
                                try { table.RefreshCachedRows(); } catch { }
                        }
                    }
                    catch { }
                }

                // Suppress the "restore EnableCaching after read" path for
                // projectDlm — Plant 3D keeps that DataLinksManager alive
                // for the whole AutoCAD session and any cached row map
                // outlives our finally block. Keep it off permanently
                // (PartReader is fast enough that this is harmless).
                prevProjCaching = false;

                // SelectAcPpRowIds is the documented read-only query. The
                // earlier attempt with BeginSaveDwg/EndSaveDwg tried to
                // grab the "live save" snapshot but that pair mutates DLM
                // state and broke quiet writes from Plant 3D. The
                // AcceptChanges + AuditDrawing + ReloadLinkMap sequence
                // above already forces the row map to rebuild against the
                // live blob, so plain SelectAcPpRowIds returns fresh data.
                Autodesk.ProcessPower.DataObjects.PnPRowIdArray rowIds;
                try { rowIds = dlm.SelectAcPpRowIds(db); }
                catch (System.Exception ex)
                {
                    Trace($"PartReader: SelectAcPpRowIds не удался — {ex.GetType().Name}: {ex.Message}");
                    return result;
                }

                var rowIdSet = new HashSet<int>();
                if (rowIds != null)
                {
                    foreach (int id in rowIds)
                        rowIdSet.Add(id);
                }

                // Supplement: scan model space for unsaved/new Plant 3D components in memory
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                        if (!msId.IsNull)
                        {
                            var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);
                            foreach (ObjectId entId in ms)
                            {
                                var className = entId.ObjectClass?.Name;
                                if (className != null && className.StartsWith("AcPp", System.StringComparison.OrdinalIgnoreCase))
                                {
                                    int rowId = 0;
                                    try { rowId = dlm.FindAcPpRowId(entId); } catch { }
                                    if (rowId != 0)
                                    {
                                        rowIdSet.Add(rowId);
                                    }
                                }
                            }
                        }
                        tr.Commit();
                    }
                    catch { }
                }

                foreach (int rowId in rowIdSet)
                {
                    PartRecord rec = null;
                    try
                    {
                        var props = dlm.GetAllProperties(rowId, true);
                        if (props == null)
                        {
                            result.SkippedRows++;
                            Trace($"PartReader: rowId {rowId} пропущен — GetAllProperties вернул null.");
                            continue;
                        }
                        rec = BuildRecord(rowId, props, dlm, projectDlm, lineGroupCache);
                    }
                    catch (System.Exception ex)
                    {
                        // One broken part must not kill the whole spec — but
                        // record it so "a part went missing" is traceable.
                        result.SkippedRows++;
                        Trace($"PartReader: rowId {rowId} пропущен — {ex.GetType().Name}: {ex.Message}");
                    }
                    if (rec == null) continue;
                    result.Records.Add(rec);

                    // Equipment-owned exclusion (manways, internal supports
                    // attached to vessels). Plant 3D doesn't always populate
                    // a separate "Equipment" PartCategory — vessels often go
                    // through with empty/null category. The companion-flange
                    // exclusion handled below catches the case the user cares
                    // about most (nozzle-style flanges attached to valves or
                    // equipment), so this path stays as a safety net only.
                    if (string.Equals(rec.PartCategory, "Equipment",
                                      System.StringComparison.OrdinalIgnoreCase))
                    {
                        CollectAssetOwnership(dlm, rowId, result.EquipmentOwnedPnPIds, Trace);
                    }
                }

                // Build topology by walking ConnectionManager.GetConnections
                // on every part ObjectId in model space, yielding the
                // connector-mediated part-to-part graph. GetRelatedRowIds was
                // attempted earlier but came back empty on real projects, so
                // we go through the connection manager directly.
                BuildTopology(db, dlm, result);
                return result;
            }
            finally
            {
                try { dlm.EnableCaching = prevCaching; } catch { }
                if (projectDlm != null)
                {
                    try { projectDlm.EnableCaching = prevProjCaching; } catch { }
                }
                if (swapped)
                {
                    try { HostApplicationServices.WorkingDatabase = prevWorking; } catch { }
                }
            }
        }

        /// <summary>
        /// Iterate every entity in model space and ask ConnectionManager
        /// for its *direct* P3dPartConnection neighbours. Each Connection
        /// is a single record with ObjectId1/ObjectId2 — the two ports
        /// that physically touch.
        ///
        /// Earlier attempt used ConnectionIterator, which (empirically)
        /// walked the *transitive closure* of the network from the
        /// starting part — every Pipe / Connector / Flange ever reachable
        /// from a valve came back as its neighbour. That produced false
        /// positives in companion detection (every flange in the
        /// connected network got dropped). GetConnections returns just
        /// the immediate edges, so a two-hop Valve → Connector → Flange
        /// walk stays local to the joint.
        /// </summary>
        static void BuildTopology(Database db, DataLinksManager dlm,
                                  PartReadResult result)
        {
            ConnectionManager cm;
            try { cm = new ConnectionManager(); }
            catch { return; }

            // ConnectionManager wraps a native object (DisposableWrapper);
            // BuildTopology runs once per drawing, so without disposing it a
            // project-wide scan leaks one native handle per DWG.
            using (cm)
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                    if (msId.IsNull) return;
                    var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

                    foreach (ObjectId entId in ms)
                    {
                        // Fast filter: skip native AutoCAD entities (like lines, circles, blocks)
                        // by checking their class name prefix. All Plant 3D entities start with "AcPp".
                        var className = entId.ObjectClass?.Name;
                        if (className == null || !className.StartsWith("AcPp", System.StringComparison.OrdinalIgnoreCase))
                            continue;

                        int fromRowId = 0;
                        try { fromRowId = dlm.FindAcPpRowId(entId); }
                        catch { /* not a Plant 3D entity */ }

                        // Equipment recognition is independent of a rowId — a
                        // vessel may not produce its own EngineeringItems row,
                        // but its connections still need to flag its child
                        // nozzles as owned. We always probe IsPnP3dEquipment
                        // and route accordingly.
                        bool isEquipment = false;
                        try { isEquipment = Runtime.IsPnP3dEquipment(entId); }
                        catch { }
                        if (isEquipment) result.EquipmentCount++;

                        if (fromRowId == 0 && !isEquipment) continue;

                        ConnectionCollection conns;
                        try { conns = cm.GetConnections(entId); }
                        catch { continue; }
                        if (conns == null) continue;

                        // ConnectionCollection exposes only an indexer + Item
                        // property, no Count. Try indices until it throws.
                        for (var i = 0; ; i++)
                        {
                            Connection c;
                            try { c = conns[i]; }
                            catch { break; }
                            if (c == null) break;
                            var otherId = c.ObjectId1 == entId
                                ? c.ObjectId2
                                : (c.ObjectId2 == entId ? c.ObjectId1 : ObjectId.Null);
                            if (otherId.IsNull) continue;
                            int toRowId = 0;
                            try { toRowId = dlm.FindAcPpRowId(otherId); }
                            catch { continue; }
                            if (toRowId == 0) continue;

                            // Equipment node ⇒ its neighbour is an owned part
                            // (nozzle, manway, internal support). Two-hop walk
                            // also picks up connector-bridged children —
                            // IsPnP3dConnector on the neighbour triggers a
                            // follow-on lookup.
                            if (isEquipment)
                            {
                                result.EquipmentOwnedPnPIds.Add(toRowId);
                                result.EquipmentNeighbourCount++;
                                FollowConnectorChildren(cm, dlm, otherId, toRowId,
                                                         result.EquipmentOwnedPnPIds);
                            }

                            // The reverse direction matters too: in some
                            // projects the Equipment entity has zero outgoing
                            // P3dPartConnections, but the Nozzle/Flange parts
                            // attached to it list the Equipment (or its nozzle
                            // subpart) as one of *their* neighbours. Detect
                            // either and mark fromRowId as owned.
                            if (!isEquipment && fromRowId != 0)
                            {
                                bool neighbourIsEquipmentLike = false;
                                try
                                {
                                    neighbourIsEquipmentLike =
                                        Runtime.IsPnP3dEquipment(otherId)
                                     || Runtime.IsPnP3dNozzleSubPart(otherId);
                                }
                                catch { }
                                if (neighbourIsEquipmentLike)
                                {
                                    result.EquipmentOwnedPnPIds.Add(fromRowId);
                                    result.EquipmentNeighbourCount++;
                                }
                            }

                            if (fromRowId != 0 && toRowId != fromRowId)
                            {
                                AddEdge(result.Topology, fromRowId, toRowId);
                                AddEdge(result.Topology, toRowId, fromRowId);
                            }
                        }
                    }
                    tr.Commit();
                }
                catch { /* topology is best-effort; spec still works without it */ }
            }
        }

        /// <summary>
        /// If a neighbour of Equipment is a P3dConnector (weld/buttweld),
        /// the part the user sees in the spec is the *other* side of that
        /// connector — typically a Nozzle flange. Follow one hop further
        /// and add that part to the equipment-owned set too.
        /// </summary>
        static void FollowConnectorChildren(ConnectionManager cm,
            DataLinksManager dlm, ObjectId connectorId, int connectorRowId,
            HashSet<int> ownedIds)
        {
            bool isConnector = false;
            try { isConnector = Runtime.IsPnP3dConnector(connectorId); }
            catch { return; }
            if (!isConnector) return;

            ConnectionCollection conns;
            try { conns = cm.GetConnections(connectorId); }
            catch { return; }
            if (conns == null) return;
            for (var i = 0; ; i++)
            {
                Connection c;
                try { c = conns[i]; }
                catch { break; }
                if (c == null) break;
                var otherId = c.ObjectId1 == connectorId
                    ? c.ObjectId2
                    : (c.ObjectId2 == connectorId ? c.ObjectId1 : ObjectId.Null);
                if (otherId.IsNull) continue;
                bool isEq = false;
                try { isEq = Runtime.IsPnP3dEquipment(otherId); } catch { }
                if (isEq) continue;  // skip back to the vessel itself
                int toRow = 0;
                try { toRow = dlm.FindAcPpRowId(otherId); } catch { }
                if (toRow != 0 && toRow != connectorRowId)
                    ownedIds.Add(toRow);
            }
        }

        static void AddEdge(Dictionary<int, List<int>> graph, int from, int to)
        {
            if (!graph.TryGetValue(from, out var list))
                graph[from] = list = new List<int>();
            if (!list.Contains(to)) list.Add(to);
        }

        /// <summary>
        /// Populate <paramref name="ownedIds"/> with PnPIDs the given
        /// equipment row owns. Tries the AssetOwnership relationship
        /// explicitly first, then falls back to the bare related-rows
        /// query — Plant 3D's wrappers don't expose the schema so we
        /// can't pick a single overload with certainty.
        /// </summary>
        static void CollectAssetOwnership(DataLinksManager dlm, int rowId, HashSet<int> ownedIds,
                                          System.Action<string> trace = null)
        {
            // Try the explicit relationship overload with several names
            // it could plausibly be registered under. AcceptedRelations
            // is empirical — Plant 3D's docs don't list them and the
            // taxonomy varies between releases.
            string[] candidates =
            {
                "AssetOwnership", "EquipmentOwnership",
                "PnPRelationshipDef:AssetOwnership",
            };
            foreach (var rel in candidates)
            {
                try
                {
                    var related = dlm.GetRelatedRowIds("EngineeringItems",
                                                       "EngineeringItems",
                                                       rowId, rel);
                    if (related != null && related.Count > 0)
                    {
                        foreach (int id in related)
                            if (id != rowId) ownedIds.Add(id);
                        return;
                    }
                }
                catch { /* try the next candidate */ }
            }

            // Fallback: bare related-rows. This is imprecise — it returns
            // every related row regardless of relationship type, so a pipe
            // run merely touching the vessel comes back here. Marking such a
            // row "owned" would silently DROP it from the spec, which for a
            // procurement document is the worst kind of error (under-count
            // that looks complete). So we guard the fallback: only exclude
            // rows whose own category is NOT a procurement category. Pipes,
            // fittings, valves and flanges are never equipment-owned in a way
            // that should hide them — mirrors the safety pass in
            // ValveCompanionFinder.
            try
            {
                var related = dlm.GetRelatedRowIds(rowId);
                if (related == null) return;
                foreach (int id in related)
                {
                    if (id == rowId) continue;
                    if (IsProcurementCategory(dlm, id))
                    {
                        trace?.Invoke($"AssetOwnership(fallback): rowId {id} НЕ помечен owned — это закупочная категория (труба/деталь/арматура/фланец), оставляем в спецификации.");
                        continue;
                    }
                    if (ownedIds.Add(id))
                        trace?.Invoke($"AssetOwnership(fallback): rowId {id} помечен owned для оборудования {rowId}.");
                }
            }
            catch { }
        }

        // True when the row's PartCategory is one a procurement spec must
        // always keep, so the imprecise AssetOwnership fallback never hides
        // it. Reads the single PartCategory property; any failure is treated
        // as "not a procurement category" so behaviour stays conservative
        // (the row can still be excluded as before).
        static bool IsProcurementCategory(DataLinksManager dlm, int rowId)
        {
            string cat = null;
            try
            {
                var props = dlm.GetAllProperties(rowId, true);
                if (props != null)
                    foreach (var kv in props)
                        if (kv.Key == "PartCategory") { cat = kv.Value; break; }
            }
            catch { return false; }
            if (string.IsNullOrEmpty(cat)) return false;
            return cat.Equals("Pipe", System.StringComparison.OrdinalIgnoreCase)
                || cat.Equals("Fittings", System.StringComparison.OrdinalIgnoreCase)
                || cat.Equals("Valves", System.StringComparison.OrdinalIgnoreCase)
                || cat.Equals("Flanges", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Walk P3dLineGroupPartRelationship to find the LineGroup row
        /// for this part, then read its current Tag. Prefer projectDlm
        /// over drawing-local: the project DLM owns the canonical
        /// P3dLineGroup table that gets updated when a user renames a
        /// line in the Project Manager. The drawing-local DLM also has
        /// the table but its copy can lag behind.
        /// </summary>
        static string ResolveLiveLineTag(int partRowId,
            DataLinksManager drawingDlm,
            DataLinksManager projectDlm,
            Dictionary<int, string> lineGroupCache)
        {
            // Query drawing-local DLM first since partRowId is drawing-local.
            // Drawing-local and project-wide PnPIDs belong to different
            // namespaces — querying projectDlm first with
            // a drawing-local partRowId can match an unrelated row (e.g. a connector)
            // and return the wrong line tag.
            int lgRowId = QueryLineGroupRowId(drawingDlm, partRowId);
            bool isProjectLg = false;

            if (lgRowId == 0)
            {
                // Fallback: try project DLM (shares namespace in SQL Server/vault)
                lgRowId = QueryLineGroupRowId(projectDlm, partRowId);
                if (lgRowId == 0) return null;
                isProjectLg = true;
            }

            if (lineGroupCache.TryGetValue(lgRowId, out var cached))
                return cached;

            // If found via projectDlm, read from projectDlm first. Otherwise,
            // read from drawingDlm (or projectDlm if they are the same instance).
            var tag = isProjectLg
                ? (ReadTag(projectDlm, lgRowId) ?? ReadTag(drawingDlm, lgRowId))
                : (ReadTag(drawingDlm, lgRowId) ?? (projectDlm == drawingDlm ? ReadTag(projectDlm, lgRowId) : null));

            lineGroupCache[lgRowId] = tag;
            return tag;
        }

        static int QueryLineGroupRowId(DataLinksManager dlmToTry, int partRowId)
        {
            if (dlmToTry == null) return 0;
            try
            {
                var related = dlmToTry.GetRelatedRowIds(
                    "P3dLineGroupPartRelationship", "Part", partRowId, "LineGroup");
                if (related == null) return 0;
                foreach (int id in related) return id;
            }
            catch { }
            return 0;
        }

        static string ReadTag(DataLinksManager dlmToTry, int lgRowId)
        {
            if (dlmToTry == null) return null;
            try
            {
                var props = dlmToTry.GetAllProperties(lgRowId, true);
                if (props == null) return null;
                foreach (var kv in props)
                    if (string.Equals(kv.Key, "Tag", System.StringComparison.OrdinalIgnoreCase))
                        return kv.Value;
            }
            catch { }
            return null;
        }

        static PartRecord BuildRecord(int rowId,
            List<System.Collections.Generic.KeyValuePair<string, string>> props,
            DataLinksManager dlm,
            DataLinksManager projectDlm,
            Dictionary<int, string> lineGroupCache)
        {
            // O(1) Lookup: convert list to dictionary to avoid 35+ linear scans
            // of ~90 items per row. Keeps the first occurrence if keys duplicate.
            var propDict = new Dictionary<string, string>(System.StringComparer.Ordinal);
            foreach (var kv in props)
            {
                if (kv.Key != null && !propDict.ContainsKey(kv.Key))
                    propDict.Add(kv.Key, kv.Value);
            }

            string Get(string key)
            {
                propDict.TryGetValue(key, out var val);
                return val;
            }

            // Resolve LineNumberTag through the live LineGroup row
            // rather than the property bag — Plant 3D copies the line
            // tag into the EngineeringItem's "LineNumberTag" property
            // at link-time, and that copy doesn't update when the user
            // renames the line in the Project Manager. The relationship
            // table + projectDlm-backed P3dLineGroup row is always
            // current.
            //
            // We try projectDlm first (it owns the live P3dLineGroup
            // table). Fall through to drawing-local DLM only when the
            // project DLM is unavailable or the query throws.
            string resolvedLineNumberTag = ResolveLiveLineTag(
                rowId, dlm, projectDlm, lineGroupCache);
            if (string.IsNullOrEmpty(resolvedLineNumberTag))
            {
                resolvedLineNumberTag = Get("LineNumberTag");
            }

            return new PartRecord
            {
                PnPID = rowId,
                Status = Get("Status"),
                PartCategory = Get("PartCategory"),
                PartFamilyLongDesc = Get("PartFamilyLongDesc"),
                PartSizeLongDesc = Get("PartSizeLongDesc"),
                ShortDescription = Get("ShortDescription"),
                NominalDiameter = Get("NominalDiameter"),
                NominalUnit = Get("NominalUnit"),
                Material = Get("Material"),
                Spec = Get("Spec"),
                ItemCode = Get("ItemCode"),
                Manufacturer = Get("Manufacturer"),
                DesignStd = Get("DesignStd"),
                CompatibleStandard = Get("CompatibleStandard"),
                Weight = Get("Weight"),
                LengthUnit = Get("LengthUnit"),
                WeightUnit = Get("WeightUnit"),
                // Pipe carries length under "Length" (full segment).
                // PipeBend doesn't populate that field — its physical
                // arc length lives in "CutLength" (verified against the
                // ru-RU DisplayNames.xml entry "$$dn-PipeBend-CutLength").
                // Try Length first; fall back to CutLength so bends
                // have a numeric length the folder can read.
                Length = Get("Length") ?? Get("CutLength"),
                LinearWeight = Get("LinearWeight"),
                LineNumberTag = resolvedLineNumberTag,
                ContentIsoSymbolDefinition = Get("ContentIsoSymbolDefinition"),
                EquipTag = Get("Tag"),
                PositionX = Get("Position X"),
                PositionY = Get("Position Y"),
                PositionZ = Get("Position Z"),
                CogX = Get("COG X"),
                CogY = Get("COG Y"),
                CogZ = Get("COG Z"),
                PnPClassName = Get("PnPClassName"),
                PressureClass = Get("PressureClass"),
                Facing = Get("Facing"),
                // Bolt sets carry their piece count under "NumberInSet"
                // — confirmed against ru-RU/DisplayNames.xml entry
                // "$$dn-BoltSet-NumberInSet -> Количество в комплекте".
                // Probe a few alternates too in case other part families
                // use slightly different names.
                Quantity = Get("NumberInSet") ?? Get("Quantity")
                            ?? Get("NumBolts") ?? Get("BoltCount")
                            ?? Get("Count") ?? Get("NumberOfBolts"),
            };
        }
    }
}
