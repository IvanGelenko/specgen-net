using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace PipingSpecPlugin.Update
{
    /// <summary>
    /// Network side of the updater: fetch the latest release from GitHub,
    /// download the installer, and hand control to it. All failures are
    /// swallowed and surfaced as "no update" / false so a flaky connection
    /// can never break plug-in load or a spec run.
    /// </summary>
    public static class UpdateService
    {
        // GitHub requires a User-Agent on every API request or it returns 403.
        const string UserAgent = "PipingSpecPlugin-Updater";

        // Short timeout for the small API call made at plug-in load…
        static readonly HttpClient Http = CreateClient(TimeSpan.FromSeconds(15), apiAccept: true);
        // …and a separate, long one for the installer download. HttpClient.
        // Timeout covers the WHOLE request including the body, so the shared
        // 15 s client aborted every installer download on a slow or proxied
        // connection and the update silently "failed".
        static readonly HttpClient Download = CreateClient(TimeSpan.FromMinutes(10), apiAccept: false);

        static HttpClient CreateClient(TimeSpan timeout, bool apiAccept)
        {
            var c = new HttpClient { Timeout = timeout };
            c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            if (apiAccept) c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        /// <summary>The running plug-in version, e.g. "1.1.5".</summary>
        public static string CurrentVersion()
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                // AssemblyVersion is stamped X.Y.Z.0 by build.cmd; show X.Y.Z.
                return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
            }
            catch { return "0.0.0"; }
        }

        /// <summary>
        /// Query GitHub for the latest release and compare against the
        /// running version. Returns <see cref="UpdateInfo.None"/> on any
        /// failure (offline, rate-limited, malformed response).
        /// </summary>
        public static async Task<UpdateInfo> CheckAsync()
        {
            var current = CurrentVersion();
            try
            {
                using var resp = await Http.GetAsync(UpdateChecker.LatestReleaseApi)
                                           .ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return UpdateInfo.None(current);
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return UpdateChecker.Evaluate(json, current);
            }
            catch
            {
                return UpdateInfo.None(current);
            }
        }

        /// <summary>
        /// Download the installer to a temp file and start it, then return
        /// true so the caller can advise the user to close AutoCAD (the
        /// installer can't overwrite the loaded DLL until AutoCAD exits).
        /// Returns false if there's no installer URL or the download fails.
        /// </summary>
        public static async Task<bool> DownloadAndLaunchInstallerAsync(UpdateInfo info)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.InstallerUrl))
                return false;
            var tempPath = Path.Combine(
                Path.GetTempPath(),
                $"PipingSpecPlugin-Setup-{info.LatestVersion}.exe");
            var partPath = tempPath + ".part";
            try
            {
                // Stream straight to disk (ResponseHeadersRead) instead of
                // buffering the whole installer in memory first, into a
                // ".part" file that only becomes the .exe once complete and
                // verified — a truncated download is never launched.
                using (var resp = await Download.GetAsync(info.InstallerUrl,
                           HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode) return false;
                    using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    using var dst = File.Create(partPath);
                    await src.CopyToAsync(dst).ConfigureAwait(false);
                }

                // Size and SHA-256 as reported by GitHub for the asset (when
                // the API provides them). There is no code-signing
                // certificate, so an Authenticode check is not possible; the
                // digest at least proves the file is the published asset.
                bool ok;
                using (var check = File.OpenRead(partPath))
                    ok = UpdateChecker.VerifyDownload(check, info.InstallerSha256, info.InstallerSize);
                if (!ok)
                {
                    TryDelete(partPath);
                    return false;
                }
                File.Move(partPath, tempPath, overwrite: true);

                // Launch the installer detached. It waits in the background;
                // the user finishes it after closing AutoCAD.
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true,
                };
                System.Diagnostics.Process.Start(psi);
                return true;
            }
            catch
            {
                TryDelete(partPath);
                return false;
            }
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
