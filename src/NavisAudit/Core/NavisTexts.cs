using System.Collections.Generic;
using System.Linq;
using PipingSpecPlugin.Audit;

namespace NavisAudit.Core
{
    // The checks as the Navisworks audit does them: the rules are the Plant
    // 3D plug-in's, what they read comes from the model (NavisAuditor).
    public static class NavisTexts
    {
        public static string Describe(string id)
        {
            switch (id)
            {
                case "collisions":
                    return "Пересечения выбранных линий со всеми объектами сводной модели (Clash Detective, жёсткая проверка). Детали, соединённые порт к порту, через объект соединения или врезкой, пересечением не считаются; контакт опоры с деталью, на которой она стоит, тоже. Совпадающие копии одного элемента — «Дубликат». Пересечения не глубже заданного допуска не учитываются.";
                case "wall":
                    return "Трубы, отводы и изгибы трубы одной линии с одинаковым номинальным диаметром — одинаковая толщина стенки; замечание — на каждую пару соединённых деталей, где стенка меняется. У тройников и переходов стенка конца не тоньше соединённой с ним трубы. Соединённые детали определяются по совпадению портов в модели. Допуск 0,01 мм.";
                case "connections":
                    return "Свободные порты и раздельные участки одной линии — по совпадению точек портов деталей в модели (зазор до 10 мм — прокладка). Свободный конец может быть предусмотрен проектом или продолжаться в файле, не добавленном в сводную модель.";
                default:
                    return AuditRules.All.FirstOrDefault(r => r.Id == id)?.Description ?? id;
            }
        }

        // The Plant 3D rules speak of the drawing; in Navisworks the model
        // holds many drawings.
        public static List<AuditIssue> Evaluate(AuditSnapshot snapshot, AuditOptions options)
        {
            var issues = AuditRules.Run(snapshot, options);
            foreach (var issue in issues.Where(i => i.RuleId == "connections" && i.Message != null))
                issue.Message = issue.Message
                    .Replace("В текущем чертеже линия", "В модели линия")
                    .Replace("Соединения через другие DWG здесь не проверяются.", "Участки могут соединяться в файлах, не добавленных в модель.");
            return issues;
        }

        public static string Title(string id) => AuditRules.All.FirstOrDefault(r => r.Id == id)?.Title ?? id;

        public static string Summary(NavisModel model, AuditOptions options)
        {
            var s = model.Snapshot;
            var text = "";
            if (options.Enabled.Contains("collisions"))
                text += $"Пар объектов с пересечением геометрии: {s.CollisionPairsChecked}; не учтено — соединённых деталей: {s.ConnectedJointsIgnored}, опор на своих деталях: {s.SupportContactsIgnored}"
                    + (options.CollisionToleranceMm > 0 ? $", в пределах допуска {options.CollisionToleranceMm:0.###} мм: {s.CollisionsWithinTolerance}" : "")
                    + (options.StructureCollisions ? $", узлов металлоконструкций: {s.StructureJointsIgnored}" : "") + ". ";
            if (model.DuplicateOf.Count > 0) text += $"Копий элементов (дубликатов) в модели: {model.DuplicateOf.Count}. ";
            if (model.ProxyCount > 0) text += $"Объектов Plant 3D без свойств (прокси): {model.ProxyCount}. ";
            return text;
        }

        public static AuditReportText Report(NavisModel model, AuditOptions options) => new AuditReportText
        {
            Describe = rule => Describe(rule.Id),
            Labels = new Dictionary<string, string>
            {
                ["Plant 3D"] = "Navisworks",
                ["Чертёж"] = "Модель",
                ["Номер элемента позволяет найти объект в модели. Для внешней ссылки указан источник; в чертеже выделяется её вставка."] =
                    "Номер элемента — дескриптор объекта в DWG, при повторах — с внешней ссылкой, через которую он загружен. Источник — файл и цепочка внешних ссылок. В окне аудита «Показать в модели» выделяет элементы в Navisworks.",
            },
            Scope = "Выбранные линии модели Navisworks (детали Plant 3D со свойствами вкладки «AutoCAD»). " + Summary(model, options)
                + (options.Enabled.Contains("connections") ? "Соединения определены по совпадению портов в модели; файлы, не добавленные в сводную модель, не учитываются. " : "")
                + "Непроверенные случаи не считаются успешной проверкой. Модель при аудите не изменяется.",
        };
    }
}
