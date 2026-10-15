using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Procurement identity of a bend whose metres were folded into a
    /// target pipe. If that pipe later gets filtered out, SpecRowBuilder
    /// uses this to reroute the stranded metres to a *sibling* pipe with
    /// the SAME diameter / spec / material on the same line — not just the
    /// first pipe it finds on the line (which could be a different bore).
    /// </summary>
    public struct BendOrphanInfo
    {
        public string LineTag;          // SpecConstants.NullLine when untagged
        public double NominalDiameter;  // parsed ND, for exact bore match
        public string Spec;
        public string Material;
        // Same technical identity Compatible() demands for folding — the
        // reroute must not be looser than the fold (null/empty = not set).
        public double? WallThickness;
        public string Schedule;
        public string PressureClass;
    }

    public class PipeBendFoldResult
    {
        public HashSet<int> BendsConsumed = new();
        public Dictionary<int, double> ExtraPipeMetres = new();
        // Maps each target Pipe PnPID to the LineNumberTag of the bend
        // that contributed metres to it. Lets SpecRowBuilder reroute
        // stranded metres (when the target pipe got filtered out) to
        // another pipe sharing the same line.
        public Dictionary<int, string> ExtraPipeLineTags = new();
        // Richer than ExtraPipeLineTags: the bend's full procurement
        // identity, so a reroute can match diameter/spec exactly before
        // falling back to a same-line-only match.
        public Dictionary<int, BendOrphanInfo> ExtraPipeOrphanInfo = new();
        // Target pipe PnPID → bend PnPIDs folded into it. Lets the builder
        // name the actual bends in the review section when their metres
        // end up stranded (target pipe filtered out, no same-bore sibling).
        public Dictionary<int, List<int>> ExtraPipeBendIds = new();
        // Bends with a real length but no compatible adjacent pipe. They are
        // NOT consumed (they stay in the spec as their own pipe row), but
        // that is a doubt worth showing — usually a broken connection.
        public HashSet<int> OrphanBends = new();
    }

    /// <summary>
    /// Folds PipeBend rows into the length of the adjacent straight Pipe.
    /// Returns the set of bend PnPIDs to skip and a dictionary of extra
    /// metres each pipe should pick up; SpecRowBuilder applies the extras
    /// when it emits the pipe bucket.
    ///
    /// Called by SpecRowBuilder.Build on the records that passed the
    /// user's filters (status / category / line, equipment-owned, valve
    /// companions) plus every connector, so an excluded bend never donates
    /// metres and an excluded pipe never receives them. Topology comes from
    /// PartReader (direct P3dPartConnection edges, one drawing each), and a
    /// fold target must be the SAME purchased pipe as the bend (SameItem:
    /// drawing, line, material, spec, wall, schedule, pressure class,
    /// nominal size) with a positive length. Search order: a direct
    /// neighbour, one connector hop (the usual bend–weld–pipe), then a
    /// breadth-first walk through connectors and same-item bends for runs
    /// of consecutive bends. A bend with a length and no such pipe is an
    /// orphan (OrphanBends) and stays a row of its own.
    /// </summary>
    public static class PipeBendFolder
    {
        public static PipeBendFoldResult Fold(
            IList<PartRecord> records,
            IDictionary<int, List<int>> topology)
        {
            var result = new PipeBendFoldResult();
            if (records == null || records.Count == 0) return result;
            if (topology == null) return result;

            var byId = new Dictionary<int, PartRecord>(records.Count);
            var metresByPipe = new Dictionary<int, decimal>();
            foreach (var r in records)
                if (!byId.ContainsKey(r.PnPID)) byId.Add(r.PnPID, r);

            foreach (var bend in records.Where(p => p.IsPipeBend))
            {
                if (result.BendsConsumed.Contains(bend.PnPID)) continue;
                var lenRaw = ParseDouble(bend.Length);
                if (!(lenRaw > 0) || !double.IsFinite(lenRaw)) continue;
                // Unknown unit: don't fold (and don't throw — that used to
                // abort the whole spec). The bend then reaches SpecRowBuilder
                // as a pipe row, where the same unknown unit sends it to the
                // review section with its PnPID and drawing.
                var factor = LengthFactor(bend.LengthUnit);
                if (!factor.HasValue) continue;
                var metres = (decimal)lenRaw * (decimal)factor.Value;
                // Prefer pipes on the same LineNumberTag as the bend —
                // otherwise the bend's length can land on a pipe the
                // user filtered out (no-line bucket or a different
                // selected line) and the extra metres disappear from
                // the spec.
                int pipePid = FindAdjacentPipe(bend.PnPID, byId, topology,
                                                bend.LineNumberTag, pipe => Compatible(bend, pipe));
                // Consecutive bends (bend–weld–bend–weld–pipe, an S-bend
                // or a run of bends with no straight piece between them):
                // the inner bend has no pipe within one connector hop and
                // used to become an "orphan" — its metres split off into a
                // separate «Изгиб трубы …» row in «Трубы». Walk on through
                // connectors and SAME-ITEM bends to the nearest compatible
                // straight pipe. Every hop is held to the same procurement
                // identity as the direct fold, so this can never move metres
                // onto a different purchased pipe.
                if (pipePid == 0)
                    pipePid = FindPipeThroughBends(bend, byId, topology);
                if (pipePid == 0) { result.OrphanBends.Add(bend.PnPID); continue; }  // truly orphan bend
                metresByPipe.TryGetValue(pipePid, out var acc);
                metresByPipe[pipePid] = acc + metres;
                if (!result.ExtraPipeBendIds.TryGetValue(pipePid, out var bendIds))
                    result.ExtraPipeBendIds[pipePid] = bendIds = new List<int>();
                bendIds.Add(bend.PnPID);
                // Remember the line the bend belonged to. If two bends
                // on different lines fold into the same pipe (rare —
                // physically would need a tee with bends on both sides
                // tagged differently), the first one's line wins.
                if (!result.ExtraPipeLineTags.ContainsKey(pipePid))
                {
                    var bendLine = string.IsNullOrEmpty(bend.LineNumberTag)
                            ? SpecConstants.NullLine
                            : bend.LineNumberTag;
                    result.ExtraPipeLineTags[pipePid] = bendLine;
                    result.ExtraPipeOrphanInfo[pipePid] = new BendOrphanInfo
                    {
                        LineTag = bendLine,
                        // Same 3-decimal quantisation as SpecRowBuilder's
                        // bucket key and Compatible() below — the reroute
                        // compares this against the builder's rounded ND,
                        // so a raw 3.99999 would never match a 4.000 pipe.
                        NominalDiameter = System.Math.Round(ParseDouble(bend.NominalDiameter), 3),
                        Spec = (bend.Spec ?? "").Trim(),
                        Material = (bend.Material ?? "").Trim(),
                        WallThickness = SpecNumber.Parse(bend.WallThickness),
                        Schedule = (bend.Schedule ?? "").Trim(),
                        PressureClass = (bend.PressureClass ?? "").Trim(),
                    };
                }
                result.BendsConsumed.Add(bend.PnPID);
            }
            foreach (var pair in metresByPipe)
                result.ExtraPipeMetres[pair.Key] = (double)pair.Value;
            return result;
        }

        /// <summary>
        /// Locate the straight Pipe attached to a bend. Physically a
        /// PipeBend is wired to its neighbour through a P3dConnector
        /// (a weld stub), so direct neighbours are usually connectors
        /// with empty PartCategory rather than pipes. Walk one hop
        /// further through any connector neighbour.
        ///
        /// <paramref name="preferredLineTag"/> drives a "same-line"
        /// preference: when several pipes are reachable, pick the one
        /// sharing the bend's LineNumberTag first. (Fold passes
        /// Compatible() as <paramref name="compatible"/>, which already
        /// demands the same line, so in practice every candidate matches.)
        /// </summary>
        static int FindAdjacentPipe(int bendPid,
            Dictionary<int, PartRecord> byId,
            IDictionary<int, List<int>> topology,
            string preferredLineTag, System.Func<PartRecord, bool> compatible)
        {
            if (!topology.TryGetValue(bendPid, out var nbrs)) return 0;

            int sameLine = 0;
            int anyPipe = 0;

            // Pass 1: direct neighbour that is itself a Pipe.
            foreach (var n in nbrs)
            {
                if (!byId.TryGetValue(n, out var nbr)) continue;
                if (!IsPipe(nbr) || !compatible(nbr)) continue;
                if (anyPipe == 0) anyPipe = nbr.PnPID;
                if (SameLine(nbr.LineNumberTag, preferredLineTag))
                { sameLine = nbr.PnPID; break; }
            }
            if (sameLine != 0) return sameLine;

            // Pass 2: through a connector hop. PipeBends touch the pipe
            // through a weld connector in 99 % of projects.
            foreach (var n in nbrs)
            {
                if (!byId.TryGetValue(n, out var nbr)) continue;
                if (!IsConnector(nbr)) continue;
                if (!topology.TryGetValue(nbr.PnPID, out var nbrNbrs)) continue;
                foreach (var nn in nbrNbrs)
                {
                    if (nn == bendPid) continue;
                    if (!byId.TryGetValue(nn, out var far)) continue;
                    if (!IsPipe(far) || !compatible(far)) continue;
                    if (anyPipe == 0) anyPipe = far.PnPID;
                    if (SameLine(far.LineNumberTag, preferredLineTag))
                        return far.PnPID;
                }
            }
            return anyPipe;
        }

        /// <summary>
        /// Breadth-first walk from <paramref name="bend"/> through connectors
        /// and bends of the same procurement item, returning the nearest
        /// compatible straight pipe (0 if none). Stops at anything else —
        /// a fitting, valve or incompatible pipe ends that branch — so the
        /// search stays on the bend's own run. Ties at equal distance go to
        /// the lower PnPID (deterministic, independent of read order).
        /// </summary>
        static int FindPipeThroughBends(PartRecord bend,
            Dictionary<int, PartRecord> byId, IDictionary<int, List<int>> topology)
        {
            const int maxVisited = 500;   // safety net on huge connected graphs
            var visited = new HashSet<int> { bend.PnPID };
            var frontier = new List<int> { bend.PnPID };
            while (frontier.Count > 0 && visited.Count < maxVisited)
            {
                var next = new List<int>();
                var found = 0;
                foreach (var id in frontier)
                {
                    if (!topology.TryGetValue(id, out var nbrs)) continue;
                    foreach (var n in nbrs)
                    {
                        if (!visited.Add(n)) continue;
                        if (!byId.TryGetValue(n, out var rec)) continue;
                        if (IsPipe(rec))
                        {
                            if (Compatible(bend, rec) && (found == 0 || rec.PnPID < found))
                                found = rec.PnPID;
                            continue;   // never walk THROUGH a pipe
                        }
                        if (IsConnector(rec) || (rec.IsPipeBend && SameItem(bend, rec)))
                            next.Add(n);
                    }
                }
                if (found != 0) return found;
                frontier = next;
            }
            return 0;
        }

        static bool SameLine(string a, string b)
        {
            // Treat empty as "no-line bucket" — a bend tagged to a line
            // should never fold into an untagged pipe just to find a
            // match. preferredLineTag null/empty means "no preference".
            if (string.IsNullOrEmpty(b)) return true;
            return string.Equals(a, b, System.StringComparison.Ordinal);
        }

        static bool Compatible(PartRecord bend, PartRecord pipe)
            => SameItem(bend, pipe) && ParseDouble(pipe.Length) > 0;

        // Same procurement item (drawing, line, material, spec, wall,
        // schedule, pressure class, nominal size). Used for the fold target
        // and for the bends a chained fold may pass through.
        static bool SameItem(PartRecord bend, PartRecord pipe)
            => string.Equals(bend.SourceDwgPath ?? "", pipe.SourceDwgPath ?? "", System.StringComparison.OrdinalIgnoreCase)
            && (bend.LineNumberTag ?? "") == (pipe.LineNumberTag ?? "")
            && (bend.Material ?? "").Trim() == (pipe.Material ?? "").Trim()
            && (bend.Spec ?? "").Trim() == (pipe.Spec ?? "").Trim()
            && SpecNumber.Parse(bend.WallThickness) == SpecNumber.Parse(pipe.WallThickness)
            && (bend.Schedule ?? "").Trim() == (pipe.Schedule ?? "").Trim()
            && (bend.PressureClass ?? "").Trim() == (pipe.PressureClass ?? "").Trim()
            && (bend.NominalUnit ?? "").Trim().ToLowerInvariant() == (pipe.NominalUnit ?? "").Trim().ToLowerInvariant()
            && System.Math.Round(ParseDouble(bend.NominalDiameter), 3) == System.Math.Round(ParseDouble(pipe.NominalDiameter), 3);

        static bool IsPipe(PartRecord r)
            => string.Equals(CategoryClassifier.Normalize(r), "Pipe",
                              System.StringComparison.OrdinalIgnoreCase)
               && !r.IsPipeBend;

        static bool IsConnector(PartRecord r)
        {
            if (string.Equals(r.PnPClassName, "P3dConnector",
                              System.StringComparison.OrdinalIgnoreCase))
                return true;
            // Some templates leave PartCategory empty on welds; the
            // class name and the SpecRowBuilder's synthetic "P3dConnector"
            // bucket are the actual signals.
            var cls = (r.PnPClassName ?? "").ToLowerInvariant();
            return cls.Contains("connector") || cls.Contains("weld");
        }

        static double ParseDouble(string s)
        {
            return SpecNumber.Parse(s) ?? 0;
        }

        // null = unknown unit (never silently treated as millimetres).
        static double? LengthFactor(string unit) => LengthUnits.MetresPerUnit(unit);
    }
}
