using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.DataObjects;

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
    /// FindOrphanLines / DeleteOrphanLines are currently not called from
    /// anywhere — the automatic orphan cleanup was taken out of the
    /// window; the code is kept for a deliberate, confirmed cleanup.
    /// </summary>
    public static partial class LineSyncService
    {
        /// <summary>
        /// Reads every P3dLineGroup row's PnPID / ID / Tag from the
        /// project DataLinksManager. Used both to build the sync plan and,
        /// standalone, to let a settings screen show "known Ids in the
        /// project" for the picklist-mapping table.
        /// </summary>
        public static List<LineGroupInfo> ReadAllLines(DataLinksManager projectDlm, Action<string> trace = null)
        {
            void Trace(string msg) { try { trace?.Invoke(msg); } catch { } }
            var result = new List<LineGroupInfo>();
            if (projectDlm == null) { Trace("LineSyncService: projectDlm отсутствует."); return result; }

            PnPDatabase pnpDb;
            try { pnpDb = projectDlm.GetPnPDatabase(); }
            catch (Exception ex)
            {
                Trace($"LineSyncService: GetPnPDatabase не удался — {ex.GetType().Name}: {ex.Message}");
                return result;
            }
            if (pnpDb == null) { Trace("LineSyncService: PnPDatabase недоступна."); return result; }

            PnPTable table;
            try { table = pnpDb.Tables["P3dLineGroup"]; }
            catch (Exception ex)
            {
                Trace($"LineSyncService: таблица P3dLineGroup недоступна — {ex.GetType().Name}: {ex.Message}");
                return result;
            }
            if (table == null) { Trace("LineSyncService: таблица P3dLineGroup не найдена в проекте."); return result; }

            try { table.RefreshCachedRows(); } catch { }

            PnPRow[] rows;
            try { rows = table.Select(); }
            catch (Exception ex)
            {
                Trace($"LineSyncService: Select на P3dLineGroup не удался — {ex.GetType().Name}: {ex.Message}");
                return result;
            }
            if (rows == null) return result;

            // PnPTable.Select() has been observed returning the same
            // PnPID more than once (e.g. a tombstoned/superseded revision
            // of a row alongside its current one) — dedupe by rowId so
            // the ledger shows exactly one row per physical line, not one
            // per Select() entry. Last-write-wins on GetAllProperties'
            // return, which reflects the current row state regardless of
            // how many times its rowId appeared in rows.
            var seenRowIds = new HashSet<int>();

            foreach (var row in rows)
            {
                int rowId;
                try { rowId = Convert.ToInt32(row["PnPID"]); }
                catch (Exception ex)
                {
                    Trace($"LineSyncService: не удалось прочитать PnPID строки — {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
                if (!seenRowIds.Add(rowId)) continue;

                try
                {
                    var props = projectDlm.GetAllProperties(rowId, true);
                    if (props == null) { Trace($"LineSyncService: rowId {rowId} — GetAllProperties вернул null."); continue; }
                    var info = new LineGroupInfo { RowId = rowId };
                    foreach (var kv in props)
                    {
                        info.AllProperties[kv.Key] = kv.Value;
                        if (string.Equals(kv.Key, "ID", StringComparison.OrdinalIgnoreCase)) info.Id = kv.Value;
                        else if (string.Equals(kv.Key, "Tag", StringComparison.OrdinalIgnoreCase)) info.Tag = kv.Value;
                    }
                    result.Add(info);
                }
                catch (Exception ex)
                {
                    Trace($"LineSyncService: rowId {rowId} пропущен при чтении — {ex.GetType().Name}: {ex.Message}");
                }
            }
            return result;
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
                        "P3dLineGroupPartRelationship", "LineGroup", line.RowId, "Part");
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
                            try { return !string.Equals(r.RelationshipType?.Name, "P3dDrawingLineGroupRelationship", StringComparison.OrdinalIgnoreCase); }
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
            if (plan == null || plan.Actions.Count == 0) return errors;

            PnPDatabase pnpDb;
            try { pnpDb = projectDlm.GetPnPDatabase(); }
            catch (Exception ex) { errors.Add($"Синхронизация не начата: {ex.Message}"); return errors; }
            if (pnpDb == null) { errors.Add("PnPDatabase недоступна. Изменения не выполнялись."); return errors; }

            errors = SyncTransaction.Run(
                () => pnpDb.StartTransaction(),
                () =>
                {
                    foreach (var action in plan.Actions)
                    {
                        if (action.Changes.Count == 0) continue;
                        var names = new StringCollection();
                        var values = new StringCollection();
                        foreach (var change in action.Changes)
                        {
                            names.Add(change.PropertyName);
                            values.Add(change.NewValue);
                        }
                        try { projectDlm.SetProperties(action.Line.RowId, names, values); }
                        catch (Exception ex) { throw new InvalidOperationException($"Линия ID={action.Line.Id}: {ex.Message}", ex); }
                    }
                },
                () => projectDlm.AcceptChanges(),
                () => pnpDb.CommitTransaction(),
                () => pnpDb.RollbackTransaction());
            foreach (var error in errors)
                try { trace?.Invoke(error); } catch { }
            return errors;
        }
    }
}
