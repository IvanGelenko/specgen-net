using System;
using System.Collections.Generic;
using System.Linq;
using static PipingSpecPlugin.Spec.VtFieldMappings;

namespace PipingSpecPlugin.Spec
{
    public static class LineSyncPlanner
    {
        /// <summary>Builds a preview by designation, with ID as a restricted fallback.</summary>
        public static LineSyncPlan BuildPlan(List<VtRowData> vtRows, List<LineGroupInfo> lines,
            IReadOnlySet<int> drawingIds = null)
        {
            var plan = new LineSyncPlan();
            foreach (var match in VtLineMatcher.Match(vtRows, lines, plan, drawingIds))
            {
                var line = match.Line;
                plan.MatchedRowIds.Add(line.RowId);
                if (!string.IsNullOrWhiteSpace(line.Id) && !string.Equals(line.Id.Trim(), match.Row.NN.Trim(), StringComparison.OrdinalIgnoreCase))
                    plan.IdConflicts[line.RowId] = $"«{VtLineMatcher.GetTag(line)}»: сопоставлено по обозначению. ID в модели «{line.Id}», n/n в ВТ «{match.Row.NN}». ID сохранён.";
                var action = BuildAction(match.Row, line, match.Designation);
                if (action.Changes.Count > 0 || action.PicklistRejections.Count > 0)
                    plan.Actions.Add(action);
            }

            plan.UnmatchedLines.AddRange(lines.Where(line => !string.IsNullOrWhiteSpace(line.Id)
                && !plan.MatchedRowIds.Contains(line.RowId) && !plan.BlockedLines.ContainsKey(line.RowId)));
            return plan;
        }

