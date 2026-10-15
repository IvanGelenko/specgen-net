using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using PipingSpecPlugin.Audit;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    public class AuditDocxTests
    {
        [Theory]
        [InlineData(false, 1)] [InlineData(true, 1)] [InlineData(false, 5)]
        public void ReportContainsScopeFindingsAndNoTemplateTokens(bool empty, int copies)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".docx");
            var snapshot = new AuditSnapshot { Drawing = @"C:\Проект\Трубопроводы\Тестовый чертёж.dwg", XrefInstancesChecked = 2, XrefInstancesUnavailable = 1, CollisionPairsChecked = 12 };
            snapshot.Parts.Add(new AuditPart { Key = "AE4", LineKey = "080-HCD002-LS016C-HW" });
            var options = new AuditOptions(); options.Enabled.UnionWith(new[] { "wall", "connections", "collisions" });
            options.SelectedLines = new HashSet<string> { "080-HCD002-LS016C-HW" };
            var issues = new List<AuditIssue>();
            if (!empty)
            {
                issues.Add(new AuditIssue { RuleId = "collisions", Check = "Коллизии", Severity = "Пересечение", Line = "080-HCD002-LS016C-HW",
                    Message = "Пересечение изоляции трубы с опорой. Проверьте расположение опоры и толщину изоляции.", Sources = "Оборудование.dwg / Опора", Keys = new List<string> { "AE4", "A0/B1" } });
                issues.Add(new AuditIssue { RuleId = "collisions", Check = "Коллизии", Severity = "Не проверено", Message = "Внешняя ссылка недоступна.", Sources = "Конструкции.dwg" });
                issues.Add(new AuditIssue { RuleId = "connections", Check = "Соединения элементов", Severity = "Проверить", Line = "080-HCD002-LS016C-HW", Message = "Свободный порт S1. Возможен проектный конец линии.", Keys = new List<string> { "AE4" } });
            }
            issues = Enumerable.Range(0, copies).SelectMany(_ => issues).ToList();
            try
            {
                AuditDocxReport.Save(path, snapshot, options, issues);
                using (var document = WordprocessingDocument.Open(path, false))
                {
                    var text = document.MainDocumentPart.Document.InnerText;
                    Assert.DoesNotContain("{{", text);
                    Assert.Contains("080-HCD002-LS016C-HW", text);
                    Assert.Contains("одинаковым номинальным диаметром", text);
                    Assert.Contains(empty ? "Замечаний по выбранным проверкам не выявлено" : "Оборудование.dwg / Опора", text);
                    Assert.Contains(empty ? "Замечаний не зарегистрировано" : "A0/B1", text);
                    var detailTable = document.MainDocumentPart.Document.Body.Elements<DocumentFormat.OpenXml.Wordprocessing.Table>().Last();
                    Assert.Equal(Math.Max(1, issues.Count) + 1, detailTable.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>().Count());
                    var errors = new OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019).Validate(document).ToList();
                    Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " " + e.Path?.XPath).Distinct()));
                }
                var qa = Environment.GetEnvironmentVariable("AUDIT_DOCX_QA_ROOT");
                if (!string.IsNullOrWhiteSpace(qa)) File.Copy(path, Path.Combine(qa, empty ? "empty.docx" : copies > 1 ? "long.docx" : "sample.docx"), true);
            }
            finally { File.Delete(path); }
        }

        [Theory]
        [InlineData("L1", "L1", true)]
        [InlineData(" l1 ", "L1", true)]
        [InlineData("L1", "L2", false)]
        [InlineData(null, null, false)]
        [InlineData("", "", false)]
        [InlineData("L1", null, false)]
        public void InsulationOverlapIsIgnoredOnlyForSameKnownLine(string a, string b, bool expected)
        {
            Assert.Equal(expected, InsulationJointPolicy.IgnoreInsulationOverlap(
                new AuditPart { LineKey = a }, new AuditPart { LineKey = b }));
        }
    }
}
