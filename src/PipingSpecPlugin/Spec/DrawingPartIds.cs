using System;
using System.Collections.Generic;
using System.Linq;

namespace PipingSpecPlugin.Spec
{
    // Local DLM row numbers are scoped to a drawing. Give all downstream
    // dictionaries one scan-wide namespace, including non-record graph nodes.
    //
    // Safe to call again on the same data: DLM row ids are positive and
    // scan-wide ids are negative (counted down from -1), so an id that is
    // already one of ours is recognised and passed through, and a record
    // that was already remapped keeps its SourcePnPID instead of having it
    // overwritten with the scan-wide id (which used to happen — a second
    // Remap re-numbered everything and lost the original row number).
    internal sealed class DrawingPartIds
    {
        readonly Dictionary<string, Dictionary<int, int>> drawings = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<int> issued = new();
        readonly HashSet<PartRecord> remapped = new(ReferenceEqualityComparer.Instance);
        int next;

        public void Remap(string path, IList<PartRecord> records, HashSet<int> owned,
            Dictionary<int, List<int>> topology)
        {
            if (!drawings.TryGetValue(path, out var ids)) drawings[path] = ids = new();
            int Map(int id)
            {
                if (issued.Contains(id)) return id;   // already scan-wide
                if (!ids.TryGetValue(id, out var mapped))
                {
                    ids[id] = mapped = checked(--next);
                    issued.Add(mapped);
                }
                return mapped;
            }
            foreach (var record in records)
            {
                if (!remapped.Add(record)) continue;
                record.SourcePnPID = record.PnPID;
                record.SourceDwgPath = path;
                record.PnPID = Map(record.PnPID);
            }
            var scopedOwned = owned.Select(Map).ToArray();
            var scopedGraph = topology.ToDictionary(k => Map(k.Key), v => v.Value.Select(Map).ToList());
            owned.Clear();
            owned.UnionWith(scopedOwned);
            topology.Clear();
            foreach (var edge in scopedGraph) topology.Add(edge.Key, edge.Value);
        }
    }
}
