using System;
using System.Collections.Generic;
using System.Linq;
using static PipingSpecPlugin.Spec.VtFieldMappings;

namespace PipingSpecPlugin.Spec
{
    public static class LineSyncPlanner
    {
        /// <summary>
        /// Builds a dry-run plan: matches every ВТ row's "n/n" (row
        /// ordinal, e.g. "1", "3.1") to a project line's `ID` property
        /// and computes the field-level diff. Nothing is written here —
        /// reads only line.AllProperties, already populated by
        /// ReadAllLines, so no DataLinksManager access happens in this
        /// method (pure in-memory diff). The sync key used to be ВТ's
        /// "ИД трубопровода" column; it's now "n/n" per the user's
        /// explicit switch — the target Plant 3D field (`ID`) is
        /// unchanged, only the ВТ source column changed. All picklist
        /// values come pre-coded from ВТ's numeric columns or from
        /// LineDesignationParser, so unlike an earlier version of this
        /// method there's no user-maintained text->code mapping table to
        /// consult.
        /// </summary>
        public static LineSyncPlan BuildPlan(
            List<VtRowData> vtRows,
            List<LineGroupInfo> lines)
        {
            var plan = new LineSyncPlan();

            var byId = new Dictionary<string, List<LineGroupInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.Id)) continue;
                var key = line.Id.Trim();
                if (!byId.TryGetValue(key, out var list)) byId[key] = list = new List<LineGroupInfo>();
                list.Add(line);
            }

            // n/n must be unique across the whole ВТ workbook to be a
            // usable sync key — a value repeated on several rows (e.g.
            // section numbering restarting at 1 per Цех) makes it
            // impossible to tell which row a given Plant 3D line's ID
            // should match. Every ВТ row sharing such a value is skipped
            // entirely rather than guessed at.
            var nnCounts = vtRows.Where(r => !string.IsNullOrWhiteSpace(r.NN))
                .GroupBy(r => r.NN.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var kv in nnCounts)
                if (kv.Value > 1) plan.DuplicateVtNN.Add(kv.Key);

            foreach (var vt in vtRows)
            {
                if (string.IsNullOrWhiteSpace(vt.NN)) continue;
                var nn = vt.NN.Trim();
                if (nnCounts.TryGetValue(nn, out var nnCount) && nnCount > 1)
                    continue;
                if (!byId.TryGetValue(nn, out var candidates) || candidates.Count == 0)
                {
                    plan.UnmatchedVtRows.Add(vt);
                    continue;
                }
                var designationTags = ReadDesignationTags(vt.Designation).ToHashSet(StringComparer.OrdinalIgnoreCase);
                bool allowEmptyTag = candidates.Count == 1 && designationTags.Count == 1;
                var matchingLines = designationTags.Count == 0
                    ? candidates
                    : candidates.Where(line =>
                    {
                        var tag = ReadLineTag(line);
                        return designationTags.Contains(LineDesignationParser.TagOf(tag))
                            || (allowEmptyTag && string.IsNullOrWhiteSpace(tag));
                    }).ToList();
                if (candidates.Count > 1)
                {
                    plan.DuplicateIds.Add(nn);
                    if (designationTags.Count == 0 || matchingLines.Count != 1)
                    {
                        foreach (var line in candidates)
                            plan.BlockedLines[line.RowId] = $"ID «{nn}» повторяется, однозначного совпадения по обозначению нет.";
                        continue;
                    }
                }

                foreach (var line in candidates.Except(matchingLines))
                    plan.BlockedLines[line.RowId] = $"ID «{nn}», линия «{line.Tag}» не совпадает с обозначением в строке {vt.RowNumber} ВТ.";

                foreach (var line in matchingLines)
                {
                    plan.MatchedRowIds.Add(line.RowId);
                    var action = BuildAction(vt, line, designationTags);
                    if (action.Changes.Count > 0 || action.PicklistRejections.Count > 0)
                        plan.Actions.Add(action);
                }
            }

            foreach (var line in lines)
                if (!string.IsNullOrWhiteSpace(line.Id) && !nnCounts.ContainsKey(line.Id.Trim()))
                    plan.UnmatchedLines.Add(line);

            return plan;
        }

        static LineSyncAction BuildAction(VtRowData vt, LineGroupInfo line, IReadOnlySet<string> designationTags)
        {
            var action = new LineSyncAction { VtRow = vt, Line = line };
            foreach (var field in DirectFields)
                AddChange(action, field.Property, field.ValueFor(vt));

            AddTestChanges(action);
            AddChange(action, NdControl.Property, NdControl.ValueFor(vt));
            AddCorrosionChanges(action);
            AddDesignationChanges(action, designationTags);
            return action;
        }

        static void AddTestChanges(LineSyncAction action)
        {
            var vt = action.VtRow;
            var (testD, combinedTestW) = VtValueParsing.SplitTestValues(vt.TestStrength);
            AddChange(action, "TestD", testD);

            bool hasTightness = !string.IsNullOrWhiteSpace(vt.TestTightness);
            var testW = hasTightness ? VtValueParsing.SplitTestValues(vt.TestTightness).testD : combinedTestW;
            if (hasTightness && testW == null)
                action.PicklistRejections.Add($"«На герметичность»: значение «{vt.TestTightness}» не является давлением. Поле TestW не будет изменено.");
            AddChange(action, "TestW", testW);
        }

        static void AddCorrosionChanges(LineSyncAction action)
        {
            var vt = action.VtRow;
            var years = VtValueParsing.ExtractServiceLifeYears(vt.ServiceLife);
            if (years == null && !string.IsNullOrWhiteSpace(vt.ServiceLife))
                action.PicklistRejections.Add($"Срок службы «{vt.ServiceLife}» не распознан как срок в годах.");
            AddChange(action, "srok", years);

            var rate = VtValueParsing.ExtractCorrosionRate(vt.CorrosionRate);
            if (rate == null && !string.IsNullOrWhiteSpace(vt.CorrosionRate))
                action.PicklistRejections.Add($"Скорость коррозии «{vt.CorrosionRate}» не распознана как скорость в мм/год.");
            AddChange(action, "CorrosionSpeed", rate);

            if (rate == null && years == null) return;
            // Reuse a model value only when the corresponding Excel cell is blank.
            var effectiveRate = string.IsNullOrWhiteSpace(vt.CorrosionRate)
                ? action.Line.AllProperties.GetValueOrDefault("CorrosionSpeed") : rate;
            var effectiveYears = string.IsNullOrWhiteSpace(vt.ServiceLife)
                ? action.Line.AllProperties.GetValueOrDefault("srok") : years;
            AddChange(action, "Corrosion", VtValueParsing.ComputeCorrosionAllowance(effectiveRate, effectiveYears));
        }

        static void AddDesignationChanges(LineSyncAction action, IReadOnlySet<string> designationTags)
        {
            var vt = action.VtRow;
            var oldTag = ReadLineTag(action.Line).Trim();
            var designationTag = "";
            if (designationTags.Count > 0)
            {
                designationTag = oldTag.Length == 0 ? designationTags.Single() : LineDesignationParser.TagOf(oldTag);
                AddChange(action, "Tag", designationTag, oldTag);
            }

            var lineNumber = LineDesignationParser.ParseSyncLineNumber(designationTag);
            if (lineNumber == null && !string.IsNullOrWhiteSpace(vt.Designation))
                action.PicklistRejections.Add("Трёхзначный номер после кода среды не распознан; поле Номер оставлено без изменений.");
            AddChange(action, "Номер", lineNumber);

            var parts = LineDesignationParser.Parse(designationTag);
            if (parts == null)
            {
                if (!string.IsNullOrWhiteSpace(vt.Designation))
                    action.PicklistRejections.Add(
                        $"Обозначение «{vt.Designation.Split('\n')[0].Trim()}» не распознано форматом "
                        + "«размер-код+номер-материал+давление+фланец-изоляция+спутник» — "
                        + "поля Size2/Material_pipe/Ysl_pressure/flanges/InsulationType/Sputnik не заполнены из него.");
                return;
            }

            foreach (var field in DesignationFields)
                AddChange(action, field.Property, field.Value(parts));
        }

        static void AddChange(LineSyncAction action, string property, string newValue, string oldValue = null)
        {
            if (string.IsNullOrEmpty(newValue)) return;
            oldValue ??= action.Line.AllProperties.GetValueOrDefault(property) ?? "";
            if (SameFieldValue(property, newValue, oldValue)) return;

            action.Changes.Add(new FieldChange
            {
                PropertyName = property,
                DisplayLabel = KnownLineGroupColumnLabels[property],
                OldValue = oldValue,
                NewValue = newValue,
            });
        }


        static string ReadLineTag(LineGroupInfo line) => line.Tag ?? line.AllProperties.GetValueOrDefault("Tag") ?? "";

        static IEnumerable<string> ReadDesignationTags(string designation) => (designation ?? "")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(LineDesignationParser.TagOf)
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        internal static LineSyncPlan BuildIdFillPlan(IEnumerable<VtRowData> vtRows,
            IEnumerable<LineGroupInfo> drawingLines, IEnumerable<LineGroupInfo> projectLines)
        {
            var comparer = StringComparer.OrdinalIgnoreCase;
            var rows = vtRows.Where(row => !string.IsNullOrWhiteSpace(row.NN)).ToList();
            var availableIds = rows.GroupBy(row => row.NN.Trim(), comparer)
                .Where(group => group.Count() == 1).Select(group => group.Key).ToHashSet(comparer);
            availableIds.ExceptWith(projectLines.Where(line => !string.IsNullOrWhiteSpace(line.Id))
                .Select(line => line.Id.Trim()));
            var rowsByTag = rows.SelectMany(row => ReadDesignationTags(row.Designation).Select(tag => (Tag: tag, Row: row)))
                .ToLookup(entry => entry.Tag, entry => entry.Row, comparer);

            var plan = new LineSyncPlan();
            var candidates = new List<LineSyncAction>();
            foreach (var lines in drawingLines.GroupBy(line => LineDesignationParser.TagOf(ReadLineTag(line)), comparer)
                .Where(group => group.Key.Length > 0))
            {
                if (!lines.Any(line => string.IsNullOrWhiteSpace(line.Id))) continue;
                var matches = rowsByTag[lines.Key].ToArray();
                if (matches.Length == 0) continue;
                if (lines.Count() != 1 || matches.Length != 1)
                {
                    plan.IdFillSkips.Add($"«{lines.Key}»: обозначение повторяется на чертеже или в ВТ.");
                    continue;
                }
                var line = lines.First();
                var row = matches[0];
                var id = row.NN.Trim();
                if (!availableIds.Contains(id))
                {
                    plan.IdFillSkips.Add($"«{lines.Key}»: n/n «{id}» повторяется в ВТ или такой ID уже используется в проекте.");
                    continue;
                }
                candidates.Add(new LineSyncAction
                {
                    Line = line,
                    VtRow = row,
                    Changes = { new FieldChange { PropertyName = "ID", DisplayLabel = "ID", OldValue = line.Id, NewValue = id } },
                });
            }

            foreach (var matches in candidates.GroupBy(action => action.Changes[0].NewValue, comparer))
            {
                if (matches.Count() == 1)
                    plan.Actions.Add(matches.First());
                else
                    plan.IdFillSkips.Add($"n/n «{matches.Key}» соответствует нескольким линиям на чертеже.");
            }
            return plan;
        }

        // P3dLineGroup columns that hold a plain number. Only these are
        // compared numerically; codes/identifiers (Номер "001", Tag, picklist
        // codes, ГОСТ groups) stay strictly textual — "001" vs "1" IS a
        // change there.
        internal static List<string> FindWriteMismatches(LineSyncAction action,
            IReadOnlyDictionary<string, string> current, bool afterWrite)
        {
            var mismatches = new List<string>();
            void Check(string name, string expected)
            {
                if (!SameFieldValue(name, expected, current.GetValueOrDefault(name)) && !mismatches.Contains(name))
                    mismatches.Add(name);
            }

            if (!afterWrite)
            {
                Check("ID", action.Line.Id);
                Check("Tag", ReadLineTag(action.Line));
                if (action.Changes.Any(change => change.PropertyName == "Corrosion"))
                {
                    Check("CorrosionSpeed", action.Line.AllProperties.GetValueOrDefault("CorrosionSpeed"));
                    Check("srok", action.Line.AllProperties.GetValueOrDefault("srok"));
                }
            }
            foreach (var change in action.Changes)
                Check(change.PropertyName, afterWrite ? change.NewValue : change.OldValue);
            return mismatches;
        }

        static readonly HashSet<string> NumericProperties = ImportedFields
            .Where(field => field.Numeric).Select(field => field.Property)
            .Concat(new[] { "CorrosionSpeed", "TestD", "TestW", "srok", "Corrosion" })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// True when the ВТ value and the current P3dLineGroup value are the
        /// same. Numeric fields compare by VALUE when both sides parse: ВТ
        /// writes "1,6" while Plant 3D stores "1.6" (or "1.60"), and a plain
        /// string compare reported a change on every run — a "write" that
        /// changes nothing and buries the real differences in the review.
        /// Either side unparsable → ordinal text compare, as before.
        /// </summary>
        internal static bool SameFieldValue(string property, string newVal, string oldVal)
        {
            var a = (newVal ?? "").Trim();
            var b = (oldVal ?? "").Trim();
            if (string.Equals(a, b, StringComparison.Ordinal)) return true;
            if (!NumericProperties.Contains(property ?? "")) return false;
            return TryParseNumber(a, out var x) && TryParseNumber(b, out var y) && x == y;
        }

        // Comma or point as the decimal separator, never a thousands
        // separator (same rule as SpecNumber). decimal, not double, so
        // "0.1" and "0,10" are exactly equal.
        static bool TryParseNumber(string s, out decimal value)
        {
            value = 0;
            if (string.IsNullOrEmpty(s)) return false;
            return decimal.TryParse(s.Replace(',', '.'),
                System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }



    }
}

