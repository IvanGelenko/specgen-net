using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.ProcessPower.DataLinks;
using PipingSpecPlugin.Audit;
using PipingSpecPlugin.Spec;

namespace PipingSpecPlugin.Commands
{
    public sealed class AuditLineCatalog
    {
        public List<AuditLineChoice> Lines { get; } = new();
        public Dictionary<string, AuditPart> Parts { get; } = new();
        // Rows of the saved drawing whose properties could not be read. Like
        // in the specification they no longer stop the run: their elements
        // are read from the open drawing and the rows are reported as
        // «Не проверено» (merge them into the snapshot's ReadIssues).
        public List<AuditIssue> ReadIssues { get; } = new();

        readonly List<(string Line, string Reason)> unresolvedIsometricRows = new();

        public Dictionary<string,string> DrawingLines() => Parts.Values
            .Where(p => !string.IsNullOrWhiteSpace(p.LineKey))
            .ToDictionary(p => p.Key, p => p.LineKey, StringComparer.OrdinalIgnoreCase);

        public void EnsureIsometricLinesReadable(IEnumerable<string> selectedLines)
        {
            var selected = selectedLines?.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var reasons = unresolvedIsometricRows.Where(r => selected == null || r.Line == null || selected.Contains(r.Line))
                .Select(r => r.Reason).Distinct().ToList();
            if (reasons.Count > 0)
                throw new InvalidOperationException("Невозможно подтвердить полный состав линии Plant 3D.\n" + string.Join("\n", reasons));
        }

