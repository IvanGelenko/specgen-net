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
    /// and the final aggregation, so the drawings are never read twice.
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
            // bucket marked with the SpecConstants sentinel.
            //
            // Connectors (welds, gaskets/bolts recognised as connectors) are
            // counted too. They used to be skipped here, but the builder's
            // status / line filters apply to them like to any other row: a
            // weld whose status (or "no line") existed ONLY on connectors got
            // no chip in the picker, so the explicit selection list could
            // never contain it and the weld silently fell out of the spec
            // when the user kept connectors in.
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
            // (supports detected via iso symbol). Equipment now ships as
            // an ordinary category checkbox — same as the welds category
            // we introduced in v0.12.7 — instead of being driven by a
            // standalone 'Исключить оборудование' rule. One place to
            // control inclusion beats two competing toggles.
            var catCounts = new Dictionary<string, int>();
            foreach (var r in list)
            {
                var cat = NormalizeCategoryPublic(r);
                // Skip unclassified parts entirely: the builder always drops
                // them, so counting a "Без категории" bucket here would
                // promise the picker something the spec can't deliver. Keeping
                // facts and output in lock-step is the whole point of this
                // class. (See the matching guard in SpecPickerWindow.)
                if (string.IsNullOrEmpty(cat)) continue;
                catCounts.TryGetValue(cat, out var c);
                catCounts[cat] = c + 1;
            }
            facts.Categories = catCounts
                .OrderBy(kv => kv.Key, System.StringComparer.Ordinal)
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
                // Line-less connectors land in the NullLine bucket (see the
                // status note above for why they are not skipped).
                if (!lineToPids.TryGetValue(key, out var set))
                    lineToPids[key] = set = new HashSet<int>();
                set.Add(r.PnPID);
            }
            // Natural order (same as the document's line blocks): "Л-2"
            // before "Л-10" in the picker too.
            facts.Lines = lineToPids
                .OrderBy(kv => kv.Key == SpecConstants.NullLine ? 1 : 0)
                .ThenBy(kv => kv.Key, System.Collections.Generic.Comparer<string>.Create(SpecRowBuilder.CompareText))
                .Select(kv => (kv.Key, kv.Value.Count))
                .ToList();

            return facts;
        }

        static string NormalizeCategoryPublic(PartRecord r)
            => CategoryClassifier.Normalize(r);
    }
}
