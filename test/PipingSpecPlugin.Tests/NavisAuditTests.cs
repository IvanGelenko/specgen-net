using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using NavisAudit.Core;
using PipingSpecPlugin.Audit;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    // The Navisworks audit (src/NavisAudit): how Plant 3D objects read from a
    // Navisworks model become the AuditSnapshot the Plant 3D rules run on, and
    // which Clash Detective results are clashes. Property names and values
    // are those of the 0873 project's NWC (Plant 3D Object Enabler, Russian).
    public class NavisAuditTests
    {
        static NavisItem Part(string key, string cls, string line, string size, string wall, double[] box, params double[][] snaps)
        {
            var item = new NavisItem { Key = key, Handle = key, Source = "Врезки.dwg", DisplayClass = "ACPPPIPEINLINEASSET", Box = box };
            item.Props["PnPClassName"] = cls;
            if (line != null) item.Props["LineNumberTag"] = line;
            if (size != null) item.Props["Size"] = size;
            if (wall != null) item.Props["WallThickness"] = wall;
            item.Props["PnPMaterial"] = "20";
            item.Props["Порт1_LengthUnit"] = "mm";
            foreach (var snap in snaps) item.Snaps.Add(snap);
            if (cls == "Pipe") item.DisplayClass = "ACPPPIPE";
            return item;
        }

        static void Position(NavisItem item, double x, double y, double z)
        {
            item.Props["Положение по оси X (порт 1)"] = x.ToString(System.Globalization.CultureInfo.InvariantCulture);
            item.Props["Положение по оси Y (порт 1)"] = y.ToString(System.Globalization.CultureInfo.InvariantCulture);
            item.Props["Отметка уровня COP (порта 1)"] = z.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        static double[] P(double x, double y, double z) => new[] { x, y, z };
        static double[] Box(double x0, double y0, double z0, double x1, double y1, double z1) => new[] { x0, y0, z0, x1, y1, z1 };

        // Pipe A 0..1000 → elbow at 1000..1225 (corner 1225,0) → pipe B up y.
        static List<NavisItem> Run()
        {
            var a = Part("A", "Pipe", "L1", "150", "6", Box(0, -80, -80, 1000, 80, 80), P(0, 0, 0), P(1000, 0, 0));
            Position(a, 0, 0, 0);
            var e = Part("E", "Elbow", "L1", "150", "6", Box(1000, -80, -80, 1305, 225, 80), P(1225, 0, 0), P(1000, 0, 0), P(1225, 225, 0));
            Position(e, 1225, 0, 0); // the corner, also a snap point
            var b = Part("B", "Pipe", "L1", "150", "6", Box(1145, 225, -80, 1305, 2225, 80), P(1225, 225, 0), P(1225, 2225, 0));
            Position(b, 1225, 225, 0);
            return new List<NavisItem> { a, e, b };
        }

        [Fact]
        public void PortsMeetingAtOnePointJoinTheParts()
        {
            var model = SnapshotBuilder.Build("m.nwd", Run());
            Assert.Contains("E", model.Parts["A"].Neighbours);
            Assert.Contains("E", model.Parts["B"].Neighbours);
            Assert.True(model.Joined("A", "E"));
            Assert.False(model.Joined("A", "B"));
            // The elbow's corner is its insertion point, not a port.
            Assert.All(model.Parts["E"].Ports, p => Assert.True(p.Connected));
            Assert.Equal(2, model.Parts["E"].Ports.Count);
        }

        [Fact]
        public void FreeEndsAreNamedByWhereTheyAre()
        {
            var model = SnapshotBuilder.Build("m.nwd", Run());
            var options = new AuditOptions();
            options.Enabled.Add("connections");
            var issues = AuditRules.Run(model.Snapshot, options);
            var free = issues.Where(i => i.Message.StartsWith("Свободный порт")).ToList();
            Assert.Equal(2, free.Count);
            Assert.Contains(free, i => i.Message.Contains("конец X 0 Y 0 Z 0") && i.Keys.Single() == "A");
            Assert.Contains(free, i => i.Message.Contains("конец X 1225 Y 2225 Z 0") && i.Keys.Single() == "B");
            Assert.DoesNotContain(issues, i => i.Message.Contains("несвязанных"));
        }

        [Fact]
        public void ASplitLineIsReportedForTheModelNotADrawing()
        {
            var a = Part("A", "Pipe", "L1", "150", "6", Box(0, -80, -80, 1000, 80, 80), P(0, 0, 0), P(1000, 0, 0));
            var b = Part("B", "Pipe", "L1", "150", "6", Box(5000, -80, -80, 6000, 80, 80), P(5000, 0, 0), P(6000, 0, 0));
            var model = SnapshotBuilder.Build("m.nwd", new[] { a, b });
            var options = new AuditOptions();
            options.Enabled.Add("connections");
            var split = Assert.Single(NavisTexts.Evaluate(model.Snapshot, options), i => i.Message.Contains("несвязанных"));
            Assert.StartsWith("В модели линия состоит из 2 несвязанных участков", split.Message);
            Assert.DoesNotContain("DWG", split.Message);
        }

        [Fact]
        public void AGasketGapJoinsAFlangedJointButAWiderGapDoesNot()
        {
            var a = Part("A", "Flange", "L1", "150", null, Box(0, -140, -140, 60, 140, 140), P(0, 0, 0), P(60, 0, 0));
            var b = Part("B", "Valve", "L1", "150", null, Box(63, -140, -140, 400, 140, 140), P(63, 0, 0), P(400, 0, 0));
            var c = Part("C", "Flange", "L1", "150", null, Box(415, -140, -140, 475, 140, 140), P(415, 0, 0), P(475, 0, 0));
            var model = SnapshotBuilder.Build("m.nwd", new[] { a, b, c });
            Assert.True(model.Joined("A", "B"));
            Assert.False(model.Joined("B", "C"));
        }

        [Fact]
        public void AStubInIsAJointOnlyWithItsWeld()
        {
            var header = Part("H", "Pipe", "L1", "300", "8", Box(0, -162, -162, 3000, 162, 162), P(0, 0, 0), P(3000, 0, 0));
            var branch = Part("BR", "Pipe", "L2", "50", "4", Box(1472, 150, -28, 1528, 1000, 28), P(1500, 155, 0), P(1500, 1000, 0));
            var model = SnapshotBuilder.Build("m.nwd", new[] { header, branch });
            Assert.Contains("H", model.Parts["BR"].Neighbours);
            Assert.False(model.Joined("BR", "H"));
            // Its overlap is a question, not a certain clash.
            var options = new AuditOptions();
            options.Enabled.Add("collisions");
            ClashClassifier.Apply(model, new[] { new RawClash { KeyA = "BR", KeyB = "H", DepthMm = 1.6 } }, new Dictionary<string, AuditPart>(), options);
            var issue = Assert.Single(model.Snapshot.GeometryIssues);
            Assert.Equal("Проверить", issue.Severity);
            Assert.StartsWith("Врезка:", issue.Message);

            var weld = new NavisItem { Key = "W", Handle = "W", Source = "Врезки.dwg", DisplayClass = "ACPPCONNECTOR" };
            weld.Props["PnPClassName"] = "P3dConnector";
            weld.Props["Крепеж1_Имя класса"] = "TapWeld";
            weld.Props["Крепеж1_COG X"] = "1500"; weld.Props["Крепеж1_COG Y"] = "156"; weld.Props["Крепеж1_COG Z"] = "0";
            model = SnapshotBuilder.Build("m.nwd", new[] { header, branch, weld });
            Assert.True(model.Joined("BR", "H"));
            Assert.Contains("W", model.Parts["H"].Neighbours);
        }

        [Fact]
        public void AStubInWeldWithoutAPositionCountsOnlyIfThereIsOnePerStubIn()
        {
            NavisItem Weld(string key)
            {
                var weld = new NavisItem { Key = key, Handle = key, Source = "Врезки.dwg", DisplayClass = "ACPPCONNECTOR" };
                weld.Props["PnPClassName"] = "P3dConnector";
                weld.Props["Крепеж1_Имя класса"] = "TapWeld";
                weld.Props["Крепеж1_LineNumberTag"] = "L2";
                weld.Props["Крепеж1_NominalDiameter"] = "50";
                return weld;
            }
            var header = Part("H", "Pipe", "L1", "300", "8", Box(0, -162, -162, 3000, 162, 162), P(0, 0, 0), P(3000, 0, 0));
            var one = Part("B1", "Pipe", "L2", "50", "4", Box(472, 150, -28, 528, 1000, 28), P(500, 155, 0), P(500, 1000, 0));
            var two = Part("B2", "Pipe", "L2", "50", "4", Box(1472, 150, -28, 1528, 1000, 28), P(1500, 155, 0), P(1500, 1000, 0));
            var model = SnapshotBuilder.Build("m.nwd", new[] { header, one, two, Weld("W1") });
            Assert.False(model.Joined("B1", "H"));
            Assert.False(model.Joined("B2", "H"));
            model = SnapshotBuilder.Build("m.nwd", new[] { header, one, two, Weld("W1"), Weld("W2") });
            Assert.True(model.Joined("B1", "H"));
            Assert.True(model.Joined("B2", "H"));
        }

        [Fact]
        public void APartTwiceInOnePlaceIsADuplicate()
        {
            var items = Run();
            var copy = Part("A#2", "Pipe", "L1", "150", "6", Box(0, -80, -80, 1000.2, 80, 80), P(0, 0, 0), P(1000, 0, 0));
            items.Add(copy);
            var model = SnapshotBuilder.Build("m.nwd", items);
            Assert.Equal("A", model.DuplicateOf["A#2"]);
            Assert.False(model.Parts.ContainsKey("A#2"));
            var issue = Assert.Single(model.Snapshot.GeometryIssues);
            Assert.Equal("Дубликат", issue.Severity);
            Assert.Equal(new[] { "A", "A#2" }, issue.Keys);
            // The copy does not hide the free end of the original.
            Assert.Contains(model.Parts["A"].Ports, p => p.Connected == false);
        }

        [Fact]
        public void ASupportStandsOnThePartUnderItsInsertionPoint()
        {
            var items = Run();
            var support = Part("S", "Support", "L1", "150", null, Box(400, -60, -300, 500, 60, -80), P(450, 0, -300));
            Position(support, 450, 0, 0);
            items.Add(support);
            var model = SnapshotBuilder.Build("m.nwd", items);
            Assert.Contains("A", model.Parts["S"].SupportedPartKeys);
            Assert.True(SupportContactPolicy.IgnoreContact(model.Parts["S"], model.Parts["A"]));
            Assert.True(model.Parts["S"].Ports.Single().Connected);
        }

        [Fact]
        public void PropertyCoordinatesFollowAModelMovedOnAppend()
        {
            // Appended 10 m to the east: the snaps moved, the properties did not.
            var items = Run();
            foreach (var item in items)
            {
                for (int i = 0; i < item.Snaps.Count; i++) item.Snaps[i] = new[] { item.Snaps[i][0] + 10000, item.Snaps[i][1], item.Snaps[i][2] };
                item.Box = new[] { item.Box[0] + 10000, item.Box[1], item.Box[2], item.Box[3] + 10000, item.Box[4], item.Box[5] };
            }
            var support = Part("S", "Support", "L1", "150", null, Box(10400, -60, -300, 10500, 60, -80), P(10450, 0, -300));
            Position(support, 450, 0, 0);
            items.Add(support);
            var model = SnapshotBuilder.Build("m.nwd", items);
            Assert.Empty(model.UnplacedSources);
            Assert.Contains("A", model.Parts["S"].SupportedPartKeys);
            // The elbow's corner is recognised after the shift too.
            Assert.All(model.Parts["E"].Ports, p => Assert.True(p.Connected));
        }

        [Fact]
        public void AReducerEndTakesTheSizeOfItsOutsideDiameter()
        {
            var reducer = Part("R", "Reducer", "L1", "150x80", "6", Box(0, -80, -80, 130, 80, 80), P(0, 0, 0), P(130, 0, 0));
            reducer.Props["Порт1_MatchingPipeOd"] = "159"; reducer.Props["Порт1_Толщина стенки"] = "6"; reducer.Props["Порт1_Название порта"] = "S1";
            reducer.Props["Порт2_MatchingPipeOd"] = "89"; reducer.Props["Порт2_Толщина стенки"] = "3"; reducer.Props["Порт2_Название порта"] = "S2";
            reducer.Props["Порт2_LengthUnit"] = "mm";
            var pipe = Part("P", "Pipe", "L1", "80", "4", Box(130, -45, -45, 1130, 45, 45), P(130, 0, 0), P(1130, 0, 0));
            var model = SnapshotBuilder.Build("m.nwd", new[] { reducer, pipe });
            var ends = model.Parts["R"].Ends;
            Assert.Equal("150", ends.Single(e => e.Name == "S1").Diameter);
            Assert.Equal("80", ends.Single(e => e.Name == "S2").Diameter);
            var options = new AuditOptions();
            options.Enabled.Add("wall");
            var issue = Assert.Single(AuditRules.Run(model.Snapshot, options), i => i.Severity == "Несоответствие");
            Assert.Contains("стенка 3 мм на конце Ду 80 тоньше соединённой трубы — 4 мм", issue.Message);
        }

        [Fact]
        public void PortPropertiesAreGroupedAndTheNozzlePositionIsNotThePartsOwn()
        {
            var pump = new NavisItem { Key = "N", DisplayClass = "ACPPEQUIPMENT" };
            pump.Props["PnPClassName"] = "Pump";
            pump.Props["Штуцер1_Положение по оси X (порт 1)"] = "85825";
            pump.Props["Штуцер1_Положение по оси Y (порт 1)"] = "10075";
            pump.Props["Штуцер1_Отметка уровня COP (порта 1)"] = "460";
            pump.Props["Порт2_Толщина стенки"] = "6,5";
            Assert.Null(PlantProps.Position(pump.Props));
            Assert.Equal(new[] { 85825d, 10075, 460 }, PlantProps.Position(PlantProps.Nozzles(pump)[1]));
            Assert.Equal(6.5, PlantProps.Number(PlantProps.Ports(pump)[2]["Толщина стенки"]));
        }

        [Fact]
        public void ProxiesAreOneReadRemark()
        {
            var model = SnapshotBuilder.Build("m.nwd", new List<NavisItem>(), 3, new[] { "1", "2", "3" });
            var issue = Assert.Single(model.Snapshot.ReadIssues);
            Assert.Contains("прокси", issue.Message);
            Assert.Equal(3, issue.Keys.Count);
        }

        static (NavisModel Model, Dictionary<string, AuditPart> Others, AuditOptions Options) ClashSetup()
        {
            var model = SnapshotBuilder.Build("m.nwd", Run());
            var others = new Dictionary<string, AuditPart>
            {
                ["BEAM1"] = new AuditPart { Key = "BEAM1", SelectionKey = "BEAM1", Description = "Балка" },
                ["BEAM2"] = new AuditPart { Key = "BEAM2", SelectionKey = "BEAM2", Description = "Колонна" },
            };
            var options = new AuditOptions { SelectedLines = new HashSet<string> { "L1" } };
            options.Enabled.Add("collisions");
            return (model, others, options);
        }

        [Fact]
        public void JointsAreNotClashesButABeamThroughAPipeIs()
        {
            var (model, others, options) = ClashSetup();
            ClashClassifier.Apply(model, new[]
            {
                new RawClash { KeyA = "A", KeyB = "E", DepthMm = 3 },
                new RawClash { KeyA = "A", KeyB = "BEAM1", DepthMm = 40, Region = Box(500, -80, -80, 600, 80, 80) },
                new RawClash { KeyA = "A", KeyB = "BEAM1", DepthMm = 55 },
            }, others, options);
            Assert.Equal(1, model.Snapshot.ConnectedJointsIgnored);
            var clash = Assert.Single(model.Snapshot.GeometryIssues);
            Assert.Equal(55, clash.Magnitude);
            Assert.Contains("пересечение объёмов", clash.Message);
            Assert.Equal(new[] { "A", "BEAM1" }, clash.Keys);
        }

        [Fact]
        public void ShallowOverlapsStayWithinTheTolerance()
        {
            var (model, others, options) = ClashSetup();
            options.CollisionToleranceMm = 2;
            ClashClassifier.Apply(model, new[] { new RawClash { KeyA = "B", KeyB = "BEAM1", DepthMm = 1.5 } }, others, options);
            Assert.Empty(model.Snapshot.GeometryIssues);
            Assert.Equal(1, model.Snapshot.CollisionsWithinTolerance);
        }

        [Fact]
        public void InsulationOfOneLineIsIgnoredAndOfAnotherIsAClash()
        {
            var (model, others, options) = ClashSetup();
            ClashClassifier.Apply(model, new[] { new RawClash { KeyA = "A", KeyB = "B", InsulationA = true, DepthMm = 30 } }, others, options);
            Assert.Empty(model.Snapshot.GeometryIssues);

            var items = Run();
            items[2].Props["LineNumberTag"] = "L2";
            items[2].Box = Box(100, 100, -80, 260, 2100, 80); // apart from the elbow
            for (int i = 0; i < items[2].Snaps.Count; i++) items[2].Snaps[i] = new[] { 180d, 100 + 2000 * i, 0 };
            model = SnapshotBuilder.Build("m.nwd", items);
            // The bodies within tolerance: the insulation decides (as in Plant 3D).
            options.CollisionToleranceMm = 1;
            ClashClassifier.Apply(model, new[]
            {
                new RawClash { KeyA = "A", KeyB = "B", DepthMm = 0.5 },
                new RawClash { KeyA = "A", KeyB = "B", InsulationB = true, DepthMm = 30 },
            }, others, options);
            var clash = Assert.Single(model.Snapshot.GeometryIssues);
            Assert.Contains("с участием изоляции", clash.Message);
            Assert.Equal(30, clash.Magnitude);
        }

        [Fact]
        public void StructuresClashAmongThemselvesOnlyWhenAskedAndNotAtTheirNodes()
        {
            var (model, others, options) = ClashSetup();
            others["BEAM3"] = new AuditPart { Key = "BEAM3", SelectionKey = "BEAM3", Description = "Связь" };
            options.StructureCollisions = false;
            model.StructureEnds["BEAM1"] = new List<double[]> { P(0, 0, 3000), P(6000, 0, 3000) };
            var clashes = new[]
            {
                new RawClash { KeyA = "BEAM1", KeyB = "BEAM2", DepthMm = 100, Region = Box(5900, -100, 2900, 6100, 100, 3100) },
                new RawClash { KeyA = "BEAM3", KeyB = "BEAM1", DepthMm = 80, Region = Box(2900, -100, 2900, 3100, 100, 3100) },
            };
            ClashClassifier.Apply(model, clashes, others, options);
            Assert.Empty(model.Snapshot.GeometryIssues);

            options.StructureCollisions = true;
            ClashClassifier.Apply(model, new[] { clashes[0] }, others, options);
            Assert.Equal(1, model.Snapshot.StructureJointsIgnored);
            ClashClassifier.Apply(model, new[] { clashes[1] }, others, options);
            var issue = Assert.Single(model.Snapshot.GeometryIssues);
            Assert.True(issue.BetweenStructures);
            Assert.Contains("конструкций", issue.Message);
        }

        [Fact]
        public void ACopyClashesAsItsOriginalAndNotWithIt()
        {
            var items = Run();
            items.Add(Part("A#2", "Pipe", "L1", "150", "6", Box(0, -80, -80, 1000, 80, 80), P(0, 0, 0), P(1000, 0, 0)));
            var model = SnapshotBuilder.Build("m.nwd", items);
            var others = new Dictionary<string, AuditPart> { ["BEAM1"] = new AuditPart { Key = "BEAM1", SelectionKey = "BEAM1", Description = "Балка" } };
            var options = new AuditOptions();
            options.Enabled.Add("collisions");
            ClashClassifier.Apply(model, new[]
            {
                new RawClash { KeyA = "A", KeyB = "A#2", DepthMm = 160 },
                new RawClash { KeyA = "A#2", KeyB = "BEAM1", DepthMm = 20 },
                new RawClash { KeyA = "A", KeyB = "BEAM1", DepthMm = 20 },
            }, others, options);
            var clashes = model.Snapshot.GeometryIssues.Where(i => i.Severity == "Пересечение").ToList();
            var clash = Assert.Single(clashes);
            Assert.Equal(new[] { "A", "BEAM1" }, clash.Keys);
        }

        [Fact]
        public void ClashesOfLinesNotSelectedAreLeftOut()
        {
            var (model, others, options) = ClashSetup();
            options.SelectedLines = new HashSet<string> { "L9" };
            ClashClassifier.Apply(model, new[] { new RawClash { KeyA = "A", KeyB = "BEAM1", DepthMm = 40 } }, others, options);
            Assert.Empty(model.Snapshot.GeometryIssues);
        }

        [Fact]
        public void TheJsonReportIsValidJson()
        {
            var (model, others, options) = ClashSetup();
            ClashClassifier.Apply(model, new[] { new RawClash { KeyA = "A", KeyB = "BEAM1", DepthMm = 40, Region = Box(1, 2, 3, 4, 5, 6) } }, others, options);
            model.Snapshot.Drawing = "C:\\модель \"1\".nwd";
            var issues = AuditRules.Run(model.Snapshot, options);
            using var json = JsonDocument.Parse(NavisJsonReport.Serialize(model, options, issues));
            Assert.Equal("Navisworks", json.RootElement.GetProperty("Host").GetString());
            Assert.Equal("C:\\модель \"1\".nwd", json.RootElement.GetProperty("Drawing").GetString());
            Assert.Equal(40, json.RootElement.GetProperty("Issues")[0].GetProperty("Magnitude").GetDouble());
            using var parts = JsonDocument.Parse(NavisJsonReport.Parts(model));
            Assert.Equal(3, parts.RootElement.GetArrayLength());
        }

        [Fact]
        public void TheWordReportSpeaksOfTheNavisworksModel()
        {
            var (model, others, options) = ClashSetup();
            options.Enabled.Add("wall");
            ClashClassifier.Apply(model, new[] { new RawClash { KeyA = "A", KeyB = "BEAM1", DepthMm = 40 } }, others, options);
            var issues = AuditRules.Run(model.Snapshot, options);
            var path = Path.Combine(Path.GetTempPath(), "navis-audit-" + Guid.NewGuid().ToString("N") + ".docx");
            try
            {
                AuditDocxReport.Save(path, model.Snapshot, options, issues, NavisTexts.Report(model, options));
                using var document = WordprocessingDocument.Open(path, false);
                var text = string.Join("\n", document.MainDocumentPart.Document.Body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Select(p => p.InnerText));
                Assert.Contains("Navisworks", text);
                Assert.DoesNotContain("Plant 3D\n", text + "\n");
                Assert.Contains("Модель", text);
                Assert.Contains("Clash Detective", text);
                Assert.Contains("по совпадению портов", text);
                Assert.DoesNotContain("XCLIP", text);
            }
            finally { File.Delete(path); }
        }
    }
}
