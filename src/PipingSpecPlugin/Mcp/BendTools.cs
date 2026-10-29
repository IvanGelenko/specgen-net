using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PartsRepository;
using Autodesk.ProcessPower.PnP3dObjects;
using Part = Autodesk.ProcessPower.PnP3dObjects.Part;
using Exception = System.Exception;

namespace Plant3dMcp
{
    // probe_bend: how the parametric "ИЗГИБ ТРУБЫ" (PipeBend) part of a spec takes its radius, angle and tails.
    // Without place=true nothing touches the drawing: the spec parts and their properties are listed.
    // With place=true one bend with the given parameters is added far from the model, its ports and description are read back, and it is erased again.
    static partial class PlantTools
    {
        public static object ProbeBend(JsonElement args)
        {
            var doc = ActiveDocument();
            var piping = Piping();
            string spec = args.TryGetProperty("spec", out var sp) && sp.ValueKind == JsonValueKind.String ? sp.GetString() : "!_Rus-PipeAndFittings_ст20_PN16";
            double nd = args.TryGetProperty("nd", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetDouble() : 20;
            double radius = args.TryGetProperty("radius", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetDouble() : 90;
            double angle = args.TryGetProperty("angle", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDouble() : 30;
            if (!(radius > 0) || !(angle > 0 && angle <= 180)) throw new ArgumentException("radius must be > 0 and angle in (0, 180] degrees");
            bool place = args.TryGetProperty("place", out var pl) && pl.ValueKind == JsonValueKind.True;
            bool keep = args.TryGetProperty("keep", out var kp) && kp.ValueKind == JsonValueKind.True;   // leave the bend in the drawing (to see whether Plant 3D complains about creation or about the erase)

            var sm = SpecManager.GetSpecManager();
            string key = LoadSpecKey(sm, piping, spec);
            var types = new List<string>();
            var keys = new List<string> { spec, key };
            try { keys.Add(System.IO.Path.GetFileNameWithoutExtension(key)); } catch (Exception) { }
            try { keys.Add(System.IO.Path.GetFileName(key)); } catch (Exception) { }
            try { keys.Add(System.IO.Path.ChangeExtension(key, ".pspc")); } catch (Exception) { }
            var diag = new List<string>();
            string usedKey = key;
            foreach (string k in keys.Where(k => !string.IsNullOrEmpty(k)).Distinct())
            {
                try
                {
                    var ts = sm.GetAvailablePartTypes(k) ?? Array.Empty<string>();
                    diag.Add("[" + Short(k) + "] types=" + ts.Length);
                    if (ts.Length > 0) { types = ts.ToList(); usedKey = k; break; }
                }
                catch (Exception e) { diag.Add("[" + Short(k) + "] GetAvailablePartTypes threw " + e.GetType().Name); }
            }
            string typeArg = args.TryGetProperty("type", out var ty) && ty.ValueKind == JsonValueKind.String ? ty.GetString() : null;
            var bendTypes = new List<string>();
            if (typeArg != null) bendTypes = new List<string> { typeArg };
            if (bendTypes.Count == 0) bendTypes = new List<string> { "Pipe" };
            List<(SpecPart part, double wall, string desc)> cands = null;
            foreach (string k in new[] { usedKey }.Concat(keys.Where(x => !string.IsNullOrEmpty(x) && x != usedKey)).Distinct())
            {
                foreach (string bt in bendTypes)
                {
                    cands = TrySelect(sm, k, new List<string> { bt }, nd, diag);
                    if (cands != null) break;
                }
                if (cands != null) break;
            }
            if (cands == null)
                return new { spec, nd, types, bendTypes, diag, note = "No bend part of this size was selected. Pass 'type' with the exact type name from 'types'." };

            object Describe(SpecPart p) => new
            {
                description = DescOf(p), type = SafeType(p),
                props = p.PropNames.Cast<string>().Select(nm => nm + "=" + (Prop(p, nm)?.ToString() ?? "")).ToList()
            };
            var listed = cands.Select(c => Describe(c.part)).ToList();
            if (!place) return new { spec, nd, types, bendTypes, parts = listed, diag, note = "Nothing was changed. Repeat with place=true (and radius, angle) to place a trial bend and erase it." };

            // The bend is made from the PIPE part of the spec by the routing class BentPipe (radius, angle, straight tails L1/L2).
            double l1 = args.TryGetProperty("l1", out var l1j) && l1j.ValueKind == JsonValueKind.Number ? l1j.GetDouble() : 100;
            double l2 = args.TryGetProperty("l2", out var l2j) && l2j.ValueKind == JsonValueKind.Number ? l2j.GetDouble() : 0;
            if (l1 < 0 || l2 < 0) throw new ArgumentException("l1 and l2 must not be negative");
            double wallArg =args.TryGetProperty("wall", out var wj) && wj.ValueKind == JsonValueKind.Number ? wj.GetDouble() : 4;
            var chosen = cands.FirstOrDefault(c => Math.Abs(c.wall - wallArg) < 1e-6);
            if (chosen.part == null) chosen = cands[0];
            var part = new SpecPart();
            part.AssignFrom(chosen.part);
            var set = new List<string> { "pipe part: " + DescOf(part) };

            var dlm = Dlm();
            var before = ModelRows(doc, dlm);
            var added = new HashSet<int>();
            var log = new List<string>(); log.AddRange(set);
            object result;
            try
            {
                var at = new Point3d(50000, -50000, 0);
                int row;
                ObjectId bendId;
                using (doc.LockDocument())
                using (var adderForGroup = new PipingObjectAdder(piping.DataLinksManager3d, doc.Database))
                {
                    // Append(lineGroupId): 0 is not a real group (Plant answers RowNotFound); use the adder's own group, or the one passed in.
                    int lineGroup = args.TryGetProperty("line_group", out var lgj) && lgj.ValueKind == JsonValueKind.Number ? lgj.GetInt32() : adderForGroup.LineGroupId;
                    log.Add("line group id used: " + lineGroup + " (adder default " + adderForGroup.LineGroupId + ")");
                    var bp = new Autodesk.ProcessPower.PnP3dPipeRouting.BentPipe();
                    bp.PartSizeProperties = part;
                    bp.SymbolId = ContentManager.GetContentManager().GetSymbol(part, doc.Database);
                    bp.OuterDiameter = OuterDiameter(part);
                    bp.Radius = radius;
                    bp.Angle = angle * Math.PI / 180.0;
                    bp.L1 = l1; bp.L2 = l2;
                    bp.Position = at;
                    bp.SetOrientation(Vector3d.XAxis, Vector3d.ZAxis);
                    log.Add("BentPipe ports before append: " + string.Join(" ", Enumerable.Range(0, bp.GetPorts(PortType.All).Count).Select(i => { var q = bp.GetPorts(PortType.All)[i]; return q.Name + Fmt(q.Position) + "/" + Fmt(q.Direction); })));
                    bendId = bp.Append(lineGroup);
                }
                row = RowId(dlm, bendId);
                if (row > 0) added.Add(row);
                var id = bendId;
                var ports = PortsOf(doc, id);
                var props = Props(dlm, row);
                string D(string k) => props.TryGetValue(k, out var v) ? v : null;
                result = new
                {
                    placed = true, handle = id.Handle.ToString(), set,
                    description = D("PartSizeLongDesc"), curveRadius = D("CurveRadius"), pathAngle = D("PathAngle"), cutLength = D("CutLength"), specRecordId = D("SpecRecordId"),
                    ports = ports.Select(p => p.name + Fmt(p.pos) + "/" + Fmt(p.dir)).ToList(),
                    chord = ports.Count == 2 ? Math.Round(ports[0].pos.DistanceTo(ports[1].pos), 2) : (double?)null
                };
            }
            finally
            {
                if (keep) log.Add("kept in the drawing at (50000,-50000,0); erase it by hand with the ERASE command");
                else
                    try { log.Add("erased: " + RollbackSince(doc, dlm, added, before)); } catch (Exception e) { log.Add("ERASE FAILED: " + e.Message); }
            }
            return new { result, log, note = keep ? "The trial bend was KEPT; erase it by hand." : "The trial bend was erased; check the log for 'ERASE FAILED'." };
        }


        // port_conditions: read-only. The end condition of every port as the part's spec stores it (the Port.EndType property is empty on all parts).
        public static object PortConditions(JsonElement args)
        {
            var doc = ActiveDocument();
            var id = IdFromHandle(doc.Database, args.GetProperty("handle").GetString());
            var res = new List<object>();
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var part = tr.GetObject(id, OpenMode.ForRead) as Part ?? throw new InvalidOperationException("Not a Plant 3D part");
                var ports = part.GetPorts(PortType.All);
                for (int i = 0; i < ports.Count; i++)
                {
                    string cond = null; var props = new List<string>();
                    try
                    {
                        var sp = part.PortProperties(ports[i].Name);
                        if (sp != null)
                        {
                            cond = sp.EndCondition;
                            foreach (string pn in sp.PropNames) { object v = null; try { v = sp.PropValue(pn); } catch (Exception) { } props.Add(pn + "=" + v); }
                        }
                    }
                    catch (Exception e) { props.Add("PortProperties threw " + e.GetType().Name + ": " + e.Message); }
                    res.Add(new { port = ports[i].Name, endTypeProperty = ports[i].EndType, endCondition = cond, props });
                }
                tr.Commit();
            }
            return new { handle = id.Handle.ToString(), cls = id.ObjectClass.Name, ports = res };
        }


        // probe_connector_wrapper: experiment. Joins two coincident ports with the routing class ConnectorWrapper, the way Plant 3D itself makes
        // typed weld connectors. Writes to the drawing: use only on test parts.
        public static object ProbeConnectorWrapper(JsonElement args)
        {
            var doc = ActiveDocument(); var dlm = Dlm(); var piping = Piping();
            var a = IdFromHandle(doc.Database, args.GetProperty("handleA").GetString());
            var b = IdFromHandle(doc.Database, args.GetProperty("handleB").GetString());
            string pa = args.GetProperty("portA").GetString(), pb = args.GetProperty("portB").GetString();
            string joint = args.TryGetProperty("joint", out var jj) && jj.ValueKind == JsonValueKind.String ? jj.GetString() : "Buttweld (RUS)";
            string spec = args.TryGetProperty("spec", out var sj) && sj.ValueKind == JsonValueKind.String ? sj.GetString() : "!_Rus-PipeAndFittings_ст20_PN16";
            string steps = args.TryGetProperty("steps", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : "connect,append";
            var log = new List<string>();
            int lineGroup; using (var ag = new PipingObjectAdder(piping.DataLinksManager3d, doc.Database)) lineGroup = ag.LineGroupId;
            var before = ModelRows(doc, dlm);
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var partA = (Part)tr.GetObject(a, OpenMode.ForWrite); var partB = (Part)tr.GetObject(b, OpenMode.ForWrite);
                var portA = partA.FindPort(pa, PortType.All) ?? throw new InvalidOperationException("no port " + pa);
                var portB = partB.FindPort(pb, PortType.All) ?? throw new InvalidOperationException("no port " + pb);
                var pairA = new Pair(); pairA.ObjectId = a; pairA.Port = portA;
                var pairB = new Pair(); pairB.ObjectId = b; pairB.Port = portB;
                var cw = new Autodesk.ProcessPower.PnP3dPipeRouting.ConnectorWrapper();
                try { cw.Spec = spec; log.Add("Spec set"); } catch (Exception e) { log.Add("Spec: " + e.Message); }
                var names = new System.Collections.Specialized.StringCollection();
                try { log.Add("CanConnect(joint names) = " + cw.CanConnect(pairA, pairB, names) + "; joints: " + string.Join(",", names.Cast<string>())); } catch (Exception e) { log.Add("CanConnect threw " + e.GetType().Name + ": " + e.Message); }
                foreach (string step in steps.Split(','))
                {
                    try
                    {
                        if (step == "connect") { cw.Connect(pairA, pairB, joint); log.Add("Connect ok; JointName=" + cw.JointName + " Connected=" + cw.Connected); }
                        else if (step == "append") { cw.Append(pairA, pairB, lineGroup); log.Add("Append ok"); }
                    }
                    catch (Exception e) { log.Add(step + " threw " + e.GetType().Name + ": " + e.Message); }
                }
                tr.Commit();
            }
            var fresh = ModelRows(doc, dlm).Where(r => !before.Contains(r)).ToList();
            var created = fresh.Select(r => { try { var id = FindByRow(doc, dlm, r); return id.Handle + " " + id.ObjectClass.Name; } catch (Exception) { return "row " + r; } }).ToList();
            return new { joint, lineGroup, log, created };
        }


        // restore_branch_link: a vent/drain whose Tap connector lost its link to the run pipe (the run's dynamic port was removed). Adds the dynamic port Dn
        // on the run at the connector's free port and joins them. Two-phase: dry run by default.
        public static object RestoreBranchLink(JsonElement args)
        {
            var doc = ActiveDocument(); var dlm = Dlm();
            var run = IdFromHandle(doc.Database, args.GetProperty("run_handle").GetString());
            var conn = IdFromHandle(doc.Database, args.GetProperty("connector_handle").GetString());
            var branch = IdFromHandle(doc.Database, args.GetProperty("branch_handle").GetString());
            string ch = conn.Handle.ToString();
            var links = ConnList(doc, conn);
            var ports = PortsOf(doc, conn);
            var free = ports.Where(q => !links.Any(l => (l.h1 == ch && l.p1 == q.name) || (l.h2 == ch && l.p2 == q.name))).ToList();
            if (free.Count != 1) throw new InvalidOperationException("The connector must have exactly one free port, it has " + free.Count);
            var fp = free[0];
            var dir = fp.dir.GetNormal().Negate();   // from the run toward the branch
            double dist;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var pipe = (Pipe)tr.GetObject(run, OpenMode.ForRead);
                var ab = pipe.EndPoint - pipe.StartPoint; var v = fp.pos - pipe.StartPoint;
                dist = (v - ab * (v.DotProduct(ab) / ab.DotProduct(ab))).Length;
                tr.Commit();
            }
            var plan = new { run = run.Handle.ToString(), connector = ch, free_port = fp.name, at = Arr(fp.pos), direction = Arr(dir), distance_from_run_axis = Math.Round(dist, 1) };
            if (IsDry(args)) return new { dry_run = true, plan, note = "Nothing was changed. Repeat with dry_run=false." };
            string dyn = AddDynamicPort(doc, run, fp.pos, dir, branch, "TAP");
            try { ConnectCore(doc, conn, run, true, fp.name); }
            catch (Exception) { try { RemoveDynamicPort(doc, run, dyn); } catch (Exception) { } throw; }
            return new { plan, dynamic_port = dyn, links = ConnList(doc, conn).Select(c => c.h1 + "." + c.p1 + "-" + c.h2 + "." + c.p2).ToList() };
        }


