using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PnP3dObjects;
using Exception = System.Exception;
using Part = Autodesk.ProcessPower.PnP3dObjects.Part;
using Port = Autodesk.ProcessPower.PnP3dObjects.Port;

namespace Plant3dMcp
{
    // Joint connectors (AcPpDb3dConnector). Plant 3D keeps the joint between two parts - weld, gasket + bolts - in a connector
    // object placed between them; the layout below was measured on an assembly built by the user (reference\vent-reference.json).
    static partial class PlantTools
    {
        // Property set of a weld joint element ("Buttweld", "TapWeld") as Plant 3D stores it. How the element type is set on a bare
        // property set is not documented, so several property names are tried and the result is verified by reading the type back.
        // A typed weld element (Type "Buttweld", with its data row and PL end conditions) copied from a connector that already exists in the drawing:
        // a bare SpecPart cannot be given the element type, and an untyped weld leaves the connector's port without an end condition (Plant 3D then
        // fails to connect "" with PL). Prefers a template of the same nominal diameter.
        static SpecPart WeldTemplate(double nd, List<string> log)
        {
            var doc = ActiveDocument(); var dlm = Dlm();
            SpecPart best = null; double bestNd = -1;
            foreach (var o in PlantObjects(doc, dlm).Where(o => o.cls.Contains("Connector")))
            {
                try
                {
                    using (doc.LockDocument())
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        var c = tr.GetObject(o.id, OpenMode.ForRead) as Connector;
                        if (c != null && c.NumSubParts >= 1 && c.GetSubPart(0) is WeldSubPart ws)
                        {
                            var psp = ws.PartSizeProperties;
                            if (psp != null && psp.Type == "Buttweld" && psp.PartId > 0)
                            {
                                double pnd = 0; double.TryParse(Convert.ToString(Prop(psp, "NominalDiameter"), System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pnd);
                                if (best == null || (Math.Abs(pnd - nd) < 1e-6 && Math.Abs(bestNd - nd) > 1e-6))
                                {
                                    var w = new SpecPart(); w.AssignFrom(psp);
                                    best = w; bestNd = pnd;
                                }
                            }
                        }
                        tr.Commit();
                    }
                }
                catch (Exception) { }
                if (best != null && Math.Abs(bestNd - nd) < 1e-6) break;
            }
            if (best == null) { log.Add("no typed Buttweld connector in the drawing to copy the weld element from"); return null; }
            try { best.PartId = -1; best.PartGuid = Guid.NewGuid().ToString(); } catch (Exception e) { log.Add("weld template ids: " + e.Message); }
            if (Math.Abs(bestNd - nd) > 1e-6)
            {
                try { best.SetPropValue("NominalDiameter", nd); best.SetPropValue("Size", nd.ToString(System.Globalization.CultureInfo.InvariantCulture)); } catch (Exception) { }
                log.Add("weld template of DN" + bestNd + " used for DN" + nd);
            }
            log.Add("weld element copied from an existing connector (type " + best.Type + ")");
            return best;
        }

