using System.Collections.Generic;
using System.Linq;

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
        public List<LineSyncAction> Actions = new();        // Matched lines with changes or diagnostics.
        public List<VtRowData> UnmatchedVtRows = new();      // Workbook rows without a project match.
        public List<LineGroupInfo> UnmatchedLines = new();   // Project lines with an ID and no workbook match.
        public List<string> DuplicateIds = new();            // Ids that matched more than one P3dLineGroup row.
        public HashSet<int> MatchedRowIds = new();
        public Dictionary<int, string> BlockedLines = new();
        public Dictionary<int, string> IdConflicts = new();
        public List<string> IdFillSkips = new();

        // "n/n" values that appear on more than one ВТ row (e.g. section
        // numbering restarts at 1 per Цех in a multi-section workbook).
        // A repeated n/n cannot identify a line on its own. A unique
        // designation can still identify the line for property sync.
        public List<string> DuplicateVtNN = new();

        public int TotalChangedFields()
        {
            int n = 0;
            foreach (var a in Actions) n += a.Changes.Count;
            return n;
        }

        /// <summary>
        /// Creates a plan containing only the actions affecting the specified line row ID(s).
        /// </summary>
        public LineSyncPlan FilterForLine(IEnumerable<int> rowIds)
        {
            var targetIds = new HashSet<int>(rowIds ?? Enumerable.Empty<int>());
            return new LineSyncPlan
            {
                Actions = Actions.Where(a => a.Line != null && targetIds.Contains(a.Line.RowId)).ToList(),
                DuplicateIds = DuplicateIds.ToList(),
                DuplicateVtNN = DuplicateVtNN.ToList(),
                UnmatchedLines = UnmatchedLines.Where(l => targetIds.Contains(l.RowId)).ToList(),
                MatchedRowIds = MatchedRowIds.Where(targetIds.Contains).ToHashSet(),
                BlockedLines = BlockedLines.Where(item => targetIds.Contains(item.Key)).ToDictionary(item => item.Key, item => item.Value),
                IdConflicts = IdConflicts.Where(item => targetIds.Contains(item.Key)).ToDictionary(item => item.Key, item => item.Value),
            };
        }

        public LineSyncPlan FilterForLine(int rowId) => FilterForLine(new[] { rowId });
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
        public int RowId { get; init; }
        public List<int> RowIds = new();
        public string Id { get; init; }
        public string Tag { get; init; }
        public bool HasProjectDuplicate { get; internal set; }
        internal bool HasVisibleDuplicate { get; set; }
        public string DisplayTag => HasVisibleDuplicate ? $"⚠ дубль ID · {Tag}" : Tag ?? "";
        public Dictionary<string, string> Values { get; } = new(System.StringComparer.OrdinalIgnoreCase);

        public IEnumerable<int> AllRowIds => RowIds != null && RowIds.Count > 0 ? RowIds : new[] { RowId };
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
