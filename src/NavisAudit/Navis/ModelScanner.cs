using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Navisworks.Api;
using NavisAudit.Core;
using PipingSpecPlugin.Audit;
using ComApi = Autodesk.Navisworks.Api.Interop.ComApi;
using ComBridge = Autodesk.Navisworks.Api.ComApi.ComApiBridge;

namespace NavisAudit.Navis
{
    public sealed class ScanResult
    {
        public string Drawing { get; set; }
        public double MmPerUnit { get; set; } = 1;
        public List<NavisItem> Items { get; } = new List<NavisItem>();
        // Plant objects (and proxies) by key, for selection and clash results.
        public Dictionary<string, ModelItem> ByKey { get; } = new Dictionary<string, ModelItem>(StringComparer.Ordinal);
        public Dictionary<ModelItem, string> KeyOf { get; } = new Dictionary<ModelItem, string>();
        public List<string> ProxyKeys { get; } = new List<string>();
        // Insulation bodies: Plant 3D exports a part's insulation as a second
        // object (same handle and properties) on the layer «…_Insulation».
        public HashSet<ModelItem> Insulation { get; } = new HashSet<ModelItem>();
        // Objects that are not Plant parts, met in clash results.
        public Dictionary<string, AuditPart> Others { get; } = new Dictionary<string, AuditPart>(StringComparer.Ordinal);
    }

    // Reads the Plant 3D objects of the open Navisworks model: their
    // «AutoCAD» property tab (Plant 3D Object Enabler), box and snap points.
    public static class ModelScanner
    {
        public const string PlantCategory = "LcAcDbxAutoCadProperty";
        static readonly Regex Index = new Regex(@"\[\d+\]$");

