using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisAudit.Core;
using PipingSpecPlugin.Audit;

namespace NavisAudit.Navis
{
    // The audit of the open Navisworks model: scanned once, rebuilt and
    // checked on each run with the user's options.
    public sealed class NavisAuditor
    {
        // The checks offered in Navisworks: the Plant 3D rules that need no
        // more than the model gives (no bolts: the flange faces and ports of
        // a joint are not in the model's properties).
        public static readonly string[] Checks = { "collisions", "wall", "material", "slope", "pipelength", "welds", "status", "placeholder", "tags", "connections" };

        public Document Document { get; }
        public ScanResult Scan { get; private set; }
        public NavisModel Model { get; private set; }

        public NavisAuditor(Document document) { Document = document; }

        public void Read(Action<string, double> progress = null)
        {
            Scan = ModelScanner.Scan(Document, progress);
            Model = Build();
        }

        NavisModel Build() => SnapshotBuilder.Build(Scan.Drawing, Scan.Items, Scan.ProxyKeys.Count, Scan.ProxyKeys);

        // Lines of the dialog: by element, natural order, «без номера» last.
        public List<AuditLineChoice> Lines() =>
            Model.Parts.Values.GroupBy(p => p.LineKey ?? AuditOptions.NoLine, StringComparer.Ordinal)
                .Select(g => new AuditLineChoice { Key = g.Key, Label = g.Key == AuditOptions.NoLine ? AuditOptions.NoLineLabel : g.Key, Count = g.Count() })
                .OrderBy(l => l.Key, Comparer<string>.Create(AuditLineOrder.Compare)).ToList();

        public AuditSnapshot Run(AuditOptions options, Action<string, double> progress = null)
        {
            Model = Build();
            var snapshot = Model.Snapshot;
            if (options.Enabled.Contains("collisions") && (options.SelectedLines == null || options.SelectedLines.Count > 0))
            {
                try
                {
                    var keys = Model.Parts.Values.Where(options.Includes).Select(p => p.Key).ToList();
                    var clashes = ClashRunner.Run(Document, Scan, keys, options.StructureCollisions, progress);
                    ClashClassifier.Apply(Model, clashes, Scan.Others, options);
                }
                catch (Exception ex)
                {
                    snapshot.GeometryIssues.Add(new AuditIssue { RuleId = "collisions", Check = "Коллизии", Severity = "Не проверено",
                        Message = "Коллизии не проверены: " + ex.Message });
                }
            }
            return snapshot;
        }

        // Model items of the issues' elements (copies included).
        public ModelItemCollection Items(IEnumerable<string> keys)
        {
            var items = new ModelItemCollection();
            foreach (var key in keys.Distinct())
                if (Scan.ByKey.TryGetValue(key, out var item)) items.Add(item);
            return items;
        }
    }
}