        // set_layer: puts objects on a layer (created with the given AutoCAD colour index if missing). Used to colour the tracing without touching the colour
        // of the Plant objects themselves.
        public static object SetLayer(JsonElement args)
        {
            var doc = ActiveDocument();
            string layer = args.GetProperty("layer").GetString();
            int color = args.TryGetProperty("color", out var cj) && cj.ValueKind == JsonValueKind.Number ? cj.GetInt32() : 1;
            var handles = args.GetProperty("handles").EnumerateArray().Select(e => e.GetString()).Distinct().ToList();
            if (handles.Count == 0 || handles.Count > 200) throw new ArgumentException("handles: 1..200");
            var ids = handles.Select(h => IdFromHandle(doc.Database, h)).ToList();
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(layer))
                {
                    lt.UpgradeOpen();
                    var rec = new LayerTableRecord { Name = layer, Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, (short)color) };
                    lt.Add(rec); tr.AddNewlyCreatedDBObject(rec, true);
                }
                bool byLayer = !(args.TryGetProperty("by_layer", out var blj) && blj.ValueKind == JsonValueKind.False);   // the colour of the objects follows the layer
                foreach (var id in ids) { var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite); ent.Layer = layer; if (byLayer) ent.ColorIndex = 256; }
                tr.Commit();
            }
            return new { layer, color, moved = ids.Count };
        }

        static string SafeType(SpecPart p) { try { return p.Type; } catch (Exception) { return null; } }
    }
}
