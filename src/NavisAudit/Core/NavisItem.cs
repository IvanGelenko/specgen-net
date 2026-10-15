using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace NavisAudit.Core
{
    // One Plant 3D object as read from a Navisworks model (ModelScanner),
    // without Navisworks types, so how it becomes an AuditPart is testable.
    // Coordinates are millimetres in the model's world (Navisworks) frame.
    public sealed class NavisItem
    {
        public string Key { get; set; }
        public string Handle { get; set; }
        // The DWG the object came from (Navisworks «Файл источника»).
        public string Source { get; set; }
        // The xrefs the object came through inside its file: «A → B»; null
        // for the file's own objects.
        public string Container { get; set; }
        // «file → xref → xref»: where it is from, as the Plant 3D audit's
        // «Источник». Property coordinates are those of this drawing.
        public string Origin => string.IsNullOrEmpty(Container) ? Source : $"{Source} → {Container}";
        public string Layer { get; set; }
        // Navisworks class: ACPPPIPE, ACPPCONNECTOR…
        public string DisplayClass { get; set; }
        // Plant 3D properties of the «AutoCAD» tab, by property name with
        // the «autocad_ubiquity_» prefix and «[0]» dropped. A name met more
        // than once (StartPoint X, Y, Z) is kept as «StartPoint», «StartPoint#1»…
        public Dictionary<string, string> Props { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // minX, minY, minZ, maxX, maxY, maxZ; null = no geometry.
        public double[] Box { get; set; }
        // Snap points of the geometry: Plant 3D exports the connection ports
        // (plus an elbow's corner, a tee's centre, a support's base point).
        public List<double[]> Snaps { get; } = new List<double[]>();

        public string Prop(string name) => Props.TryGetValue(name, out var value) ? value : null;

        public string ClassName => Prop("PnPClassName");
    }

    // Names of the Plant 3D properties as Navisworks shows them. The port and
    // fastener groups carry the project's display names («Порт1_Толщина
    // стенки» in a Russian project, «Port1_Wall Thickness» in an English one).
    public static class PlantProps
    {
        static readonly Regex PortName = new Regex(@"^(Порт|Port)\s*(\d+)_(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex NozzleName = new Regex(@"^(Штуцер|Nozzle)\s*(\d+)_(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex FastenerName = new Regex(@"^(Крепеж|Крепёж|Fastener|Fasteners)\s*(\d+)_(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static readonly string[] WallNames = { "Толщина стенки", "WallThickness", "Wall Thickness" };
        public static readonly string[] PortNameNames = { "Название порта", "Имя порта", "PortName", "Port Name", "Порт", "Port" };
        public static readonly string[] EndTypeNames = { "Тип соединения", "EndType", "End Type" };
        // The part's own properties carry the internal names; a fastener
        // group carries the project's display names (0873: «Крепеж1_Статус»,
        // «Крепеж1_Миникаталог» = its Spec).
        public static readonly string[] StatusNames = { "Status", "Статус" };
        public static readonly string[] SpecNames = { "Spec", "Миникаталог", "Спецификация" };
        public static readonly string[] DescriptionNames = { "PartSizeLongDesc", "Полное описание (размер)", "ShortDescription", "Краткое описание", "Имя класса", "ClassName" };

        public static double? Number(string text) =>
            double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && !double.IsNaN(value) && !double.IsInfinity(value) ? value : (double?)null;

        public static string First(IReadOnlyDictionary<string, string> group, IEnumerable<string> names)
        {
            foreach (var name in names)
                if (group.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)) return value.Trim();
            return null;
        }

        // Port n → its properties (MatchingPipeOd, LengthUnit, Толщина стенки…).
        public static SortedDictionary<int, Dictionary<string, string>> Ports(NavisItem item) => Groups(item, PortName);
        public static SortedDictionary<int, Dictionary<string, string>> Nozzles(NavisItem item) => Groups(item, NozzleName);
        public static SortedDictionary<int, Dictionary<string, string>> Fasteners(NavisItem item) => Groups(item, FastenerName);

        static SortedDictionary<int, Dictionary<string, string>> Groups(NavisItem item, Regex pattern)
        {
            var groups = new SortedDictionary<int, Dictionary<string, string>>();
            foreach (var pair in item.Props)
            {
                var match = pattern.Match(pair.Key);
                if (!match.Success) continue;
                var index = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                if (!groups.TryGetValue(index, out var group))
                    groups[index] = group = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                group[match.Groups[3].Value.Trim()] = pair.Value;
            }
            return groups;
        }

        // Plant 3D «Position»: X, Y and the elevation «COP» of port 1 (the
        // part's insertion point — an elbow's corner, a tee's centre). In the
        // DWG's own coordinates.
        // A grouped name («Штуцер1_Положение…») is a nozzle's, not the part's.
        public static double[] Position(IReadOnlyDictionary<string, string> props) =>
            Point(props, k => k.IndexOf('_') < 0 && Regex.IsMatch(k, @"(^|\s)X\s*\((порт|port)\s*1\)$", RegexOptions.IgnoreCase),
                k => k.IndexOf('_') < 0 && Regex.IsMatch(k, @"(^|\s)Y\s*\((порт|port)\s*1\)$", RegexOptions.IgnoreCase),
                k => k.IndexOf('_') < 0 && Regex.IsMatch(k, @"(COP|(^|\s)Z|Elevation)\s*\((порта|порт|port)\s*1\)$", RegexOptions.IgnoreCase));

        public static double[] Cog(IReadOnlyDictionary<string, string> props, string prefix = "") =>
            Point(props, k => k.Equals(prefix + "COG X", StringComparison.OrdinalIgnoreCase),
                k => k.Equals(prefix + "COG Y", StringComparison.OrdinalIgnoreCase),
                k => k.Equals(prefix + "COG Z", StringComparison.OrdinalIgnoreCase));

        static double[] Point(IReadOnlyDictionary<string, string> props, Func<string, bool> x, Func<string, bool> y, Func<string, bool> z)
        {
            double? Get(Func<string, bool> match) => props.Where(p => match(p.Key)).Select(p => Number(p.Value)).FirstOrDefault(v => v.HasValue);
            var (px, py, pz) = (Get(x), Get(y), Get(z));
            return px.HasValue && py.HasValue && pz.HasValue ? new[] { px.Value, py.Value, pz.Value } : null;
        }
    }
}
