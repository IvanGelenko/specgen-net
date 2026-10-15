using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    public class InsulationTakeoffTests
    {
        const string PipeDesc = "Труба 25x4 ГОСТ 8734-75/В 20 ГОСТ 8733-74";
        const string BendDesc = "ИЗГИБ ТРУБЫ: Р = 90, У = 90 град, Д1 = 100, Д2 = 100, ДЛИНА ТРУБЫ = 341.3717, " + PipeDesc;

        static int _id;

        static PartRecord Part(string cls, string desc, string line = "L-1", string length = null,
                               string thickness = "80", string category = null, string status = "Новое")
            => new PartRecord
            {
                PnPID = ++_id, SourcePnPID = _id, PnPClassName = cls,
                PartCategory = category ?? (cls == "Pipe" || cls == "PipeBend" ? "Pipe" : "Fittings"),
                PartSizeLongDesc = desc, LineNumberTag = line, Status = status, InsulationThickness = thickness,
                Length = length, LengthUnit = "mm", NominalDiameter = "20", NominalUnit = "mm",
                Material = "20", WallThickness = "4",
            };

        /// <summary>The command's pipeline: Prepare, then the spec's own builder grouped by line.</summary>
        static (List<SpecRow> rows, List<ReviewItem> review) Takeoff(IEnumerable<PartRecord> parts)
        {
            var prepared = InsulationTakeoff.Prepare(parts);
            var builder = new SpecRowBuilder(new SpecBuilderOptions
            {
                GroupByLine = true, ExcludeEquipment = false, ExcludeConnectors = false, IncludePipeBendsAsPipe = false,
            });
            var rows = builder.Build(prepared.Records);
            return (rows, prepared.ReviewItems.Concat(InsulationTakeoff.WithoutMassNotes(builder.ReviewItems)).ToList());
        }

        [Fact]
        public void Bend_Description_Is_Parsed()
        {
            var d = BendDescription.Parse(BendDesc);
            Assert.Equal(90, d.RadiusMm);
            Assert.Equal(90, d.AngleDeg);
            Assert.Equal(200, d.TailsMm);
            Assert.Equal(141.3717, d.ArcMm, 6);
            Assert.Equal(PipeDesc, d.PipeText);
            Assert.Equal("25x4", d.Size);
            Assert.Equal("ИЗГИБ ТРУБЫ 25x4, Р = 90, У = 90 град, ДЛИНА = 141.3717", d.DisplayName);
        }

        [Fact]
        public void Older_Bend_Wording_Is_Parsed_Too()
        {
            var d = BendDescription.Parse("Изгиб трубы, радиус гиба = 40 мм, угол гиба = 180°, Д1=0 мм, Д2=100мм, длина=325.7 мм, труба 25,0x4,0 ст.20");
            Assert.Equal(40, d.RadiusMm);
            Assert.Equal(180, d.AngleDeg);
            Assert.Equal(100, d.TailsMm);
            Assert.Equal(225.7, d.ArcMm, 6);
            Assert.Equal("25,0x4,0", d.Size);
            Assert.Equal("труба 25,0x4,0 ст.20", d.PipeText);
        }

        [Fact]
        public void Bend_Without_Tails_Uses_Its_Length_Or_Radius_Times_Angle()
        {
            Assert.Equal(141.3717, BendDescription.Parse("ИЗГИБ ТРУБЫ: Р = 90, У = 90 град, ДЛИНА ТРУБЫ = 141.3717, " + PipeDesc).ArcMm, 6);
            Assert.Equal(90 * Math.PI / 2, BendDescription.Parse("ИЗГИБ ТРУБЫ: Р = 90, У = 90 град, " + PipeDesc).ArcMm, 6);
            Assert.Null(BendDescription.Parse("ИЗГИБ ТРУБЫ: Д1 = 100, Д2 = 100, " + PipeDesc));
        }

        [Fact]
        public void User_Example_Tails_Go_To_The_Pipe_Bends_Are_Counted()
        {
            var (rows, review) = Takeoff(new[]
            {
                Part("Pipe", PipeDesc, length: "1000"),
                Part("PipeBend", BendDesc, length: "341.3717"),
                Part("PipeBend", BendDesc, length: "341.3717"),
            });
            Assert.Empty(review);
            Assert.Equal(2, rows.Count);
            var pipe = rows.Single(r => r.Unit == "м");
            Assert.Equal(PipeDesc, pipe.Name);
            Assert.Equal(1.4, pipe.Qty, 6);                     // 1 + 2 × (0.1 + 0.1)
            var bend = rows.Single(r => r.Unit != "м");
            Assert.Equal("ИЗГИБ ТРУБЫ 25x4, Р = 90, У = 90 град, ДЛИНА = 141.4", bend.Name);  // spec rounding
            Assert.Equal(2, bend.Qty);
            Assert.All(rows, r => Assert.Equal("L-1", r.LineTag));
        }

        [Fact]
        public void Temp_Dwg_Only_The_Insulated_Stretch_And_The_Bend_Count()
        {
            // 2 m of pipe in two parts, one bend; insulation on the bend and on 1 m.
            var (rows, review) = Takeoff(new[]
            {
                Part("Pipe", PipeDesc, length: "1000", thickness: "80"),
                Part("Pipe", PipeDesc, length: "1000", thickness: "0"),
                Part("PipeBend", BendDesc, thickness: "80"),
            });
            Assert.Empty(review);
            Assert.Equal(1.2, rows.Single(r => r.Unit == "м").Qty, 6);   // 1 + 0.1 + 0.1
            Assert.Equal(1, rows.Single(r => r.Unit != "м").Qty);
        }

        [Fact]
        public void Tails_Of_An_Insulated_Bend_Count_Even_When_The_Pipe_Is_Not_Insulated()
        {
            var (rows, _) = Takeoff(new[]
            {
                Part("Pipe", PipeDesc, length: "2000", thickness: null),
                Part("PipeBend", BendDesc, thickness: "80"),
            });
            Assert.Equal(0.2, rows.Single(r => r.Unit == "м").Qty, 6);
        }

        [Fact]
        public void Bends_With_Swapped_Tails_Are_One_Row()
        {
            var (rows, _) = Takeoff(new[]
            {
                Part("Pipe", PipeDesc, length: "500"),
                Part("PipeBend", "ИЗГИБ ТРУБЫ: Р = 90, У = 90 град, Д1 = 0, Д2 = 100, ДЛИНА ТРУБЫ = 241.3717, " + PipeDesc),
                Part("PipeBend", "ИЗГИБ ТРУБЫ: Р = 90, У = 90 град, Д1 = 100, Д2 = 0, ДЛИНА ТРУБЫ = 241.3717, " + PipeDesc),
            });
            Assert.Equal(0.7, rows.Single(r => r.Unit == "м").Qty, 6);
            Assert.Equal(2, rows.Single(r => r.Unit != "м").Qty);
        }

        [Fact]
        public void Cyrillic_And_Latin_Size_Letters_Match_The_Same_Pipe()
        {
            var (rows, _) = Takeoff(new[]
            {
                Part("Pipe", "Труба 25x4 ГОСТ 8734-75/В 20 ГОСТ 8733-74", length: "1000"),
                Part("PipeBend", "ИЗГИБ ТРУБЫ: Р = 90, У = 90 град, Д1 = 100, Д2 = 100, ДЛИНА ТРУБЫ = 341.3717, Труба 25х4 ГОСТ 8734-75/В 20 ГОСТ 8733-74"),
            });
            var pipe = Assert.Single(rows, r => r.Unit == "м");
            Assert.Equal(1.2, pipe.Qty, 6);
        }

        [Fact]
        public void Tails_Without_A_Pipe_On_The_Line_Become_Their_Own_Pipe_Row()
        {
            var (rows, _) = Takeoff(new[]
            {
                Part("Pipe", PipeDesc, line: "OTHER", length: "1000"),
                Part("PipeBend", BendDesc, line: "L-1"),
            });
            var tail = rows.Single(r => r.LineTag == "L-1" && r.Unit == "м");
            Assert.Equal(PipeDesc, tail.Name);
            Assert.Equal(0.2, tail.Qty, 6);
            Assert.Equal(1.0, rows.Single(r => r.LineTag == "OTHER").Qty, 6);
        }

        [Fact]
        public void Unparsed_Bend_Is_Listed_As_Is_And_Reviewed()
        {
            var (rows, review) = Takeoff(new[] { Part("PipeBend", "Гиб особый 25x4") });
            Assert.Equal("Гиб особый 25x4", Assert.Single(rows).Name);
            Assert.Contains("не разобрано", Assert.Single(review).Reason);
        }

        [Fact]
        public void Only_Pipes_Bends_Elbows_And_Tees_Are_Taken()
        {
            var (rows, _) = Takeoff(new[]
            {
                Part("Elbow", "Отвод 90 159x6 ГОСТ 17375-2001"),
                Part("Elbow", "Отвод 90 159x6 ГОСТ 17375-2001"),
                Part("Tee", "Тройник 159х6 ГОСТ 17376-2001"),
                Part("Reducer", "Переход К 159x8-89x6 ГОСТ 17378-2001", length: "130"),
                Part("Valve", "Задвижка DN150", category: "Valves", length: "400"),
                Part("Flange", "Фланец 150", category: "Flanges"),
            });
            Assert.Equal(2, rows.Single(r => r.Name.StartsWith("Отвод")).Qty);
            Assert.Equal(1, rows.Single(r => r.Name.StartsWith("Тройник")).Qty);
            Assert.Equal(2, rows.Count);
        }

        [Theory]
        [InlineData("80", true)] [InlineData("60.5", true)] [InlineData("0", false)]
        [InlineData("", false)] [InlineData(null, false)] [InlineData("abc", false)]
        public void Insulated_Means_Own_Thickness_Above_Zero(string thickness, bool expected)
            => Assert.Equal(expected, InsulationTakeoff.IsInsulated(new PartRecord { InsulationThickness = thickness }));

        [Fact]
        public void Part_Properties_Carry_The_Insulation_Thickness()
        {
            var r = PartPropertyReader.Build(7, new Dictionary<string, string> { ["InsulationThickness"] = "80" });
            Assert.Equal("80", r.InsulationThickness);
        }

        [Fact]
        public void Settings_Ignore_Keys_Of_Earlier_Insulation_Variants()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, "{\"SchemaVersion\":8,\"InsulationMaterials\":[{\"InsulationType\":\"H\",\"Unit\":\"м\"}],"
                    + "\"InsulationBreakdown\":\"size\",\"InsulationTypes\":[\"H\"],\"InsulationScope\":\"dwg\","
                    + "\"InsulationLines\":[\"L-1\"]}");
                var loaded = PipingSpecPlugin.Settings.PluginSettings.Load(path);
                Assert.False(loaded.LoadFailed);
                Assert.Equal("dwg", loaded.InsulationScope);
                Assert.Equal(new[] { "L-1" }, loaded.InsulationLines);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }

    public class InsulationListWriterTests
    {
        [Fact]
        public void Line_Heading_Then_Numbered_Elements_And_Review()
        {
            var rows = new List<SpecRow>
            {
                new SpecRow { LineTag = "150-KSN001", Name = "Труба 25х4 ГОСТ 8734-75", Unit = "м", Qty = 1.4 },
                new SpecRow { LineTag = "150-KSN001", Name = "ИЗГИБ ТРУБЫ 25х4, Р = 90, У = 90 град, ДЛИНА = 141.4", Unit = "шт.", Qty = 2 },
                new SpecRow { LineTag = SpecConstants.NullLine, Name = "Отвод 90 159x6", Unit = "шт.", Qty = 1 },
            };
            var review = new List<SpecRow> { new SpecRow { Name = "Гиб особый", Note = "Описание изгиба не разобрано" } };
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var output = Path.Combine(dir, "out.docx");
                InsulationListWriter.Write(output, "Проект: Тест, весь проект", rows, review);
                using var doc = WordprocessingDocument.Open(output, false);
                var lines = doc.MainDocumentPart.Document.Body.Elements<Paragraph>().Select(p => p.InnerText).ToList();
                Assert.Equal(new[]
                {
                    InsulationListWriter.DocumentTitle,
                    "Проект: Тест, весь проект",
                    "Линия 150-KSN001",
                    "1) Труба 25х4 ГОСТ 8734-75 — 1.4 м",
                    "2) ИЗГИБ ТРУБЫ 25х4, Р = 90, У = 90 град, ДЛИНА = 141.4 — 2 шт.",
                    SpecConstants.NullLineLabel,
                    "1) Отвод 90 159x6 — 1 шт.",
                    SpecConstants.ReviewSectionHeader,
                    "– Гиб особый — Описание изгиба не разобрано",
                }, lines);
                var errors = new OpenXmlValidator(FileFormatVersions.Office2010).Validate(doc)
                    .Select(e => e.Description + " @ " + e.Path?.XPath).ToList();
                Assert.Empty(errors);
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
