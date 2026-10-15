using System;
using System.Collections.Generic;

namespace PipingSpecPlugin.Spec
{
    public static class PartPropertyReader
    {
        public static PartRecord Build(int rowId, IEnumerable<KeyValuePair<string, string>> props,
                                       string resolvedLineNumberTag = null)
        {
            // O(1) Lookup: convert list to dictionary to avoid 35+ linear scans
            // of ~90 items per row. Keeps the first occurrence if keys duplicate.
            var propDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in props)
            {
                if (kv.Key != null && !propDict.ContainsKey(kv.Key))
                    propDict.Add(kv.Key, kv.Value);
            }

            string Get(string key)
            {
                propDict.TryGetValue(key, out var val);
                return val;
            }

            string FirstValue(params string[] names)
            {
                foreach (var name in names)
                {
                    var value = Get(name);
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
                return null;
            }

            if (string.IsNullOrEmpty(resolvedLineNumberTag))
                resolvedLineNumberTag = Get("LineNumberTag");
            var isBend = (Get("PnPClassName") ?? "").IndexOf("PipeBend", StringComparison.OrdinalIgnoreCase) >= 0;

            return new PartRecord
            {
                PnPID = rowId,
                SourcePnPID = rowId,
                Status = Get("Status"),
                PartCategory = Get("PartCategory"),
                PartFamilyLongDesc = Get("PartFamilyLongDesc"),
                PartSizeLongDesc = Get("PartSizeLongDesc"),
                ShortDescription = Get("ShortDescription"),
                NominalDiameter = Get("NominalDiameter"),
                NominalUnit = Get("NominalUnit"),
                Material = Get("Material"),
                MaterialCode = Get("MaterialCode"),
                Spec = Get("Spec"),
                ItemCode = Get("ItemCode"),
                Manufacturer = Get("Manufacturer"),
                DesignStd = Get("DesignStd"),
                CompatibleStandard = Get("CompatibleStandard"),
                Weight = Get("Weight"),
                LengthUnit = Get("LengthUnit"),
                WeightUnit = Get("WeightUnit"),
                // Pipe carries length under "Length" (full segment).
                // PipeBend doesn't populate that field — its physical
                // arc length lives in "CutLength" (verified against the
                // ru-RU DisplayNames.xml entry "$$dn-PipeBend-CutLength").
                // For bends use the arc length first, even if generic Length
                // contains a zero placeholder. Empty fields do not block fallback.
                Length = isBend ? FirstValue("CutLength", "Length") : FirstValue("Length", "CutLength"),
                LinearWeight = Get("LinearWeight"),
                LinearWeightUnit = Get("LinearWeightUnit"),
                WallThickness = Get("WallThickness"),
                Schedule = Get("Schedule"),
                LineNumberTag = resolvedLineNumberTag,
                ContentIsoSymbolDefinition = Get("ContentIsoSymbolDefinition"),
                EquipTag = Get("Tag"),
                PositionX = Get("Position X"),
                PositionY = Get("Position Y"),
                PositionZ = Get("Position Z"),
                CogX = Get("COG X"),
                CogY = Get("COG Y"),
                CogZ = Get("COG Z"),
                PnPClassName = Get("PnPClassName"),
                PressureClass = Get("PressureClass"),
                Facing = Get("Facing"),
                // Bolt sets carry their piece count under "NumberInSet"
                // — confirmed against ru-RU/DisplayNames.xml entry
                // "$$dn-BoltSet-NumberInSet -> Количество в комплекте".
                // Probe a few alternates too in case other part families
                // use slightly different names.
                Quantity = FirstValue("NumberInSet", "Quantity", "NumBolts", "BoltCount", "Count", "NumberOfBolts"),
                InsulationThickness = Get("InsulationThickness"),
            };
        }
    }
}