        static SpecPart MakeWeldProps(string type, string shortDesc, double nd, string specName, List<string> log)
        {
            var tpl = WeldTemplate(nd, log);
            if (tpl != null) return tpl;
            // probe_part_type showed that no property name sets the Type of a bare property set (it stays empty), so the element type
            // is expected to come from the joint name passed to the adder ("Tap" -> Tapweld, "Buttweld" -> Buttweld). The property set carries
            // the same data as the reference assembly's weld elements.
            var w = new SpecPart();
            var failed = new List<string>();
            void Set(string n, object v) { try { w.SetPropValue(n, v); } catch (Exception) { failed.Add(n); } }
            Set("Bolt", "1"); Set("NominalDiameter", nd); Set("NominalUnit", "mm");
            Set("PartSizeLongDesc", shortDesc); Set("ShortDescription", shortDesc); Set("Size", nd.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Set("Spec", specName); Set("Status", "New"); Set("WeightUnit", "KG"); Set("Thermo1", "Нет"); Set("Additional1", "-");
            Set("SpecRecordId", -1); Set("Shop_Field", "SHOP");
            log.Add("weld property set '" + type + "': " + w.PropCount + " properties" + (failed.Count > 0 ? ", rejected: " + string.Join(",", failed) : ""));
            return w;
        }

        // Read-only experiment: how can the element type of a bare property set be set? Nothing is added to the drawing.
        public static object ProbePartType(JsonElement args)
        {
            var results = new List<object>();
            foreach (var typeProp in new[] { "PnPClassName", "Type", "PartType", "ClassName", "PartCategory" })
            {
                var w = new SpecPart();
                string setErr = null;
                try { w.SetPropValue(typeProp, "Buttweld"); } catch (Exception e) { setErr = e.GetType().Name + ": " + e.Message; }
                string got;
                try { got = w.Type; } catch (Exception e) { got = "<" + e.GetType().Name + ">"; }
                results.Add(new { typeProp, setError = setErr, typeAfter = got, propNames = w.PropNames.Cast<string>().ToList() });
            }
            var f = new SpecPart();
            string ft; try { ft = f.Type; } catch (Exception e) { ft = "<" + e.GetType().Name + ">"; }
            return new { fresh = new { type = ft, propCount = f.PropCount }, variants = results };
        }

        // Adds a connector at 'origin'. Local X runs from port S1 toward port S2 (S1 faces the first part of the joint),
        // Z is any perpendicular. 'subs' are the sub-parts with their property sets, in order.
        internal const string WeldJointName = "Buttweld (RUS)";
        static int AddConnector(PipingObjectAdder adder, string jointName, Point3d origin, Vector3d x, Vector3d z, double slopeToleranceRad,
                                List<(SubPart sub, PartSizeProperties props)> subs, List<string> log, HashSet<int> added)
        {
            var c = new Connector();
            c.Position = origin;
            if (!c.SetOrientation(x, z)) throw new InvalidOperationException(jointName + ": SetOrientation rejected the axes");
            c.SlopeTolerance = slopeToleranceRad;
            c.OffsetTolerance = 0;
            var coll = new PartSizePropertiesCollection();
            foreach (var s in subs) { c.AddSubPart(s.sub); coll.Add(s.props); }
            int row = adder.Add(jointName, coll, c);
            added.Add(row);
            log.Add("connector '" + jointName + "' row " + row + " at " + Fmt(origin) + " x=" + Fmt(x));
            return row;
        }

        // Thickness of a gasket from its description ("СНП Д-2-43-1,6-4,5" -> 4.5).
        static double GasketThickness(SpecPart gasket, double fallback)
        {
            var m = Regex.Match(DescOf(gasket) ?? "", @"-(\d+(?:[.,]\d+)?)\s*$");
            return m.Success ? double.Parse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture) : fallback;
        }

