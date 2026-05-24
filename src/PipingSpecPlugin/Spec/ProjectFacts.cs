using System.Collections.Generic;
using System.Linq;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Snapshot of what the project (or active drawing) contains:
    /// available statuses, categories and lines plus their part counts.
    /// Drives the picker UI checkboxes.
    ///
    /// We compute this off the same PartRecord stream the spec builder
    /// consumes — a single read of the drawings powers both the picker
    /// and the final aggregation. The Python edition does three separate
    /// SQL queries instead; we can't, because there's no Piping.dcf to
    /// query through the managed API, only the per-drawing DataLinks
    /// blob.
    /// </summary>
    public class ProjectFacts
    {
        // (key, count) lists, sorted with empty/sentinel bucket last so
        // the picker checkboxes show the most relevant items at the top.
        public List<(string key, int count)> Statuses = new();
        public List<(string key, int count)> Categories = new();
        public List<(string key, int count)> Lines = new();

        // Cached list of records used to derive the facts — handed back
        // to SpecRowBuilder so we don't re-read the drawings.
        public IReadOnlyList<PartRecord> Records;

        public static ProjectFacts From(IEnumerable<PartRecord> records)
        {
            var list = records.ToList();
            var facts = new ProjectFacts { Records = list };

            // Statuses: empty/null collapses into a single "<no status>"
            // bucket marked with the same sentinel the Python edition uses.
            var statusCounts = new Dictionary<string, int>();
            foreach (var r in list)
            {
                var key = string.IsNullOrEmpty(r.Status) ? SpecConstants.NullStatus : r.Status;
                statusCounts.TryGetValue(key, out var c);
                statusCounts[key] = c + 1;
            }
            facts.Statuses = statusCounts
                .OrderBy(kv => kv.Key == SpecConstants.NullStatus ? 1 : 0)
                .ThenBy(kv => kv.Key, System.StringComparer.Ordinal)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();

            // Categories: apply the same normalisation the builder uses
            // (supports detected via iso symbol). Equipment is filtered
            // out of the picker (Python does the same — equipment has
            // its own dedicated checkbox).
            var catCounts = new Dictionary<string, int>();
            foreach (var r in list)
            {
                var cat = NormalizeCategoryPublic(r);
                if (cat == "Equipment") continue;
                var key = string.IsNullOrEmpty(cat) ? SpecConstants.NullCategory : cat;
                catCounts.TryGetValue(key, out var c);
                catCounts[key] = c + 1;
            }
            facts.Categories = catCounts
                .OrderBy(kv => kv.Key == SpecConstants.NullCategory ? 1 : 0)
                .ThenBy(kv => kv.Key, System.StringComparer.Ordinal)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();

            // Lines: count DISTINCT PnPIDs per LineTag (a single part can
            // appear on multiple lines through P3dLineGroupPartRelationship;
            // we want to show the user how many unique parts the line
            // contains, not the row count).
            var lineToPids = new Dictionary<string, HashSet<int>>();
            foreach (var r in list)
            {
                var key = string.IsNullOrEmpty(r.LineNumberTag)
                    ? SpecConstants.NullLine
                    : r.LineNumberTag;
                if (!lineToPids.TryGetValue(key, out var set))
                    lineToPids[key] = set = new HashSet<int>();
                set.Add(r.PnPID);
            }
            facts.Lines = lineToPids
                .OrderBy(kv => kv.Key == SpecConstants.NullLine ? 1 : 0)
                .ThenBy(kv => kv.Key, System.StringComparer.Ordinal)
                .Select(kv => (kv.Key, kv.Value.Count))
                .ToList();

            return facts;
        }

        // Mirrors SpecRowBuilder.NormalizeCategory, exposed so this class
        // can run before the builder. Keep in sync if the support
        // detection logic ever changes.
        static string NormalizeCategoryPublic(PartRecord r)
        {
            if (!string.IsNullOrEmpty(r.ContentIsoSymbolDefinition)
                && r.ContentIsoSymbolDefinition.IndexOf("TYPE=SUPPORT",
                    System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "Supports";
            return r.PartCategory ?? "";
        }
    }
}
