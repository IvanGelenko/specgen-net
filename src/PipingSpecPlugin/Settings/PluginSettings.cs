using System;
using System.IO;
using System.Text.Json;

namespace PipingSpecPlugin.Settings
{
    /// <summary>
    /// User settings persisted to <c>%APPDATA%\PipingSpecPlugin\settings.json</c>.
    /// Don't rename the folder casually — existing users would lose their
    /// saved template choice and output directory.
    /// Schema is forward-compatible: unknown fields are ignored on load,
    /// missing fields fall back to defaults.
    /// </summary>
    public class PluginSettings
    {
        // Bumped whenever the JSON schema gains or removes a field in a
        // way that needs a migration. Load() reads this first and runs
        // Upgrade() before deserialising the rest of the object — same
        // pattern as a database migrations table. Bumping the constant
        // and adding an Upgrade case is the only way to change the
        // file shape safely; never silently rename a property.
        public const int CurrentSchemaVersion = 4;
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        // Path to the user's preferred .docx Word template. Empty means
        // "use built-in default".
        public string TemplatePath { get; set; } = "";

        // Last directory the user picked an output file from. Used to pre-
        // populate the save-as dialog so they don't navigate from scratch.
        public string LastOutputDir { get; set; } = "";

        // When true, group parts by line number in the generated spec.
        public bool GroupByLine { get; set; } = true;

        // Filter parts by Status. null = include all statuses (default);
        // a non-null array (even empty) limits the output to listed values.
        // Stored as nullable so settings.json can omit the key for the
        // "all statuses" case rather than serialise an empty array.
        public string[] Statuses { get; set; } = null;
        public string[] Categories { get; set; } = null;
        public string[] Lines { get; set; } = null;

        public string[] KnownStatuses { get; set; } = null;
        public string[] KnownCategories { get; set; } = null;
        public string[] KnownLines { get; set; } = null;

        // "proj" or "dwg" — restored on next picker open.
        public string Scope { get; set; } = "proj";

        public bool ExcludeValveCompanions { get; set; } = true;
        public bool ExcludeEquipment { get; set; } = true;
        public bool ExcludeConnectors { get; set; } = true;
        // Fold PipeBend rows into the adjacent straight pipe — defaults
        // to true so the spec matches the procurement convention of
        // listing only one Pipe entry per run.
        public bool IncludePipeBendsAsPipe { get; set; } = true;

        // Open the resulting .docx in Word automatically. Default on.
        public bool OpenAfterGenerate { get; set; } = true;

        // When true, output each element on a separate row in the spec rather
        // than aggregate identical items.
        public bool DisableElementGrouping { get; set; } = false;

        // Check GitHub Releases for a newer plug-in build on load and show a
        // notification if one is found. Default on; users on locked-down /
        // offline machines can turn it off to avoid the (silent) network call.
        public bool CheckForUpdates { get; set; } = true;

        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PipingSpecPlugin", "settings.json");

        public static PluginSettings Load()
        {
            try
            {
                var path = FilePath;
                if (!File.Exists(path)) return new PluginSettings();
                var json = File.ReadAllText(path);
                // Read SchemaVersion first so we can patch the JSON
                // before binding it to the strongly-typed class. Cheap
                // peek via JsonDocument keeps the path single-pass.
                int oldVersion = 0;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object
                        && doc.RootElement.TryGetProperty("SchemaVersion", out var v)
                        && v.ValueKind == JsonValueKind.Number)
                        oldVersion = v.GetInt32();
                }
                catch { /* leave oldVersion = 0 */ }

                var loaded = JsonSerializer.Deserialize<PluginSettings>(json)
                             ?? new PluginSettings();
                if (oldVersion < CurrentSchemaVersion)
                    loaded.Upgrade(oldVersion);
                return loaded;
            }
            catch
            {
                // Corrupted settings file should never block the plug-in;
                // a fresh default is a better user experience than a hard
                // failure on every command invocation.
                return new PluginSettings();
            }
        }

        /// <summary>
        /// Forward-only migration of the in-memory object. Each <c>case</c>
        /// patches v(N) into v(N+1) — fall-through is intentional, so a
        /// jump from v0 to v2 runs both migrations in order. Settings
        /// written back to disk on next Save() carry CurrentSchemaVersion.
        /// </summary>
        void Upgrade(int from)
        {
            switch (from)
            {
                case 0:
                    // v0 was the unversioned default; fields that didn't
                    // exist (Statuses, Categories, Lines, ExcludeXxx,
                    // IncludePipeBendsAsPipe) get their constructor
                    // defaults — no field-level work needed.
                    goto case 1;
                case 1:
                    // v1 → v2: introduced OpenAfterGenerate. Existing
                    // users expect the historical behaviour (Word always
                    // opened) so keep the default true.
                    OpenAfterGenerate = true;
                    goto case 2;
                case 2:
                    // v2 → v3: introduced DisableElementGrouping.
                    DisableElementGrouping = false;
                    goto case 3;
                case 3:
                    // v3 → v4: introduced CheckForUpdates. Default on so
                    // existing users start getting update notifications.
                    CheckForUpdates = true;
                    break;
            }
            SchemaVersion = CurrentSchemaVersion;
        }

        public void Save()
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var json = JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
    }
}
