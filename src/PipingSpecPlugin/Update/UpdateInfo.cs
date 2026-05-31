namespace PipingSpecPlugin.Update
{
    /// <summary>
    /// Result of an update check: the latest published version, whether it
    /// is newer than what is running, and the download URL of the installer
    /// asset. Pure data — no network or AutoCAD types — so the comparison
    /// logic that produces it can be unit-tested.
    /// </summary>
    public class UpdateInfo
    {
        public bool UpdateAvailable;
        public string CurrentVersion;   // e.g. "1.1.5"
        public string LatestVersion;    // e.g. "1.2.0"
        public string ReleaseTag;       // raw tag, e.g. "v1.2.0"
        public string ReleaseUrl;       // html_url of the release page
        public string InstallerUrl;     // browser_download_url of the Setup.exe asset
        public string ReleaseNotes;     // release body (markdown), may be empty

        /// <summary>A check that found nothing newer (or failed quietly).</summary>
        public static UpdateInfo None(string current) => new UpdateInfo
        {
            UpdateAvailable = false,
            CurrentVersion = current,
            LatestVersion = current,
        };
    }
}