        // Audit reads the saved DWG. Isometrics must use the open drawing and its current project links.
        public static AuditLineCatalog Read(Document document, Action<string> trace, bool forIsometric = false)
        {
            if (!forIsometric && (string.IsNullOrEmpty(document.Name) || !File.Exists(document.Name)))
                throw new InvalidOperationException("Сохраните чертёж перед чтением данных линий.");
            var result = new AuditLineCatalog();
            var missing = new List<(Handle Handle, int Row, string Line, string Reason)>();
            var primaryRows = new Dictionary<string, int>();
            var hasPrimaryProperties = new HashSet<string>();
            var subParts = new Dictionary<string, List<(int Row, AuditSubPart Part)>>();
            using var saved = forIsometric ? null : new Database(false, true);
            if (saved != null)
            {
                saved.ReadDwgFile(document.Name, FileShare.ReadWrite, true, "");
                saved.CloseInput(true);
            }
            var disk = saved ?? document.Database;
            DataLinksManager projectLinks = null;
            if (forIsometric && Autodesk.ProcessPower.PlantInstance.PlantApplication.CurrentProject is {} project &&
                project.ProjectParts["Piping"] is Autodesk.ProcessPower.P3dProjectParts.PipingProject piping)
                projectLinks = piping.DataLinksManager;
            var read = PartReader.Read(disk, projectLinks, trace);
            string sourceName = forIsometric ? "открытого чертежа" : "сохранённого чертежа";
            // Only a drawing-wide failure stops (same policy as the spec scan).
            if (ReviewSection.ReadStopsGeneration(read.SkippedRows, read.WorkingDbSwapFailed, read.PnpReconnectFailed))
                throw new InvalidOperationException(read.PnpReconnectFailed
                    ? "База Plant 3D не переподключилась при чтении чертежа — данные недостоверны. Перезапустите AutoCAD и повторите аудит."
                    : "Не удалось переключить рабочую базу для чтения сохранённого чертежа — данные недостоверны. Повторите аудит.");
            var dlm = DataLinksManager.GetManager(disk);
            var previous = HostApplicationServices.WorkingDatabase;
            try
            {
                HostApplicationServices.WorkingDatabase = disk;
                foreach (var group in read.Records.GroupBy(p => string.IsNullOrWhiteSpace(p.LineNumberTag) ? AuditOptions.NoLine : p.LineNumberTag.Trim(), StringComparer.Ordinal))
                {
                    result.Lines.Add(new AuditLineChoice { Key = group.Key, Label = group.Key == AuditOptions.NoLine ? AuditOptions.NoLineLabel : group.Key });
                    foreach (var record in group)
                    {
                        foreach (var handle in PartEntityIndex.EntityHandlesForRow(dlm, record.PnPID, disk))
                        {
                            // An isometric marker linked to the part's row is
                            // not one of its elements (the audit skips it too).
                            if (IsIsoSymbol(disk, handle)) continue;
                            var key = handle.ToString();
                            // Gaskets/bolts/weld subparts can share a connector's
                            // handle. Prefer the entity's own row for its properties.
                            if (!primaryRows.TryGetValue(key, out var primaryRow))
                                primaryRows[key] = primaryRow = PrimaryRowOf(dlm, disk, handle, trace);
                            // Another row on the element — a connection object's
                            // gasket, bolt set or weld: its spec and status.
                            if (record.PnPID != primaryRow)
                            {
                                if (!subParts.TryGetValue(key, out var list)) subParts[key] = list = new List<(int, AuditSubPart)>();
                                if (!list.Any(s => s.Row == record.PnPID))
                                    list.Add((record.PnPID, new AuditSubPart { ClassName = record.PnPClassName, Spec = record.Spec, Status = record.Status,
                                        Description = string.IsNullOrWhiteSpace(record.PartSizeLongDesc) ? record.ShortDescription ?? record.PnPClassName : record.PartSizeLongDesc }));
                            }
                            if (hasPrimaryProperties.Contains(key)) continue;
                            if (record.PnPID == primaryRow) hasPrimaryProperties.Add(key);
                            else if (result.Parts.ContainsKey(key)) continue;
                            var part = new AuditPart { Key = key,
                                LineKey = group.Key == AuditOptions.NoLine ? null : group.Key,
                                LineLabel = group.Key == AuditOptions.NoLine ? AuditOptions.NoLineLabel : group.Key,
                                ClassName = record.PnPClassName,
                                // Blank, not only null, falls back — as in the live read.
                                Description = string.IsNullOrWhiteSpace(record.PartSizeLongDesc) ? record.PartFamilyLongDesc : record.PartSizeLongDesc,
                                InsulationThickness = record.InsulationThickness,
                                Diameter = record.NominalDiameter, WallRaw = record.WallThickness, LengthUnit = record.LengthUnit,
                                Material = record.Material, MaterialCode = record.MaterialCode,
                                Tag = record.EquipTag, Status = record.Status, Spec = record.Spec };
                            if (AuditRules.IsTeeOrReducerClass(record.PnPClassName))
                            {
                                try { part.Ends = DrawingAuditReader.ReadEnds(dlm, record.PnPID, "S1", record.NominalDiameter, record.WallThickness, record.LengthUnit); }
                                catch (System.Exception ex)
                                {
                                    part.EndsError = ex.Message;
                                    trace?.Invoke($"AuditLineCatalog: концы детали {key} (строка {record.PnPID}) не прочитаны — {ex.GetType().Name}: {ex.Message}");
                                }
                            }
                            result.Parts[key] = part;
                        }
                    }
                }
                foreach (var (rowId, reason) in read.SkippedRowDetails)
                {
                    var handles = PartEntityIndex.EntityHandlesForRow(dlm, rowId, disk).Distinct().ToList();
                    var keys = handles.Select(h => h.ToString()).ToList();
                    if (forIsometric)
                    {
                        var row = new AuditPart();
                        try { DrawingAuditReader.SetLine(row, rowId, dlm, null); }
                        catch (System.Exception ex) { trace?.Invoke($"Строка {rowId}: {ex.Message}"); }
                        if (handles.Count == 0)
                            result.unresolvedIsometricRows.Add((row.LineKey, $"Строка {rowId}: не найден элемент чертежа ({reason})."));
                        foreach (var handle in handles)
                        {
                            if (IsIsoSymbol(disk, handle) || hasPrimaryProperties.Contains(handle.ToString())) continue;
                            missing.Add((handle, rowId, row.LineKey, reason));
                        }
                    }
                    trace?.Invoke($"AuditLineCatalog: строка {rowId} не прочитана ({reason}); элементы: {(keys.Count == 0 ? "не найдены" : string.Join(", ", keys))}");
                    // A gasket / bolt row shares its connector's element, which
                    // may still have properties from another row.
                    result.ReadIssues.Add(new AuditIssue { RuleId = "read", Check = "Чтение данных", Severity = "Не проверено",
                        Message = $"Свойства детали (строка Plant 3D {rowId}) не прочитаны из {sourceName}: {reason}. " +
                            (keys.Count == 0 ? "Элемент чертежа для неё не найден — деталь в аудит не вошла."
                             : keys.All(result.Parts.ContainsKey) ? "Элемент проверен по данным другой строки, связанной с ним."
                             : "Номер линии и свойства элемента взяты из открытого чертежа и могут быть устаревшими."),
                        Keys = keys });
                }
            }
            finally { HostApplicationServices.WorkingDatabase = previous; }
            if (missing.Count > 0)
            {
                var db = document.Database;
                var live = DataLinksManager.GetManager(db);
                using var transaction = db.TransactionManager.StartOpenCloseTransaction();
                foreach (var entries in missing.GroupBy(e => e.Handle))
                {
                    string key = entries.Key.ToString();
                    result.Parts.Remove(key); // A shared subpart row cannot establish the owner's line.
                    try
                    {
                        var id = db.GetObjectId(false, entries.Key, 0);
                        if (transaction.GetObject(id, OpenMode.ForRead) is not Autodesk.ProcessPower.PnP3dObjects.Part) continue;
                        var part = ReadLiveIsometricPart(live, id);
                        if (part == null || string.IsNullOrWhiteSpace(part.LineKey))
                            throw new InvalidOperationException("Номер линии не определён.");
                        result.Parts[key] = part;
                        trace?.Invoke($"Элемент {key}: восстановлен из открытого DWG, линия {part.LineKey}.");
                    }
                    catch (System.Exception ex)
                    {
                        foreach (var entry in entries)
                            result.unresolvedIsometricRows.Add((entry.Line, $"Строка {entry.Row}, элемент {key}: {entry.Reason}. {ex.Message}"));
                    }
                }
            }
            foreach (var pair in subParts)
                if (result.Parts.TryGetValue(pair.Key, out var owner)) owner.SubParts.AddRange(pair.Value.Select(s => s.Part));
            if (read.Records.Count > 0 && result.Parts.Count == 0 && result.unresolvedIsometricRows.Count == 0)
                throw new InvalidOperationException("Не удалось сопоставить линии с элементами текущего чертежа.");
            // The count is of drawing elements, as in the audit summary — not of
            // data rows (gaskets and bolt sets have no element of their own).
            // A line all of whose rows went to other lines' elements audits
            // nothing and is not offered.
            var elements = result.Parts.Values.GroupBy(p => p.LineKey ?? AuditOptions.NoLine, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            foreach (var (line, _) in result.unresolvedIsometricRows)
                if (line != null) elements.TryAdd(line, 0);
            var existing = result.Lines.Select(l => l.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var key in elements.Keys.Where(k => !existing.Contains(k)))
                result.Lines.Add(new AuditLineChoice { Key = key, Label = key == AuditOptions.NoLine ? AuditOptions.NoLineLabel : key });
            result.Lines.RemoveAll(l => !elements.ContainsKey(l.Key));
            foreach (var line in result.Lines) line.Count = elements[line.Key];
            result.Lines.Sort((a, b) => AuditLineOrder.Compare(a.Key, b.Key));
            return result;
        }

        public static AuditLineCatalog ReadLiveIsometric(Database db, DataLinksManager projectLinks = null)
        {
            var result = new AuditLineCatalog();
            var dlm = DataLinksManager.GetManager(db);
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var space = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Autodesk.ProcessPower.PnP3dObjects.Part || IsIsoSymbol(db, id.Handle)) continue;
                var part = ReadLiveIsometricPart(dlm, id);
                if (string.IsNullOrWhiteSpace(part?.LineKey) && projectLinks != null && !ReferenceEquals(dlm, projectLinks))
                    part = ReadLiveIsometricPart(projectLinks, id) ?? part;
                if (part != null) result.Parts.Add(part.Key, part);
            }
            return result;
        }

