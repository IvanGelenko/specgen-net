using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.PlantInstance;
using Autodesk.ProcessPower.ProjectManager;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// Iteration 2: prove the Plant 3D managed pipeline resolves at runtime.
    /// PlantApplication.CurrentProject is the static entry point — when a
    /// Plant 3D project is open it returns the live PlantProject; otherwise
    /// null (e.g. when a stock AutoCAD drawing is loaded without Plant 3D).
    /// We dump just enough of it to confirm the binding works before wiring
    /// up the real spec-generation flow.
    /// </summary>
    public class GenSpecCommand
    {
        [CommandMethod("GENSPEC")]
        public void Run()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var editor = doc?.Editor;
            if (editor == null) return;

            PlantProject project;
            try
            {
                project = PlantApplication.CurrentProject;
            }
            catch (System.Exception ex)
            {
                // A type-load failure here means one of the PnP* DLLs didn't
                // resolve — surface it as plain text instead of letting the
                // command crash out.
                editor.WriteMessage("\n[GENSPEC] Plant 3D managed API not available: "
                    + ex.GetType().Name + ": " + ex.Message);
                return;
            }

            if (project == null)
            {
                editor.WriteMessage("\n[GENSPEC] No Plant 3D project is currently open.");
                return;
            }

            var drawingPath = doc.Name;  // empty for an unsaved drawing
            editor.WriteMessage("\n[GENSPEC] Active project:");
            editor.WriteMessage("\n    Name        : " + project.Name);
            editor.WriteMessage("\n    Folder      : " + project.ProjectFolderPath);
            editor.WriteMessage("\n    Project file: " + project.FileName);
            editor.WriteMessage("\n    Drawing     : "
                + (string.IsNullOrEmpty(drawingPath) ? "<unsaved>" : drawingPath));
        }
    }
}
