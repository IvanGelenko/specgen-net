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

                // Equipment ⇒ everything related to it is "owned" and
                // should be excluded from the spec (nozzles, manways,
                // internal supports). The Python edition does this via
                // SELECT Owned FROM AssetOwnership; on managed API we
                // pull related rowIds directly.
                if (string.Equals(rec.PartCategory, "Equipment",
                                  System.StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var related = dlm.GetRelatedRowIds(rowId);
                        if (related != null)
                            foreach (int ownedId in related)
                                if (ownedId != rowId)
                                    result.EquipmentOwnedPnPIds.Add(ownedId);
                    }
                    catch { /* relationship lookup is best-effort */ }
                }
            }
            return result;
        }

        // Back-compat helper for diagnostic commands (LISTPARTS / LISTPROJECT)
        // that don't care about the owned-by-equipment set.
        public static IEnumerable<PartRecord> ReadRows(Database db)
            => Read(db).Records;

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
            };
        }
    }
}
