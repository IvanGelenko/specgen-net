using System.Collections.Generic;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Sentinels and Russian labels used throughout the spec pipeline.
    /// Keep the section-header strings identical to the .docx template's
    /// header text — the writer locates the table to fill by matching them.
    /// </summary>
    public static class SpecConstants
    {
        public const string MaterialsSectionHeader = "Изделия и материалы";

        // Trailing section the writer appends AFTER every regular section
        // (see ReviewSection). Unlike MaterialsSectionHeader it is NOT
        // looked up in the template — the writer emits it itself — so it
        // can be renamed freely.
        public const string ReviewSectionHeader = "Позиции, требующие проверки";

        public const string NullCategory = "__NULL__";
        public const string OtherCategoryLabel = "Прочее";
        public const string NullStatus = "__NULL_STATUS__";
        public const string NullStatusLabel = "Без статуса";
        public const string NullCategoryLabel = "Без категории";

        public const string NullLine = "__NO_LINE__";
        public const string NullLineLabel = "Без номера линии";

        // Order matches the layout the user signed off on:
        // Оборудование → Трубы → Детали → Арматура → Приборы →
        // Спецдетали → Опоры → Сварные швы → Фланцы → Крепёж
        // (→ Прочие изделия, where catalogue categories outside this list
        // used to land anyway). Welds/Flanges/Fasteners move to the end so
        // the procurement sections (the bigger structural items) come first.
        // Synonym keys ("Instrument", "Support", …) never reach the output —
        // CategoryClassifier.CanonicalSection folds them — they stay listed
        // for callers that sort raw catalogue keys.
        public static readonly string[] CategoryOrder =
        {
            "Equipment",
            "Pipe",
            "Fittings",
            "Valves",
            "Instruments",
            "Instrument",
            "PipeSpecialties",
            "PipeSpecialty",
            "Specialty",
            "Supports",
            "Support",
            "P3dConnector",
            "Flanges",
            "Fasteners",
            "Miscellaneous",
        };

        public static readonly Dictionary<string, string> CategoryLabelRu =
            new Dictionary<string, string>
            {
                { "Equipment",       "Оборудование" },
                { "Pipe",            "Трубы" },
                { "Fittings",        "Детали трубопроводов" },
                { "Flanges",         "Фланцы" },
                { "Valves",          "Арматура" },
                { "Instruments",     "Приборы" },
                { "Instrument",      "Приборы" },
                { "PipeSpecialties", "Специальные детали" },
                { "PipeSpecialty",   "Специальные детали" },
                { "Specialty",       "Специальные детали" },
                { "Supports",        "Опоры" },
                { "Support",         "Опоры" },
                { "Fasteners",       "Крепёж" },
                { "P3dConnector",    "Сварные швы / коннекторы" },
                // Plant 3D's own catalogue category for blind disks, caps,
                // strainers… (42 parts in the 0873 project). Without a label
                // the section header and the picker chip read
                // "Miscellaneous" in English in a Russian ГОСТ document.
                { "Miscellaneous",   "Прочие изделия" },
            };

        // Connector keywords (case-insensitive substring match). A row
        // is treated as a connector and excluded if its category is
        // null/empty/Fasteners AND any of these substrings appear in
        // its description.
        public static readonly string[] ConnectorKeywords =
            { "buttweld", "universal", "weld joint", "weld connector" };

        // Support keywords (case-insensitive substring match). Supports
        // modelled as StructureMember (beams/frames) instead of Plant 3D
        // Pipe Supports have no "TYPE=SUPPORT" iso symbol and an empty
        // PartCategory, so they'd otherwise drop out as "empty-category".
        // When such a part's description matches one of these, it lands
        // in the "Supports" bucket. "опора" is the Russian catalogue word.
        public static readonly string[] SupportKeywords =
            { "опора", "support" };
    }
}
