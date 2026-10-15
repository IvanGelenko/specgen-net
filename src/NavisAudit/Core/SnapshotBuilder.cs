using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using PipingSpecPlugin.Audit;

namespace NavisAudit.Core
{
    // What the Navisworks model gives the audit besides the AuditSnapshot
    // the Plant 3D rules run on.
    public sealed class NavisModel
    {
        public AuditSnapshot Snapshot { get; set; }
        public Dictionary<string, AuditPart> Parts { get; } = new Dictionary<string, AuditPart>(StringComparer.Ordinal);
        // Plant structure members etc.: not audit parts, but clash objects.
        public Dictionary<string, AuditPart> Structures { get; } = new Dictionary<string, AuditPart>(StringComparer.Ordinal);
        // Axis end points of structure members (StructureJointPolicy).
        public Dictionary<string, List<double[]>> StructureEnds { get; } = new Dictionary<string, List<double[]>>(StringComparer.Ordinal);
        // A copy → the object it repeats (same class, size, line, place).
        public Dictionary<string, string> DuplicateOf { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        // Pairs joined port to port, through a connection object or by a
        // verified stub-in: their overlap is the joint, not a clash.
        public HashSet<string> JointPairs { get; } = new HashSet<string>(StringComparer.Ordinal);
        // Model boxes of the parts, mm (for the parts dump).
        public Dictionary<string, double[]> Boxes { get; } = new Dictionary<string, double[]>(StringComparer.Ordinal);
        public int ProxyCount { get; set; }
        // Property coordinates (DWG) could not be tied to the model (moved
        // or rotated on append) in these source files.
        public List<string> UnplacedSources { get; } = new List<string>();

        public static string Pair(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + "\n" + b : b + "\n" + a;
        public bool Joined(string a, string b) => JointPairs.Contains(Pair(a, b));
        // A branch end on a pipe whose stub-in weld is not in the model (no
        // position): «Проверить», not a clash for certain.
        public HashSet<string> OpenTaps { get; } = new HashSet<string>(StringComparer.Ordinal);
    }

    public static class SnapshotBuilder
    {
        // Two ports this close are one joint: the gasket gap of a flanged
        // joint is 2-4 mm in Plant 3D models (0873 project).
        public const double JointGapMm = 10;
        const double PointSlackMm = 1;

        public static bool IsStructureClass(string className) => (className ?? "").StartsWith("Structure", StringComparison.OrdinalIgnoreCase);
        public static bool IsConnectorClass(string className) => (className ?? "").IndexOf("Connector", StringComparison.OrdinalIgnoreCase) >= 0;
        public static bool IsSupportClass(string className) => (className ?? "").IndexOf("Support", StringComparison.OrdinalIgnoreCase) >= 0;
        static bool IsEquipment(NavisItem item) => (item.DisplayClass ?? "").IndexOf("EQUIPMENT", StringComparison.OrdinalIgnoreCase) >= 0;
        // Pipe / elbow / bend bodies a branch can be welded into.
        static bool IsRun(string className) => AuditRules.IsPipeClass(className) || (className ?? "").IndexOf("Elbow", StringComparison.OrdinalIgnoreCase) >= 0;

        // Ports a part of the class has when its port properties are not all
        // in the model (some elbows carry Порт1_* only).
        static int DefaultPorts(string className)
        {
            var name = className ?? "";
            bool Is(string s) => name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
            if (Is("Cross")) return 4;
            if (Is("Tee")) return 3;
            if (Is("BlindFlange") || Is("Cap") || Is("Plug")) return 1;
            if (Is("Pipe") || Is("Elbow") || Is("Reducer") || Is("Flange") || Is("Coupling") || Is("Union") || Is("Nipple")) return 2;
            return 0;
        }

        public static NavisModel Build(string drawing, IReadOnlyList<NavisItem> items, int proxyCount = 0, IReadOnlyList<string> proxyKeys = null)
        {
            var model = new NavisModel { Snapshot = new AuditSnapshot { Drawing = drawing, DrawingMmPerUnit = 1 }, ProxyCount = proxyCount };
            var snapshot = model.Snapshot;
            if (proxyCount > 0)
                snapshot.ReadIssues.Add(new AuditIssue { RuleId = "read", Check = "Чтение данных", Severity = "Не проверено",
                    Message = $"Объектов Plant 3D без свойств (прокси, например ACAD_PROXY_ENTITY ACPPPIPE): {proxyCount}. Модель выгружена в NWC без Plant 3D Object Enabler — номер линии, Ду, стенка и материал им недоступны; в коллизиях они участвуют как объекты без линии. Переэкспортируйте NWC из Plant 3D или откройте DWG в Navisworks на машине с Plant 3D.",
                    Keys = (proxyKeys ?? new string[0]).ToList() });

            var plant = items.Where(i => !string.IsNullOrWhiteSpace(i.ClassName)).ToList();
            var offsets = SourceOffsets(plant, model);
            var copies = FindDuplicates(plant, model);

            // OD → nominal size, from parts whose ends are all one size.
            var nominal = new Dictionary<double, string>();
            foreach (var item in plant)
            {
                var size = (item.Prop("Size") ?? "").Trim();
                var ods = PlantProps.Ports(item).Values.Select(g => PlantProps.Number(g.TryGetValue("MatchingPipeOd", out var od) ? od : null))
                    .Where(v => v.HasValue).Select(v => Math.Round(v.Value, 3)).Distinct().ToList();
                if (ods.Count == 1 && AuditRules.ParsePositive(size).HasValue && !nominal.ContainsKey(ods[0])) nominal[ods[0]] = size;
            }

            var points = new Dictionary<string, List<double[]>>(StringComparer.Ordinal);
            var expected = new Dictionary<string, int>(StringComparer.Ordinal);
            var items2 = new Dictionary<string, NavisItem>(StringComparer.Ordinal);
            foreach (var item in plant)
            {
                if (copies.Contains(item.Key)) continue;
                var cls = item.ClassName.Trim();
                if (IsStructureClass(cls))
                {
                    model.Structures[item.Key] = new AuditPart { Key = item.Key, SelectionKey = item.Key, Source = item.Origin,
                        ClassName = cls, Description = Describe(item) };
                    if (cls.Equals("StructureMember", StringComparison.OrdinalIgnoreCase) && item.Snaps.Count == 2)
                        model.StructureEnds[item.Key] = item.Snaps.ToList();
                    continue;
                }
                var part = ToPart(item, nominal);
                model.Parts[part.Key] = part;
                snapshot.Parts.Add(part);
                items2[part.Key] = item;
                if (item.Box != null) model.Boxes[part.Key] = item.Box;
                offsets.TryGetValue(item.Origin ?? "", out var offset);
                var at = Points(item, part, offset, out var n);
                points[part.Key] = at;
                expected[part.Key] = n;
                // A straight pipe's ends — its farthest port points (a stub-in
                // adds one on the axis): its slope (SlopeRule).
                if (string.Equals(cls, "Pipe", StringComparison.OrdinalIgnoreCase)) part.Axis = SlopeRule.Ends(at);
            }

            Connect(model, items2, points, expected, offsets);
            return model;
        }

        static string Describe(NavisItem item)
        {
            foreach (var name in new[] { "PartSizeLongDesc", "Полное описание (размер)", "PartFamilyLongDesc", "Полное описание", "ShortDescription", "Краткое описание" })
            {
                var value = item.Prop(name);
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            var size = item.Prop("Size");
            return string.IsNullOrWhiteSpace(size) ? item.ClassName : $"{item.ClassName} {size.Trim()}";
        }

        static AuditPart ToPart(NavisItem item, IReadOnlyDictionary<double, string> nominal)
        {
            var cls = item.ClassName.Trim();
            var ports = PlantProps.Ports(item);
            string Port1(string name) => ports.Count > 0 && ports.First().Value.TryGetValue(name, out var v) ? v : null;
            var part = new AuditPart
            {
                Key = item.Key, SelectionKey = item.Key, Source = item.Origin, ClassName = cls, Description = Describe(item),
                Diameter = item.Prop("Size")?.Trim(),
                Material = item.Prop("PnPMaterial")?.Trim(),
                MaterialCode = (item.Prop("MaterialCode") ?? item.Prop("Materialcode"))?.Trim(),
                WallRaw = item.Prop("WallThickness")?.Trim(),
                LengthUnit = (item.Prop("LengthUnit") ?? Port1("LengthUnit"))?.Trim(),
            };
            var line = item.Prop("LineNumberTag");
            if (string.IsNullOrWhiteSpace(line) && IsConnectorClass(cls))
                line = PlantProps.Fasteners(item).Values.Select(g => g.TryGetValue("LineNumberTag", out var t) ? t : null).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            if (!string.IsNullOrWhiteSpace(line)) { part.LineKey = line.Trim(); part.LineLabel = line.Trim(); }
            else part.LineLabel = AuditOptions.NoLineLabel;
            part.WallMm = AuditRules.WallMillimetres(part.WallRaw, part.LengthUnit);
            part.Tag = item.Prop("Tag")?.Trim();
            part.Status = PlantProps.First(item.Props, PlantProps.StatusNames);
            part.Spec = PlantProps.First(item.Props, PlantProps.SpecNames);
            // A connection object's gasket / bolts / weld («Крепеж1_…»): their
            // spec («Миникаталог» in a Russian project) and status.
            foreach (var fastener in PlantProps.Fasteners(item).Values)
                part.SubParts.Add(new AuditSubPart
                {
                    Description = PlantProps.First(fastener, PlantProps.DescriptionNames),
                    Spec = PlantProps.First(fastener, PlantProps.SpecNames),
                    Status = PlantProps.First(fastener, PlantProps.StatusNames),
                });
            part.IsPipeOrElbow = cls.Equals("Pipe", StringComparison.OrdinalIgnoreCase) || cls.IndexOf("Elbow", StringComparison.OrdinalIgnoreCase) >= 0
                || cls.IndexOf("PipeBend", StringComparison.OrdinalIgnoreCase) >= 0;
            part.IsConnector = IsConnectorClass(cls);
            part.IsPipe = AuditRules.IsPipeClass(cls);
            part.IsTeeOrReducer = AuditRules.IsTeeOrReducerClass(cls);
            part.IsSupport = IsSupportClass(cls);
            var dxf = (item.DisplayClass ?? "").ToUpperInvariant();
            part.IsPipingComponent = dxf == "ACPPPIPE" || dxf == "ACPPPIPEINLINEASSET" || dxf == "ACPPCONNECTOR" || part.IsPipe || part.IsConnector;
            if (part.IsTeeOrReducer) part.Ends = Ends(item, part, ports, nominal, out var why);
            if (part.IsTeeOrReducer && part.Ends == null) part.EndsError = "в свойствах модели Navisworks нет данных портов (Порт1_…).";
            return part;
        }

        // Tee / reducer ends from the port properties. A port has its outside
        // diameter (MatchingPipeOd), not the nominal size the pipes are
        // grouped by: the larger OD takes the larger size of «250x150».
        static List<AuditEnd> Ends(NavisItem item, AuditPart part, SortedDictionary<int, Dictionary<string, string>> ports,
            IReadOnlyDictionary<double, string> nominal, out string why)
        {
            why = null;
            if (ports.Count == 0) return null;
            var sizes = Regex.Split(part.Diameter ?? "", @"\s*[xXхХ×*]\s*").Where(s => AuditRules.ParsePositive(s).HasValue).ToList();
            double? Od(Dictionary<string, string> g) => PlantProps.Number(g.TryGetValue("MatchingPipeOd", out var od) ? od : null) is double v ? Math.Round(v, 3) : (double?)null;
            var ods = ports.Values.Select(Od).Where(v => v.HasValue).Select(v => v.Value).Distinct().OrderByDescending(v => v).ToList();
            var bySize = new Dictionary<double, string>();
            if (sizes.Count == 1) foreach (var od in ods) bySize[od] = sizes[0];
            else if (sizes.Count == ods.Count)
            {
                var ordered = sizes.OrderByDescending(s => AuditRules.ParsePositive(s).Value).ToList();
                for (int i = 0; i < ods.Count; i++) bySize[ods[i]] = ordered[i];
            }
            var ends = new List<AuditEnd>();
            foreach (var port in ports)
            {
                var od = Od(port.Value);
                string size = null;
                if (od.HasValue && !bySize.TryGetValue(od.Value, out size)) nominal.TryGetValue(od.Value, out size);
                ends.Add(new AuditEnd { Name = PlantProps.First(port.Value, PlantProps.PortNameNames) ?? "S" + port.Key, Diameter = size,
                    WallRaw = PlantProps.First(port.Value, PlantProps.WallNames) ?? (port.Key == 1 ? part.WallRaw : null),
                    LengthUnit = (port.Value.TryGetValue("LengthUnit", out var unit) ? unit : null) ?? part.LengthUnit });
            }
            return ends;
        }

        // The connection points of a part: its snap points, less the
        // insertion point when it is an extra (elbow corner, tee centre);
        // a part with no snap points and one port (blind flange, plug) —
        // its insertion point; equipment — its nozzles.
        static List<double[]> Points(NavisItem item, AuditPart part, double[] offset, out int expected)
        {
            var ports = PlantProps.Ports(item);
            expected = Math.Max(ports.Count, DefaultPorts(part.ClassName));
            var position = Shift(PlantProps.Position(item.Props), offset);
            if (IsEquipment(item))
            {
                expected = 0;
                return PlantProps.Nozzles(item).Values.Select(g => Shift(PlantProps.Position(g), offset)).Where(p => p != null).ToList();
            }
            if (part.IsSupport)
            {
                // Its port is on the pipe axis (the insertion point); its one
                // snap point is the base on the steel.
                expected = 1;
                return position == null ? new List<double[]>() : new List<double[]> { position };
            }
            if (part.IsConnector) { expected = 0; return new List<double[]>(); }
            var at = new List<double[]>();
            foreach (var snap in item.Snaps)
                if (!at.Any(p => Distance(p, snap) <= 0.5)) at.Add(snap);
            if (at.Count > expected && expected > 0 && position != null)
            {
                var extra = at.FirstOrDefault(p => Distance(p, position) <= PointSlackMm);
                if (extra != null && at.Count - 1 >= expected) at.Remove(extra);
            }
            if (at.Count == 0 && expected == 1 && position != null) at.Add(position);
            return at;
        }

        static void Connect(NavisModel model, IReadOnlyDictionary<string, NavisItem> items, Dictionary<string, List<double[]>> points,
            Dictionary<string, int> expected, IReadOnlyDictionary<string, double[]> offsets)
        {
            var parts = model.Parts;
            // Port-to-port: every point within the gasket gap of another
            // part's point. Supports, connection objects and equipment's own
            // body take no part; equipment nozzles do.
            var grid = new PointGrid(20);
            foreach (var pair in points)
            {
                var part = parts[pair.Key];
                if (part.IsSupport || part.IsConnector) continue;
                for (int i = 0; i < pair.Value.Count; i++) grid.Add(pair.Value[i], (pair.Key, i));
            }
            var matched = points.ToDictionary(p => p.Key, p => new bool[p.Value.Count], StringComparer.Ordinal);
            var joints = new List<(double[] At, string A, string B, bool Tap)>();
            foreach (var pair in points)
            {
                var part = parts[pair.Key];
                if (part.IsSupport || part.IsConnector) continue;
                for (int i = 0; i < pair.Value.Count; i++)
                    foreach (var (other, j) in grid.Near(pair.Value[i], JointGapMm))
                    {
                        if (other == pair.Key) continue;
                        matched[pair.Key][i] = true;
                        if (string.CompareOrdinal(pair.Key, other) > 0) continue;
                        joints.Add((Middle(pair.Value[i], points[other][j]), pair.Key, other, false));
                    }
            }
            // Stub-in: a free end on the body of a pipe, elbow or bend.
            var runs = parts.Values.Where(p => IsRun(p.ClassName) && items[p.Key].Box != null).ToList();
            var boxes = new BoxGrid(500);
            foreach (var run in runs) boxes.Add(items[run.Key].Box, run.Key);
            var taps = new List<(double[] At, string A, string B, bool Tap)>();
            foreach (var pair in points)
            {
                var part = parts[pair.Key];
                if (part.IsSupport || part.IsConnector || IsEquipment(items[pair.Key])) continue;
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    if (matched[pair.Key][i]) continue;
                    var header = boxes.Containing(pair.Value[i], PointSlackMm).FirstOrDefault(k => k != pair.Key);
                    if (header == null) continue;
                    matched[pair.Key][i] = true;
                    taps.Add((pair.Value[i], pair.Key, header, true));
                }
            }
            foreach (var joint in joints.Concat(taps)) Link(parts[joint.A], parts[joint.B]);
            foreach (var joint in joints) model.JointPairs.Add(NavisModel.Pair(joint.A, joint.B));

            // Connection objects (weld, flanged joint, stub-in weld): the joint
            // at their centre of gravity or inside their gasket / bolts.
            var at = new PointGrid(20);
            var all = joints.Concat(taps).ToList();
            for (int i = 0; i < all.Count; i++) at.Add(all[i].At, (null, i));
            var verified = new HashSet<int>();
            var unplaced = new List<AuditPart>();
            foreach (var connector in parts.Values.Where(p => p.IsConnector))
            {
                var item = items[connector.Key];
                offsets.TryGetValue(item.Origin ?? "", out var offset);
                var cog = Shift(PlantProps.Cog(item.Props) ?? PlantProps.Cog(item.Props, "Крепеж1_") ?? PlantProps.Cog(item.Props, "Fastener1_"), offset);
                if (cog == null && item.Box == null) unplaced.Add(connector);
                var hits = new HashSet<int>();
                if (cog != null) foreach (var (_, i) in at.Near(cog, JointGapMm)) hits.Add(i);
                // Gasket and bolts of one flanged joint; a larger box is not one joint.
                if (item.Box != null && Enumerable.Range(0, 3).All(k => item.Box[k + 3] - item.Box[k] <= 2000))
                    foreach (var (_, i) in at.InBox(item.Box, 2)) hits.Add(i);
                foreach (var i in hits)
                {
                    Link(connector, parts[all[i].A]);
                    Link(connector, parts[all[i].B]);
                    if (all[i].Tap) verified.Add(i);
                }
                connector.ConnectionsRead = true;
            }
            // A stub-in weld without a position (17 of 57 in the 0873 model):
            // the stub-ins of a branch line and size it could be. When there
            // are as many such welds as stub-ins left, each has its weld; with
            // fewer, which one lacks it is unknown and none is taken as welded
            // — a branch pushed into a pipe without a weld is a clash in
            // Plant 3D too (ConnectedJointPolicy).
            string Branch(string line, string size) => (line ?? "").Trim().ToUpperInvariant() + "|" + (size ?? "").Trim();
            var spare = unplaced.Select(c => (Connector: c, Fastener: PlantProps.Fasteners(items[c.Key]).Values.FirstOrDefault()))
                .Where(c => c.Fastener != null && IsTapWeld(c.Fastener))
                .GroupBy(c => Branch(c.Fastener.TryGetValue("LineNumberTag", out var l) ? l : c.Connector.LineKey,
                    c.Fastener.TryGetValue("NominalDiameter", out var d) ? d : c.Fastener.TryGetValue("Размер", out var r) ? r : null), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(c => c.Connector).ToList(), StringComparer.Ordinal);
            var open = Enumerable.Range(joints.Count, all.Count - joints.Count).Where(i => !verified.Contains(i))
                .GroupBy(i => Branch(parts[all[i].A].LineKey, parts[all[i].A].Diameter), StringComparer.Ordinal);
            foreach (var group in open)
            {
                if (!spare.TryGetValue(group.Key, out var welds) || welds.Count < group.Count()) continue;
                int n = 0;
                foreach (var i in group)
                {
                    var weld = welds[n++];
                    Link(weld, parts[all[i].A]);
                    Link(weld, parts[all[i].B]);
                    verified.Add(i);
                }
            }
            for (int i = joints.Count; i < all.Count; i++)
                (verified.Contains(i) ? model.JointPairs : model.OpenTaps).Add(NavisModel.Pair(all[i].A, all[i].B));

            // Supports: the parts their insertion point is on.
            foreach (var support in parts.Values.Where(p => p.IsSupport))
            {
                var position = points[support.Key].FirstOrDefault();
                if (position != null)
                    foreach (var other in parts.Values)
                    {
                        if (other == support || other.IsSupport || other.IsConnector || !other.IsPipingComponent) continue;
                        var box = items[other.Key].Box;
                        if (box != null && Inside(position, box, 2)) { support.SupportedPartKeys.Add(other.Key); Link(support, other); }
                    }
                support.Ports.Add(new AuditPort { Name = "S1", Connected = position == null ? (bool?)null : support.SupportedPartKeys.Count > 0 });
                support.ConnectionsRead = position != null;
                if (position == null) support.ConnectionsError = "в свойствах модели нет положения опоры.";
            }

            foreach (var part in parts.Values.Where(p => !p.IsSupport && !p.IsConnector))
            {
                var at2 = points[part.Key];
                var n = expected[part.Key];
                var free = Enumerable.Range(0, at2.Count).Where(i => !matched[part.Key][i]).ToList();
                var joined = at2.Count - free.Count;
                if (n == 0) { part.ConnectionsRead = true; continue; }
                for (int i = 0; i < Math.Min(joined, n); i++) part.Ports.Add(new AuditPort { Name = "S" + (i + 1), Connected = true });
                if (at2.Count < n)
                {
                    // Fewer points than ports: what is not there is unknown.
                    part.ConnectionsRead = false;
                    part.ConnectionsError = $"в модели Navisworks найдено точек портов {at2.Count} из {n} (точки привязки геометрии).";
                    foreach (var i in free) part.Ports.Add(new AuditPort { Name = Where(at2[i]), Connected = false });
                    continue;
                }
                part.ConnectionsRead = true;
                // The free ports: the free points nearest the part's own box
                // faces (an elbow's corner or a tee's centre is inside it).
                var box = items[part.Key].Box;
                foreach (var i in free.OrderBy(i => box == null ? 0 : FaceDistance(at2[i], box)).Take(Math.Max(0, n - joined)))
                    part.Ports.Add(new AuditPort { Name = Where(at2[i]), Connected = false });
            }
        }

        static bool IsTapWeld(IReadOnlyDictionary<string, string> fastener) =>
            PlantProps.First(fastener, new[] { "Имя класса", "ClassName", "Class Name" }) is string name
            && name.IndexOf("TapWeld", StringComparison.OrdinalIgnoreCase) >= 0;

        static void Link(AuditPart a, AuditPart b)
        {
            if (a == b) return;
            a.Neighbours.Add(b.Key);
            b.Neighbours.Add(a.Key);
        }

        // «конец X 118830 Y 6397 Z 2178» — where the free end is.
        static string Where(double[] p) => string.Format(CultureInfo.InvariantCulture, "конец X {0:0} Y {1:0} Z {2:0}", p[0], p[1], p[2]);

        // Copies of one object in one place — a file appended twice, an xref
        // attached in two drawings of an assembly. Kept: the first; the
        // copies are left out of the audit (no false clashes, no doubled
        // walls) and reported — one remark per repeated file or xref, not
        // one per element (3313 copies in 0873-001-ТХМ_ОбщаяСборка.nwc).
        static HashSet<string> FindDuplicates(IReadOnlyList<NavisItem> plant, NavisModel model)
        {
            var copies = new HashSet<string>(StringComparer.Ordinal);
            var found = new List<(NavisItem First, NavisItem Copy)>();
            string Signature(NavisItem i) => string.Join("|", i.ClassName?.Trim(), i.Prop("Size")?.Trim(), i.Prop("LineNumberTag")?.Trim(),
                string.Join(",", i.Box.Select(v => Math.Round(v).ToString(CultureInfo.InvariantCulture))));
            foreach (var group in plant.Where(i => i.Box != null && !IsConnectorClass(i.ClassName)).GroupBy(Signature, StringComparer.Ordinal))
            {
                var list = group.ToList();
                if (list.Count < 2) continue;
                var first = list[0];
                foreach (var copy in list.Skip(1).Where(i => i.Box.Zip(first.Box, (a, b) => Math.Abs(a - b)).All(d => d <= 0.5)))
                {
                    copies.Add(copy.Key);
                    model.DuplicateOf[copy.Key] = first.Key;
                    found.Add((first, copy));
                }
            }
            // Connection objects have no body to compare and stay: a copied
            // one sits on the original joint and is linked to it like the
            // original (ConnectedJointPolicy), which changes no result.

            string Line(NavisItem i) => string.IsNullOrWhiteSpace(i.Prop("LineNumberTag")) ? null : i.Prop("LineNumberTag").Trim();
            foreach (var group in found.GroupBy(f => (f.First.Origin ?? "") + "\n" + (f.Copy.Origin ?? ""), StringComparer.OrdinalIgnoreCase))
            {
                var list = group.ToList();
                var parts = list.Select(f => f.First).Concat(list.Select(f => f.Copy)).Select(i => new AuditPart { Key = i.Key, SelectionKey = i.Key,
                    Source = i.Origin, LineKey = Line(i), LineLabel = Line(i) }).ToList();
                string message;
                if (list.Count == 1)
                    message = $"Элемент продублирован: {Describe(list[0].First)} [{list[0].First.Key}] — копия [{list[0].Copy.Key}] в том же месте ({list[0].Copy.Origin}). ";
                else
                {
                    var sample = string.Join(", ", list.Take(5).Select(f => $"{Describe(f.First)} [{f.Copy.Key}]")) + (list.Count > 5 ? " …" : "");
                    message = list[0].First.Origin == list[0].Copy.Origin
                        ? $"Содержимое «{list[0].Copy.Origin}» загружено в модель повторно в том же месте: продублировано элементов — {list.Count} ({sample}). "
                        : $"Содержимое «{list[0].Copy.Origin}» повторяет «{list[0].First.Origin}» в том же месте: продублировано элементов — {list.Count} ({sample}). ";
                }
                var issue = AuditRules.Issue("collisions", "Коллизии", "Дубликат", message +
                    "Копии исключены из аудита; в модели они дают ложные коллизии и задваивают количество. Проверьте, не добавлен ли файл или внешняя ссылка дважды (вложенные ссылки — «Наложение» вместо «Вставки»).", parts);
                // «Показать в модели» selects the copies — what is to go.
                issue.SelectionKeys = list.Select(f => f.Copy.Key).ToList();
                var lines = list.Select(f => Line(f.First)).Where(l => l != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (lines.Count > 3) issue.Line = $"{lines.Count} линий";
                issue.Magnitude = list.Count;
                issue.Bounds = list.Count == 1 ? list[0].First.Box.ToArray() : null;
                issue.BetweenStructures = list.All(f => IsStructureClass(f.First.ClassName));
                model.Snapshot.GeometryIssues.Add(issue);
            }
            return copies;
        }

        // Plant property coordinates are the DWG's; the model may have been
        // moved on append. A pipe's insertion point is its first port — one
        // of its snap points — which gives the shift per source file.
        static Dictionary<string, double[]> SourceOffsets(IReadOnlyList<NavisItem> plant, NavisModel model)
        {
            var result = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in plant.GroupBy(i => i.Origin ?? "", StringComparer.OrdinalIgnoreCase))
            {
                var shifts = new List<double[]>();
                foreach (var pipe in source.Where(i => AuditRules.IsPipeClass(i.ClassName) && i.Snaps.Count > 0))
                {
                    var position = PlantProps.Position(pipe.Props);
                    if (position == null) continue;
                    var snap = pipe.Snaps.OrderBy(s => Distance(s, position)).First();
                    shifts.Add(new[] { snap[0] - position[0], snap[1] - position[1], snap[2] - position[2] });
                }
                if (shifts.Count == 0) { result[source.Key] = new double[3]; continue; }
                var median = Enumerable.Range(0, 3).Select(k => shifts.Select(s => s[k]).OrderBy(v => v).ElementAt(shifts.Count / 2)).ToArray();
                if (shifts.Count(s => Distance(s, median) <= PointSlackMm) >= 0.8 * shifts.Count)
                    result[source.Key] = median.Select(v => Math.Abs(v) <= PointSlackMm ? 0 : v).ToArray();
                else model.UnplacedSources.Add(source.Key);
            }
            return result;
        }

        static double[] Shift(double[] point, double[] offset) =>
            point == null || offset == null ? null : new[] { point[0] + offset[0], point[1] + offset[1], point[2] + offset[2] };

        static double[] Middle(double[] a, double[] b) => new[] { (a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (a[2] + b[2]) / 2 };

        public static double Distance(double[] a, double[] b)
        {
            double dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static bool Inside(double[] p, double[] box, double slack) =>
            p[0] >= box[0] - slack && p[0] <= box[3] + slack && p[1] >= box[1] - slack && p[1] <= box[4] + slack
            && p[2] >= box[2] - slack && p[2] <= box[5] + slack;

        // How far inside the box the point is (0 on a face).
        static double FaceDistance(double[] p, double[] box) =>
            Enumerable.Range(0, 3).Min(k => Math.Min(Math.Abs(p[k] - box[k]), Math.Abs(box[k + 3] - p[k])));

        sealed class PointGrid
        {
            readonly double _cell;
            readonly Dictionary<(long, long, long), List<(double[] P, (string Key, int Index) Tag)>> _cells = new Dictionary<(long, long, long), List<(double[], (string, int))>>();
            public PointGrid(double cell) { _cell = cell; }
            (long, long, long) Cell(double[] p) => ((long)Math.Floor(p[0] / _cell), (long)Math.Floor(p[1] / _cell), (long)Math.Floor(p[2] / _cell));
            public void Add(double[] p, (string, int) tag)
            {
                var c = Cell(p);
                if (!_cells.TryGetValue(c, out var list)) _cells[c] = list = new List<(double[], (string, int))>();
                list.Add((p, tag));
            }
            public IEnumerable<(string Key, int Index)> Near(double[] p, double radius)
            {
                var c = Cell(p);
                int reach = (int)Math.Ceiling(radius / _cell);
                for (long x = c.Item1 - reach; x <= c.Item1 + reach; x++)
                    for (long y = c.Item2 - reach; y <= c.Item2 + reach; y++)
                        for (long z = c.Item3 - reach; z <= c.Item3 + reach; z++)
                            if (_cells.TryGetValue((x, y, z), out var list))
                                foreach (var entry in list)
                                    if (Distance(entry.P, p) <= radius) yield return entry.Tag;
            }
            public IEnumerable<(string Key, int Index)> InBox(double[] box, double slack)
            {
                var low = Cell(new[] { box[0] - slack, box[1] - slack, box[2] - slack });
                var high = Cell(new[] { box[3] + slack, box[4] + slack, box[5] + slack });
                for (long x = low.Item1; x <= high.Item1; x++)
                    for (long y = low.Item2; y <= high.Item2; y++)
                        for (long z = low.Item3; z <= high.Item3; z++)
                            if (_cells.TryGetValue((x, y, z), out var list))
                                foreach (var entry in list)
                                    if (Inside(entry.P, box, slack)) yield return entry.Tag;
            }
        }

        sealed class BoxGrid
        {
            readonly double _cell;
            readonly Dictionary<(long, long, long), List<(double[] Box, string Key)>> _cells = new Dictionary<(long, long, long), List<(double[], string)>>();
            public BoxGrid(double cell) { _cell = cell; }
            long C(double v) => (long)Math.Floor(v / _cell);
            public void Add(double[] box, string key)
            {
                // A box spanning very many cells (a long pipe) is clamped to
                // cells along its extent — a 10 m pipe is 20 cells at 500 mm.
                for (long x = C(box[0]); x <= C(box[3]); x++)
                    for (long y = C(box[1]); y <= C(box[4]); y++)
                        for (long z = C(box[2]); z <= C(box[5]); z++)
                        {
                            if (!_cells.TryGetValue((x, y, z), out var list)) _cells[(x, y, z)] = list = new List<(double[], string)>();
                            list.Add((box, key));
                        }
            }
            public IEnumerable<string> Containing(double[] p, double slack) =>
                _cells.TryGetValue((C(p[0]), C(p[1]), C(p[2])), out var list)
                    ? list.Where(e => Inside(p, e.Box, slack)).Select(e => e.Key).Distinct()
                    : Enumerable.Empty<string>();
        }
    }
}
