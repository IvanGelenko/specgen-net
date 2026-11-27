using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.DataObjects;
using Autodesk.ProcessPower.PnP3dObjects;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Matches ВТ rows to P3dLineGroup rows by the custom "ID" column and
    /// writes the ВТ field values back through
    /// DataLinksManager.SetProperties. This is the plug-in's first DLM
    /// write path — everything else in the project only reads
    /// (GetAllProperties / GetRelatedRowIds). Always operate on the
    /// project-level DataLinksManager: P3dLineGroup is a project-wide
    /// table and the project DLM is the one Plant 3D keeps authoritative.
    /// (PartReader.ResolveLiveLineTag goes the other way on purpose: a
    /// PART's line is looked up in the drawing DLM, because the part's
    /// rowId only exists in that drawing's id space.)
    ///
    /// Empty ВТ cells never clear a project value (BuildPlan skips them).
    /// The ledger uses FindOrphanLines / DeleteOrphanLines when the user
    /// confirms deletion of a selected duplicate ID record.
    /// </summary>
    public static class LineSyncService
    {
        const string DrawingLineGroupRelationship = "P3dDrawingLineGroupRelationship";
        const string LineGroupPartRelationship = "P3dLineGroupPartRelationship";

        internal static HashSet<int> ReadDrawingLineIds(DataLinksManager projectDlm, Document drawing)
        {
            // Drawing-to-line registrations can outlive the parts on the DWG.
            // Start at live parts and resolve both row IDs with the project DLM,
            // which also supplies the P3dLineGroup IDs used by the sync plan.
            var lineIds = new HashSet<int>();
            var seenBlocks = new HashSet<ObjectId>();
            var pendingBlocks = new Stack<ObjectId>();
            var db = drawing.Database;
            using (drawing.LockDocument())
            using (var transaction = db.TransactionManager.StartOpenCloseTransaction())
            {
                pendingBlocks.Push(SymbolUtilityServices.GetBlockModelSpaceId(db));
                while (pendingBlocks.Count > 0)
                {
                    var blockId = pendingBlocks.Pop();
                    if (blockId.IsNull || blockId.IsErased || !seenBlocks.Add(blockId)) continue;
                    var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
                    if (block.IsFromExternalReference || block.IsFromOverlayReference) continue;

                    foreach (ObjectId entityId in block)
                    {
                        var entity = transaction.GetObject(entityId, OpenMode.ForRead);
                        if (entity is not Part)
                        {
                            if (entity is BlockReference reference)
                                pendingBlocks.Push(reference.BlockTableRecord);
                            continue;
                        }

                        int partRowId;
                        try { partRowId = projectDlm.FindAcPpRowId(entityId); }
                        catch (DLException ex) when (ex.ErrorCode == DLStatus.ObjectDoesNotHaveLink) { continue; }
                        if (partRowId <= 0) continue;

                        var related = projectDlm.GetRelatedRowIds(LineGroupPartRelationship, "Part", partRowId, "LineGroup");
                        if (related == null) continue;
                        foreach (int rowId in related)
                            if (rowId > 0) lineIds.Add(rowId);
                    }
                }
                transaction.Commit();
            }
            return lineIds;
        }

        /// <summary>
        /// Reads every P3dLineGroup row's PnPID / ID / Tag from the
        /// project DataLinksManager. Used both to build the sync plan and,
        /// standalone, to let a settings screen show "known Ids in the
        /// project" for the picklist-mapping table.
        /// </summary>
        public static List<LineGroupInfo> ReadAllLines(DataLinksManager projectDlm)
        {
            var pnpDb = projectDlm?.GetPnPDatabase()
                ?? throw new InvalidOperationException("База данных проекта Plant 3D недоступна.");
            var table = pnpDb.Tables["P3dLineGroup"]
                ?? throw new InvalidOperationException("Таблица P3dLineGroup не найдена в проекте.");
            table.RefreshCachedRows();
            var rows = table.Select()
                ?? throw new InvalidOperationException("Не удалось прочитать список строк P3dLineGroup.");

            var result = new List<LineGroupInfo>();
            var seenRowIds = new HashSet<int>();
            foreach (var row in rows)
            {
                var rowId = Convert.ToInt32(row["PnPID"]);
                if (rowId <= 0)
                    throw new InvalidOperationException("В таблице P3dLineGroup найдена строка без корректного PnPID.");
                if (!seenRowIds.Add(rowId) || pnpDb.IsRowDeleted(rowId)) continue;

                try
                {
                    var info = new LineGroupInfo { RowId = rowId };
                    foreach (var kv in ReadLineProperties(projectDlm, rowId))
                        info.AllProperties[kv.Key] = kv.Value;
                    info.AllProperties.TryGetValue("ID", out info.Id);
                    info.AllProperties.TryGetValue("Tag", out info.Tag);
                    result.Add(info);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Не удалось прочитать линию PnPID {rowId}: {ex.Message}", ex);
                }
            }
            return result;
        }

        static Dictionary<string, string> ReadLineProperties(DataLinksManager projectDlm, int rowId)
        {
            var values = projectDlm.GetAllProperties(rowId, true)
                ?? throw new InvalidOperationException($"Свойства строки PnPID {rowId} недоступны.");
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
                properties[value.Key] = value.Value;
            return properties;
        }

        /// <summary>
        /// Finds P3dLineGroup rows with a non-blank Id but zero linked
        /// parts — candidates for deletion. Real orphans, per the user:
        /// if there's no line with this ID anywhere in the actual
        /// drawings, the P3dLineGroup row is stale/junk (e.g. left over
        /// from testing an ID value) and should go.
        ///
        /// Checked TWO independent ways before a row is trusted as an
        /// orphan, because deleting a P3dLineGroup row cascades through
        /// PnPRow.Delete() to EVERY relationship type in the project
        /// database, not just P3dLineGroupPartRelationship (confirmed by
        /// reflection against PnPDataObjects.dll) — a false positive from
        /// a single stale check could silently sever real links:
        ///   1. GetRelatedRowIds("P3dLineGroupPartRelationship",
        ///      "LineGroup", rowId, "Part") — the same relationship
        ///      PartReader.QueryLineGroupRowId walks the other direction.
        ///   2. PnPDatabase.SelectRowRelationships(rowId) — every
        ///      relationship type on this row, not just the one above,
        ///      EXCEPT P3dDrawingLineGroupRelationship. Confirmed by
        ///      live-run log output: this one relationship exists on
        ///      every P3dLineGroup row regardless of whether any part
        ///      uses it — it's Plant 3D's "this row belongs to drawing X"
        ///      bookkeeping, not evidence of use. If check #2 finds any
        ///      OTHER relationship type, the row is kept — that might be
        ///      a real link check #1 missed.
        /// A row must come back empty on check #1 and carry nothing but
        /// P3dDrawingLineGroupRelationship (or truly nothing) on check #2
        /// to be reported as an orphan. Either check throwing is treated
        /// as "not verified, don't touch it" — never as "must be an
        /// orphan".
        /// </summary>
        public static List<OrphanLineInfo> FindOrphanLines(
            List<LineGroupInfo> lines, DataLinksManager projectDlm, Action<string> trace = null)
        {
            void Trace(string msg) { try { trace?.Invoke(msg); } catch { } }
            var result = new List<OrphanLineInfo>();
            if (projectDlm == null) return result;

            PnPDatabase pnpDb = null;
            try { pnpDb = projectDlm.GetPnPDatabase(); }
            catch (Exception ex) { Trace($"LineSyncService.FindOrphanLines: GetPnPDatabase не удался — {ex.Message}"); return result; }
            if (pnpDb == null) return result;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.Id)) continue;

                bool check1Empty;
                try
                {
                    var related = projectDlm.GetRelatedRowIds(
                        LineGroupPartRelationship, "LineGroup", line.RowId, "Part");
                    check1Empty = related == null || !related.GetEnumerator().MoveNext();
                }
                catch (Exception ex)
                {
                    Trace($"LineSyncService.FindOrphanLines: rowId {line.RowId} (ID={line.Id}) — проверка №1 не удалась, пропуск — {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
                if (!check1Empty) continue; // has a linked part — definitely not an orphan.

                PnPRelationship[] allRelations;
                try
                {
                    allRelations = pnpDb.SelectRowRelationships(line.RowId);
                }
                catch (Exception ex)
                {
                    Trace($"LineSyncService.FindOrphanLines: rowId {line.RowId} (ID={line.Id}) — проверка №2 не удалась, пропуск — {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
                if (allRelations != null && allRelations.Length > 0)
                {
                    // Every P3dLineGroup row carries a
                    // P3dDrawingLineGroupRelationship purely because it's
                    // registered against a drawing in the project — this
                    // is Plant 3D bookkeeping ("this row belongs to
                    // drawing X"), not evidence the line is actually used
                    // by any part. Confirmed by log output: real orphans
                    // (zero parts, per check #1) carry exactly this one
                    // relationship type and nothing else. Anything OTHER
                    // than that type still blocks deletion — it might be
                    // a real link check #1 missed.
                    var otherRelations = allRelations
                        .Where(r =>
                        {
                            try { return !string.Equals(r.RelationshipType?.Name, DrawingLineGroupRelationship, StringComparison.OrdinalIgnoreCase); }
                            catch { return true; } // can't identify it — treat as "other", err on not deleting.
                        })
                        .ToList();
                    if (otherRelations.Count > 0)
                    {
                        var typeNames = otherRelations.Select(r =>
                        {
                            try { return r.RelationshipType?.Name ?? "?"; }
                            catch { return "?"; }
                        }).Distinct().ToList();
                        Trace($"LineSyncService.FindOrphanLines: rowId {line.RowId} (ID={line.Id}) — нет связи через P3dLineGroupPartRelationship, но SelectRowRelationships нашёл связи, отличные от P3dDrawingLineGroupRelationship: [{string.Join(", ", typeNames)}] — НЕ удаляется.");
                        continue;
                    }
                    Trace($"LineSyncService.FindOrphanLines: rowId {line.RowId} (ID={line.Id}) — только служебная связь P3dDrawingLineGroupRelationship ({allRelations.Length}), деталей нет — считается сиротой.");
                }

                result.Add(new OrphanLineInfo { Line = line });
            }
            return result;
        }

        internal static bool CanReleaseLineId(LineGroupInfo line, DataLinksManager projectDlm, Action<string> trace) =>
            FindOrphanLines(new List<LineGroupInfo> { line }, projectDlm, trace).Count == 1;

        /// <summary>
        /// Deletes confirmed-orphan P3dLineGroup rows via
        /// PnPDatabase.DeleteRows(PnPRowIdArray) — the actual delete API
        /// lives on PnPDatabase/PnPRow, not DataLinksManager (which has
        /// no row-delete method of its own; it only raises
        /// RowDeleting/RowDeleted events for what happens below it).
        /// Same StartTransaction/AcceptChanges/CommitTransaction pattern
        /// as Apply. After commit, every targeted rowId is re-checked
        /// with PnPDatabase.IsRowDeleted — Delete() returns void and
        /// Plant 3D can silently cancel a deletion via row-constraint
        /// checks, so success is verified, not assumed. This is
        /// irreversible outside Plant 3D itself (not covered by AutoCAD
        /// UNDO) — every deleted rowId/Id/Tag is logged.
        /// </summary>
        public static List<string> DeleteOrphanLines(
            List<OrphanLineInfo> orphans, DataLinksManager projectDlm, Action<string> trace = null)
        {
            void Trace(string msg) { try { trace?.Invoke(msg); } catch { } }
            var errors = new List<string>();
            if (orphans == null || orphans.Count == 0) return errors;

            PnPDatabase pnpDb;
            try { pnpDb = projectDlm.GetPnPDatabase(); }
            catch (Exception ex)
            {
                errors.Add($"Не удалось получить PnPDatabase: {ex.GetType().Name}: {ex.Message}");
                return errors;
            }
            if (pnpDb == null) { errors.Add("PnPDatabase недоступна."); return errors; }

            var rowIds = orphans.Select(o => o.Line.RowId).ToList();
            foreach (var o in orphans)
                Trace($"LineSyncService.DeleteOrphanLines: удаляется rowId {o.Line.RowId} (ID={o.Line.Id}, Tag={o.Line.Tag}) — нет связанных деталей.");

            errors = SyncTransaction.Run(
                () => pnpDb.StartTransaction(),
                () => pnpDb.DeleteRows(new PnPRowIdArray(rowIds)),
                () => projectDlm.AcceptChanges(),
                () => pnpDb.CommitTransaction(),
                () => pnpDb.RollbackTransaction());
            if (errors.Count > 0) return errors;

            // PnPRow.Delete() returns void — Plant 3D can veto a deletion
            // via row-constraint checks without surfacing an exception.
            // Confirm every rowId actually left the table.
            foreach (var o in orphans)
            {
                bool deleted;
                try { deleted = pnpDb.IsRowDeleted(o.Line.RowId); }
                catch (Exception ex)
                {
                    errors.Add($"Не удалось проверить удаление rowId {o.Line.RowId}: {ex.Message}");
                    continue;
                }
                if (!deleted)
                {
                    var msg = $"Линия ID={o.Line.Id} (rowId {o.Line.RowId}): удаление не подтвердилось (Plant 3D могла отклонить его).";
                    errors.Add(msg);
                    Trace($"LineSyncService.DeleteOrphanLines: {msg}");
                }
            }

            return errors;
        }

        /// <summary>
        /// Applies the plan inside a required transaction. Any write or persistence
        /// failure stops the batch and attempts rollback; errors always reach the UI.
        /// </summary>
        public static List<string> Apply(LineSyncPlan plan, DataLinksManager projectDlm, Action<string> trace = null)
        {
            var errors = new List<string>();
            var actions = plan?.Actions.Where(action => action.Changes.Count > 0).ToList();
            if (actions == null || actions.Count == 0) return errors;

            bool? previousCaching = null;
            try
            {
                var pnpDb = projectDlm.GetPnPDatabase()
                    ?? throw new InvalidOperationException("База данных проекта Plant 3D недоступна.");
                var table = pnpDb.Tables["P3dLineGroup"]
                    ?? throw new InvalidOperationException("Таблица P3dLineGroup недоступна.");
                previousCaching = projectDlm.EnableCaching;
                projectDlm.EnableCaching = false;

                errors = SyncTransaction.Run(
                    () => pnpDb.StartTransaction(),
                    () =>
                    {
                        table.RefreshCachedRows();
                        foreach (var action in actions)
                        {
                            int rowId = action.Line.RowId;
                            if (pnpDb.IsRowDeleted(rowId))
                                throw new InvalidOperationException($"Строка PnPID {rowId} удалена. Обновите ведомость.");
                            var mismatches = LineSyncPlanner.FindWriteMismatches(action, ReadLineProperties(projectDlm, rowId), afterWrite: false);
                            if (mismatches.Count > 0)
                                throw new InvalidOperationException($"Строка PnPID {rowId} изменилась ({string.Join(", ", mismatches)}). Обновите ведомость.");
                        }
                        var ordered = actions.Any(action => action.Changes.Any(change => change.PropertyName == "ID"))
                            ? LineSyncPlanner.ValidateIdWrites(actions, ReadAllLines(projectDlm), line => CanReleaseLineId(line, projectDlm, trace))
                            : actions;
                        foreach (var action in ordered)
                        {
                            var names = new StringCollection();
                            var values = new StringCollection();
                            foreach (var change in action.Changes)
                            {
                                names.Add(change.PropertyName);
                                values.Add(change.NewValue);
                            }
                            try { projectDlm.SetProperties(action.Line.RowId, names, values); }
                            catch (Exception ex) { throw new InvalidOperationException($"PnPID {action.Line.RowId}: {ex.Message}", ex); }
                        }
                    },
                    () =>
                    {
                        projectDlm.AcceptChanges();
                        foreach (var action in actions)
                        {
                            var idChange = action.Changes.FirstOrDefault(change => change.PropertyName == "ID");
                            if (idChange == null) continue;
                            var current = ReadLineProperties(projectDlm, action.Line.RowId).GetValueOrDefault("ID");
                            if (!LineSyncPlanner.SameFieldValue("ID", idChange.NewValue, current))
                                throw new InvalidOperationException($"Не удалось подтвердить запись ID в строке PnPID {action.Line.RowId}.");
                        }
                    },
                    () => pnpDb.CommitTransaction(),
                    () => pnpDb.RollbackTransaction());

                if (errors.Count == 0)
                {
                    try
                    {
                        table.RefreshCachedRows();
                        foreach (var action in actions)
                        {
                            int rowId = action.Line.RowId;
                            var mismatches = LineSyncPlanner.FindWriteMismatches(action, ReadLineProperties(projectDlm, rowId), afterWrite: true);
                            if (mismatches.Count > 0)
                                errors.Add($"После записи не подтверждены значения в строке PnPID {rowId}: {string.Join(", ", mismatches)}.");
                        }
                    }
                    catch (Exception ex) { errors.Add($"Транзакция завершена, но проверить запись не удалось: {ex.Message}"); }
                }
            }
            catch (Exception ex) { errors.Add($"Синхронизация не начата: {ex.Message}"); }
            finally
            {
                if (previousCaching.HasValue)
                    try { projectDlm.EnableCaching = previousCaching.Value; }
                    catch (Exception ex) { errors.Add($"Не удалось восстановить режим кэша Plant 3D: {ex.Message}"); }
            }
            foreach (var error in errors)
                try { trace?.Invoke(error); } catch { }
            return errors;
        }

        internal static bool RequiresServiceLifeColumn(LineSyncPlan plan, DataLinksManager projectDlm) =>
            plan.Actions.Any(action => action.Changes.Any(change => change.PropertyName == "srok"))
            && !GetLineGroupTable(projectDlm).Columns.Contains("srok");

        static PnPTable GetLineGroupTable(DataLinksManager projectDlm) =>
            projectDlm.GetPnPDatabase()?.Tables["P3dLineGroup"]
            ?? throw new InvalidOperationException("Таблица P3dLineGroup недоступна.");

        internal static void CreateServiceLifeColumn(DataLinksManager projectDlm)
        {
            var pnpDb = projectDlm.GetPnPDatabase()
                ?? throw new InvalidOperationException("База данных проекта Plant 3D недоступна.");
            var table = pnpDb.Tables["P3dLineGroup"];
            if (table.Columns.Contains("srok")) return;

            table.Columns.Add(new PnPColumn("srok", typeof(string), 255));
            table.SetColumnAttribute("srok", "DISPLAYNAME", "Срок службы, лет");
            table.SetColumnAttribute("srok", "ISHIDDENINAREAVIEW", "false");
            table.SetColumnAttribute("srok", "ISNEWCREATION", "true");
            table.SetColumnAttribute("srok", "TYPE", "PnPStringType");
            pnpDb.AcceptSchemaChanges();
        }
    }
}
