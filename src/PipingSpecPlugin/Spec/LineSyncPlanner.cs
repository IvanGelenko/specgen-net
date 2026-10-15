using System;
using System.Collections.Generic;
using System.Linq;

namespace PipingSpecPlugin.Spec
{
    public static partial class LineSyncService
    {
        // Straight ВТ-field -> P3dLineGroup column mappings that need no
        // value translation (both sides are free text / numeric text).
        // Left out on purpose: Плотность / t°C-при-плотности have no
        // P3dLineGroup column yet. Срок службы now has one (`srok`,
        // added by the user) — handled below alongside the Прибавка на
        // коррозию calculation that depends on it, not through this
        // table (writing srok alone wouldn't also update Corrosion).
        static readonly (string Label, Func<VtRowData, string> VtValue, string Property)[] DirectFields =
        {
            ("Классификация по ГОСТ 32569",              r => r.GostGroup,           "GostGroup"),
            ("Классификация по ТР ТС 032-2013",          r => r.TrTsGroup,           "TRTSGroup"),
            ("Давление рабочее, МПа",                    r => r.WorkPressure,        "Pwork"),
            ("Температура рабочая, С",                   r => r.WorkTemperature,     "Tworking"),
            ("Давление расчетное, МПа",                  r => r.DesignPressure,      "PressureDesign"),
            ("Температура расчетная, С",                 r => r.DesignTemperature,   "Tdesign"),
            ("Скорость коррозии, мм/год",                r => r.CorrosionRate,       "CorrosionSpeed"),
            ("Термообработка",                           r => r.HeatTreatment,       "Thermo"),
            ("Доп. указания",                             r => r.Additional,          "Additional"),
            ("Толщина изоляции, мм",                      r => r.InsulationThickness, "InsulationThickness"),
            // Испытания на прочность/плотность come from ВТ as one cell
            // ("2,0/1,6") but TestD/TestW are separate numeric fields —
            // handled by VtValueParsing.SplitTestValues below, not
            // through this table.
            // Объем контроля швов comes from ВТ as "РД/УЗК 10%" (method +
            // percent in one cell) but NDControl is numeric-only —
            // handled by VtValueParsing.ExtractNdPercent below, also not
            // through here.
            // Срок службы / Прибавка на коррозию: srok is written as-is,
            // Corrosion is CorrosionSpeed × srok — both handled below,
            // not through this table, so the calculation always uses the
            // value about to be written rather than whatever's stale in
            // the project already.
        };

        // Fields decoded straight out of the "Обозначение" segment code
        // (see LineDesignationParser) rather than a separate ВТ column.
        // The letters/digits Plant 3D's own tag format encodes ARE the
        // picklist codes already (e.g. designation segment "H" ==
        // InsulationType code "H" — "Изоляция от теплопотерь"), so unlike
        // a text ВТ column these never go through the user's text->code
        // mapping table. A code that fails PnPTable validation is still
        // reported, never silently dropped. Material and environment code
        // are decoded from here (not from ВТ's own "Материал трубопровода"
        // / "Наименование транспортируемого продукта" text columns) per
        // the user's explicit call — the designation's letter codes
        // already ARE the picklist codes, the ВТ text columns would need
        // a lossy text->code guess instead. "Спутник" is Heat_type, not a
        // separate field — confirmed against the live P3dLineGroup
        // property list (Heat_type shows as "Тип обогрева" in the UI).
        static readonly (string Label, Func<LineDesignationParts, string> Value, string Property)[] DesignationFields =
        {
            ("Размер (ГОСТ)",                     p => p.NominalSize,     "Size2"),
            ("Код среды",                         p => p.EnvironmentCode, "Код_трубопровода"),
            ("Материал трубопровода",             p => p.MaterialCode,    "Material_pipe"),
            ("Код условного давления",            p => p.PressureCode,    "Ysl_pressure"),
            ("Тип присоединения арматуры",        p => p.FlangeCode,      "flanges"),
            ("Тип изоляции",                      p => p.InsulationCode,  "InsulationType"),
            ("Тип обогрева (Спутник)",            p => p.SputnikCode,     "Heat_type"),
            // "Номер" is handled separately: exactly three digits after the environment code.
        };

