using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PipingSpecPlugin.Audit
{
    // Plant 3D sizes a bolt set as base length − 2 × its FlangeThickness +
    // the two flanges' FlangeThickness, rounded up by the bolt length mapping;
    // the gasket is ignored, the nuts are in the base length (Autodesk support
    // articles). A wafer body or a thick gasket the base length did not allow
    // for, or a flange changed after the joint was made, leaves the bolt short.
    // This checks the set against the joint itself.
    public sealed class BoltLengthRule : IAuditRule
    {
        // Nut height per thread diameter: DIN 934 / ГОСТ 5915 m = 0.8d, the
        // lowest of the usual hex nuts — a shortfall is real, not a margin.
        public const double NutHeightPerDiameter = 0.8;
        const double ToleranceMm = 0.05;

        public string Id => "bolts";
        public string Title => "Длина болтов и шпилек";
        public string Description => "Комплект болтов фланцевого соединения не короче пакета: толщины фланцев по каталогу, зазоры прокладок и корпус межфланцевой арматуры или поворотной заглушки по модели, плюс гайки высотой 0,8·d (у шпильки две, у болта одна). Шайбы и выступ резьбы не учитываются. Plant 3D толщину прокладки в длине болта не учитывает. Если у фланца арматуры толщина в каталоге не задана, она принимается равной ответному фланцу; если комплекты стоят по обе стороны поворотной заглушки, каждый сравнивается со всем пакетом — в обоих случаях нехватка длины отмечается «Проверить». Соединения с арматурой под проушины (LUG) не проверяются.";

        public IEnumerable<AuditIssue> Evaluate(AuditSnapshot snapshot, AuditOptions options)
        {
            var byKey = new Dictionary<string, AuditPart>(StringComparer.Ordinal);
            foreach (var p in snapshot.Parts) byKey.TryAdd(p.Key, p);
            // Links stored on the other side, indexed once: scanning every
            // part's links per connector grew with the square of the model.
            var reverse = Reverse(snapshot.Parts);
            foreach (var connector in snapshot.Parts.Where(p => p.IsConnector && (p.BoltSets.Count > 0 || p.BoltsError != null)))
            {
                var faces = Faces(connector, reverse);
                // A joint is on the selected lines if the connector or a part
                // it joins is (connectors often have no line number).
                if (!options.Includes(connector) && !faces.Any(f => byKey.TryGetValue(f.Key, out var p) && options.Includes(p))) continue;
                var parts = new List<AuditPart> { connector };
                parts.AddRange(faces.Select(f => byKey.GetValueOrDefault(f.Key)).Where(p => p != null));
                var name = $"Соединение [{connector.Key}]";
                if (connector.BoltsError != null)
                {
                    yield return AuditRules.Issue(Id, Title, "Не проверено", $"{name}: комплект болтов не прочитан: {connector.BoltsError.Trim()}", parts);
                    continue;
                }
                var caveats = new List<string>();
                var stack = Stack(connector, faces, byKey, snapshot, reverse, parts, caveats, out var why, out var twin);
                // The same sets on both sides of a spectacle blind: one remark
                // (from the joint with the lower key) naming both joints.
                if (twin != null && string.CompareOrdinal(twin.Key, connector.Key) < 0 && SameSets(twin, connector)) continue;
                if (twin != null && SameSets(twin, connector)) name = $"Соединения [{connector.Key}] и [{twin.Key}]";
                foreach (var set in connector.BoltSets)
                {
                    var issue = Check(set, stack, why, caveats, name, parts);
                    if (issue != null) yield return issue;
                }
            }
        }

        // A stack resting on an assumption (caveats) makes a shortfall
        // «Проверить», not «Несоответствие».
        AuditIssue Check(AuditBoltSet set, List<(string What, double Mm)> stack, string why, List<string> caveats, string name, List<AuditPart> parts)
        {
            var issue = Check(set, stack, why, name, parts);
            if (issue == null || caveats.Count == 0 || issue.Severity != "Несоответствие") return issue;
            issue.Severity = "Проверить";
            issue.Message += " " + string.Join(" ", caveats);
            return issue;
        }

        AuditIssue Check(AuditBoltSet set, List<(string What, double Mm)> stack, string why, string name, List<AuditPart> parts)
        {
            var bolt = $"{BoltKind(set)} {set.Size} × {set.LengthRaw}".Trim();
            if (stack == null) return AuditRules.Issue(Id, Title, "Не проверено", $"{name}, {bolt}: {why}", parts);
            var length = AuditRules.ParsePositive(set.LengthRaw) is double raw && AuditRules.MillimetresPerUnit(set.LengthUnit) is double factor ? raw * factor : (double?)null;
            if (!length.HasValue)
                return AuditRules.Issue(Id, Title, "Не проверено", $"{name}, {bolt}: длина «{set.LengthRaw}» {(string.IsNullOrWhiteSpace(set.LengthUnit) ? "" : $"({set.LengthUnit.Trim()}) ")}не распознана.", parts);
            var grip = stack.Sum(s => s.Mm);
            var listing = string.Join(" + ", stack.Select(s => $"{s.What} {s.Mm:0.##}"));
            if (length.Value < grip - ToleranceMm)
            {
                var short1 = AuditRules.Issue(Id, Title, "Несоответствие",
                    $"{name}: {bolt} — длина {length.Value:0.##} мм меньше толщины пакета {grip:0.##} мм ({listing}).", parts);
                short1.Magnitude = grip - length.Value;
                return short1;
            }
            var diameter = ThreadDiameterMm(set.Size);
            if (!diameter.HasValue)
                return AuditRules.Issue(Id, Title, "Не проверено", $"{name}, {bolt}: размер резьбы «{set.Size}» не распознан — гайки не учтены.", parts);
            var stud = IsStud(set.Description);
            int nuts = stud == true ? 2 : 1;
            var nut = NutHeightPerDiameter * diameter.Value;
            var need = grip + nuts * nut;
            if (length.Value >= need - ToleranceMm) return null;
            var kind = stud == null ? " (тип крепежа не определён — принят болт с одной гайкой)" : "";
            var issue = AuditRules.Issue(Id, Title, "Несоответствие",
                $"{name}: {bolt} — длина {length.Value:0.##} мм меньше необходимой {need:0.##} мм: пакет {grip:0.##} мм ({listing}) + {nuts} × гайка {nut:0.##} мм{kind}.", parts);
            issue.Magnitude = need - length.Value;
            return issue;
        }

        // Thicknesses clamped by the set, mm. null with the reason when the
        // joint cannot be measured.
        // twin: the joint on the far side of a wafer / blind when it carries
        // bolt sets too.
        static List<(string What, double Mm)> Stack(AuditPart connector, List<(string Key, string Port)> faces,
            IReadOnlyDictionary<string, AuditPart> byKey, AuditSnapshot snapshot, ILookup<string, (AuditPart Part, AuditLink Link)> reverse,
            List<AuditPart> parts, List<string> caveats, out string why, out AuditPart twin)
        {
            why = null;
            twin = null;
            if (!connector.ConnectionsRead) { why = "соединения не прочитаны" + (string.IsNullOrWhiteSpace(connector.ConnectionsError) ? "." : ": " + connector.ConnectionsError.Trim()); return null; }
            if (faces.Count != 2) { why = $"соединение связывает {faces.Count} дет. вместо двух — пакет не определён."; return null; }
            var stack = new List<(string, double)>();
            var gap = Gap(connector, snapshot.DrawingMmPerUnit, out why);
            if (!gap.HasValue) return null;
            stack.Add(("прокладка", gap.Value));
            var flanges = new List<(AuditPart Part, AuditPort End, double? Mm, string Why)>();
            foreach (var (key, port) in faces)
            {
                if (!byKey.TryGetValue(key, out var part)) { why = $"деталь [{key}] не найдена."; return null; }
                var end = part.Ports.FirstOrDefault(p => string.Equals(p.Name, port, StringComparison.OrdinalIgnoreCase));
                var type = (end?.EndType ?? "").Trim().ToUpperInvariant();
                if (type == "LUG") { why = $"арматура под проушины (LUG) {AuditRules.NameOf(part)} — не проверяется."; return null; }
                if (type == "WF" || IsSandwiched(part))
                {
                    // A wafer body or a spectacle blind sits between two
                    // gaskets; the set runs from the flange on this side to
                    // the flange on the far side.
                    if (end == null) { why = $"порт детали {AuditRules.NameOf(part)} у соединения не найден."; return null; }
                    if (!Wafer(part, end, byKey, snapshot, reverse, parts, stack, flanges, caveats, out why, out var far)) return null;
                    if (far.BoltSets.Count > 0) twin = far;
                    continue;
                }
                var thickness = Flange(part, end, out var reason);
                flanges.Add((part, end, thickness, reason));
            }
            // A valve's flange often has no FlangeThickness in the catalogue.
            // The other flange of the joint (its mate, or the one beyond a
            // wafer / blind) is of the same DN / PN standard, so its thickness
            // stands in — and a shortfall is then «Проверить».
            var known = flanges.Where(f => f.Mm.HasValue).ToList();
            foreach (var flange in flanges)
            {
                if (flange.Mm.HasValue) { stack.Add(($"фланец [{flange.Part.Key}]", flange.Mm.Value)); continue; }
                if (flange.End == null || flanges.Count != 2 || known.Count != 1) { why = flange.Why; return null; }
                stack.Add(($"фланец [{flange.Part.Key}] ≈", known[0].Mm.Value));
                caveats.Add($"Толщина фланца {AuditRules.NameOf(flange.Part)} в каталоге не задана — принята равной ответному фланцу (≈).");
            }
            return stack;
        }

        // Spectacle blinds / spacers: clamped between two flanges like a
        // wafer body, though their catalogue ends are FL.
        static bool IsSandwiched(AuditPart part)
        {
            var name = part.ClassName ?? "";
            return name.Contains("BlindDisk", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Spectacle", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Spacer", StringComparison.OrdinalIgnoreCase);
        }

        // Adds the body and the far gasket to the stack, the far flange to
        // flanges (its thickness may have to be estimated, see Stack).
        static bool Wafer(AuditPart wafer, AuditPort near, IReadOnlyDictionary<string, AuditPart> byKey, AuditSnapshot snapshot,
            ILookup<string, (AuditPart Part, AuditLink Link)> reverse, List<AuditPart> parts, List<(string, double)> stack,
            List<(AuditPart Part, AuditPort End, double? Mm, string Why)> flanges, List<string> caveats, out string why, out AuditPart joint)
        {
            joint = null;
            var name = AuditRules.NameOf(wafer);
            var far = wafer.Ports.Where(p => !string.Equals(p.Name, near.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (far.Count != 1) { why = $"у межфланцевой детали {name} {far.Count + 1} портов — пакет не определён."; return false; }
            var body = Distance(near, far[0], snapshot.DrawingMmPerUnit, out why);
            if (!body.HasValue) { why = $"{name}: {why}"; return false; }
            stack.Add(($"корпус [{wafer.Key}]", body.Value));
            var other = LinksOf(wafer, reverse).Where(l => string.Equals(l.Port, far[0].Name, StringComparison.OrdinalIgnoreCase))
                .Select(l => byKey.GetValueOrDefault(l.OtherKey)).Where(p => p != null).ToList();
            if (other.Count != 1 || !other[0].IsConnector)
            { why = $"за межфланцевой деталью {name} не найдено фланцевое соединение — пакет не определён."; return false; }
            joint = other[0];
            parts.Add(joint);
            // Plant 3D puts a set on each side of a spectacle blind
            // (audit-model.dwg); the flange holes take one stud through it all.
            if (joint.BoltSets.Count > 0)
                caveats.Add($"Комплекты болтов стоят по обе стороны {name}; физически шпилька проходит через весь пакет — проверьте схему крепежа.");
            var gap = Gap(joint, snapshot.DrawingMmPerUnit, out why);
            if (!gap.HasValue) return false;
            stack.Add(($"прокладка [{joint.Key}]", gap.Value));
            var beyond = Faces(joint, reverse).Where(f => f.Key != wafer.Key).ToList();
            if (beyond.Count != 1 || !byKey.TryGetValue(beyond[0].Key, out var flange))
            { why = $"за межфланцевой деталью {name} фланец не найден — пакет не определён."; return false; }
            parts.Add(flange);
            var end = flange.Ports.FirstOrDefault(p => string.Equals(p.Name, beyond[0].Port, StringComparison.OrdinalIgnoreCase));
            var thickness = Flange(flange, end, out var reason);
            flanges.Add((flange, end, thickness, reason));
            return true;
        }

        static double? Flange(AuditPart part, AuditPort end, out string why)
        {
            why = null;
            var name = AuditRules.NameOf(part);
            if (end == null) { why = $"порт детали {name} у соединения не найден."; return null; }
            var unit = string.IsNullOrWhiteSpace(end.LengthUnit) ? part.LengthUnit : end.LengthUnit;
            if (AuditRules.ParsePositive(end.FlangeThicknessRaw) is double raw && AuditRules.MillimetresPerUnit(unit) is double factor)
                return raw * factor;
            why = $"толщина фланца {name} (порт {end.Name}) не задана: «{end.FlangeThicknessRaw}».";
            return null;
        }

        // The gasket gap: between the connection object's two ports.
        static double? Gap(AuditPart connector, double? mmPerUnit, out string why)
        {
            why = null;
            var ends = connector.Ports.Where(p => p.Position != null).ToList();
            if (ends.Count != 2) { why = $"у соединения [{connector.Key}] не 2 порта с координатами — зазор прокладки не определён."; return null; }
            return Distance(ends[0], ends[1], mmPerUnit, out why);
        }

        static double? Distance(AuditPort a, AuditPort b, double? mmPerUnit, out string why)
        {
            why = null;
            if (a?.Position == null || b?.Position == null) { why = "координаты портов не прочитаны."; return null; }
            if (!mmPerUnit.HasValue) { why = "единицы чертежа (INSUNITS) не заданы — размеры по модели в мм не переводятся."; return null; }
            double dx = a.Position[0] - b.Position[0], dy = a.Position[1] - b.Position[1], dz = a.Position[2] - b.Position[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz) * mmPerUnit.Value;
        }

        static bool SameSets(AuditPart a, AuditPart b) =>
            a.BoltSets.Count == b.BoltSets.Count && a.BoltSets.Zip(b.BoltSets).All(p =>
                string.Equals((p.First.LengthRaw ?? "").Trim(), (p.Second.LengthRaw ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals((p.First.Size ?? "").Trim(), (p.Second.Size ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

        // Every part's links indexed by the part they lead to.
        internal static ILookup<string, (AuditPart Part, AuditLink Link)> Reverse(IEnumerable<AuditPart> all) =>
            all.SelectMany(o => o.Links.Select(l => (Part: o, Link: l))).ToLookup(t => t.Link.OtherKey ?? "", StringComparer.Ordinal);

        // The parts a part is joined to, with their port at the joint.
        internal static List<(string Key, string Port)> Faces(AuditPart part, ILookup<string, (AuditPart Part, AuditLink Link)> reverse) =>
            LinksOf(part, reverse).Select(l => (l.OtherKey, l.OtherPort)).ToList();

        // This part's links seen from its side. Native connections may be
        // stored on either side only, so the others' links are turned round.
        internal static List<AuditLink> LinksOf(AuditPart part, ILookup<string, (AuditPart Part, AuditLink Link)> reverse)
        {
            var links = new List<AuditLink>();
            void Add(string port, string key, string otherPort)
            {
                if (string.IsNullOrEmpty(key) || key == part.Key) return;
                if (links.Any(l => l.OtherKey == key && (l.OtherPort == null || otherPort == null
                    || string.Equals(l.OtherPort, otherPort, StringComparison.OrdinalIgnoreCase)))) return;
                links.Add(new AuditLink { Port = port, OtherKey = key, OtherPort = otherPort });
            }
            foreach (var link in part.Links) Add(link.Port, link.OtherKey, link.OtherPort);
            foreach (var (other, link) in reverse[part.Key ?? ""]) Add(link.OtherPort, other.Key, link.Port);
            return links;
        }

        // «M16», «М20x1,5» (Cyrillic М too), «5/8"», «1-1/8 in», «0.75"».
        public static double? ThreadDiameterMm(string size)
        {
            var text = (size ?? "").Trim();
            var metric = Regex.Match(text, @"^[MМ]\s*(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
            if (metric.Success) return AuditRules.ParsePositive(metric.Groups[1].Value);
            var mixed = Regex.Match(text, @"^(\d+)\s*-\s*(\d+)\s*/\s*(\d+)");
            if (mixed.Success && int.Parse(mixed.Groups[3].Value, CultureInfo.InvariantCulture) is int den1 && den1 > 0)
                return (int.Parse(mixed.Groups[1].Value, CultureInfo.InvariantCulture) + (double)int.Parse(mixed.Groups[2].Value, CultureInfo.InvariantCulture) / den1) * 25.4;
            var fraction = Regex.Match(text, @"^(\d+)\s*/\s*(\d+)");
            if (fraction.Success && int.Parse(fraction.Groups[2].Value, CultureInfo.InvariantCulture) is int den && den > 0)
                return (double)int.Parse(fraction.Groups[1].Value, CultureInfo.InvariantCulture) / den * 25.4;
            var number = Regex.Match(text, @"^(\d+(?:[.,]\d+)?)\s*(""|in\b|inch)?", RegexOptions.IgnoreCase);
            if (number.Success && AuditRules.ParsePositive(number.Groups[1].Value) is double value)
                return number.Groups[2].Success ? value * 25.4 : value;
            return null;
        }

        // ОСТ 26-2040-96: studs for flanged joints (its «крепёжный комплект»
        // is a stud with two nuts).
        static readonly string[] StudMarks = { "stud", "шпильк", "ост 26-2040","din 975", "din 976", "din 938", "din 939", "din 2509", "гост 9066", "гост 22032", "гост 22034", "гост 22036", "гост 22038", "гост 22040", "гост 22042", "threaded rod" };
        static readonly string[] BoltMarks = { "bolt", "болт", "din 931", "din 933", "iso 4014", "iso 4017", "гост 7798", "гост 7805", "гост 15589" };

        // true = stud (two nuts), false = bolt (head and one nut), null = unknown.
        public static bool? IsStud(string description)
        {
            var text = (description ?? "").ToLowerInvariant().Replace(' ', ' ');
            text = Regex.Replace(text, @"\s+", " ");
            if (StudMarks.Any(text.Contains)) return true;
            if (BoltMarks.Any(text.Contains)) return false;
            return null;
        }

        static string BoltKind(AuditBoltSet set) => IsStud(set.Description) switch { true => "шпилька", false => "болт", _ => "крепёж" };
    }
}
