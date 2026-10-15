using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PipingSpecPlugin.Audit
{
    // Pipes and fittings of one line are of one material. Valves, instruments
    // and other bodies, supports and the connection objects' gaskets / bolts
    // legitimately differ — in audit-model.dwg a valve's Material reads
    // «хладостойкая сталь», a support's «ВСт3пс» — and are not compared.
    public sealed class MaterialRule : IAuditRule
    {
        public string Id => "material";
        public string Title => "Материал деталей линии";
        public string Description => "Трубы, отводы, изгибы, тройники, переходы, фланцы, заглушки и другие фасонные детали одной линии — одного материала (свойство Material, при его отсутствии — MaterialCode; «Сталь 20» = «20»). Арматура, приборы, опоры, прокладки и крепёж не сравниваются. Детали без номера линии не проверяются.";

        static readonly string[] NotCompared = { "Connector", "Support", "Valve", "Instrument", "Equipment", "Nozzle", "Gasket",
            "Bolt", "Weld", "Strainer", "Filter", "Trap", "SightGlass", "Meter" };

        public static bool IsCompared(AuditPart part)
        {
            if (part.IsConnector || part.IsSupport) return false;
            var name = part.ClassName ?? "";
            return name.Length > 0 && !NotCompared.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<AuditIssue> Evaluate(AuditSnapshot snapshot, AuditOptions options)
        {
            var parts = snapshot.Parts.Where(p => IsCompared(p) && !p.LineUnknown && !string.IsNullOrWhiteSpace(p.LineKey) && options.Includes(p));
            foreach (var line in parts.GroupBy(p => p.LineKey, StringComparer.OrdinalIgnoreCase))
            {
                var blank = line.Where(p => string.IsNullOrWhiteSpace(MaterialOf(p))).ToList();
                if (blank.Count > 0)
                    yield return AuditRules.Issue(Id, Title, "Не проверено",
                        $"Материал не задан у {blank.Count} дет.: " + string.Join(", ", blank.Select(AuditRules.NameOf)), blank);
                var groups = line.Where(p => !string.IsNullOrWhiteSpace(MaterialOf(p)))
                    .GroupBy(p => Normalize(MaterialOf(p)), StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
                if (groups.Count < 2) continue;
                // The line's own material is the clear majority; the issue
                // then names (and selects) the parts that differ from it.
                bool majority = groups[0].Count() > groups[1].Count();
                string Label(IGrouping<string, AuditPart> g) => MaterialOf(g.First()).Trim();
                var odd = majority ? groups.Skip(1).ToList() : groups;
                var details = string.Join("; ", odd.Select(g => $"{Label(g)}: " + string.Join(", ", g.Select(AuditRules.NameOf))));
                var message = majority
                    ? $"Материал линии — {Label(groups[0])} ({groups[0].Count()} дет.); отличается: {details}."
                    : $"Разные материалы на линии: {details}.";
                var issue = AuditRules.Issue(Id, Title, "Несоответствие", message, odd.SelectMany(g => g));
                issue.Magnitude = odd.Sum(g => g.Count());
                yield return issue;
            }
        }

        public static string MaterialOf(AuditPart part) =>
            string.IsNullOrWhiteSpace(part.Material) ? part.MaterialCode ?? "" : part.Material;

        // «Сталь 20» = «20», case and spacing ignored, Latin look-alikes in a
        // Russian grade read as Cyrillic («09Г2C» with a Latin C = «09Г2С»).
        public static string Normalize(string material)
        {
            var text = Regex.Replace((material ?? "").Trim(), @"\s+", " ");
            text = Regex.Replace(text, @"^сталь\s+", "", RegexOptions.IgnoreCase);
            text = text.ToUpperInvariant();
            if (Regex.IsMatch(text, @"[Ѐ-ӿ]"))
            {
                var map = new Dictionary<char, char> { ['A'] = 'А', ['B'] = 'В', ['C'] = 'С', ['E'] = 'Е', ['H'] = 'Н', ['K'] = 'К',
                    ['M'] = 'М', ['O'] = 'О', ['P'] = 'Р', ['T'] = 'Т', ['X'] = 'Х', ['Y'] = 'У' };
                var builder = new StringBuilder(text.Length);
                foreach (var c in text) builder.Append(map.TryGetValue(c, out var cyr) ? cyr : c);
                text = builder.ToString();
            }
            return text.Replace(" ", "");
        }
    }
}