        static LineSyncAction BuildAction(VtRowData vt, LineGroupInfo line, string designationTag)
        {
            var action = new LineSyncAction { VtRow = vt, Line = line };
            foreach (var field in DirectFields)
                AddChange(action, field.Property, field.ValueFor(vt));

            AddTestChanges(action);
            AddChange(action, NdControl.Property, NdControl.ValueFor(vt));
            AddCorrosionChanges(action);
            AddDesignationChanges(action, designationTag);
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

        static void AddDesignationChanges(LineSyncAction action, string designationTag)
        {
            var vt = action.VtRow;
            var oldTag = VtLineMatcher.GetTag(action.Line).Trim();
            if (oldTag.Length == 0)
                AddChange(action, "Tag", designationTag, oldTag);

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


        internal static LineSyncPlan BuildIdFillPlan(IEnumerable<VtRowData> vtRows,
            IEnumerable<LineGroupInfo> drawingLines, IEnumerable<LineGroupInfo> projectLines,
            Func<LineGroupInfo, bool> canReleaseId = null)
        {
            var comparer = StringComparer.OrdinalIgnoreCase;
            var rows = vtRows.ToList();
            var project = projectLines.ToList();
            var drawing = drawingLines.ToList();
            var drawingTargets = drawing.Where(line => string.IsNullOrWhiteSpace(line.Id)).Select(line => line.RowId).ToHashSet();
            var drawingIds = drawing.Select(line => line.RowId).ToHashSet();
            var occupiedIds = project.Where(line => !string.IsNullOrWhiteSpace(line.Id)).ToLookup(line => line.Id.Trim(), comparer);
            var plan = new LineSyncPlan();
            var diagnostics = new LineSyncPlan();
            var matches = VtLineMatcher.Match(rows, project, diagnostics, drawingIds, allowIdFallback: false);
            plan.IdFillSkips.AddRange(diagnostics.BlockedLines.Where(item => drawingTargets.Contains(item.Key))
                .Select(item => item.Value).Distinct());
            var repeatedIds = diagnostics.DuplicateVtNN.ToHashSet(comparer);
            var candidates = new List<LineSyncAction>();
            var releases = new Dictionary<int, LineSyncAction>();

            foreach (var match in matches.Where(item => drawingTargets.Contains(item.Line.RowId)))
            {
                var tag = VtLineMatcher.GetTag(match.Line);
                var id = match.Row.NN.Trim();
                if (repeatedIds.Contains(id))
                {
                    plan.IdFillSkips.Add($"«{tag}»: n/n «{id}» повторяется в ВТ.");
                    continue;
                }

                if (occupiedIds.Contains(id))
                {
                    var holders = occupiedIds[id].ToList();
                    if (holders.Count == 1 && IsIdTransferBetweenSameTags(holders[0], match.Line)
                        && canReleaseId?.Invoke(holders[0]) == true)
                    {
                        var holder = holders[0];
                        releases.Add(match.Line.RowId, new LineSyncAction
                        {
                            Line = holder,
                            VtRow = match.Row,
                            Changes = { new FieldChange { PropertyName = "ID", DisplayLabel = "ID", OldValue = holder.Id, NewValue = "" } },
                        });
                    }
                    else
                    {
                        var descriptions = string.Join(", ", holders.Select(line => $"«{VtLineMatcher.GetTag(line)}» (PnPID {line.RowId})"));
                        plan.IdFillSkips.Add($"«{tag}»: ID «{id}» уже занимает {descriptions}.");
                        continue;
                    }
                }

                candidates.Add(new LineSyncAction
                {
                    Line = match.Line,
                    VtRow = match.Row,
                    Changes = { new FieldChange { PropertyName = "ID", DisplayLabel = "ID", OldValue = match.Line.Id, NewValue = id } },
                });
            }

            foreach (var group in candidates.GroupBy(action => action.Changes[0].NewValue, comparer))
            {
                if (group.Count() == 1)
                {
                    var assignment = group.First();
                    if (releases.TryGetValue(assignment.Line.RowId, out var release)) plan.Actions.Add(release);
                    plan.Actions.Add(assignment);
                }
                else
                    plan.IdFillSkips.Add($"n/n «{group.Key}» соответствует нескольким линиям на чертеже.");
            }
            return plan;
        }

        static bool IsIdTransferBetweenSameTags(LineGroupInfo holder, LineGroupInfo recipient)
        {
            var tag = VtLineMatcher.GetTag(recipient).Trim();
            var holderTag = VtLineMatcher.GetTag(holder).Trim().TrimEnd('?').TrimEnd();
            return holder.RowId != recipient.RowId && tag.Length > 0
                && string.Equals(holderTag, tag, StringComparison.OrdinalIgnoreCase);
        }

        internal static List<LineSyncAction> ValidateIdWrites(IReadOnlyList<LineSyncAction> actions,
            IReadOnlyList<LineGroupInfo> currentLines, Func<LineGroupInfo, bool> canReleaseId)
        {
            var comparer = StringComparer.OrdinalIgnoreCase;
            var currentByRowId = currentLines.ToDictionary(line => line.RowId);
            var occupied = currentLines.Where(line => !string.IsNullOrWhiteSpace(line.Id))
                .ToLookup(line => line.Id.Trim(), comparer);
            var edits = actions.SelectMany(action => action.Changes.Where(change => change.PropertyName == "ID")
                .Select(change => (action.Line.RowId, Change: change))).ToList();
            var assignments = edits.Where(edit => !string.IsNullOrWhiteSpace(edit.Change.NewValue))
                .ToLookup(edit => edit.Change.NewValue.Trim(), comparer);
            var releasedIds = new HashSet<string>(comparer);
            var releaseRows = new HashSet<int>();

            foreach (var edit in edits.Where(edit => string.IsNullOrWhiteSpace(edit.Change.NewValue)))
            {
                var id = edit.Change.OldValue?.Trim() ?? "";
                var holders = occupied[id].ToList();
                var recipients = assignments[id].ToList();
                if (holders.Count != 1 || holders[0].RowId != edit.RowId || recipients.Count != 1
                    || !currentByRowId.TryGetValue(recipients[0].RowId, out var recipient)
                    || !IsIdTransferBetweenSameTags(holders[0], recipient)
                    || !canReleaseId(holders[0]) || !releasedIds.Add(id))
                    throw new InvalidOperationException($"Условия переноса ID «{id}» изменились. Загрузите ВТ повторно.");
                releaseRows.Add(edit.RowId);
            }

            foreach (var group in assignments)
            {
                if (group.Count() != 1 || (occupied.Contains(group.Key) && !releasedIds.Contains(group.Key)))
                    throw new InvalidOperationException($"ID «{group.Key}» уже используется. Загрузите ВТ повторно.");
                var recipientId = group.Single().RowId;
                if (!currentByRowId.TryGetValue(recipientId, out var recipient) || !string.IsNullOrWhiteSpace(recipient.Id))
                    throw new InvalidOperationException($"Состояние ID строки PnPID {recipientId} изменилось. Загрузите ВТ повторно.");
            }
            return actions.OrderByDescending(action => releaseRows.Contains(action.Line.RowId)).ToList();
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
                Check("Tag", VtLineMatcher.GetTag(action.Line));
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

