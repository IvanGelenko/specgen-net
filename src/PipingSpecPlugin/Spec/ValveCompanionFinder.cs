using System;
using System.Collections.Generic;

namespace PipingSpecPlugin.Spec
{
    /// <summary>Finds the flanges and fasteners of the same joint as a valve.</summary>
    public static class ValveCompanionFinder
    {
        public static HashSet<int> Discover(IList<PartRecord> records,
                                           Dictionary<int, List<int>> topology)
        {
            var excluded = new HashSet<int>();
            if (records == null || topology == null) return excluded;

            var byId = new Dictionary<int, PartRecord>(records.Count);
            foreach (var record in records)
                byId.TryAdd(record.PnPID, record);

            foreach (var valve in byId.Values)
            {
                if (CategoryClassifier.Normalize(valve) != "Valves"
                    || !topology.TryGetValue(valve.PnPID, out var neighbours))
                    continue;

                foreach (var connectorId in neighbours)
                {
                    if (!byId.TryGetValue(connectorId, out var connector)
                        || !(string.Equals(connector.PnPClassName, "P3dConnector", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(connector.PartCategory, "P3dConnector", StringComparison.OrdinalIgnoreCase))
                        || !topology.TryGetValue(connectorId, out var joint))
                        continue;

                    bool hasFlange = false;
                    foreach (var id in joint)
                        if (byId.TryGetValue(id, out var part) && CategoryClassifier.Normalize(part) == "Flanges")
                        {
                            excluded.Add(id);
                            hasFlange = true;
                        }

                    if (!hasFlange) continue;
                    // PartReader adds the connector's own sub-part rows to
                    // this graph. Coordinates alone cannot establish ownership.
                    foreach (var id in joint)
                        if (byId.TryGetValue(id, out var part) && IsJointFastener(part))
                            excluded.Add(id);
                }
            }
            return excluded;
        }

        static bool IsJointFastener(PartRecord part)
        {
            if (CategoryClassifier.Normalize(part) != "Fasteners") return false;

            foreach (var value in new[] { part.PnPClassName, part.ShortDescription,
                         part.PartFamilyLongDesc, part.PartSizeLongDesc })
            {
                var text = value?.ToLowerInvariant();
                if (text != null && (text.Contains("gasket") || text.Contains("прокладк")
                    || text.Contains("boltset") || text.Contains("bolt set")
                    || text.Contains("stud") || text.Contains("болт")))
                    return true;
            }
            return false;
        }
    }
}
