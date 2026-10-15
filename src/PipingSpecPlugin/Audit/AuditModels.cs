using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PipingSpecPlugin.Audit
{
    public sealed class AuditOptions
    {
        public HashSet<string> Enabled { get; } = new(StringComparer.Ordinal);
        public double WallToleranceMm { get; set; } = 0.01;
        // null means all lines (scripted audit); an empty set means none.
        public HashSet<string> SelectedLines { get; set; }
        public bool Includes(AuditPart part) => SelectedLines == null || SelectedLines.Contains(part.LineKey ?? NoLine);
        public const string NoLine = "__NULL_LINE__";
        public const string NoLineLabel = "Без номера линии";
    }

    // Line order of the dialog and the report: natural, «без номера» last.
    public static class AuditLineOrder
    {
        public static int Compare(string a, string b)
        {
            bool noA = a == AuditOptions.NoLine, noB = b == AuditOptions.NoLine;
            if (noA || noB) return noA == noB ? 0 : noA ? 1 : -1;
            return Spec.VtValueParsing.NaturalCompare(a, b);
        }
    }

    public sealed class AuditLineChoice
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public int Count { get; set; }
        public override string ToString() => $"{Label} ({Count})";
    }

    public sealed class AuditPart
    {
        public string Key { get; set; }
        public string SelectionKey { get; set; }
        public string Source { get; set; }
        public string Description { get; set; }
        public string ClassName { get; set; }
        public string LineKey { get; set; }
        public string LineLabel { get; set; }
        // The properties could not be read, so LineKey is unknown, not empty.
        public bool LineUnknown { get; set; }
        public string Diameter { get; set; }
        public string WallRaw { get; set; }
        // Plant «LengthUnit» of the part: the catalogue unit of WallRaw (blank = mm).
        public string LengthUnit { get; set; }
        public double? WallMm { get; set; }
        public bool IsPipeOrElbow { get; set; }
        // A pipe or a pipe bend (the bend is made of pipe).
        public bool IsPipe { get; set; }
        public bool IsTeeOrReducer { get; set; }
        // Tee / reducer ends: S1 from the part's own row, the others from its
        // Plant 3D ports (PartPort). null when not read; EndsError says why.
        public List<AuditEnd> Ends { get; set; }
        public string EndsError { get; set; }
        public bool IsConnector { get; set; }
        public bool IsSupport { get; set; }
        public bool IsPipingComponent { get; set; }
        public HashSet<string> SupportedPartKeys { get; } = new(StringComparer.Ordinal);
        public bool ConnectionsRead { get; set; }
        // Why the ports / connections could not be read; null when they were.
        public string ConnectionsError { get; set; }
        public List<AuditPort> Ports { get; } = new();
        public HashSet<string> Neighbours { get; } = new(StringComparer.Ordinal);
    }

    public sealed class AuditEnd
    {
        public string Name { get; set; }
        public string Diameter { get; set; }
        public string WallRaw { get; set; }
        public string LengthUnit { get; set; }
        public double? WallMm => AuditRules.WallMillimetres(WallRaw, LengthUnit);
    }

    public sealed class AuditPort
    {
        public string Name { get; set; }
        public bool? Connected { get; set; }
    }

    public sealed class AuditIssue
    {
        public string RuleId { get; set; }
        public string Check { get; set; }
        public string Severity { get; set; }
        public string Line { get; set; }
        public string Message { get; set; }
        public List<string> Keys { get; set; } = new();
        public List<string> SelectionKeys { get; set; } = new();
        public string Sources { get; set; }
        // Where the issue is, WCS: minX, minY, minZ, maxX, maxY, maxZ. For a
        // collision the overlap of the two parts' boxes («Показать в модели»
        // zooms there); null when the place is just the elements themselves.
        public double[] Bounds { get; set; }
        public IEnumerable<string> SelectableKeys => SelectionKeys.Count > 0 ? SelectionKeys : Keys;
        public string Elements => string.Join(", ", Keys);
    }

    public sealed class AuditSnapshot
    {
        public string Drawing { get; set; }
        public List<AuditPart> Parts { get; } = new();
        public List<AuditIssue> GeometryIssues { get; } = new();
        public List<AuditIssue> ReadIssues { get; } = new();
        public int CollisionPairsChecked { get; set; }
        public int SupportContactsIgnored { get; set; }
        // Overlaps found between parts joined through a connection object
        // and therefore not reported (ConnectedJointPolicy).
        public int ConnectedJointsIgnored { get; set; }
        public int NonVolumetricCount { get; set; }
        public int XrefInstancesChecked { get; set; }
        public int XrefInstancesUnavailable { get; set; }
    }

    public interface IAuditRule
    {
        string Id { get; }
        string Title { get; }
        string Description { get; }
        IEnumerable<AuditIssue> Evaluate(AuditSnapshot snapshot, AuditOptions options);
    }

    public static class AuditRules
    {
        // Add a rule here: the options dialog and report use this same registry.
        public static IReadOnlyList<IAuditRule> All { get; } = new IAuditRule[]
        { new WallThicknessRule(), new ConnectivityRule(), new CollisionRule() };

        public static List<AuditIssue> Run(AuditSnapshot snapshot, AuditOptions options)
        {
            // A read issue is hidden only when every element it names is known
            // to lie on a line the user did not select. An element that could
            // not be read has no known line — it may be on a selected one — so
            // its issue is always shown (it used to be dropped unless «Без
            // номера линии» was ticked).
            var parts = new Dictionary<string, AuditPart>(StringComparer.Ordinal);
            foreach (var p in snapshot.Parts) parts.TryAdd(p.Key, p);
            bool Relevant(AuditIssue i) => options.SelectedLines == null || i.Keys.Count == 0 || i.Keys.Any(k =>
                !parts.TryGetValue(k, out var p) || string.IsNullOrWhiteSpace(p.LineKey) || options.Includes(p));
            var issues = snapshot.ReadIssues.Where(i => (i.RuleId == "read" || options.Enabled.Contains(i.RuleId)) && Relevant(i)).ToList();
            foreach (var rule in All.Where(r => options.Enabled.Contains(r.Id)))
                issues.AddRange(rule.Evaluate(snapshot, options));
            return issues;
        }

        public static double? ParsePositive(string value)
        {
            if (double.TryParse((value ?? "").Trim().Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number > 0)
                return number;
            return null;
        }

        // Plant 3D keeps WallThickness in the part's catalogue LengthUnit (the
        // unit of its Length and MatchingPipeOd), not in drawing units: a
        // metric catalogue stays in mm inside an inch or unitless drawing.
        // Same unit table as the specification (blank = mm); null = unknown.
        public static double? MillimetresPerUnit(string lengthUnit) =>
            Spec.LengthUnits.MetresPerUnit(lengthUnit) is double metres ? metres * 1000 : null;

        public static double? WallMillimetres(string wallRaw, string lengthUnit) =>
            ParsePositive(wallRaw) is double wall && MillimetresPerUnit(lengthUnit) is double factor ? wall * factor : null;

        // Plant 3D class names (PnPClassName): Pipe, PipeBend, Elbow, Tee, Reducer.
        public static bool IsPipeClass(string className) =>
            string.Equals(className, "Pipe", StringComparison.OrdinalIgnoreCase)
            || (className ?? "").Contains("PipeBend", StringComparison.OrdinalIgnoreCase);

        public static bool IsTeeOrReducerClass(string className) =>
            (className ?? "").Contains("Tee", StringComparison.OrdinalIgnoreCase)
            || (className ?? "").Contains("Reducer", StringComparison.OrdinalIgnoreCase);

        // The part's kind in Russian for messages (the class name meant
        // nothing to a reader: «PipeBend DN 100»).
        public static string KindOf(AuditPart part)
        {
            var name = part.ClassName ?? "";
            if (name.Contains("PipeBend", StringComparison.OrdinalIgnoreCase)) return "Изгиб";
            if (name.Contains("Elbow", StringComparison.OrdinalIgnoreCase)) return "Отвод";
            if (name.Contains("Reducer", StringComparison.OrdinalIgnoreCase)) return "Переход";
            if (name.Contains("Tee", StringComparison.OrdinalIgnoreCase)) return "Тройник";
            if (part.IsPipe || name.Equals("Pipe", StringComparison.OrdinalIgnoreCase)) return "Труба";
            return name;
        }

        // «Переход К-108x4.0-89x3.5 … [4C00]»: the kind only when the
        // description does not already start with it («Переход Переход …»).
        public static string NameOf(AuditPart part)
        {
            var kind = KindOf(part);
            var description = (part.Description ?? "").Trim();
            var text = description.Length == 0 ? kind
                : description.StartsWith(kind, StringComparison.OrdinalIgnoreCase) ? description : $"{kind} {description}";
            return $"{text} [{part.Key}]";
        }

        public static AuditIssue Issue(string rule, string check, string severity, string message, IEnumerable<AuditPart> parts)
        {
            var list = parts.ToList();
            return new AuditIssue { RuleId = rule, Check = check, Severity = severity, Message = message,
                Line = string.Join("; ", list.Select(p => p.LineLabel).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()),
                Keys = list.Select(p => p.Key).Distinct().ToList(),
                SelectionKeys = list.Select(p => p.SelectionKey ?? p.Key).Distinct().ToList(),
                Sources = string.Join("; ", list.Select(p => p.Source).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()) };
        }
    }
}
