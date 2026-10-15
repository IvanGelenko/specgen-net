using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisAudit.Core;

namespace NavisAudit.Navis
{
    // Runs a temporary Clash Detective test (Hard) of the selected lines
    // against the whole model and returns its results; the test is removed
    // again, so the model's own clash tests are left as they were.
    public static class ClashRunner
    {
        public const string TestName = "Аудит трубопроводов (временная проверка)";

        public static List<RawClash> Run(Document document, ScanResult scan, IEnumerable<string> selectedKeys, bool everything, Action<string, double> progress = null)
        {
            var clash = document.GetClash() ?? throw new InvalidOperationException(
                "Clash Detective недоступен: он есть только в Navisworks Manage.");
            var left = new ModelItemCollection();
            if (everything) left.AddRange(document.Models.RootItems);
            else foreach (var key in selectedKeys) if (scan.ByKey.TryGetValue(key, out var item)) left.Add(item);
            var result = new List<RawClash>();
            if (left.Count == 0) return result;
            var right = new ModelItemCollection();
            right.AddRange(document.Models.RootItems);

            var test = new ClashTest
            {
                DisplayName = TestName,
                TestType = ClashTestType.Hard,
                // Touching surfaces are not reported; the user's tolerance is
                // applied afterwards (ClashClassifier counts what it hides).
                Tolerance = 0.01 / scan.MmPerUnit,
            };
            test.SelectionA.Selection.CopyFrom(left);
            test.SelectionB.Selection.CopyFrom(right);
            test.SelectionA.PrimitiveTypes = PrimitiveTypes.Triangles;
            test.SelectionB.PrimitiveTypes = PrimitiveTypes.Triangles;
            var tests = clash.TestsData;
            tests.TestsAddCopy(test);
            var added = tests.Tests.OfType<ClashTest>().LastOrDefault(t => t.DisplayName == TestName);
            if (added == null) throw new InvalidOperationException("Не удалось создать проверку Clash Detective.");
            try
            {
                progress?.Invoke("Clash Detective: поиск пересечений…", 0.6);
                tests.TestsRunTest(added);
                added = tests.Tests.OfType<ClashTest>().LastOrDefault(t => t.DisplayName == TestName) ?? added;
                var boxes = new Dictionary<ModelItem, double[]>();
                double[] BoxOf(ModelItem item)
                {
                    if (item == null) return null;
                    if (!boxes.TryGetValue(item, out var box)) boxes[item] = box = ModelScanner.Box(item, scan.MmPerUnit);
                    return box;
                }
                foreach (var r in Results(added))
                {
                    if (r.Item1 == null || r.Item2 == null) continue;
                    result.Add(new RawClash
                    {
                        KeyA = ModelScanner.Owner(scan, r.Item1),
                        KeyB = ModelScanner.Owner(scan, r.Item2),
                        InsulationA = ModelScanner.InInsulation(scan, r.Item1),
                        InsulationB = ModelScanner.InInsulation(scan, r.Item2),
                        DepthMm = Math.Max(0, -r.Distance) * scan.MmPerUnit,
                        Region = ClashClassifier.Overlap(BoxOf(r.Item1), BoxOf(r.Item2)),
                    });
                }
            }
            finally
            {
                try { tests.TestsRemove(added); } catch { }
            }
            return result;
        }

        static IEnumerable<ClashResult> Results(GroupItem group)
        {
            foreach (var child in group.Children)
            {
                if (child is ClashResult result) yield return result;
                else if (child is GroupItem inner) foreach (var nested in Results(inner)) yield return nested;
            }
        }
    }
}
