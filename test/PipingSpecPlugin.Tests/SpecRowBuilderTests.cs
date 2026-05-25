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
            var builder = new SpecRowBuilder(new SpecBuilderOptions
            {
                IncludePipeBendsAsPipe = true,
                Topology = topology,
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
            var builder = new SpecRowBuilder(new SpecBuilderOptions
            {
                IncludePipeBendsAsPipe = false,
            });
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
    }
}
