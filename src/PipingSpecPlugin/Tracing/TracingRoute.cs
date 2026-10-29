using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PartsRepository;
using Autodesk.ProcessPower.PnP3dObjects;
using Exception = System.Exception;

namespace PipingSpecPlugin.Tracing
{
    // create_tracing_route: heat tracing of a whole pipe route: straight sections joined by 90 degree elbows (risers included), flanges, valves, spectacle blinds, tees
    // (the branch is passed by). The tracers keep their place on the pipe through every elbow (the offset frame is carried round the bend), run on arcs concentric
    // with the elbows, step sideways round wide parts, and are closed with 180 degree bends beyond the two free ends of the route.
    internal static partial class TracingEngine
    {
        sealed class RPart
        {
            public ObjectId Id; public string H, Kind, Desc; public double Od;
            public List<(string name, Point3d pos, Vector3d dir)> Ports; public string In, Out;
            public Point3d InPos => Ports.First(p => p.name == In).pos;
            public Point3d OutPos => Ports.First(p => p.name == Out).pos;
        }
        sealed class RBranch
        {
            public string Name, Pipe; public int Sec; public double A, ZMin, ZMax, Rho, Side, LDip, Shift; public bool Down;
            public bool Riser; public Vector3d B, C; public double CSign, Bmax, DeltaLat; public Point3d Origin; public Dictionary<double, double> SignBySide = new Dictionary<double, double>();
            public List<(double x1, double x2, double y1, double y2, double z1, double z2)> Rects = new List<(double, double, double, double, double, double)>();
            public List<string> Handles = new List<string>(), Info = new List<string>();
            public List<(Point3d p1, Point3d p2, double rad)> Cyls = new List<(Point3d, Point3d, double)>();   // the pipes of a branch (real geometry, not the box of a slanted pipe)
            public List<RSec> Chain; public string FirstPipe;     // a branch that goes on round elbows: its sections (the tracer legs follow them without a break)
            public string TeeH;                    // the tee of the route this branch grows from (null: a vent / drain on a dynamic port)
            public int HeavyC, HeavyD;             // the side (+1 / -1 along C / along the run) on which the branch parts reach farther: the handwheel and the stem
        }
        sealed class RBox { public string H, Desc; public double A1, A2, L1, L2, W1, W2; }
        sealed class RSec
        {
            public Point3d A0, EndPt; public Vector3d D, L, W, L2, W2; public double Len, Twist;   // (L,W) at the start of the section, (L2,W2) at its end; Twist = rotation of the tracers about the axis between the two
            public List<RPart> Parts = new List<RPart>(); public List<RBox> Boxes = new List<RBox>();
            public RPart ElbowAfter; public double Re; public Point3d Pc, Cc;
            public bool IsRiser, Hold, Perp; public Vector3d PP, PM, Combo, Gd, Lpv, Lnv; public double LjP, LjM, LbP, LbM, GP, GM, JS, JE;   // LjP/LjM: lateral offset against the run before the riser, LbP/LbM: against the run after it   // riser: horizontal offsets of the two tracers; horizontal: where the lateral shift near the risers is done
        }

        // the specs (mini-catalogues) of the project: the tracer pipe is taken from one of them
        internal static List<string> SpecNames()
        {
            var res = new List<string>();
            try { foreach (var o in Piping().GetSpecNames()) { var n = o?.ToString(); if (!string.IsNullOrEmpty(n)) res.Add(n); } }
            catch (Exception) { }
            return res.Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static double BoxDist3(Point3d q, double x1, double x2, double y1, double y2, double z1, double z2)
        {
            double dx = Math.Max(Math.Max(x1 - q.X, 0), q.X - x2), dy = Math.Max(Math.Max(y1 - q.Y, 0), q.Y - y2), dz = Math.Max(Math.Max(z1 - q.Z, 0), q.Z - z2);
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // the boxes of the supports of the drawing (the tracers must not run through the shoes, plates and posts)
        static List<(double x1, double x2, double y1, double y2, double z1, double z2, string h)> SupportBoxes(Document doc)
        {
            var res = new List<(double, double, double, double, double, double, string)>();
            using (doc.LockDocument()) using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.ObjectClass.Name != "AcPpDb3dSupport") continue;
                    try
                    {
                        var ext = ((Entity)tr.GetObject(id, OpenMode.ForRead)).GeometricExtents;
                        if (ext.MaxPoint.DistanceTo(ext.MinPoint) > 4000) continue;     // not a support of a pipe: skip
                        res.Add((ext.MinPoint.X, ext.MaxPoint.X, ext.MinPoint.Y, ext.MaxPoint.Y, ext.MinPoint.Z, ext.MaxPoint.Z, id.Handle.ToString()));
                    }
                    catch (Exception) { }
                }
                tr.Commit();
            }
            return res;
        }

