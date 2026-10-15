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

        // LEGACY precomputed PipeBend folding state (tests / old callers).
        // WriteSpecCommand never sets these: with IncludePipeBendsAsPipe
        // on, Build() runs PipeBendFolder itself on the records that passed
        // the user's filters (plus every connector, for traversal) and
        // overrides whatever is here. BendsConsumed holds PnPIDs to skip;
        // ExtraPipeMetres maps the adjacent pipe PnPID to the metres
        // folded into it. ExtraPipeLineTags maps the same pipe PnPID to
        // the LineNumberTag of the contributing bend, so Builder can
        // reroute orphan metres to a sibling pipe on the same line when
        // the original target got filtered out. All null when unused.
        public HashSet<int> BendsConsumed = null;
        public Dictionary<int, double> ExtraPipeMetres = null;
        public Dictionary<int, string> ExtraPipeLineTags = null;
        // Full procurement identity (line, ND, spec, material) of each
        // orphan bend, used to reroute its metres to a same-bore sibling
        // pipe rather than the first pipe on the line. Null when off.
        public Dictionary<int, BendOrphanInfo> ExtraPipeOrphanInfo = null;
        public bool DisableElementGrouping = false;
        public bool IncludePipeBendsAsPipe = false;
        public IDictionary<int, List<int>> Topology = null;
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

        // Doubtful parts / rows found during Build() — rendered by the caller
        // as the trailing «Позиции, требующие проверки» section (see
        // ReviewSection). Problems that used to throw and abort the whole
        // spec (unknown unit, bad bolt count) are localised to one part and
        // land here instead; the part is left out of the main sections only
        // when its QUANTITY cannot be trusted. Mass/description/line caveats
        // keep the part in the main section and just flag it.
        readonly List<ReviewItem> _review = new List<ReviewItem>();
        public IReadOnlyList<ReviewItem> ReviewItems => _review;

        public SpecRowBuilder(SpecBuilderOptions opts = null)
        {
            _opts = opts ?? new SpecBuilderOptions();
        }

        public List<SpecRow> Build(IEnumerable<PartRecord> rows)
        {
            _warnings.Clear();
            DropReasons.Clear();
            _review.Clear();
            // Materialise once — we need two passes (bend-folding then
            // the main aggregation) and ditching the lazy enumeration
            // keeps both consistent.
            var rowList = rows as IList<PartRecord> ?? rows.ToList();

            // Retain the legacy precomputed inputs without mutating them.
            // Production folding below uses only selected records in scope.
            HashSet<int> bendsConsumed = _opts.BendsConsumed;
            Dictionary<int, double> extraPipeMetres = _opts.ExtraPipeMetres == null
                ? null : new Dictionary<int, double>(_opts.ExtraPipeMetres);
            // Reroute metadata for stranded bend metres. Must come from the
            // SAME fold that produced extraPipeMetres: the production path
            // (fold below) never sets the legacy _opts fields, so reading
            // them there disabled the same-bore reroute entirely and every
            // stranded metre ended up in the "не удалось распределить"
            // warning instead of on its sibling pipe.
            Dictionary<int, string> extraPipeLineTags = _opts.ExtraPipeLineTags;
            Dictionary<int, BendOrphanInfo> extraPipeOrphanInfo = _opts.ExtraPipeOrphanInfo;
            Dictionary<int, List<int>> extraPipeBendIds = null;
            HashSet<int> orphanBends = null;

            // null = no status filter; non-null = include only listed statuses.
            HashSet<string> statusSet = null;
            if (_opts.Statuses != null)
                statusSet = new HashSet<string>(_opts.Statuses.Select(s => s == SpecConstants.NullStatus ? "" : s),
                                                StringComparer.OrdinalIgnoreCase);
            HashSet<string> catSet = null;
            if (_opts.Categories != null)
                catSet = new HashSet<string>(_opts.Categories, StringComparer.Ordinal);
            HashSet<string> lineSet = null;
            if (_opts.Lines != null)
                lineSet = new HashSet<string>(_opts.Lines, StringComparer.Ordinal);

            if (_opts.IncludePipeBendsAsPipe)
            {
                // Fold only selected source parts. Connectors stay in the graph
                // for traversal, but excluded bends cannot donate their length.
                var selected = rowList.Where(r =>
                    (statusSet == null || statusSet.Contains(r.Status ?? "")) &&
                    (catSet == null || catSet.Contains(NormalizeCategory(r))) &&
                    (lineSet == null || lineSet.Contains(string.IsNullOrEmpty(r.LineNumberTag)
                        ? SpecConstants.NullLine : r.LineNumberTag)) &&
                    !(_opts.EquipmentOwnedPnPIds?.Contains(r.PnPID) ?? false) &&
                    !(_opts.ExcludeValveCompanions && (_opts.ValveCompanionPnPIds?.Contains(r.PnPID) ?? false)))
                    .ToList();
                var foldInput = selected.Concat(rowList.Where(r =>
                    NormalizeCategory(r) == "P3dConnector")).Distinct().ToList();
                var folded = PipeBendFolder.Fold(foldInput, _opts.Topology);
                bendsConsumed = folded.BendsConsumed;
                extraPipeMetres = folded.ExtraPipeMetres;
                extraPipeLineTags = folded.ExtraPipeLineTags;
                extraPipeOrphanInfo = folded.ExtraPipeOrphanInfo;
                extraPipeBendIds = folded.ExtraPipeBendIds;
                orphanBends = folded.OrphanBends;
            }

            // First record per PnPID, to name bends whose metres got stranded.
            var recordById = new Dictionary<int, PartRecord>();
            foreach (var r in rowList)
                if (!recordById.ContainsKey(r.PnPID)) recordById[r.PnPID] = r;

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
                    // Not a settings exclusion — no picker chip can select
                    // these — so it is a doubt: a real item may be missing.
                    // Honour the line filter (a part on an unselected line
                    // is the user's choice, not a doubt). Named by class,
                    // not description: unclassified structure members would
                    // otherwise make one review row per profile size.
                    var emptyCatLine = string.IsNullOrEmpty(r.LineNumberTag) ? SpecConstants.NullLine : r.LineNumberTag;
                    if (lineSet == null || lineSet.Contains(emptyCatLine))
                        _review.Add(ReviewItem.ForPart(r,
                            "Категория детали не определена — деталь не включена в спецификацию",
                            $"Детали класса «{(string.IsNullOrWhiteSpace(r.PnPClassName) ? "?" : r.PnPClassName.Trim())}» без категории"));
                    continue;
                }

                // Category filter — using Plant 3D's category key (not the
                // localized label). 'category' is guaranteed non-empty here:
                // the empty-category drop above already removed unclassified
                // rows, so there's no NullCategory case to handle — the
                // picker doesn't offer that chip either.
                if (catSet != null && !catSet.Contains(category))
                { Drop($"category-not-selected:{category}"); continue; }

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
                        _review.Add(ReviewItem.ForPart(r,
                            $"Деталь связана с несколькими линиями ({LineLabel(oldLine)}, {LineLabel(rawLine)}) — учтена один раз, в {LineLabel(oldLine)}",
                            BuildName(NormalizeCategory(r), r)));
                    }
                    Drop("dedup-by-pid");
                    continue;
                }
                seenPids[r.PnPID] = rawLine;

                // Filters above ran on the catalogue category (it must match
                // the picker chip). From here on we work with the OUTPUT
                // section: with bend-folding off, every PipeBend becomes a
                // piping detail in pieces, whatever its catalogue category —
                // see CategoryClassifier.SpecSection for the why.
                category = CategoryClassifier.SpecSection(category, r, _opts.IncludePipeBendsAsPipe);
                var isPipe = category == "Pipe";
                var name = BuildName(category, r);
                var typeMark = BuildTypeMark(category, r);
                // Quantise the nominal diameter before it enters the bucket
                // key. The raw catalogue value can arrive as "100" or
                // "100.00" (fine — same double) but also as a slightly
                // different float for the SAME nominal size when it was
                // converted between in/mm in different catalogue revisions
                // (e.g. 3.99999 vs 4.0). Exact double equality in the key
                // would then split one procurement item into two rows.
                // Rounding to 3 decimals collapses those to one key while
                // staying finer than any real nominal-size difference.
                var ndVal = Math.Round(ParseDouble(r.NominalDiameter), 3);
                var nomUnit = (r.NominalUnit ?? "").Trim().ToLowerInvariant();
                var material = (r.Material ?? "").Trim();
                var spec = (r.Spec ?? "").Trim();
                var itemCode = (r.ItemCode ?? "").Trim();
                var manuf = (r.Manufacturer ?? "").Trim();

                // Pipes with zero/empty length are skipped — they show
                // up in the DB but represent stubs the spec shouldn't
                // try to quantify.
                double toMeters = 0;
                double pieces = 1.0;
                if (isPipe)
                {
                    var lenRaw = ParseDouble(r.Length);
                    // Reject non-positive AND non-finite lengths. A NaN length
                    // passes "<= 0" as false and would otherwise propagate into
                    // b.Qty, turning the whole pipe row's quantity into NaN.
                    if (!(lenRaw > 0.0) || double.IsInfinity(lenRaw))
                    {
                        zeroPipes++;
                        seenPids.Remove(r.PnPID);
                        Drop("pipe-zero-length");
                        _review.Add(ReviewItem.ForPart(r,
                            "Труба нулевой или пустой длины — не включена в спецификацию", name));
                        continue;
                    }
                    // Unknown length unit: the length cannot be converted, so
                    // the pipe cannot be quantified. Used to throw and abort
                    // the WHOLE spec; now only this part goes to the review
                    // section (never silently read as millimetres).
                    var lf = LengthFactor(r.LengthUnit);
                    if (!lf.HasValue)
                    {
                        seenPids.Remove(r.PnPID);
                        Drop("pipe-unknown-length-unit");
                        _review.Add(ReviewItem.ForPart(r,
                            $"Неизвестная единица длины «{(r.LengthUnit ?? "").Trim()}» — длина {r.Length} не учтена, труба не включена в спецификацию",
                            name));
                        continue;
                    }
                    toMeters = lf.Value;
                }
                else
                {
                    // Most parts contribute 1 piece, but bolt sets and
                    // multi-pack gaskets carry an embedded piece count
                    // under "Quantity"/"NumBolts"/etc. Use it when present
                    // and >= 1 so a single BoltSet PnPID with 8 bolts
                    // lands as qty=8 in the spec; otherwise keep the
                    // historical behaviour of counting the row as 1 piece.
                    //
                    // Only fasteners (bolt sets, gasket packs) carry a piece
                    // count. PartPropertyReader probes a generic list of names
                    // ("Count", "Quantity", …), and on other families those
                    // mean something else — a flange's "Count" can be its
                    // number of bolt holes, which would turn 1 flange into 8.
                    // So the multiplier (and its validation) applies only to
                    // the Fasteners section / BoltSet class; everything else
                    // is 1 piece per row.
                    //
                    // A missing/invalid count used to throw and abort the
                    // spec. It is still never guessed (a BoltSet without a
                    // count is NOT one bolt) — the part goes to the review
                    // section instead, validated BEFORE it touches a bucket.
                    var isBoltSet = string.Equals(r.PnPClassName, "BoltSet", StringComparison.OrdinalIgnoreCase);
                    var carriesPieceCount = isBoltSet || category == "Fasteners";
                    var qtyText = carriesPieceCount ? r.Quantity : null;
                    var qparsed = ParseNullableDouble(qtyText);
                    string badCount = null;
                    if (isBoltSet && string.IsNullOrWhiteSpace(qtyText))
                        badCount = "Не задано количество болтов в комплекте — комплект не включён в спецификацию";
                    else if (!string.IsNullOrWhiteSpace(qtyText) &&
                        (!qparsed.HasValue || qparsed.Value < 1 || qparsed.Value != Math.Truncate(qparsed.Value)))
                        badCount = $"Некорректное количество «{qtyText.Trim()}» — деталь не включена в спецификацию";
                    if (badCount != null)
                    {
                        seenPids.Remove(r.PnPID);
                        Drop("invalid-piece-count");
                        _review.Add(ReviewItem.ForPart(r, badCount, name));
                        continue;
                    }
                    pieces = qparsed ?? 1.0;
                }

                var key = new BucketKey(category, name, ndVal, nomUnit, material,
                                        typeMark, itemCode, manuf, currentLine,
                                        _opts.DisableElementGrouping ? r.PnPID : 0,
                                        ((r.PressureClass ?? "").Trim(), (r.Facing ?? "").Trim(),
                                         DesignStandardOf(r)),
                                        (ParseNullableDouble(r.WallThickness), (r.Schedule ?? "").Trim()));
                if (!buckets.TryGetValue(key, out var b))
                {
                    b = new Bucket { LineTag = currentLine };
                    buckets[key] = b;
                }
                b.SourceCount++;
                var weightsBefore = b.Weights.Count;
                var massFlagged = false;

                // Collect the identifier of tagged valves/equipment so the
                // "Тип, марка…" column can list every tag in the merged row.
                if (TypeMarkUsesTag(category))
                {
                    var tag = TagOf(r);
                    if (tag != null) b.Tags.Add(tag);
                }

                // Caveats on a part that IS counted: it stays in its main
                // section, the review section only flags it.
                if (string.IsNullOrEmpty(name))
                    _review.Add(ReviewItem.ForPart(r,
                        "Не задано наименование (описание) детали — строка выведена без наименования", null));
                if (r.ReadWarnings != null)
                    foreach (var rw in r.ReadWarnings)
                        _review.Add(ReviewItem.ForPart(r, rw, name));
                if (orphanBends != null && orphanBends.Contains(r.PnPID))
                    _review.Add(ReviewItem.ForPart(r,
                        "Гиб не присоединён к совместимой трубе — учтён отдельной строкой, а не в длине трубы", name));

                if (isPipe)
                {
                    b.Qty += (decimal)ParseDouble(r.Length) * (decimal)toMeters;
                    // Fold accumulated PipeBend lengths into this pipe.
                    // extraPipeMetres is keyed by Pipe PnPID; the bends
                    // themselves were dropped earlier via bendsConsumed,
                    // so this is the only place their length surfaces.
                    if (extraPipeMetres != null
                        && extraPipeMetres.TryGetValue(r.PnPID, out var extra))
                    {
                        b.Qty += (decimal)extra;
                        extraPipeMetres.Remove(r.PnPID);  // accounted for
                    }
                    // Remember this bucket as a fallback target so
                    // bend metres tied to a pipe that itself got
                    // filtered out (status, line, category) can still
                    // land *somewhere* in the same line and not vanish.
                    // Keyed by (LineTag, NominalDiameter, Spec) — the
                    // identity a procurement person would treat as
                    // "the same piping run".
                    // Wall / schedule / pressure class are part of the key:
                    // PipeBendFolder only folds a bend into a pipe that
                    // matches on them (Compatible), so a reroute must not
                    // be looser and park the metres on a sibling with a
                    // different wall — that is a different purchased pipe.
                    //
                    // An ON-mode orphan bend is itself a "Pipe" row, but it
                    // is NOT a valid target: stranded metres belong to the
                    // straight-pipe row, not to an «Изгиб трубы …» line
                    // (which one won depended on read order).
                    if (!r.IsPipeBend)
                        pipeFallbackBuckets[FallbackKey(rawLine, ndVal, spec, material,
                            ParseNullableDouble(r.WallThickness), r.Schedule, r.PressureClass)] = b;
                    // A zero/negative LinearWeight is "not filled in", not a
                    // mass: fall through to Weight / length instead of
                    // reporting the pipe as massless while Weight is set.
                    var lw = ParseNullableDouble(r.LinearWeight);
                    if (lw.HasValue && lw.Value > 0)
                    {
                        // LinearWeight is a per-length quantity (kg/m), so it
                        // takes the linear factor, NOT toKg (which is derived
                        // from WeightUnit and may be a non-linear unit).
                        var linearUnit = string.IsNullOrWhiteSpace(r.LinearWeightUnit) ? r.WeightUnit : r.LinearWeightUnit;
                        if (!string.IsNullOrWhiteSpace(r.LinearWeightUnit) &&
                            !LinearMassUnits.Contains(linearUnit.Trim().ToLowerInvariant()))
                        {
                            // Length is fine, mass is not: keep the pipe, flag
                            // the mass (used to throw for the whole spec).
                            massFlagged = true;
                            _review.Add(ReviewItem.ForPart(r,
                                $"Неизвестная единица погонной массы «{linearUnit.Trim()}» — масса не учтена", name));
                        }
                        else
                            AddWeight(b, lw.Value * LinearWeightFactor(linearUnit), ParseDouble(r.Length) * toMeters);
                    }
                    else
                    {
                        var w = ParseNullableDouble(r.Weight);
                        var l = ParseNullableDouble(r.Length);
                        if (w.HasValue && l.HasValue && l.Value > 0)
                        {
                            var lenM = l.Value * toMeters;
                            var wf = WeightFactor(r.WeightUnit);
                            if (!wf.HasValue)
                            {
                                massFlagged = true;
                                _review.Add(ReviewItem.ForPart(r,
                                    $"Неизвестная единица массы «{(r.WeightUnit ?? "").Trim()}» — масса не учтена", name));
                            }
                            else if (lenM > 0) AddWeight(b, w.Value * wf.Value / lenM, lenM);
                        }
                    }
                }
                else
                {
                    b.Qty += (decimal)pieces;
                    var w = ParseNullableDouble(r.Weight);
                    if (w.HasValue)
                    {
                        // In Plant 3D, the Weight property of multi-piece entities (like BoltSets)
                        // represents the total weight of the set. To get the unit weight of a single
                        // piece ("шт."), we divide the total weight by the quantity in the set.
                        var wf = WeightFactor(r.WeightUnit);
                        if (!wf.HasValue)
                        {
                            massFlagged = true;
                            _review.Add(ReviewItem.ForPart(r,
                                $"Неизвестная единица массы «{(r.WeightUnit ?? "").Trim()}» — масса не учтена", name));
                        }
                        else
                            AddWeight(b, w.Value * wf.Value / pieces, pieces);
                    }
                    // A bend counted in pieces («Детали трубопроводов»: folding
                    // off, or an ON-mode orphan without a length) usually has
                    // no Weight of its own — the catalogue describes it as bent
                    // PIPE. Its mass is then its arc length (CutLength, see
                    // PartPropertyReader) × the pipe's linear mass, when the
                    // bend's own record carries both; otherwise every such bend
                    // landed in the review section as "масса не задана". Never
                    // overrides a real Weight, and never guesses past an
                    // unknown unit. No double count: a bend folded into a pipe
                    // never reaches this branch (its metres take the pipe's
                    // kg/m), and an ON-mode orphan WITH a length is a «Трубы»
                    // row whose mass column is already kg/m.
                    if (r.IsPipeBend && b.Weights.Count == weightsBefore && !massFlagged)
                    {
                        var bendKg = BendMassFromLinearWeight(r);
                        if (bendKg.HasValue) AddWeight(b, bendKg.Value / pieces, pieces);
                    }
                }

                // No usable mass and not already flagged for a unit problem:
                // remembered per part so the review row can name PnPIDs and
                // drawings (the row note only says "X of Y").
                if (b.Weights.Count == weightsBefore && !massFlagged)
                    b.NoMassParts.Add(r);
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
                && extraPipeLineTags != null
                && pipeFallbackBuckets.Count > 0)
            {
                foreach (var kv in extraPipeMetres.ToList())
                {
                    var orphanPipePid = kv.Key;
                    var orphanMetres = kv.Value;
                    if (orphanMetres <= 0) continue;
                    extraPipeLineTags.TryGetValue(orphanPipePid, out var bendLine);
                    if (string.IsNullOrEmpty(bendLine)) continue;

                    // Stage 1 — exact procurement match. The bend carries
                    // its own ND/spec/material; the bucket key is
                    // (line|nd|spec|material). Prefer a sibling pipe of the
                    // SAME bore so a 24" bend's metres never land on a 2"
                    // pipe just because they share a line.
                    Bucket target = null;
                    if (extraPipeOrphanInfo != null
                        && extraPipeOrphanInfo.TryGetValue(orphanPipePid, out var info))
                    {
                        // Round here too: ExtraPipeOrphanInfo can come from a
                        // caller (legacy precomputed path) that did not
                        // quantise ND, while fallbackKey uses the rounded
                        // ndVal. Unrounded 3.99999 vs 4.000 never matched.
                        var exactKey = FallbackKey(bendLine, Math.Round(info.NominalDiameter, 3),
                            info.Spec, info.Material, info.WallThickness, info.Schedule, info.PressureClass);
                        pipeFallbackBuckets.TryGetValue(exactKey, out target);
                    }

                    if (target != null)
                    {
                        target.Qty += (decimal)orphanMetres;
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
                        $"Не удалось распределить {lost.ToString("F2", CultureInfo.InvariantCulture)} м длины изгибов: "
                      + "соответствующих труб нет среди выбранных в спецификации.");
                    // Same metres, per bend, into the review section — with
                    // a known length, since that is exactly what is missing
                    // from the pipe rows.
                    const string stranded = "Длину гиба не удалось отнести к трубе (труба-цель не попала в спецификацию) — метры не учтены";
                    foreach (var kv in extraPipeMetres)
                    {
                        if (!(kv.Value > 0)) continue;
                        List<int> bendIds = null;
                        extraPipeBendIds?.TryGetValue(kv.Key, out bendIds);
                        var bends = (bendIds ?? new List<int>())
                            .Select(id => recordById.TryGetValue(id, out var br) ? br : null)
                            .Where(br => br != null).ToList();
                        if (bends.Count == 0)
                        {
                            // Legacy precomputed path: no bend ids, only the total.
                            string orphanLine = null;
                            extraPipeLineTags?.TryGetValue(kv.Key, out orphanLine);
                            _review.Add(new ReviewItem
                            {
                                Reason = stranded,
                                Description = "Гибы, " + LineLabel(orphanLine ?? SpecConstants.NullLine),
                                Qty = Math.Round(kv.Value, 3), Unit = "м",
                            });
                            continue;
                        }
                        foreach (var br in bends)
                        {
                            var bf = LengthFactor(br.LengthUnit);
                            var bendM = bf.HasValue ? ParseDouble(br.Length) * bf.Value : 0;
                            // 1 µm, like the pipe rows: ReviewSection sums the
                            // bends and rounds UP to 0.1 m, so a per-bend
                            // round to 1 mm could show less than the main
                            // section would have added (300.04 mm → 0.3 m
                            // instead of 0.4 m).
                            _review.Add(ReviewItem.ForPart(br, stranded, BuildName("Pipe", br),
                                bendM > 0 ? Math.Round(bendM, 6) : (double?)null, "м"));
                        }
                    }
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
                        weightWarning = $"Внимание: разный вес {FormatInv(min)}..{FormatInv(max)} кг";
                        weight = b.WeightedMass / b.WeightQuantity;
                        _warnings.Add(
                            $"Разнородные веса в группе '{k.Name}': {FormatInv(min)}..{FormatInv(max)} кг. Использовано среднее: {weight.ToString("F3", CultureInfo.InvariantCulture)} кг.");
                        // Row-level doubt (no single part is "wrong"), so no
                        // PnPID/drawing; the row itself stays in its section.
                        _review.Add(new ReviewItem
                        {
                            Reason = $"Разная масса 1 ед. в одной строке: {FormatInv(min)}..{FormatInv(max)} кг — в спецификации взято среднее {weight.ToString("F3", CultureInfo.InvariantCulture)} кг",
                            Description = string.IsNullOrEmpty(k.Name) ? null : k.Name,
                        });
                    }
                    else
                    {
                        // Weights agree within 5% — use the average rather
                        // than Weights[0]. The first element depends on DWG
                        // read order, which would make the same project emit
                        // a slightly different weight between runs; the
                        // average is order-independent and deterministic.
                        weight = b.WeightedMass / b.WeightQuantity;
                    }
                }
                if (b.Weights.Count < b.SourceCount)
                {
                    var missing = $"Масса не задана или некорректна у {b.SourceCount - b.Weights.Count} из {b.SourceCount} компонентов";
                    weightWarning = string.IsNullOrEmpty(weightWarning) ? missing : weightWarning + "; " + missing;
                    _warnings.Add($"{k.Name}: {missing}.");
                }
                // Parts flagged for an unknown mass unit already have their
                // own review item; NoMassParts holds only the rest.
                foreach (var nm in b.NoMassParts)
                    _review.Add(ReviewItem.ForPart(nm,
                        "Масса не задана или некорректна — в строке спецификации масса не учтена", k.Name));

                double qty;
                string note;
                if (isPipe)
                {
                    // Round up to nearest 0.1 m so the spec never under-
                    // reports the required length, then add a 10% safety
                    // note for procurement.
                    //
                    // Snap to 1 µm FIRST. Plant 3D stores lengths as doubles
                    // with binary noise ("3000.00000000004" mm), and the
                    // double→decimal conversion keeps 15 significant digits —
                    // so a 3.000 m pipe reached Ceiling as 3.00000000000004
                    // and was reported as 3.1 m. 1 µm is far below any real
                    // length difference but far above that noise.
                    var snapped = decimal.Round(b.Qty, 6, MidpointRounding.AwayFromZero);
                    qty = (double)(decimal.Ceiling(snapped * 10m) / 10m);
                    // Apply the reserve to the displayed quantity, then round
                    // upward to one decimal place using decimal arithmetic.
                    var margin = decimal.Ceiling((decimal)qty * 1.1m * 10m) / 10m;
                    var noteParts = new List<string> { $"с учетом 10% запаса: {FormatInv(margin)} м" };
                    if (!string.IsNullOrEmpty(weightWarning)) noteParts.Add(weightWarning);
                    note = string.Join("; ", noteParts);
                }
                else
                {
                    qty = (double)b.Qty;
                    note = weightWarning;
                }

                // Tagged valves/equipment carry their identifier(s) in the
                // bucket. Join them with ", " — that's the "Тип, марка…"
                // value. Otherwise fall back to the key's TypeMark (standard).
                var typeMarkOut = b.Tags.Count > 0
                    ? string.Join(", ", b.Tags)
                    : k.TypeMark;

                result.Add(new SpecRow
                {
                    Category = k.Category,
                    Name = k.Name,
                    TypeMark = typeMarkOut,
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

            result.Sort(CompareRows);
            return result;
        }

        /// <summary>
        /// Display order of spec rows (and therefore of the position
        /// numbers). Must be a TOTAL order: List.Sort is unstable and the
        /// buckets come out of a Dictionary in DWG-read order, so any tie
        /// left here made the same project number its rows differently from
        /// run to run. Names compare "naturally" — digit runs as numbers —
        /// so «Труба 108х4» precedes «Труба 108х10» (ordinal put "10" first).
        /// </summary>
        internal static int CompareRows(SpecRow a, SpecRow b)
        {
            var lc = CompareText(a.LineTag, b.LineTag);
            if (lc != 0) return lc;
            var ai = Array.IndexOf(SpecConstants.CategoryOrder, a.Category);
            var bi = Array.IndexOf(SpecConstants.CategoryOrder, b.Category);
            if (ai < 0) ai = 999;
            if (bi < 0) bi = 999;
            if (ai != bi) return ai.CompareTo(bi);
            // Categories outside CategoryOrder share index 999 — keep each
            // one contiguous so the writer's per-category grouping and the
            // numbering agree.
            var cc = string.CompareOrdinal(a.Category ?? "", b.Category ?? "");
            if (cc != 0) return cc;
            // Within a category, sort by nominal diameter ascending so
            // the procurement table reads small→large. Zero/unknown
            // diameters fall to the end of the category block.
            var aNd = a.NominalDiameterMm > 0 ? a.NominalDiameterMm : double.MaxValue;
            var bNd = b.NominalDiameterMm > 0 ? b.NominalDiameterMm : double.MaxValue;
            var ndc = aNd.CompareTo(bNd);
            if (ndc != 0) return ndc;
            int c;
            if ((c = CompareText(a.Name, b.Name)) != 0) return c;
            if ((c = CompareText(a.TypeMark, b.TypeMark)) != 0) return c;
            if ((c = CompareText(a.ItemCode, b.ItemCode)) != 0) return c;
            if ((c = CompareText(a.Manufacturer, b.Manufacturer)) != 0) return c;
            if ((c = string.CompareOrdinal(a.Unit ?? "", b.Unit ?? "")) != 0) return c;
            if ((c = a.Weight.CompareTo(b.Weight)) != 0) return c;
            if ((c = a.Qty.CompareTo(b.Qty)) != 0) return c;
            return string.CompareOrdinal(a.Note ?? "", b.Note ?? "");
        }

        /// <summary>
        /// Natural comparison (digit runs numerically), with an ordinal
        /// tie-break so "01" and "1" — equal for NaturalCompare — still get
        /// a fixed order. Shared with SpecTemplateWriter's line grouping.
        /// </summary>
        internal static int CompareText(string a, string b)
        {
            a ??= ""; b ??= "";
            var c = VtValueParsing.NaturalCompare(a, b);
            return c != 0 ? c : string.CompareOrdinal(a, b);
        }

        // ---- helpers ----------------------------------------------------

        static readonly Regex WsRun = new Regex(@"\s+", RegexOptions.Compiled);
        static string CollapseWs(string s) => WsRun.Replace(s ?? "", " ").Trim();

        static string NormalizeCategory(PartRecord r)
            => CategoryClassifier.Normalize(r);

        // Numbers that end up in the document (note column) or next to it
        // must use the SAME decimal separator as the «Кол.» / «Масса»
        // columns, which SpecTemplateWriter formats with InvariantCulture
        // (a point). Interpolating with the current culture produced
        // "12.3" in «Кол.» but "13,6 м" in the note on a Russian Windows.
        // The reserve keeps one decimal like the quantity it refers to.
        static string FormatInv(decimal metres)
            => metres.ToString("F1", CultureInfo.InvariantCulture);
        // Masses in notes / review reasons: at most 3 decimals like the
        // «Масса 1 ед.» column. "g" printed raw doubles ("2.3456789123").
        static string FormatInv(double v)
            => v.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>
        /// Procurement identity of a straight-pipe bucket for rerouting
        /// stranded bend metres. Both sides (the pipe registering itself and
        /// the bend looking for a sibling) must build it through here so the
        /// formatting can never drift apart. Invariant formatting: the key is
        /// internal, but culture-dependent "F6" is a needless trap.
        /// </summary>
        static string FallbackKey(string line, double nd, string spec, string material,
                                  double? wall, string schedule, string pressure)
            => string.Join("|",
                line ?? "",
                nd.ToString("F6", CultureInfo.InvariantCulture),
                (spec ?? "").Trim(),
                (material ?? "").Trim(),
                wall.HasValue ? wall.Value.ToString("R", CultureInfo.InvariantCulture) : "",
                (schedule ?? "").Trim(),
                (pressure ?? "").Trim());

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
            => CategoryClassifier.IsConnector(category, r);

        static string BuildName(string category, PartRecord r)
        {
            // Note: for Equipment the Tag (identifier) used to be appended to
            // the name. It now goes into the "Тип, марка…" column instead
            // (see BuildTypeMark / TagOf), so the name is just the description.
            var n = CollapseWs(FirstNonEmpty(r.PartSizeLongDesc, r.PartFamilyLongDesc, r.ShortDescription) ?? "");
            // The name doubles as the grouping key. Bend descriptions carry
            // a direction-dependent Д1/Д2 pair and a noisy computed length,
            // so the same procurement item would otherwise split into
            // several rows — see BendNameNormalizer.
            return r.IsPipeBend ? BendNameNormalizer.Normalize(n) : n;
        }

        // Categories whose identifier (Tag) is written into the
        // "Тип, марка, обозначение документа, опросного листа" column.
        static bool TypeMarkUsesTag(string category)
            => category == "Valves" || category == "Equipment";

        /// <summary>The trimmed Tag of a part, or null when it has none.</summary>
        static string TagOf(PartRecord r)
            => string.IsNullOrWhiteSpace(r.EquipTag) ? null : r.EquipTag.Trim();

        /// <summary>
        /// "Тип, марка…" cell content for a row that did NOT collect tags
        /// (everything except tagged valves/equipment). Falls back to the
        /// design standard. For valves/equipment the tags are aggregated in
        /// the bucket and joined at output, so this returns empty for them.
        /// </summary>
        static string BuildTypeMark(string category, PartRecord r)
        {
            if (TypeMarkUsesTag(category) && TagOf(r) != null)
                // Tag is collected into the bucket and rendered later — keep
                // it OUT of the key so identical parts with different tags
                // still merge into one row.
                return "";

            return DesignStandardOf(r);
        }

        static string DesignStandardOf(PartRecord r)
            => FirstNonEmpty(r.DesignStd, r.CompatibleStandard)?.Trim() ?? "";

        static string FirstNonEmpty(params string[] xs)
        {
            foreach (var x in xs)
                if (!string.IsNullOrWhiteSpace(x)) return x;
            return null;
        }

        static readonly string[] LinearMassUnits = { "kg/m", "кг/м", "lb/ft" };

        /// <summary>
        /// Mass of ONE pipe bend from its own record: arc length (Length is
        /// CutLength for bends) × linear mass. null when either value is
        /// missing / non-positive or a unit is unknown — then the bend stays
        /// "масса не задана" rather than getting a guessed number. Same unit
        /// rules as the pipe rows: LinearWeightUnit when set (kg/m, lb/ft),
        /// otherwise the catalogue's kg/m.
        /// </summary>
        internal static double? BendMassFromLinearWeight(PartRecord r)
        {
            var lw = ParseNullableDouble(r.LinearWeight);
            if (!(lw > 0)) return null;
            var linearUnit = string.IsNullOrWhiteSpace(r.LinearWeightUnit) ? r.WeightUnit : r.LinearWeightUnit;
            if (!string.IsNullOrWhiteSpace(r.LinearWeightUnit)
                && !LinearMassUnits.Contains(r.LinearWeightUnit.Trim().ToLowerInvariant()))
                return null;
            var len = ParseNullableDouble(r.Length);
            var lf = LengthFactor(r.LengthUnit);
            if (!(len > 0) || !lf.HasValue) return null;
            var kg = lw.Value * LinearWeightFactor(linearUnit) * len.Value * lf.Value;
            return double.IsFinite(kg) && kg > 0 ? kg : (double?)null;
        }

        /// <summary>
        /// Add a per-unit weight to the bucket, rejecting values that would
        /// corrupt the aggregate. A NaN slips through any later Average()/Min()
        /// and renders as "NaN кг" in the spec. Non-positive mass is treated
        /// as missing and reported alongside the row, never averaged as zero.
        /// </summary>
        static void AddWeight(Bucket b, double kg, double quantity = 1)
        {
            if (double.IsNaN(kg) || double.IsInfinity(kg) || kg <= 0) return;
            b.Weights.Add(kg);
            b.WeightedMass += kg * quantity;
            b.WeightQuantity += quantity;
        }

        static double ParseDouble(string s)
            => ParseNullableDouble(s) ?? 0.0;

        static double? ParseNullableDouble(string s)
        {
            return SpecNumber.Parse(s);
        }

        // Unit factors return null for an unknown unit — never a silent
        // "assume millimetres/kilograms". They used to throw and abort the
        // whole spec; the caller now localises the problem to one part and
        // sends it to the review section.
        internal static double? LengthFactor(string unit) => LengthUnits.MetresPerUnit(unit);

        static string LineLabel(string rawLine)
            => string.IsNullOrEmpty(rawLine) || rawLine == SpecConstants.NullLine
                ? "без номера линии"
                : $"линия «{rawLine}»";

        static double? WeightFactor(string unit)
        {
            var u = (unit ?? "").Trim().ToLowerInvariant();
            switch (u)
            {
                case "":
                case "kg":
                case "кг":
                case "кг/м":
                case "kg/m": return 1.0;
                case "г": case "g": return 0.001;
                case "lb":   return 0.45359237;
                case "lb/ft":return 1.488163943;
                default: return null;
            }
        }

        /// <summary>
        /// Factor that turns a LINEAR weight (mass per length) into kg/m.
        /// The pipe's WeightUnit describes the lump Weight field, which may
        /// be a non-linear unit ("kg", "lb"); applying that to LinearWeight
        /// (already a per-length quantity) would mis-scale it — e.g. "lb"
        /// would shrink a kg/m value by 0.4536. So we only honour units that
        /// are themselves linear: kg/m → 1, lb/ft → 1.488. Any non-linear or
        /// unknown unit means "the catalogue gave LinearWeight in project
        /// units" (kg/m for the metric projects this plug-in targets), so we
        /// leave it unscaled rather than corrupt it.
        /// </summary>
        static double LinearWeightFactor(string unit)
        {
            var u = (unit ?? "").Trim().ToLowerInvariant();
            switch (u)
            {
                case "кг/м": case "kg/m": return 1.0;
                case "lb/ft": return 1.488163943;
                default:      return 1.0;   // assume already kg/m
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
            public decimal Qty;
            public double WeightedMass, WeightQuantity;
            public int SourceCount;
            public readonly List<double> Weights = new List<double>();
            public string LineTag;
            // Counted parts that contributed no usable mass (review section).
            public readonly List<PartRecord> NoMassParts = new List<PartRecord>();
            // Tags collected for tagged valves/equipment. Identical parts
            // with different identifiers merge into one row; their tags are
            // joined with ", " into the "Тип, марка…" column. The sorted set
            // keeps the order stable and de-duplicates; natural order so the
            // cell reads "КЛ-2, КЛ-10", not "КЛ-10, КЛ-2".
            public readonly SortedSet<string> Tags =
                new SortedSet<string>(Comparer<string>.Create(CompareText));
        }

        readonly struct BucketKey : IEquatable<BucketKey>
        {
            public readonly string Category, Name, NomUnit, Material, TypeMark, ItemCode, Manufacturer, LineTag;
            public readonly double NdVal;
            public readonly int PnPID;
            public readonly (string pressure, string facing, string standard) Identity;
            public readonly (double? wall, string schedule) PipeIdentity;
            public BucketKey(string c, string n, double nd, string nu, string m,
                             string tm, string ic, string mf, string lt, int pnpid = 0,
                             (string, string, string) identity = default, (double?, string) pipeIdentity = default)
            { Category=c; Name=n; NdVal=nd; NomUnit=nu; Material=m; TypeMark=tm; ItemCode=ic; Manufacturer=mf; LineTag=lt; PnPID=pnpid; Identity=identity; PipeIdentity=pipeIdentity; }
            public bool Equals(BucketKey o)
                => Category==o.Category && Name==o.Name && NdVal.Equals(o.NdVal)
                && NomUnit==o.NomUnit && Material==o.Material
                && TypeMark==o.TypeMark && ItemCode==o.ItemCode
                && Manufacturer==o.Manufacturer && LineTag==o.LineTag && PnPID==o.PnPID && Identity==o.Identity && PipeIdentity==o.PipeIdentity;
            public override bool Equals(object o) => o is BucketKey k && Equals(k);
            public override int GetHashCode() => HashCode.Combine(
                Category, Name, NdVal, NomUnit, Material,
                HashCode.Combine(TypeMark, ItemCode, Manufacturer, LineTag, PnPID, Identity, PipeIdentity));
        }
    }
}
