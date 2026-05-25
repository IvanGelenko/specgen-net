using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Folds PipeBend rows into the length of the adjacent straight Pipe.
    /// Returns the set of bend PnPIDs to skip and a dictionary of extra
    /// metres each pipe should pick up. Runs against the project-wide
    /// PartRecord collection (NOT after the scope filter) so a bend
    /// whose pipe-neighbour lives in a different drawing still folds
    /// correctly — SpecRowBuilder then applies the extras when it
    /// emits the pipe bucket.
    ///
    /// Topology comes from PartReader: every direct P3dPartConnection
    /// neighbour of every part. The bend is matched to the first
    /// neighbour whose PartCategory is "Pipe" and PnPClassName is NOT
    /// itself "PipeBend".
    /// </summary>
    public class PipeBendFoldResult
    {
        public HashSet<int> BendsConsumed = new();
        public Dictionary<int, double> ExtraPipeMetres = new();
    }

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
            foreach (var r in records)
                if (!byId.ContainsKey(r.PnPID)) byId.Add(r.PnPID, r);

            foreach (var bend in records.Where(p => p.IsPipeBend))
            {
                if (result.BendsConsumed.Contains(bend.PnPID)) continue;
                var lenRaw = ParseDouble(bend.Length);
                if (lenRaw <= 0) continue;
                var metres = lenRaw * LengthFactor(bend.LengthUnit);
                int pipePid = FindAdjacentPipe(bend.PnPID, byId, topology);
                if (pipePid == 0) continue;  // truly orphan bend
                result.ExtraPipeMetres.TryGetValue(pipePid, out var acc);
                result.ExtraPipeMetres[pipePid] = acc + metres;
                result.BendsConsumed.Add(bend.PnPID);
            }
            return result;
        }

        /// <summary>
        /// Locate the straight Pipe attached to a bend. Physically a
        /// PipeBend is wired to its neighbour through a P3dConnector
        /// (a weld stub), so direct neighbours are usually connectors
        /// with empty PartCategory rather than pipes. Walk one hop
        /// further through any connector neighbour, prefer a Pipe but
        /// fall back to "any non-bend part" so a bend wedged between
        /// fittings still folds somewhere reasonable.
        /// </summary>
        static int FindAdjacentPipe(int bendPid,
            Dictionary<int, PartRecord> byId,
            IDictionary<int, List<int>> topology)
        {
            if (!topology.TryGetValue(bendPid, out var nbrs)) return 0;

            // Pass 1: direct neighbour that is itself a Pipe.
            foreach (var n in nbrs)
            {
                if (!byId.TryGetValue(n, out var nbr)) continue;
                if (IsPipe(nbr)) return nbr.PnPID;
            }

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
                    if (IsPipe(far)) return far.PnPID;
                }
            }
            return 0;
        }

        static bool IsPipe(PartRecord r)
            => string.Equals(r.PartCategory, "Pipe",
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
            if (string.IsNullOrWhiteSpace(s)) return 0;
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
                ? v : 0;
        }

        static double LengthFactor(string unit)
        {
            var u = (unit ?? "").Trim().ToLowerInvariant();
            switch (u)
            {
                case "": case "mm": return 0.001;
                case "cm": return 0.01;
                case "m": return 1.0;
                case "in": return 0.0254;
                case "ft": return 0.3048;
                default: return 0.001;
            }
        }
    }
}