        // how many of the planned tracer pipes lie exactly on tracer pipes (OD 25) that exist already
        static int CountExistingTracerPipes(Document doc, List<TItem> items)
        {
            var existing = new List<(Point3d a, Point3d b)>();
            using (doc.LockDocument()) using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.ObjectClass.Name != "AcPpDb3dPipe") continue;
                    try
                    {
                        if (Math.Abs(((Pipe)tr.GetObject(id, OpenMode.ForRead)).OuterDiameter - 25) > 0.6) continue;
                        var pr = SafePorts(doc, id); var s1 = pr.FirstOrDefault(q => q.name == "S1"); var s2 = pr.FirstOrDefault(q => q.name == "S2");
                        if (s1.name != null && s2.name != null) existing.Add((s1.pos, s2.pos));
                    }
                    catch (Exception) { }
                }
                tr.Commit();
            }
            if (existing.Count == 0) return 0;
            int n = 0;
            foreach (var it in items.Where(i => i.Kind == "pipe"))
                if (existing.Any(e => (e.a.DistanceTo(it.P1) < 1.5 && e.b.DistanceTo(it.P2) < 1.5) || (e.a.DistanceTo(it.P2) < 1.5 && e.b.DistanceTo(it.P1) < 1.5))) n++;
            return n;
        }

        static RPart MakeRPart(Document doc, DataLinksManager dlm, ObjectId id)
        {
            // dynamic ports (D1, D2 ...) are the attachment points of supports and taps, not part of the route
            var ports = SafePorts(doc, id).Where(q => !Regex.IsMatch(q.name, @"^D\d+$")).ToList();
            string cls = id.ObjectClass.Name, desc = DescById(doc, dlm, id);
            var r = new RPart { Id = id, H = id.Handle.ToString(), Desc = desc.Trim(), Ports = ports };
            if (cls == "AcPpDb3dPipe")
            {
                r.Kind = "pipe";
                using (doc.LockDocument()) using (var tr = doc.Database.TransactionManager.StartTransaction()) { r.Od = ((Pipe)tr.GetObject(id, OpenMode.ForRead)).OuterDiameter; tr.Commit(); }
            }
            else if (ports.Count == 3 && Regex.IsMatch(desc, "Тройник|Tee")) r.Kind = "tee";
            else if (ports.Count == 2 && (Regex.IsMatch(desc, "Отвод|Elbow") || ports[0].dir.GetNormal().DotProduct(ports[1].dir.GetNormal()) > -0.9)) r.Kind = "elbow";
            else if (ValveRx.IsMatch(desc)) r.Kind = "valve";
            else if (FlangeRx.IsMatch(desc)) r.Kind = "flange";
            else if (Regex.IsMatch(desc, "Заглушка|Blind|Диск")) r.Kind = "blind";
            else r.Kind = "other";
            return r;
        }

        static (ObjectId id, string port)? AdvanceRoute(Document doc, ObjectId id, string outPort)
        {
            string h = id.Handle.ToString();
            var l = ConnList(doc, id).Where(c => (c.h1 == h && c.p1 == outPort) || (c.h2 == h && c.p2 == outPort)).ToList();
            if (l.Count == 0) return null;
            string oh = l[0].h1 == h ? l[0].h2 : l[0].h1, op = l[0].h1 == h ? l[0].p2 : l[0].p1;
            if (oh == null) return null;
            var oid = IdFromHandle(doc.Database, oh);
            if (oid.ObjectClass.Name.Contains("Connector"))
            {
                var l2 = ConnList(doc, oid).Where(c => c.h1 != h && c.h2 != h).ToList();
                if (l2.Count == 0) return null;
                string ch = oh;
                string nh = l2[0].h1 == ch ? l2[0].h2 : l2[0].h1, np = l2[0].h1 == ch ? l2[0].p2 : l2[0].p1;
                if (nh == null) return null;
                return (IdFromHandle(doc.Database, nh), np);
            }
            return (oid, op);
        }

        static string ThroughPort(RPart p, string enter)
        {
            var e = p.Ports.First(q => q.name == enter);
            if (p.Ports.Count == 2) return p.Ports.First(q => q.name != enter).name;
            if (p.Ports.Count == 3)
            {
                var o = p.Ports.Where(q => q.name != enter && q.dir.GetNormal().DotProduct(e.dir.GetNormal()) < -0.9).ToList();
                return o.Count == 1 ? o[0].name : null;
            }
            return null;
        }

        static List<RPart> WalkRoute(Document doc, DataLinksManager dlm, RPart first, string outPort, out bool endsAtTee)
        {
            endsAtTee = false;
            var res = new List<RPart> { first };
            first.Out = outPort; first.In = first.Ports.First(q => q.name != outPort).name;
            var cur = first;
            for (int guard = 0; guard < 1000; guard++)
            {
                var nx = AdvanceRoute(doc, cur.Id, cur.Out);
                if (nx == null) break;
                var np = MakeRPart(doc, dlm, nx.Value.id); np.In = nx.Value.port;
                np.Out = ThroughPort(np, np.In);
                if (np.Out == null && np.Kind == "tee") { endsAtTee = true; break; }      // this route is a branch of that tee: it ends in front of it
                if (np.Out == null) throw new InvalidOperationException("The route runs into a part it cannot pass through (" + np.Desc + ", " + np.H + ").");
                res.Add(np); cur = np;
            }
            return res;
        }

        public static object CreateTracingRoute(JsonElement args)
        {
            var doc = ActiveDocument(); var dlm = Dlm(); var piping = Piping();
            string specName = args.TryGetProperty("spec", out var sp) && sp.ValueKind == JsonValueKind.String ? sp.GetString() : "!_Rus-PipeAndFittings_ст20_PN16";
            double nd = args.TryGetProperty("nd", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetDouble() : 20;
            double wallArg = args.TryGetProperty("wall", out var wj) && wj.ValueKind == JsonValueKind.Number ? wj.GetDouble() : 4;
            double gap = args.TryGetProperty("gap", out var gj) && gj.ValueKind == JsonValueKind.Number ? gj.GetDouble() : 10;   // the default gap between the tracer and the main pipe (user: 10 mm)
            double R = args.TryGetProperty("radius", out var rj) && rj.ValueKind == JsonValueKind.Number ? rj.GetDouble() : 90;
            double pad = args.TryGetProperty("pad", out var pj) && pj.ValueKind == JsonValueKind.Number ? pj.GetDouble() : 10;
            double endMargin = args.TryGetProperty("end_margin", out var ej) && ej.ValueKind == JsonValueKind.Number ? ej.GetDouble() : 50;
            double tailU = args.TryGetProperty("tail_u", out var tj) && tj.ValueKind == JsonValueKind.Number ? tj.GetDouble() : 100;
            double minStraight = args.TryGetProperty("min_straight", out var mj) && mj.ValueKind == JsonValueKind.Number ? mj.GetDouble() : 100;
            double dipTail = args.TryGetProperty("dip_tail", out var dtj) && dtj.ValueKind == JsonValueKind.Number ? dtj.GetDouble() : 100;
            double overshoot = args.TryGetProperty("branch_overshoot", out var ovj) && ovj.ValueKind == JsonValueKind.Number ? ovj.GetDouble() : 50;
            double Rdip = args.TryGetProperty("dip_radius", out var drj) && drj.ValueKind == JsonValueKind.Number ? drj.GetDouble() : 40;
            double dipSide = args.TryGetProperty("dip_side", out var dsj) && dsj.ValueKind == JsonValueKind.Number && dsj.GetDouble() > 0 ? 1 : -1;
            bool heatBranches = !(args.TryGetProperty("heat_branches", out var hbj) && hbj.ValueKind == JsonValueKind.False);
            double handleLen = args.TryGetProperty("handle", out var hdj) && hdj.ValueKind == JsonValueKind.Number ? hdj.GetDouble() : 100;
            int stage = args.TryGetProperty("stage", out var stj) && stj.ValueKind == JsonValueKind.Number ? stj.GetInt32() : 3;
            // the smallest gap that is accepted (the user: 1, 2 or 3 mm in tight places, never an interference); the planner and the final check use this one number.
            // 'clear_tolerance' (= gap - min_gap) is still accepted for the old callers.
            double tol = args.TryGetProperty("min_gap", out var mgj) && mgj.ValueKind == JsonValueKind.Number ? Math.Max(0, gap - mgj.GetDouble())
                       : args.TryGetProperty("clear_tolerance", out var ctj) && ctj.ValueKind == JsonValueKind.Number ? ctj.GetDouble() : Math.Max(0, gap - 1.0);
            if (!(R > 0) || gap < 0 || pad < 0 || handleLen < 0 || minStraight < 0 || endMargin < 0 || tailU < 0 || !(nd > 0) || !(wallArg > 0))
                throw new ArgumentException("radius, nd and wall must be positive; gap, pad, handle, min_straight, end_margin and tail_u must not be negative");
            var startId = IdFromHandle(doc.Database, args.GetProperty("pipe").GetString());
            var warnings = new List<string>();

            // ---- the tracer pipe part -------------------------------------------------------------------------
            var sm = SpecManager.GetSpecManager();
            string key = LoadSpecKey(sm, piping, specName);
            var cands = CollectParts(sm, specName, key, "Pipe", nd);
            var chosen = cands.FirstOrDefault(c => Math.Abs(c.wall - wallArg) < 1e-6);
            if (chosen.part == null) throw new InvalidOperationException("No DN" + nd + " pipe with wall " + wallArg + " in " + specName);
            var pipePart = chosen.part;
            double odT = OuterDiameter(pipePart), rT = odT / 2;

            // ---- the route: parts in order from one free end to the other ---------------------------------------
            var first = MakeRPart(doc, dlm, startId);
            if (first.Kind != "pipe") throw new InvalidOperationException("'pipe' must be a plain pipe of the route.");
            var fwd = WalkRoute(doc, dlm, first, "S2", out bool fwdAtTee);
            var first2 = MakeRPart(doc, dlm, startId);
            var bwd = WalkRoute(doc, dlm, first2, "S1", out bool bwdAtTee);
            bwd.Reverse();
            foreach (var bp0 in bwd) { var t = bp0.In; bp0.In = bp0.Out; bp0.Out = t; }   // seen from the other end: swap
            var chain = new List<RPart>(bwd); chain.AddRange(fwd.Skip(1));
            // chain[0].In and chain[last].Out are the free ends; the start part appears once (from 'bwd', with In/Out swapped back correctly below)
            chain[bwd.Count - 1].Out = fwd[0].Out; chain[bwd.Count - 1].In = bwd[bwd.Count - 1].In;
            bool startAtTee, endAtTee;      // the end of the route is the branch port of a tee (a branch of a larger route)
            // the same route direction whichever pipe of the line was picked (the plan must not depend on it): start at the end with the smaller X, then Y, then Z
            {
                var e0 = chain[0].InPos; var e1 = chain[chain.Count - 1].OutPos;
                bool startFirst = Math.Abs(e0.X - e1.X) > 1 ? e0.X < e1.X : Math.Abs(e0.Y - e1.Y) > 1 ? e0.Y < e1.Y : e0.Z <= e1.Z;
                if (!startFirst) { chain.Reverse(); foreach (var cp in chain) { var tp = cp.In; cp.In = cp.Out; cp.Out = tp; } }
                startAtTee = startFirst ? bwdAtTee : fwdAtTee; endAtTee = startFirst ? fwdAtTee : bwdAtTee;
            }
            double rMain = chain.Where(c => c.Kind == "pipe").Max(c => c.Od) / 2;
            var distinctOd = chain.Where(c => c.Kind == "pipe").Select(c => Math.Round(c.Od, 1)).Distinct().OrderBy(x => x).ToList();
            if (distinctOd.Count > 1) warnings.Add("The pipes of the route have different outer diameters (" + string.Join(", ", distinctOd) + "): the tracers keep the offset of the largest one, on the smaller pipes they run farther from the wall than " + gap + " mm.");
            double rOff = rMain + gap + rT, l0 = rOff * Math.Sin(Math.PI / 4), v0 = rOff * Math.Cos(Math.PI / 4), rU = l0;

            // ---- sections between the elbows ----------------------------------------------------------------------
            var secs = new List<RSec>(); var cur = new RSec();
            Point3d secStart = chain[0].InPos;
            foreach (var part in chain)
            {
                if (part.Kind == "elbow")
                {
                    cur.EndPt = part.InPos; cur.ElbowAfter = part; secs.Add(cur);
                    cur = new RSec(); secStart = part.OutPos;
                    continue;
                }
                if (cur.Parts.Count == 0 && secs.Count == 0) secStart = part.InPos;
                cur.Parts.Add(part);
            }
            cur.EndPt = chain[chain.Count - 1].OutPos; secs.Add(cur);
            Point3d startPt = chain[0].InPos;
            for (int k = 0; k < secs.Count; k++)
            {
                var sc = secs[k];
                if (sc.Parts.Count == 0) throw new InvalidOperationException("Two elbows follow each other: not supported.");
                sc.A0 = k == 0 ? startPt : secs[k - 1].ElbowAfter.OutPos;
                var dv = sc.EndPt - sc.A0; sc.Len = dv.Length;
                if (sc.Len < 1) throw new InvalidOperationException("A section of the route has no length.");
                sc.D = dv.GetNormal();
            }
            if (secs.Count > 1)
                for (int k = 0; k + 1 < secs.Count; k++)
                {
                    var el = secs[k].ElbowAfter; var dk = secs[k].D; var dn = secs[k + 1].D;
                    if (Math.Abs(dk.DotProduct(dn)) > 0.05) throw new InvalidOperationException("The elbow " + el.H + " is not a 90 degree one: only 90 degree elbows are supported.");
                    secs[k].Re = el.InPos.DistanceTo(el.OutPos) / Math.Sqrt(2);
                    secs[k].Pc = el.InPos + dk * secs[k].Re; secs[k].Cc = el.InPos + dn * secs[k].Re;
                }

            // ---- the offset frames: view 3 on every horizontal run (two tracers at 45 degrees below the axis, both sides) ---------
            // a riser takes its start frame from the horizontal run below it and its end frame from the one above it; when they differ the tracers twist round the riser
            if (!secs.Any(sc => Math.Abs(sc.D.Z) < 0.5)) throw new InvalidOperationException("The route has no horizontal section to take the tracer side from.");
            Matrix3d TurnRot(int k)
            {
                var ax = secs[k].D.CrossProduct(secs[k + 1].D).GetNormal();
                var rot = Matrix3d.Rotation(Math.PI / 2, ax, Point3d.Origin);
                if (secs[k].D.TransformBy(rot).DotProduct(secs[k + 1].D) < 0.99) rot = Matrix3d.Rotation(-Math.PI / 2, ax, Point3d.Origin);
                return rot;
            }
            // view 3 on every horizontal run; a riser carries the tracers on straight vertical lines (no twist) that meet both neighbouring horizontal runs with two 90 degree bends
            // (the user's sample): the vertical line of tracer sigma stands at sigma * lj * Combo, i.e. at the lateral offset lj of the run below and of the run above.
            // lj is the smallest of a few values (l0 = view 3 itself first) with which the line and the corners keep clear of the elbows and the risers.
            double lJ = Math.Ceiling(rOff) + 8;
            {
                // After a riser that turns the line back (the horizontal runs on both sides go in opposite directions) the side "left of the direction" is the other physical
                // side: the lateral axis of the following runs is mirrored so that each tracer keeps its physical side of the pipe all along the route.
                Vector3d prevDir = default; bool havePrev = false, flipSide = false;
                for (int k = 0; k < secs.Count; k++)
                {
                    var sc = secs[k];
                    sc.IsRiser = Math.Abs(sc.D.Z) >= 0.5;
                    if (sc.IsRiser) continue;
                    if (havePrev && k > 0 && secs[k - 1].IsRiser && prevDir.DotProduct(sc.D) < -0.9) flipSide = !flipSide;
                    prevDir = sc.D; havePrev = true;
                    var l0v = sc.D.CrossProduct(Vector3d.ZAxis).GetNormal();
                    sc.W = sc.D.CrossProduct(l0v).GetNormal();              // always down
                    sc.L = flipSide ? l0v.Negate() : l0v;
                    sc.L2 = sc.L; sc.W2 = sc.W; sc.Twist = 0;
                }
            }
            for (int k = 0; k < secs.Count; k++)
            {
                var sc = secs[k]; if (!sc.IsRiser) continue;
                bool hasP = k > 0 && !secs[k - 1].IsRiser, hasN = k + 1 < secs.Count && !secs[k + 1].IsRiser;
                if (!hasP && !hasN) throw new InvalidOperationException("Section " + k + " is a riser with no horizontal run next to it.");
                Vector3d Lp = hasP ? secs[k - 1].L : secs[k + 1].L, Ln = hasN ? secs[k + 1].L : secs[k - 1].L;
                sc.Gd = (hasP ? secs[k - 1].D : secs[k + 1].D);
                sc.Gd = new Vector3d(sc.Gd.X, sc.Gd.Y, 0).GetNormal(); sc.Lpv = Lp; sc.Lnv = Ln;
                if (!(hasP && hasN)) sc.Combo = Lp;
                else if (Math.Abs(Lp.DotProduct(Ln)) < 0.3) { sc.Combo = Lp + Ln; sc.Perp = true; }
                else if (Lp.DotProduct(Ln) > 0.9) sc.Combo = Lp;
                else throw new InvalidOperationException("Riser " + k + " joins two horizontal runs of opposite directions: not supported.");
                sc.LjP = sc.LjM = sc.LbP = sc.LbM = l0; sc.L = Lp; sc.W = sc.D.CrossProduct(sc.L).GetNormal(); sc.L2 = sc.L; sc.W2 = sc.W; sc.Twist = 0;
            }
            double LJ(RSec rs, double sg, RSec hz) => (secs.IndexOf(hz) < secs.IndexOf(rs)) ? (sg > 0 ? rs.LjP : rs.LjM) : (rs.Perp ? (sg > 0 ? rs.LbP : rs.LbM) : (sg > 0 ? rs.LjP : rs.LjM));
            Vector3d Off(RSec sc, double sg) => !sc.IsRiser ? sg * l0 * sc.L + v0 * sc.W : sc.Perp ? sg * ((sg > 0 ? sc.LjP : sc.LjM) * sc.Lpv + (sg > 0 ? sc.LbP : sc.LbM) * sc.Lnv) : sg * (sg > 0 ? sc.LjP : sc.LjM) * sc.Combo + (sg > 0 ? sc.GP : sc.GM) * sc.Gd;
            double KA(RSec hz, RSec rs, double sg)
            {
                var vxy = rs.A0 + Off(rs, sg);                              // a point of the vertical line
                var hp = hz.A0 + sg * LJ(rs, sg, hz) * hz.L + v0 * hz.W;        // a point of the horizontal line at a = 0
                var dxy = new Vector3d(hz.D.X, hz.D.Y, 0);
                return ((vxy.X - hp.X) * dxy.X + (vxy.Y - hp.Y) * dxy.Y) / dxy.DotProduct(dxy);
            }
            Point3d KPt(RSec hz, RSec rs, double sg) => hz.A0 + hz.D * KA(hz, rs, sg) + sg * LJ(rs, sg, hz) * hz.L + v0 * hz.W;
            Point3d Pt(RSec s, double a, double lat, double v) => s.A0 + s.D * a + s.L * lat + s.W * v;

            // the main pipes and the elbows as cylinders: for the clearance of the corners next to the risers
            var mainCyls = new List<(Point3d p1, Point3d p2, double rad)>();
            foreach (var part in chain.Where(c => c.Kind == "pipe")) mainCyls.Add((part.InPos, part.OutPos, part.Od / 2));
            foreach (var sc in secs.Where(sc => sc.ElbowAfter != null))
            {
                var u0 = (sc.ElbowAfter.InPos - sc.Cc) / sc.Re; var u1 = (sc.ElbowAfter.OutPos - sc.Cc) / sc.Re; Point3d pa = sc.ElbowAfter.InPos;
                for (int kk = 1; kk <= 48; kk++)
                { double phi = Math.PI / 2 * kk / 48.0; var q2 = sc.Cc + (u0 * Math.Cos(phi) + u1 * Math.Sin(phi)) * sc.Re; mainCyls.Add((pa, q2, rMain)); pa = q2; }
            }
            double MainClear(Point3d q)
            {
                double best = double.MaxValue;
                foreach (var cy in mainCyls)
                {
                    var sg = cy.p2 - cy.p1; double len2 = sg.DotProduct(sg);
                    double tt = len2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, (q - cy.p1).DotProduct(sg) / len2));
                    best = Math.Min(best, (q - (cy.p1 + sg * tt)).Length - rT - cy.rad);
                }
                return best;
            }
            // the shift of a tracer next to the riser needs room on the neighbouring horizontal run (its S-bend between the corner bends)
            bool ShiftFits(int k, double sg)
            {
                var rs = secs[k];
                foreach (int nk in new[] { k - 1, k + 1 })
                {
                    if (nk < 0 || nk >= secs.Count || secs[nk].IsRiser) continue;
                    var hz = secs[nk]; double lj = LJ(rs, sg, hz);
                    if (lj - l0 < 0.5) continue;
                    if (hz.Len >= 15 * (lj - l0) + 300) continue;                       // a long run goes at a slight angle: no S-bend, no room needed
                    Detour(lj - l0, out double th, out double dg, double.MaxValue);
                    double rt = R * Math.Tan(th / 2), x = (lj - l0) / Math.Tan(th), ka = KA(hz, rs, sg);
                    bool other = nk < k ? (nk > 0 && secs[nk - 1].IsRiser) : (nk + 1 < secs.Count && secs[nk + 1].IsRiser);
                    if (nk > k) { if (Math.Max(pad, ka + R + 10) + 2 * rt + x > hz.Len - (other ? 100 : 20)) return false; }      // the run behind the riser: the shift is at its start
                    else { if (Math.Min(hz.Len - pad, ka - R - 10) - 2 * rt - x < (other ? 100 : 20)) return false; }               // the run before the riser: the shift is at its end
                }
                return true;
            }
            double RiserMin(int k, double sg)
            {
                var rs = secs[k]; bool hasP = k > 0 && !secs[k - 1].IsRiser, hasN = k + 1 < secs.Count && !secs[k + 1].IsRiser;
                var pts = new List<Point3d>();
                Point3d lo = hasP ? KPt(secs[k - 1], rs, sg) : rs.A0 + Off(rs, sg), hi = hasN ? KPt(secs[k + 1], rs, sg) : rs.A0 + rs.D * rs.Len + Off(rs, sg);
                int nn = Math.Max(2, (int)(lo.DistanceTo(hi) / 20));
                for (int i = 0; i <= nn; i++) pts.Add(lo + (hi - lo) * ((double)i / nn));                   // the vertical line
                if (hasP) { var h = secs[k - 1]; for (double t = 0; t <= R + 20; t += 15) pts.Add(lo - h.D * t); for (double t = 0; t <= R; t += 15) pts.Add(lo + (hi - lo).GetNormal() * t + (-h.D) * (R - t)); }
                if (hasN) { var h = secs[k + 1]; for (double t = 0; t <= 250; t += 15) pts.Add(hi + h.D * t); for (double t = 0; t <= R; t += 15) pts.Add(hi - (hi - lo).GetNormal() * t + h.D * (R - t)); }
                double mn = double.MaxValue; foreach (var q in pts) mn = Math.Min(mn, MainClear(q));
                return mn;
            }
            // the tracers keep the lateral offset of view 3 (no sideways shift, no S-bends: the user's sample); the vertical line of a riser stands diagonally to the pipe,
            // l0 ahead of or behind its axis: the side with the smaller touch of the elbows wins (two perpendicular runs leave no choice)
            double flexGap = Math.Max(0.0, gap - tol);   // the smallest accepted gap
            var riserTouch = new List<string>();
            for (int k = 0; k < secs.Count; k++)
            {
                var rs = secs[k]; if (!rs.IsRiser) continue;
                foreach (double sg in new[] { 1.0, -1.0 })
                {
                    double bestG = 0, bestC = double.MinValue;
                    bool okFound = false;
                    if (rs.Perp)
                    {
                        // two perpendicular runs: the diagonal place is fixed, only the lateral offsets against the two runs can grow; the smallest total shift that keeps the gap
                        var cand = new List<double> { l0 }.Concat(new[] { -9.0, -3, 0.5, 3, 11, 21, 36, 60 }.Select(d => rOff + d)).Where(x => x < l0 + 0.1 || x > l0 + 6.5).Distinct().ToList();   // relative to the offset of the tracer from the axis: any DN and any gap
                        var pairs = cand.SelectMany(a => cand.Select(b => (a, b))).OrderBy(t => (t.a - l0) + (t.b - l0)).ThenBy(t => Math.Max(t.a, t.b)).ToList();
                        double keepA = l0, keepB = l0, keepC = double.MinValue;
                        foreach (var (pa, pb) in pairs)
                        {
                            if (sg > 0) { rs.LjP = pa; rs.LbP = pb; } else { rs.LjM = pa; rs.LbM = pb; }
                            if (!ShiftFits(k, sg)) continue;
                            double c = RiserMin(k, sg);
                            if (c > keepC) { keepC = c; keepA = pa; keepB = pb; }
                            if (c >= flexGap) { keepC = c; keepA = pa; keepB = pb; okFound = true; break; }
                        }
                        if (sg > 0) { rs.LjP = keepA; rs.LbP = keepB; } else { rs.LjM = keepA; rs.LbM = keepB; }
                        bestC = keepC;
                        // a short run between two risers cannot shift the tracer in between: both risers take the same lateral offset (the whole run holds it)
                        int hk0 = k - 1;
                        if (!okFound && hk0 > 0 && !secs[hk0].IsRiser && secs[hk0 - 1].IsRiser)
                        {
                            var rp = secs[hk0 - 1]; int pk = hk0 - 1;
                            double sA = sg > 0 ? rp.LjP : rp.LjM, sB = sg > 0 ? rp.LbP : rp.LbM;
                            double keepAA = keepA, keepBB = keepB; bool coupled = false;
                            foreach (double lat in new[] { 50.0, 56, 59.5, 62, 70, 80, 95 })
                            {
                                foreach (double bb2 in cand)
                                {
                                    if (sg > 0) { rp.LbP = lat; if (rp.Perp) rp.LbP = lat; else rp.LjP = lat; rs.LjP = lat; rs.LbP = bb2; } else { rp.LbM = lat; if (!rp.Perp) rp.LjM = lat; rs.LjM = lat; rs.LbM = bb2; }
                                    double c1 = RiserMin(pk, sg), c2 = RiserMin(k, sg);
                                    if (Math.Min(c1, c2) >= flexGap) { coupled = true; keepAA = lat; keepBB = bb2; bestC = Math.Min(c1, c2); break; }
                                }
                                if (coupled) break;
                            }
                            if (!coupled)
                            {
                                if (sg > 0) { rp.LjP = rp.Perp ? rp.LjP : sA; rp.LbP = sB; rs.LjP = keepA; rs.LbP = keepB; } else { rp.LjM = rp.Perp ? rp.LjM : sA; rp.LbM = sB; rs.LjM = keepA; rs.LbM = keepB; }
                            }
                        }
                    }
                    else
                    {
                        // first the user's way for a riser between runs that go along one line (a U-turn up and back, a Z): the vertical line stands BESIDE the pipe at the level of
                        // its axis (along the run: 0) at the side offset "pipe + tracer + a few mm"; the runs leave view 3 for that offset by a smooth S-bend before the riser
                        bool sideOk = false;
                        foreach (double extra in new[] { 0.0, 4, 8, 12, 18, 25, 35 })
                        {
                            double ljs = rOff + extra;
                            if (ljs > l0 && ljs < l0 + 6.5) continue;                   // two 15 degree bends make at least 6.1 mm
                            if (sg > 0) { rs.LjP = ljs; rs.GP = 0; } else { rs.LjM = ljs; rs.GM = 0; }
                            if (!ShiftFits(k, sg)) continue;
                            double cs = RiserMin(k, sg);
                            if (cs >= flexGap) { sideOk = true; bestC = cs; bestG = 0; break; }
                        }
                        if (sideOk) { okFound = true; }
                        else
                        {
                            if (sg > 0) rs.LjP = l0; else rs.LjM = l0;                 // no room for that: the vertical stands ahead of / behind the riser, at the lateral offset of view 3
                        foreach (double g in new[] { l0, -l0 }.Concat(new[] { 3.0, 21, 41, 51, 61, 81 }.SelectMany(d => new[] { rOff + d, -(rOff + d) })))      // the vertical stands diagonally: l0 ... 140 mm ahead of or behind the axis
                        {
                            if (sg > 0) rs.GP = g; else rs.GM = g;
                            double c = RiserMin(k, sg);
                            if (c > bestC && !okFound) { bestC = c; bestG = g; }
                            if (c >= flexGap) { bestC = c; bestG = g; okFound = true; break; }      // the smallest shift along the run that keeps the gap
                        }
                        if (sg > 0) rs.GP = bestG; else rs.GM = bestG;
                        }
                    }
                    if (bestC < flexGap) riserTouch.Add("riser " + k + " tracer " + (sg > 0 ? "+" : "-") + " " + Math.Round(bestC, 1) + " mm");
                }
            }
            if (riserTouch.Count > 0) warnings.Add("Clearance of the tracers to the elbows next to the risers is less than " + flexGap + " mm: " + string.Join("; ", riserTouch) + ".");
            warnings.Add("Lateral offsets of the tracers next to the risers (+ / -): " + string.Join("; ", secs.Select((q, qi) => (q, qi)).Where(t => t.q.IsRiser).Select(t => "riser " + t.qi + " before/after +: " + Math.Round(t.q.LjP, 1) + "/" + Math.Round(t.q.Perp ? t.q.LbP : t.q.LjP, 1) + " -: " + Math.Round(t.q.LjM, 1) + "/" + Math.Round(t.q.Perp ? t.q.LbM : t.q.LjM, 1) + " shift along " + Math.Round(t.q.GP, 0) + "/" + Math.Round(t.q.GM, 0))) + ".");

            // ---- boxes of the wide parts, in the coordinates of their section ---------------------------------------
            foreach (var sc in secs)
                foreach (var part in sc.Parts.Where(p => p.Kind != "pipe"))
                {
                    var b = new RBox { H = part.H, Desc = part.Kind + " " + part.Desc, A1 = double.MaxValue, A2 = double.MinValue, L1 = double.MaxValue, L2 = double.MinValue, W1 = double.MaxValue, W2 = double.MinValue };
                    using (doc.LockDocument()) using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        var ext = ((Entity)tr.GetObject(part.Id, OpenMode.ForRead)).GeometricExtents;
                        foreach (var x in new[] { ext.MinPoint.X, ext.MaxPoint.X }) foreach (var y in new[] { ext.MinPoint.Y, ext.MaxPoint.Y }) foreach (var z in new[] { ext.MinPoint.Z, ext.MaxPoint.Z })
                        {
                            var c = new Point3d(x, y, z) - sc.A0;
                            double a = c.DotProduct(sc.D), l = c.DotProduct(sc.L), w = c.DotProduct(sc.W);
                            b.A1 = Math.Min(b.A1, a); b.A2 = Math.Max(b.A2, a); b.L1 = Math.Min(b.L1, l); b.L2 = Math.Max(b.L2, l); b.W1 = Math.Min(b.W1, w); b.W2 = Math.Max(b.W2, w);
                        }
                        tr.Commit();
                    }
                    sc.Boxes.Add(b);
                }

            // ---- the lateral shift of the tracers near the risers: lateral lj at the elbow (only where lj differs from l0), back to l0 (view 3) on the straight run ------
            for (int k = 0; k < secs.Count; k++)
            {
                var sc = secs[k]; sc.JS = 0; sc.JE = sc.Len; sc.Hold = false;
                if (sc.IsRiser) continue;
                bool pr = k > 0 && secs[k - 1].IsRiser, nr = k + 1 < secs.Count && secs[k + 1].IsRiser;
                if (!pr && !nr) continue;
                double SpanOf(double lj, double kaa, bool start)
                {
                    if (lj - l0 < 0.5) return 0;
                    if (sc.Len >= 15 * (lj - l0) + 300) return 0;                      // a long run goes at a slight angle, no room is kept for an S-bend
                    Detour(lj - l0, out double thj, out double dgj, double.MaxValue);
                    double rtj0 = R * Math.Tan(thj / 2), xj0 = (lj - l0) / Math.Tan(thj);
                    return start ? Math.Max(pad, kaa + R + 10) + 2 * rtj0 + xj0 : sc.Len - (Math.Min(sc.Len - pad, kaa - R - 10) - 2 * rtj0 - xj0);
                }
                if (pr) sc.JS = new[] { 1.0, -1.0 }.Max(sg => SpanOf(LJ(secs[k - 1], sg, sc), KA(sc, secs[k - 1], sg), true));
                if (nr) sc.JE = sc.Len - new[] { 1.0, -1.0 }.Max(sg => SpanOf(LJ(secs[k + 1], sg, sc), KA(sc, secs[k + 1], sg), false));
                if (sc.JS > sc.JE - 20)
                {
                    sc.Hold = pr && nr;
                    if (!sc.Hold) throw new InvalidOperationException("Section " + k + " is too short for the shift of the tracers next to the riser.");
                    foreach (double sg in new[] { 1.0, -1.0 })
                        if (Math.Abs(LJ(secs[k - 1], sg, sc) - LJ(secs[k + 1], sg, sc)) > 0.5) throw new InvalidOperationException("Section " + k + " between two risers is too short for the different shifts of the tracers.");
                    sc.JS = sc.Len / 2; sc.JE = sc.Len / 2;
                }
            }

            // ---- vents and drains: vertical branches welded into the horizontal pipes of the route (dynamic ports Dn) -----------------
            var branches = new List<RBranch>(); var branchByName = new Dictionary<string, RBranch>(); var branchRoutes = new List<string>();
            var mainSet = new HashSet<string>(chain.Select(c => c.H), StringComparer.OrdinalIgnoreCase);
            for (int k = 0; k < secs.Count; k++)
            {
                var sc = secs[k];
                foreach (var p in sc.Parts.Where(p => p.Kind == "pipe"))
                    foreach (var q in SafePorts(doc, p.Id).Where(q => Regex.IsMatch(q.name, @"^D\d+$")))
                    {
                        double ab = (q.pos - sc.A0).DotProduct(sc.D);
                        var linksD = ConnList(doc, p.Id).Where(c => (c.h1 == p.H && c.p1 == q.name) || (c.h2 == p.H && c.p2 == q.name)).ToList();
                        if (linksD.Count != 1) continue;
                        string nb = linksD[0].h1 == p.H ? linksD[0].h2 : linksD[0].h1;
                        var nbId = IdFromHandle(doc.Database, nb);
                        if (nbId.ObjectClass.Name.Contains("Support")) continue;
                        if (!heatBranches) { warnings.Add("Branch " + p.H + "." + q.name + " is not heated (heat_branches=false)."); continue; }
                        bool riserBr = false;
                        if (Math.Abs(q.dir.DotProduct(sc.D)) < 0.3 && Math.Abs(sc.Twist) < 1e-6) riserBr = true;
                        else { warnings.Add("NOT HEATED: branch " + p.H + "." + q.name + " (" + DescById(doc, dlm, nbId).Trim() + ") is not perpendicular to its pipe: heat it by hand."); continue; }
                        if (ab < 0 || ab > sc.Len) continue;
                        var br = new RBranch { Name = p.H + "." + q.name, Pipe = p.H, Sec = k, A = ab, Riser = riserBr, Origin = q.pos, Down = q.dir.Z < 0, ZMin = double.MaxValue, ZMax = double.MinValue };
                        var seenB = new HashSet<string> { p.H }; var queue = new Queue<string>(); queue.Enqueue(nb);
                        while (queue.Count > 0 && seenB.Count < 60)
                        {
                            string hb = queue.Dequeue();
                            if (!seenB.Add(hb) || mainSet.Contains(hb)) continue;
                            var idb = IdFromHandle(doc.Database, hb);
                            br.Handles.Add(hb);
                            foreach (var c in ConnList(doc, idb)) { string o = c.h1 == hb ? c.h2 : c.h1; if (o != null && !seenB.Contains(o)) queue.Enqueue(o); }
                            if (idb.ObjectClass.Name.Contains("Connector")) continue;
                            using (doc.LockDocument()) using (var tr = doc.Database.TransactionManager.StartTransaction())
                            {
                                var ext = ((Entity)tr.GetObject(idb, OpenMode.ForRead)).GeometricExtents;
                                br.Rects.Add((ext.MinPoint.X, ext.MaxPoint.X, ext.MinPoint.Y, ext.MaxPoint.Y, ext.MinPoint.Z, ext.MaxPoint.Z));
                                br.Info.Add(hb + " " + idb.ObjectClass.Name.Replace("AcPpDb3d", "") + " z " + Math.Round(ext.MinPoint.Z, 0) + ".." + Math.Round(ext.MaxPoint.Z, 0) + " " + DescById(doc, dlm, idb).Trim());
                                br.ZMin = Math.Min(br.ZMin, ext.MinPoint.Z); br.ZMax = Math.Max(br.ZMax, ext.MaxPoint.Z);
                                tr.Commit();
                            }
                        }
                        if (br.Rects.Count == 0) { warnings.Add("NOT HEATED: branch " + br.Name + ": no parts found behind the dynamic port."); continue; }
                        if (riserBr)
                        {
                            br.B = (q.dir - sc.D * q.dir.DotProduct(sc.D)).GetNormal(); br.C = sc.D.CrossProduct(br.B).GetNormal(); br.Bmax = double.MinValue;
                            foreach (var r in br.Rects)
                                foreach (var x in new[] { r.x1, r.x2 }) foreach (var y in new[] { r.y1, r.y2 }) foreach (var z in new[] { r.z1, r.z2 })
                                    br.Bmax = Math.Max(br.Bmax, (new Point3d(x, y, z) - q.pos).DotProduct(br.B));
                        }
                        SetHeavySides(br, sc);
                        branches.Add(br); branchByName[br.Name] = br;
                    }
                // ---- the branches of the tees of the route: the line goes on beyond the tee, the supply tracer runs along it to the end and back ------------------
                foreach (var tee in sc.Parts.Where(p => p.Kind == "tee" && p.In != null && p.Out != null))
                {
                    try
                    {
                        var bp = tee.Ports.FirstOrDefault(q => q.name != tee.In && q.name != tee.Out);
                        if (bp.name == null) continue;
                        if (!heatBranches) { warnings.Add("The branch of tee " + tee.H + " is not heated (heat_branches=false)."); continue; }
                        double ab = (bp.pos - sc.A0).DotProduct(sc.D);
                        if (Math.Abs(bp.dir.DotProduct(sc.D)) > 0.3) { warnings.Add("NOT HEATED: the branch of tee " + tee.H + " is not perpendicular to the run: heat it by hand."); continue; }
                        if (ab < 0 || ab > sc.Len) continue;
                        var br = new RBranch { Name = tee.H + "." + bp.name, Pipe = tee.H, TeeH = tee.H, Sec = k, A = ab, Riser = true, Origin = bp.pos, ZMin = double.MaxValue, ZMax = double.MinValue };
                        br.B = (bp.dir - sc.D * bp.dir.DotProduct(sc.D)).GetNormal(); br.C = sc.D.CrossProduct(br.B).GetNormal();
                        // the parts of the branch, from the tee on: pipes as cylinders, valves and flanges as boxes; the walk stops at the first elbow
                        var links = ConnList(doc, tee.Id).Where(c => (c.h1 == tee.H && c.p1 == bp.name) || (c.h2 == tee.H && c.p2 == bp.name)).ToList();
                        if (links.Count == 0) { warnings.Add("The branch port " + bp.name + " of tee " + tee.H + " is free: nothing to heat."); continue; }
                        var seenB = new HashSet<string> { tee.H }; var queue = new Queue<string>(); queue.Enqueue(links[0].h1 == tee.H ? links[0].h2 : links[0].h1);
                        bool bends = false; string firstPipe = null;
                        while (queue.Count > 0 && seenB.Count < 80)
                        {
                            string hb = queue.Dequeue();
                            if (hb == null || !seenB.Add(hb) || mainSet.Contains(hb)) continue;
                            var idb = IdFromHandle(doc.Database, hb);
                            br.Handles.Add(hb);
                            string cls = idb.ObjectClass.Name;
                            bool stop = false;
                            if (!cls.Contains("Connector"))
                            {
                                string desc = DescById(doc, dlm, idb).Trim();
                                if (cls == "AcPpDb3dPipe")
                                {
                                    if (firstPipe == null) firstPipe = hb;
                                    var pr = SafePorts(doc, idb); var s1 = pr.FirstOrDefault(q => q.name == "S1"); var s2 = pr.FirstOrDefault(q => q.name == "S2");
                                    double od;
                                    using (doc.LockDocument()) using (var tr = doc.Database.TransactionManager.StartTransaction()) { od = ((Pipe)tr.GetObject(idb, OpenMode.ForRead)).OuterDiameter; tr.Commit(); }
                                    if (s1.name != null && s2.name != null) { br.Cyls.Add((s1.pos, s2.pos, od / 2)); br.Info.Add(hb + " Pipe " + Math.Round(s1.pos.DistanceTo(s2.pos), 0) + " mm " + desc); }
                                }
                                else if (Regex.IsMatch(desc, "Отвод|Elbow")) { bends = true; stop = true; }       // the branch turns here: it is heated up to this point
                                else
                                {
                                    using (doc.LockDocument()) using (var tr = doc.Database.TransactionManager.StartTransaction())
                                    {
                                        var ext = ((Entity)tr.GetObject(idb, OpenMode.ForRead)).GeometricExtents;
                                        br.Rects.Add((ext.MinPoint.X, ext.MaxPoint.X, ext.MinPoint.Y, ext.MaxPoint.Y, ext.MinPoint.Z, ext.MaxPoint.Z));
                                        br.Info.Add(hb + " " + cls.Replace("AcPpDb3d", "") + " " + desc);
                                        tr.Commit();
                                    }
                                }
                            }
                            if (stop) continue;
                            foreach (var c in ConnList(doc, idb)) { string o = c.h1 == hb ? c.h2 : c.h1; if (o != null && !seenB.Contains(o)) queue.Enqueue(o); }
                        }
                        if (br.Cyls.Count == 0 && br.Rects.Count == 0) { warnings.Add("NOT HEATED: nothing found behind the branch port of tee " + tee.H + "."); continue; }
                        // the end of the branch along B, measured from the axis of the run
                        br.Bmax = double.MinValue;
                        foreach (var cy in br.Cyls) foreach (var e in new[] { cy.p1, cy.p2 }) br.Bmax = Math.Max(br.Bmax, (e - bp.pos).DotProduct(br.B) + cy.rad);
                        foreach (var r in br.Rects)
                            foreach (var x in new[] { r.x1, r.x2 }) foreach (var y in new[] { r.y1, r.y2 }) foreach (var z in new[] { r.z1, r.z2 })
                                br.Bmax = Math.Max(br.Bmax, (new Point3d(x, y, z) - bp.pos).DotProduct(br.B));
                        if (bends)
                        {
                            // the branch goes on round an elbow: the legs of the dip follow all its pipes without a break (one tracer goes out along the whole branch and comes back);
                            // when that is not possible the branch is heated by a loop of its own (a break in the tracer, a warning says so)
                            try
                            {
                                br.Chain = BuildBranchChain(tee, bp); br.FirstPipe = firstPipe;
                                br.Bmax = br.Chain[0].Len - 30 - overshoot + dipTail + Rdip;               // the first straight leg stops 30 mm in front of the first elbow
                                foreach (var bs in br.Chain.Skip(1)) foreach (var bpart in bs.Parts.Where(q => q.Kind == "pipe")) br.Cyls.Add((bpart.InPos, bpart.OutPos, bpart.Od / 2));
                            }
                            catch (Exception bex)
                            {
                                if (firstPipe != null) { branchRoutes.Add(firstPipe); warnings.Add("The branch of tee " + tee.H + " goes on round elbows and cannot be followed by the dip (" + bex.Message + "): it is heated by a loop of its own, the tracer is interrupted at the tee."); }
                                else warnings.Add("NOT HEATED: the branch of tee " + tee.H + " has no pipe in front of its elbow.");
                                continue;
                            }
                        }
                        SetHeavySides(br, sc);
                        branches.Add(br); branchByName[br.Name] = br;
                    }
                    catch (Exception ex) { warnings.Add("NOT HEATED: the branch of tee " + tee.H + " could not be read: " + ex.Message); }
                }
            }
            // the sections of a branch that goes on round elbows (from the branch port of the tee to its free end), the same construction as the sections of the route
            List<RSec> BuildBranchChain(RPart tee, (string name, Point3d pos, Vector3d dir) bp)
            {
                var first = MakeRPart(doc, dlm, tee.Id);
                var walked = WalkRoute(doc, dlm, first, bp.name, out bool endsAtTee);
                var partsB = walked.Skip(1).ToList();
                if (partsB.Count == 0) throw new InvalidOperationException("nothing behind the branch port");
                if (partsB.Any(q => q.Kind == "tee")) throw new InvalidOperationException("a tee inside the branch");
                var list = new List<RSec>(); var cur2 = new RSec(); Point3d a0 = bp.pos;
                foreach (var part in partsB)
                {
                    if (part.Kind == "elbow")
                    {
                        if (cur2.Parts.Count == 0) throw new InvalidOperationException("two elbows follow each other");
                        cur2.A0 = a0; cur2.EndPt = part.InPos; cur2.ElbowAfter = part; list.Add(cur2);
                        cur2 = new RSec(); a0 = part.OutPos; continue;
                    }
                    cur2.Parts.Add(part);
                }
                if (cur2.Parts.Count == 0) throw new InvalidOperationException("the branch ends in an elbow");
                cur2.A0 = a0; cur2.EndPt = partsB[partsB.Count - 1].OutPos; list.Add(cur2);
                for (int i = 0; i < list.Count; i++)
                {
                    var sc2 = list[i]; var dv = sc2.EndPt - sc2.A0; sc2.Len = dv.Length;
                    if (sc2.Len < 1) throw new InvalidOperationException("a section of the branch has no length");
                    sc2.D = dv.GetNormal();
                }
                for (int i = 0; i + 1 < list.Count; i++)
                {
                    var el = list[i].ElbowAfter; var dk = list[i].D; var dn = list[i + 1].D;
                    if (Math.Abs(dk.DotProduct(dn)) > 0.05) throw new InvalidOperationException("an elbow of the branch is not a 90 degree one");
                    list[i].Re = el.InPos.DistanceTo(el.OutPos) / Math.Sqrt(2);
                    list[i].Pc = el.InPos + dk * list[i].Re; list[i].Cc = el.InPos + dn * list[i].Re;
                }
                return list;
            }
            // the two legs of the dip along a branch with elbows: vertices (without the first corners q1 / q2 and the closing bend), the radii of the corners, the direction of the last section
            bool BranchLegs(RBranch br, Vector3d runDir, double cdir, double lat, double shift, out List<Point3d> la, out List<Point3d> lb, out List<(Point3d p, double r)> radii, out Vector3d dLast, out string why)
            {
                la = new List<Point3d>(); lb = new List<Point3d>(); radii = new List<(Point3d, double)>(); dLast = default; why = null;
                var ch = br.Chain;
                Vector3d oA = br.C * (cdir * lat) + runDir * (shift - Rdip), oB = br.C * (cdir * lat) + runDir * (shift + Rdip);
                for (int i = 0; i + 1 < ch.Count; i++)
                {
                    var dk = ch[i].D; var dn = ch[i + 1].D;
                    var ax = dk.CrossProduct(dn).GetNormal();
                    var rot = Matrix3d.Rotation(Math.PI / 2, ax, Point3d.Origin);
                    if (dk.TransformBy(rot).DotProduct(dn) < 0.99) rot = Matrix3d.Rotation(-Math.PI / 2, ax, Point3d.Origin);
                    var oA2 = oA.TransformBy(rot); var oB2 = oB.TransformBy(rot);
                    foreach (var (o1, o2, list) in new[] { (oA, oA2, la), (oB, oB2, lb) })
                    {
                        var corner = ch[i].Pc + o1 + dk * ((o2 - o1).DotProduct(dk));
                        double rT2 = ch[i].Re - o1.DotProduct(dn);
                        if (rT2 < 25) { why = "the radius of the tracer round the elbow would be only " + Math.Round(rT2, 1) + " mm"; return false; }
                        radii.Add((corner, rT2)); list.Add(corner);
                    }
                    oA = oA2; oB = oB2;
                }
                var last = ch[ch.Count - 1]; dLast = last.D;
                double legEnd = last.Len + overshoot - (dipTail + Rdip);
                if (legEnd < 60) { why = "the last section of the branch is too short for the closing bend"; return false; }
                la.Add(last.A0 + last.D * legEnd + oA); lb.Add(last.A0 + last.D * legEnd + oB);
                return true;
            }
            // the handwheel and the stem make the branch reach farther to one side: the tracer must dip on the other side (user)
            void SetHeavySides(RBranch br, RSec sc)
            {
                double cMax = double.MinValue, cMin = double.MaxValue, dMax = double.MinValue, dMin = double.MaxValue;
                void Take(Point3d q)
                {
                    var v = q - br.Origin; double c = v.DotProduct(br.C), d = v.DotProduct(sc.D);
                    cMax = Math.Max(cMax, c); cMin = Math.Min(cMin, c); dMax = Math.Max(dMax, d); dMin = Math.Min(dMin, d);
                }
                foreach (var r in br.Rects)
                    foreach (var x in new[] { r.x1, r.x2 }) foreach (var y in new[] { r.y1, r.y2 }) foreach (var z in new[] { r.z1, r.z2 }) Take(new Point3d(x, y, z));
                if (br.Rects.Count == 0) return;
                double ac = cMax + cMin, ad = dMax + dMin;          // > 0: reaches farther towards + (twice the offset of the centre)
                br.HeavyC = Math.Abs(ac) > 6 ? Math.Sign(ac) : 0;           // the box of a valve with a handwheel is lopsided by a few mm already
                br.HeavyD = Math.Abs(ad) > 6 ? Math.Sign(ad) : 0;
            }
            double VOfZ(RSec sc, double a, double z) => (z - (sc.A0.Z + sc.D.Z * a)) / sc.W.Z;
            bool RectsClear(RBranch br, Point3d q)
            {
                foreach (var cy in br.Cyls)
                {
                    var sg = cy.p2 - cy.p1; double len2 = sg.DotProduct(sg);
                    double tt = len2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, (q - cy.p1).DotProduct(sg) / len2));
                    if ((q - (cy.p1 + sg * tt)).Length < cy.rad + rT + gap - 0.1) return false;
                }
                foreach (var r in br.Rects)
                    if (q.Z >= r.z1 - rT - gap && q.Z <= r.z2 + rT + gap)
                    {
                        double dx = Math.Max(Math.Max(r.x1 - q.X, 0), q.X - r.x2), dy = Math.Max(Math.Max(r.y1 - q.Y, 0), q.Y - r.y2);
                        if (Math.Sqrt(dx * dx + dy * dy) < rT + gap - 0.1) return false;
                    }
                return true;
            }
            bool DipOk(RBranch br, double sg, double lat, double shift)
            {
                var sc = secs[br.Sec];
                double vApex = br.Down ? VOfZ(sc, br.A, br.ZMin - overshoot) : VOfZ(sc, br.A, br.ZMax + overshoot);
                double vPort = br.Down ? vApex - (dipTail + Rdip) : vApex + (dipTail + Rdip);
                if (br.Down ? vPort < v0 + Rdip + 0.5 : vPort > v0 - Rdip - 0.5) return false;
                double aL = br.A + shift - Rdip, aR = br.A + shift + Rdip;
                if (aL < 40 || aR > sc.Len - 40) return false;
                foreach (double a in new[] { aL, aR })
                    for (double vv = v0; ; vv += br.Down ? 15 : -15)
                    {
                        if (br.Down ? vv > vPort : vv < vPort) vv = vPort;
                        if (!RectsClear(br, Pt(sc, a, sg * lat, vv))) return false;
                        if (vv == vPort) break;
                    }
                var p1 = Pt(sc, aL, sg * lat, vPort); var p2 = Pt(sc, aR, sg * lat, vPort);
                var X = br.Down ? Vector3d.ZAxis : Vector3d.ZAxis.Negate(); var Y = (p2 - p1).GetNormal();
                var cen = p1 - X * dipTail + Y * Rdip;
                for (int k = 0; k <= 24; k++)
                {
                    double phi = Math.PI * k / 24.0;
                    if (!RectsClear(br, cen + (Y.Negate() * Math.Cos(phi) - X * Math.Sin(phi)) * Rdip)) return false;
                }
                return true;
            }
            // a horizontal branch of a riser: the tracer shifts sideways (along C), turns into the branch direction B at height A-Rdip, runs out beyond the end of the branch,
            // makes the 180 degree bend (in the plane of the riser axis and B) and comes back at height A+Rdip
            bool RDipOk(RBranch br, double sg, double cdir, double lat, double shift)
            {
                // the tracer shifts sideways (along C) to 'lat', turns into the branch direction B at height A+shift-Rdip, runs out beyond the end of the branch,
                // makes the 180 degree bend (radius Rdip, in the plane of the riser axis and B) and comes back at height A+shift+Rdip; the legs stay on the tracer's own side
                var sc = secs[br.Sec]; var o = Off(sc, sg); double b0 = o.DotProduct(br.B), oC = o.DotProduct(br.C);
                if (cdir != (oC >= 0 ? 1.0 : -1.0)) return false;
                double vPortB = br.Bmax + overshoot - (dipTail + Rdip);
                if (vPortB < b0 + Rdip + 0.5) return false;
                double a2 = br.A + shift - Rdip, a3 = br.A + shift + Rdip;
                if (a2 < 40 || a3 > sc.Len - 40) return false;
                double dlt = Math.Abs(cdir * lat - oC); if (dlt > 0.5 && dlt < 6.3) return false;   // two 15 degree bends make at least 6.1 mm
                double ctr = CompactTrans(Math.Abs(cdir * lat - oC));
                if (a3 + Rdip + pad + ctr + 20 > (br.Sec == secs.Count - 1 ? sc.Len + endMargin - tailU : sc.JE)) return false;
                if ((br.Sec > 0 || sc.JS > 0) && a2 - Rdip - pad - ctr - 20 < sc.JS) return false;
                foreach (double a in new[] { a2, a3 })
                    for (double bb = b0; ; bb += 15)
                    {
                        if (bb > vPortB) bb = vPortB;
                        if (Math.Sqrt(lat * lat + bb * bb) < rOff - 0.5) return false;
                        if (!RectsClear(br, sc.A0 + sc.D * a + br.C * (cdir * lat) + br.B * bb)) return false;
                        if (bb == vPortB) break;
                    }
                var p1 = sc.A0 + sc.D * a2 + br.C * (cdir * lat) + br.B * vPortB; var p2 = sc.A0 + sc.D * a3 + br.C * (cdir * lat) + br.B * vPortB;
                var X = br.B.Negate(); var Y = (p2 - p1).GetNormal(); var cen = p1 - X * dipTail + Y * Rdip;
                for (int k = 0; k <= 24; k++)
                {
                    double phi = Math.PI * k / 24.0;
                    if (!RectsClear(br, cen + (Y.Negate() * Math.Cos(phi) - X * Math.Sin(phi)) * Rdip)) return false;
                }
                return true;
            }
            (double cost, double lat, double shift)? RBestFor(RBranch br, double sg)
            {
                var lats = new List<double>();
                foreach (int dg in new[] { 15, 30, 45, 60 }) lats.Add(l0 + 2 * R * (1 - Math.Cos(dg * Math.PI / 180)));
                for (int k = 0; k <= 30; k++) lats.Add(rOff + 1 + 10 * k);
                double bestCost = double.MaxValue, bl = 0, bsh = 0, bs = 1;
                foreach (double cdir in new[] { 1.0, -1.0 })
                    foreach (double lat in lats)
                        foreach (double shift in new[] { 0.0, 40, -40, 80, -80, 120, -120, 160, -160, 200, -200, 260, -260 })
                        {
                            double cost = lat + 2.0 * Math.Abs(shift) + (cdir == br.HeavyC ? 400 : 0) + (br.HeavyD != 0 && Math.Sign(shift) == br.HeavyD ? 150 : 0);
                            if (cost >= bestCost) continue;
                            if (RDipOk(br, sg, cdir, lat, shift)) { bestCost = cost; bl = lat; bsh = shift; bs = cdir; }
                        }
                if (bestCost == double.MaxValue) return null;
                br.SignBySide[sg] = bs;
                return (bestCost, bl, bsh);
            }
            (double cost, double lat, double shift)? BestFor(RBranch br, double sg)
            {
                if (br.Riser) return RBestFor(br, sg);
                var lats = new List<double>();
                foreach (int dg in new[] { 15, 30, 45, 60 }) lats.Add(l0 + 2 * R * (1 - Math.Cos(dg * Math.PI / 180)));
                for (int k = 1; k <= 40; k++) lats.Add(l0 + 90 + 20 * k);
                if (!br.Down) lats = lats.Where(l => l >= rMain + gap + rT - 1e-9).ToList();
                double bestCost = double.MaxValue, bl = 0, bsh = 0;
                foreach (double lat in lats)
                    foreach (double shift in new[] { 0.0, 40, -40, 80, -80, 120, -120, 160, -160, 200, -200, 260, -260 })
                    {
                        double cost = lat + 0.3 * Math.Abs(shift);
                        if (cost >= bestCost) continue;
                        if (DipOk(br, sg, lat, shift)) { bestCost = cost; bl = lat; bsh = shift; }
                    }
                if (bestCost == double.MaxValue) return null;
                return (bestCost, bl, bsh);
            }
            foreach (var br in branches.ToList())
            {
                (double cost, double lat, double shift)? bestSupply = BestFor(br, dipSide), bestOther = BestFor(br, -dipSide);
                double costSupply = bestSupply == null ? double.MaxValue : bestSupply.Value.cost + 2.0 * Math.Abs(bestSupply.Value.shift);
                double costOther = bestOther == null ? double.MaxValue : bestOther.Value.cost + 2.0 * Math.Abs(bestOther.Value.shift) + 100;
                if (bestSupply == null && bestOther == null) { warnings.Add("NOT HEATED: branch " + br.Name + ": no clear place for the tracer dip beside it (handwheel?)."); branches.Remove(br); branchByName.Remove(br.Name); continue; }
                bool useSupply = costSupply <= costOther;
                var pick = useSupply ? bestSupply.Value : bestOther.Value;
                br.Side = useSupply ? dipSide : -dipSide; br.LDip = pick.lat; br.Shift = pick.shift;
                if (br.Riser)
                {
                    br.CSign = br.SignBySide[br.Side]; var rsc = secs[br.Sec]; var ro = Off(rsc, br.Side);
                    br.DeltaLat = Math.Abs(br.CSign * br.LDip - ro.DotProduct(br.C));
                    warnings.Add("Riser branch " + br.Name + ": section " + br.Sec + " (" + Math.Round(rsc.Len, 0) + " mm) at " + Math.Round(br.A, 0) + " mm, side " + br.Side + ", lateral " + Math.Round(br.LDip, 1) + " (sign " + br.CSign + "), shift " + br.Shift + ", delta " + Math.Round(br.DeltaLat, 1) + ", Bmax " + Math.Round(br.Bmax, 0) + ", parts " + string.Join(" | ", br.Info) + ".");
                }
                if (br.Chain != null)
                {
                    var rsc2 = secs[br.Sec];
                    if (!BranchLegs(br, rsc2.D, br.CSign, br.LDip, br.Shift, out _, out _, out _, out _, out string whyNot))
                    {
                        warnings.Add("The branch " + br.Name + " goes on round elbows and the dip cannot follow it (" + whyNot + "): it is heated by a loop of its own, the tracer is interrupted at the tee.");
                        branches.Remove(br); branchByName.Remove(br.Name); if (br.FirstPipe != null) branchRoutes.Add(br.FirstPipe); continue;
                    }
                }
                if (!useSupply) warnings.Add("Branch " + br.Name + " is heated from the return side (handwheel on the other one).");
                if (br.Riser && br.HeavyC != 0 && br.SignBySide.TryGetValue(br.Side, out var chosenSign) && chosenSign == br.HeavyC)
                    warnings.Add("Branch " + br.Name + ": the dip had to go on the side of the handwheel (no room on the other side): check it.");
            }
            // the tracer that does NOT heat a branch must still pass it without touching: a plain detour where its straight pass is too close to the branch parts
            var passObst = new Dictionary<double, List<(double a1, double a2, double lobs, string h)>> { [+1] = new List<(double, double, double, string)>(), [-1] = new List<(double, double, double, string)>() };
            foreach (var br in branches)
            {
                double other = -br.Side; var sc = secs[br.Sec];
                if (br.Riser)
                {
                    if (br.TeeH != null) continue;       // the other tracer passes the branch of a tee under it: the box of the tee gives it the detour
                    var oo = Off(sc, other);
                    for (double aa = br.A - 150; aa <= br.A + 150; aa += 10)
                        if (!RectsClear(br, sc.A0 + sc.D * aa + oo)) { warnings.Add("CHECK: the straight tracer passes too close to the branch " + br.Name + " (at " + Math.Round(aa, 0) + " mm of riser " + br.Sec + "); move it by hand."); break; }
                    continue;
                }
                var cands2 = new List<double> { l0 };
                foreach (int dg in new[] { 15, 30, 45, 60 }) cands2.Add(l0 + 2 * R * (1 - Math.Cos(dg * Math.PI / 180)));
                for (int k = 1; k <= 20; k++) cands2.Add(l0 + 90 + 20 * k);
                foreach (double lat in cands2)
                {
                    bool ok = true;
                    for (double aa = br.A - 150; aa <= br.A + 150 && ok; aa += 10) ok = RectsClear(br, Pt(sc, aa, other * lat, v0));
                    if (!ok) continue;
                    if (lat > l0 + 0.01) passObst[other].Add((br.A - 90, br.A + 90, lat, "pass " + br.Name));
                    break;
                }
            }

            // ---- helpers: the shift round a wide part (two bends of 15/30/45/60 degrees), clusters of parts that need it -------------
            void Detour(double delta, out double theta, out double diag, double room)
            {
                bool Fits(double t) => delta / Math.Tan(t) + 2 * R * Math.Tan(t / 2) <= room + 1e-6;
                foreach (int deg0 in new[] { 60, 45, 30, 15 })
                { double t0 = deg0 * Math.PI / 180; if (Math.Abs(delta - 2 * R * (1 - Math.Cos(t0))) < 0.05 && Fits(t0)) { theta = t0; diag = 0; return; } }
                foreach (int deg in new[] { 60, 45, 30, 15 })
                { double t = deg * Math.PI / 180; double dd = (delta - 2 * R * (1 - Math.Cos(t))) / Math.Sin(t); if (dd >= minStraight - 1e-6 && Fits(t)) { theta = t; diag = dd; return; } }
                foreach (int deg in new[] { 60, 45, 30, 15 })
                { double t = deg * Math.PI / 180; double dd = (delta - 2 * R * (1 - Math.Cos(t))) / Math.Sin(t); if (dd >= 0 && Fits(t)) { theta = t; diag = dd; return; } }
                theta = 15 * Math.PI / 180; diag = Math.Max(0, (delta - 2 * R * (1 - Math.Cos(theta))) / Math.Sin(theta));
            }
            double CompactTrans(double delta)
            {
                double best = double.MaxValue;
                foreach (int deg in new[] { 15, 30, 45, 60 })
                { double t = deg * Math.PI / 180; if ((delta - 2 * R * (1 - Math.Cos(t))) / Math.Sin(t) < 0) continue; best = Math.Min(best, 2 * R * Math.Tan(t / 2) + delta / Math.Tan(t)); }
                return best == double.MaxValue ? 2 * R * Math.Tan(Math.PI / 24) + delta / Math.Tan(Math.PI / 12) : best;
            }
            List<(double a1, double a2, double lobs, List<string> hs)> Clusters(List<(double a1, double a2, double lobs, string h)> list)
            {
                var res = new List<(double a1, double a2, double lobs, List<string> hs)>();
                foreach (var o in list.OrderBy(o => o.a1))
                {
                    if (res.Count > 0)
                    {
                        var c = res[res.Count - 1];
                        double lm = Math.Max(c.lobs, o.lobs);
                        if (o.a1 - c.a2 < 2 * CompactTrans(lm - l0) + 2 * pad) { c.hs.Add(o.h); res[res.Count - 1] = (c.a1, Math.Max(c.a2, o.a2), lm, c.hs); continue; }
                    }
                    res.Add((o.a1, o.a2, o.lobs, new List<string> { o.h }));
                }
                return res;
            }
            // the tracer's place (l, v) in the frame of the section is inside the box grown by gap + tracer radius
            double eClear = rT + gap;
            bool InExp(RBox b, double l, double v) => l > b.L1 - eClear && l < b.L2 + eClear && v > b.W1 - eClear && v < b.W2 + eClear;
            // the shortest free move of the tracer away from the pipe (any of 8 directions that does not approach the pipe) that clears all the boxes
            (Vector3d disp, double m, bool lateral) DispFor(RSec sc, double sigma, IEnumerable<RBox> bx)
            {
                var bl = bx.ToList(); var o = Off(sc, sigma); double lc = o.DotProduct(sc.L), vc = o.DotProduct(sc.W);
                double bestCost = double.MaxValue, bestM = 0; Vector3d bd = default; bool lat = true;
                for (int q = 0; q < 8; q++)
                {
                    double ul = Math.Cos(q * Math.PI / 4), uv = Math.Sin(q * Math.PI / 4);
                    if (ul * lc + uv * vc < -1e-6) continue;                       // never towards the pipe
                    bool isLat = Math.Abs(uv) < 1e-6; double pen = isLat ? 0 : (sc.IsRiser ? (q % 2 == 0 ? 8 : 4) : 500);   // user: round a valve on a horizontal run always at the SIDES, never underneath (only if the side has no free place at all)
                    for (double m = 2 * R * (1 - Math.Cos(Math.PI / 12)) + 0.1; m <= 700; m += 0.5)
                    {
                        if (bl.Any(b => InExp(b, lc + m * ul, vc + m * uv))) continue;
                        if (m + pen < bestCost - 1e-9) { bestCost = m + pen; bestM = m; bd = ul * sc.L + uv * sc.W; lat = isLat; }
                        break;
                    }
                }
                if (bestCost == double.MaxValue) throw new InvalidOperationException("No free place for the tracer round " + string.Join(",", bl.Select(b => b.H)) + ".");
                return (bd * bestM, bestM, lat);
            }
            // obstacles of a section for the tracer at lateral sign sigma: a part whose box reaches the tracer's place
            List<(double a1, double a2, double lobs, string h)> ObstaclesFor(RSec sc, double sigma)
            {
                var res = new List<(double, double, double, string)>();
                var oo = Off(sc, sigma); double olc = oo.DotProduct(sc.L), ovc = oo.DotProduct(sc.W);
                foreach (var b in sc.Boxes)
                {
                    if (branches.Any(x => x.TeeH == b.H && x.Side == sigma)) continue;     // this tracer dips along the branch of that tee
                    if (!InExp(b, olc, ovc)) continue;                       // the tracer's place is clear of the box
                    res.Add((b.A1, b.A2, l0 + DispFor(sc, sigma, new[] { b }).m, b.H));
                }
                int si = secs.IndexOf(sc);
                foreach (var br in branches.Where(b => b.Sec == si && b.Side == sigma))
                    res.Add((br.A + br.Shift - 2 * Rdip - (br.Riser ? 0 : minStraight), br.A + br.Shift + 2 * Rdip + (br.Riser ? 0 : minStraight), br.Riser ? l0 + br.DeltaLat : br.LDip, "branch " + br.Name));
                foreach (var po in passObst[sigma]) if (branches.Any(b => b.Sec == si && "pass " + b.Name == po.h)) res.Add(po);
                return res;
            }

            // ---- the end closures: far enough beyond the ends that the first / last detour fits ---------------------------------
            double aStart = -endMargin + tailU, aEnd = secs[secs.Count - 1].Len + endMargin - tailU;
            foreach (double sg in new[] { 1.0, -1.0 })
            {
                var c0 = Clusters(ObstaclesFor(secs[0], sg)); if (c0.Count > 0) aStart = Math.Min(aStart, c0[0].a1 - pad - CompactTrans(c0[0].lobs - l0) - 20);
                var cl = Clusters(ObstaclesFor(secs[secs.Count - 1], sg)); if (cl.Count > 0) aEnd = Math.Max(aEnd, cl[cl.Count - 1].a2 + pad + CompactTrans(cl[cl.Count - 1].lobs - l0) + 20);
            }

            // ---- a wide ring: when the obstacle sits at the very end of the route, the tracers stay at their detour offset and close with one wide 180-degree bend
            double wideEnd = 0, wideStart = 0, wideReach = 150;
            {
                var ce = new List<(double a2, double lobs)>(); var cs = new List<(double a1, double lobs)>();
                foreach (double sg in new[] { 1.0, -1.0 })
                {
                    var sL = secs[secs.Count - 1]; var cl = Clusters(ObstaclesFor(sL, sg));
                    if (!sL.IsRiser && cl.Count > 0 && cl[cl.Count - 1].a2 >= sL.Len - wideReach && !cl[cl.Count - 1].hs.Any(x => x.StartsWith("branch ")))
                    { var dd = DispFor(sL, sg, sL.Boxes.Where(b => cl[cl.Count - 1].hs.Contains(b.H))); if (dd.lateral) ce.Add((cl[cl.Count - 1].a2, l0 + dd.m)); }
                    var s0b = secs[0]; var c0 = Clusters(ObstaclesFor(s0b, sg));
                    if (!s0b.IsRiser && c0.Count > 0 && c0[0].a1 <= wideReach && !c0[0].hs.Any(x => x.StartsWith("branch ")))
                    { var dd = DispFor(s0b, sg, s0b.Boxes.Where(b => c0[0].hs.Contains(b.H))); if (dd.lateral) cs.Add((c0[0].a1, l0 + dd.m)); }
                }
                if (ce.Count == 2) { wideEnd = ce.Max(q => q.lobs); aEnd = ce.Max(q => q.a2) + pad + 10; }
                if (cs.Count == 2) { wideStart = cs.Max(q => q.lobs); aStart = cs.Min(q => q.a1) - pad - 10; }
            }
            if (startAtTee) { aStart = tailU + l0 + 60; wideStart = 0; }                                                          // the closing bend lies inside the pipe, 60 mm from the tee
            if (endAtTee) { aEnd = secs[secs.Count - 1].Len - (tailU + l0 + 60); wideEnd = 0; }
            double rUstart = wideStart > 0 ? wideStart : rU, rUend = wideEnd > 0 ? wideEnd : rU;

            // ---- the tracer polylines -------------------------------------------------------------------------------------------
            var cornerRs = new List<(Point3d p, double r)>();
            var detours = new List<object>();
            var twistProblems = new SortedSet<string>();
            // the vertices of the twist of one tracer round a riser (null when the riser is too short for bends of radius rt)
            List<Point3d> TwistVertices(RSec sc, Vector3d off0, double rt, double rOffT)
            {
                int m = (int)Math.Ceiling(Math.Abs(sc.Twist) / (Math.PI / 2) - 1e-6); double step = sc.Twist / m, sgn = Math.Sign(sc.Twist);
                var X = new List<Vector3d> { off0 }; var ss = new List<double>();
                for (int j = 0; j <= m; j++)
                {
                    var uj = off0.TransformBy(Matrix3d.Rotation(j * step, sc.D, Point3d.Origin));
                    var tj = sc.D.CrossProduct(uj).GetNormal() * sgn;
                    double sj = rOffT * Math.Tan(Math.Abs(step) / 2) * (j == 0 || j == m ? 1 : 2); ss.Add(sj); X.Add(X[j] + tj * sj);
                }
                var beta = new List<double>();
                foreach (double sj in ss)
                {
                    double b = 0;
                    foreach (double cb in new[] { Math.PI / 4, Math.PI / 6, Math.PI / 12 })
                        if (sj >= 2 * rt * Math.Tan(cb / 2) * Math.Sin(cb) - 1e-6) { b = cb; break; }
                    if (b == 0) return null;
                    beta.Add(b);
                }
                double span = 0;
                for (int j = 0; j <= m; j++) { span += ss[j] / Math.Tan(beta[j]); if (j < m) span += rt * Math.Tan(beta[j] / 2) + rt * Math.Tan(beta[j + 1] / 2); }
                double tw0 = rt * Math.Tan(beta[0] / 2) + 15;   // the twist sits right behind the lower elbow of the riser, not in the middle
                if (tw0 < rt * Math.Tan(beta[0] / 2) + 10 || tw0 + span > sc.Len - rt * Math.Tan(beta[m] / 2) - 10) return null;
                var res = new List<Point3d>(); double a = tw0;
                for (int j = 0; j <= m; j++)
                {
                    res.Add(sc.A0 + sc.D * a + X[j]);
                    a += ss[j] / Math.Tan(beta[j]);
                    res.Add(sc.A0 + sc.D * a + X[j + 1]);
                    if (j < m) a += rt * Math.Tan(beta[j] / 2) + rt * Math.Tan(beta[j + 1] / 2);
                }
                return res;
            }
            var pathsFor = new Dictionary<double, List<List<Point3d>>>(); var dipsFor = new Dictionary<double, List<TItem>>();
            void Trace(double sigma)
            {
                var v = new List<Point3d>(); var paths = new List<List<Point3d>>(); var dips = new List<TItem>();
                for (int k = 0; k < secs.Count; k++)
                {
                    var sc = secs[k]; bool lastSec = k == secs.Count - 1;
                    double prevEnd = k == 0 ? aStart : 0;
                    if (k == 0) v.Add(sc.IsRiser ? sc.A0 + sc.D * aStart + Off(sc, sigma) : Pt(sc, aStart, sigma * (wideStart > 0 ? wideStart : l0), v0));
                    if (!sc.IsRiser && k > 0 && secs[k - 1].IsRiser && !sc.Hold && LJ(secs[k - 1], sigma, sc) - l0 > 0.5)
                    {
                        // behind a riser the tracer lies at the lateral offset lj; back to view 3 on the straight run
                        double ljs = LJ(secs[k - 1], sigma, sc);
                        double aKs = KA(sc, secs[k - 1], sigma);
                        var clsS = Clusters(ObstaclesFor(sc, sigma));
                        double nextF = clsS.Count > 0 ? clsS[0].a1 - pad - 2 * R * Math.Tan(Math.PI / 24) : sc.Len;      // where the next feature of the run begins
                        if (nextF - (aKs + R) >= 15 * (ljs - l0)) prevEnd = aKs + R;       // the user's way: no S-bend, the straight pipe goes at a slight angle from the corner to the next feature (its bend turns by a fraction of a degree)
                        else
                        {
                            Detour(ljs - l0, out double thj, out double dgj, double.MaxValue);
                            double rtj = R * Math.Tan(thj / 2), xj = (ljs - l0) / Math.Tan(thj);
                            double s3 = Math.Max(pad, aKs + R + 10) + rtj;
                            v.Add(Pt(sc, s3, sigma * ljs, v0)); v.Add(Pt(sc, s3 + xj, sigma * l0, v0)); prevEnd = s3 + xj + rtj;
                        }
                    }
                    double endA = lastSec ? aEnd : sc.Len;
                    var cls = Clusters(ObstaclesFor(sc, sigma));
                    if (Math.Abs(sc.Twist) > 0)
                    {
                        // a riser between horizontal runs of different directions: each tracer winds round the pipe along tangent lines (never closer than rOff to the axis).
                        // One move = an S of two equal bends (45/30/15 degrees) from one vertical line to the next one; tangent polygon with a vertex every <= 90 degrees
                        if (cls.Count > 0) throw new InvalidOperationException("Section " + k + ": parts with obstacles in a riser where the tracers have to twist are not supported.");
                        var off0 = sigma * l0 * sc.L + v0 * sc.W;
                        List<Point3d> tv = null; double usedR = 0;
                        foreach (double Rt in new[] { R, 40.0 })
                        {
                            tv = TwistVertices(sc, off0, Rt, rOff); if (tv != null) { usedR = Rt; break; }
                        }
                        if (tv == null) twistProblems.Add("riser " + k + " (" + string.Join(",", sc.Parts.Select(q => q.H)) + ", " + Math.Round(sc.Len, 0) + " mm, twist " + Math.Round(sc.Twist * 180 / Math.PI, 0) + " deg)");
                        else
                        {
                            foreach (var q in tv) { v.Add(q); if (usedR != R) cornerRs.Add((q, usedR)); }
                            if (usedR != R) warnings.Add("Riser " + k + " is short: the tracers twist round it with bends R=" + usedR + " instead of R=" + R + ".");
                            detours.Add(new { section = k, tracer = sigma > 0 ? "+" : "-", twist_deg = Math.Round(sc.Twist * 180 / Math.PI, 0), bend_radius = usedR, from_mm = Math.Round(tv[0].DistanceTo(sc.A0), 0), to_mm = Math.Round(tv[tv.Count - 1].DistanceTo(sc.A0), 0) });
                        }
                    }
                    for (int ci = 0; ci < cls.Count; ci++)
                    {
                        var c = cls[ci];
                        var cBr = c.hs.Where(x => x.StartsWith("branch ")).Select(x => branchByName[x.Substring(7)]).OrderBy(b => b.A).ToList();
                        bool ringStart = k == 0 && ci == 0 && wideStart > 0 && cBr.Count == 0, ringEnd = lastSec && ci == cls.Count - 1 && wideEnd > 0 && cBr.Count == 0;
                        double lob = ringStart ? wideStart : ringEnd ? wideEnd : c.lobs;
                        double delta = lob - l0;
                        bool riserDip = cBr.Count == 1 && cBr[0].Riser;
                        if (cBr.Any(b => b.Riser) && !riserDip) throw new InvalidOperationException("Section " + k + ": a branch of a riser lies next to another obstacle or branch (" + string.Join(",", c.hs) + "): not supported, heat it by hand.");
                        Vector3d oS = Off(sc, sigma), tS = oS + sigma * (lob - l0) * sc.L;
                        if (cBr.Count > 0 && c.hs.Count > 1) throw new InvalidOperationException("Section " + k + ": a branch lies next to another obstacle or branch (" + string.Join(",", c.hs) + "): not supported, heat it by hand.");
                        if (riserDip) { var rb0 = cBr[0]; tS = rb0.CSign * rb0.LDip * rb0.C + oS.DotProduct(rb0.B) * rb0.B; delta = (tS - oS).Length; }
                        else if (!ringStart && !ringEnd && cBr.Count == 0)
                        {
                            var dsp = DispFor(sc, sigma, sc.Boxes.Where(b => c.hs.Contains(b.H)));
                            tS = oS + dsp.disp; delta = dsp.m;
                        }
                        double nextB = ci + 1 < cls.Count ? (c.a2 + cls[ci + 1].a1) / 2 : endA;
                        double roomB = ringStart ? double.MaxValue : (c.a1 - pad) - prevEnd, roomA = ringEnd ? double.MaxValue : nextB - (c.a2 + pad);
                        Detour(delta, out double th, out double dg, Math.Min(roomB, roomA));
                        double rt = R * Math.Tan(th / 2), x = delta / Math.Tan(th);
                        double c2 = c.a1 - pad - rt, c1 = c2 - x, c3 = c.a2 + pad + rt, c4 = c3 + x;
                        // the user's way at a vent / drain: no S-bends; the straight pipe runs at a slight angle from the previous feature straight to the (turned) bend of the dip,
                        // and from the dip straight on to the next feature; the gap grows a little on the way, which is allowed
                        bool skewIn = false, skewOut = false; double qaOut = 0;
                        if (riserDip)
                        {
                            double qa = cBr[0].A + cBr[0].Shift;
                            skewIn = !ringStart && (qa - 2 * Rdip - prevEnd) >= Math.Max(15 * delta, 120);
                            double nextF2 = ci + 1 < cls.Count ? cls[ci + 1].a1 - pad - 2 * R * Math.Tan(Math.PI / 24) - 20 : endA;
                            skewOut = !ringEnd && (nextF2 - (qa + 2 * Rdip)) >= Math.Max(15 * delta, 120);
                            qaOut = qa + 2 * Rdip;
                        }
                        if (!ringStart && !skewIn) { v.Add(sc.A0 + sc.D * c1 + oS); v.Add(sc.A0 + sc.D * c2 + tS); }
                        if (riserDip)
                        {
                            var br = cBr[0];
                            double b0 = oS.DotProduct(br.B), vPortB = br.Bmax + overshoot - (dipTail + Rdip);
                            var q1 = sc.A0 + sc.D * (br.A + br.Shift - Rdip) + tS; var q2 = sc.A0 + sc.D * (br.A + br.Shift + Rdip) + tS;
                            var p1 = q1 + br.B * (vPortB - b0); var p2 = q2 + br.B * (vPortB - b0);
                            cornerRs.Add((q1, Rdip)); cornerRs.Add((q2, Rdip));
                            if (br.Chain != null)
                            {
                                // the branch goes on round elbows: out along all of it and back, one tracer without a break
                                BranchLegs(br, sc.D, br.CSign, br.LDip, br.Shift, out var legA, out var legB, out var radii, out var dLast, out _);
                                foreach (var rr in radii) cornerRs.Add(rr);
                                v.Add(q1); v.AddRange(legA); paths.Add(v);
                                var pA = legA[legA.Count - 1]; var pB = legB[legB.Count - 1];
                                dips.Add(new TItem { Kind = "u", P1 = pA, P2 = pB, X = dLast.Negate(), Y = (pB - pA).GetNormal(), R = Rdip, L1 = dipTail, L2 = dipTail, Tracer = "dip" });
                                legB.Reverse(); v = new List<Point3d>(legB); v.Add(q2);
                            }
                            else
                            {
                            v.Add(q1); v.Add(p1); paths.Add(v);
                            dips.Add(new TItem { Kind = "u", P1 = p1, P2 = p2, X = br.B.Negate(), Y = (p2 - p1).GetNormal(), R = Rdip, L1 = dipTail, L2 = dipTail, Tracer = "dip" });
                            v = new List<Point3d> { p2, q2 };
                            }
                        }
                        else
                        foreach (var br in cBr)
                        {
                            double vApex = br.Down ? VOfZ(sc, br.A, br.ZMin - overshoot) : VOfZ(sc, br.A, br.ZMax + overshoot);
                            double vPort = br.Down ? vApex - (dipTail + Rdip) : vApex + (dipTail + Rdip);
                            var q1 = Pt(sc, br.A + br.Shift - Rdip, sigma * lob, v0); var q2 = Pt(sc, br.A + br.Shift + Rdip, sigma * lob, v0);
                            var p1 = Pt(sc, br.A + br.Shift - Rdip, sigma * lob, vPort); var p2 = Pt(sc, br.A + br.Shift + Rdip, sigma * lob, vPort);
                            cornerRs.Add((q1, Rdip)); cornerRs.Add((q2, Rdip));
                            v.Add(q1); v.Add(p1); paths.Add(v);
                            dips.Add(new TItem { Kind = "u", P1 = p1, P2 = p2, X = br.Down ? Vector3d.ZAxis : Vector3d.ZAxis.Negate(), Y = (p2 - p1).GetNormal(), R = Rdip, L1 = dipTail, L2 = dipTail, Tracer = "dip" });
                            v = new List<Point3d> { p2, q2 };
                        }
                        if (!ringEnd && !skewOut) { v.Add(sc.A0 + sc.D * c3 + tS); v.Add(sc.A0 + sc.D * c4 + oS); }
                        prevEnd = ringEnd ? c.a2 + pad : skewOut ? qaOut : c4 + rt;
                        detours.Add(new { section = k, tracer = sigma > 0 ? "+" : "-", parts = c.hs, from_mm = Math.Round(c.a1, 1), to_mm = Math.Round(c.a2, 1), lateral = Math.Round(c.lobs, 1), bend_angle_deg = Math.Round(th * 180 / Math.PI, 1) });
                        if (prevEnd > endA + 0.5) throw new InvalidOperationException("Section " + k + ": the detour round " + string.Join(",", c.hs) + " does not fit before the end of the section (" + Math.Round(prevEnd - endA, 0) + " mm short; section " + Math.Round(sc.Len, 0) + " mm, window " + Math.Round(c.a1, 0) + ".." + Math.Round(c.a2, 0) + ", delta " + Math.Round(delta, 1) + ", angle " + Math.Round(th * 180 / Math.PI, 0) + " " + string.Join(" | ", cBr.Select(b => b.Name + " lat " + Math.Round(b.LDip, 1) + " sign " + b.CSign + " deltaLat " + Math.Round(b.DeltaLat, 1) + " A " + Math.Round(b.A, 0) + " shift " + b.Shift + " Bmax " + Math.Round(b.Bmax, 0))) + ").");
                    }
                    if (lastSec) v.Add(sc.IsRiser ? sc.A0 + sc.D * aEnd + Off(sc, sigma) : Pt(sc, aEnd, sigma * (wideEnd > 0 ? wideEnd : l0), v0));
                    else if (sc.IsRiser || secs[k + 1].IsRiser)
                    {
                        // a riser: square corner between the vertical line of the riser and the lateral line lJ of the horizontal run (90 degree bend)
                        var nx = secs[k + 1];
                        if (!sc.IsRiser && !sc.Hold && LJ(nx, sigma, sc) - l0 > 0.5)
                        {
                            double ljE = LJ(nx, sigma, sc);
                            var rsx = nx;
                            if (KA(sc, rsx, sigma) - R - prevEnd >= 15 * (ljE - l0)) { /* the user's way: the straight pipe goes at a slight angle from the previous feature straight to the corner */ }
                            else
                            {
                                Detour(ljE - l0, out double the, out double dge, double.MaxValue);
                                double rte = R * Math.Tan(the / 2), xe = (ljE - l0) / Math.Tan(the);
                                double e2 = Math.Min(sc.Len - pad, KA(sc, rsx, sigma) - R - 10) - rte, e1 = e2 - xe;
                                if (e1 - rte < prevEnd - 0.5) throw new InvalidOperationException("Section " + k + ": no room for the shift of the tracers before the riser (" + Math.Round(prevEnd - (e1 - rte), 0) + " mm short).");
                                v.Add(Pt(sc, e1, sigma * l0, v0)); v.Add(Pt(sc, e2, sigma * ljE, v0));
                            }
                        }
                        RSec hz = sc.IsRiser ? nx : sc, rs = sc.IsRiser ? sc : nx;
                        double aK = KA(hz, rs, sigma);
                        v.Add(hz.A0 + hz.D * aK + sigma * LJ(rs, sigma, hz) * hz.L + v0 * hz.W);
                        if (prevEnd > sc.Len + 0.5) throw new InvalidOperationException("Section " + k + " is too short for the detours and the corner.");
                    }
                    else
                    {
                        // the corner of the offset lines; the tracer bend is concentric with the elbow
                        var nx = secs[k + 1];
                        var ok = sigma * l0 * sc.L2 + v0 * sc.W2; var on = sigma * l0 * nx.L + v0 * nx.W;
                        var corner = sc.Pc + ok + sc.D * ((on - ok).DotProduct(sc.D));
                        double rT2 = sc.Re - ok.DotProduct(nx.D);
                        if (rT2 <= 1) throw new InvalidOperationException("The radius of elbow " + sc.ElbowAfter.H + " is too small for the tracer to follow it.");
                        cornerRs.Add((corner, rT2)); v.Add(corner);
                        if (prevEnd > sc.Len + 0.5) throw new InvalidOperationException("Section " + k + " is too short for the detours and the corner.");
                    }
                }
                paths.Add(v); pathsFor[sigma] = paths; dipsFor[sigma] = dips;
            }
            Trace(+1); Trace(-1);
            if (twistProblems.Count > 0) throw new InvalidOperationException("The tracers must twist round these risers to keep view 3 on the horizontal runs, but they are too short: " + string.Join("; ", twistProblems));

            // ---- items of the closed loop (same construction as create_tracing) ---------------------------------------------------
            var items = new List<TItem>();
            void Walk(List<Point3d> V, string tag)
            {
                var bends = new List<TItem>(); var entry = new List<Point3d>(); var exit = new List<Point3d>();
                for (int i = 1; i < V.Count - 1; i++)
                {
                    var tin = (V[i] - V[i - 1]).GetNormal(); var tout = (V[i + 1] - V[i]).GetNormal();
                    double th = Math.Acos(Math.Max(-1, Math.Min(1, tin.DotProduct(tout))));
                    if (th < 1e-4) continue;
                    double rb = R; var cr = cornerRs.Where(c0 => c0.p.DistanceTo(V[i]) < 1e-6).ToList(); if (cr.Count > 0) rb = cr[0].r;
                    double rt = rb * Math.Tan(th / 2);
                    bends.Add(new TItem { Kind = "bend", Corner = V[i], TIn = tin, TOut = tout, R = rb, Theta = th, L1 = 0, L2 = 0, Tracer = tag });
                    entry.Add(V[i] - tin * rt); exit.Add(V[i] + tout * rt);
                }
                for (int k = 0; k <= bends.Count; k++)
                {
                    Point3d from = k == 0 ? V[0] : exit[k - 1]; Point3d to = k == bends.Count ? V[V.Count - 1] : entry[k];
                    double seg = from.DistanceTo(to);
                    var dir = seg > 1e-6 ? (to - from).GetNormal() : Vector3d.XAxis;
                    var prev = k == 0 ? null : bends[k - 1]; var next = k == bends.Count ? null : bends[k];
                    var expDir = prev != null ? prev.TOut : (next != null ? next.TIn : (V[V.Count - 1] - V[0]).GetNormal());
                    if ((to - from).DotProduct(expDir) < -0.5) throw new InvalidOperationException("The bends overlap near " + Fmt(from) + " (tracer " + tag + ", prev bend " + (prev == null ? "-" : Fmt(prev.Corner) + " th " + Math.Round(prev.Theta * 180 / Math.PI, 1) + " R " + prev.R) + ", next bend " + (next == null ? "-" : Fmt(next.Corner) + " th " + Math.Round(next.Theta * 180 / Math.PI, 1) + " R " + next.R) + ", straight " + Math.Round((to - from).DotProduct(expDir), 1) + "): there is no room for them between the neighbouring features.");
                    double l2p = 0, l1n = 0;
                    foreach (var cand in new[] { (handleLen, handleLen), (handleLen, 0.0), (0.0, handleLen), (0.0, 0.0) })
                    {
                        if ((prev == null && cand.Item1 > 0) || (next == null && cand.Item2 > 0)) continue;
                        double rest = seg - cand.Item1 - cand.Item2; if (rest < -1e-6) continue;
                        if (rest <= 0.5 || rest >= minStraight - 1e-6) { l2p = cand.Item1; l1n = cand.Item2; break; }
                    }
                    if (prev != null) prev.L2 = l2p; if (next != null) next.L1 = l1n;
                    var pa = from + dir * l2p; var pb = to - dir * l1n;
                    if (pa.DistanceTo(pb) > 0.5 && (pb - pa).DotProduct(dir) > 0) items.Add(new TItem { Kind = "pipe", P1 = pa, P2 = pb, Tracer = tag });
                    if (next != null) items.Add(next);
                }
            }
            var s0 = secs[0]; var sl = secs[secs.Count - 1];
            var pP = pathsFor[+1]; var pM = pathsFor[-1];
            var vPlus0 = pP[0][0]; var vMinus0 = pM[0][0]; var vPlusL = pP[pP.Count - 1][pP[pP.Count - 1].Count - 1]; var vMinusL = pM[pM.Count - 1][pM[pM.Count - 1].Count - 1];
            items.Add(new TItem { Kind = "u", P1 = vPlus0, P2 = vMinus0, X = s0.D, Y = (vMinus0 - vPlus0).GetNormal(), R = vPlus0.DistanceTo(vMinus0) / 2, L1 = tailU, L2 = tailU, Tracer = "start" });
            for (int pi = 0; pi < pP.Count; pi++) { Walk(pP[pi], "+"); if (pi < dipsFor[+1].Count) items.Add(dipsFor[+1][pi]); }
            items.Add(new TItem { Kind = "u", P1 = vPlusL, P2 = vMinusL, X = sl.D.Negate(), Y = (vMinusL - vPlusL).GetNormal(), R = vPlusL.DistanceTo(vMinusL) / 2, L1 = tailU, L2 = tailU, Tracer = "end" });
            for (int pi = pM.Count - 1; pi >= 0; pi--)
            {
                var rev = new List<Point3d>(pM[pi]); rev.Reverse(); Walk(rev, "-");
                if (pi > 0) items.Add(dipsFor[-1][pi - 1]);
            }
            warnings.Add("The closing bends lie beyond the free ends of the route (start " + Math.Round(-aStart + tailU, 0) + " mm, end " + Math.Round(aEnd - sl.Len + tailU, 0) + " mm outside the end faces).");

            // ---- every number of every item must be a finite one (NaN / infinity would make Plant 3D fail while it sets the properties) --------------------------------------
            {
                bool Fin(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
                bool FinP(Point3d q) => Fin(q.X) && Fin(q.Y) && Fin(q.Z);
                bool FinV(Vector3d q) => Fin(q.X) && Fin(q.Y) && Fin(q.Z);
                for (int ii = 0; ii < items.Count; ii++)
                {
                    var it = items[ii];
                    bool ok = it.Kind == "pipe" ? FinP(it.P1) && FinP(it.P2)
                            : it.Kind == "bend" ? FinP(it.Corner) && FinV(it.TIn) && FinV(it.TOut) && Fin(it.R) && Fin(it.Theta) && Fin(it.L1) && Fin(it.L2)
                            : FinP(it.P1) && FinP(it.P2) && FinV(it.X) && FinV(it.Y) && Fin(it.R) && Fin(it.L1) && Fin(it.L2);
                    if (!ok)
                    {
                        var prevIt = ii > 0 ? items[ii - 1] : null;
                        throw new InvalidOperationException("Item " + ii + " (" + it.Kind + ", tracer " + it.Tracer + ") has a non-finite number: P1 " + (it.Kind == "bend" ? Fmt(it.Corner) : Fmt(it.P1)) + " P2 " + Fmt(it.P2) + " R " + it.R + " theta " + it.Theta + " L1 " + it.L1 + " L2 " + it.L2 + "; the previous item is " + (prevIt == null ? "-" : prevIt.Kind + " at " + (prevIt.Kind == "bend" ? Fmt(prevIt.Corner) : Fmt(prevIt.P2))) + ".");
                    }
                }
            }

            // ---- bends tighter than the tracer pipe is bent in practice (the user accepts R = 40 round vents and drains) --------------------------------
            {
                int tight = items.Count(i => (i.Kind == "bend" || i.Kind == "u") && i.R < 39.5);
                if (tight > 0) warnings.Add(tight + " tracer bends have a radius below 40 mm (" + Math.Round(items.Where(i => i.Kind == "bend" || i.Kind == "u").Min(i => i.R), 1) + " mm at the smallest): check them, 25x4 is not bent that tight.");
            }
            // ---- a tracing that is already there (the same pipes at the same places): never a second one on top -----------------------------------------
            {
                int already = CountExistingTracerPipes(doc, items);
                if (already >= 2)
                {
                    string msg = already + " of the planned tracer pipes coincide with tracer pipes that are already in the drawing: the line is already traced.";
                    if (!IsDry(args)) throw new InvalidOperationException(msg + " Delete the old tracing first (restart Plant 3D without saving if it was made in this session) and repeat.");
                    warnings.Add("ALREADY TRACED: " + msg);
                }
            }

            // ---- collisions: every point of every tracer part against the pipes, the elbows and the boxes of the wide parts -------------
            var collisions = new List<string>(); double minClear = double.MaxValue; int samples = 0;
            var supBoxes = SupportBoxes(doc);
            var cyls = new List<(Point3d p1, Point3d p2, double rad, string what)>();
            foreach (var part in chain.Where(c => c.Kind == "pipe")) cyls.Add((part.InPos, part.OutPos, part.Od / 2, "pipe " + part.H));
            foreach (var sc in secs.Where(sc => sc.ElbowAfter != null))
            {
                var u0 = (sc.ElbowAfter.InPos - sc.Cc) / sc.Re; var u1 = (sc.ElbowAfter.OutPos - sc.Cc) / sc.Re; Point3d pa = sc.ElbowAfter.InPos;
                for (int k = 1; k <= 48; k++)
                { double phi = Math.PI / 2 * k / 48.0; var q2 = sc.Cc + (u0 * Math.Cos(phi) + u1 * Math.Sin(phi)) * sc.Re; cyls.Add((pa, q2, rMain, "elbow " + sc.ElbowAfter.H)); pa = q2; }
            }
            TItem curItem = null;
            void CheckPoint(Point3d q, string what)
            {
                samples++;
                foreach (var cy in cyls)
                {
                    var sg = cy.p2 - cy.p1; double len2 = sg.DotProduct(sg);
                    double tt = len2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, (q - cy.p1).DotProduct(sg) / len2));
                    double cl = (q - (cy.p1 + sg * tt)).Length - rT - cy.rad;
                    if (cl < minClear) minClear = cl;
                    if (cl < gap - tol) { if (curItem != null) curItem.Hit = true; if (collisions.Count < 8) collisions.Add(what + " at " + Fmt(q) + " is " + Math.Round(cl, 2) + " mm from " + cy.what + " (needs " + gap + ")"); }
                }
                foreach (var sb in supBoxes)
                {
                    double cs = BoxDist3(q, sb.x1, sb.x2, sb.y1, sb.y2, sb.z1, sb.z2) - rT;
                    if (cs < gap - tol) { if (curItem != null) curItem.Hit = true; if (collisions.Count < 8) collisions.Add(what + " at " + Fmt(q) + " is " + Math.Round(cs, 2) + " mm from the support " + sb.h + " (needs " + gap + ")"); }
                }
                foreach (var br in branches)
                    foreach (var cy in br.Cyls)
                    {
                        var sg2 = cy.p2 - cy.p1; double l22 = sg2.DotProduct(sg2);
                        double t2 = l22 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, (q - cy.p1).DotProduct(sg2) / l22));
                        double cc = (q - (cy.p1 + sg2 * t2)).Length - rT - cy.rad;
                        if (cc < gap - tol) { if (curItem != null) curItem.Hit = true; if (collisions.Count < 8) collisions.Add(what + " at " + Fmt(q) + " is " + Math.Round(cc, 2) + " mm from the pipe of the branch " + br.Name + " (needs " + gap + ")"); }
                    }
                foreach (var br in branches)
                    foreach (var r in br.Rects)
                    {
                        double cb = BoxDist3(q, r.x1, r.x2, r.y1, r.y2, r.z1, r.z2) - rT;
                        if (cb < gap - tol) { if (curItem != null) curItem.Hit = true; if (collisions.Count < 8) collisions.Add(what + " at " + Fmt(q) + " is " + Math.Round(cb, 2) + " mm from the branch " + br.Name + " (needs " + gap + ")"); }
                    }
                foreach (var sc in secs)
                    foreach (var b in sc.Boxes)
                    {
                        if (branches.Any(x => x.TeeH == b.H)) continue;        // the box of a tee with a heated branch is replaced by the cylinders of the branch
                        var c = q - sc.A0; double a = c.DotProduct(sc.D), l = c.DotProduct(sc.L), w = c.DotProduct(sc.W);
                        double da = Math.Max(Math.Max(b.A1 - a, 0), a - b.A2), dl = Math.Max(Math.Max(b.L1 - l, 0), l - b.L2), dw = Math.Max(Math.Max(b.W1 - w, 0), w - b.W2);
                        double cl = Math.Sqrt(da * da + dl * dl + dw * dw) - rT;
                        if (cl < minClear) minClear = cl;
                        if (cl < gap - tol) { if (curItem != null) curItem.Hit = true; if (collisions.Count < 8) collisions.Add(what + " at " + Fmt(q) + " is " + Math.Round(cl, 2) + " mm from " + b.Desc + " (needs " + gap + ")"); }
                    }
            }
            void CheckLine(Point3d p1, Point3d p2, string what) { int nn = Math.Max(1, (int)Math.Ceiling(p1.DistanceTo(p2) / 40)); for (int i = 0; i <= nn; i++) CheckPoint(p1 + (p2 - p1) * ((double)i / nn), what); }
            foreach (var it in items)
            {
                curItem = it;
                if (it.Kind == "pipe") CheckLine(it.P1, it.P2, "tracer " + it.Tracer + " pipe");
                else if (it.Kind == "bend")
                {
                    double rt = it.R * Math.Tan(it.Theta / 2);
                    var ep = it.Corner - it.TIn * rt; var xp = it.Corner + it.TOut * rt;
                    CheckLine(ep - it.TIn * it.L1, ep, "tracer " + it.Tracer + " bend handle"); CheckLine(xp, xp + it.TOut * it.L2, "tracer " + it.Tracer + " bend handle");
                    var cen = it.Corner + (it.TOut - it.TIn).GetNormal() * (it.R / Math.Cos(it.Theta / 2)); var nrm = it.TIn.CrossProduct(it.TOut).GetNormal();
                    for (int k = 0; k <= 12; k++) CheckPoint(ep.TransformBy(Matrix3d.Rotation(it.Theta * k / 12.0, nrm, cen)), "tracer " + it.Tracer + " bend");
                }
                else
                {
                    var cen = it.P1 - it.X * it.L1 + it.Y * it.R;
                    CheckLine(it.P1, it.P1 - it.X * it.L1, "closing bend handle"); CheckLine(it.P2, it.P2 - it.X * it.L2, "closing bend handle");
                    for (int k = 0; k <= 24; k++) { double phi = Math.PI * k / 24.0; CheckPoint(cen + (it.Y.Negate() * Math.Cos(phi) - it.X * Math.Sin(phi)) * it.R, "closing bend"); }
                }
            }
            int hitParts = items.Count(i => i.Hit);
            if (collisions.Count > 0) warnings.Add("COLLISIONS with the route: " + string.Join("; ", collisions));
            if (hitParts > 0) warnings.Add(hitParts + " tracer parts touch or enter the main line where that cannot be avoided: they are created anyway and painted violet (layer of the collisions).");
            foreach (var part in chain.Where(c => c.Kind == "tee" && !branches.Any(b => b.TeeH == c.H)))
                warnings.Add("Tee " + part.H + ": its branch is passed by, not heated.");
            foreach (var part in chain.Where(c => c.Kind == "other")) warnings.Add("Part " + part.H + " (" + part.Desc + ") is treated as a wide part of unknown kind.");

            int nPipes = items.Count(i => i.Kind == "pipe"), nBends = items.Count(i => i.Kind == "bend"), nU = items.Count(i => i.Kind == "u");
            double pipeLen = items.Where(i => i.Kind == "pipe").Sum(i => i.P1.DistanceTo(i.P2));
            var plan = new
            {
                route = chain.Select(c => c.Kind + " " + c.H).ToList(),
                sections = secs.Select((sc, i) => new { index = i, start = Arr(sc.A0), direction = Arr(sc.D), length = Math.Round(sc.Len, 1), offset_side = Arr(sc.L), offset_down = Arr(sc.W), elbow_after = sc.ElbowAfter?.H, elbow_radius = Math.Round(sc.Re, 1), wide_parts = sc.Boxes.Select(b => b.H + " " + b.Desc.Substring(0, Math.Min(22, b.Desc.Length)) + " a " + Math.Round(b.A1, 0) + ".." + Math.Round(b.A2, 0)).ToList() }).ToList(),
                tracer = new { spec = specName, pipe = DescOf(pipePart), od = odT, gap, radius = R, lateral_offset = Math.Round(l0, 2), vertical_offset = Math.Round(v0, 2) },
                detours,
                collision_check = new { points_checked = samples, min_clearance_mm = Math.Round(minClear, 2), required_mm = gap, tolerance_mm = tol, collisions, parts_in_collision = hitParts },
                branch_routes = branchRoutes.ToList(),
                parts = new { pipes = nPipes, bends = nBends, closing_bends_180 = nU, welds = nPipes + nBends + nU, tracer_pipe_length_mm = Math.Round(pipeLen, 0) },
                warnings
            };
            return CommitTracing(args, doc, dlm, piping, items, pipePart, odT, specName, stage, plan, collisions, nPipes, nBends, nU);
        }
    }
}
