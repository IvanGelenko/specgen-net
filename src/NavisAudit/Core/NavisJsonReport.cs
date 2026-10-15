using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PipingSpecPlugin.Audit;

namespace NavisAudit.Core
{
    // The JSON report of a scripted run (same fields as the Plant 3D
    // PSAUDITJSON). Written by hand: Navisworks (.NET Framework) has no
    // System.Text.Json, and a second copy of it in the host is not worth it.
    public static class NavisJsonReport
    {
        public static string Serialize(NavisModel model, AuditOptions options, IReadOnlyList<AuditIssue> issues)
        {
            var s = model.Snapshot;
            var root = new Dictionary<string, object>
            {
                ["SchemaVersion"] = 2,
                ["Host"] = "Navisworks",
                ["CreatedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["Drawing"] = s.Drawing,
                ["Checks"] = options.Enabled.OrderBy(c => c, StringComparer.Ordinal).ToList(),
                ["SelectedLines"] = options.SelectedLines?.OrderBy(c => c, StringComparer.Ordinal).ToList(),
                ["WallToleranceMm"] = options.WallToleranceMm,
                ["CollisionToleranceMm"] = options.CollisionToleranceMm,
                ["StructureCollisions"] = options.StructureCollisions,
                ["PartCount"] = s.Parts.Count,
                ["ProxyCount"] = model.ProxyCount,
                ["DuplicateCount"] = model.DuplicateOf.Count,
                ["CollisionPairsChecked"] = s.CollisionPairsChecked,
                ["SupportContactsIgnored"] = s.SupportContactsIgnored,
                ["ConnectedJointsIgnored"] = s.ConnectedJointsIgnored,
                ["CollisionsWithinTolerance"] = s.CollisionsWithinTolerance,
                ["StructureJointsIgnored"] = s.StructureJointsIgnored,
                ["UnplacedSources"] = model.UnplacedSources,
                ["Issues"] = issues.Select(i => new Dictionary<string, object>
                {
                    ["RuleId"] = i.RuleId, ["Check"] = i.Check, ["Severity"] = i.Severity, ["Line"] = i.Line, ["Message"] = i.Message,
                    ["Keys"] = i.Keys, ["Sources"] = i.Sources, ["Bounds"] = i.Bounds, ["BetweenStructures"] = i.BetweenStructures, ["Magnitude"] = i.Magnitude,
                }).ToList(),
            };
            var builder = new StringBuilder();
            Write(builder, root, 0);
            return builder.ToString();
        }

        // Parts as read, for checking the reading itself.
        public static string Parts(NavisModel model) =>
            Text(model.Snapshot.Parts.Select(p => (object)new Dictionary<string, object>
            {
                ["Key"] = p.Key, ["Class"] = p.ClassName, ["Line"] = p.LineKey, ["Size"] = p.Diameter, ["Wall"] = p.WallRaw, ["WallMm"] = p.WallMm,
                ["Unit"] = p.LengthUnit, ["Material"] = p.Material, ["Source"] = p.Source, ["Description"] = p.Description, ["Box"] = model.Boxes.TryGetValue(p.Key, out var box) ? box : null,
                ["ConnectionsRead"] = p.ConnectionsRead, ["ConnectionsError"] = p.ConnectionsError,
                ["Ports"] = p.Ports.Select(q => q.Name + "=" + (q.Connected?.ToString() ?? "?")).ToList(),
                ["Neighbours"] = p.Neighbours.OrderBy(k => k, StringComparer.Ordinal).ToList(),
                ["Supports"] = p.SupportedPartKeys.ToList(),
                ["Ends"] = p.Ends?.Select(e => $"{e.Name}: Ду {e.Diameter}, s {e.WallRaw} {e.LengthUnit}").ToList(), ["EndsError"] = p.EndsError,
            }).ToList());

        public static string Text(object value)
        {
            var builder = new StringBuilder();
            Write(builder, value, 0);
            return builder.ToString();
        }

        static void Write(StringBuilder b, object value, int indent)
        {
            switch (value)
            {
                case null: b.Append("null"); return;
                case string text: Quote(b, text); return;
                case bool flag: b.Append(flag ? "true" : "false"); return;
                case double number: b.Append(double.IsNaN(number) || double.IsInfinity(number) ? "null" : number.ToString("R", CultureInfo.InvariantCulture)); return;
                case int whole: b.Append(whole.ToString(CultureInfo.InvariantCulture)); return;
                case IDictionary<string, object> map:
                    b.Append("{");
                    bool first = true;
                    foreach (var pair in map)
                    {
                        b.Append(first ? "\n" : ",\n").Append(' ', (indent + 1) * 2);
                        first = false;
                        Quote(b, pair.Key);
                        b.Append(": ");
                        Write(b, pair.Value, indent + 1);
                    }
                    if (!first) b.Append('\n').Append(' ', indent * 2);
                    b.Append("}");
                    return;
                case IEnumerable list:
                    b.Append("[");
                    bool any = false;
                    foreach (var element in list)
                    {
                        b.Append(any ? ",\n" : "\n").Append(' ', (indent + 1) * 2);
                        any = true;
                        Write(b, element, indent + 1);
                    }
                    if (any) b.Append('\n').Append(' ', indent * 2);
                    b.Append("]");
                    return;
                default:
                    Quote(b, Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
            }
        }

        static void Quote(StringBuilder b, string text)
        {
            b.Append('"');
            foreach (var c in text)
            {
                if (c == '"' || c == '\\') b.Append('\\').Append(c);
                else if (c == '\n') b.Append("\\n");
                else if (c == '\r') b.Append("\\r");
                else if (c == '\t') b.Append("\\t");
                else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
                else b.Append(c);
            }
            b.Append('"');
        }
    }
}