        public static string[] KnownLineGroupColumns =>
            DirectFields.Select(f => f.Property)
                .Concat(DesignationFields.Select(f => f.Property))
                .Concat(new[] { "Tag", "Номер", "TestD", "TestW", "NDControl", "srok", "Corrosion" })
                .ToArray();

        // Russian display labels for KnownLineGroupColumns, used by the
        // review window to render the "внутренняя ведомость" grid header
        // without duplicating this list a third time.
        public static IReadOnlyDictionary<string, string> KnownLineGroupColumnLabels
        {
            get
            {
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (label, _, property) in DirectFields) d[property] = label;
                foreach (var (label, _, property) in DesignationFields) d[property] = label;
                d["Tag"] = "Идентификатор";
                d["Номер"] = "Номер линии";
                d["TestD"] = "Испытания на прочность, МПа";
                d["TestW"] = "Испытания на плотность, МПа";
                d["NDControl"] = "Объем контроля швов, %";
                d["srok"] = "Срок службы, лет";
                d["Corrosion"] = "Прибавка на коррозию (расч.)";
                return d;
            }
        }

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

            var matchedLineRowIds = new HashSet<int>();

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
                {
                    plan.UnmatchedVtRows.Add(vt);
                    continue;
                }
                if (!byId.TryGetValue(nn, out var candidates) || candidates.Count == 0)
                {
                    plan.UnmatchedVtRows.Add(vt);
                    continue;
                }
                if (candidates.Count > 1)
                {
                    plan.DuplicateIds.Add(nn);
                    // Still plan changes against every matching line — the
                    // user resolves the duplicate in Plant 3D, not here.
                }

