using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Discovers parts that should be excluded when the user picks
    /// "Exclude valve companions". Mirrors the Python edition's
    /// <c>_discover_valve_companion_pnpids</c>: walk Valve → Connector
    /// → Flange through the topology graph to find companion flanges,
    /// then for each companion flange pull in the nearest gasket and
    /// bolt set by 3D distance, with a (Spec, NominalDiameter, LineTag)
    /// fallback when coordinates are missing.
    /// </summary>
    public static class ValveCompanionFinder
    {
        public static HashSet<int> Discover(IList<PartRecord> records,
                                            Dictionary<int, List<int>> topology)
        {
            var drop = new HashSet<int>();
            if (records == null || records.Count == 0) return drop;
            if (topology == null) topology = new Dictionary<int, List<int>>();

            // Threshold: 4 inches if the project uses imperial length
            // units, 100 mm otherwise (matches Python's check on the
            // first non-null LengthUnit found in EngineeringItems).
            var firstUnit = records
                .Select(r => (r.LengthUnit ?? "").ToLowerInvariant())
                .FirstOrDefault(u => !string.IsNullOrEmpty(u));
            var isImperial = !string.IsNullOrEmpty(firstUnit)
                          && (firstUnit.Contains("in") || firstUnit.Contains("ft"));
            var threshold = isImperial ? 4.0 : 100.0;

            // Index records by PnPID for cheap lookup during the walk.
            var byId = records.ToDictionary(r => r.PnPID);
            // Heuristics — Plant 3D doesn't expose a typed "is connector"
            // flag; class names, PartCategory or descriptions are what
            // we have to go on.
            bool IsConnector(PartRecord r)
                => string.Equals(r.PnPClassName, "P3dConnector",
                                 System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.PartCategory, "P3dConnector",
                                 System.StringComparison.OrdinalIgnoreCase);
            bool IsFlange(PartRecord r)
                => string.Equals(r.PartCategory, "Flanges",
                                 System.StringComparison.OrdinalIgnoreCase);
            bool IsValve(PartRecord r)
                => string.Equals(r.PartCategory, "Valves",
                                 System.StringComparison.OrdinalIgnoreCase);

            // Step 1: Valve → Connector → Flange. For each valve, look at
            // its neighbours; the connectors among them, in turn, lead to
            // the companion flanges.
            var companionFlanges = new HashSet<int>();
            foreach (var v in records.Where(IsValve))
            {
                if (!topology.TryGetValue(v.PnPID, out var vNbrs)) continue;
                foreach (var nbrId in vNbrs)
                {
                    if (!byId.TryGetValue(nbrId, out var nbr)) continue;
                    if (!IsConnector(nbr)) continue;
                    if (!topology.TryGetValue(nbr.PnPID, out var cNbrs)) continue;
                    foreach (var fId in cNbrs)
                    {
                        if (fId == v.PnPID) continue;
                        if (!byId.TryGetValue(fId, out var maybeFlange)) continue;
                        if (IsFlange(maybeFlange)) companionFlanges.Add(fId);
                    }
                }
            }
            if (companionFlanges.Count == 0) return drop;
            foreach (var id in companionFlanges) drop.Add(id);

            // Step 2: each companion flange has a matching gasket + bolt
            // set. We need to know which records are gaskets vs bolts.
            // Python uses the Gasket / BoltSet sub-tables; on managed
            // API we approximate by description / category keywords.
            // It's good enough for the common case — gaskets and bolts
            // have very distinct PartFamilyLongDesc text.
            var gaskets = new List<PartRecord>();
            var bolts = new List<PartRecord>();
            foreach (var r in records)
            {
                if (companionFlanges.Contains(r.PnPID)) continue;
                var cat = r.PartCategory ?? "";
                if (!cat.Equals("Flanges", System.StringComparison.OrdinalIgnoreCase)
                    && !cat.Equals("Fasteners", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsGasket(r)) gaskets.Add(r);
                else if (IsBolt(r)) bolts.Add(r);
            }

            // Fallback buckets keyed by (Spec, NominalDiameter, LineTag).
            // Python uses these when 3D coords are missing or no proximity
            // match is close enough.
            var gasketFallback = new Dictionary<string, List<PartRecord>>();
            var boltFallback = new Dictionary<string, List<PartRecord>>();
            foreach (var g in gaskets) AppendToBucket(gasketFallback, FallbackKey(g), g);
            foreach (var b in bolts) AppendToBucket(boltFallback, FallbackKey(b), b);

            foreach (var fId in companionFlanges)
            {
                if (!byId.TryGetValue(fId, out var f)) continue;
                var fCoords = GetCoords(f);

                int? gasketPick = null;
                int? boltPick = null;

                if (fCoords != null)
                {
                    gasketPick = NearestSameSize(f, gaskets, fCoords.Value, threshold);
                    if (gasketPick.HasValue)
                    {
                        drop.Add(gasketPick.Value);
                        gaskets.RemoveAll(x => x.PnPID == gasketPick.Value);
                    }
                    boltPick = NearestSameSize(f, bolts, fCoords.Value, threshold);
                    if (boltPick.HasValue)
                    {
                        drop.Add(boltPick.Value);
                        bolts.RemoveAll(x => x.PnPID == boltPick.Value);
                    }
                }

                if (!gasketPick.HasValue)
                {
                    var pick = TakeFromBucket(gasketFallback, FallbackKey(f), drop);
                    if (pick.HasValue)
                    {
                        drop.Add(pick.Value);
                        gaskets.RemoveAll(x => x.PnPID == pick.Value);
                    }
                }
                if (!boltPick.HasValue)
                {
                    var pick = TakeFromBucket(boltFallback, FallbackKey(f), drop);
                    if (pick.HasValue)
                    {
                        drop.Add(pick.Value);
                        bolts.RemoveAll(x => x.PnPID == pick.Value);
                    }
                }
            }

            return drop;
        }

        // ---- helpers ----------------------------------------------------

        static bool IsGasket(PartRecord r)
        {
            // Keyword match on multiple descriptive fields — Plant 3D
            // doesn't expose the Gasket sub-table through managed API,
            // so we look for the word in the strings the user sees.
            return DescContains(r, "gasket") || DescContains(r, "прокладк");
        }

        static bool IsBolt(PartRecord r)
        {
            return DescContains(r, "boltset") || DescContains(r, "bolt set")
                || DescContains(r, "stud") || DescContains(r, "болт");
        }

        static bool DescContains(PartRecord r, string needle)
        {
            needle = needle.ToLowerInvariant();
            foreach (var s in new[] { r.ShortDescription, r.PartFamilyLongDesc,
                                       r.PartSizeLongDesc, r.PnPClassName })
            {
                if (!string.IsNullOrEmpty(s)
                    && s.ToLowerInvariant().Contains(needle))
                    return true;
            }
            return false;
        }

        static (double x, double y, double z)? GetCoords(PartRecord r)
        {
            // Prefer Position; fall back to COG (matches Python).
            var x = ParseDouble(r.PositionX) ?? ParseDouble(r.CogX);
            var y = ParseDouble(r.PositionY) ?? ParseDouble(r.CogY);
            var z = ParseDouble(r.PositionZ) ?? ParseDouble(r.CogZ);
            if (x == null || y == null || z == null) return null;
            return (x.Value, y.Value, z.Value);
        }

        static double? ParseDouble(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
                ? v : (double?)null;
        }

        static int? NearestSameSize(PartRecord flange, List<PartRecord> pool,
                                    (double x, double y, double z) fc, double threshold)
        {
            var fNd = ParseDouble(flange.NominalDiameter) ?? 0;
            int? bestId = null;
            var bestDist = threshold;
            foreach (var p in pool)
            {
                var pc = GetCoords(p);
                if (pc == null) continue;
                var pNd = ParseDouble(p.NominalDiameter) ?? 0;
                if (System.Math.Abs(pNd - fNd) > 1e-6) continue;
                var dx = fc.x - pc.Value.x;
                var dy = fc.y - pc.Value.y;
                var dz = fc.z - pc.Value.z;
                var dist = System.Math.Sqrt(dx*dx + dy*dy + dz*dz);
                if (dist < bestDist) { bestDist = dist; bestId = p.PnPID; }
            }
            return bestId;
        }

        static string FallbackKey(PartRecord r)
        {
            var nd = ParseDouble(r.NominalDiameter) ?? 0;
            return $"{nd:F6}|{r.Spec ?? ""}|{r.LineNumberTag ?? ""}";
        }

        static void AppendToBucket(Dictionary<string, List<PartRecord>> buckets,
                                   string key, PartRecord rec)
        {
            if (!buckets.TryGetValue(key, out var list))
                buckets[key] = list = new List<PartRecord>();
            list.Add(rec);
        }

        static int? TakeFromBucket(Dictionary<string, List<PartRecord>> buckets,
                                   string key, HashSet<int> alreadyDropped)
        {
            if (!buckets.TryGetValue(key, out var list)) return null;
            for (var i = 0; i < list.Count; i++)
            {
                var pid = list[i].PnPID;
                if (alreadyDropped.Contains(pid)) continue;
                list.RemoveAt(i);
                return pid;
            }
            return null;
        }
    }
}
