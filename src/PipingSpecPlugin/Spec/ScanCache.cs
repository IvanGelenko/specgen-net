using System;
using System.Collections.Generic;
using System.IO;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Per-session cache of the WRITESPEC scan output. Keyed by project
    /// folder + a "fingerprint" of every drawing file's last-write time.
    /// Two consecutive WRITESPEC clicks without DWG edits between them
    /// reuse the cached scan and skip the multi-second read of every
    /// DataLinks blob.
    ///
    /// In-memory only: nothing persists to disk. A fresh AutoCAD session
    /// starts cold (which is what we want — settings.json could be
    /// stale across sessions but the live drawing state can't be).
    /// </summary>
    public static class ScanCache
    {
        public sealed class Entry
        {
            public List<PartRecord> Records = new();
            public List<PartRecord> ActiveDwgRecords = new();
            public HashSet<int> EquipmentOwnedPnPIds = new();
            public Dictionary<int, List<int>> Topology = new();
            public int SkippedFiles;
            public string Fingerprint = "";
        }

        // Only one cache slot — the user is virtually always working on a
        // single project. Keeping a dictionary would just leak memory when
        // they switch projects mid-session.
        static string _projectKey;
        static Entry _entry;

        /// <summary>
        /// Build a cheap fingerprint of the current scan inputs. Returns
        /// a string that changes whenever any of the supplied DWGs is
        /// edited or the file list itself changes — that's our cache
        /// invalidator. We hash filename + size + write-time per DWG
        /// rather than reading the bytes; good enough for "did anything
        /// change since last scan".
        /// </summary>
        public static string Fingerprint(IEnumerable<string> dwgPaths, string activeDocPath)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("active=").Append(activeDocPath ?? "").Append('|');
            foreach (var path in dwgPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                try
                {
                    var fi = new FileInfo(path);
                    sb.Append(fi.Exists ? fi.Length.ToString() : "x")
                      .Append(':')
                      .Append(fi.Exists ? fi.LastWriteTimeUtc.Ticks.ToString() : "x")
                      .Append('|')
                      .Append(path.ToLowerInvariant())
                      .Append('\n');
                }
                catch
                {
                    sb.Append("err|").Append(path).Append('\n');
                }
            }
            return sb.ToString();
        }

        public static Entry TryGet(string projectKey, string fingerprint)
        {
            if (_entry == null) return null;
            if (!string.Equals(_projectKey, projectKey, StringComparison.OrdinalIgnoreCase))
                return null;
            if (!string.Equals(_entry.Fingerprint, fingerprint, StringComparison.Ordinal))
                return null;
            return _entry;
        }

        public static void Put(string projectKey, Entry entry)
        {
            _projectKey = projectKey;
            _entry = entry;
        }

        public static void Invalidate()
        {
            _projectKey = null;
            _entry = null;
        }
    }
}