        public static ScanResult Scan(Document document, Action<string, double> progress = null)
        {
            var result = new ScanResult { Drawing = string.IsNullOrEmpty(document.FileName) ? document.Title : document.FileName,
                MmPerUnit = MillimetresPer(document.Units) };
            var found = new List<(ModelItem Item, bool Proxy)>();
            var stack = new Stack<ModelItem>();
            foreach (var root in document.Models.RootItems.Reverse()) stack.Push(root);
            int visited = 0;
            while (stack.Count > 0)
            {
                var item = stack.Pop();
                if (++visited % 5000 == 0) progress?.Invoke($"Просмотрено объектов модели: {visited}", 0.05);
                var dxf = item.ClassDisplayName ?? "";
                if (dxf.StartsWith("ACPPDB3DISO", StringComparison.OrdinalIgnoreCase)) continue; // isometric annotation
                var category = item.PropertyCategories.FindCategoryByName(PlantCategory);
                if (category != null && category.Properties.Any(p => Index.Replace(p.Name, "") == "PnPClassName"))
                {
                    found.Add((item, false));
                    continue;
                }
                if (string.Equals(item.ClassName, "ACAD_PROXY_ENTITY", StringComparison.OrdinalIgnoreCase)
                    && dxf.StartsWith("ACPP", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add((item, true));
                    continue;
                }
                var children = item.Children.ToList();
                for (int i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
            }

            var raw = new List<(ModelItem Item, NavisItem Data, bool Proxy)>();
            var wraps = new List<(ModelItem Item, NavisItem Data)>();
            for (int i = 0; i < found.Count; i++)
            {
                if (i % 200 == 0) progress?.Invoke($"Чтение деталей Plant 3D: {i} из {found.Count}", 0.1 + 0.4 * i / Math.Max(1, found.Count));
                var (item, proxy) = found[i];
                if (!proxy && IsInsulation(item))
                {
                    wraps.Add((item, new NavisItem { Handle = Text(item, "LcOpDwgEntityAttrib", "LcOaNat64AttributeValue"), Source = SourceOf(item),
                        Box = Box(item, result.MmPerUnit) }));
                    continue;
                }
                var data = new NavisItem
                {
                    Handle = Text(item, "LcOpDwgEntityAttrib", "LcOaNat64AttributeValue"),
                    Source = SourceOf(item),
                    Layer = Text(item, "LcOaNode", "LcOaNodeLayer"),
                    Container = XrefsOf(item),
                    DisplayClass = item.ClassDisplayName,
                    Box = Box(item, result.MmPerUnit),
                };
                if (!proxy)
                {
                    var category = item.PropertyCategories.FindCategoryByName(PlantCategory);
                    foreach (var property in category.Properties)
                    {
                        var name = Index.Replace(property.Name ?? "", "");
                        if (name.StartsWith("autocad_ubiquity_", StringComparison.Ordinal)) name = name.Substring("autocad_ubiquity_".Length);
                        var key = name;
                        for (int n = 1; data.Props.ContainsKey(key); n++) key = name + "#" + n;
                        data.Props[key] = Value(property.Value);
                    }
                    if (item.HasGeometry) ReadSnaps(item, data, result.MmPerUnit);
                }
                raw.Add((item, data, proxy));
            }

            // Keys: the DWG handle where it is unique in the model, else with
            // the xref (or file) it came through — «4E3F (Общая подложка)» —
            // and «#2»… for one object present more than once there.
            var unique = raw.GroupBy(r => r.Data.Handle ?? "", StringComparer.OrdinalIgnoreCase).Where(g => g.Count() == 1).Select(g => g.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (item, data, proxy) in raw)
            {
                var where = string.IsNullOrEmpty(data.Container) ? Path.GetFileNameWithoutExtension(data.Source ?? "")
                    : data.Container.Substring(data.Container.LastIndexOf('→') + 1).Trim();
                var key = string.IsNullOrEmpty(data.Handle) ? "объект" : unique.Contains(data.Handle) ? data.Handle
                    : string.IsNullOrEmpty(where) ? data.Handle : $"{data.Handle} ({where})";
                var candidate = key;
                for (int n = 2; !used.Add(candidate); n++) candidate = key + "#" + n;
                data.Key = candidate;
                result.ByKey[candidate] = item;
                result.KeyOf[item] = candidate;
                if (proxy)
                {
                    result.ProxyKeys.Add(candidate);
                    result.Others[candidate] = new AuditPart { Key = candidate, SelectionKey = candidate, Source = data.Source,
                        Description = $"{data.DisplayClass} без свойств Plant 3D{(string.IsNullOrWhiteSpace(data.Layer) ? "" : $", слой «{data.Layer}»")}" };
                }
                else result.Items.Add(data);
            }
            // An insulation body belongs to the part it wraps: the same file
            // and handle, the part's box inside it (an xref attached twice
            // has two such parts).
            var bodies = raw.Where(r => !r.Proxy).ToLookup(r => (r.Data.Source ?? "") + "|" + (r.Data.Handle ?? ""), StringComparer.OrdinalIgnoreCase);
            foreach (var (item, data) in wraps)
            {
                var candidates = bodies[(data.Source ?? "") + "|" + (data.Handle ?? "")].ToList();
                var body = candidates.FirstOrDefault(c => c.Data.Box != null && data.Box != null && SnapshotBuilder.Inside(c.Data.Box.Take(3).ToArray(), data.Box, 1)
                    && SnapshotBuilder.Inside(c.Data.Box.Skip(3).ToArray(), data.Box, 1));
                if (body.Item == null) body = candidates.FirstOrDefault();
                if (body.Item == null) continue;
                result.KeyOf[item] = body.Data.Key;
                result.Insulation.Add(item);
            }
            return result;
        }

        // «A → B»: the xrefs above the item inside its file (an xref is an
        // insertion carrying the «Внешняя ссылка» tab, its block the name).
        static string XrefsOf(ModelItem item)
        {
            var names = new List<string>();
            foreach (var level in item.Ancestors)
            {
                if (string.Equals(level.ClassName, "LcOaPartition", StringComparison.OrdinalIgnoreCase)) break;
                if (!string.Equals(level.ClassName, "LcDWGNwcBlock", StringComparison.OrdinalIgnoreCase)) continue;
                var insert = level.Parent;
                if (IsXref(level) || (insert != null && IsXref(insert))) names.Add(level.DisplayName);
            }
            names.Reverse();
            return names.Count == 0 ? null : string.Join(" → ", names);
        }

        // The «Внешняя ссылка» tab is on the xref's insertion or its block.
        static bool IsXref(ModelItem item) => item.PropertyCategories.FindCategoryByName("NameAttribute") != null;

        // On a layer «…_Insulation» (an xref's «drawing|0_Insulation» too).
        static bool IsInsulation(ModelItem item)
        {
            bool Wrap(string layer) => (layer ?? "").TrimEnd().EndsWith("_Insulation", StringComparison.OrdinalIgnoreCase);
            if (Wrap(Text(item, "LcOaNode", "LcOaNodeLayer"))) return true;
            foreach (var level in item.Ancestors)
                if (string.Equals(level.ClassName, "LcDWGNwcLayer", StringComparison.OrdinalIgnoreCase)) return Wrap(level.DisplayName);
            return false;
        }

        // The clash item is (part of) an insulation body.
        public static bool InInsulation(ScanResult scan, ModelItem item) => scan.Insulation.Count > 0 && item.AncestorsAndSelf.Any(scan.Insulation.Contains);

        // The audit object a clash item belongs to: its Plant object (a
        // flanged joint's gasket and bolts are children of the connector),
        // else the item itself, described by class, layer and file.
        public static string Owner(ScanResult scan, ModelItem item)
        {
            foreach (var level in item.AncestorsAndSelf)
                if (scan.KeyOf.TryGetValue(level, out var key)) return key;
            var handle = Text(item, "LcOpDwgEntityAttrib", "LcOaNat64AttributeValue");
            var source = SourceOf(item);
            var xrefs = XrefsOf(item);
            var where = string.IsNullOrEmpty(xrefs) ? Path.GetFileNameWithoutExtension(source ?? "") : xrefs.Substring(xrefs.LastIndexOf('→') + 1).Trim();
            var baseKey = string.IsNullOrEmpty(handle) ? "объект " + (item.InstanceGuid == Guid.Empty ? item.GetHashCode().ToString("X") : item.InstanceGuid.ToString("N").Substring(0, 8))
                : string.IsNullOrEmpty(where) ? handle : $"{handle} ({where})";
            if (!string.IsNullOrEmpty(xrefs)) source = $"{source} → {xrefs}";
            var other = baseKey;
            for (int n = 2; scan.ByKey.TryGetValue(other, out var existing) && !existing.Equals(item); n++) other = baseKey + "#" + n;
            if (!scan.ByKey.ContainsKey(other))
            {
                scan.ByKey[other] = item;
                scan.KeyOf[item] = other;
                var block = BlockOf(item);
                var layer = Text(item, "LcOaNode", "LcOaNodeLayer");
                var kind = item.ClassDisplayName ?? item.ClassName ?? "Объект";
                scan.Others[other] = new AuditPart
                {
                    Key = other, Source = source,
                    // Pieces of one block insertion are one object (not of an xref).
                    SelectionKey = block == null ? other : "block:" + (scan.KeyOf.TryGetValue(block, out var b) ? b : Register(scan, block)),
                    Description = $"{kind}{(string.IsNullOrWhiteSpace(layer) ? "" : $", слой «{layer}»")}{(string.IsNullOrWhiteSpace(source) ? "" : $" ({source})")}",
                };
            }
            return other;
        }

        static string Register(ScanResult scan, ModelItem block)
        {
            var key = "блок " + (block.DisplayName ?? "") + " " + block.GetHashCode().ToString("X");
            scan.KeyOf[block] = key;
            return key;
        }

        // The nearest block insertion above the item — not an xref, whose
        // objects are separate objects.
        static ModelItem BlockOf(ModelItem item)
        {
            foreach (var level in item.Ancestors)
            {
                if (string.Equals(level.ClassName, "LcOaPartition", StringComparison.OrdinalIgnoreCase)) return null;
                if (!string.Equals(level.ClassName, "LcDWGNwcInsert", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsXref(level) || level.Children.Any(IsXref)) return null; // an xref: separate objects
                return level;
            }
            return null;
        }

        static string SourceOf(ModelItem item)
        {
            var source = Text(item, "LcOaNode", "LcOaNodeSourceFile");
            if (!string.IsNullOrWhiteSpace(source)) return source;
            var file = item.AncestorsAndSelf.FirstOrDefault(a => string.Equals(a.ClassName, "LcOaPartition", StringComparison.OrdinalIgnoreCase));
            return file?.DisplayName;
        }

        public static string Text(ModelItem item, string category, string property)
        {
            try { return item.PropertyCategories.FindPropertyByName(category, property)?.Value is VariantData v ? Value(v) : null; }
            catch { return null; }
        }

        static string Value(VariantData value)
        {
            if (value == null) return null;
            try
            {
                if (value.IsDisplayString) return value.ToDisplayString();
                if (value.IsIdentifierString) return value.ToIdentifierString();
                if (value.IsDouble) return value.ToDouble().ToString("R", CultureInfo.InvariantCulture);
                if (value.IsDoubleLength) return value.ToDoubleLength().ToString("R", CultureInfo.InvariantCulture);
                if (value.IsInt32) return value.ToInt32().ToString(CultureInfo.InvariantCulture);
                if (value.IsBoolean) return value.ToBoolean() ? "True" : "False";
                return value.ToString();
            }
            catch { return null; }
        }

        public static double[] Box(ModelItem item, double mmPerUnit)
        {
            try
            {
                var box = item.BoundingBox();
                if (box == null || box.IsEmpty || box.Min.X > box.Max.X) return null;
                return new[] { box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z }.Select(v => v * mmPerUnit).ToArray();
            }
            catch { return null; }
        }

        // Snap points of the item's own geometry fragments, world frame, mm.
        static void ReadSnaps(ModelItem item, NavisItem data, double mmPerUnit)
        {
            try
            {
                var path = ComBridge.ToInwOaPath(item);
                var collector = new SnapCollector { MmPerUnit = mmPerUnit };
                foreach (ComApi.InwOaFragment3 fragment in path.Fragments())
                {
                    var matrix = (Array)(object)((ComApi.InwLTransform3f3)fragment.GetLocalToWorldMatrix()).Matrix;
                    collector.Matrix = new double[16];
                    int lower = matrix.GetLowerBound(0);
                    for (int k = 0; k < 16; k++) collector.Matrix[k] = Convert.ToDouble(matrix.GetValue(lower + k), CultureInfo.InvariantCulture);
                    fragment.GenerateSimplePrimitives(ComApi.nwEVertexProperty.eNORMAL, collector);
                }
                data.Snaps.AddRange(collector.Points);
            }
            catch
            {
                // No snap points: the part's ports count as not read
                // (SnapshotBuilder), nothing else is lost.
            }
        }

        sealed class SnapCollector : ComApi.InwSimplePrimitivesCB
        {
            public double[] Matrix;
            public double MmPerUnit;
            public readonly List<double[]> Points = new List<double[]>();

            public void SnapPoint(ComApi.InwSimpleVertex v1)
            {
                var c = (Array)(object)v1.coord;
                int lower = c.GetLowerBound(0);
                double x = Convert.ToDouble(c.GetValue(lower)), y = Convert.ToDouble(c.GetValue(lower + 1)), z = Convert.ToDouble(c.GetValue(lower + 2));
                var m = Matrix;
                Points.Add(new[]
                {
                    (m[0] * x + m[4] * y + m[8] * z + m[12]) * MmPerUnit,
                    (m[1] * x + m[5] * y + m[9] * z + m[13]) * MmPerUnit,
                    (m[2] * x + m[6] * y + m[10] * z + m[14]) * MmPerUnit,
                });
            }
            public void Triangle(ComApi.InwSimpleVertex v1, ComApi.InwSimpleVertex v2, ComApi.InwSimpleVertex v3) { }
            public void Line(ComApi.InwSimpleVertex v1, ComApi.InwSimpleVertex v2) { }
            public void Point(ComApi.InwSimpleVertex v1) { }
        }

        public static double MillimetresPer(Units units)
        {
            switch (units)
            {
                case Units.Millimeters: return 1;
                case Units.Centimeters: return 10;
                case Units.Meters: return 1000;
                case Units.Kilometers: return 1e6;
                case Units.Micrometers: return 1e-3;
                case Units.Inches: return 25.4;
                case Units.Feet: return 304.8;
                case Units.Yards: return 914.4;
                case Units.Miles: return 1609344;
                case Units.Mils: return 0.0254;
                case Units.Microinches: return 25.4e-6;
                default: return 1;
            }
        }
    }
}
