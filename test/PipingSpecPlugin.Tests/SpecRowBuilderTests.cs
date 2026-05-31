using System.Collections.Generic;
using System.Linq;
using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    public class SpecRowBuilderTests
    {
        // --- helpers ---------------------------------------------------

        static PartRecord Pipe(int pid, double mm, string spec = "CS150", string line = "L1")
            => new PartRecord
            {
                PnPID = pid,
                PartCategory = "Pipe",
                PnPClassName = "Pipe",
                PartSizeLongDesc = $"PIPE {pid}",
                ShortDescription = "Pipe, Seamless",
                NominalDiameter = "24",
                NominalUnit = "in",
                Material = "CS",
                Spec = spec,
                Length = mm.ToString(System.Globalization.CultureInfo.InvariantCulture),
                LengthUnit = "mm",
                LinearWeight = "140.94",
                WeightUnit = "kg/m",
                LineNumberTag = line,
                Status = "Новое",
            };

        static PartRecord Flange(int pid, string spec = "CS150", string line = "L1")
            => new PartRecord
            {
                PnPID = pid,
                PartCategory = "Flanges",
                PnPClassName = "Flange",
                PartSizeLongDesc = $"FLANGE {pid}",
                ShortDescription = "Flange",
                NominalDiameter = "4",
                NominalUnit = "in",
                Material = "CS",
                Spec = spec,
                Weight = "7.5",
                WeightUnit = "kg",
                LineNumberTag = line,
                Status = "Новое",
            };

        static PartRecord BoltSet(int pid, int numInSet, string line = "L1")
            => new PartRecord
            {
                PnPID = pid,
                PartCategory = "Fasteners",
                PnPClassName = "BoltSet",
                PartFamilyLongDesc = "MACHINE BOLT ASTM A325",
                ShortDescription = "BoltSet",
                NominalDiameter = "0.5",
                NominalUnit = "in",
                Spec = "CS150",
                Weight = "5.2",                      // total weight of the set
                WeightUnit = "kg",
                Quantity = numInSet.ToString(),
                LineNumberTag = line,
                Status = "Новое",
            };

        static PartRecord Bend(int pid, double mm, string line = "L1")
            => new PartRecord
            {
                PnPID = pid,
                PartCategory = "Pipe",     // bends carry Pipe category
                PnPClassName = "PipeBend", // distinguishing class
                PartSizeLongDesc = $"BEND {pid}",
                ShortDescription = "Bend",
                NominalDiameter = "24",
                NominalUnit = "in",
                Spec = "CS150",
                Length = mm.ToString(System.Globalization.CultureInfo.InvariantCulture),
                LengthUnit = "mm",
                LineNumberTag = line,
                Status = "Новое",
            };

        // --- tests -----------------------------------------------------

        [Fact]
        public void Pipe_Length_Aggregates_And_Rounds_Up()
        {
            // Two pipe segments of the same family/size go into one bucket;
            // qty is the sum in metres, rounded up to nearest 0.1 m, and
            // the note carries a 10 % procurement margin.
            var p1 = Pipe(1, 6445.6);    // 6.4456 m
            var p2 = Pipe(2, 3000.0);    // 3.0 m
            // Same name to share a bucket
            p2.PartSizeLongDesc = p1.PartSizeLongDesc;
            var builder = new SpecRowBuilder();
            var rows = builder.Build(new[] { p1, p2 });

            var pipeRow = Assert.Single(rows);
            Assert.Equal("Pipe", pipeRow.Category);
            Assert.Equal("м", pipeRow.Unit);
            // 6.4456 + 3.0 = 9.4456 -> ceil(94.456)/10 = 9.5
            Assert.Equal(9.5, pipeRow.Qty);
            Assert.Contains("с учетом 10% запаса:", pipeRow.Note);
        }

        [Fact]
        public void Dedup_By_PnPID_Drops_Cross_Drawing_Duplicates()
        {
            // The same part may be reported by two side-DB scans because
            // it appears in multiple drawings. Builder must count it once.
            var p1 = Pipe(7, 1000);
            var p1Again = Pipe(7, 1000);  // identical PnPID
            var builder = new SpecRowBuilder();
            var rows = builder.Build(new[] { p1, p1Again });
            Assert.Single(rows);
            // 1.0 m rounded up to 0.1 m == 1.0 m
            Assert.Equal(1.0, rows[0].Qty);
        }

        [Fact]
        public void Zero_Length_Pipe_Is_Excluded()
        {
            var ok = Pipe(1, 5000);
            var stub = Pipe(2, 0);
            stub.PartSizeLongDesc = ok.PartSizeLongDesc;
            var builder = new SpecRowBuilder();
            var rows = builder.Build(new[] { ok, stub });
            var pipe = Assert.Single(rows);
            Assert.Equal(5.0, pipe.Qty);
        }

        [Fact]
        public void Status_Filter_Includes_Only_Listed_Statuses()
        {
            var keep = Pipe(1, 1000);
            keep.Status = "Новое";
            var drop = Pipe(2, 1000);
            drop.Status = "В работе";
            drop.PartSizeLongDesc = keep.PartSizeLongDesc;
            var builder = new SpecRowBuilder(new SpecBuilderOptions
            {
                Statuses = new[] { "Новое" },
            });
            var rows = builder.Build(new[] { keep, drop });
            Assert.Single(rows);
        }

        [Fact]
        public void Status_Filter_Null_Includes_Everything()
        {
            var newR = Pipe(1, 1000);
            newR.Status = "Новое";
            var wipR = Pipe(2, 2000);
            wipR.Status = "В работе";
            // distinct names so they end up in two buckets
            wipR.PartSizeLongDesc = "Other";
            var builder = new SpecRowBuilder();  // Statuses=null => all
            var rows = builder.Build(new[] { newR, wipR });
            Assert.Equal(2, rows.Count);
        }

        [Fact]
        public void BoltSet_Quantity_Multiplies_Pieces_And_Weight_Is_Per_Unit()
        {
            // BoltSet with NumberInSet=4 weighing 5.2 kg total — should
            // land as qty=4, weight=5.2/4=1.3 kg per piece.
            var bs = BoltSet(1, numInSet: 4);
            var builder = new SpecRowBuilder();
            var rows = builder.Build(new[] { bs });
            var row = Assert.Single(rows);
            Assert.Equal("шт.", row.Unit);
            Assert.Equal(4.0, row.Qty);
            Assert.Equal(1.3, row.Weight);
        }

        [Fact]
        public void Equipment_Owned_PnPIDs_Are_Always_Dropped()
        {
            var keep = Flange(1);
            var owned = Flange(2);
            owned.PartSizeLongDesc = "Other";  // different bucket
            var builder = new SpecRowBuilder(new SpecBuilderOptions
            {
                EquipmentOwnedPnPIds = new HashSet<int> { 2 },
            });
            var rows = builder.Build(new[] { keep, owned });
            var row = Assert.Single(rows);
            Assert.Equal(1, row.Qty);
            Assert.Contains("equipment-owned", builder.DropReasons.Keys);
        }

        [Fact]
        public void ValveCompanion_Only_Dropped_When_Flag_On()
        {
            var keep = Flange(1);
            var companion = Flange(2);
            companion.PartSizeLongDesc = "Other";
            var b1 = new SpecRowBuilder(new SpecBuilderOptions
            {
                ExcludeValveCompanions = false,  // off → both pass
                ValveCompanionPnPIds = new HashSet<int> { 2 },
            });
            Assert.Equal(2, b1.Build(new[] { keep, companion }).Count);

            var b2 = new SpecRowBuilder(new SpecBuilderOptions
            {
                ExcludeValveCompanions = true,
                ValveCompanionPnPIds = new HashSet<int> { 2 },
            });
            Assert.Single(b2.Build(new[] { keep, companion }));
        }

        [Fact]
        public void Equipment_Nozzle_Is_Always_Dropped_By_Class()
        {
            // PartCategory='Nozzles' OR PnPClassName contains 'nozzle'.
            // Use distinct part names so the survivor is identifiable
            // by Name rather than PnPID (SpecRow has no PnPID column).
            var nozzle = Flange(99);
            nozzle.PartCategory = "Nozzles";
            nozzle.PartSizeLongDesc = "NOZZLE FLANGE";
            var ordinary = Flange(1);
            ordinary.PartSizeLongDesc = "ORDINARY FLANGE";
            var builder = new SpecRowBuilder();
            var rows = builder.Build(new[] { nozzle, ordinary });
            var row = Assert.Single(rows);
            Assert.Equal("ORDINARY FLANGE", row.Name);
            Assert.Contains("equipment-nozzle", builder.DropReasons.Keys);
        }

        [Fact]
        public void Welds_Surface_As_Synthetic_P3dConnector_Category()
        {
            var weld = new PartRecord
            {
                PnPID = 1,
                PartCategory = "",
                PnPClassName = "Buttweld",
                PartSizeLongDesc = "Buttweld 24in",
                Status = "Новое",
            };
            // ExcludeConnectors defaults to true in SpecBuilderOptions
            // (the production picker overrides it to false because the
            // category list now drives connector inclusion). Match that
            // production wiring here so the test exercises the category
            // synthesis rather than the back-compat keyword filter.
            var builder = new SpecRowBuilder(new SpecBuilderOptions
            {
                ExcludeConnectors = false,
            });
            var rows = builder.Build(new[] { weld });
            var row = Assert.Single(rows);
            Assert.Equal("P3dConnector", row.Category);
        }

        [Fact]
        public void PipeBend_Folds_Into_Adjacent_Pipe_When_Flag_On()
        {
            var pipe = Pipe(1, 5000);  // 5 m
            var bend = Bend(2, 1500);  // 1.5 m
            var topology = new Dictionary<int, List<int>>
            {
                { 1, new List<int> { 2 } },
                { 2, new List<int> { 1 } },
            };
            // PipeBendFolder runs against the project-wide record set
            // and hands SpecRowBuilder a pre-built fold result.
            var fold = PipeBendFolder.Fold(new[] { pipe, bend }, topology);
            var builder = new SpecRowBuilder(new SpecBuilderOptions
            {
                BendsConsumed = fold.BendsConsumed,
                ExtraPipeMetres = fold.ExtraPipeMetres,
            });
            var rows = builder.Build(new[] { pipe, bend });
            var row = Assert.Single(rows);
            // 5.0 + 1.5 = 6.5; rounded up to 0.1m stays 6.5
            Assert.Equal(6.5, row.Qty);
            Assert.Contains("pipe-bend-folded", builder.DropReasons.Keys);
        }

        [Fact]
        public void PipeBend_Stays_Standalone_When_Flag_Off()
        {
            var pipe = Pipe(1, 5000);
            var bend = Bend(2, 1500);
            bend.PartSizeLongDesc = "Different";  // different bucket
            // No bend-fold passed in => bends pass through to the
            // standard aggregation as a separate spec row.
            var builder = new SpecRowBuilder();
            Assert.Equal(2, builder.Build(new[] { pipe, bend }).Count);
        }

        [Fact]
        public void Rows_Sort_By_Category_Order()
        {
            // CategoryOrder: Equipment, Pipe, Fittings, Valves, …, Flanges, Fasteners
            // Build a flange and a pipe, expect Pipe row first in result.
            var f = Flange(1);
            var p = Pipe(2, 1000);
            var builder = new SpecRowBuilder();
            var rows = builder.Build(new[] { f, p });
            Assert.Equal("Pipe", rows[0].Category);
            Assert.Equal("Flanges", rows[1].Category);
        }

        [Fact]
        public void DisableElementGrouping_Prevents_Aggregation()
        {
            // Two identical flanges
            var f1 = Flange(1);
            var f2 = Flange(2);
            f2.PartSizeLongDesc = f1.PartSizeLongDesc; // make them identical

            // With default options (DisableElementGrouping = false), they should be merged
            var builderDefault = new SpecRowBuilder();
            var rowsDefault = builderDefault.Build(new[] { f1, f2 });
            var rowDefault = Assert.Single(rowsDefault);
            Assert.Equal(2.0, rowDefault.Qty);

            // With DisableElementGrouping = true, they should remain separate
            var builderNoGroup = new SpecRowBuilder(new SpecBuilderOptions
            {
                DisableElementGrouping = true
            });
            var rowsNoGroup = builderNoGroup.Build(new[] { f1, f2 });
            Assert.Equal(2, rowsNoGroup.Count);
            Assert.Equal(1.0, rowsNoGroup[0].Qty);
            Assert.Equal(1.0, rowsNoGroup[1].Qty);
        }

        [Fact]
        public void Rows_Sort_Within_Category_By_NominalDiameter_Ascending()
        {
            // Three flanges of different nominal sizes, distinct names so
            // they land in three separate buckets. After sorting the
            // smallest ND must come first.
            var big = Flange(1);
            big.NominalDiameter = "24"; big.NominalUnit = "in";
            big.PartSizeLongDesc = "FLANGE 24";
            var small = Flange(2);
            small.NominalDiameter = "2"; small.NominalUnit = "in";
            small.PartSizeLongDesc = "FLANGE 2";
            var mid = Flange(3);
            mid.NominalDiameter = "8"; mid.NominalUnit = "in";
            mid.PartSizeLongDesc = "FLANGE 8";

            var builder = new SpecRowBuilder();
            var rows = builder.Build(new[] { big, mid, small });

            Assert.Equal(3, rows.Count);
            Assert.Equal("FLANGE 2", rows[0].Name);
            Assert.Equal("FLANGE 8", rows[1].Name);
            Assert.Equal("FLANGE 24", rows[2].Name);
        }

        [Fact]
        public void Pipes_With_Different_Specs_But_Identical_Properties_Are_Merged()
        {
            var p1 = Pipe(1, 4000, spec: "CS300");
            var p2 = Pipe(2, 3000, spec: "CS150");
            // Same name to share a bucket
            p2.PartSizeLongDesc = p1.PartSizeLongDesc;
            var builder = new SpecRowBuilder();
            var rows = builder.Build(new[] { p1, p2 });

            var pipeRow = Assert.Single(rows);
            Assert.Equal("Pipe", pipeRow.Category);
            // 4.0 + 3.0 = 7.0 m
            Assert.Equal(7.0, pipeRow.Qty);
        }

        [Fact]
        public void Rows_Sort_Mixes_Inches_And_Millimetres_Correctly()
        {
            // A 50 mm flange and a 2-inch (50.8 mm) flange are visually
            // similar but must order by their mm equivalent — 50 first,
            // then 2". Confirms the unit conversion is applied before
            // comparison rather than the raw "NominalDiameter" string.
            var inFlange = Flange(1);
            inFlange.NominalDiameter = "2"; inFlange.NominalUnit = "in";
            inFlange.PartSizeLongDesc = "FLANGE 2in";
            var mmFlange = Flange(2);
            mmFlange.NominalDiameter = "50"; mmFlange.NominalUnit = "mm";
            mmFlange.PartSizeLongDesc = "FLANGE 50mm";
            var bigMm = Flange(3);
            bigMm.NominalDiameter = "600"; bigMm.NominalUnit = "mm";
            bigMm.PartSizeLongDesc = "FLANGE 600mm";

            var builder = new SpecRowBuilder();
            var rows = builder.Build(new[] { bigMm, inFlange, mmFlange });

            Assert.Equal(3, rows.Count);
            // 50 mm < 2 in (50.8 mm) < 600 mm
            Assert.Equal("FLANGE 50mm", rows[0].Name);
            Assert.Equal("FLANGE 2in", rows[1].Name);
            Assert.Equal("FLANGE 600mm", rows[2].Name);
        }

        [Fact]
        public void Rows_With_Zero_Or_Missing_NominalDiameter_Sort_To_End_Of_Category()
        {
            // A flange with no nominal diameter should land at the bottom
            // of the Flange category — better to break ties at the tail
            // than to claim it's the smallest item in the run.
            var sized = Flange(1);
            sized.NominalDiameter = "4"; sized.NominalUnit = "in";
            sized.PartSizeLongDesc = "FLANGE WITH SIZE";
            var noSize = Flange(2);
            noSize.NominalDiameter = ""; noSize.NominalUnit = "";
            noSize.PartSizeLongDesc = "FLANGE NO SIZE";

            var builder = new SpecRowBuilder();
            var rows = builder.Build(new[] { noSize, sized });
            Assert.Equal(2, rows.Count);
            Assert.Equal("FLANGE WITH SIZE", rows[0].Name);
            Assert.Equal("FLANGE NO SIZE", rows[1].Name);
        }

        [Fact]
        public void NominalDiameterMm_Is_Populated_On_SpecRow()
        {
            // SpecRow exposes the parsed ND in mm so downstream consumers
            // (template writer, future Excel writer) can group by size.
            var p2in = Pipe(1, 1000);
            p2in.NominalDiameter = "2"; p2in.NominalUnit = "in";

            var builder = new SpecRowBuilder();
            var row = Assert.Single(builder.Build(new[] { p2in }));
            Assert.Equal(50.8, row.NominalDiameterMm, 3);  // 2 * 25.4
        }

        [Fact]
        public void ND_Sort_Respects_LineTag_Then_Category_Then_ND()
        {
            // Two lines with mixed categories; check ordering is
            // (LineTag, CategoryOrder, ND ascending). Build with
            // groupByLine on so LineTag differs in the output.
            var lineA_pipeBig = Pipe(1, 1000, line: "L1");
            lineA_pipeBig.NominalDiameter = "24";
            lineA_pipeBig.PartSizeLongDesc = "PIPE A24";
            var lineA_pipeSmall = Pipe(2, 1000, line: "L1");
            lineA_pipeSmall.NominalDiameter = "2";
            lineA_pipeSmall.PartSizeLongDesc = "PIPE A2";
            var lineB_pipe = Pipe(3, 1000, line: "L2");
            lineB_pipe.NominalDiameter = "8";
            lineB_pipe.PartSizeLongDesc = "PIPE B8";
            var lineB_flange = Flange(4, line: "L2");
            lineB_flange.NominalDiameter = "4";
            lineB_flange.PartSizeLongDesc = "FLANGE B4";

            var builder = new SpecRowBuilder(new SpecBuilderOptions
            {
                GroupByLine = true,
            });
            var rows = builder.Build(new[]
            {
                lineB_flange, lineA_pipeBig, lineB_pipe, lineA_pipeSmall,
            });

            Assert.Equal(4, rows.Count);
            // L1: Pipe 2", Pipe 24"   (category Pipe only, sorted by ND)
            // L2: Pipe 8", Flange 4"  (Pipe before Flange per CategoryOrder)
            Assert.Equal("PIPE A2", rows[0].Name);
            Assert.Equal("PIPE A24", rows[1].Name);
            Assert.Equal("PIPE B8", rows[2].Name);
            Assert.Equal("FLANGE B4", rows[3].Name);
        }
    }
}
