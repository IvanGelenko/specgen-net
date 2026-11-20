using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PipingSpecPlugin.Tests;

public class IsoWordWorkerTests
{
    static ProcessStartInfo GetWorkerStart(string args, string workDir = "")
    {
        string repoRoot = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(repoRoot) && !File.Exists(Path.Combine(repoRoot, "VERSION")))
        {
            string? parent = Path.GetDirectoryName(repoRoot);
            if (parent == repoRoot) break;
            repoRoot = parent!;
        }

        string[] exeCandidates = {
            Path.Combine(repoRoot, "iso", "src", "IsoGenerator.WordWorker", "bin", "Debug", "net8.0-windows", "IsoWordWorker.exe"),
            Path.Combine(repoRoot, "iso", "src", "IsoGenerator.WordWorker", "bin", "Release", "net8.0-windows", "IsoWordWorker.exe"),
            Path.Combine(repoRoot, "src", "PipingSpecPlugin", "bin", "Release", "IsoWordWorker.exe"),
            Path.Combine(repoRoot, "bundle_staged", "Contents", "Windows", "IsoWordWorker.exe"),
        };
        string[] dllCandidates = {
            Path.Combine(repoRoot, "iso", "src", "IsoGenerator.WordWorker", "bin", "Debug", "net8.0-windows", "IsoWordWorker.dll"),
            Path.Combine(repoRoot, "iso", "src", "IsoGenerator.WordWorker", "bin", "Release", "net8.0-windows", "IsoWordWorker.dll"),
            Path.Combine(repoRoot, "src", "PipingSpecPlugin", "bin", "Release", "IsoWordWorker.dll"),
            Path.Combine(repoRoot, "bundle_staged", "Contents", "Windows", "IsoWordWorker.dll"),
        };

        foreach (var exe in exeCandidates)
        {
            if (File.Exists(exe))
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                if (!string.IsNullOrEmpty(workDir)) psi.WorkingDirectory = workDir;
                return psi;
            }
        }

        foreach (var dll in dllCandidates)
        {
            if (File.Exists(dll))
            {
                var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" {args}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                if (!string.IsNullOrEmpty(workDir)) psi.WorkingDirectory = workDir;
                return psi;
            }
        }

        throw new FileNotFoundException("Не найден исполняемый файл IsoWordWorker.exe или IsoWordWorker.dll.");
    }

    [Fact]
    public void WorkerDisplaysUsageWhenNoArgs()
    {
        var psi = GetWorkerStart("");
        using var proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        Assert.Equal(0, proc.ExitCode);
        Assert.Contains("Использование:", stdout);
        Assert.Contains("front-matter", stdout);
        Assert.Contains("restore-signatures", stdout);
    }

    [Fact]
    public void WorkerReturnsErrorOnInvalidArgs()
    {
        var psi = GetWorkerStart("only-one-argument");
        using var proc = Process.Start(psi)!;
        proc.WaitForExit();

        Assert.Equal(2, proc.ExitCode);
    }

    [Fact]
    public void RestoreSignaturesFailsGracefullyOnMissingFiles()
    {
        string fakeDocx = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid() + ".docx");
        string fakePdf = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid() + ".pdf");

        var psi = GetWorkerStart($"restore-signatures \"{fakeDocx}\" \"{fakePdf}\"");
        using var proc = Process.Start(psi)!;
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        Assert.Equal(1, proc.ExitCode);
        Assert.Contains("не найден", stderr);
    }

    [Fact]
    public void JournalRecordsStepsWithTimestampsOnExecution()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "iso_word_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string reqPath = Path.Combine(tempDir, "request.json");
            string resPath = Path.Combine(tempDir, "result.json");
            string logPath = Path.Combine(tempDir, "report.log");

            // Write an invalid request (missing template file)
            var requestData = new
            {
                Details = new
                {
                    TitleTemplate = "nonexistent-template.docx",
                    ContentsTemplate = "nonexistent-contents.docx",
                    Title = new { ObjectName = "Тестовый объект", UnitName = "Установка 1", DocumentNumber = "123-TX" }
                },
                Number = "123-TX",
                Titles = new[] { "Лист 1" }
            };
            File.WriteAllText(reqPath, JsonSerializer.Serialize(requestData));

            var psi = GetWorkerStart($"front-matter \"{reqPath}\" \"{resPath}\"", tempDir);
            using var proc = Process.Start(psi)!;
            proc.WaitForExit();

            // Should exit with code 1 because templates do not exist or Word isn't automated in test runner
            Assert.True(File.Exists(logPath), "Файл журнала report.log должен быть создан");

            string logContent = File.ReadAllText(logPath);
            Assert.Contains("[СТАРТ] Инициализация задания", logContent);
            Assert.Contains("[ЗАВЕРШЕНО] Инициализация задания", logContent);
            // Verify timestamp format: [yyyy-MM-dd HH:mm:ss.
            Assert.Matches(@"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\]", logContent);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void SignatureRestorationCrashIsIsolatedAndPreservesPdf()
    {
        // Test that when restore-signatures subprocess fails, the original Word PDF is preserved
        string tempDir = Path.Combine(Path.GetTempPath(), "iso_restore_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string pdfPath = Path.Combine(tempDir, "sample.pdf");
            string docxPath = Path.Combine(tempDir, "sample.docx");
            string originalContent = "%PDF-1.4 test dummy word pdf";
            File.WriteAllText(pdfPath, originalContent);
            File.WriteAllText(docxPath, "corrupted-or-empty-docx");

            // Calling restore-signatures directly on corrupted files fails with exit code 1
            var psi = GetWorkerStart($"restore-signatures \"{docxPath}\" \"{pdfPath}\"", tempDir);
            using var proc = Process.Start(psi)!;
            proc.WaitForExit();

            Assert.Equal(1, proc.ExitCode);

            // Verify original PDF was preserved unchanged!
            Assert.True(File.Exists(pdfPath));
            Assert.Equal(originalContent, File.ReadAllText(pdfPath));

            // Verify no leftover .images file
            Assert.False(File.Exists(pdfPath + ".images"));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