        static List<(ObjectId id, int row, string cls)> PlantObjects(Document doc, DataLinksManager dlm)
        {
            var res = new List<(ObjectId, int, string)>();
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForRead);
                foreach (ObjectId id in ms) { int r = RowId(dlm, id); if (r > 0) res.Add((id, r, id.ObjectClass.Name)); }
                tr.Commit();
            }
            return res;
        }

        static List<(string name, Point3d pos, Vector3d dir)> SafePorts(Document doc, ObjectId id)
        {
            try { return PortsOf(doc, id); } catch (Exception) { return new List<(string, Point3d, Vector3d)>(); }
        }

        // A connector joins two parts at one point: its S1 faces one part, S2 the other, and each part's port there points the opposite way.
        // Any connector port that has no connection gets joined to the part port that sits on it and faces it. Returns what was done / not possible.
        static List<string> RepairConnectors(Document doc, DataLinksManager dlm, bool really, IEnumerable<ObjectId> only = null)
        {
            var log = new List<string>();
            var all = PlantObjects(doc, dlm);
            var partPorts = all.Where(o => !o.cls.Contains("Connector")).Select(o => (o.id, ports: SafePorts(doc, o.id))).ToList();
            var connectors = only != null ? only.ToList() : all.Where(o => o.cls.Contains("Connector")).Select(o => o.id).ToList();
            foreach (var cid in connectors)
            {
                if (cid.IsErased) continue;
                string me = cid.Handle.ToString();
                var links = ConnList(doc, cid);
                foreach (var cp in SafePorts(doc, cid))
                {
                    if (links.Any(l => (l.h1 == me && l.p1 == cp.name) || (l.h2 == me && l.p2 == cp.name))) continue;
                    var cands = partPorts.SelectMany(pp => pp.ports.Where(q => q.pos.DistanceTo(cp.pos) < 0.5 && q.dir.Length > 1e-9 && cp.dir.Length > 1e-9 &&
                                         q.dir.GetNormal().DotProduct(cp.dir.GetNormal()) < -0.99).Select(q => (pp.id, q.name))).ToList();
                    if (cands.Count != 1) { log.Add(me + "." + cp.name + ": not repaired, " + cands.Count + " candidate parts"); continue; }
                    try
                    {
                        if (really) ConnectCore(doc, cid, cands[0].id, true, cp.name);
                        log.Add(me + "." + cp.name + (really ? " joined to " : " would be joined to ") + cands[0].id.Handle + "." + cands[0].name);
                    }
                    catch (Exception ex) { log.Add(me + "." + cp.name + ": repair failed: " + ex.Message); }
                }
            }
            return log;
        }

        // Creates the Buttweld connector of a weld joint between two parts whose ports coincide and face each other (e.g. a flange and a pipe).
        public static object CreateJoint(JsonElement args)
        {
            var doc = ActiveDocument();
            var a = IdFromHandle(doc.Database, args.GetProperty("handleA").GetString());
            var b = IdFromHandle(doc.Database, args.GetProperty("handleB").GetString());
            string specName = (args.TryGetProperty("spec", out var sp) && sp.ValueKind == JsonValueKind.String ? sp.GetString() : null) ?? DefaultSpec;
            if (a == b) throw new ArgumentException("handleA and handleB are the same object");
            string jointArg = args.TryGetProperty("joint", out var jj) && jj.ValueKind == JsonValueKind.String ? jj.GetString() : WeldJointName;
            var dlm = Dlm();
            var pa = SafePorts(doc, a); var pb = SafePorts(doc, b);
            double best = double.MaxValue; int ia = -1, ib = -1;
            for (int i = 0; i < pa.Count; i++)
                for (int j = 0; j < pb.Count; j++)
                {
                    double d = pa[i].pos.DistanceTo(pb[j].pos);
                    if (d < best) { best = d; ia = i; ib = j; }
                }
            if (ia < 0 || best > 1.0) throw new InvalidOperationException("The closest ports are " + (ia < 0 ? "n/a" : Math.Round(best, 2).ToString()) + " mm apart; they must coincide");
            if (pa[ia].dir.GetNormal().DotProduct(pb[ib].dir.GetNormal()) > -0.99) throw new InvalidOperationException("The ports at the joint do not face each other");
            Point3d at = pa[ia].pos;
            Vector3d travel = pa[ia].dir.GetNormal();   // outward from A, toward B
            // refuse if a connector already sits there
            foreach (var o in PlantObjects(doc, dlm).Where(o => o.cls.Contains("Connector")))
                if (SafePorts(doc, o.id).Any(p => p.pos.DistanceTo(at) < 0.5))
                    throw new InvalidOperationException("Connector " + o.id.Handle + " is already at this joint");
            double nd = 100;
            try { if (double.TryParse(Props(dlm, RowId(dlm, a))["NominalDiameter"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ndv) && ndv > 0) nd = ndv; } catch (Exception) { }
            var plan = new { joint = "Buttweld", at = Arr(at), a = a.Handle.ToString() + "." + pa[ia].name, b = b.Handle.ToString() + "." + pb[ib].name, distance = Math.Round(best, 3), nd };
            if (IsDry(args)) return new { dry_run = true, plan, note = "Nothing was changed. Repeat with dry_run=false to create the connector." };

            var before = ModelRows(doc, dlm);
            var added = new HashSet<int>();
            var log = new List<string>();
            try
            {
                int row;
                using (doc.LockDocument())
                using (var adder = new PipingObjectAdder(Piping().DataLinksManager3d, doc.Database))
                {
                    var wp = MakeWeldProps("Buttweld", "Buttweld", nd, specName, log);
                    row = AddConnector(adder, jointArg, at, travel.Negate(), Perp(travel), 0.0523598775598299, new List<(SubPart, PartSizeProperties)> { (new WeldSubPart(), wp) }, log, added);
                }
                var cid = FindByRow(doc, dlm, row);
                bool s2First = args.TryGetProperty("s2_first", out var s2f) && s2f.ValueKind == JsonValueKind.True;
                if (s2First) { ConnectCore(doc, cid, a, true, "S2"); ConnectCore(doc, cid, b, true, "S1"); }
                else { ConnectCore(doc, cid, b, true, "S1"); ConnectCore(doc, cid, a, true, "S2"); }   // S1 faces B, S2 faces A
                return new { plan, created = cid.Handle.ToString(), links = ConnList(doc, cid).Select(c => c.h1 + "." + c.p1 + "<>" + c.h2 + "." + c.p2).ToList(), log };
            }
            catch (Exception ex)
            {
                string rb = RollbackSince(doc, dlm, added, before);
                throw new InvalidOperationException("Joint failed and was rolled back (" + rb + "): " + ex.Message);
            }
        }

        // Repairs missing connector links (see RepairConnectors). Default is a dry run over every connector of the drawing.
        public static object RepairConnections(JsonElement args)
        {
            var doc = ActiveDocument();
            var dlm = Dlm();
            List<ObjectId> only = null;
            if (args.TryGetProperty("handles", out var hs) && hs.ValueKind == JsonValueKind.Array)
                only = hs.EnumerateArray().Select(e => IdFromHandle(doc.Database, e.GetString())).ToList();
            bool dry = IsDry(args);
            var log = RepairConnectors(doc, dlm, !dry, only);
            return new { dry_run = dry, actions = log, note = dry ? "Nothing was changed. Repeat with dry_run=false to apply." : "Applied. Check with get_connections." };
        }

        // Plant 3D can create parts and typed weld connectors of its own where a new part touches an existing pipe, which leaves two
        // objects at one place. Keeps the typed/foreign one, erases the duplicate created by this call, then restores the links.
        static List<string> DedupeAndRepair(Document doc, DataLinksManager dlm, HashSet<int> added)
        {
            var log = new List<string>();
            var all = PlantObjects(doc, dlm);
            string K(Point3d p) => Math.Round(p.X / 0.5) + "," + Math.Round(p.Y / 0.5) + "," + Math.Round(p.Z / 0.5);
            var drop = new HashSet<int>();

            // duplicate pipes: same two end points
            var pipes = all.Where(o => o.cls == "AcPpDb3dPipe").Select(o => (o, ports: SafePorts(doc, o.id).Where(p => p.name == "S1" || p.name == "S2").ToList())).Where(x => x.ports.Count == 2).ToList();
            foreach (var g in pipes.GroupBy(x => string.Join("|", x.ports.Select(p => K(p.pos)).OrderBy(k => k))).Where(g => g.Count() > 1))
            {
                var mine = g.Where(x => added.Contains(x.o.row)).ToList();
                var others = g.Where(x => !added.Contains(x.o.row)).ToList();
                var victims = others.Count > 0 ? mine : mine.Skip(1).ToList();
                foreach (var v in victims) { drop.Add(v.o.row); log.Add("duplicate pipe " + v.o.id.Handle + " (same ends as " + g.First(x => x.o.row != v.o.row).o.id.Handle + ") will be erased"); }
            }
            // duplicate connectors: same joint type at the same point
            var conns = new List<(ObjectId id, int row, string jt, string key, int weldId)>();
            foreach (var o in all.Where(o => o.cls.Contains("Connector")))
            {
                try
                {
                    var pr = Props(dlm, o.row);
                    pr.TryGetValue("JointType", out var jt);
                    var ps = SafePorts(doc, o.id);
                    if (ps.Count == 0) continue;
                    conns.Add((o.id, o.row, jt ?? "", (jt ?? "") + "@" + K(ps.OrderBy(p => p.name).First().pos), WeldPartId(doc, o.id)));
                }
                catch (Exception) { }
            }
            foreach (var g in conns.GroupBy(c => c.key).Where(g => g.Count() > 1))
            {
                var ranked = g.OrderByDescending(c => c.weldId > 0 ? 1 : 0).ThenByDescending(c => added.Contains(c.row) ? 0 : 1).ToList();   // keep typed, then foreign
                foreach (var v in ranked.Skip(1).Where(c => added.Contains(c.row)))
                { drop.Add(v.row); log.Add("duplicate " + v.jt + " connector " + v.id.Handle + " will be erased (kept " + ranked[0].id.Handle + ")"); }
            }
            if (drop.Count > 0)
            {
                int n = EraseRows(doc, dlm, drop);
                foreach (var r in drop) added.Remove(r);
                log.Add("erased " + n + " duplicate object(s); restoring connector links");
            }
            log.AddRange(RepairConnectors(doc, dlm, true));
            return log;
        }

        // Data row of the weld element of a Tap/Buttweld connector: > 0 when it has real weld data, -1 when it is empty.
        static int WeldPartId(Document doc, ObjectId id)
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var c = tr.GetObject(id, OpenMode.ForRead) as Connector;
                int pid = -1;
                if (c != null && c.NumSubParts > 0) { try { pid = c.GetSubPart(0).PartSizeProperties.PartId; } catch (Exception) { pid = -1; } }
                tr.Commit();
                return pid;
            }
        }

        static int SpecRecordOf(SpecPart p) { try { return Convert.ToInt32(Prop(p, "SpecRecordId") ?? int.MaxValue); } catch (Exception) { return int.MaxValue; } }

        static bool SameText(object a, object b) => string.Equals(a?.ToString() ?? "", b?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

        // Gasket and bolt set for a flanged joint, chosen like the project's joint definition does: same ND, facing and pressure class as the flange.
        // 'gasketType' ("Д", "В") picks the spiral-wound gasket type; the user's reference assembly uses type Д.
        static (SpecPart gasket, SpecPart bolts) PickFlangeHardware(SpecManager sm, string specName, string key, double nd, SpecPart flange, string gasketType = "Д")
        {
            object facing = Prop(flange, "Facing"), pc = Prop(flange, "PressureClass");
            var gaskets = CollectParts(sm, specName, key, "Gasket", nd).Select(c => c.part)
                .Where(p => SameText(Prop(p, "Facing"), facing) && SameText(Prop(p, "PressureClass"), pc)).OrderBy(SpecRecordOf).ToList();
            if (gaskets.Count == 0) throw new InvalidOperationException("No gasket with facing '" + facing + "' and pressure class '" + pc + "' for DN" + nd + " in the spec");
            if (!string.IsNullOrEmpty(gasketType))
            {
                string tag = "СНП " + gasketType + "-";
                var typed = gaskets.Where(p => (DescOf(p) ?? "").IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (typed.Count > 0) gaskets = typed;
            }
            var bolts = CollectParts(sm, specName, key, "BoltSet", nd).Select(c => c.part)
                .Where(p => SameText(Prop(p, "PressureClass"), pc)).OrderBy(SpecRecordOf).ToList();
            if (bolts.Count == 0) throw new InvalidOperationException("No bolt set with pressure class '" + pc + "' for DN" + nd + " in the spec");
            return (gaskets[0], bolts[0]);
        }
    }
}
