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

            // Proximity radius in millimetres. Coordinates stay in DRAWING
            // units (see GetCoords); the radius is converted to the flange's
            // drawing units per lookup (ThresholdInDrawingUnits).
            const double thresholdMm = 100.0;

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
                => string.Equals(CategoryClassifier.Normalize(r), "Flanges",
                                 System.StringComparison.OrdinalIgnoreCase);
            bool IsValve(PartRecord r)
                => string.Equals(CategoryClassifier.Normalize(r), "Valves",
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
                var cat = CategoryClassifier.Normalize(r);
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
                    var threshold = ThresholdInDrawingUnits(f, thresholdMm);
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

                if (!gasketPick.HasValue && fCoords == null)
                {
                    var pick = TakeFromBucket(gasketFallback, FallbackKey(f), rawDrop);
                    if (pick.HasValue)
                    {
                        rawDrop.Add(pick.Value);
                        gaskets.RemoveAll(x => x.PnPID == pick.Value);
                    }
                }
                if (!boltPick.HasValue && fCoords == null)
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
                var cat = CategoryClassifier.Normalize(r);
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
            // Raw drawing-unit coordinates. They used to be multiplied by a
            // factor derived from the part's catalogue LengthUnit, but that
            // unit describes catalogue dimensions (pipe length, …), not the
            // model space the Position X/Y/Z values live in: a metric
            // catalogue in an inch drawing (or the reverse) scaled the
            // distance by 25.4 and the 100 mm radius became meaningless.
            // Candidates are always compared within one drawing (FallbackKey
            // carries SourceDwgPath), so raw coordinates are comparable.
            return (x.Value, y.Value, z.Value);
        }

        /// <summary>
        /// The millimetre radius expressed in the drawing units of
        /// <paramref name="r"/>'s DWG. Unknown/unitless drawings are taken as
        /// millimetres — the Plant 3D metric default; an imperial project
        /// always sets INSUNITS (inches/feet), so it is handled exactly.
        /// </summary>
        internal static double ThresholdInDrawingUnits(PartRecord r, double thresholdMm)
        {
            var mmPerUnit = r.DrawingMmPerUnit;
            if (mmPerUnit == null || !(mmPerUnit.Value > 0) || !double.IsFinite(mmPerUnit.Value))
                return thresholdMm;
            return thresholdMm / mmPerUnit.Value;
        }

        static double? ParseDouble(string s)
        {
            return SpecNumber.Parse(s);
        }

        static int? NearestSameSize(PartRecord flange, List<PartRecord> pool,
                                    (double x, double y, double z) fc, double threshold)
        {
            var fNd = ParseDouble(flange.NominalDiameter) ?? 0;
            int? bestId = null;
            var bestDist = double.MaxValue;
            const double tie = 1e-9;
            foreach (var p in pool)
            {
                if (FallbackKey(p) != FallbackKey(flange)) continue;
                var pc = GetCoords(p);
                if (pc == null) continue;
                var pNd = ParseDouble(p.NominalDiameter) ?? 0;
                if (System.Math.Abs(pNd - fNd) > 1e-6) continue;
                var dx = fc.x - pc.Value.x;
                var dy = fc.y - pc.Value.y;
                var dz = fc.z - pc.Value.z;
                var dist = System.Math.Sqrt(dx*dx + dy*dy + dz*dz);

                // Only candidates within the proximity threshold qualify.
                if (dist > threshold) continue;

                if (bestId == null)
                {
                    bestDist = dist; bestId = p.PnPID; continue;
                }
                // Strictly closer wins; an exact tie picks the lower PnPID so
                // the result is independent of pool (DWG read) order. The old
                // code rejected equidistant candidates before this tie-break
                // could run, making the pick depend on read order.
                if (dist < bestDist - tie
                    || (System.Math.Abs(dist - bestDist) <= tie && p.PnPID < bestId.Value))
                {
                    bestDist = dist; bestId = p.PnPID;
                }
            }
            return bestId;
        }

        static string FallbackKey(PartRecord r)
        {
            var nd = ParseDouble(r.NominalDiameter) ?? 0;
            return $"{(r.SourceDwgPath ?? "").ToUpperInvariant()}|{nd:F6}|{r.NominalUnit ?? ""}|{r.Spec ?? ""}|{r.LineNumberTag ?? ""}";
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
            // An ambiguous fallback cannot prove ownership. Keep those parts.
            if (list.Count(r => !alreadyDropped.Contains(r.PnPID)) != 1) return null;
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
