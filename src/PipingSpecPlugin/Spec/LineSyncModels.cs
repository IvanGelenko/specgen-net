using System.Collections.Generic;

namespace PipingSpecPlugin.Spec
{
    /// <summary>One P3dLineGroup row as seen in the live Plant 3D project.</summary>
    public class LineGroupInfo
    {
        public int RowId;
        public string Id;   // custom "ID" column — the sync key.
        public string Tag;  // system "Tag" (= "Идентификатор") — display only.

        // Every property GetAllProperties returned for this row, keyed by
        // property name (case-insensitive). Populated by
        // LineSyncService.ReadAllLines so the review window can render a
        // full "внутренняя ведомость" table (every column, not just the
        // ones СИНХРОНИЗАЦИЯ ВТ touches) without a second DLM round-trip.
        public System.Collections.Generic.Dictionary<string, string> AllProperties = new(System.StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One planned field change: current value in Plant 3D vs. the value
    /// from ВТ that would replace it. Shown to the user before write.
    /// </summary>
    public class FieldChange
    {
        public string PropertyName;   // e.g. "Pwork"
        public string DisplayLabel;   // e.g. "Давление рабочее, МПа"
        public string OldValue;
        public string NewValue;
    }

    /// <summary>
    /// Everything that would happen to one P3dLineGroup row for one ВТ
    /// row's Id. Multiple LineActions can share the same VtRow when a
    /// single ВТ cell listed several line tags.
    /// </summary>
    public class LineSyncAction
    {
        public VtRowData VtRow;
        public LineGroupInfo Line;   // null if the Id from ВТ wasn't found in the project.
        public List<FieldChange> Changes = new();
        public List<string> PicklistRejections = new(); // "field: text value has no matching picklist code"
    }

    public class LineSyncPlan
    {
        public List<LineSyncAction> Actions = new();        // Id matched to a line, at least one change.
        public List<VtRowData> UnmatchedVtRows = new();      // Id in ВТ not found among project lines.
        public List<LineGroupInfo> UnmatchedLines = new();   // Lines with an Id not present in ВТ.
        public List<string> DuplicateIds = new();            // Ids that matched more than one P3dLineGroup row.

        // "n/n" values that appear on more than one ВТ row (e.g. section
        // numbering restarts at 1 per Цех in a multi-section workbook).
        // Every ВТ row sharing such a value is ambiguous — it's unclear
        // which one a given Plant 3D line's ID should match — so all of
        // them are skipped entirely (added to UnmatchedVtRows) rather
        // than guessed at. Populated by BuildPlan.
        public List<string> DuplicateVtNN = new();

        public int TotalChangedFields()
        {
            int n = 0;
            foreach (var a in Actions) n += a.Changes.Count;
            return n;
        }
    }

    /// <summary>
    /// One row of the "внутренняя ведомость" review table: a single
    /// P3dLineGroup line with every property СИНХРОНИЗАЦИЯ ВТ knows how
    /// to write, laid out for a wide spreadsheet-style grid (one row per
    /// line, one column per property) rather than the old one-row-per-
    /// changed-field diff view.
    /// </summary>
    public class LineLedgerRow
    {
        public int RowId;
        public string Id;
        public string Tag;
        public System.Collections.Generic.Dictionary<string, string> Values = new(System.StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A P3dLineGroup row with a non-blank Id but zero linked parts in
    /// any relationship type — a candidate for deletion. Confirmed
    /// orphan status has already passed both checks in
    /// LineSyncService.FindOrphanLines: the direct P3dLineGroupPartRelationship
    /// AND PnPDatabase.SelectRowRelationships (every relationship type,
    /// not just that one) came back empty.
    /// </summary>
    public class OrphanLineInfo
    {
        public LineGroupInfo Line;
    }
}
