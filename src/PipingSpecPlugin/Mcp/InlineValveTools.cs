using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.ProcessPower.P3dProjectParts;
using Autodesk.ProcessPower.PartsRepository;
using Autodesk.ProcessPower.PnP3dObjects;

namespace Plant3dMcp
{
    static partial class PlantTools
    {
        static object InsertFlangedValveOnPipe(JsonElement args, string handle, Document doc, PipingProject piping,
            SpecManager manager, string spec, string key, double dn, SpecPart valve, string description)
        {
            var dlm = piping.DataLinksManager;
            var host = IdFromHandle(doc.Database, handle);
            var ports = PortsOf(doc, host);
            if (ports.Count != 2 || !ports.Any(p => p.name == "S1") || !ports.Any(p => p.name == "S2"))
                throw new InvalidOperationException("Choose a straight pipe with two static ports and no branch or support attachments.");
            var start = ports.Single(p => p.name == "S1").pos;
            var end = ports.Single(p => p.name == "S2").pos;
            var direction = (end - start).GetNormal();
            var length = start.DistanceTo(end);
            double fraction = Tools.OptionalNumber(args, "fraction") ?? .5;
            if (!double.IsFinite(fraction) || fraction <= 0 || fraction >= 1)
                throw new ArgumentException("fraction must be between 0 and 1.");
            double boltLength = CatalogPositiveNumber(args, "bolt_length");
            string facing = ValveVariant(description ?? "");
            if (facing.Length == 0)
                throw new InvalidOperationException("Cannot determine the flange execution of this valve.");
            if (!new[] { "S1", "S2" }.All(p => string.Equals(EndCond(valve, p), "FL", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Both valve ports must be flanged.");

            SpecPart pipePart;
            double outsideDiameter;
            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var pipe = tr.GetObject(host, OpenMode.ForRead) as Pipe ??
                    throw new ArgumentException("pipe_handle must identify a Plant pipe.");
                pipePart = new SpecPart();
                pipePart.AssignFrom(pipe.PartSizeProperties);
                outsideDiameter = pipe.OuterDiameter;
            }
            if (!double.TryParse(Convert.ToString(Prop(pipePart, "NominalDiameter"), System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double hostDn) || Math.Abs(hostDn - dn) > .001)
                throw new InvalidOperationException("Valve DN must match the host pipe DN.");
            int hostRow = RowId(dlm, host);
            var groups = new List<int>();
            var related = dlm.GetRelatedRowIds("P3dLineGroupPartRelationship", "Part", hostRow, "LineGroup");
            if (related != null) foreach (int group in related) groups.Add(group);
            int[] distinctGroups = groups.Distinct().ToArray();
            if (distinctGroups.Length != 1)
                throw new InvalidOperationException("Host pipe must belong to exactly one line group; found " + distinctGroups.Length + ".");
            int lineGroup = distinctGroups[0];
            string hostHandle = host.Handle.ToString();
            var ends = ConnList(doc, host);
            var downstream = ends.Where(link => (link.h1 == hostHandle && link.p1 == "S2") ||
                (link.h2 == hostHandle && link.p2 == "S2")).ToArray();
            if (downstream.Length > 1) throw new InvalidOperationException("The downstream end has multiple links.");
            var previous = downstream.SingleOrDefault();
            ObjectId oldJoint = downstream.Length == 0 ? ObjectId.Null :
                IdFromHandle(doc.Database, previous.h1 == hostHandle ? previous.h2 : previous.h1);
            string oldJointPort = previous.h1 == hostHandle ? previous.p2 : previous.p1;
            if (!oldJoint.IsNull && oldJoint.ObjectClass.Name != "AcPpDb3dConnector")
                throw new InvalidOperationException("The downstream connection must be a Plant connector.");

            var flange = PickByVariant(CollectParts(manager, spec, key, "Flange", dn), "flange", facing, FlangeVariant, null);
            var hardware = PickFlangeHardware(manager, spec, key, dn, flange, CatalogString(args, "gasket_type"));
            var center = start + direction * (length * fraction);
            var plan = new { pipe_handle = handle, valve = description, flange = DescOf(flange),
                gasket = DescOf(hardware.gasket), bolts = DescOf(hardware.bolts), bolt_length = boltLength,
                line_group = lineGroup, center = Arr(center), pipe_length = length };
            if (!args.TryGetProperty("dry_run", out var dry) || dry.GetBoolean())
                return new { dry_run = true, plan };

            using var documentLock = doc.LockDocument();
            var before = ModelRows(doc, dlm);
            var added = new HashSet<int>();
            var log = new List<string>();
            var undo = new Stack<Action>();
            try
            {
                using var adder = new PipingObjectAdder(piping.DataLinksManager3d, doc.Database);
                adder.LineGroupId = lineGroup;
                bool WeldEnd(string value) => !string.Equals(value, "FL", StringComparison.OrdinalIgnoreCase);
                double flangeLength = MeasureInline(doc, adder, dlm, flange, "flange", log, added, WeldEnd);
                double valveLength = MeasureInline(doc, adder, dlm, valve, "valve", log, added);
                double gasketLength = GasketThickness(hardware.gasket, 4.5);
                double halfLength = flangeLength + gasketLength + valveLength / 2;
                double minimum = DesignRules.WeldSpacingMin(outsideDiameter, WallOf(pipePart));
                if (length * fraction - halfLength < minimum || length * (1 - fraction) - halfLength < minimum)
                    throw new InvalidOperationException("The valve assembly would leave too little straight pipe between welds.");

                var left = PlaceInline(doc, adder, dlm, flange, "upstream flange", center - direction * halfLength, direction, log, added, WeldEnd);
                var gate = PlaceInline(doc, adder, dlm, valve, "valve", left.PortB + direction * gasketLength, direction, log, added);
                var right = PlaceInline(doc, adder, dlm, flange, "downstream flange", gate.PortB + direction * gasketLength, direction, log, added, value => !WeldEnd(value));
                int downstreamRow = AddPipe(adder, pipePart, right.PortB, end, outsideDiameter);
                added.Add(downstreamRow);
                var tail = FindByRow(doc, dlm, downstreamRow);

                ObjectId Weld(Point3d at) => FindByRow(doc, dlm, AddConnector(adder, "Buttweld", at, direction.Negate(),
                    Perp(direction), Math.PI / 60, new List<(SubPart, PartSizeProperties)>
                    { (new WeldSubPart(), MakeWeldProps("Buttweld", "Buttweld", dn, spec, log)) }, log, added));
                ObjectId Joint(Point3d at)
                {
                    var gasket = new BlockSubPart { SymbolId = ContentManager.GetContentManager().GetSymbol(hardware.gasket, doc.Database) };
                    var bolts = new BoltSetSubPart { Length = boltLength, Offset = 0 };
                    return FindByRow(doc, dlm, AddConnector(adder, "Flanged FL1-FL1 (RUS)", at, direction.Negate(),
                        Perp(direction), Math.PI / 1800, new List<(SubPart, PartSizeProperties)>
                        { (gasket, hardware.gasket), (bolts, hardware.bolts) }, log, added));
                }
                void Join(ObjectId connector, ObjectId previousPart, ObjectId nextPart)
                {
                    ConnectCore(doc, connector, nextPart, true, "S1");
                    ConnectCore(doc, connector, previousPart, true, "S2");
                }

                Join(Joint(gate.PortA), left.Id, gate.Id);
                Join(Joint(right.PortA), gate.Id, right.Id);
                Join(Weld(right.PortB), right.Id, tail);
                var firstWeld = Weld(left.PortA);
                ConnectCore(doc, firstWeld, left.Id, true, "S1");

                if (!oldJoint.IsNull)
                {
                    Disconnect(doc, host, oldJoint);
                    undo.Push(() => ConnectCore(doc, oldJoint, host, true, oldJointPort));
                }
                SetPipeEnd(doc, host, false, left.PortA);
                undo.Push(() => SetPipeEnd(doc, host, false, end));
                ConnectCore(doc, firstWeld, host, true, "S2");
                undo.Push(() => Disconnect(doc, firstWeld, host));
                if (!oldJoint.IsNull)
                {
                    ConnectCore(doc, oldJoint, tail, true, oldJointPort);
                    undo.Push(() => Disconnect(doc, oldJoint, tail));
                }
                foreach (int row in new[] { left.Row, gate.Row, right.Row, downstreamRow })
                {
                    var rowGroups = dlm.GetRelatedRowIds("P3dLineGroupPartRelationship", "Part", row, "LineGroup");
                    bool assigned = false;
                    if (rowGroups != null) foreach (int group in rowGroups) assigned |= group == lineGroup;
                    if (!assigned) throw new InvalidOperationException("Plant did not assign the new part to the host line.");
                }
                return new { dry_run = false, plan, valve_handle = gate.Id.Handle.ToString(),
                    downstream_pipe = tail.Handle.ToString(), flanges = new[] { left.Id.Handle.ToString(), right.Id.Handle.ToString() } };
            }
            catch (System.Exception failure)
            {
                while (undo.TryPop(out var restore))
                {
                    try { restore(); }
                    catch (System.Exception error) { log.Add("Restore: " + error.Message); }
                }
                string cleanup = RollbackSince(doc, dlm, added, before);
                throw new InvalidOperationException(failure.Message + " Rollback: " + cleanup + " " + string.Join("; ", log), failure);
            }
        }
    }
}
