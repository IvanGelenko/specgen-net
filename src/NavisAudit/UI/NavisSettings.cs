using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace NavisAudit.UI
{
    // The window's last choices: %APPDATA%\NavisAudit\settings.ini, key=value.
    public sealed class NavisSettings
    {
        public double CollisionToleranceMm { get; set; }
        // Off by default: a combined model holds whole structure and civil
        // models, and their contacts with each other are not a piping audit.
        public bool StructureCollisions { get; set; }
        public HashSet<string> DisabledChecks { get; set; } = new HashSet<string>(StringComparer.Ordinal);

        public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NavisAudit", "settings.ini");

        public static NavisSettings Load(string path = null)
        {
            var settings = new NavisSettings();
            try
            {
                path = path ?? FilePath;
                if (!File.Exists(path)) return settings;
                foreach (var line in File.ReadAllLines(path))
                {
                    var at = line.IndexOf('=');
                    if (at <= 0) continue;
                    var key = line.Substring(0, at).Trim();
                    var value = line.Substring(at + 1).Trim();
                    if (key == "CollisionToleranceMm" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mm) && mm >= 0)
                        settings.CollisionToleranceMm = mm;
                    else if (key == "StructureCollisions") settings.StructureCollisions = value == "1";
                    else if (key == "DisabledChecks")
                        settings.DisabledChecks = new HashSet<string>(value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()), StringComparer.Ordinal);
                }
            }
            catch { }
            return settings;
        }

        public void Save(string path = null)
        {
            try
            {
                path = path ?? FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, new[]
                {
                    "CollisionToleranceMm=" + CollisionToleranceMm.ToString("R", CultureInfo.InvariantCulture),
                    "StructureCollisions=" + (StructureCollisions ? "1" : "0"),
                    "DisabledChecks=" + string.Join(",", DisabledChecks.OrderBy(s => s, StringComparer.Ordinal)),
                });
            }
            catch { }
        }
    }
}
