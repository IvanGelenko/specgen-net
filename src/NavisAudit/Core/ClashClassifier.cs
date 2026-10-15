using System;
using System.Collections.Generic;
using System.Linq;
using PipingSpecPlugin.Audit;

namespace NavisAudit.Core
{
    // One Clash Detective result, its two items resolved to audit objects.
    public sealed class RawClash
    {
        public string KeyA { get; set; }
        public string KeyB { get; set; }
        // The item is the part's insulation body (layer «…_Insulation»).
        public bool InsulationA { get; set; }
        public bool InsulationB { get; set; }
        // Penetration, mm (Clash Detective «Distance», sign dropped).
        public double DepthMm { get; set; }
        // The two clashing items' boxes overlapped, mm.
        public double[] Region { get; set; }
    }

    // Clash Detective finds every overlap; the Plant 3D audit's policies
    // decide which are clashes — the same ones, in the same order, as
    // DrawingAuditReader.CheckCollisions.
    public static class ClashClassifier
    {
        // others: objects that are not Plant parts (structures, solids,
        // proxies), by key.
        public static void Apply(NavisModel model, IEnumerable<RawClash> clashes, IReadOnlyDictionary<string, AuditPart> others, AuditOptions options)
        {
            var snapshot = model.Snapshot;
            AuditPart Find(string key)
            {
                if (key == null) return null;
                if (model.DuplicateOf.TryGetValue(key, out var original)) key = original;
                if (model.Parts.TryGetValue(key, out var part)) return part;
                if (model.Structures.TryGetValue(key, out part)) return part;
                return others.TryGetValue(key, out part) ? part : null;
            }
            bool IsStructure(AuditPart p) => !model.Parts.ContainsKey(p.Key);
            bool Selected(AuditPart p) => model.Parts.ContainsKey(p.Key) && options.Includes(p);

            // Several results per pair (a part of several bodies, a copy, its
            // insulation): one remark with the deepest overlap of the bodies,
            // and of the insulation apart.
            var pairs = new Dictionary<string, Pair>(StringComparer.Ordinal);
            foreach (var clash in clashes)
            {
                var a = Find(clash.KeyA);
                var b = Find(clash.KeyB);
                if (a == null || b == null || a.Key == b.Key) continue;
                // Pieces of one block insertion are one object.
                if (a.SelectionKey != null && string.Equals(a.SelectionKey, b.SelectionKey, StringComparison.Ordinal)) continue;
                var key = NavisModel.Pair(a.Key, b.Key);
                if (!pairs.TryGetValue(key, out var pair)) pairs[key] = pair = new Pair { A = a, B = b };
                if (clash.InsulationA || clash.InsulationB)
                {
                    pair.Insulation = Math.Max(pair.Insulation ?? 0, clash.DepthMm);
                    pair.InsulationRegion = Union(pair.InsulationRegion, clash.Region);
                }
                else
                {
                    pair.Body = Math.Max(pair.Body ?? 0, clash.DepthMm);
                    pair.Region = Union(pair.Region, clash.Region);
                }
            }

            foreach (var pair in pairs.Values)
            {
                var (a, b) = (pair.A, pair.B);
                // As DrawingAuditReader: the insulation counts when the bodies
                // stay within tolerance and it reaches deeper — never between
                // parts of one (filled-in) line (InsulationJointPolicy).
                double? depth = pair.Body;
                var region = pair.Region;
                bool insulation = false;
                if ((depth == null || depth <= options.CollisionToleranceMm) && pair.Insulation != null
                    && !InsulationJointPolicy.IgnoreInsulationOverlap(a, b) && (depth == null || pair.Insulation > depth))
                { depth = pair.Insulation; region = pair.InsulationRegion; insulation = true; }
                if (depth == null) continue;
                bool betweenStructures = IsStructure(a) && IsStructure(b);
                if (betweenStructures && !options.StructureCollisions) continue;
                if (!Selected(a) && !Selected(b) && !betweenStructures) continue;
                if (SupportContactPolicy.IgnoreContact(a, b)) { snapshot.SupportContactsIgnored++; continue; }
                snapshot.CollisionPairsChecked++;
                if (model.Joined(a.Key, b.Key) || ConnectedJointPolicy.IgnoreOverlap(a, b, model.Parts)) { snapshot.ConnectedJointsIgnored++; continue; }
                if (depth <= options.CollisionToleranceMm) { snapshot.CollisionsWithinTolerance++; continue; }
                if (betweenStructures && region != null)
                {
                    var ends = new List<double[]>();
                    if (model.StructureEnds.TryGetValue(a.Key, out var ea)) ends.AddRange(ea);
                    if (model.StructureEnds.TryGetValue(b.Key, out var eb)) ends.AddRange(eb);
                    if (StructureJointPolicy.IsNode(region, ends, 1.0)) { snapshot.StructureJointsIgnored++; continue; }
                }
                var tap = !insulation && model.OpenTaps.Contains(NavisModel.Pair(a.Key, b.Key));
                var issue = tap
                    ? AuditRules.Issue("collisions", "Коллизии", "Проверить",
                        $"Врезка: {a.Description} ↔ {b.Description}, глубина ≈ {depth.Value:0.#} мм. Конец трубы входит в другую трубу, а сварной шов врезки в модели не найден (у части объектов соединения нет координат). " +
                        "Если врезка выполнена в Plant 3D, это не коллизия; иначе труба вставлена без соединения.", new[] { a, b })
                    : AuditRules.Issue("collisions", "Коллизии", "Пересечение",
                        $"Обнаружено пересечение {(betweenStructures ? "конструкций" : insulation ? "с участием изоляции" : "объёмов")}: {a.Description} ↔ {b.Description}. " +
                        $"Глубина ≈ {depth.Value:0.#} мм. Проверьте, не является ли оно предусмотренным сопряжением.", new[] { a, b });
                issue.Bounds = region;
                issue.Magnitude = depth.Value;
                issue.BetweenStructures = betweenStructures;
                snapshot.GeometryIssues.Add(issue);
            }
        }

        sealed class Pair
        {
            public AuditPart A, B;
            public double? Body, Insulation;
            public double[] Region, InsulationRegion;
        }

        static double[] Union(double[] a, double[] b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return new[] { Math.Min(a[0], b[0]), Math.Min(a[1], b[1]), Math.Min(a[2], b[2]), Math.Max(a[3], b[3]), Math.Max(a[4], b[4]), Math.Max(a[5], b[5]) };
        }

        // The overlap of two boxes; null when they do not overlap.
        public static double[] Overlap(double[] a, double[] b)
        {
            if (a == null || b == null) return null;
            var box = new[] { Math.Max(a[0], b[0]), Math.Max(a[1], b[1]), Math.Max(a[2], b[2]), Math.Min(a[3], b[3]), Math.Min(a[4], b[4]), Math.Min(a[5], b[5]) };
            return box[0] <= box[3] && box[1] <= box[4] && box[2] <= box[5] ? box : null;
        }
    }
}
