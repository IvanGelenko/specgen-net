using System;
using System.Collections.Generic;
using System.Linq;

namespace PipingSpecPlugin.Spec
{
    internal sealed class VtSyncSession(VtSyncWorkflow workflow)
    {
        VtReadResult _workbook;
        VtSyncPreview _preview;

        internal bool HasDrawing => workflow.HasDrawing;
        internal bool HasWorkbook => _workbook != null;
        internal int WorkbookRowCount => _workbook?.Rows.Count ?? 0;
        internal IReadOnlyList<string> WorkbookWarnings => _workbook?.Warnings is { } warnings ? warnings : Array.Empty<string>();
        internal IReadOnlyList<string> IdFillSkips { get; private set; } = Array.Empty<string>();
        internal LineSyncPlan Plan => _preview?.Plan;

        internal void ResetWorkbook()
        {
            _workbook = null;
            IdFillSkips = Array.Empty<string>();
            InvalidatePreview();
        }

        internal void InvalidatePreview() => _preview = null;

        internal VtReadResult LoadWorkbook(string path)
        {
            ResetWorkbook();
            var result = workflow.LoadWorkbook(path);
            if (result.Error == null && result.Rows.Count > 0)
                _workbook = result;
            return result;
        }

        internal VtSyncPreview Refresh(bool drawingOnly, bool includeIdFill = false)
        {
            InvalidatePreview();
            _preview = workflow.Prepare(_workbook?.Rows, drawingOnly, includeIdFill);
            if (includeIdFill)
                IdFillSkips = _preview.IdFillProposal?.IdFillSkips is { } skips ? skips : Array.Empty<string>();
            return _preview;
        }

        internal (bool Stale, IReadOnlyList<string> Errors) ApplyIdFill(LineSyncPlan proposal)
        {
            if (proposal == null || _workbook == null || _preview?.IdFillProposal != proposal)
                return (true, Array.Empty<string>());
            InvalidatePreview();
            return workflow.ApplyIdFill(_workbook.Rows, proposal);
        }

        internal VtApplyResult Apply(LineSyncPlan plan, Func<bool> confirmSchemaChange)
        {
            ArgumentNullException.ThrowIfNull(plan);
            var current = Plan;
            if (current == null || (!ReferenceEquals(current, plan) && !plan.Actions.All(current.Actions.Contains)))
                throw new InvalidOperationException("План синхронизации устарел. Обновите ведомость.");

            try
            {
                var result = workflow.Apply(plan, confirmSchemaChange);
                if (!result.Cancelled) InvalidatePreview();
                return result;
            }
            catch
            {
                InvalidatePreview();
                throw;
            }
        }
    }
}
