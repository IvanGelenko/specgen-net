using System;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Single source of truth for category classification — what bucket a
    /// PartRecord lands in, and whether it counts as a connector. Both the
    /// picker (<see cref="ProjectFacts"/>) and the aggregator
    /// (<see cref="SpecRowBuilder"/>) must agree on these, otherwise the
    /// counts the user ticks in the dialog won't match what actually ends
    /// up in the spec. This logic used to be copy-pasted in both places
    /// with a "keep in sync" comment; it now lives here once.
    ///
    /// Note: <see cref="PipeBendFolder"/> and <see cref="ValveCompanionFinder"/>
    /// keep their own, deliberately narrower connector checks (class-name
    /// only / strict P3dConnector) because they walk topology rather than
    /// classify spec rows — those are intentionally not unified here.
    /// </summary>
    internal static class CategoryClassifier
    {
        /// <summary>
        /// Canonical category for a part. Supports are detected via the iso
        /// symbol definition substring; parts with an empty PartCategory get
        /// promoted to a synthetic "P3dConnector"/"Equipment" bucket by
        /// class name so they earn their own picker chip instead of being
        /// lumped into "Без категории".
        /// </summary>
        public static string Normalize(PartRecord r)
        {
            if (!string.IsNullOrEmpty(r.ContentIsoSymbolDefinition)
                && r.ContentIsoSymbolDefinition.IndexOf("TYPE=SUPPORT",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                return "Supports";

            var cat = r.PartCategory ?? "";
            if (!string.IsNullOrEmpty(cat)) return cat;

            var cls = (r.PnPClassName ?? "").ToLowerInvariant();
            if (cls == "p3dconnector" || cls == "buttweld"
                || cls == "universal" || cls == "weldspec"
                || cls.Contains("weld") || cls.Contains("connector"))
                return "P3dConnector";
            if (cls == "vessel" || cls == "pump"
                || cls.Contains("equipment") || cls.Contains("vessel")
                || cls.Contains("pump") || cls.Contains("tank")
                || cls.Contains("exchanger") || cls.Contains("compressor"))
                return "Equipment";
            return "";
        }

        /// <summary>
        /// True when the part is a connector for spec-filtering purposes:
        /// the synthetic "P3dConnector" category, or a description keyword
        /// match for the rare empty-category / Fasteners leftovers.
        /// <paramref name="category"/> is the already-normalized category.
        /// </summary>
        public static bool IsConnector(string category, PartRecord r)
        {
            if (category == "P3dConnector") return true;
            if (!(string.IsNullOrEmpty(category)
                  || category == SpecConstants.NullCategory
                  || category == "Fasteners"))
                return false;
            foreach (var fld in new[] { r.ShortDescription, r.PartSizeLongDesc, r.PartFamilyLongDesc })
            {
                var t = (fld ?? "").ToLowerInvariant();
                foreach (var kw in SpecConstants.ConnectorKeywords)
                    if (t.Contains(kw)) return true;
            }
            return false;
        }
    }
}
