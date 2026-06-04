using Autodesk.AutoCAD.Runtime;
using PipingSpecPlugin.Spec;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// SELECTBYLINE — выбирает на чертеже все детали Plant 3D с заданным
    /// номером линии (LineNumberTag). Показывает список номеров линий,
    /// реально встречающихся в АКТИВНОМ чертеже, с количеством деталей; после
    /// выбора формирует штатное выделение AutoCAD. Вся работа — в
    /// <see cref="SelectByPropertyHelper"/>; здесь только выбор поля
    /// (LineNumberTag) и подписи.
    /// </summary>
    public class SelectByLineCommand
    {
        [CommandMethod("SELECTBYLINE")]
        public void Run()
        {
            SelectByPropertyHelper.Run(
                title: "Выбрать по номеру линии",
                logName: "SELECTBYLINE",
                valueOf: r => r.LineNumberTag,
                nullKey: SpecConstants.NullLine,
                nullLabel: SpecConstants.NullLineLabel,
                promptText: "Выберите номер линии — будут выделены все детали с ним:");
        }
    }
}
