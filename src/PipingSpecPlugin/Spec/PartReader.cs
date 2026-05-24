using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
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
        public static PartReadResult Read(Database db)
        {
            var result = new PartReadResult();
            DataLinksManager dlm;
            try { dlm = DataLinksManager.GetManager(db); }
            catch { return result; }
            if (dlm == null) return result;

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
            try
            {
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
                    rec = BuildRecord(rowId, props);
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
                    int fromRowId;
                    try { fromRowId = dlm.FindAcPpRowId(entId); }
                    catch { continue; }
                    if (fromRowId == 0) continue;

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
                        if (toRowId == 0 || toRowId == fromRowId) continue;
                        AddEdge(result.Topology, fromRowId, toRowId);
                        AddEdge(result.Topology, toRowId, fromRowId);
                    }
                }
            }
            catch { /* topology is best-effort; spec still works without it */ }
            finally
            {
                try { tr.Commit(); } catch { }
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

        static PartRecord BuildRecord(int rowId,
            List<System.Collections.Generic.KeyValuePair<string, string>> props)
        {
            // Linear scan: there are ~90 properties per row, and the cost
            // of building a dictionary every time outweighs the lookup
            // savings on this many fields.
            string Get(string key)
            {
                foreach (var kv in props)
                    if (kv.Key == key) return kv.Value;
                return null;
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
                Length = Get("Length"),
                LinearWeight = Get("LinearWeight"),
                LineNumberTag = Get("LineNumberTag"),
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
