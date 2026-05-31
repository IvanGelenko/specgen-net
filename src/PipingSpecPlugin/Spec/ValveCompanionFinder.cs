using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Discovers parts that should be excluded when the user picks
    /// "Exclude valve companions": walk Valve → Connector → Flange
    /// through the topology graph to find companion flanges, then for
    /// each companion flange pull in the nearest gasket and bolt set by
    /// 3D distance, with a (Spec, NominalDiameter, LineTag) fallback
    /// when coordinates are missing.
    /// </summary>
    public static class ValveCompanionFinder
    {
        public static HashSet<int> Discover(IList<PartRecord> records,
                                            Dictionary<int, List<int>> topology)
        {
            var rawDrop = new HashSet<int>();
            if (records == null || records.Count == 0) return rawDrop;
            if (topology == null) topology = new Dictionary<int, List<int>>();

            // Threshold: 4 inches if the project uses imperial length
            // units, 100 mm otherwise — decided by the first non-null
            // LengthUnit found across the EngineeringItems.
            var firstUnit = records
                .Select(r => (r.LengthUnit ?? "").ToLowerInvariant())
                .FirstOrDefault(u => !string.IsNullOrEmpty(u));
            var isImperial = !string.IsNullOrEmpty(firstUnit)
                          && (firstUnit.Contains("in") || firstUnit.Contains("ft"));
            var threshold = isImperial ? 4.0 : 100.0;

            // Index records by PnPID for cheap lookup during the walk.
            // CollectFromProject sweeps every drawing, so the same part
            // (e.g. a pipe run crossing two sheets) shows up multiple
            // times in records — first occurrence wins, the rest are
            // ignored. Aligns with SpecRowBuilder's own dedup by PnPID.
            var byId = new Dictionary<int, PartRecord>(records.Count);
            foreach (var r in records)
                if (!byId.ContainsKey(r.PnPID)) byId.Add(r.PnPID, r);
            var deduped = byId.Values.ToList();
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
            foreach (var v in deduped.Where(IsValve))
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
            if (companionFlanges.Count == 0) return rawDrop;
            foreach (var id in companionFlanges) rawDrop.Add(id);

            // Step 2: each companion flange has a matching gasket + bolt
            // set. We need to know which records are gaskets vs bolts.
            // We classify by description / category keywords — good enough
            // for the common case, since gaskets and bolts have very
            // distinct PartFamilyLongDesc text.
            var gaskets = new List<PartRecord>();
            var bolts = new List<PartRecord>();
            foreach (var r in deduped)
            {
                if (companionFlanges.Contains(r.PnPID)) continue;
                var cat = r.PartCategory ?? "";
                // Restrict candidates to 'Fasteners' only — earlier we
                // also accepted 'Flanges' here for non-listed gasket
                // variants, but that let real flange items sneak into
                // the fallback pool and get dropped. Fasteners is the
                // canonical home of gaskets and bolt sets in Plant 3D.
                if (!cat.Equals("Fasteners", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsGasket(r)) gaskets.Add(r);
                else if (IsBolt(r)) bolts.Add(r);
            }

            // Fallback buckets keyed by (Spec, NominalDiameter, LineTag),
            // used when 3D coords are missing or no proximity match is
            // close enough.
            var gasketFallback = new Dictionary<string, List<PartRecord>>();
            var boltFallback = new Dictionary<string, List<PartRecord>>();
            foreach (var g in gaskets) AppendToBucket(gasketFallback, FallbackKey(g), g);
            foreach (var b in bolts) AppendToBucket(boltFallback, FallbackKey(b), b);

            // Iterate flanges in a STABLE order (by PnPID). The gasket/bolt
            // pick is greedy — each flange removes its nearest match from
            // the shared pool — so iteration order decides which flange wins
            // a contested gasket when two are near-equidistant. A HashSet's
            // order is undefined, which made the spec wobble between runs on
            // dense joints. Sorting makes the result deterministic.
            var orderedFlanges = companionFlanges.ToList();
            orderedFlanges.Sort();
            foreach (var fId in orderedFlanges)
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
                        rawDrop.Add(gasketPick.Value);
                        gaskets.RemoveAll(x => x.PnPID == gasketPick.Value);
                    }
                    boltPick = NearestSameSize(f, bolts, fCoords.Value, threshold);
                    if (boltPick.HasValue)
                    {
                        rawDrop.Add(boltPick.Value);
                        bolts.RemoveAll(x => x.PnPID == boltPick.Value);
                    }
                }

                if (!gasketPick.HasValue)
                {
                    var pick = TakeFromBucket(gasketFallback, FallbackKey(f), rawDrop);
                    if (pick.HasValue)
                    {
                        rawDrop.Add(pick.Value);
                        gaskets.RemoveAll(x => x.PnPID == pick.Value);
                    }
                }
                if (!boltPick.HasValue)
                {
                    var pick = TakeFromBucket(boltFallback, FallbackKey(f), rawDrop);
                    if (pick.HasValue)
                    {
                        rawDrop.Add(pick.Value);
                        bolts.RemoveAll(x => x.PnPID == pick.Value);
                    }
                }
            }

            // Final safety pass — drop only well-typed parts. Earlier
            // builds had bugs where Pipe records sneaked into the
            // bolt/gasket fallback buckets (PartFamilyLongDesc happened
            // to mention 'bolt' or NominalDiameter matched a flange),
            // and entire pipe runs vanished from the spec. Restrict
            // exclusions to categories that genuinely belong to a
            // valve-companion tuple.
            var safe = new HashSet<int>();
            foreach (var id in rawDrop)
            {
                if (!byId.TryGetValue(id, out var r)) continue;
                var cat = r.PartCategory ?? "";
                if (cat.Equals("Pipe", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (cat.Equals("Fittings", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (cat.Equals("Valves", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (cat.Equals("Equipment", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                safe.Add(id);
            }
            return safe;
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
            // Prefer Position; fall back to COG when it isn't populated.
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
                if (dist >= bestDist) continue;

                // Closer than the running best — but if it's an exact tie
                // with the current pick, prefer the lower PnPID so the
                // result is stable regardless of pool order.
                bool isTie = bestId.HasValue
                             && System.Math.Abs(dist - bestDist) <= 1e-9;
                if (isTie && p.PnPID >= bestId.Value) continue;

                bestDist = dist;
                bestId = p.PnPID;
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
