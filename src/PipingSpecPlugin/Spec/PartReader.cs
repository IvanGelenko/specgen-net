using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.ProcessPower.DataLinks;

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

            var rowIds = dlm.SelectAcPpRowIds(db);
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

                // Build the topology graph: every row's neighbours through
                // P3dPartConnection. This is what valve-companion discovery
                // walks (Valve → Connector → Flange). GetRelatedRowIds is
                // a best-effort call — if it throws, the part just becomes
                // a graph leaf and detection will fall back to fallback_key.
                try
                {
                    var related = dlm.GetRelatedRowIds(rowId);
                    if (related != null)
                    {
                        var ids = new List<int>();
                        foreach (int id in related)
                            if (id != rowId) ids.Add(id);
                        if (ids.Count > 0) result.Topology[rowId] = ids;
                    }
                }
                catch { /* best-effort */ }

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
            return result;
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
            };
        }
    }
}
