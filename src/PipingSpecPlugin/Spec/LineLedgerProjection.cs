using System;
using System.Collections.Generic;
using System.Linq;
using static PipingSpecPlugin.Spec.VtFieldMappings;

namespace PipingSpecPlugin.Spec
{
    internal static class LineLedgerProjection
    {
        /// <summary>
        /// Builds the "внутренняя ведомость" review table: every project
        /// line with a non-blank Id, one row per line, with every
        /// property СИНХРОНИЗАЦИЯ ВТ can write (KnownLineGroupColumns).
        /// Reflects current Plant 3D state — call again after Apply to
        /// see the result rather than trying to predict it here. Sorted
        /// by Id so repeated runs render in a stable order.
        /// </summary>
        public static List<LineLedgerRow> BuildLedger(List<LineGroupInfo> lines, bool collapseDuplicates = false,
            IEnumerable<LineGroupInfo> projectLines = null, IReadOnlySet<int> matchedRowIds = null)
        {
            var rows = new List<LineLedgerRow>();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.Id) && matchedRowIds?.Contains(line.RowId) != true) continue;
                var row = new LineLedgerRow
                {
                    RowId = line.RowId,
                    RowIds = new List<int> { line.RowId },
                    Id = line.Id ?? "",
                    Tag = line.Tag
                };
                foreach (var col in KnownLineGroupColumns)
                    row.Values[col] = line.AllProperties.TryGetValue(col, out var v) ? v : "";

                // If srok is empty in the Plant 3D database, but Corrosion and CorrosionSpeed are present,
                // compute srok = Corrosion / CorrosionSpeed dynamically as a fallback.
                if (string.IsNullOrWhiteSpace(row.Values["srok"]))
                {
                    var calcLife = VtValueParsing.ComputeServiceLifeFromCorrosion(
                        row.Values.TryGetValue("Corrosion", out var corr) ? corr : null,
                        row.Values.TryGetValue("CorrosionSpeed", out var cs) ? cs : null);
                    if (calcLife != null)
                        row.Values["srok"] = calcLife;
                }

                // If Corrosion is not explicitly saved in the Plant 3D line properties,
                // but CorrosionSpeed and srok are present, compute Corrosion dynamically
                // so the calculated column displays properly in the ledger.
                if (string.IsNullOrWhiteSpace(row.Values["Corrosion"]))
                {
                    var calc = VtValueParsing.ComputeCorrosionAllowance(
                        row.Values.TryGetValue("CorrosionSpeed", out var cs) ? cs : null,
                        row.Values.TryGetValue("srok", out var sr) ? sr : null);
                    if (calc != null)
                        row.Values["Corrosion"] = calc;
                }

                rows.Add(row);
            }
            if (collapseDuplicates)
                rows = rows.GroupBy(r => new { Id = r.Id.Trim().ToUpperInvariant(), Tag = (r.Tag ?? "").Trim(),
                    UnassignedRowId = string.IsNullOrWhiteSpace(r.Id) ? r.RowId : 0 })
                    .Select(group =>
                    {
                        var first = group.First();
                        var merged = new LineLedgerRow
                        {
                            RowId = first.RowId,
                            RowIds = group.SelectMany(r => r.AllRowIds).Distinct().ToList(),
                            Id = first.Id.Trim(),
                            Tag = (first.Tag ?? "").Trim()
                        };
                        foreach (var col in KnownLineGroupColumns)
                        {
                            var values = group.Select(r => (r.Values[col] ?? "").Trim()).Distinct(StringComparer.Ordinal).ToList();
                            merged.Values[col] = values.Count == 1 ? values[0] :
                                "⚠ " + string.Join(" | ", values.Select(v => v.Length == 0 ? "(пусто)" : v));
                        }
                        return merged;
                    }).ToList();
            // Plain string.Compare puts "1.10" before "1.2" (lexical: '1'
            // < '2' at the first differing character) even though the
            // user's "раздел.номер" ID scheme (e.g. "1.1"…"1.13", "3.1",
            // "3.2") means 1.10 belongs after 1.9. VtValueParsing.
            // NaturalCompare splits each Id into alternating digit/non-
            // digit runs and compares digit runs numerically — falls back
            // to ordinary text comparison for non-numeric Ids (e.g.
            // "asdd", "P-22002"), which aren't part of this scheme but
            // still need a stable, sensible order.
            rows.Sort((a, b) => VtValueParsing.NaturalCompare(a.Id, b.Id));

            // A genuine duplicate Id (two distinct RowIds sharing the
            // same "ID" column value — legitimate, tracked elsewhere as
            // LineSyncPlan.DuplicateIds) should stay visible as separate
            // rows rather than silently collapsing, but flagged so it
            // doesn't read as a rendering bug.
            var visibleDuplicates = DuplicateIds(rows.Select(row => row.Id));
            var projectDuplicates = DuplicateIds((projectLines ?? lines).Select(line => line.Id));
            foreach (var row in rows)
            {
                row.HasVisibleDuplicate = visibleDuplicates.Contains(row.Id.Trim());
                row.HasProjectDuplicate = projectDuplicates.Contains(row.Id.Trim());
            }

            return rows;
        }

        static HashSet<string> DuplicateIds(IEnumerable<string> ids) => ids
            .Where(id => !string.IsNullOrWhiteSpace(id)).GroupBy(id => id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        public static bool IsLedgerVisibleColumn(string column) =>
            !string.Equals(column, "Tag", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(column, "Номер", StringComparison.OrdinalIgnoreCase);
    }
}
