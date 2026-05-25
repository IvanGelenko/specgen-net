using System;
using System.IO;
using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    public class ScanCacheTests : IDisposable
    {
        public ScanCacheTests() => ScanCache.Invalidate();
        public void Dispose() => ScanCache.Invalidate();

        [Fact]
        public void Fingerprint_Changes_When_File_Is_Touched()
        {
            var tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmp, "v1");
                var fp1 = ScanCache.Fingerprint(new[] { tmp }, "active.dwg");
                // Bump the file's contents and mtime.
                System.Threading.Thread.Sleep(20);
                File.WriteAllText(tmp, "v2 longer payload");
                var fp2 = ScanCache.Fingerprint(new[] { tmp }, "active.dwg");
                Assert.NotEqual(fp1, fp2);
            }
            finally
            {
                File.Delete(tmp);
            }
        }

        [Fact]
        public void Put_Then_TryGet_With_Matching_Fingerprint_Returns_Same_Entry()
        {
            var key = "C:\\projects\\foo";
            var fp = "fp-abc";
            var entry = new ScanCache.Entry { Fingerprint = fp, SkippedFiles = 7 };
            ScanCache.Put(key, entry);

            var got = ScanCache.TryGet(key, fp);
            Assert.NotNull(got);
            Assert.Equal(7, got.SkippedFiles);
        }

        [Fact]
        public void TryGet_Returns_Null_For_Different_Project_Key()
        {
            ScanCache.Put("A", new ScanCache.Entry { Fingerprint = "x" });
            Assert.Null(ScanCache.TryGet("B", "x"));
        }

        [Fact]
        public void TryGet_Returns_Null_For_Different_Fingerprint()
        {
            ScanCache.Put("A", new ScanCache.Entry { Fingerprint = "x" });
            Assert.Null(ScanCache.TryGet("A", "y"));
        }

        [Fact]
        public void Invalidate_Wipes_Cache()
        {
            ScanCache.Put("A", new ScanCache.Entry { Fingerprint = "x" });
            ScanCache.Invalidate();
            Assert.Null(ScanCache.TryGet("A", "x"));
        }

        [Fact]
        public void Fingerprint_Stable_When_Nothing_Changes()
        {
            var tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmp, "stable");
                var fp1 = ScanCache.Fingerprint(new[] { tmp }, "active.dwg");
                var fp2 = ScanCache.Fingerprint(new[] { tmp }, "active.dwg");
                Assert.Equal(fp1, fp2);
            }
            finally
            {
                File.Delete(tmp);
            }
        }
    }
}
