using System.Collections.Generic;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Sentinels and Russian labels copied verbatim from the Python
    /// edition's generate_spec.py. Keep the sentinel strings identical
    /// — the .docx template's section header text matches exactly and
    /// the Python edition's settings file uses these same constants.
    /// </summary>
    public static class SpecConstants
    {
        public const string MaterialsSectionHeader = "Изделия и материалы";

        public const string NullCategory = "__NULL__";
        public const string OtherCategoryLabel = "Прочее";

        public const string NullLine = "__NO_LINE__";
        public const string NullLineLabel = "Без номера линии";

        // Statuses considered "active" — anything else gets skipped by
        // default. The Russian variants exist because in localized
        // installs Plant 3D writes 'Новое' / 'Новый' instead of 'New'.
        public static readonly string[] DefaultActiveStatuses =
            { "New", "Новое", "Новый" };

        public static readonly string[] CategoryOrder =
        {
            "Equipment",
            "Pipe",
            "Fittings",
            "Flanges",
            "Valves",
            "Instruments",
            "Instrument",
            "PipeSpecialties",
            "PipeSpecialty",
            "Specialty",
            "Supports",
            "Support",
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
            };

        // Connector keywords (case-insensitive substring match). A row
        // is treated as a connector and excluded if its category is
        // null/empty/Fasteners AND any of these substrings appear in
        // its description.
        public static readonly string[] ConnectorKeywords =
            { "buttweld", "universal", "weld joint", "weld connector" };
    }
}
