using System.Diagnostics;
using System.Text.Json;
using IsoGenerator.Publishing;

static class IsoJobRetentionChecks
{
    public static void Run()
    {
        string temporary = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Combine(temporary, "iso-retention-" + Guid.NewGuid().ToString("N"));
        string jobs = Path.Combine(root, "Jobs");
        string remembered = Path.Combine(root, "Remembered");
        Directory.CreateDirectory(remembered);

        string OldJob()
        {
            string path = Path.Combine(jobs, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(path, "line-01"));
            Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-45));
            return path;
        }

        IDisposable Begin(bool prune = true) => IsoJobRetention.Begin(
            Path.Combine(jobs, Guid.NewGuid().ToString("N")), prune);

        try
        {
            string old = OldJob(), dwgJob = OldJob(), pdfJob = OldJob();
            string custom = Path.Combine(jobs, "manual");
            Directory.CreateDirectory(custom);
            Directory.SetLastWriteTimeUtc(custom, DateTime.UtcNow.AddDays(-45));
            string recent = Path.Combine(jobs, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(recent);
            string manifest = Path.Combine(remembered, "layout.json");
            File.WriteAllText(manifest, JsonSerializer.Serialize(new
            {
                Pages = new[] { new { Drawing = Path.Combine(dwgJob, "line-01", "page.dwg"), Pdf = Path.Combine(pdfJob, "page.pdf") } }
            }));

            using (Begin(prune: false))
            {
                PruneInAnotherProcess(jobs);
                Check(Directory.Exists(old), "Cleanup ran while another generation could still reference an older job.");
            }
            Check(Directory.Exists(old), "Disposing a generation lease triggered cleanup.");
            using (Begin()) { }
            Check(!Directory.Exists(old), "Cleanup did not remove an old, unreferenced job.");
            Check(new[] { dwgJob, pdfJob, recent, custom }.All(Directory.Exists), "Cleanup removed a referenced, recent, or unrecognized directory.");

            string malformed = Path.Combine(remembered, "broken.json");
            old = OldJob();
            File.WriteAllText(malformed, "{missing-json");
            using (Begin()) { }
            Check(Directory.Exists(old), "Cleanup ran without being able to read all remembered manifests.");
            File.WriteAllText(malformed, """{"Pages":[{"Drawing":"relative.dwg","Pdf":"relative.pdf"}]}""");
            using (Begin()) { }
            Check(Directory.Exists(old), "Cleanup ran with invalid remembered paths.");
            File.Delete(malformed);

            var others = Enumerable.Range(0, 9).Select(_ => OldJob()).Append(old).ToArray();
            using (Begin()) { }
            Check(others.Count(Directory.Exists) == 2, "Cleanup exceeded the batch limit or skipped eligible jobs.");
            Check(new[] { dwgJob, pdfJob, recent, custom }.All(Directory.Exists), "Cleanup removed a protected directory in a larger batch.");
            Console.WriteLine("Isometric job retention checks passed.");
        }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(root)) == Path.TrimEndingDirectorySeparator(temporary))
                Directory.Delete(root, recursive: true);
        }
    }

    static void PruneInAnotherProcess(string jobs)
    {
        string executable = Environment.ProcessPath!;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(IsoJobRetentionChecks).Assembly.Location);
        start.ArgumentList.Add("--retention-probe");
        start.ArgumentList.Add(jobs);
        using var child = Process.Start(start)!;
        if (!child.WaitForExit(15_000))
        {
            child.Kill();
            child.WaitForExit();
            throw new TimeoutException("Concurrent cleanup did not complete.");
        }
        Check(child.ExitCode == 0, "Concurrent generation could not create its job.");
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
