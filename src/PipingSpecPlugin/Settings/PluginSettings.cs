using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        public const int CurrentSchemaVersion = 8;
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
        // listing only one Pipe entry per run. When false, every PipeBend
        // goes to «Детали трубопроводов» in pieces, regardless of its
        // catalogue PartCategory (CategoryClassifier.SpecSection).
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

        // Last "Ведомость трубопроводов" (ВТ) workbook the user picked for
        // СИНХРОНИЗАЦИЯ ВТ, to pre-populate the open-file dialog.
        public string LastVtFilePath { get; set; } = "";

        // Reserved: text -> code mapping for a P3dLineGroup picklist
        // column whose stored values don't match ВТ's free text. Not
        // currently consulted by LineSyncService — every picklist field
        // it writes today (Material_pipe, Код_трубопровода, Ysl_pressure,
        // flanges, InsulationType, Heat_type) is decoded straight from
        // the "Обозначение" segment code via LineDesignationParser, whose
        // letter codes already match Plant 3D's picklist codes. Kept for
        // a future ВТ column that needs a genuine lossy text->code guess
        // (schema field, not dead: removing it would break settings.json
        // for anyone who already saved one on this schema version).
        public System.Collections.Generic.Dictionary<string, string> VtPicklistTextToCode { get; set; }
            = new System.Collections.Generic.Dictionary<string, string>();

        // ---- INSULSPEC (данные для расчёта изоляции) -------------------------
        // Scope and the line / status memory, same known/selected scheme as
        // the spec picker (PickerSelectionMemory).
        // PSAUDIT: overlaps no deeper than this (mm) are not clashes; last
        // value entered in the audit window, also used by PSAUDITJSON.
        public double AuditCollisionToleranceMm { get; set; } = 0;

        public string InsulationScope { get; set; } = "proj";
        public string[] InsulationLines { get; set; } = null;
        public string[] KnownInsulationLines { get; set; } = null;
        public string[] InsulationStatuses { get; set; } = null;
        public string[] KnownInsulationStatuses { get; set; } = null;

        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PipingSpecPlugin", "settings.json");

        /// <summary>
        /// True when settings.json exists but could not be read or parsed
        /// (corrupt JSON, file locked by antivirus / another AutoCAD, …) and
        /// this instance therefore holds DEFAULTS, not the user's settings.
        /// A missing file is not a failure. Callers that would merely persist
        /// a side value (SYNCVT's last ВТ path) must not Save() such an
        /// instance — that silently replaced every saved choice with
        /// defaults. Not serialised.
        /// </summary>
        [JsonIgnore] public bool LoadFailed { get; private set; }
        [JsonIgnore] public string LoadError { get; private set; }

        public static PluginSettings Load() => Load(FilePath);

        internal static PluginSettings Load(string path)
        {
            try
            {
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
            catch (Exception ex)
            {
                // Corrupted settings file should never block the plug-in;
                // a fresh default is a better user experience than a hard
                // failure on every command invocation. But flag it, so the
                // defaults are not written back over the user's file.
                return new PluginSettings { LoadFailed = true, LoadError = ex.Message };
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
                    goto case 4;
                case 4:
                    // v4 → v5: introduced ВТ sync (LastVtFilePath,
                    // VtPicklistTextToCode). Empty defaults are fine —
                    // there's no historical value to backfill.
                    LastVtFilePath = "";
                    VtPicklistTextToCode = new System.Collections.Generic.Dictionary<string, string>();
                    goto case 5;
                case 5:
                case 6:
                case 7:
                    // v5 → v8: INSULSPEC scope and line / status memory; null
                    // defaults. (v6–v8 builds also stored a materials table,
                    // template path, breakdown mode or insulation types for
                    // earlier variants — dropped keys are ignored on load.)
                    break;
            }
            SchemaVersion = CurrentSchemaVersion;
        }

        public void Save() => Save(FilePath);

        /// <summary>
        /// Atomic write: serialise to a sibling temp file, then replace the
        /// real file in one rename (MoveFileEx REPLACE_EXISTING on the same
        /// volume). File.WriteAllText truncated settings.json first, so a
        /// crash / full disk mid-write left an empty or half-written file
        /// that the next Load() silently replaced with defaults.
        ///
        /// If this instance came from a failed Load() (LoadFailed), the
        /// unreadable original is first copied aside to
        /// settings.json.unreadable — WRITESPEC still saves the user's fresh
        /// picks over it, but the old content is not lost.
        /// </summary>
        internal void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var json = JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true });

            if (LoadFailed && File.Exists(path))
            {
                try { File.Copy(path, path + ".unreadable", overwrite: true); }
                catch { /* best effort — never block the save itself */ }
            }

            // Unique temp name: two AutoCAD sessions saving at the same moment
            // shared "settings.json.tmp" and could move each other's half-
            // written file into place. A failed write/move must not leave
            // the temp behind either.
            var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
            LoadFailed = false;
            LoadError = null;
        }
    }
}
