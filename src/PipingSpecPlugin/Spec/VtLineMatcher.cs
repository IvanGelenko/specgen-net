using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PipingSpecPlugin.Spec
{
    internal sealed record VtLineMatch(VtRowData Row, LineGroupInfo Line, string Designation, bool MatchedByTag);

    internal static class VtLineMatcher
    {
        static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;
        static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

        internal static string GetTag(LineGroupInfo line) => line.Tag ?? line.AllProperties.GetValueOrDefault("Tag") ?? "";

        static string FullTag(string tag) => Whitespace.Replace((tag ?? "").Trim(), " ");

        static string[] ReadTags(string designation) => (designation ?? "")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(FullTag).Where(tag => LineDesignationParser.TagOf(tag).Length > 0)
            .Distinct(KeyComparer).ToArray();

        internal static List<VtLineMatch> Match(IReadOnlyList<VtRowData> rows, IReadOnlyList<LineGroupInfo> lines,
            LineSyncPlan diagnostics, IReadOnlySet<int> drawingIds = null, bool allowIdFallback = true)
        {
            var workbookIds = rows.Where(row => !string.IsNullOrWhiteSpace(row.NN)).ToLookup(row => row.NN.Trim(), KeyComparer);
            var projectIds = lines.Where(line => !string.IsNullOrWhiteSpace(line.Id)).ToLookup(line => line.Id.Trim(), KeyComparer);
            var taggedLines = lines.Select(line => (Line: line, Full: FullTag(GetTag(line))))
                .Where(item => item.Full.Length > 0).ToList();
            var exact = taggedLines.ToLookup(item => item.Full, item => item.Line, KeyComparer);
            var canonical = taggedLines.ToLookup(item => LineDesignationParser.TagOf(item.Full), KeyComparer);
            var candidates = new List<VtLineMatch>();
            var idFallbackFailures = new Dictionary<int, string>();

            diagnostics.DuplicateVtNN.AddRange(workbookIds.Where(group => group.Count() > 1).Select(group => group.Key));
            diagnostics.DuplicateIds.AddRange(projectIds.Where(group => workbookIds.Contains(group.Key) && group.Count() > 1)
                .Select(group => group.Key));

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.NN)) continue;
                var ids = new HashSet<int>();
                var tags = ReadTags(row.Designation);
                bool ambiguousTag = false;
                foreach (var tag in tags)
                {
                    var possible = exact[tag].ToList();
                    var baseTag = LineDesignationParser.TagOf(tag);
                    if (possible.Count == 0)
                    {
                        // Do not treat two different annotations on the same base tag as equal.
                        possible = canonical[baseTag]
                            .Where(item => KeyComparer.Equals(tag, baseTag) || KeyComparer.Equals(item.Full, baseTag))
                            .Select(item => item.Line).ToList();
                    }
                    else if (possible.Count > 1 && drawingIds != null)
                    {
                        var inDrawing = possible.Where(line => drawingIds.Contains(line.RowId)).ToList();
                        if (inDrawing.Count > 0) possible = inDrawing;
                    }

                    if (possible.Count > 1)
                    {
                        ambiguousTag = true;
                        var rowIds = string.Join(", ", possible.Select(line => line.RowId));
                        foreach (var line in possible)
                            diagnostics.BlockedLines[line.RowId] = $"Обозначение «{tag}» из строки {row.RowNumber} ВТ совпало с несколькими группами линий (PnPID {rowIds}).";
                    }
                    else if (possible.Count == 1 && ids.Add(possible[0].RowId))
                    {
                        candidates.Add(new VtLineMatch(row, possible[0], baseTag, MatchedByTag: true));
                    }
                }

                if (ids.Count > 0 || ambiguousTag || !allowIdFallback) continue;
                var nn = row.NN.Trim();
                var byId = projectIds[nn].ToList();
                if (byId.Count == 0)
                {
                    diagnostics.UnmatchedVtRows.Add(row);
                    continue;
                }

                bool uniqueId = byId.Count == 1 && workbookIds[nn].Count() == 1;
                if (uniqueId && (tags.Length == 0 && string.IsNullOrWhiteSpace(row.Designation)
                    || tags.Length == 1 && string.IsNullOrWhiteSpace(GetTag(byId[0]))))
                {
                    candidates.Add(new VtLineMatch(row, byId[0], tags.Length == 0 ? "" : LineDesignationParser.TagOf(tags[0]), MatchedByTag: false));
                    continue;
                }

                foreach (var line in byId)
                    idFallbackFailures[line.RowId] = uniqueId
                        ? $"ID «{nn}», линия «{GetTag(line)}» не совпадает с обозначением в строке {row.RowNumber} ВТ."
                        : $"Номер «{nn}» повторяется, однозначного совпадения по обозначению нет.";
            }

            var assignments = new List<VtLineMatch>();
            foreach (var group in candidates.GroupBy(candidate => candidate.Line.RowId))
            {
                var tagged = group.Where(match => match.MatchedByTag).ToList();
                var preferred = tagged.Count > 0 ? tagged : group.ToList();
                var match = preferred[0];
                if (preferred.Count > 1)
                    diagnostics.BlockedLines[group.Key] = $"Линия «{GetTag(match.Line)}» сопоставляется с несколькими строками ВТ ({string.Join(", ", preferred.Select(item => item.Row.RowNumber))}).";
                else if (!diagnostics.BlockedLines.ContainsKey(group.Key))
                    assignments.Add(match);
            }
            var assigned = assignments.Select(match => match.Line.RowId).ToHashSet();
            foreach (var failure in idFallbackFailures)
                if (!assigned.Contains(failure.Key)) diagnostics.BlockedLines.TryAdd(failure.Key, failure.Value);
            return assignments;
        }
    }
}
