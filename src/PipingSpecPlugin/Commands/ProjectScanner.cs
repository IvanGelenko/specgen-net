using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PlantInstance;
using Autodesk.ProcessPower.ProjectManager;
using PipingSpecPlugin.Spec;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace PipingSpecPlugin.Commands
{
    /// <summary>One project / drawing scan: every record plus what the scan learned.</summary>
    internal sealed class ScanResult
    {
        public List<PartRecord> records = new();
        public List<PartRecord> activeDwgRecords = new();
        public HashSet<int> ownedPnPIds = new();
        public Dictionary<int, List<int>> topology = new();
        public int skippedFiles;
        // One line per skipped drawing: file name + reason. Only whole-
        // drawing failures land here (file missing, read error, PnP
        // reconnect / WorkingDatabase swap failed) — they still stop.
        public List<string> failures = new();
        // Individual parts PartReader could not read. They used to stop
        // generation ("strict mode"); now they go to the review section.
        public List<ReviewItem> unreadParts = new();
    }

    /// <summary>
    /// Reading the project's saved drawings and the file-system plumbing
    /// around it, shared by WRITESPEC* and INSULSPEC: the "save first"
    /// check, the side-database scan with progress, the output dialog and
    /// opening the result.
    /// </summary>
    internal static class ProjectScanner
    {
        /// <summary>
        /// Disk-based scans require saved inputs. Never save or close users'
        /// drawings here: an open drawing of the scan scope with unsaved
        /// changes stops the command with a "save it first" message.
        /// </summary>
        internal static bool EnsureDrawingsSaved(PlantProject project, Document activeDoc, bool activeOnly,
                                                 string title, Action<string> sayErr)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (activeOnly) paths.Add(activeDoc.Name);
            else
                foreach (var pp in project.ProjectParts)
                    if (pp is Autodesk.ProcessPower.P3dProjectParts.PipingProject pipingPart)
                        foreach (var file in pipingPart.ProjectFiles)
                            if (IsUnderPlant3dModels(file))
                            {
                                var path = ProjectFilePath(file);
                                if (!string.IsNullOrEmpty(path)) paths.Add(path);
                            }
            foreach (Document document in AcadApp.DocumentManager)
            {
                if (document == null || !paths.Any(path => PathEquals(path, document.Name))) continue;
                try
                {
                    dynamic comDocument = document.GetAcadDocument();
                    if (comDocument.Saved) continue;
                    WinForms.MessageBox.Show(
                        $"Сохраните чертёж {Path.GetFileName(document.Name)} и повторите генерацию. Данные читаются с диска.",
                        title, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                    return false;
                }
                catch (System.Exception ex)
                {
                    sayErr($"Не удалось проверить сохранение {document.Name}: {ex.Message}. Генерация остановлена.");
                    return false;
                }
            }
            return true;
        }

        internal static string AskOutputPath(string initialDir, string defaultName, string title)
        {
            using var dlg = new WinForms.SaveFileDialog
            {
                Title = title,
                Filter = "Word документ (*.docx)|*.docx",
                FileName = SanitizeFileName(defaultName ?? "Спецификация") + ".docx",
                InitialDirectory = string.IsNullOrEmpty(initialDir) ? null : initialDir,
            };
            return dlg.ShowDialog() == WinForms.DialogResult.OK ? dlg.FileName : null;
        }

        /// <summary>
        /// Strip filename-illegal characters so the SaveFileDialog never
        /// receives "Foo/Bar.docx" or similar. Project names occasionally
        /// have colons or slashes; leaving them in tripped GUI dialogs on
        /// Russian Windows.
        /// </summary>
        static string SanitizeFileName(string name)
        {
            var bad = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var ch in name)
                sb.Append(Array.IndexOf(bad, ch) >= 0 ? '_' : ch);
            var cleaned = sb.ToString().Trim();
            return string.IsNullOrEmpty(cleaned) ? "Спецификация" : cleaned;
        }

        internal static void TryOpenInWord(string path, string tag, Action<string> say)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
            }
            catch (System.Exception ex)
            {
                say($"[{tag}] Файл сохранен, открыть в Word не удалось: {ex.Message}");
            }
        }

        internal static bool IsFileLocked(IOException exception)
        {
            int errorCode = System.Runtime.InteropServices.Marshal.GetHRForException(exception) & ((1 << 16) - 1);
            return errorCode == 32 || errorCode == 33; // 32 = ERROR_SHARING_VIOLATION, 33 = ERROR_LOCK_VIOLATION
        }

        // --- scan --------------------------------------------------------

        static void CollectUnread(ScanResult scan, PartReadResult result, string path)
        {
            if (result == null) return;
            scan.unreadParts.AddRange(ReviewSection.FromUnreadRows(result.SkippedRowDetails, path));
        }

        /// <summary>
        /// Read a single DWG — the active drawing — instead of the whole
        /// project. Used by the "Текущий чертёж" button (forcedScope=dwg)
        /// where scope is fixed before the picker. Not cached: a one-file
        /// read is already fast, and ScanCache is keyed on the full project
        /// fingerprint which doesn't apply here.
        ///
        /// records == activeDwgRecords by construction (one and the same
        /// drawing), so ProjectFacts/activeFacts in RunCore match and the
        /// downstream scope=dwg filter is a no-op.
        /// </summary>
        internal static ScanResult ScanSingleDrawing(PlantProject project, string activePath,
            Action<string> say, Action<string> trace, UI.ProgressWindow progress,
            string tag = "Спецификация")
        {
            // Need the project DataLinksManager: a side-DB read alone can't
            // resolve every property without the project-level link blob.
            Project piping = null;
            foreach (var pp in project.ProjectParts)
                if (pp is Autodesk.ProcessPower.P3dProjectParts.PipingProject)
                { piping = pp; break; }
            var projectDlm = piping?.DataLinksManager as DataLinksManager;

            progress.Report(0, 1, $"Чертёж: {Path.GetFileName(activePath)}");
            var scan = new ScanResult();

            if (!File.Exists(activePath))
            {
                say($"[{tag}] Активный чертёж не найден на диске: {Path.GetFileName(activePath)}");
                scan.skippedFiles++;
                scan.failures.Add($"{Path.GetFileName(activePath)}: файл не найден на диске");
                return scan;
            }

            try
            {
                PartReadResult result = ReadOneDrawing(activePath, activePath, projectDlm, trace);
                CollectUnread(scan, result, activePath);
                if (result != null)
                {
                    foreach (var r in result.Records)
                        r.SourceDwgPath = activePath;
                    scan.records.AddRange(result.Records);
                    scan.activeDwgRecords.AddRange(result.Records);
                    foreach (var id in result.EquipmentOwnedPnPIds) scan.ownedPnPIds.Add(id);
                    foreach (var kv in result.Topology)
                        scan.topology[kv.Key] = new List<int>(kv.Value);
                }
            }
            catch (System.Exception ex)
            {
                trace($"FAILED: {Path.GetFileName(activePath)}: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    trace($"  inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                scan.skippedFiles++;
                scan.failures.Add(DescribeScanFailure(activePath, ex));
            }
            progress.Report(1, 1, $"Готово: {Path.GetFileName(activePath)}");
            return scan;
        }

        /// <summary>
        /// Enumerate the current project drawings and scan fresh data on every run.
        /// </summary>
        internal static ScanResult ScanProject(PlantProject project,
            Action<string> say, Action<string> trace, UI.ProgressWindow progress,
            string tag = "Спецификация")
        {
            // Build the file list once so cache fingerprinting and the
            // real scan see the same set.
            Project piping = null;
            foreach (var pp in project.ProjectParts)
                if (pp is Autodesk.ProcessPower.P3dProjectParts.PipingProject)
                { piping = pp; break; }
            if (piping == null)
            {
                say($"[{tag}] В проекте нет дисциплины Piping.");
                return new ScanResult();
            }
            var dwgFiles = ((Autodesk.ProcessPower.P3dProjectParts.PipingProject)piping)
                .ProjectFiles
                .Where(f => !string.IsNullOrEmpty(f.Name)
                            && f.Name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)
                            && IsUnderPlant3dModels(f))
                .ToList();

            // Project database properties can change without a DWG timestamp
            // changing. A specification always gets a fresh scan.
            var activeDocPath = AcadApp.DocumentManager.MdiActiveDocument?.Name ?? "";
            return CollectFromProject(dwgFiles, piping, activeDocPath, trace, progress);
        }

        static ScanResult CollectFromProject(List<PnPProjectFile> dwgFiles, Project piping, string activeDocPath,
            Action<string> trace, UI.ProgressWindow progress)
        {
            var scan = new ScanResult();

            trace($"Чертежей в Plant 3D Models: {dwgFiles.Count}");
            var drawingIds = new DrawingPartIds();
            var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var projectDlm = piping.DataLinksManager as DataLinksManager;

            for (var i = 0; i < dwgFiles.Count; i++)
            {
                if (progress.IsCancelled) break;
                var f = dwgFiles[i];

                var path = ProjectFilePath(f);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    trace($"Пропущен (не найден на диске): {f.Name}");
                    scan.skippedFiles++;
                    scan.failures.Add($"{f.Name}: файл не найден на диске");
                    progress.Report(i + 1, dwgFiles.Count, $"Пропущен: {f.Name}");
                    continue;
                }

                progress.Report(i, dwgFiles.Count, $"Чертёж: {Path.GetFileName(path)}");
                if (!scannedPaths.Add(Path.GetFullPath(path))) continue;

                try
                {
                    PartReadResult result = ReadOneDrawing(path, activeDocPath, projectDlm, trace);
                    if (result == null) continue;
                    CollectUnread(scan, result, path);

                    // Tag every record with the absolute path it came from.
                    // The scope=dwg filter (and any future per-drawing
                    // diagnostics) must compare by path rather than PnPID:
                    // PnPIDs are drawing-local row-ids and the same int
                    // identifies different parts across different DWGs.
                    drawingIds.Remap(Path.GetFullPath(path), result.Records,
                        result.EquipmentOwnedPnPIds, result.Topology);
                    foreach (var r in result.Records)
                        trace($"  scan#{r.PnPID} = {Path.GetFileName(path)} / PnPID {r.SourcePnPID}");

                    scan.records.AddRange(result.Records);
                    if (PathEquals(path, activeDocPath))
                        scan.activeDwgRecords.AddRange(result.Records);
                    foreach (var id in result.EquipmentOwnedPnPIds) scan.ownedPnPIds.Add(id);
                    foreach (var kv in result.Topology)
                    {
                        if (!scan.topology.TryGetValue(kv.Key, out var existing))
                            scan.topology[kv.Key] = new List<int>(kv.Value);
                        else
                            foreach (var id in kv.Value)
                                if (!existing.Contains(id)) existing.Add(id);
                    }
                }
                catch (System.Exception ex)
                {
                    trace($"FAILED: {Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
                    if (ex.InnerException != null)
                        trace($"  inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    scan.skippedFiles++;
                    scan.failures.Add(DescribeScanFailure(path, ex));
                }
                progress.Report(i + 1, dwgFiles.Count, $"Готово: {Path.GetFileName(path)}");
            }
            return scan;
        }

        /// <summary>
        /// One-line reason for a drawing that failed to scan. Our own
        /// incomplete-read exception already starts with the file name;
        /// anything else (ReadDwgFile, DLM) gets the file name prefixed.
        /// </summary>
        static string DescribeScanFailure(string path, System.Exception ex)
        {
            var name = Path.GetFileName(path);
            return ex is InvalidOperationException && ex.Message.StartsWith(name + ":", StringComparison.Ordinal)
                ? ex.Message
                : $"{name}: {ex.GetType().Name}: {ex.Message}";
        }

        static PartReadResult ReadOneDrawing(string path, string activeDocPath,
            DataLinksManager projectDlm, Action<string> trace)
        {
            // Read saved drawings; the commands check relevant open documents without modifying them.
            using var db = new Database(false, true);
            db.ReadDwgFile(path, FileShare.ReadWrite, true, "");
            db.CloseInput(true);
            string dwgName = Path.GetFileName(path);
            var result = PartReader.Read(db, projectDlm,
                msg => trace?.Invoke($"  [{dwgName}] {msg}"));

            // Surface read-stage drops in the log: a part that vanished here
            // never reaches the filter pipeline, so DropReasons won't show it.
            if (result != null && result.SkippedRows > 0)
                trace?.Invoke($"  [{dwgName}] пропущено строк при чтении: {result.SkippedRows} (см. строки выше)");
            if (result != null && result.WorkingDbSwapFailed)
                trace?.Invoke($"  [{dwgName}] ВНИМАНИЕ: swap WorkingDatabase не удался — часть строк могла не прочитаться.");
            if (result != null && result.PnpReconnectFailed)
                trace?.Invoke($"  [{dwgName}] ВНИМАНИЕ: база PnP не переподключилась после Disconnect() — чтение недостоверно, перезапустите AutoCAD.");
            // Individual unreadable parts (SkippedRows) no longer stop the run
            // — the caller lists them in the «Позиции, требующие проверки»
            // section. Only drawing-wide unreliability still stops: a failed
            // PnP reconnect or WorkingDatabase swap taints EVERY row read
            // after it, so no single-part list could describe the damage.
            if (result == null || ReviewSection.ReadStopsGeneration(
                    result.SkippedRows, result.WorkingDbSwapFailed, result.PnpReconnectFailed))
                throw new InvalidOperationException(DescribeIncompleteRead(dwgName, result));
            return result;
        }

        /// <summary>
        /// The stop message for a drawing whose whole read is unreliable
        /// (see ReadOneDrawing). Unreadable single parts are not listed here
        /// any more — they go to the review section.
        /// </summary>
        static string DescribeIncompleteRead(string dwgName, PartReadResult result)
        {
            if (result == null) return $"{dwgName}: чертёж не прочитан.";
            var reasons = new List<string>();
            if (result.WorkingDbSwapFailed) reasons.Add("не удалось переключить WorkingDatabase");
            if (result.PnpReconnectFailed) reasons.Add("база PnP не переподключилась (перезапустите AutoCAD)");
            return $"{dwgName}: " + string.Join("; ", reasons);
        }

        /// <summary>
        /// Только DWG из "Plant 3D Models" поддерева. Ortho / Iso /
        /// symbol-style DWG в спецификацию не идут и только тормозят
        /// сканирование. Совпадение по фрагменту пути, не по абсолютному
        /// родителю — на случай локализованных установок.
        /// </summary>
        static bool IsUnderPlant3dModels(PnPProjectFile f)
        {
            string path = null;
            try { path = f.RelativeFileName; } catch { }
            if (string.IsNullOrEmpty(path)) path = ProjectFilePath(f);
            if (string.IsNullOrEmpty(path)) return false;
            var normalized = path.Replace('/', '\\');
            return normalized.IndexOf("\\Plant 3D Models\\",
                       StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.StartsWith("Plant 3D Models\\",
                       StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The on-disk path of a project drawing — the ONE rule used by both
        /// the "is it saved?" pre-check and the scan itself. They used to
        /// differ (`??` vs IsNullOrEmpty): an EMPTY ResolvedFilePath made the
        /// pre-check fall through to nothing while the scan used
        /// AbsoluteFileName, so an unsaved open drawing could be read from
        /// its stale disk copy without the "save first" stop.
        /// </summary>
        static string ProjectFilePath(PnPProjectFile f)
        {
            string resolved = null, absolute = null;
            try { resolved = f.ResolvedFilePath; } catch { }
            if (!string.IsNullOrEmpty(resolved)) return resolved;
            try { absolute = f.AbsoluteFileName; } catch { }
            return string.IsNullOrEmpty(absolute) ? null : absolute;
        }

        internal static bool PathEquals(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                var p1 = Path.GetFullPath(a).Replace('/', '\\').TrimEnd('\\');
                var p2 = Path.GetFullPath(b).Replace('/', '\\').TrimEnd('\\');
                return string.Equals(p1, p2, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(
                    a.Replace('/', '\\').TrimEnd('\\'),
                    b.Replace('/', '\\').TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
