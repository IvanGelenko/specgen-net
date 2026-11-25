using System.Text.Json;

namespace IsoGenerator.Publishing;

public static class IsoJobRetention
{
    private static readonly TimeSpan RetentionAge = TimeSpan.FromDays(30);
    private const int BatchSize = 8;

    // A generation can reuse pages from older jobs. Keep the shared lease through the
    // remembered manifest commit so cleanup can inspect a stable set of references.
    public static IDisposable Begin(string job, bool prune = false)
    {
        job = Path.TrimEndingDirectorySeparator(Path.GetFullPath(job));
        string jobs = Path.GetDirectoryName(job)!;
        if (!Guid.TryParseExact(Path.GetFileName(job), "N", out _) ||
            !string.Equals(Path.GetFileName(jobs), "Jobs", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Expected a GUID job directory directly under Jobs.", nameof(job));

        Directory.CreateDirectory(jobs);
        string gate = Path.Combine(jobs, ".retention.lock");
        if (prune)
        {
            try
            {
                using var exclusive = new FileStream(gate, FileMode.OpenOrCreate, FileAccess.Read, FileShare.None);
                Prune(jobs, job);
            }
            catch (Exception ex) when (IsFileFailure(ex) || ex is JsonException or InvalidDataException)
            {
                // A concurrent generation or an unreadable reference is a reason to defer cleanup.
            }
        }

        var lease = OpenSharedLease(gate);
        try
        {
            Directory.CreateDirectory(job);
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private static FileStream OpenSharedLease(string gate)
    {
        long started = Environment.TickCount64;
        while (true)
        {
            try { return new FileStream(gate, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read); }
            catch (IOException ex) when (OperatingSystem.IsWindows() && (ex.HResult & 0xffff) is 32 or 33 &&
                Environment.TickCount64 - started < 5_000)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static void Prune(string jobs, string currentJob)
    {
        var jobsDirectory = new DirectoryInfo(jobs);
        for (DirectoryInfo? parent = jobsDirectory; parent is not null; parent = parent.Parent)
            if (IsReparsePoint(parent.FullName)) return;

        var cutoff = DateTime.UtcNow - RetentionAge;
        var candidates = jobsDirectory.EnumerateDirectories()
            .Where(directory => Guid.TryParseExact(directory.Name, "N", out _) &&
                !string.Equals(directory.FullName, currentJob, StringComparison.OrdinalIgnoreCase) &&
                directory.LastWriteTimeUtc < cutoff &&
                (directory.Attributes & FileAttributes.ReparsePoint) == 0)
            .OrderBy(directory => directory.LastWriteTimeUtc)
            .ToArray();
        if (candidates.Length == 0) return;

        var pinned = ReadRememberedJobs(jobs);
        foreach (var directory in candidates.Where(directory => !pinned.Contains(directory.Name)).Take(BatchSize))
        {
            try
            {
                string target = Path.GetFullPath(directory.FullName);
                if (!string.Equals(Path.GetDirectoryName(target), jobs, StringComparison.OrdinalIgnoreCase) ||
                    !Guid.TryParseExact(Path.GetFileName(target), "N", out _) || HasReparsePoint(target)) continue;
                Directory.Delete(target, recursive: true);
            }
            catch (Exception ex) when (IsFileFailure(ex))
            {
                // A locked diagnostic file can be retried on a future generation.
            }
        }
    }

    private static HashSet<string> ReadRememberedJobs(string jobs)
    {
        string remembered = Path.Combine(Path.GetDirectoryName(jobs)!, "Remembered");
        var pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        FileAttributes attributes;
        try { attributes = File.GetAttributes(remembered); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return pinned; }
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
            throw new InvalidDataException("Expected a normal Remembered directory.");

        foreach (string manifest in Directory.EnumerateFiles(remembered, "*.json"))
        {
            if (IsReparsePoint(manifest)) throw new InvalidDataException("Remembered manifest is a reparse point.");
            using var input = File.OpenRead(manifest);
            using var document = JsonDocument.Parse(input);
            var entry = document.RootElement;
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("Pages", out var pages) ||
                pages.ValueKind != JsonValueKind.Array || pages.GetArrayLength() == 0)
                throw new InvalidDataException("Remembered manifest has no pages.");

            foreach (var page in pages.EnumerateArray())
            {
                pinned.Add(ReadJobReference(page, "Drawing", jobs));
                pinned.Add(ReadJobReference(page, "Pdf", jobs));
            }
        }
        return pinned;
    }

    private static string ReadJobReference(JsonElement page, string property, string jobs)
    {
        if (page.ValueKind != JsonValueKind.Object || !page.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String || value.GetString() is not { } path || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Remembered page has no absolute job path.");

        string fullPath = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(jobs, fullPath);
        int separator = relative.IndexOf(Path.DirectorySeparatorChar);
        if (separator <= 0 || Path.EndsInDirectorySeparator(fullPath) || !Guid.TryParseExact(relative[..separator], "N", out _))
            throw new InvalidDataException("Remembered page path is outside a job directory.");

        // A redirected path can refer to a different job from the one in its name.
        for (string? component = fullPath; component is not null &&
            !string.Equals(component, jobs, StringComparison.OrdinalIgnoreCase); component = Path.GetDirectoryName(component))
        {
            try
            {
                if (IsReparsePoint(component)) throw new InvalidDataException("Remembered page path passes through a reparse point.");
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // Keep the named job pinned even if the referenced file has gone missing.
            }
        }
        return relative[..separator];
    }

    private static bool HasReparsePoint(string path)
    {
        if (IsReparsePoint(path)) return true;
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.TryPop(out string? directory))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return true;
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
        return false;
    }

    private static bool IsReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsFileFailure(Exception exception) => exception is IOException or UnauthorizedAccessException or
        ArgumentException or NotSupportedException or System.Security.SecurityException;
}
