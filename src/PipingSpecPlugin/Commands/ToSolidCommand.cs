using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using PipingSpecPlugin.Settings;
using PipingSpecPlugin.Solids;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PipingSpecPlugin.Commands
{
    // PSTOSOLID «В 3D-тело»: the selection becomes one 3D solid (SolidMerger),
    // its faces in the objects' colours. The originals are kept unless the
    // user says «Удалить». One command: ОТМЕНИТЬ undoes it all.
    public sealed class ToSolidCommand
    {
        const string Title = "В 3D-тело";

        [CommandMethod("PSTOSOLID", CommandFlags.Modal | CommandFlags.UsePickSet | CommandFlags.Redraw)]
        public void Run()
        {
            var document = AcadApp.DocumentManager.MdiActiveDocument;
            if (document == null) return;
            var editor = document.Editor;
            var database = document.Database;
            using var logger = new RunLogger("PSTOSOLID");
            void Say(string line) { logger.Log(line); editor.WriteMessage("\n[" + Title + "] " + line); }

            var selection = editor.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nВыберите объекты для превращения в одно 3D-тело: " });
            if (selection.Status != PromptStatus.OK || selection.Value.Count == 0) return;
            var keep = new PromptKeywordOptions("\nИсходные объекты") { AllowNone = true, AppendKeywordsToMessage = true };
            keep.Keywords.Add("Delete", "Удалить", "Удалить");
            keep.Keywords.Add("Keep", "Оставить", "Оставить");
            keep.Keywords.Default = "Keep";
            var answer = editor.GetKeywords(keep);
            if (answer.Status != PromptStatus.OK && answer.Status != PromptStatus.None) return;
            bool erase = answer.Status == PromptStatus.OK && answer.StringResult == "Delete";

            try
            {
                SolidMergeResult result;
                using (var transaction = database.TransactionManager.StartTransaction())
                {
                    result = SolidMerger.Merge(database, transaction, selection.Value.GetObjectIds(), logger.Log);
                    Say($"Выбрано объектов: {selection.Value.Count}; найдено тел: {result.Bodies}.");
                    if (result.Created.Count == 0)
                    {
                        foreach (var note in result.Notes) Say(note);
                        Alert("В выбранных объектах нет 3D-тел — объединять нечего." + (result.Notes.Count == 0 ? "" : "\n\n" + string.Join("\n", result.Notes)));
                        return;
                    }
                    transaction.Commit();
                }
                if (erase) SolidMerger.EraseOriginals(editor, result, logger.Log);
                foreach (var note in result.Notes) Say(note);
                editor.SetImpliedSelection(result.Created.ToArray());

                var message = (result.Created.Count == 1 ? "Создано одно 3D-тело" : $"Создано 3D-тел: {result.Created.Count} (часть тел не объединилась — см. журнал)")
                    + $" из {result.Bodies} тел; цветов граней: {result.FaceColors} (у исходных объектов — {result.SourceColors}). Тело на текущем слое, выделено."
                    + (erase ? $" Исходные объекты удалены: {result.Erased}." : " Исходные объекты оставлены.")
                    + " Отменить — ОТМЕНИТЬ.";
                Say(message);
                logger.Log($"Окрашено граней: {result.PaintedFaces}; не объединилось пар: {result.UniteFailures}.");
                Alert(message + (result.Notes.Count == 0 ? "" : "\n\n" + string.Join("\n", result.Notes)) + "\n\nЖурнал: " + logger.Path);
            }
            catch (OperationCanceledException ex) { Say(ex.Message + " Чертёж не изменён."); }
            catch (System.Exception ex)
            {
                logger.Log(ex.ToString());
                Say("Не выполнено: " + ex.Message + " Чертёж не изменён.");
                Alert(Title + ": " + ex.Message + "\n\nЧертёж не изменён. Журнал: " + logger.Path);
            }
        }

        // No message box in the console AutoCAD (accoreconsole): the command
        // line has it all already.
        static void Alert(string text)
        {
            if (System.Diagnostics.Process.GetCurrentProcess().ProcessName.StartsWith("accoreconsole", StringComparison.OrdinalIgnoreCase)) return;
            AcadApp.ShowAlertDialog(text);
        }
    }
}
