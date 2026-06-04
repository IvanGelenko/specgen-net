using Autodesk.AutoCAD.Runtime;
using PipingSpecPlugin.Spec;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// SELECTBYSTATUS — выбирает на чертеже все детали Plant 3D с заданным
    /// статусом строительства (например, «Существующее», «Новое», «Под снос»).
    /// Показывает список статусов, реально встречающихся в АКТИВНОМ чертеже, с
    /// количеством деталей; после выбора формирует штатное выделение AutoCAD.
    /// Вся работа — в <see cref="SelectByPropertyHelper"/>; здесь только выбор
    /// поля (Status) и подписи.
    /// </summary>
    public class SelectByStatusCommand
    {
        [CommandMethod("SELECTBYSTATUS")]
        public void Run()
        {
            SelectByPropertyHelper.Run(
                title: "Выбрать по статусу",
                logName: "SELECTBYSTATUS",
                valueOf: r => r.Status,
                nullKey: SpecConstants.NullStatus,
                nullLabel: SpecConstants.NullStatusLabel,
                promptText: "Выберите статус — будут выделены все детали с ним:");
        }
    }
}