                foreach (var line in candidates)
                {
                    matchedLineRowIds.Add(line.RowId);
                    var action = new LineSyncAction { VtRow = vt, Line = line };

                    // line.AllProperties was already populated by
                    // ReadAllLines — no need for a second GetAllProperties
                    // round-trip per matched line.
                    string CurrentOf(string prop) =>
                        line.AllProperties.TryGetValue(prop, out var v) ? v : null;

                    foreach (var (label, vtValue, property) in DirectFields)
                    {
                        var newVal = (vtValue(vt) ?? "").Trim();
                        if (newVal == "") continue; // don't blank out an existing value from an empty ВТ cell.
                        var oldVal = CurrentOf(property) ?? "";
                        if (!SameFieldValue(property, newVal, oldVal))
                            action.Changes.Add(new FieldChange
                            {
                                PropertyName = property,
                                DisplayLabel = label,
                                OldValue = oldVal,
                                NewValue = newVal,
                            });
                    }

                    // Испытания на прочность: ВТ carries one "2,0/1,6"
                    // cell, TestD is a separate numeric field — split
                    // rather than trying to write the raw string (which
                    // is why this field went unfilled before: TestD
                    // rejected "2,0/1,6" as not a number).
                    var (testD, testStrengthTestW) = VtValueParsing.SplitTestValues(vt.TestStrength);
                    if (testD != null)
                    {
                        var oldVal = CurrentOf("TestD") ?? "";
                        if (!SameFieldValue("TestD", testD, oldVal))
                            action.Changes.Add(new FieldChange { PropertyName = "TestD", DisplayLabel = "Испытания на прочность, МПа", OldValue = oldVal, NewValue = testD });
                    }

                    // Испытания на плотность (TestW): ВТ has a DEDICATED
                    // "На герметичность" column (vt.TestTightness) that is
                    // NOT always equal to the second half of TestStrength
                    // — confirmed on real data, e.g. rows 3.1/3.2 in
                    // 873.РД-ТХМ.ВТ have TestStrength="1,29/0,39" but
                    // TestTightness="0,41"/"0,45" respectively. Prefer
                    // TestTightness when it parses; fall back to
                    // TestStrength's second half only when TestTightness
                    // is blank/unparsable (some ВТ rows leave it empty and
                    // only carry the combined cell).
                    var testWFromTightness = VtValueParsing.SplitTestValues(vt.TestTightness).testD;
                    var testW = testWFromTightness ?? testStrengthTestW;
                    if (testW != null)
                    {
                        var oldVal = CurrentOf("TestW") ?? "";
                        if (!SameFieldValue("TestW", testW, oldVal))
                            action.Changes.Add(new FieldChange { PropertyName = "TestW", DisplayLabel = "Испытания на плотность, МПа", OldValue = oldVal, NewValue = testW });
                    }
                    // Неразрушающий контроль: ВТ carries "РД/УЗК 10%",
                    // NDControl is numeric-only — pull just the percent.
                    var ndPercent = VtValueParsing.ExtractNdPercent(vt.NdControl);
                    if (ndPercent != null)
                    {
                        var oldVal = CurrentOf("NDControl") ?? "";
                        if (!SameFieldValue("NDControl", ndPercent, oldVal))
                            action.Changes.Add(new FieldChange { PropertyName = "NDControl", DisplayLabel = "Объем контроля швов, %", OldValue = oldVal, NewValue = ndPercent });
                    }

                    // Срок службы (srok) -> years extracted from ВТ's
                    // "лет/мм" composite cell, then Прибавка на коррозию
                    // (Corrosion) = CorrosionSpeed × srok. Uses the
                    // CorrosionSpeed value ВТ is providing THIS run
                    // (vt.CorrosionRate), not whatever's already on the
                    // line — the two numbers must come from the same
                    // source or the product is meaningless. Skipped
                    // entirely if either input is missing/unparsable
                    // rather than writing a bogus 0.
                    var srokYears = VtValueParsing.ExtractServiceLifeYears(vt.ServiceLife);
                    if (srokYears != null)
                    {
                        var oldSrok = CurrentOf("srok") ?? "";
                        if (!SameFieldValue("srok", srokYears, oldSrok))
                            action.Changes.Add(new FieldChange { PropertyName = "srok", DisplayLabel = "Срок службы, лет", OldValue = oldSrok, NewValue = srokYears });

                        var corrosion = VtValueParsing.ComputeCorrosionAllowance(vt.CorrosionRate, vt.ServiceLife);
                        if (corrosion != null)
                        {
                            var oldCorrosion = CurrentOf("Corrosion") ?? "";
                            if (!SameFieldValue("Corrosion", corrosion, oldCorrosion))
                                action.Changes.Add(new FieldChange { PropertyName = "Corrosion", DisplayLabel = "Прибавка на коррозию (расч.)", OldValue = oldCorrosion, NewValue = corrosion });
                        }
                    }

                    // Tag keeps the designation minus ВТ remarks in parentheses; Номер receives only the three-digit line number.
                    var designationTag = LineDesignationParser.TagOf(vt.Designation);
                    if (designationTag != "")
                    {
                        var oldTag = CurrentOf("Tag") ?? "";
                        if (!string.Equals(designationTag, oldTag.Trim(), StringComparison.Ordinal))
                            action.Changes.Add(new FieldChange { PropertyName = "Tag", DisplayLabel = "Идентификатор", OldValue = oldTag, NewValue = designationTag });
                    }
                    var lineNumber = LineDesignationParser.ParseSyncLineNumber(designationTag);
                    if (lineNumber != null)
                    {
                        var oldNumber = CurrentOf("Номер") ?? "";
                        if (!string.Equals(lineNumber, oldNumber.Trim(), StringComparison.Ordinal))
                            action.Changes.Add(new FieldChange { PropertyName = "Номер", DisplayLabel = "Номер линии", OldValue = oldNumber, NewValue = lineNumber });
                    }
                    else if (!string.IsNullOrWhiteSpace(vt.Designation))
                        action.PicklistRejections.Add("Трёхзначный номер после кода среды не распознан; поле Номер оставлено без изменений.");

                    var parts = LineDesignationParser.Parse(designationTag);
                    if (parts == null)
                    {
                        // Older tag formats (e.g. "P-22002") don't carry
                        // the segment encoding at all — not an error, just
                        // nothing to decode. Only flag it when ВТ actually
                        // has a designation that failed to parse, so a
                        // genuinely blank cell doesn't spam the review.
                        if (!string.IsNullOrWhiteSpace(vt.Designation))
                            action.PicklistRejections.Add(
                                $"Обозначение «{vt.Designation.Split('\n')[0].Trim()}» не распознано форматом "
                                + "«размер-код+номер-материал+давление+фланец-изоляция+спутник» — "
                                + "поля Size2/Material_pipe/Ysl_pressure/flanges/InsulationType/Sputnik не заполнены из него.");
                    }
                    else
                    {
                        foreach (var (label, value, property) in DesignationFields)
                        {
                            var newVal = value(parts);
                            if (string.IsNullOrEmpty(newVal)) continue; // optional segment (e.g. flanges) can be absent.
                            var oldVal = CurrentOf(property) ?? "";
                            if (!string.Equals(newVal, oldVal.Trim(), StringComparison.Ordinal))
                                action.Changes.Add(new FieldChange
                                {
                                    PropertyName = property,
                                    DisplayLabel = label,
                                    OldValue = oldVal,
                                    NewValue = newVal,
                                });
                        }
                    }

                    if (action.Changes.Count > 0 || action.PicklistRejections.Count > 0)
                        plan.Actions.Add(action);
                }
            }

            foreach (var line in lines)
                if (!string.IsNullOrWhiteSpace(line.Id) && !matchedLineRowIds.Contains(line.RowId))
                    plan.UnmatchedLines.Add(line);

            return plan;
        }

        /// <summary>
        /// Builds the "внутренняя ведомость" review table: every project
        /// line with a non-blank Id, one row per line, with every
        /// property СИНХРОНИЗАЦИЯ ВТ can write (KnownLineGroupColumns).
        /// Reflects current Plant 3D state — call again after Apply to
        /// see the result rather than trying to predict it here. Sorted
        /// by Id so repeated runs render in a stable order.
        /// </summary>
        public static List<LineLedgerRow> BuildLedger(List<LineGroupInfo> lines, bool collapseDuplicates = false)
        {
            var rows = new List<LineLedgerRow>();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.Id)) continue;
                var row = new LineLedgerRow { RowId = line.RowId, Id = line.Id, Tag = line.Tag };
                foreach (var col in KnownLineGroupColumns)
                    row.Values[col] = line.AllProperties.TryGetValue(col, out var v) ? v : "";
                rows.Add(row);
            }
            if (collapseDuplicates)
                rows = rows.GroupBy(r => new { Id = r.Id.Trim().ToUpperInvariant(), Tag = (r.Tag ?? "").Trim() })
                    .Select(group =>
                    {
                        var first = group.First();
                        var merged = new LineLedgerRow { RowId = first.RowId, Id = first.Id.Trim(), Tag = (first.Tag ?? "").Trim() };
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
            var idCounts = rows.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
                if (idCounts.TryGetValue(row.Id, out var n) && n > 1)
                    row.Tag = $"⚠ дубль ID · {row.Tag}";

            return rows;
        }

        // P3dLineGroup columns that hold a plain number. Only these are
        // compared numerically; codes/identifiers (Номер "001", Tag, picklist
        // codes, ГОСТ groups) stay strictly textual — "001" vs "1" IS a
        // change there.
        static readonly HashSet<string> NumericProperties = new(StringComparer.OrdinalIgnoreCase)
        {
            "Pwork", "Tworking", "PressureDesign", "Tdesign", "CorrosionSpeed",
            "InsulationThickness", "TestD", "TestW", "NDControl", "srok", "Corrosion",
        };

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

        public static bool IsLedgerVisibleColumn(string column) =>
            !string.Equals(column, "Tag", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(column, "Номер", StringComparison.OrdinalIgnoreCase);

    }
}

