using Autodesk.AutoCAD.Runtime;
using PipingSpecPlugin.Layers;
using PipingSpecPlugin.Settings;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PipingSpecPlugin.Commands
{
    public sealed class ColorByLayerCommand
    {
        [CommandMethod("PSCOLORBYLAYER", CommandFlags.Modal)]
        public void Run()
        {
            var document = AcadApp.DocumentManager.MdiActiveDocument;
            if (document == null) return;
            using var logger = new RunLogger("PSCOLORBYLAYER");
            try
            {
                var catalog = AuditLineCatalog.Read(document, logger.Log);
                var result = LayerColorService.Apply(document, catalog.Parts);
                foreach (var warning in result.Warnings) logger.Log(warning);
                document.Editor.Regen();
                var message = $"Изменён цвет объектов: {result.Changed}. Клапанов с цветом слоя линии: {result.ValveLineColors}." +
                    $"\nПредупреждений: {result.Warnings.Count}. Слои объектов сохранены. Изменения можно отменить командой ОТМЕНИТЬ.";
                if (result.Warnings.Count > 0) message += "\n\n" + string.Join("\n", result.Warnings.GetRange(0, System.Math.Min(8, result.Warnings.Count))) + "\n\nПолный журнал: " + logger.Path;
                document.Editor.WriteMessage("\n[Цвет по слою] " + message);
                AcadApp.ShowAlertDialog(message);
            }
            catch (System.Exception ex)
            {
                logger.Log(ex.ToString());
                AcadApp.ShowAlertDialog("Цвет по слою: " + ex.Message);
            }
        }
    }
}