        static AuditPart ReadLiveIsometricPart(DataLinksManager dlm, ObjectId id)
        {
            int rowId;
            try { rowId = dlm.FindAcPpRowId(id); }
            catch (System.Exception ex) when (ex.Message.Contains("ObjectDoesNotHaveLink", StringComparison.OrdinalIgnoreCase)) { return null; }
            if (rowId <= 0) return null;
            var values = dlm.GetAllProperties(rowId, true);
            string Get(string key) => values.FirstOrDefault(p => key.Equals(p.Key, StringComparison.OrdinalIgnoreCase)).Value;
            var part = new AuditPart { Key = id.Handle.ToString(), Diameter = Get("NominalDiameter"),
                Tag = Get("Tag"), InsulationThickness = Get("InsulationThickness") };
            DrawingAuditReader.SetLine(part, rowId, dlm, Get("LineNumberTag"));
            return part;
        }

        static bool IsIsoSymbol(Database disk, Handle handle)
        {
            try
            {
                for (var rx = disk.GetObjectId(false, handle, 0).ObjectClass; rx != null; rx = rx.MyParent)
                    if (rx.Name.StartsWith("AcPpDb3dIso", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        // One entity whose own row cannot be resolved (stale handle, non-
        // Plant entity sharing a sub-part handle) used to throw out of the
        // whole catalogue read and abort the audit. 0 = "no own row": the
        // first record that maps to the handle then supplies its properties.
        static int PrimaryRowOf(DataLinksManager dlm, Database disk, Handle handle, Action<string> trace)
        {
            try { return dlm.FindAcPpRowId(disk.GetObjectId(false, handle, 0)); }
            catch (System.Exception ex)
            {
                trace?.Invoke($"AuditLineCatalog: строка для handle {handle} не определена — {ex.GetType().Name}: {ex.Message}");
                return 0;
            }
        }
    }
}
