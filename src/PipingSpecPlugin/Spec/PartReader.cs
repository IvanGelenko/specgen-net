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
    /// supports, etc.). The Python edition gets the latter from the
    /// AssetOwnership SQLite table; on managed API we get the same data
    /// via DataLinksManager.GetRelatedRowIds on Equipment rows.
    /// </summary>
    public class PartReadResult
    {
        public List<PartRecord> Records = new();
        public HashSet<int> EquipmentOwnedPnPIds = new();
        // PnPID -> neighbouring PnPIDs through P3dPartConnection. Built
        // by calling DataLinksManager.GetRelatedRowIds on every row;
        // includes connectors (Buttweld, Universal) as standalone nodes
        // so valve-companion discovery can walk Valve → Connector →
        // Flange the same way the Python edition's SQL does.
        public Dictionary<int, List<int>> Topology = new();
        // Diagnostic counts so WRITESPEC can show whether topology /
        // equipment recognition found anything. Updated by BuildTopology.
        public int EquipmentCount;
        public int EquipmentNeighbourCount;
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
        public static PartReadResult Read(Database db, DataLinksManager projectDlm = null)
        {
            var result = new PartReadResult();
            DataLinksManager dlm;
            try { dlm = DataLinksManager.GetManager(db); }
            catch { return result; }
            if (dlm == null) return result;

            var lineGroupCache = new Dictionary<int, string>();

            // GetRelatedRowIds on a side-database resolves its data through
            // the native AcPp layer which keys off HostApplicationServices.
            // WorkingDatabase — if we leave the working DB pointing at the
            // active document we get RowNotInThisDatabase exceptions on the
            // side rows. Swap it for the duration of the read.
            var prevWorking = HostApplicationServices.WorkingDatabase;
            var swapped = false;
            if (db != prevWorking)
            {
                try { HostApplicationServices.WorkingDatabase = db; swapped = true; }
                catch { /* not always allowed — fall back to per-row try/catch */ }
            }

            bool prevCaching = true;
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

            bool prevProjCaching = true;
            if (projectDlm != null)
            {
                try { prevProjCaching = projectDlm.EnableCaching; } catch { }
                try { projectDlm.EnableCaching = false; } catch { }
                try { projectDlm.AcceptChanges(); } catch { }
                try { projectDlm.AuditDrawing(db); } catch { }
                try { projectDlm.ReloadLinkMap(); } catch { }
                try { projectDlm.AuditLinks(); } catch { }
            }

            // Suppress the "restore EnableCaching after read" path for
            // projectDlm — Plant 3D keeps that DataLinksManager alive
            // for the whole AutoCAD session and any cached row map
            // outlives our finally block. Keep it off permanently
            // (PartReader is fast enough that this is harmless).
            prevProjCaching = false;

            try
            {
            // SelectAcPpRowIds is the documented read-only query. The
            // earlier attempt with BeginSaveDwg/EndSaveDwg tried to
            // grab the "live save" snapshot but that pair mutates DLM
            // state and broke quiet writes from Plant 3D. The
            // AcceptChanges + AuditDrawing + ReloadLinkMap sequence
            // above already forces the row map to rebuild against the
            // live blob, so plain SelectAcPpRowIds returns fresh data.
            Autodesk.ProcessPower.DataObjects.PnPRowIdArray rowIds;
            try { rowIds = dlm.SelectAcPpRowIds(db); }
            catch { return result; }
            if (rowIds == null) return result;

            foreach (int rowId in rowIds)
            {
                PartRecord rec = null;
                try
                {
                    var props = dlm.GetAllProperties(rowId, true);
                    if (props == null) continue;
                    rec = BuildRecord(rowId, props, dlm, projectDlm, lineGroupCache);
                }
                catch { /* skip broken rows */ }
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
                    CollectAssetOwnership(dlm, rowId, result.EquipmentOwnedPnPIds);
                }
            }

            // Build topology by walking ConnectionIterator on every part
            // ObjectId in model space. This is the managed API equivalent
            // of Python's P3dPartConnection SQL — it returns the same
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
        /// that physically touch. This is the managed equivalent of the
        /// Python edition's P3dPartConnection SQL join.
        ///
        /// Earlier attempt used ConnectionIterator, which (empirically)
        /// walked the *transitive closure* of the network from the
        /// starting part — every Pipe / Connector / Flange ever reachable
        /// from a valve came back as its neighbour. That produced false
        /// positives in companion detection (every flange in the
        /// connected network got dropped). GetConnections returns just
        /// the immediate edges, so two-hop Valve → Connector → Flange
        /// works the same way as Python's two-JOIN walk.
        /// </summary>
        static void BuildTopology(Database db, DataLinksManager dlm,
                                  PartReadResult result)
        {
            ConnectionManager cm;
            try { cm = new ConnectionManager(); }
            catch { return; }

            using var tr = db.TransactionManager.StartTransaction();
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
                        // (Python equivalent: SELECT Owned FROM AssetOwnership
                        // joined on the vessel). Two-hop walk also picks up
                        // connector-bridged children — IsPnP3dConnector on the
                        // neighbour triggers a follow-on lookup.
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
            }
            catch { /* topology is best-effort; spec still works without it */ }
            finally
            {
                try { tr.Commit(); } catch { }
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

        // Back-compat helper for diagnostic commands (LISTPARTS / LISTPROJECT)
        // that don't care about the owned-by-equipment set.
        public static IEnumerable<PartRecord> ReadRows(Database db)
            => Read(db).Records;

        /// <summary>
        /// Populate <paramref name="ownedIds"/> with PnPIDs the given
        /// equipment row owns. Tries the AssetOwnership relationship
        /// explicitly first, then falls back to the bare related-rows
        /// query — Plant 3D's wrappers don't expose the schema so we
        /// can't pick a single overload with certainty.
        /// </summary>
        static void CollectAssetOwnership(DataLinksManager dlm, int rowId, HashSet<int> ownedIds)
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

            // Fallback: bare related-rows. Includes other connection
            // types but is better than nothing — at worst we exclude a
            // couple of parts that touch the vessel through pipe runs,
            // which still belong with the vessel anyway.
            try
            {
                var related = dlm.GetRelatedRowIds(rowId);
                if (related == null) return;
                foreach (int id in related)
                    if (id != rowId) ownedIds.Add(id);
            }
            catch { }
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
            // Try projectDlm first. If projectDlm.GetRelatedRowIds throws
            // because partRowId isn't valid in that DLM (different rowId
            // namespace in some Plant 3D versions), fall back to the
            // drawing DLM relationship walk and then read the Tag through
            // projectDlm using the same rowId — that path covered the
            // legacy behaviour in v1.1.1.
            int lgRowId = QueryLineGroupRowId(projectDlm, partRowId);
            if (lgRowId == 0)
                lgRowId = QueryLineGroupRowId(drawingDlm, partRowId);
            if (lgRowId == 0) return null;

            if (lineGroupCache.TryGetValue(lgRowId, out var cached))
                return cached;

            var tag = ReadTag(projectDlm, lgRowId) ?? ReadTag(drawingDlm, lgRowId);
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
