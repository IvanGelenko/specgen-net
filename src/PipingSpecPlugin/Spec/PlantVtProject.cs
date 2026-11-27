using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.ProcessPower.DataLinks;

namespace PipingSpecPlugin.Spec
{
    internal interface IVtDuplicateStore
    {
        OrphanLineInfo FindDeletableDuplicate(int rowId, string lineId);
        IReadOnlyList<string> DeleteDuplicate(OrphanLineInfo candidate);
    }

    internal sealed class PlantVtProject(DataLinksManager projectDlm, Document drawing, Action<string> trace)
        : IVtLineReader, IVtPlanWriter, IVtSchemaEditor, IVtDuplicateStore
    {
        public bool HasDrawing => drawing != null;

        public List<LineGroupInfo> ReadProjectLines() => LineSyncService.ReadAllLines(projectDlm);

        public HashSet<int> ReadDrawingLineIds()
        {
            if (!HasDrawing)
                throw new InvalidOperationException("Текущий чертёж трубопроводного проекта недоступен.");
            var rowIds = LineSyncService.ReadDrawingLineIds(projectDlm, drawing);
            trace?.Invoke($"Текущий чертёж: групп линий по деталям {rowIds.Count}.");
            return rowIds;
        }

        public bool RequiresServiceLifeColumn(LineSyncPlan plan) => LineSyncService.RequiresServiceLifeColumn(plan, projectDlm);

        public void CreateServiceLifeColumn() => LineSyncService.CreateServiceLifeColumn(projectDlm);

        public IReadOnlyList<string> Apply(LineSyncPlan plan) => LineSyncService.Apply(plan, projectDlm, trace);

        public OrphanLineInfo FindDeletableDuplicate(int rowId, string lineId)
        {
            var matches = ReadProjectLines()
                .Where(line => string.Equals(line.Id?.Trim(), lineId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var selected = matches.Find(line => line.RowId == rowId);
            return matches.Count > 1 && selected != null
                ? LineSyncService.FindOrphanLines(new List<LineGroupInfo> { selected }, projectDlm, trace).SingleOrDefault()
                : null;
        }

        public IReadOnlyList<string> DeleteDuplicate(OrphanLineInfo candidate) =>
            LineSyncService.DeleteOrphanLines(new List<OrphanLineInfo> { candidate }, projectDlm, trace);
    }
}
