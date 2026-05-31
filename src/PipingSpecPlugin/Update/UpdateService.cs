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

        static readonly HttpClient Http = CreateClient();

        static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
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
            try
            {
                var tempPath = Path.Combine(
                    Path.GetTempPath(),
                    $"PipingSpecPlugin-Setup-{info.LatestVersion}.exe");

                using (var resp = await Http.GetAsync(info.InstallerUrl).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode) return false;
                    using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    using var dst = File.Create(tempPath);
                    await src.CopyToAsync(dst).ConfigureAwait(false);
                }

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
                return false;
            }
        }
    }
}
