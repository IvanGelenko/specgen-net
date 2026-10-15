using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PipingSpecPlugin.Audit
{
    // Line-data checks shared by the Plant 3D and Navisworks audits: slope,
    // status, placeholder spec, valve identifiers. They read only AuditPart
    // fields each host fills in (DrawingAuditReader / SnapshotBuilder).
    static class LineData
    {
        // A connection object often has no line number of its own: it belongs
        // to the line of a part it joins (the first one with a line).
        public static string LineOf(AuditPart part, IReadOnlyDictionary<string, AuditPart> byKey)
        {
            if (!string.IsNullOrWhiteSpace(part.LineKey) || !part.IsConnector) return part.LineKey;
            return part.Neighbours.Select(k => byKey.TryGetValue(k, out var n) ? n : null)
                .Where(n => n != null && !n.IsConnector && !string.IsNullOrWhiteSpace(n.LineKey))
                .Select(n => n.LineKey).OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();
        }

        // A part (or the connection object of a part) on a selected line.
        public static bool Included(AuditPart part, string line, AuditOptions options) =>
            options.SelectedLines == null || (line != null && options.SelectedLines.Contains(line)) || options.Includes(part);

        public static Dictionary<string, AuditPart> Index(AuditSnapshot snapshot)
        {
            var byKey = new Dictionary<string, AuditPart>(StringComparer.Ordinal);
            foreach (var p in snapshot.Parts) if (!byKey.ContainsKey(p.Key)) byKey.Add(p.Key, p);
            return byKey;
        }

        // The line's own value is the clear majority; the remark names (and
        // selects) the parts that differ from it — without one, all of them.
        public static AuditIssue Odd<T>(string rule, string title, string line, List<IGrouping<T, AuditPart>> groups,
            Func<IGrouping<T, AuditPart>, string> label, string what, string unit)
        {
            if (groups.Count < 2) return null;
            bool majority = groups[0].Count() > groups[1].Count();
            var odd = majority ? groups.Skip(1).ToList() : groups;
            var details = string.Join("; ", odd.Select(g => $"{label(g)}: " + string.Join(", ", g.Select(AuditRules.NameOf))));
            var message = majority
                ? $"{what} линии — {label(groups[0])} ({groups[0].Count()} {unit}); отличается: {details}."
                : $"{what} на линии разный: {details}.";
            var issue = AuditRules.Issue(rule, title, "Несоответствие", message, odd.SelectMany(g => g));
            issue.Magnitude = odd.Sum(g => g.Count());
            // The line compared — not «без номера» of a connection object on it.
            issue.Line = line;
            return issue;
        }
    }

    // One slope on the whole line — or none. A straight pipe's slope is its
    // rise over its run in plan; vertical pipes and steep runs (over 1:10, a
    // riser at an angle rather than a drainage slope) are left out.
    public sealed class SlopeRule : IAuditRule
    {
        public const double MaxSlope = 0.1;
        // Below this a pipe is level; slopes closer than this are one slope.
        public const double Tolerance = 0.0002;

        public string Id => "slope";
        public string Title => "Уклон линии";
        public string Description => "Прямые трубы одной линии — либо все с одинаковым уклоном, либо все без уклона. Уклон — перепад высот концов трубы к её длине в плане; вертикальные трубы и участки круче 1:10 не учитываются. Допуск 0,0002.";

        public IEnumerable<AuditIssue> Evaluate(AuditSnapshot snapshot, AuditOptions options)
        {
            var pipes = snapshot.Parts.Where(p => string.Equals((p.ClassName ?? "").Trim(), "Pipe", StringComparison.OrdinalIgnoreCase)
                && !p.LineUnknown && !string.IsNullOrWhiteSpace(p.LineKey) && options.Includes(p));
            foreach (var line in pipes.GroupBy(p => p.LineKey, StringComparer.OrdinalIgnoreCase))
            {
                var unknown = new List<AuditPart>();
                var measured = new List<(AuditPart Part, double Slope)>();
                foreach (var pipe in line)
                {
                    var slope = SlopeOf(pipe, out var known);
                    if (!known) unknown.Add(pipe);
                    else if (slope.HasValue) measured.Add((pipe, slope.Value));
                }
                if (unknown.Count > 0)
                    yield return AuditRules.Issue(Id, Title, "Не проверено",
                        $"Не определены концы {unknown.Count} труб — уклон не вычислен: " + string.Join(", ", unknown.Select(AuditRules.NameOf)), unknown);
                // Slopes within the tolerance are one slope; level pipes are 0.
                var clusters = new List<List<(AuditPart Part, double Slope)>>();
                foreach (var m in measured.OrderBy(m => m.Slope))
                {
                    var value = m.Slope < Tolerance ? 0 : m.Slope;
                    var last = clusters.LastOrDefault();
                    if (last != null && Math.Abs(Key(last) - value) <= Tolerance && (Key(last) == 0) == (value == 0)) last.Add((m.Part, value));
                    else clusters.Add(new List<(AuditPart, double)> { (m.Part, value) });
                }
                var groups = clusters.Select(c => (Slope: Key(c), Parts: c.Select(x => x.Part).ToList()))
                    .SelectMany(c => c.Parts.Select(p => (c.Slope, Part: p)))
                    .GroupBy(x => x.Slope, x => x.Part).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
                var issue = LineData.Odd(Id, Title, line.Key, groups, g => Label(g.Key), "Уклон", "труб");
                if (issue != null) yield return issue;
            }
        }

        static double Key(List<(AuditPart Part, double Slope)> cluster) => Math.Round(cluster.Average(c => c.Slope), 5);

        public static string Label(double slope) => slope == 0 ? "без уклона" : "i = " + slope.ToString("0.####", CultureInfo.GetCultureInfo("ru-RU"));

        // The slope of a straight pipe; null — vertical or steeper than
        // MaxSlope (not a slope); known = false when its ends are unknown.
        public static double? SlopeOf(AuditPart pipe, out bool known)
        {
            var ends = Ends(pipe.Axis ?? pipe.Ports.Where(p => p.Position != null).Select(p => p.Position).ToList());
            known = ends != null;
            if (!known) return null;
            double dx = ends[1][0] - ends[0][0], dy = ends[1][1] - ends[0][1], dz = ends[1][2] - ends[0][2];
            var run = Math.Sqrt(dx * dx + dy * dy);
            if (run < 1e-6) return null;
            var slope = Math.Abs(dz) / run;
            return slope > MaxSlope ? (double?)null : slope;
        }

        // The pipe's two ends: the farthest pair of its points. A pipe with a
        // stub-in has a third port on its axis (audit-model.dwg: 14 pipes) —
        // «exactly two» left those unchecked.
        public static List<double[]> Ends(IReadOnlyList<double[]> points)
        {
            var valid = (points ?? new List<double[]>()).Where(p => p != null && p.Length == 3).ToList();
            if (valid.Count < 2) return null;
            double best = -1; List<double[]> ends = null;
            for (int i = 0; i < valid.Count; i++)
                for (int j = i + 1; j < valid.Count; j++)
                {
                    double dx = valid[j][0] - valid[i][0], dy = valid[j][1] - valid[i][1], dz = valid[j][2] - valid[i][2];
                    var d = dx * dx + dy * dy + dz * dz;
                    if (d > best) { best = d; ends = new List<double[]> { valid[i], valid[j] }; }
                }
            return best > 0 ? ends : null;
        }
    }

    // A straight pipe piece is not shorter than 100 mm.
    public sealed class PipeLengthRule : IAuditRule
    {
        public const double MinMm = 100;

        public string Id => "pipelength";
        public string Title => "Длина прямых участков труб";
        public string Description => "Прямая труба не короче 100 мм. Длина — между концами трубы в модели.";

        public IEnumerable<AuditIssue> Evaluate(AuditSnapshot snapshot, AuditOptions options)
        {
            var pipes = snapshot.Parts.Where(p => string.Equals((p.ClassName ?? "").Trim(), "Pipe", StringComparison.OrdinalIgnoreCase) && options.Includes(p)).ToList();
            if (pipes.Count == 0) yield break;
            var mm = snapshot.DrawingMmPerUnit;
            var unknown = new List<AuditPart>();
            foreach (var pipe in pipes)
            {
                var length = LengthMm(pipe, mm);
                if (!length.HasValue) { unknown.Add(pipe); continue; }
                if (length.Value >= MinMm - 0.05) continue;
                var issue = AuditRules.Issue(Id, Title, "Несоответствие",
                    $"{AuditRules.NameOf(pipe)}: длина {length.Value:0.#} мм — меньше {MinMm:0} мм.", new[] { pipe });
                issue.Magnitude = MinMm - length.Value;
                yield return issue;
            }
            foreach (var line in unknown.GroupBy(p => p.LineKey ?? AuditOptions.NoLine, StringComparer.OrdinalIgnoreCase))
                yield return AuditRules.Issue(Id, Title, "Не проверено", (mm.HasValue ? "Не определены концы труб" : "Единицы чертежа (INSUNITS) не заданы")
                    + $" — длина не вычислена ({line.Count()}): " + string.Join(", ", line.Select(AuditRules.NameOf)), line);
        }

        public static double? LengthMm(AuditPart pipe, double? mmPerUnit)
        {
            var ends = SlopeRule.Ends(pipe.Axis ?? pipe.Ports.Where(p => p.Position != null).Select(p => p.Position).ToList());
            if (ends == null || !mmPerUnit.HasValue) return null;
            return Geometry.Distance(ends[0], ends[1]) * mmPerUnit.Value;
        }
    }

    // Neighbouring welds are at least 100 mm apart: the welds on one pipe —
    // its two ends, a stub-in next to a butt weld. Measured between the
    // welds' centres, along the pipe's axis. The welds at the ends of a
    // tee, elbow or reducer are as close as the factory fitting makes them
    // (a DN 50 tee: 67 mm) — every one was a remark (39 in the 0873
    // Navisworks model), so fittings are not checked.
    public sealed class WeldSpacingRule : IAuditRule
    {
        public const double MinMm = 100;

        public string Id => "welds";
        public string Title => "Расстояние между сварными швами";
        public string Description => "Между соседними сварными швами на одной трубе — не менее 100 мм (между центрами швов, по оси трубы): швы на концах короткой трубы, врезка рядом со стыковым швом. Швы на концах отводов, тройников и переходов не проверяются — их расстояние задано заводской деталью.";

        public static bool IsWeld(AuditPart part) => part.IsConnector && part.SubParts.Any(s =>
            (s.ClassName ?? "").IndexOf("Weld", StringComparison.OrdinalIgnoreCase) >= 0
            || (s.Description ?? "").IndexOf("weld", StringComparison.OrdinalIgnoreCase) >= 0
            || (s.Description ?? "").IndexOf("сварн", StringComparison.OrdinalIgnoreCase) >= 0);

        // The weld's centre: its own position, else the middle of its ports.
        public static double[] Centre(AuditPart weld)
        {
            if (weld.Position != null && weld.Position.Length == 3) return weld.Position;
            var points = weld.Ports.Where(p => p.Position != null && p.Position.Length == 3).Select(p => p.Position).ToList();
            if (points.Count == 0) return null;
            return new[] { points.Average(p => p[0]), points.Average(p => p[1]), points.Average(p => p[2]) };
        }

        public IEnumerable<AuditIssue> Evaluate(AuditSnapshot snapshot, AuditOptions options)
        {
            var byKey = LineData.Index(snapshot);
            var mm = snapshot.DrawingMmPerUnit;
            var reported = new HashSet<string>(StringComparer.Ordinal);
            var unknown = new Dictionary<string, AuditPart>(StringComparer.Ordinal);
            foreach (var part in snapshot.Parts.Where(p => string.Equals((p.ClassName ?? "").Trim(), "Pipe", StringComparison.OrdinalIgnoreCase) && options.Includes(p)))
            {
                var welds = part.Neighbours.Select(k => byKey.TryGetValue(k, out var w) ? w : null).Where(w => w != null && IsWeld(w))
                    .GroupBy(w => w.Key).Select(g => g.First()).OrderBy(w => w.Key, StringComparer.Ordinal).ToList();
                if (welds.Count < 2) continue;
                foreach (var weld in welds.Where(w => Centre(w) == null || !mm.HasValue)) unknown[weld.Key] = weld;
                var placed = welds.Where(w => Centre(w) != null).ToList();
                if (!mm.HasValue) continue;
                for (int i = 0; i < placed.Count; i++)
                    for (int j = i + 1; j < placed.Count; j++)
                    {
                        var distance = Geometry.Distance(Centre(placed[i]), Centre(placed[j])) * mm.Value;
                        if (distance >= MinMm - 0.05 || !reported.Add(placed[i].Key + "\n" + placed[j].Key)) continue;
                        var issue = AuditRules.Issue(Id, Title, "Несоответствие",
                            $"Сварные швы [{placed[i].Key}] и [{placed[j].Key}] на трубе {AuditRules.NameOf(part)}: {distance:0.#} мм между центрами — меньше {MinMm:0} мм.",
                            new[] { placed[i], part, placed[j] });
                        issue.Magnitude = MinMm - distance;
                        issue.Line = part.LineLabel;
                        yield return issue;
                    }
            }
            if (unknown.Count > 0)
                yield return AuditRules.Issue(Id, Title, "Не проверено", (mm.HasValue ? "Не определено положение сварных швов" : "Единицы чертежа (INSUNITS) не заданы")
                    + $" — расстояние не вычислено ({unknown.Count}): " + string.Join(", ", unknown.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => $"[{k}]")), unknown.Values);
        }
    }

    static class Geometry
    {
        public static double Distance(double[] a, double[] b)
        {
            double dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    // One status for every element of a line (pipes, fittings, valves,
    // supports, the line's welds and flanged joints).
    public sealed class StatusRule : IAuditRule
    {
        public string Id => "status";
        public string Title => "Статус элементов линии";
        public string Description => "У всех элементов одной линии — труб, деталей, арматуры, опор, соединений — одинаковый статус (Новое, Существующее, Демонтируемое…; «New» = «Новое»). Элементы без номера линии не проверяются.";

        public IEnumerable<AuditIssue> Evaluate(AuditSnapshot snapshot, AuditOptions options)
        {
            var byKey = LineData.Index(snapshot);
            var parts = snapshot.Parts.Where(p => !p.LineUnknown).Select(p => (Part: p, Line: LineData.LineOf(p, byKey)))
                .Where(x => !string.IsNullOrWhiteSpace(x.Line) && LineData.Included(x.Part, x.Line, options));
            foreach (var line in parts.GroupBy(x => x.Line, x => x.Part, StringComparer.OrdinalIgnoreCase))
            {
                // No status is missing data, not another status (Navisworks
                // often has none for supports): not compared, named apart.
                var blank = line.Where(p => string.IsNullOrWhiteSpace(StatusOf(p))).ToList();
                if (blank.Count > 0)
                {
                    var unknown = AuditRules.Issue(Id, Title, "Не проверено",
                        $"Статус не задан у {blank.Count} эл.: " + string.Join(", ", blank.Select(AuditRules.NameOf)), blank);
                    unknown.Line = line.Key;
                    yield return unknown;
                }
                var groups = line.Where(p => !string.IsNullOrWhiteSpace(StatusOf(p))).GroupBy(p => Normalize(StatusOf(p)), StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
                var issue = LineData.Odd(Id, Title, line.Key, groups, g => StatusOf(g.First()).Trim(), "Статус", "эл.");
                if (issue != null) yield return issue;
            }
        }

        // A connection object's status is its own, else its gasket's / bolts'.
        public static string StatusOf(AuditPart part) =>
            !string.IsNullOrWhiteSpace(part.Status) ? part.Status
            : part.SubParts.Select(s => s.Status).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";

        static readonly (string Key, string[] Words)[] Synonyms =
        {
            ("NEW", new[] { "new", "новое", "новый", "новая", "новые", "проектируемое", "проектируемый" }),
            ("EXISTING", new[] { "existing", "существующее", "существующий", "существующая", "существующие" }),
            ("DEMOLISH", new[] { "demolish", "demolished", "демонтируемое", "демонтируемый", "демонтаж", "демонтировать" }),
            ("FUTURE", new[] { "future", "будущее", "перспективное", "перспектива" }),
            ("RELOCATE", new[] { "relocate", "relocated", "переносимое", "перенос" }),
        };

        // The project's language does not matter: «New» = «Новое».
        public static string Normalize(string status)
        {
            var text = (status ?? "").Trim().ToLowerInvariant();
            foreach (var (key, words) in Synonyms) if (words.Contains(text)) return key;
            return text.ToUpperInvariant();
        }
    }

    // «PlaceHolder Metric» / «PlaceHolder Imperial» is Plant 3D's stand-in
    // spec: a part — most often the bolts or gasket of a flanged joint — it
    // took because the line's spec had none that fits.
    public sealed class PlaceholderSpecRule : IAuditRule
    {
        public string Id => "placeholder";
        public string Title => "Детали из PlaceHolder";
        public string Description => "Детали, крепёж и прокладки из спецификации-заглушки «PlaceHolder Metric» / «PlaceHolder Imperial»: Plant 3D подставил их, потому что в спецификации линии подходящей позиции нет. Их нужно заменить деталями спецификации.";

        public static bool IsPlaceholder(string spec) => (spec ?? "").IndexOf("placeholder", StringComparison.OrdinalIgnoreCase) >= 0;

        public IEnumerable<AuditIssue> Evaluate(AuditSnapshot snapshot, AuditOptions options)
        {
            var byKey = LineData.Index(snapshot);
            var found = new List<(AuditPart Part, string Line, string What)>();
            foreach (var part in snapshot.Parts)
            {
                var line = LineData.LineOf(part, byKey);
                if (!LineData.Included(part, line, options)) continue;
                var what = new List<string>();
                if (IsPlaceholder(part.Spec)) what.Add($"{AuditRules.NameOf(part)} — «{part.Spec.Trim()}»");
                // A joint's gasket and bolts: one entry per joint and spec.
                foreach (var spec in part.SubParts.Where(s => IsPlaceholder(s.Spec)).GroupBy(s => s.Spec.Trim(), StringComparer.OrdinalIgnoreCase))
                    what.Add($"{(part.IsConnector ? $"соединение [{part.Key}]" : AuditRules.NameOf(part))}: "
                        + string.Join(", ", spec.Select(s => string.IsNullOrWhiteSpace(s.Description) ? "вложенная деталь" : s.Description.Trim()).Distinct())
                        + $" — «{spec.Key}»");
                if (what.Count > 0) found.Add((part, line, string.Join("; ", what)));
            }
            foreach (var group in found.GroupBy(f => f.Line ?? AuditOptions.NoLine, StringComparer.OrdinalIgnoreCase))
            {
                var list = group.ToList();
                var issue = AuditRules.Issue(Id, Title, "Несоответствие",
                    $"Из спецификации-заглушки ({list.Count}): " + string.Join("; ", list.Select(f => f.What)) + ". Добавьте позиции в спецификацию линии и замените детали.",
                    list.Select(f => f.Part));
                issue.Magnitude = list.Count;
                issue.Line = group.Key == AuditOptions.NoLine ? AuditOptions.NoLineLabel : group.Key;
                yield return issue;
            }
        }
    }

    // Every valve carries its identifier (Plant «Tag»). A tag format left
    // with «?» — «?-?», «GV-?» — is not filled in.
    public sealed class ValveTagRule : IAuditRule
    {
        public string Id => "tags";
        public string Title => "Идентификаторы арматуры";
        public string Description => "У каждой единицы арматуры заполнен идентификатор (свойство Tag). Незаполненный шаблон («?-?») — нет идентификатора; частично заполненный («GV-?») — «Проверить».";

        public static bool IsValve(AuditPart part) => (part.ClassName ?? "").IndexOf("Valve", StringComparison.OrdinalIgnoreCase) >= 0;

        // Missing: blank or only the format's placeholders; partial: a «?» left.
        public static bool Missing(string tag) => Regex.IsMatch(tag ?? "", @"^[\s?\-_.:/]*$");
        public static bool Partial(string tag) => !Missing(tag) && (tag ?? "").IndexOf('?') >= 0;

        public IEnumerable<AuditIssue> Evaluate(AuditSnapshot snapshot, AuditOptions options)
        {
            var valves = snapshot.Parts.Where(p => IsValve(p) && options.Includes(p)).ToList();
            foreach (var line in valves.GroupBy(p => p.LineKey ?? AuditOptions.NoLine, StringComparer.OrdinalIgnoreCase))
            {
                var missing = line.Where(p => Missing(p.Tag)).ToList();
                if (missing.Count > 0)
                {
                    var issue = AuditRules.Issue(Id, Title, "Несоответствие",
                        $"Нет идентификатора у {missing.Count} ед. арматуры: " + string.Join(", ", missing.Select(p => AuditRules.NameOf(p) + Shown(p.Tag))), missing);
                    issue.Magnitude = missing.Count;
                    yield return issue;
                }
                var partial = line.Where(p => Partial(p.Tag)).ToList();
                if (partial.Count > 0)
                {
                    var issue = AuditRules.Issue(Id, Title, "Проверить",
                        "Идентификатор заполнен не полностью: " + string.Join(", ", partial.Select(p => $"{AuditRules.NameOf(p)} — «{p.Tag.Trim()}»")), partial);
                    issue.Magnitude = partial.Count;
                    yield return issue;
                }
            }
        }

        static string Shown(string tag) => string.IsNullOrWhiteSpace(tag) ? "" : $" («{tag.Trim()}»)";
    }
}
