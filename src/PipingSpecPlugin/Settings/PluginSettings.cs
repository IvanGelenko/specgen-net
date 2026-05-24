using System;
using System.IO;
using System.Text.Json;

namespace PipingSpecPlugin.Settings
{
    /// <summary>
    /// User settings persisted to <c>%APPDATA%\PipingSpecPlugin\settings.json</c>.
    /// This is intentionally the SAME folder the Python edition uses so a
    /// user can flip between the .NET and Python plug-ins without losing
    /// their template choice. Don't rename the folder casually.
    /// Schema is forward-compatible: unknown fields are ignored on load,
    /// missing fields fall back to defaults.
    /// </summary>
    public class PluginSettings
    {
        // Path to the user's preferred .docx Word template. Empty means
        // "use built-in default".
        public string TemplatePath { get; set; } = "";

        // Last directory the user picked an output file from. Used to pre-
        // populate the save-as dialog so they don't navigate from scratch.
        public string LastOutputDir { get; set; } = "";

        // When true, group parts by line number in the generated spec.
        public bool GroupByLine { get; set; } = true;

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
                return JsonSerializer.Deserialize<PluginSettings>(json) ?? new PluginSettings();
            }
            catch
            {
                // Corrupted settings file should never block the plug-in;
                // a fresh default is a better user experience than a hard
                // failure on every command invocation.
                return new PluginSettings();
            }
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
