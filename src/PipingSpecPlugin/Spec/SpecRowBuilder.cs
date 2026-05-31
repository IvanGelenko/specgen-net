using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PipingSpecPlugin.Spec
{
    public class SpecBuilderOptions
    {
        // For Statuses/Categories/Lines, null = no filter (include all).
        // For Categories use SpecConstants.NullCategory to include rows
        // with empty PartCategory; for Lines use SpecConstants.NullLine
        // to include rows not attached to any LineGroup.
        public IEnumerable<string> Statuses = null;
        public IEnumerable<string> Categories = null;
        public IEnumerable<string> Lines = null;
        public bool GroupByLine = false;
        public bool ExcludeEquipment = true;
        public bool ExcludeConnectors = true;
        // Default false on the options object itself; the picker and saved
        // settings drive the real value (which defaults to true). When on,
        // the caller must also supply ValveCompanionPnPIds from
        // ValveCompanionFinder.Discover.
        public bool ExcludeValveCompanions = false;

        // PnPIDs of parts that are owned by equipment (nozzles, manways,
        // internal supports) and must therefore never appear in the spec
        // — they belong to the vessel/pump, not the piping. Populated by
        // PartReader; empty if reader didn't see any equipment.
        public ISet<int> EquipmentOwnedPnPIds = null;

        // PnPIDs flagged by ValveCompanionFinder as the matching flange/
        // gasket/bolt set of a valve. Only excluded when
        // ExcludeValveCompanions is also true.
        public ISet<int> ValveCompanionPnPIds = null;

        // PipeBend folding state — computed by PipeBendFolder against
        // the project-wide records, then passed in here. BendsConsumed
        // holds PnPIDs to skip; ExtraPipeMetres maps the adjacent pipe
        // PnPID to the metres folded into it. ExtraPipeLineTags maps
        // the same pipe PnPID to the LineNumberTag of the contributing
        // bend, so Builder can reroute orphan metres to a sibling pipe
        // on the same line when the original target got filtered out.
        // All null when the feature is off.
        public HashSet<int> BendsConsumed = null;
        public Dictionary<int, double> ExtraPipeMetres = null;
        public Dictionary<int, string> ExtraPipeLineTags = null;
        public bool DisableElementGrouping = false;
    }

    /// <summary>
    /// Aggregates raw PartRecords into SpecRows ready for the docx
    /// template. Handles the dedup-by-PnPID step, the pipe length
    /// conversion, the weight-variance warning and the 10% pipe margin
    /// note.
    /// </summary>
    public class SpecRowBuilder
    {
        readonly SpecBuilderOptions _opts;
        readonly List<string> _warnings = new List<string>();
        public IReadOnlyList<string> Warnings => _warnings;

        // Diagnostic: counts of rows dropped at each gate. Populated by
        // Build(); WRITESPEC prints it so users can see exactly which
        // filter ate their parts.
        public Dictionary<string, int> DropReasons { get; } = new();
        void Drop(string reason)
        {
            DropReasons.TryGetValue(reason, out var c);
            DropReasons[reason] = c + 1;
        }

        public SpecRowBuilder(SpecBuilderOptions opts = null)
        {
            _opts = opts ?? new SpecBuilderOptions();
        }

        public List<SpecRow> Build(IEnumerable<PartRecord> rows)
        {
            // Materialise once — we need two passes (bend-folding then
            // the main aggregation) and ditching the lazy enumeration
            // keeps both consistent.
            var rowList = rows as IList<PartRecord> ?? rows.ToList();

            // Phase 0: pull pre-computed bend-folding result from options.
            // PipeBendFolder runs on the *project-wide* PartRecord set
            // (before any scope filter narrows it to the active drawing),
            // because a bend can be paired with a pipe that lives in a
            // different DWG and the scope filter would otherwise hide it.
            HashSet<int> bendsConsumed = _opts.BendsConsumed;
            Dictionary<int, double> extraPipeMetres = _opts.ExtraPipeMetres;

            // null = no status filter; non-null = include only listed statuses.
            HashSet<string> statusSet = null;
            if (_opts.Statuses != null)
                statusSet = new HashSet<string>(_opts.Statuses,
                                                StringComparer.OrdinalIgnoreCase);
            HashSet<string> catSet = null;
            if (_opts.Categories != null)
                catSet = new HashSet<string>(_opts.Categories, StringComparer.Ordinal);
            HashSet<string> lineSet = null;
            if (_opts.Lines != null)
                lineSet = new HashSet<string>(_opts.Lines, StringComparer.Ordinal);

            var buckets = new Dictionary<BucketKey, Bucket>();
            var seenPids = new Dictionary<int, string>();   // pid -> first-seen LineTag
            var multiLine = new HashSet<int>();
            var zeroPipes = 0;
            // Index of created pipe buckets keyed by their procurement
            // identity (line, ND, spec, material) so we can park orphan
            // bend metres into a sibling pipe when the original target
            // pipe got filtered out.
            var pipeFallbackBuckets = new Dictionary<string, Bucket>(
                System.StringComparer.Ordinal);
            // Need access to lineTag from inside the isPipe block where
            // raw line is captured in 'rawLine'; assign here to a
            // mutable string used by the inner closure-style assignment.
            // (No closure; the variable rawLine is in scope from the
            // earlier dedup-by-PnPID block above.)

            foreach (var r in rowList)
            {
                if (bendsConsumed != null && bendsConsumed.Contains(r.PnPID))
                { Drop("pipe-bend-folded"); continue; }

                if (statusSet != null && !statusSet.Contains(r.Status ?? ""))
                { Drop("status"); continue; }

                if (_opts.EquipmentOwnedPnPIds != null
                    && _opts.EquipmentOwnedPnPIds.Contains(r.PnPID))
                { Drop("equipment-owned"); continue; }

                if (_opts.ExcludeValveCompanions
                    && _opts.ValveCompanionPnPIds != null
                    && _opts.ValveCompanionPnPIds.Contains(r.PnPID))
                { Drop("valve-companion"); continue; }

                var category = NormalizeCategory(r);
                if (_opts.ExcludeEquipment && category == "Equipment")
                { Drop("equipment-category"); continue; }
                if (_opts.ExcludeConnectors && IsConnector(category, r))
                { Drop("connector-keyword"); continue; }
                // Equipment nozzles ("штуцера") belong to the vessel,
                // not to the piping spec. Plant 3D files them under
                // PartCategory='Nozzles' (the diagrammed equipment
                // sub-tree in the data manager), or surfaces the same
                // sub-class through PnPClassName containing 'nozzle'
                // (e.g. FlangedNozzle, SprayNozzle). Always excluded
                // regardless of the user's check-boxes — they're never
                // part of the procurement list.
                if (IsEquipmentNozzle(category, r))
                { Drop("equipment-nozzle"); continue; }
                if (string.IsNullOrEmpty(category))
                {
                    // Track which PnPClassName ended up here — most of
                    // these are weld variants under names like Buttweld /
                    // UniversalSpec. Knowing the class name lets us widen
                    // the NormalizeCategory P3dConnector heuristic.
                    Drop("empty-category:" + (r.PnPClassName ?? "?"));
                    continue;
                }

                // Category filter — using Plant 3D's category key (not the
                // localized label). NullCategory sentinel lets the user
                // include rows where PartCategory is empty.
                if (catSet != null)
                {
                    var catKey = string.IsNullOrEmpty(category) ? SpecConstants.NullCategory : category;
                    if (!catSet.Contains(catKey))
                    { Drop($"category-not-selected:{catKey}"); continue; }
                }

                // Dedup by PnPID. A part can be linked to multiple lines
                // (multi-membership in P3dLineGroupPartRelationship); we
                // count it once but warn the user.
                var rawLine = string.IsNullOrEmpty(r.LineNumberTag)
                    ? SpecConstants.NullLine
                    : r.LineNumberTag;
                if (lineSet != null && !lineSet.Contains(rawLine))
                { Drop($"line-not-selected:{rawLine}"); continue; }
                var currentLine = _opts.GroupByLine ? rawLine : "";
                if (seenPids.TryGetValue(r.PnPID, out var oldLine))
                {
                    if (oldLine != rawLine && !multiLine.Contains(r.PnPID))
                    {
                        multiLine.Add(r.PnPID);
                        _warnings.Add(_opts.GroupByLine
                            ? $"Компонент PnPID {r.PnPID} связан с несколькими линиями: '{oldLine}' и '{rawLine}'. Учтен только в '{oldLine}'."
                            : $"Компонент PnPID {r.PnPID} связан с несколькими линиями ('{oldLine}', '{rawLine}', ...). Учтен один раз.");
                    }
                    Drop("dedup-by-pid");
                    continue;
                }
                seenPids[r.PnPID] = rawLine;

                var isPipe = category == "Pipe";
                var name = BuildName(category, r);
                var typeMark = BuildTypeMark(r);
                var ndVal = ParseDouble(r.NominalDiameter);
                var nomUnit = (r.NominalUnit ?? "").Trim().ToLowerInvariant();
                var material = (r.Material ?? "").Trim();
                var spec = (r.Spec ?? "").Trim();
                var itemCode = (r.ItemCode ?? "").Trim();
                var manuf = (r.Manufacturer ?? "").Trim();

                // Pipes with zero/empty length are skipped — they show
                // up in the DB but represent stubs the spec shouldn't
                // try to quantify.
                if (isPipe)
                {
                    var lenRaw = ParseDouble(r.Length);
                    if (lenRaw <= 0.0)
                    {
                        zeroPipes++;
                        seenPids.Remove(r.PnPID);
                        Drop("pipe-zero-length");
                        continue;
                    }
                }

                var toMeters = LengthFactor(r.LengthUnit);
                var toKg = WeightFactor(r.WeightUnit);

                var key = new BucketKey(category, name, ndVal, nomUnit, material,
                                        typeMark, itemCode, manuf, currentLine,
                                        _opts.DisableElementGrouping ? r.PnPID : 0);
                if (!buckets.TryGetValue(key, out var b))
                {
                    b = new Bucket { LineTag = currentLine };
                    buckets[key] = b;
                }

                if (isPipe)
                {
                    b.Qty += ParseDouble(r.Length) * toMeters;
                    // Fold accumulated PipeBend lengths into this pipe.
                    // extraPipeMetres is keyed by Pipe PnPID; the bends
                    // themselves were dropped earlier via bendsConsumed,
                    // so this is the only place their length surfaces.
                    if (extraPipeMetres != null
                        && extraPipeMetres.TryGetValue(r.PnPID, out var extra))
                    {
                        b.Qty += extra;
                        extraPipeMetres.Remove(r.PnPID);  // accounted for
                    }
                    // Remember this bucket as a fallback target so
                    // bend metres tied to a pipe that itself got
                    // filtered out (status, line, category) can still
                    // land *somewhere* in the same line and not vanish.
                    // Keyed by (LineTag, NominalDiameter, Spec) — the
                    // identity a procurement person would treat as
                    // "the same piping run".
                    var fallbackKey = $"{rawLine}|{ndVal:F6}|{spec}|{material}";
                    pipeFallbackBuckets[fallbackKey] = b;
                    var lw = ParseNullableDouble(r.LinearWeight);
                    if (lw.HasValue)
                    {
                        b.Weights.Add(lw.Value * toKg);
                    }
                    else
                    {
                        var w = ParseNullableDouble(r.Weight);
                        var l = ParseNullableDouble(r.Length);
                        if (w.HasValue && l.HasValue && l.Value > 0)
                        {
                            var lenM = l.Value * toMeters;
                            if (lenM > 0) b.Weights.Add(w.Value * toKg / lenM);
                        }
                    }
                }
                else
                {
                    // Most parts contribute 1 piece, but bolt sets and
                    // multi-pack gaskets carry an embedded piece count
                    // under "Quantity"/"NumBolts"/etc. Use it when present
                    // and >= 1 so a single BoltSet PnPID with 8 bolts
                    // lands as qty=8 in the spec; otherwise keep the
                    // historical behaviour of counting the row as 1 piece.
                    var qparsed = ParseNullableDouble(r.Quantity);
                    var pieces = qparsed.HasValue && qparsed.Value >= 1
                        ? qparsed.Value : 1.0;
                    b.Qty += pieces;
                    var w = ParseNullableDouble(r.Weight);
                    if (w.HasValue)
                    {
                        // In Plant 3D, the Weight property of multi-piece entities (like BoltSets)
                        // represents the total weight of the set. To get the unit weight of a single
                        // piece ("шт."), we divide the total weight by the quantity in the set.
                        b.Weights.Add(w.Value * toKg / pieces);
                    }
                }
            }

            if (zeroPipes > 0)
                _warnings.Add($"Исключено {zeroPipes} сегментов труб с нулевой или пустой длиной.");

            // Post-pass: bend metres tied to a Pipe PnPID that was
            // filtered out (its own line wasn't selected, its status
            // didn't match, etc) end up stranded in extraPipeMetres.
            // Reroute each leftover to a same-line pipe bucket if we
            // built one; otherwise warn the user that some length
            // couldn't be placed.
            if (extraPipeMetres != null && extraPipeMetres.Count > 0
                && _opts.ExtraPipeLineTags != null
                && pipeFallbackBuckets.Count > 0)
            {
                foreach (var kv in extraPipeMetres.ToList())
                {
                    var orphanPipePid = kv.Key;
                    var orphanMetres = kv.Value;
                    if (orphanMetres <= 0) continue;
                    _opts.ExtraPipeLineTags.TryGetValue(orphanPipePid, out var bendLine);
                    if (string.IsNullOrEmpty(bendLine)) continue;

                    // Try to land the metres on any pipe bucket sharing
                    // the bend's LineTag. The bucket key is
                    // (line|nd|spec|material) — exact ND/spec match is
                    // ideal but unavailable, so we match by line prefix.
                    Bucket target = null;
                    foreach (var pkv in pipeFallbackBuckets)
                    {
                        if (pkv.Key.StartsWith(
                                bendLine + "|",
                                System.StringComparison.Ordinal))
                        { target = pkv.Value; break; }
                    }
                    if (target != null)
                    {
                        target.Qty += orphanMetres;
                        extraPipeMetres.Remove(orphanPipePid);
                    }
                }
            }

            if (extraPipeMetres != null && extraPipeMetres.Count > 0)
            {
                var lost = extraPipeMetres.Values.Sum();
                if (lost > 0.0001)
                {
                    _warnings.Add(
                        $"Не удалось распределить {lost:F2} м длины изгибов: "
                      + "соответствующих труб нет среди выбранных в спецификации.");
                }
            }

            var result = new List<SpecRow>();
            foreach (var kv in buckets)
            {
                var k = kv.Key;
                var b = kv.Value;
                var isPipe = k.Category == "Pipe";

                double weight = 0;
                string weightWarning = "";
                if (b.Weights.Count > 0)
                {
                    var min = b.Weights.Min();
                    var max = b.Weights.Max();
                    if (min > 0 && (max - min) / min > 0.05)
                    {
                        weightWarning = $"Внимание: разный вес {min:g}..{max:g} кг";
                        weight = b.Weights.Average();
                        _warnings.Add(
                            $"Разнородные веса в группе '{k.Name}': {min:g}..{max:g} кг. Использовано среднее: {weight:F3} кг.");
                    }
                    else
                    {
                        weight = b.Weights[0];
                    }
                }

                double qty;
                string note;
                if (isPipe)
                {
                    // Round up to nearest 0.1 m so the spec never under-
                    // reports the required length, then add a 10% safety
                    // note for procurement.
                    qty = Math.Ceiling(b.Qty * 10.0) / 10.0;
                    var margin = (int)Math.Ceiling(qty * 1.1);
                    var noteParts = new List<string> { $"с учетом 10% запаса: {margin} м" };
                    if (!string.IsNullOrEmpty(weightWarning)) noteParts.Add(weightWarning);
                    note = string.Join("; ", noteParts);
                }
                else
                {
                    qty = b.Qty;
                    note = weightWarning;
                }

                result.Add(new SpecRow
                {
                    Category = k.Category,
                    Name = k.Name,
                    TypeMark = k.TypeMark,
                    ItemCode = k.ItemCode,
                    Manufacturer = k.Manufacturer,
                    Unit = isPipe ? "м" : "шт.",
                    Qty = Math.Round(qty, 2),
                    Weight = Math.Round(weight, 3),
                    Note = note,
                    LineTag = k.LineTag,
                    NominalDiameterMm = NominalDiameterToMm(k.NdVal, k.NomUnit),
                });
            }

            result.Sort((a, b) =>
            {
                var lc = string.Compare(a.LineTag ?? "", b.LineTag ?? "", StringComparison.Ordinal);
                if (lc != 0) return lc;
                var ai = Array.IndexOf(SpecConstants.CategoryOrder, a.Category);
                var bi = Array.IndexOf(SpecConstants.CategoryOrder, b.Category);
                if (ai < 0) ai = 999;
                if (bi < 0) bi = 999;
                if (ai != bi) return ai.CompareTo(bi);
                // Within a category, sort by nominal diameter ascending so
                // the procurement table reads small→large. Zero/unknown
                // diameters fall to the end of the category block.
                var aNd = a.NominalDiameterMm > 0 ? a.NominalDiameterMm : double.MaxValue;
                var bNd = b.NominalDiameterMm > 0 ? b.NominalDiameterMm : double.MaxValue;
                var ndc = aNd.CompareTo(bNd);
                if (ndc != 0) return ndc;
                return string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.Ordinal);
            });
            return result;
        }

        // ---- helpers ----------------------------------------------------

        static readonly Regex WsRun = new Regex(@"\s+", RegexOptions.Compiled);
        static string CollapseWs(string s) => WsRun.Replace(s ?? "", " ").Trim();

        static string NormalizeCategory(PartRecord r)
        {
            // Supports are detected via the iso symbol definition substring;
            // everything else uses PartCategory as-is.
            if (!string.IsNullOrEmpty(r.ContentIsoSymbolDefinition)
                && r.ContentIsoSymbolDefinition.IndexOf("TYPE=SUPPORT", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Supports";
            var cat = r.PartCategory ?? "";
            if (!string.IsNullOrEmpty(cat)) return cat;

            // PartCategory is empty for several Plant 3D part families.
            // Promote each to a synthetic category so they get their
            // own picker chip — otherwise the user can only include
            // them through 'Без категории', which lumps unrelated
            // things together.
            var cls = (r.PnPClassName ?? "").ToLowerInvariant();
            if (cls == "p3dconnector" || cls == "buttweld"
                || cls == "universal" || cls == "weldspec"
                || cls.Contains("weld") || cls.Contains("connector"))
                return "P3dConnector";
            if (cls == "vessel" || cls == "pump"
                || cls.Contains("equipment") || cls.Contains("vessel")
                || cls.Contains("pump") || cls.Contains("tank")
                || cls.Contains("exchanger") || cls.Contains("compressor"))
                return "Equipment";
            return "";
        }

        /// <summary>
        /// Equipment-attached nozzles ("штуцера") — vessel sub-objects
        /// the user sees as 'Nozzle, flanged' rows but which belong with
        /// the equipment, not in the piping procurement spec. Plant 3D
        /// files them under PartCategory='Nozzles' (its own branch in
        /// the data manager) and under PnPClassName containing 'nozzle'
        /// (FlangedNozzle, SprayNozzle, etc.).
        /// </summary>
        static bool IsEquipmentNozzle(string category, PartRecord r)
        {
            if (string.Equals(category, "Nozzles", StringComparison.OrdinalIgnoreCase))
                return true;
            var cls = (r.PnPClassName ?? "").ToLowerInvariant();
            if (cls.Contains("nozzle")) return true;
            return false;
        }

        static bool IsConnector(string category, PartRecord r)
        {
            // P3dConnector-class rows are connectors by class. The
            // keyword sweep below still runs for Fasteners (where some
            // weld-stud variants land) and for any rare empty-category
            // leftovers.
            if (category == "P3dConnector") return true;
            if (!(string.IsNullOrEmpty(category)
                  || category == SpecConstants.NullCategory
                  || category == "Fasteners"))
                return false;
            foreach (var fld in new[] { r.ShortDescription, r.PartSizeLongDesc, r.PartFamilyLongDesc })
            {
                var t = (fld ?? "").ToLowerInvariant();
                foreach (var kw in SpecConstants.ConnectorKeywords)
                    if (t.Contains(kw)) return true;
            }
            return false;
        }

        static string BuildName(string category, PartRecord r)
        {
            if (category == "Equipment" && !string.IsNullOrWhiteSpace(r.EquipTag))
            {
                var tag = r.EquipTag.Trim();
                var desc = FirstNonEmpty(r.PartSizeLongDesc, r.PartFamilyLongDesc, r.ShortDescription) ?? "";
                desc = desc.Trim();
                string name;
                if (!string.IsNullOrEmpty(desc))
                    name = desc.Contains(tag) ? desc : $"{desc} {tag}";
                else
                    name = tag;
                return CollapseWs(name);
            }
            var n = FirstNonEmpty(r.PartSizeLongDesc, r.PartFamilyLongDesc, r.ShortDescription) ?? "";
            return CollapseWs(n);
        }

        static string BuildTypeMark(PartRecord r)
        {
            var std = !string.IsNullOrWhiteSpace(r.DesignStd) ? r.DesignStd.Trim() : "";
            if (string.IsNullOrEmpty(std))
                std = !string.IsNullOrWhiteSpace(r.CompatibleStandard) ? r.CompatibleStandard.Trim() : "";
            return std;
        }

        static string FirstNonEmpty(params string[] xs)
        {
            foreach (var x in xs)
                if (!string.IsNullOrWhiteSpace(x)) return x;
            return null;
        }

        static double ParseDouble(string s)
            => ParseNullableDouble(s) ?? 0.0;

        static double? ParseNullableDouble(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            // Plant 3D writes numbers with '.' decimal regardless of
            // Windows locale, so force invariant parsing.
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                return v;
            return null;
        }

        static double LengthFactor(string unit)
        {
            var u = (unit ?? "").Trim().ToLowerInvariant();
            switch (u)
            {
                case "":
                case "mm": return 0.001;
                case "cm": return 0.01;
                case "m":  return 1.0;
                case "in": return 0.0254;
                case "ft": return 0.3048;
                default:   return 0.001;
            }
        }

        static double WeightFactor(string unit)
        {
            var u = (unit ?? "").Trim().ToLowerInvariant();
            switch (u)
            {
                case "":
                case "kg":
                case "kg/m": return 1.0;
                case "g":    return 0.001;
                case "lb":   return 0.45359237;
                case "lb/ft":return 1.488163943;
                default:     return 1.0;
            }
        }

        /// <summary>
        /// Convert a (value, unit) pair from PartRecord.NominalDiameter /
        /// NominalUnit into millimetres. Used to sort spec rows within a
        /// category by physical size. Plant 3D typically writes either
        /// "mm" or "in"; anything unrecognised falls through to mm so the
        /// row still sorts numerically rather than vanishing to the tail.
        /// </summary>
        static double NominalDiameterToMm(double nd, string nomUnit)
        {
            if (nd <= 0) return 0;
            var u = (nomUnit ?? "").Trim().ToLowerInvariant();
            switch (u)
            {
                case "in":
                case "\"":
                case "inch":
                case "inches": return nd * 25.4;
                case "cm":     return nd * 10.0;
                case "m":      return nd * 1000.0;
                case "":
                case "mm":
                default:       return nd;
            }
        }

        sealed class Bucket
        {
            public double Qty;
            public readonly List<double> Weights = new List<double>();
            public string LineTag;
        }

        readonly struct BucketKey : IEquatable<BucketKey>
        {
            public readonly string Category, Name, NomUnit, Material, TypeMark, ItemCode, Manufacturer, LineTag;
            public readonly double NdVal;
            public readonly int PnPID;
            public BucketKey(string c, string n, double nd, string nu, string m,
                             string tm, string ic, string mf, string lt, int pnpid = 0)
            { Category=c; Name=n; NdVal=nd; NomUnit=nu; Material=m; TypeMark=tm; ItemCode=ic; Manufacturer=mf; LineTag=lt; PnPID=pnpid; }
            public bool Equals(BucketKey o)
                => Category==o.Category && Name==o.Name && NdVal.Equals(o.NdVal)
                && NomUnit==o.NomUnit && Material==o.Material
                && TypeMark==o.TypeMark && ItemCode==o.ItemCode
                && Manufacturer==o.Manufacturer && LineTag==o.LineTag && PnPID==o.PnPID;
            public override bool Equals(object o) => o is BucketKey k && Equals(k);
            public override int GetHashCode() => HashCode.Combine(
                Category, Name, NdVal, NomUnit, Material,
                HashCode.Combine(TypeMark, ItemCode, Manufacturer, LineTag, PnPID));
        }
    }
}
