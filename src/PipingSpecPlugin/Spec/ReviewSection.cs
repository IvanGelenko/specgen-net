using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// One doubtful part (or spec row) the user has to check by hand: a
    /// part that could not be read, could not be quantified, or was
    /// counted but with a caveat (no mass, ambiguous line, …).
    ///
    /// Replaces the old "strict mode" — a single unreadable part used to
    /// stop the whole project's spec. Now the spec is generated in full and
    /// every doubt lands in the trailing «Позиции, требующие проверки»
    /// section instead of only in the log / a warning line nobody reads.
    ///
    /// Settings-driven exclusions (status/category/line not selected,
    /// equipment nozzles, valve companions) are NOT doubts and never
    /// produce a ReviewItem.
    /// </summary>
    public sealed class ReviewItem
    {
        // Why the part is doubtful. Kept free of per-part data (PnPID,
        // drawing) so identical problems group into one section row.
        public string Reason;
        // Part name/description when known; null → the row is named by PnPID.
        public string Description;
        // Drawing-local PnPID as the user sees it in Data Manager
        // (PartRecord.SourcePnPID, not the scan-wide remapped id). 0 = n/a.
        public int PnPID;
        // Source drawing (full path or file name); null when not tied to one.
        public string DrawingPath;
        // Quantity / unit mass, only when actually known. These never
        // count toward the main sections — the section is informational.
        public double? Qty;
        public string Unit;
        public double? Weight;

        /// <summary>Review item for a record we did read.</summary>
        public static ReviewItem ForPart(PartRecord r, string reason, string description,
                                         double? qty = null, string unit = null)
            => new ReviewItem
            {
                Reason = reason,
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                PnPID = r == null ? 0 : (r.SourcePnPID != 0 ? r.SourcePnPID : r.PnPID),
                DrawingPath = r?.SourceDwgPath,
                Qty = qty,
                Unit = unit,
            };
    }

    /// <summary>
    /// Groups ReviewItems into the rows of the trailing section. One row
    /// per (reason, description): identical problems on many parts collapse
    /// into a single line with a part count and a capped PnPID / drawing
    /// list, so a project with 300 weightless supports yields one row, not
    /// 300.
    /// </summary>
    public static class ReviewSection
    {
        // Caps for the note column — the full list is in last-run.log.
        const int MaxPnPIdsInNote = 10;
        const int MaxDrawingsInNote = 5;
        const int MaxPnPIdsInName = 5;

        /// <summary>
        /// Does a drawing read stop the whole WRITESPEC run? Unreadable
        /// individual parts (<paramref name="skippedRows"/>) deliberately do
        /// NOT — that was the old "strict mode"; they become review items
        /// (<see cref="FromUnreadRows"/>). Only failures that make EVERY row
        /// of the drawing unreliable stop: a failed PnP reconnect after the
        /// freshness flush, or a failed WorkingDatabase swap.
        /// Kept here (pure logic) so the policy is covered by tests.
        /// </summary>
        public static bool ReadStopsGeneration(int skippedRows, bool workingDbSwapFailed,
                                               bool pnpReconnectFailed)
            => workingDbSwapFailed || pnpReconnectFailed;

        /// <summary>Review items for parts PartReader could not read at all.</summary>
        public static IEnumerable<ReviewItem> FromUnreadRows(
            IEnumerable<(int RowId, string Reason)> rows, string drawingPath)
        {
            if (rows == null) yield break;
            foreach (var (rowId, reason) in rows)
                yield return new ReviewItem
                {
                    Reason = $"Деталь не прочитана ({reason}) — не включена в спецификацию",
                    PnPID = rowId,   // drawing-local id, as in Data Manager
                    DrawingPath = drawingPath,
                };
        }

        public static List<SpecRow> BuildRows(IEnumerable<ReviewItem> items)
        {
            var result = new List<SpecRow>();
            if (items == null) return result;

            // Preserve first-occurrence order: the caller adds unread parts
            // first, then builder findings in pipeline order — the most
            // severe problems end up at the top of the section.
            var groups = new List<(string reason, string desc, List<ReviewItem> list)>();
            var index = new Dictionary<(string, string), int>();
            // One physical part = one item per problem. A part linked to
            // several lines reaches SpecRowBuilder once PER line record; when
            // it is rejected (zero length, bad bolt count, no category, …) the
            // builder never marks it seen, so each record raised the same
            // review item again — "деталей: 2" (and a doubled metre sum) for
            // one part. Identity = (problem, drawing-local PnPID, drawing).
            var seenParts = new HashSet<(string, string, int, string)>();
            foreach (var it in items)
            {
                if (it == null) continue;
                if (it.PnPID != 0 && !seenParts.Add(((it.Reason ?? "").Trim(), it.Description ?? "",
                        it.PnPID, (it.DrawingPath ?? "").Trim().ToUpperInvariant())))
                    continue;
                var key = ((it.Reason ?? "").Trim(), it.Description ?? "");
                if (!index.TryGetValue(key, out var i))
                {
                    index[key] = i = groups.Count;
                    groups.Add((key.Item1, it.Description, new List<ReviewItem>()));
                }
                groups[i].list.Add(it);
            }

            foreach (var (reason, desc, list) in groups)
            {
                var pids = list.Where(x => x.PnPID != 0).Select(x => x.PnPID).Distinct().ToList();
                string name;
                if (!string.IsNullOrEmpty(desc)) name = desc;
                else if (pids.Count == 0) name = "Деталь без наименования";
                else
                {
                    name = "PnPID " + string.Join(", ", pids.Take(MaxPnPIdsInName))
                         + (pids.Count > MaxPnPIdsInName ? $" и ещё {pids.Count - MaxPnPIdsInName}" : "");
                }

                // Quantity only when every item carries one in the same unit;
                // a partial sum would look authoritative and be wrong.
                double qty = 0;
                string unit = "";
                if (list.All(x => x.Qty.HasValue && double.IsFinite(x.Qty.Value) && x.Qty.Value > 0)
                    && list.Select(x => x.Unit ?? "").Distinct().Count() == 1)
                {
                    unit = list[0].Unit ?? "";
                    var sum = list.Sum(x => (decimal)x.Qty.Value);
                    // Same 1 µm snap as SpecRowBuilder: double noise must
                    // not push an exact length up by a whole 0.1 m.
                    qty = unit == "м"
                        ? (double)(decimal.Ceiling(decimal.Round(sum, 6, MidpointRounding.AwayFromZero) * 10m) / 10m)
                        : (double)sum;
                }
                double weight = 0;
                if (list.All(x => x.Weight.HasValue && double.IsFinite(x.Weight.Value) && x.Weight.Value > 0))
                {
                    var ws = list.Select(x => x.Weight.Value).ToList();
                    if (ws.Max() - ws.Min() <= ws.Min() * 0.05) weight = Math.Round(ws.Average(), 3);
                }

                result.Add(new SpecRow
                {
                    Category = SpecConstants.ReviewSectionHeader,
                    Name = name,
                    Unit = unit,
                    Qty = qty,
                    Weight = weight,
                    Note = BuildNote(reason, list),
                });
            }
            return result;
        }

        static string BuildNote(string reason, List<ReviewItem> list)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(reason)) parts.Add(reason);
            // Row-level items (mass spread inside one spec row, legacy
            // stranded metres) carry no PnPID — they count spec rows, not
            // parts, and must not be labelled as a number of parts.
            if (list.Count > 1)
                parts.Add(list.Any(x => x.PnPID != 0)
                    ? $"деталей: {list.Count}"
                    : $"строк спецификации: {list.Count}");

            // "A.dwg (PnPID 1, 2); B.dwg (PnPID 7)", capped on both axes.
            var byDwg = list
                .GroupBy(x => DrawingName(x.DrawingPath), StringComparer.OrdinalIgnoreCase)
                .ToList();
            var shownPids = 0;
            var dwgParts = new List<string>();
            foreach (var g in byDwg.Take(MaxDrawingsInNote))
            {
                var ids = g.Where(x => x.PnPID != 0).Select(x => x.PnPID).Distinct().ToList();
                var take = Math.Max(0, Math.Min(ids.Count, MaxPnPIdsInNote - shownPids));
                shownPids += take;
                var idText = take > 0
                    ? "PnPID " + string.Join(", ", ids.Take(take).Select(i => i.ToString(CultureInfo.InvariantCulture)))
                      + (ids.Count > take ? $" и ещё {ids.Count - take}" : "")
                    : (ids.Count > 0 ? $"деталей: {ids.Count}" : "");
                var dwg = string.IsNullOrEmpty(g.Key) ? "" : "чертёж " + g.Key;
                var piece = string.Join(", ", new[] { dwg, idText }.Where(s => !string.IsNullOrEmpty(s)));
                if (!string.IsNullOrEmpty(piece)) dwgParts.Add(piece);
            }
            if (byDwg.Count > MaxDrawingsInNote)
                dwgParts.Add($"и ещё чертежей: {byDwg.Count - MaxDrawingsInNote}");
            if (dwgParts.Count > 0) parts.Add(string.Join("; ", dwgParts));
            return string.Join(". ", parts);
        }

        static string DrawingName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { return Path.GetFileName(path); }
            catch { return path; }
        }

        /// <summary>One log line per item — the uncapped detail behind the section.</summary>
        public static string Describe(ReviewItem it)
        {
            var dwg = DrawingName(it.DrawingPath);
            return $"{it.Reason}"
                 + (it.Description != null ? $" | {it.Description}" : "")
                 + (it.PnPID != 0 ? $" | PnPID {it.PnPID}" : "")
                 + (dwg != "" ? $" | {dwg}" : "")
                 + (it.Qty.HasValue ? $" | {it.Qty.Value.ToString("0.###", CultureInfo.InvariantCulture)} {it.Unit}" : "");
        }
    }
}
