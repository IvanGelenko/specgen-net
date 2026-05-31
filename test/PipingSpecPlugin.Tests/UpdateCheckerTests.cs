using PipingSpecPlugin.Update;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    public class UpdateCheckerTests
    {
        // --- NormalizeVersion ------------------------------------------

        [Theory]
        [InlineData("1.2.3", new[] { 1, 2, 3 })]
        [InlineData("v1.2.3", new[] { 1, 2, 3 })]
        [InlineData("V1.2.3", new[] { 1, 2, 3 })]
        [InlineData(" 1.2 ", new[] { 1, 2 })]
        [InlineData("1.2.0-rc1", new[] { 1, 2, 0 })]   // suffix truncated
        [InlineData("1.2.3.4.5", new[] { 1, 2, 3, 4 })] // capped at 4
        public void NormalizeVersion_Parses(string raw, int[] expected)
        {
            Assert.Equal(expected, UpdateChecker.NormalizeVersion(raw));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("vX")]
        [InlineData("not-a-version")]
        public void NormalizeVersion_ReturnsNull_OnGarbage(string raw)
        {
            Assert.Null(UpdateChecker.NormalizeVersion(raw));
        }

        // --- Compare ----------------------------------------------------

        [Theory]
        [InlineData("1.2.0", "1.1.5", 1)]    // newer minor
        [InlineData("1.1.6", "1.1.5", 1)]    // newer patch
        [InlineData("2.0.0", "1.9.9", 1)]    // newer major
        [InlineData("1.1.5", "1.1.5", 0)]    // equal
        [InlineData("1.1.5", "1.1.5.0", 0)]  // trailing zero ignored
        [InlineData("1.1.4", "1.1.5", -1)]   // older
        public void Compare_Orders_Versions(string a, string b, int sign)
        {
            var va = UpdateChecker.NormalizeVersion(a);
            var vb = UpdateChecker.NormalizeVersion(b);
            Assert.Equal(sign, System.Math.Sign(UpdateChecker.Compare(va, vb)));
        }

        // --- Evaluate (GitHub JSON → UpdateInfo) -----------------------

        [Fact]
        public void Evaluate_DetectsNewer_AndPicksInstallerAsset()
        {
            var json = @"{
                ""tag_name"": ""v1.2.0"",
                ""draft"": false,
                ""html_url"": ""https://github.com/IvanGelenko/specgen-net/releases/tag/v1.2.0"",
                ""body"": ""Bug fixes"",
                ""assets"": [
                    { ""name"": ""PipingSpecPlugin-Setup.exe"",
                      ""browser_download_url"": ""https://example.com/Setup.exe"" },
                    { ""name"": ""something-else.zip"",
                      ""browser_download_url"": ""https://example.com/other.zip"" }
                ]
            }";

            var info = UpdateChecker.Evaluate(json, "1.1.5");

            Assert.True(info.UpdateAvailable);
            Assert.Equal("1.2.0", info.LatestVersion);
            Assert.Equal("v1.2.0", info.ReleaseTag);
            Assert.Equal("https://example.com/Setup.exe", info.InstallerUrl);
            Assert.Equal("Bug fixes", info.ReleaseNotes);
        }

        [Fact]
        public void Evaluate_SameVersion_NoUpdate()
        {
            var json = @"{ ""tag_name"": ""v1.1.5"", ""assets"": [] }";
            var info = UpdateChecker.Evaluate(json, "1.1.5");
            Assert.False(info.UpdateAvailable);
        }

        [Fact]
        public void Evaluate_OlderRelease_NoUpdate()
        {
            var json = @"{ ""tag_name"": ""v1.1.0"", ""assets"": [] }";
            var info = UpdateChecker.Evaluate(json, "1.1.5");
            Assert.False(info.UpdateAvailable);
        }

        [Fact]
        public void Evaluate_Draft_NeverOffered()
        {
            var json = @"{ ""tag_name"": ""v2.0.0"", ""draft"": true, ""assets"": [] }";
            var info = UpdateChecker.Evaluate(json, "1.1.5");
            Assert.False(info.UpdateAvailable);
        }

        [Fact]
        public void Evaluate_NewerButNoInstaller_StillFlagsUpdate_WithNullUrl()
        {
            // Release exists and is newer, but has no Setup.exe asset —
            // UI should still notify but fall back to the release page.
            var json = @"{ ""tag_name"": ""v1.3.0"", ""assets"": [] }";
            var info = UpdateChecker.Evaluate(json, "1.1.5");
            Assert.True(info.UpdateAvailable);
            Assert.Null(info.InstallerUrl);
        }

        [Theory]
        [InlineData("")]
        [InlineData("{ not valid json")]
        [InlineData("{ }")]              // no tag_name
        public void Evaluate_Garbage_ReturnsNone(string json)
        {
            var info = UpdateChecker.Evaluate(json, "1.1.5");
            Assert.False(info.UpdateAvailable);
            Assert.Equal("1.1.5", info.CurrentVersion);
        }
    }
}
