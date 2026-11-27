using System;
using System.Collections.Generic;
using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    public class VtSyncWorkflowTests
    {
        [Fact]
        public void Prepare_Scopes_Changes_To_Drawing_And_Keeps_Project_Diagnostics()
        {
            var project = new FakeProject();
            project.Lines.Add(new LineGroupInfo { RowId = 10, Id = "1", Tag = "IN" });
            project.Lines.Add(new LineGroupInfo { RowId = 20, Id = "2", Tag = "OUT" });
            project.DrawingIds.Add(10);
            var rows = new List<VtRowData>
            {
                new() { NN = "1", Designation = "IN", WorkPressure = "2" },
                new() { NN = "2", Designation = "OUT", WorkPressure = "3" },
                new() { NN = "3", Designation = "MISSING" },
            };

            var preview = CreateWorkflow(project).Prepare(rows, drawingOnly: true);

            Assert.Equal(10, Assert.Single(preview.VisibleLines).RowId);
            Assert.Equal(2, preview.ProjectLines.Count);
            Assert.Equal(10, Assert.Single(preview.Plan.Actions).Line.RowId);
            Assert.Equal("3", Assert.Single(preview.Plan.UnmatchedVtRows).NN);
            Assert.Null(project.AppliedPlan);
        }

        [Fact]
        public void Apply_Id_Fill_Rejects_Proposal_If_Id_Became_Occupied()
        {
            var project = new FakeProject();
            project.Lines.Add(new LineGroupInfo { RowId = 10, Tag = "IN" });
            project.DrawingIds.Add(10);
            var workflow = CreateWorkflow(project);
            var rows = new List<VtRowData> { new() { NN = "1", Designation = "IN" } };
            var proposal = workflow.Prepare(rows, drawingOnly: false, includeIdFill: true).IdFillProposal;
            Assert.Single(proposal.Actions);
            project.Lines.Add(new LineGroupInfo { RowId = 20, Id = "1", Tag = "OUT" });

            Assert.True(workflow.ApplyIdFill(rows, proposal).Stale);
            Assert.Null(project.AppliedPlan);
        }

        [Fact]
        public void Apply_Id_Fill_Rejects_Transfer_If_Holder_Became_Linked()
        {
            var project = new FakeProject();
            project.Lines.Add(new LineGroupInfo { RowId = 10, Tag = "KSN009" });
            project.Lines.Add(new LineGroupInfo { RowId = 20, Id = "3", Tag = "KSN009?" });
            project.DrawingIds.Add(10);
            var holderUnlinked = true;
            var workflow = new VtSyncWorkflow(project, project, project, _ => throw new InvalidOperationException(),
                line => line.RowId == 20 && holderUnlinked);
            var rows = new List<VtRowData> { new() { NN = "3", Designation = "KSN009" } };
            var proposal = workflow.Prepare(rows, drawingOnly: true, includeIdFill: true).IdFillProposal;
            Assert.Equal(2, proposal.Actions.Count);

            holderUnlinked = false;
            Assert.True(workflow.ApplyIdFill(rows, proposal).Stale);
            Assert.Null(project.AppliedPlan);
        }

        [Fact]
        public void Apply_Plan_Requires_Consent_Before_Migration_And_Propagates_Write_Failure()
        {
            var project = new FakeProject { NeedsMigration = true, WriteErrors = new[] { "Конфликт записи" } };
            var workflow = CreateWorkflow(project);
            var plan = new LineSyncPlan();

            Assert.True(workflow.Apply(plan, () => false).Cancelled);
            Assert.False(project.Migrated);
            Assert.Null(project.AppliedPlan);

            var applied = workflow.Apply(plan, () => true);
            Assert.False(applied.Cancelled);
            Assert.Equal("Конфликт записи", Assert.Single(applied.Errors));
            Assert.True(project.Migrated);
            Assert.Same(plan, project.AppliedPlan);
        }

        [Fact]
        public void Drawing_Operations_Report_That_The_Drawing_Is_Unavailable()
        {
            var project = new FakeProject { HasDrawing = false };
            var workflow = CreateWorkflow(project);
            var rows = new List<VtRowData>();

            Assert.Contains("чертёж", Assert.Throws<InvalidOperationException>(() => workflow.Prepare(rows, drawingOnly: true)).Message);
            Assert.Contains("чертёж", Assert.Throws<InvalidOperationException>(() => workflow.ApplyIdFill(rows, new LineSyncPlan())).Message);
            Assert.Null(workflow.Prepare(rows, drawingOnly: false, includeIdFill: true).IdFillProposal);
            Assert.Equal(0, project.DrawingReads);
            Assert.Null(project.AppliedPlan);
        }

        [Fact]
        public void Session_Clears_Previous_Plan_After_A_Failed_Load_Or_Refresh()
        {
            var project = new FakeProject();
            project.Lines.Add(new LineGroupInfo { RowId = 10, Id = "1" });
            var workflow = new VtSyncWorkflow(project, project, project, path => path == "bad.xlsx"
                ? new VtReadResult { Error = "Ошибка чтения" }
                : new VtReadResult { Rows = new List<VtRowData> { new() { NN = "1", WorkPressure = "2" } } });
            var session = new VtSyncSession(workflow);

            session.LoadWorkbook("good.xlsx");
            session.Refresh(drawingOnly: false);
            var stalePlan = session.Plan;
            Assert.Single(stalePlan.Actions);
            Assert.Equal("Ошибка чтения", session.LoadWorkbook("bad.xlsx").Error);
            Assert.False(session.HasWorkbook);
            Assert.Null(session.Plan);
            Assert.Throws<InvalidOperationException>(() => session.Apply(stalePlan, () => true));
            Assert.Null(project.AppliedPlan);

            session.LoadWorkbook("good.xlsx");
            session.Refresh(drawingOnly: false);
            project.ProjectReadError = true;
            Assert.Throws<InvalidOperationException>(() => session.Refresh(drawingOnly: false));
            Assert.Null(session.Plan);
            Assert.True(session.HasWorkbook);
        }

        [Fact]
        public void Workbook_Reader_Is_Injected_And_Reports_Import_Failure_Without_Accessing_Project()
        {
            var project = new FakeProject();
            var expected = new VtReadResult { Error = "Повреждённый файл" };
            var workflow = new VtSyncWorkflow(project, project, project, path =>
            {
                Assert.Equal("test.xlsx", path);
                return expected;
            });

            Assert.Same(expected, workflow.LoadWorkbook("test.xlsx"));
            Assert.Equal(0, project.ProjectReads);
        }

        static VtSyncWorkflow CreateWorkflow(FakeProject project) =>
            new(project, project, project, _ => throw new InvalidOperationException("Unexpected workbook read."));

        sealed class FakeProject : IVtLineReader, IVtPlanWriter, IVtSchemaEditor
        {
            public List<LineGroupInfo> Lines { get; } = new();
            public HashSet<int> DrawingIds { get; } = new();
            public bool HasDrawing { get; init; } = true;
            public int ProjectReads;
            public int DrawingReads;
            public bool ProjectReadError;
            public bool NeedsMigration;
            public bool Migrated;
            public LineSyncPlan AppliedPlan;
            public IReadOnlyList<string> WriteErrors = System.Array.Empty<string>();

            public List<LineGroupInfo> ReadProjectLines()
            {
                ProjectReads++;
                if (ProjectReadError) throw new InvalidOperationException("Project read failed.");
                return new(Lines);
            }
            public HashSet<int> ReadDrawingLineIds()
            {
                DrawingReads++;
                return new(DrawingIds);
            }
            public bool RequiresServiceLifeColumn(LineSyncPlan plan) => NeedsMigration;
            public void CreateServiceLifeColumn() => Migrated = true;
            public IReadOnlyList<string> Apply(LineSyncPlan plan)
            {
                AppliedPlan = plan;
                return WriteErrors;
            }
        }
    }
}
