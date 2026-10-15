using System;
using System.Linq;
using System.Text.Json;

namespace PipingSpecPlugin.Update
{
    /// <summary>
    /// Checks GitHub Releases for a newer plug-in build. The network call
    /// and the parse/compare logic are split: <see cref="Evaluate"/> takes
    /// the raw GitHub API JSON and the running version and is fully unit-
    /// testable; <see cref="CheckAsync"/> wraps it with an HttpClient.
    ///
    /// Failures are deliberately quiet — a plug-in that can't reach GitHub
    /// (offline, proxy, rate-limited) must still load and work. Callers get
    /// <see cref="UpdateInfo.None"/> rather than an exception.
    /// </summary>
    public static class UpdateChecker
    {
        // GitHub "latest release" endpoint. Pre-releases and drafts are
        // excluded by /latest, which is what we want for end users.
        public const string LatestReleaseApi =
            "https://api.github.com/repos/IvanGelenko/specgen-net/releases/latest";

        // Installer asset name produced by build.cmd / installer.iss.
        public const string InstallerAssetName = "PipingSpecPlugin-Setup.exe";

        /// <summary>
        /// Parse a GitHub "release" JSON object and decide whether it is
        /// newer than <paramref name="currentVersion"/>. Never throws —
        /// malformed JSON yields <see cref="UpdateInfo.None"/>.
        /// </summary>
        public static UpdateInfo Evaluate(string releaseJson, string currentVersion)
        {
            var none = UpdateInfo.None(currentVersion);
            if (string.IsNullOrWhiteSpace(releaseJson)) return none;

            try
            {
                using var doc = JsonDocument.Parse(releaseJson);
                var root = doc.RootElement;

                // Drafts shouldn't surface as updates.
                if (root.TryGetProperty("draft", out var draft)
                    && draft.ValueKind == JsonValueKind.True)
                    return none;

                string tag = root.TryGetProperty("tag_name", out var t)
                    ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(tag)) return none;

                var latest = NormalizeVersion(tag);
                var current = NormalizeVersion(currentVersion);
                if (latest == null) return none;

                string installerUrl = null;
                string installerSha256 = null;
                long? installerSize = null;
                if (root.TryGetProperty("assets", out var assets)
                    && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in assets.EnumerateArray())
                    {
                        var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (string.Equals(name, InstallerAssetName, StringComparison.OrdinalIgnoreCase)
                            && a.TryGetProperty("browser_download_url", out var u))
                        {
                            installerUrl = u.GetString();
                            // GitHub computes and reports the asset digest
                            // itself ("sha256:<hex>") — nothing to publish by
                            // hand, so the downloaded installer can be
                            // verified against it.
                            if (a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String)
                                installerSha256 = ParseSha256Digest(d.GetString());
                            if (a.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number
                                && s.TryGetInt64(out var size) && size > 0)
                                installerSize = size;
                            break;
                        }
                    }
                }

                string releaseUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;
                string notes = root.TryGetProperty("body", out var b) ? b.GetString() : "";

                return new UpdateInfo
                {
                    UpdateAvailable = Compare(latest, current) > 0,
                    CurrentVersion = currentVersion,
                    LatestVersion = VersionToString(latest),
                    ReleaseTag = tag,
                    ReleaseUrl = releaseUrl,
                    InstallerUrl = installerUrl,
                    InstallerSha256 = installerSha256,
                    InstallerSize = installerSize,
                    ReleaseNotes = notes ?? "",
                };
            }
            catch
            {
                return none;
            }
        }

        /// <summary>
        /// Strip a leading "v"/whitespace and parse into up to 4 numeric
        /// components. Returns null when there's no parseable number.
        /// Non-numeric suffixes (e.g. "1.2.0-rc1") are truncated at the
        /// first non-numeric component.
        /// </summary>
        public static int[] NormalizeVersion(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim().TrimStart('v', 'V').Trim();
            var parts = s.Split('.');
            var nums = new System.Collections.Generic.List<int>(4);
            foreach (var p in parts)
            {
                // Take the leading integer of each dotted segment; stop at
                // the first segment that isn't numeric at all.
                var digits = new string(p.TakeWhile(char.IsDigit).ToArray());
                if (digits.Length == 0) break;
                if (!int.TryParse(digits, out var v)) break;
                nums.Add(v);
                if (nums.Count == 4) break;
            }
            return nums.Count == 0 ? null : nums.ToArray();
        }

        /// <summary>Compare two normalized version arrays component-wise.</summary>
        public static int Compare(int[] a, int[] b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            int len = Math.Max(a.Length, b.Length);
            for (int i = 0; i < len; i++)
            {
                int av = i < a.Length ? a[i] : 0;
                int bv = i < b.Length ? b[i] : 0;
                if (av != bv) return av.CompareTo(bv);
            }
            return 0;
        }

        static string VersionToString(int[] v) => string.Join(".", v);

        /// <summary>
        /// "sha256:ABCD…" → lower-case 64-char hex, or null for any other
        /// algorithm / malformed value (then the download is simply not
        /// hash-checked — never rejected because of an unknown format).
        /// </summary>
        public static string ParseSha256Digest(string digest)
        {
            if (string.IsNullOrWhiteSpace(digest)) return null;
            var s = digest.Trim();
            const string prefix = "sha256:";
            if (!s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            var hex = s.Substring(prefix.Length).Trim().ToLowerInvariant();
            if (hex.Length != 64 || !hex.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                return null;
            return hex;
        }

        /// <summary>
        /// Checks a downloaded installer against what the release reported.
        /// Missing expectations (null) pass; a mismatch fails.
        /// </summary>
        public static bool VerifyDownload(System.IO.Stream content, string expectedSha256, long? expectedSize)
        {
            if (expectedSize.HasValue && content.CanSeek && content.Length != expectedSize.Value)
                return false;
            if (expectedSha256 == null) return true;
            if (content.CanSeek) content.Position = 0;
            using var sha = System.Security.Cryptography.SHA256.Create();
            var actual = Convert.ToHexString(sha.ComputeHash(content)).ToLowerInvariant();
            return actual == expectedSha256;
        }
    }
}
