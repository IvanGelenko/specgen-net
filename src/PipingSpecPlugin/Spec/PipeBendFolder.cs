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
                if (!topology.TryGetValue(bend.PnPID, out var nbrs)) continue;
                int pipePid = 0;
                foreach (var nbrId in nbrs)
                {
                    if (!byId.TryGetValue(nbrId, out var nbr)) continue;
                    if (string.Equals(nbr.PartCategory, "Pipe",
                                       System.StringComparison.OrdinalIgnoreCase)
                        && !nbr.IsPipeBend)
                    { pipePid = nbr.PnPID; break; }
                }
                if (pipePid == 0) continue;
                result.ExtraPipeMetres.TryGetValue(pipePid, out var acc);
                result.ExtraPipeMetres[pipePid] = acc + metres;
                result.BendsConsumed.Add(bend.PnPID);
            }
            return result;
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
