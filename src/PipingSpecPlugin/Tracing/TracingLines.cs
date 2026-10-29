using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.ProcessPower.PnP3dObjects;
using Exception = System.Exception;

namespace PipingSpecPlugin.Tracing
{
    /// <summary>A pipeline of the active drawing: its number and the plain pipes it consists of.</summary>
    internal sealed class TracingLine
    {
        public string Tag;                       // LineNumberTag; "(без номера линии)" when empty
        public string Sizes;                     // "80" or "80, 50"
        public double LengthMm;
        public List<(string handle, double length)> Pipes = new List<(string, double)>();
        public int PipeCount => Pipes.Count;
    }

    internal static partial class TracingEngine
    {
        public const string NoLineTag = "(без номера линии)";

        /// <summary>
        /// The pipelines of the live drawing (not of the saved file): pipes grouped by their LineNumberTag.
        /// The tracers themselves (the DN20 25x4 pipes of an earlier tracing) are not lines and are left out.
        /// </summary>
        internal static List<TracingLine> CollectLines(Document doc)
        {
            var dlm = Dlm();
            var byTag = new Dictionary<string, TracingLine>(StringComparer.OrdinalIgnoreCase);
            var sizes = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.ObjectClass.Name != "AcPpDb3dPipe") continue;
                    int row = RowId(dlm, id); if (row <= 0) continue;
                    double od; string layer;
                    try { var pipe = (Pipe)tr.GetObject(id, OpenMode.ForRead); od = pipe.OuterDiameter; layer = pipe.Layer; } catch (Exception) { continue; }
                    // a tracer is told by its layer (a real 25 mm pipe of a process line stays in the list); a 25 mm pipe on the default layer without a line number is taken for an old tracer
                    if (string.Equals(layer, "Спутник", StringComparison.OrdinalIgnoreCase) || string.Equals(layer, "Спутник-коллизия", StringComparison.OrdinalIgnoreCase)) continue;
                    Dictionary<string, string> p;
                    try { p = Props(dlm, row); } catch (Exception) { continue; }
                    if (Math.Abs(od - 25) < 0.6 && !(p.TryGetValue("LineNumberTag", out var ltag) && !string.IsNullOrWhiteSpace(ltag))) continue;
                    string tag = p.TryGetValue("LineNumberTag", out var t) && !string.IsNullOrWhiteSpace(t) ? t.Trim() : NoLineTag;
                    string size = p.TryGetValue("Size", out var sz) ? sz : "?";
                    var ports = SafePorts(doc, id);
                    var s1 = ports.FirstOrDefault(q => q.name == "S1"); var s2 = ports.FirstOrDefault(q => q.name == "S2");
                    if (s1.name == null || s2.name == null) continue;
                    double len = s1.pos.DistanceTo(s2.pos);
                    if (!byTag.TryGetValue(tag, out var line)) { byTag[tag] = line = new TracingLine { Tag = tag }; sizes[tag] = new SortedSet<string>(); }
                    line.Pipes.Add((id.Handle.ToString(), len)); line.LengthMm += len; sizes[tag].Add(size);
                }
                tr.Commit();
            }
            foreach (var kv in byTag) kv.Value.Sizes = string.Join(", ", sizes[kv.Key]);
            return byTag.Values.OrderBy(l => l.Tag == NoLineTag ? 1 : 0).ThenBy(l => l.Tag, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }
}
