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

            var cat = (r.PartCategory ?? "").Trim();
            var cls = (r.PnPClassName ?? "").Trim().ToLowerInvariant();
            if (cls == "support") return "Supports";
            if (!string.IsNullOrEmpty(cat))
            {
                foreach (var known in SpecConstants.CategoryLabelRu.Keys)
                    if (string.Equals(cat, known, StringComparison.OrdinalIgnoreCase)) return known;
                return cat;
            }

            // Some project catalogue rows omit PartCategory entirely. The
            // concrete engineering class still identifies the physical item.
            switch (cls)
            {
                case "boltset": case "gasket": return "Fasteners";
                case "pipe": case "pipebend": return "Pipe";
                case "flange": case "blindflange": return "Flanges";
                case "elbow": case "tee": case "reducer": case "cap":
                case "plug": case "nipple": case "olet": return "Fittings";
                case "valve": return "Valves";
                case "instrument": return "Instruments";
                case "filter": case "strainer": case "blinddisk": return "PipeSpecialties";
                case "thread": return "P3dConnector";
            }

            // Supports modelled as structure members (beams/frames) carry
            // no "TYPE=SUPPORT" iso symbol and an empty PartCategory, so
            // they'd drop out as empty-category. Recognise them by their
            // description ("Опора …" in the Russian catalogue) so all the
            // supports on a line make it into the spec, not just the one
            // real Plant 3D Pipe Support. Keyed on description rather than
            // the bare class name to avoid pulling in non-support
            // StructureMember geometry.
            foreach (var fld in new[] { r.ShortDescription, r.PartFamilyLongDesc, r.PartSizeLongDesc })
            {
                var t = (fld ?? "").ToLowerInvariant();
                foreach (var kw in SpecConstants.SupportKeywords)
                    if (t.Contains(kw)) return "Supports";
            }

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
        /// Spec section (bucket category) a part is emitted under, given its
        /// already-normalized <paramref name="category"/>.
        ///
        /// PipeBend ("ИЗГИБ ТРУБЫ") is the one class whose section depends on
        /// a user option rather than on the catalogue. Plant 3D catalogues are
        /// inconsistent here: most bends carry PartCategory="Pipe" (so they
        /// landed in «Трубы» and were measured in metres), some carry
        /// "Fittings" (→ «Детали трубопроводов», pieces). Same physical item,
        /// two sections, two units — depending only on how the catalogue row
        /// was authored.
        ///
        /// Rule: with «Учитывать изгибы как трубу» OFF every bend is a
        /// piping detail counted in pieces («Fittings»), regardless of its
        /// catalogue category. With the option ON bends fold into the
        /// adjacent pipe and never reach this method; the ones that DO reach
        /// it are orphans (no compatible pipe neighbour). Those used to keep
        /// their catalogue category — the same inconsistency in mirror image
        /// («Pipe» orphan → «Трубы», metres; «Fittings» orphan → «Детали»,
        /// pieces). Now every ON-mode orphan is «Pipe» in metres: that is
        /// what the user asked for by ticking the option, and it is exactly
        /// the historical behaviour of the common catalogue-"Pipe" bend.
        ///
        /// Deliberately NOT folded into <see cref="Normalize"/>: the picker
        /// chips (<see cref="ProjectFacts"/>) are built before the user sets
        /// the option, and the category filter must keep matching the chip
        /// the user actually sees — otherwise a project with no real
        /// "Fittings" parts (no such chip offered) would silently drop every
        /// bend at the category-filter gate.
        /// </summary>
        public static string SpecSection(string category, PartRecord r, bool pipeBendsAsPipe)
        {
            if (!r.IsPipeBend) return CanonicalSection(category);
            // An ON-mode orphan without a usable length cannot be measured in
            // metres: as «Pipe» it would hit the zero-length gate and vanish
            // from the spec (only an anonymous "N segments skipped" warning
            // remained). A physical bend is still a purchased item, so it
            // stays in «Детали трубопроводов» in pieces — exactly as in
            // OFF mode — instead of being lost.
            if (pipeBendsAsPipe && HasPositiveLength(r)) return "Pipe";
            return "Fittings";
        }

        /// <summary>
        /// Output section for catalogue-category synonyms. CategoryLabelRu
        /// carries singular/plural variants ("Support"/"Supports",
        /// "Instrument"/"Instruments", "PipeSpecialty"/"Specialty"/
        /// "PipeSpecialties") because catalogues use both. Normalize keeps
        /// them apart (the picker chips and the saved category selection are
        /// keyed on them), but in the OUTPUT they used to become separate
        /// sections with the SAME Russian header — «Опоры» twice, and the
        /// same support split into two rows. Folded here, after the filters.
        /// </summary>
        public static string CanonicalSection(string category)
        {
            switch (category)
            {
                case "Support": return "Supports";
                case "Instrument": return "Instruments";
                case "PipeSpecialty":
                case "Specialty": return "PipeSpecialties";
                default: return category;
            }
        }

        static bool HasPositiveLength(PartRecord r)
        {
            var len = SpecNumber.Parse(r.Length);
            return len.HasValue && len.Value > 0 && double.IsFinite(len.Value);
        }

        /// <summary>
        /// True for the categories a procurement spec must always keep —
        /// pipe, fittings, valves, flanges. PartReader uses this as a guard
        /// on EVERY path that marks a row "owned by equipment": topology
        /// (vessel → neighbour, part → nozzle sub-part, connector hop) and
        /// the AssetOwnership query alike. Those paths are geometric /
        /// relational heuristics; the pipe flange bolted to a nozzle or a
        /// pipe touching a vessel is a real purchased item, and dropping it
        /// silently is an under-count that looks complete. Real equipment
        /// sub-objects (nozzles, manways, internals) carry other categories
        /// and are unaffected; nozzles are additionally always dropped by
        /// SpecRowBuilder's equipment-nozzle gate.
        /// <paramref name="category"/> is the already-normalized category.
        /// </summary>
        public static bool IsProcurementCategory(string category)
            => string.Equals(category, "Pipe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, "Fittings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, "Valves", StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, "Flanges", StringComparison.OrdinalIgnoreCase);

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
