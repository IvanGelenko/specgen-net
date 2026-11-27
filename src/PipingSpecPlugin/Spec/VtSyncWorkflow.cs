using System;
using System.Collections.Generic;
using System.Linq;

namespace PipingSpecPlugin.Spec
{
    internal interface IVtLineReader
    {
        bool HasDrawing { get; }
        List<LineGroupInfo> ReadProjectLines();
        /// <summary>Reads membership in the active drawing. Throws when HasDrawing is false.</summary>
        HashSet<int> ReadDrawingLineIds();
    }

    internal interface IVtPlanWriter
    {
        IReadOnlyList<string> Apply(LineSyncPlan plan);
    }

    internal interface IVtSchemaEditor
    {
        bool RequiresServiceLifeColumn(LineSyncPlan plan);
        void CreateServiceLifeColumn();
    }

    internal sealed record VtSyncPreview(List<LineGroupInfo> VisibleLines, List<LineGroupInfo> ProjectLines,
        LineSyncPlan Plan, LineSyncPlan IdFillProposal);

    internal sealed record VtApplyResult(bool Cancelled, IReadOnlyList<string> Errors);

    internal sealed class VtSyncWorkflow(IVtLineReader reader, IVtPlanWriter writer,
        IVtSchemaEditor schema, Func<string, VtReadResult> readWorkbook)
    {
        internal bool HasDrawing => reader.HasDrawing;

        internal VtReadResult LoadWorkbook(string path) => readWorkbook(path);

        internal VtSyncPreview Prepare(List<VtRowData> rows, bool drawingOnly, bool includeIdFill = false)
        {
            if (drawingOnly) RequireDrawing();
            var projectLines = reader.ReadProjectLines();
            var drawingLines = drawingOnly || (includeIdFill && rows != null && HasDrawing)
                ? ReadDrawingLines(projectLines) : null;
            var visibleLines = drawingOnly ? drawingLines : projectLines;
            if (rows == null) return new VtSyncPreview(visibleLines, projectLines, null, null);

            var plan = LineSyncPlanner.BuildPlan(rows, projectLines);
            if (drawingOnly)
            {
                var scoped = plan.FilterForLine(visibleLines.Select(line => line.RowId));
                scoped.UnmatchedVtRows = plan.UnmatchedVtRows;
                var visibleIds = visibleLines.Select(line => line.Id?.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                scoped.DuplicateIds.RemoveAll(id => !visibleIds.Contains(id));
                plan = scoped;
            }

            var idFill = includeIdFill && drawingLines != null
                ? LineSyncPlanner.BuildIdFillPlan(rows, drawingLines, projectLines) : null;
            return new VtSyncPreview(visibleLines, projectLines, plan, idFill);
        }

        internal (bool Stale, IReadOnlyList<string> Errors) ApplyIdFill(List<VtRowData> rows, LineSyncPlan proposal)
        {
            RequireDrawing();
            var projectLines = reader.ReadProjectLines();
            var current = LineSyncPlanner.BuildIdFillPlan(rows, ReadDrawingLines(projectLines), projectLines);
            var currentIds = current.Actions.Select(action => (action.Line.RowId, action.Changes[0].NewValue)).ToHashSet();
            if (proposal.Actions.Any(action => !currentIds.Contains((action.Line.RowId, action.Changes[0].NewValue))))
                return (true, Array.Empty<string>());
            return (false, writer.Apply(proposal));
        }

        internal VtApplyResult Apply(LineSyncPlan plan, Func<bool> confirmSchemaChange)
        {
            if (schema.RequiresServiceLifeColumn(plan))
            {
                if (!confirmSchemaChange()) return new VtApplyResult(true, Array.Empty<string>());
                schema.CreateServiceLifeColumn();
            }
            return new VtApplyResult(false, writer.Apply(plan));
        }

        void RequireDrawing()
        {
            if (!HasDrawing)
                throw new InvalidOperationException("Текущий чертёж трубопроводного проекта недоступен.");
        }

        List<LineGroupInfo> ReadDrawingLines(List<LineGroupInfo> projectLines)
        {
            var rowIds = reader.ReadDrawingLineIds();
            return projectLines.Where(line => rowIds.Contains(line.RowId)).ToList();
        }
    }
}
