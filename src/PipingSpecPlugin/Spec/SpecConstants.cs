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

        public const string NullCategory = "__NULL__";
        public const string OtherCategoryLabel = "Прочее";
        public const string NullStatus = "__NULL_STATUS__";
        public const string NullStatusLabel = "Без статуса";
        public const string NullCategoryLabel = "Без категории";

        public const string NullLine = "__NO_LINE__";
        public const string NullLineLabel = "Без номера линии";

        // Order matches the layout the user signed off on:
        // Оборудование → Трубы → Детали → Арматура → Приборы →
        // Спецдетали → Опоры → Сварные швы → Фланцы → Крепёж.
        // Welds/Flanges/Fasteners move to the end so the procurement
        // sections (the bigger structural items) come first.
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
            };

        // Connector keywords (case-insensitive substring match). A row
        // is treated as a connector and excluded if its category is
        // null/empty/Fasteners AND any of these substrings appear in
        // its description.
        public static readonly string[] ConnectorKeywords =
            { "buttweld", "universal", "weld joint", "weld connector" };
    }
}
