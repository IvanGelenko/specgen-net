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
using Autodesk.ProcessPower.P3dProjectParts;
using Exception = System.Exception;
using Part = Autodesk.ProcessPower.PnP3dObjects.Part;

namespace PipingSpecPlugin.Tracing
{
    // create_tracing: heat tracing of a straight main line, as in the user's sample (view 3): two DN20 tracers (25x4, steel 20) on both
    // sides, 45 degrees below the axis, 2 mm between the walls; they go sideways around flanges and valves with two pipe bends (S-shape,
    // radius 90); the ends are closed with 180 degree bends, so the tracing is one closed loop with no inlet or outlet.
    internal static partial class TracingEngine
    {
        sealed class TBranch
        {
            public string Name, Pipe; public double A, ZMin, ZMax, Rho, LDip, Side, Shift; public bool Down; public Point3d Axis;
            public List<(double x1, double x2, double y1, double y2, double z1, double z2)> Rects = new List<(double, double, double, double, double, double)>();
            public List<string> Handles = new List<string>(); public List<string> Info = new List<string>();
        }
        sealed class TBypass
        {
            public string Tee1, Tee2; public double Leg1A, Leg2A, TopAxisV, LegBottomZ, RB, TeeLat;
            public List<TObst> TopObst = new List<TObst>();
            public List<(Point3d p1, Point3d p2, double rad, string what)> Cyls = new List<(Point3d, Point3d, double, string)>();
            public List<string> Handles = new List<string>();
        }
        sealed class TObst { public string H, Desc; public double A1, A2, LatR, LObs; public TBranch Branch; }
        sealed class TCluster { public double A1, A2, LObs; public List<string> Hs = new List<string>(); public List<TBranch> Branches = new List<TBranch>(); }
        sealed class TItem
        {
            public string Kind;                       // "pipe", "bend", "u"
            public Point3d P1, P2;                    // pipe ends, or the two ports of a U bend
            public Point3d Corner; public Vector3d TIn, TOut; public double R, Theta, L1, L2;   // bends
            public Vector3d X, Y;                     // U orientation
            public string Tracer;
            public bool Hit;                          // the part touches / enters the main line where that cannot be avoided: it is painted violet
            public ObjectId Id; public int Row;
        }

        public static object CreateTracing(JsonElement args)
        {
            var doc = ActiveDocument();
            var dlm = Dlm();
            var piping = Piping();
            string specName = args.TryGetProperty("spec", out var sp) && sp.ValueKind == JsonValueKind.String ? sp.GetString() : "!_Rus-PipeAndFittings_ст20_PN16";
            double nd = args.TryGetProperty("nd", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetDouble() : 20;
            double wallArg = args.TryGetProperty("wall", out var wj) && wj.ValueKind == JsonValueKind.Number ? wj.GetDouble() : 4;
            double gap = args.TryGetProperty("gap", out var gj) && gj.ValueKind == JsonValueKind.Number ? gj.GetDouble() : 10;
            double R = args.TryGetProperty("radius", out var rj) && rj.ValueKind == JsonValueKind.Number ? rj.GetDouble() : 90;
            double pad = args.TryGetProperty("pad", out var pj) && pj.ValueKind == JsonValueKind.Number ? pj.GetDouble() : 10;
            double endMargin = args.TryGetProperty("end_margin", out var ej) && ej.ValueKind == JsonValueKind.Number ? ej.GetDouble() : 50;
            double tailU = args.TryGetProperty("tail_u", out var tj) && tj.ValueKind == JsonValueKind.Number ? tj.GetDouble() : 100;
            double minStraight = args.TryGetProperty("min_straight", out var mj) && mj.ValueKind == JsonValueKind.Number ? mj.GetDouble() : 100;
            double handleLen = args.TryGetProperty("handle", out var hdj) && hdj.ValueKind == JsonValueKind.Number ? hdj.GetDouble() : 100;   // the ideal handle of a bend (0 = no handles)
            double dipTail = args.TryGetProperty("dip_tail", out var dtj) && dtj.ValueKind == JsonValueKind.Number ? dtj.GetDouble() : 100;       // handles of the U bend under a vent / drain
            double overshoot = args.TryGetProperty("branch_overshoot", out var ovj) && ovj.ValueKind == JsonValueKind.Number ? ovj.GetDouble() : 50; // how far the dip goes beyond the end of the branch
            double Rdip = args.TryGetProperty("dip_radius", out var drj) && drj.ValueKind == JsonValueKind.Number ? drj.GetDouble() : 40;   // bend radius around a vent / drain: 40 (the user allows 40 there; 90 as in the sample leaves 0 mm to the valve)
            bool bypassOn = !(args.TryGetProperty("bypass", out var bpj) && bpj.ValueKind == JsonValueKind.False);
            double? bypSideArg = args.TryGetProperty("bypass_side", out var bsj) && bsj.ValueKind == JsonValueKind.Number ? (bsj.GetDouble() < 0 ? -1.0 : 1.0) : (double?)null;   // default: the supply tracer
            double dipSide = args.TryGetProperty("dip_side", out var dsj) && dsj.ValueKind == JsonValueKind.Number && dsj.GetDouble() > 0 ? 1 : -1;  // the tracer that heats vents and drains (the sample: the - side)
            bool heatBranches = !(args.TryGetProperty("heat_branches", out var hbj) && hbj.ValueKind == JsonValueKind.False);
            int stage = args.TryGetProperty("stage", out var stj) && stj.ValueKind == JsonValueKind.Number ? stj.GetInt32() : 3;          // 1 parts only, 2 + connectors (both rolled back), 3 full
            if (R <= 0 || gap < 0 || pad < 0 || endMargin < 0 || tailU < 0) throw new ArgumentException("radius must be > 0 and gap, pad, end_margin, tail_u must not be negative");
            var handles = args.GetProperty("pipes").EnumerateArray().Select(e => e.GetString()).Where(h => !string.IsNullOrWhiteSpace(h)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (handles.Count == 0 || handles.Count > 40) throw new ArgumentException("pipes: 1..40 handles of the main line");

            // ---- the tracer pipe part -------------------------------------------------------------------------
            var sm = SpecManager.GetSpecManager();
            string key = LoadSpecKey(sm, piping, specName);
            var cands = CollectParts(sm, specName, key, "Pipe", nd);
            var chosen = cands.FirstOrDefault(c => Math.Abs(c.wall - wallArg) < 1e-6);
            if (chosen.part == null) throw new InvalidOperationException("No DN" + nd + " pipe with wall " + wallArg + " in " + specName + ". Available: " + string.Join(" | ", cands.Select(c => "s=" + c.wall + " " + c.desc)));
            var pipePart = chosen.part;
            double odT = OuterDiameter(pipePart), rT = odT / 2;

            // ---- the main line: collinear pipes -----------------------------------------------------------------
            var pipes = handles.Select(h => ReadSPipe(doc, dlm, IdFromHandle(doc.Database, h), new List<(ObjectId, Point3d)>())).ToList();
            var d = pipes[0].D;
            foreach (var p in pipes)
                if (p.D.CrossProduct(d).Length > 1e-3 || (p.A - pipes[0].A).CrossProduct(d).Length > Math.Max(p.Od, 1))
                    throw new InvalidOperationException("The main pipes are not on one straight line (pipe " + p.H + "). Pass the pipes of one straight section.");
            var pts = pipes.SelectMany(p => new[] { p.A, p.B }).ToList();
            Point3d A0 = pts.OrderBy(q => (q - pipes[0].A).DotProduct(d)).First();
            double Ltot = pts.Max(q => (q - A0).DotProduct(d));
            double rMain = pipes.Max(p => p.Od) / 2;
            var sdir = d.CrossProduct(Vector3d.ZAxis);
            if (sdir.Length < 1e-6) throw new InvalidOperationException("The main line is vertical: tracing below it at 45 degrees is not defined.");
            sdir = sdir.GetNormal();
            var up = sdir.CrossProduct(d).GetNormal();
            Point3d P(double a, double l, double v) => A0 + d * a + sdir * l - up * v;

            double UpZ = up.Z;
            double VOfZ(double a, double z) => (A0.Z + d.Z * a - z) / UpZ;   // vertical offset below the main axis (along -up) of a world height z at position a

            double rOff = rMain + gap + rT;
            double l0 = rOff * Math.Sin(Math.PI / 4), v0 = rOff * Math.Cos(Math.PI / 4);
            double rU = l0;                       // the 180 degree bend joins the two tracers: radius = half their distance
            var warnings = new List<string>();

            // ---- flanges and valves on the line ---------------------------------------------------------------
            var objs = PlantObjects(doc, dlm);
            var obst = new List<TObst>();

            // ---- an elbow at the start of the line: the tracers go round it into the straight run behind it, the closing bends stand at the free end of that run -------
            Vector3d startDir = d;
            Func<double, List<Point3d>> prefixVerts = null;
            var cornerRs = new List<(Point3d p, double r)>();
            var extraCyls = new List<(Point3d p1, Point3d p2, double rad, string what)>();
            object prefixInfo = null;
            try
            {
                ObjectId elbowId = ObjectId.Null; (string name, Point3d pos, Vector3d dir) e2Port = default;
                foreach (var o in objs.Where(o => o.cls.Contains("PipeInlineAsset")))
                {
                    var ep = SafePorts(doc, o.id); if (ep.Count != 2) continue;
                    if (!System.Text.RegularExpressions.Regex.IsMatch(DescById(doc, dlm, o.id), "Отвод|Elbow")) continue;
                    var atA0 = ep.Where(q => q.pos.DistanceTo(A0) < 1.5).ToList(); if (atA0.Count != 1) continue;
                    elbowId = o.id; e2Port = ep.First(q => q.pos.DistanceTo(A0) >= 1.5); break;
                }
                if (!elbowId.IsNull)
                {
                    var e1 = e2Port.dir.GetNormal().Negate();                                  // travel direction along the run behind the elbow, toward the elbow
                    if (Math.Abs(e1.DotProduct(d)) > 0.01 || Math.Abs(e1.DotProduct(up)) > 0.05) throw new InvalidOperationException("the elbow at the start is not a 90 degree horizontal one");
                    double Re = A0.DistanceTo(e2Port.pos) / Math.Sqrt(2);
                    var Pc = A0 - d * Re;
                    var Cc = e2Port.pos + d * Re;                                              // centre of the elbow arc
                    // the straight run behind the elbow: pipes joined end to end, to its free end
                    var xp = new List<(Point3d s1, Point3d s2, double od)>(); var xh = new List<string>();
                    string curH = elbowId.Handle.ToString(), fromPort = e2Port.name; var prevH = "";
                    var nextId = elbowId; string portHere = e2Port.name;
                    for (int guard = 0; guard < 30; guard++)
                    {
                        string hh = nextId.Handle.ToString();
                        var ln = ConnList(doc, nextId).Where(c => (c.h1 == hh && c.p1 == portHere) || (c.h2 == hh && c.p2 == portHere)).ToList();
                        if (ln.Count == 0) break;                                              // free end
                        var linkTo = ln[0].h1 == hh ? ln[0].h2 : ln[0].h1;
                        var nid = IdFromHandle(doc.Database, linkTo);
                        if (nid.ObjectClass.Name.Contains("Connector"))
                        {
                            var other = ConnList(doc, nid).Select(c => c.h1 == linkTo ? c.h2 : c.h1).FirstOrDefault(x => x != hh);
                            if (other == null) break;
                            nid = IdFromHandle(doc.Database, other);
                        }
                        if (nid.ObjectClass.Name != "AcPpDb3dPipe") throw new InvalidOperationException("behind the elbow there is " + DescById(doc, dlm, nid).Trim() + ", not a plain pipe run (only pipes are supported there)");
                        var np = SafePorts(doc, nid); var s1 = np.First(q => q.name == "S1").pos; var s2 = np.First(q => q.name == "S2").pos;
                        if (((s2 - s1).GetNormal().CrossProduct(e1)).Length > 0.01) throw new InvalidOperationException("the run behind the elbow bends again");
                        double od3; using (doc.LockDocument()) using (var tr = doc.Database.TransactionManager.StartTransaction()) { od3 = ((Pipe)tr.GetObject(nid, OpenMode.ForRead)).OuterDiameter; tr.Commit(); }
                        xp.Add((s1, s2, od3)); xh.Add(nid.Handle.ToString());
                        // continue through the far end of this pipe
                        portHere = (s1 - e2Port.pos).Length < (s2 - e2Port.pos).Length ? "S2" : "S1";
                        nextId = nid;
                    }
                    if (xp.Count == 0) throw new InvalidOperationException("nothing behind the elbow");
                    var allx = xp.SelectMany(q => new[] { q.s1, q.s2 }).ToList();
                    Point3d F = allx.OrderBy(q => (q - Pc).DotProduct(e1)).First();             // the free end
                    var sx = e1.CrossProduct(Vector3d.ZAxis).GetNormal();
                    var upx = sx.CrossProduct(e1).GetNormal();
                    if (upx.DotProduct(up) < 0.99) throw new InvalidOperationException("the run behind the elbow is not in the same horizontal plane");
                    Point3d Px(double a, double l, double v) => F + e1 * a + sx * l - up * v;
                    double sdotE = sdir.DotProduct(e1);
                    startDir = e1;
                    prefixVerts = sigma =>
                    {
                        var corner = Pc + e1 * (sigma * l0 * sdotE) + sx * (sigma * l0) - up * v0;
                        bool inner = (sx * sigma).DotProduct(d) > 0;
                        double rT2 = inner ? Re - l0 : Re + l0;
                        if (rT2 <= 1) throw new InvalidOperationException("the elbow radius is too small for the tracer to follow it");
                        cornerRs.Add((corner, rT2));
                        var first = Px(-endMargin + tailU, sigma * l0, v0);
                        if ((corner - first).DotProduct(e1) < rT2 + minStraight * 0) throw new InvalidOperationException("the run behind the elbow is too short for the closing bend and the corner");
                        return new List<Point3d> { first, corner };
                    };
                    foreach (var q in xp) extraCyls.Add((q.s1, q.s2, q.od / 2, "pipe behind the elbow"));
                    var u0 = (e2Port.pos - Cc) / Re; var u1 = (A0 - Cc) / Re;
                    Point3d prevArc = e2Port.pos;
                    for (int k = 1; k <= 12; k++)
                    {
                        double phi = Math.PI / 2 * k / 12.0;
                        var arcPt = Cc + (u0 * Math.Cos(phi) + u1 * Math.Sin(phi)) * Re;
                        extraCyls.Add((prevArc, arcPt, rMain, "the elbow")); prevArc = arcPt;
                    }
                    prefixInfo = new { elbow = elbowId.Handle.ToString(), run_behind = xh, elbow_radius = Math.Round(Re, 1), free_end = Arr(F), tracer_radius_inner = Math.Round(Re - l0, 1), tracer_radius_outer = Math.Round(Re + l0, 1) };
                }
            }
            catch (Exception pe) { warnings.Add("The elbow at the start is not followed by the tracers: " + pe.Message); prefixVerts = null; startDir = d; cornerRs.Clear(); extraCyls.Clear(); }
            foreach (var o in objs.Where(o => o.cls.Contains("PipeInlineAsset")))
            {
                var ports = SafePorts(doc, o.id);
                if (ports.Count == 0) continue;
                var along = ports.Select(q => (q.pos - A0).DotProduct(d)).ToList();
                bool onLine = ports.All(q => ((q.pos - A0) - d * (q.pos - A0).DotProduct(d)).Length <= rMain + 5);
                if (!onLine || along.Min() < -5 || along.Max() > Ltot + 5) continue;
                string desc = DescById(doc, dlm, o.id);
                bool known = ValveRx.IsMatch(desc) || FlangeRx.IsMatch(desc) || System.Text.RegularExpressions.Regex.IsMatch(desc, "Заглушка|Blind|Диск|Тройник|Tee");
                if (!known)
                    throw new InvalidOperationException("Part " + o.id.Handle + " on the main line (" + desc.Trim() + ") is not a flange or a valve. Tracing of lines with tees, reducers or elbows is not implemented yet; pass a straight section of pipes, flanges and valves.");
                double latR = 0;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ext = ((Entity)tr.GetObject(o.id, OpenMode.ForRead)).GeometricExtents;
                    foreach (var x in new[] { ext.MinPoint.X, ext.MaxPoint.X }) foreach (var y in new[] { ext.MinPoint.Y, ext.MaxPoint.Y }) foreach (var z in new[] { ext.MinPoint.Z, ext.MaxPoint.Z })
                        latR = Math.Max(latR, Math.Abs((new Point3d(x, y, z) - A0).DotProduct(sdir)));
                    tr.Commit();
                }
                obst.Add(new TObst { H = o.id.Handle.ToString(), Desc = desc.Trim(), A1 = along.Min(), A2 = along.Max(), LatR = latR, LObs = latR + gap + rT });
            }
            foreach (var o in objs.Where(o => o.cls.Contains("Support") && !o.cls.Contains("Structure")))
                try
                {
                    var q = PartPosition(doc, o.id); double a = (q - A0).DotProduct(d);
                    if (a >= 0 && a <= Ltot && ((q - A0) - d * a).Length <= rMain + 5)
                        warnings.Add("Support " + o.id.Handle + " stands on the line at " + Math.Round(a, 0) + " mm: the tracers pass beside it, check their clearance to its body and plates.");
                }
                catch (Exception) { }
            // ---- vents and drains: vertical branches welded into the main pipes (dynamic ports Dn) --------------------------------
            // As in the sample, one tracer dips along the branch (90 degree bend, vertical legs, a 180 degree bend below the end of the branch) and comes back.
            var branches = new List<TBranch>();
            var mainSet = new HashSet<string>(handles, StringComparer.OrdinalIgnoreCase);
            foreach (var p in pipes)
                foreach (var q in SafePorts(doc, p.Id).Where(q => q.name.StartsWith("D")))
                {
                    double ab = (q.pos - A0).DotProduct(d);
                    if (ab < 0 || ab > Ltot) continue;
                    var linksD = ConnList(doc, p.Id).Where(c => (c.h1 == p.H && c.p1 == q.name) || (c.h2 == p.H && c.p2 == q.name)).ToList();
                    if (linksD.Count != 1) { warnings.Add("Pipe " + p.H + " has a dynamic port " + q.name + " at " + Math.Round(ab, 0) + " mm with " + linksD.Count + " connections (a lost vent or drain?): not heated."); continue; }
                    string nb = linksD[0].h1 == p.H ? linksD[0].h2 : linksD[0].h1;
                    var nbId = IdFromHandle(doc.Database, nb);
                    if (nbId.ObjectClass.Name.Contains("Support")) continue;     // a support is not a branch
                    if (!heatBranches) { warnings.Add("Branch at " + Math.Round(ab, 0) + " mm of pipe " + p.H + " is not heated (heat_branches=false)."); continue; }
                    if (Math.Abs(q.dir.Z) < 0.9) { warnings.Add("Branch " + q.name + " of pipe " + p.H + " at " + Math.Round(ab, 0) + " mm is not vertical: not heated, check it for collisions by hand."); continue; }
                    var br = new TBranch { Name = p.H + "." + q.name, Pipe = p.H, A = ab, Axis = q.pos, Down = q.dir.Z < 0, ZMin = double.MaxValue, ZMax = double.MinValue };
                    var seenB = new HashSet<string> { p.H }; var queue = new Queue<string>(); queue.Enqueue(nb);
                    while (queue.Count > 0 && seenB.Count < 60)
                    {
                        string hb = queue.Dequeue();
                        if (!seenB.Add(hb) || mainSet.Contains(hb)) continue;
                        var idb = IdFromHandle(doc.Database, hb);
                        br.Handles.Add(hb);
                        foreach (var c in ConnList(doc, idb)) { string o = c.h1 == hb ? c.h2 : c.h1; if (o != null && !seenB.Contains(o)) queue.Enqueue(o); }
                        if (idb.ObjectClass.Name.Contains("Connector")) continue;
                        using (doc.LockDocument())
                        using (var tr = doc.Database.TransactionManager.StartTransaction())
                        {
                            var ext = ((Entity)tr.GetObject(idb, OpenMode.ForRead)).GeometricExtents;
                            double rho = 0;
                            foreach (var x in new[] { ext.MinPoint.X, ext.MaxPoint.X }) foreach (var y in new[] { ext.MinPoint.Y, ext.MaxPoint.Y })
                                rho = Math.Max(rho, Math.Sqrt((x - q.pos.X) * (x - q.pos.X) + (y - q.pos.Y) * (y - q.pos.Y)));
                            br.Rects.Add((ext.MinPoint.X, ext.MaxPoint.X, ext.MinPoint.Y, ext.MaxPoint.Y, ext.MinPoint.Z, ext.MaxPoint.Z)); br.Info.Add(hb + " " + idb.ObjectClass.Name.Replace("AcPpDb3d", "") + " z " + Math.Round(ext.MinPoint.Z, 0) + ".." + Math.Round(ext.MaxPoint.Z, 0) + " rho " + Math.Round(rho, 0) + " " + DescById(doc, dlm, idb).Trim());
                            br.ZMin = Math.Min(br.ZMin, ext.MinPoint.Z); br.ZMax = Math.Max(br.ZMax, ext.MaxPoint.Z); br.Rho = Math.Max(br.Rho, rho);
                            tr.Commit();
                        }
                    }
                    if (br.Rects.Count == 0) { warnings.Add("Branch " + br.Name + ": no parts found behind the dynamic port; not heated."); continue; }
                    branches.Add(br);
                }
            branches = branches.OrderBy(b => b.A).ToList();

            // ---- where the dip of every branch goes: on the side without the handwheel, as close to the valve as possible ----------------
            // The real boxes of the branch parts (with the handwheel) are used. Candidates: both sides, lateral offsets that two bends of 15/30/45/60 degrees make
            // exactly (or larger ones), the legs moved along the line; the cheapest one that keeps gap + tracer radius from every box wins.
            bool RectsClear(TBranch br, Point3d q)
            {
                foreach (var r in br.Rects)
                    if (q.Z >= r.z1 - rT - gap && q.Z <= r.z2 + rT + gap)
                    {
                        double dx = Math.Max(Math.Max(r.x1 - q.X, 0), q.X - r.x2), dy = Math.Max(Math.Max(r.y1 - q.Y, 0), q.Y - r.y2);
                        if (Math.Sqrt(dx * dx + dy * dy) < rT + gap - 0.1) return false;
                    }
                return true;
            }
            bool DipOk(TBranch br, double sg, double lat, double shift)
            {
                double vApex = br.Down ? VOfZ(br.A, br.ZMin - overshoot) : VOfZ(br.A, br.ZMax + overshoot);
                double vPort = br.Down ? vApex - (dipTail + Rdip) : vApex + (dipTail + Rdip);
                if (br.Down ? vPort < v0 + Rdip + 0.5 : vPort > v0 - Rdip - 0.5) return false;
                double aL = br.A + shift - Rdip, aR = br.A + shift + Rdip;
                foreach (double a in new[] { aL, aR })
                    for (double vv = v0; ; vv += br.Down ? 15 : -15)
                    {
                        if (br.Down ? vv > vPort : vv < vPort) vv = vPort;
                        if (!RectsClear(br, P(a, sg * lat, vv))) return false;
                        if (vv == vPort) break;
                    }
                var p1 = P(aL, sg * lat, vPort); var p2 = P(aR, sg * lat, vPort);
                var X = br.Down ? up : up.Negate(); var Y = (p2 - p1).GetNormal();
                var cen = p1 - X * dipTail + Y * Rdip;
                for (int k = 0; k <= 24; k++)
                {
                    double phi = Math.PI * k / 24.0;
                    if (!RectsClear(br, cen + (Y.Negate() * Math.Cos(phi) - X * Math.Sin(phi)) * Rdip)) return false;
                }
                return true;
            }
            // All vents, drains and the bypass go on ONE tracer (the supply: it runs first and heats everything); the other one (the return) runs straight.
            // Try each side for all branches together; only if no side suits all of them, every branch takes its best side and a warning says so.
            (double cost, double lat, double shift)? BestFor(TBranch br, double sg)
            {
                var lats = new List<double>();
                foreach (int dg in new[] { 15, 30, 45, 60 }) lats.Add(l0 + 2 * R * (1 - Math.Cos(dg * Math.PI / 180)));
                for (int k = 1; k <= 40; k++) lats.Add(l0 + 90 + 20 * k);
                if (!br.Down) lats = lats.Where(l => l >= rMain + gap + rT - 1e-9).ToList();
                double bestCost = double.MaxValue; double bl = 0, bsh = 0;
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
            // The supply side is the preferred one (dip_side). A branch moves to the other side only if that keeps its dip at the branch: moving the legs along the
            // line away from the branch (to get round the handwheel) costs more than changing the side.
            double supplySide = dipSide;
            foreach (var br in branches.ToList())
            {
                (double cost, double lat, double shift)? bestSupply = BestFor(br, supplySide), bestOther = BestFor(br, -supplySide);
                double costSupply = bestSupply == null ? double.MaxValue : bestSupply.Value.cost + 2.0 * Math.Abs(bestSupply.Value.shift);
                double costOther = bestOther == null ? double.MaxValue : bestOther.Value.cost + 2.0 * Math.Abs(bestOther.Value.shift) + 100;
                if (bestSupply == null && bestOther == null) { warnings.Add("Branch " + br.Name + ": no clear place for the tracer dip beside it (handwheel?); not heated."); branches.Remove(br); continue; }
                bool useSupply = costSupply <= costOther;
                var pick = useSupply ? bestSupply.Value : bestOther.Value;
                br.Side = useSupply ? supplySide : -supplySide; br.LDip = pick.lat; br.Shift = pick.shift;
                if (!useSupply) warnings.Add("Branch " + br.Name + " is heated from the return side: on the supply side the dip would have to stand " + Math.Abs(bestSupply == null ? 0 : bestSupply.Value.shift) + " mm away from it (handwheel).");
            }

            double bypSide = bypSideArg ?? supplySide;   // the tracer that goes round the bypass: the supply one

            // ---- a bypass loop: two tees on the line joined by a chain of parts going up, across and down -------------------------------
            TBypass byp = null;
            if (bypassOn)
                try
                {
                    var tees = new List<(ObjectId id, double a, double lat)>();
                    foreach (var o in objs.Where(o => o.cls.Contains("PipeInlineAsset")))
                    {
                        var tp = SafePorts(doc, o.id); if (tp.Count != 3) continue;
                        string td = DescById(doc, dlm, o.id); if (!System.Text.RegularExpressions.Regex.IsMatch(td, "Тройник|Tee")) continue;
                        var onAx = tp.Where(q => ((q.pos - A0) - d * (q.pos - A0).DotProduct(d)).Length <= rMain + 5).ToList();
                        var off = tp.Where(q => !onAx.Contains(q)).ToList();
                        if (onAx.Count != 2 || off.Count != 1 || off[0].dir.DotProduct(up) < 0.9) continue;
                        double latR = 0;
                        using (doc.LockDocument())
                        using (var tr = doc.Database.TransactionManager.StartTransaction())
                        {
                            var ext = ((Entity)tr.GetObject(o.id, OpenMode.ForRead)).GeometricExtents;
                            foreach (var x in new[] { ext.MinPoint.X, ext.MaxPoint.X }) foreach (var y in new[] { ext.MinPoint.Y, ext.MaxPoint.Y }) foreach (var z in new[] { ext.MinPoint.Z, ext.MaxPoint.Z })
                                latR = Math.Max(latR, Math.Abs((new Point3d(x, y, z) - A0).DotProduct(sdir)));
                            tr.Commit();
                        }
                        tees.Add((o.id, (off[0].pos - A0).DotProduct(d), latR));
                    }
                    tees = tees.OrderBy(t => t.a).ToList();
                    if (tees.Count >= 2)
                    {
                        var t1 = tees[0]; var t2 = tees[tees.Count - 1];
                        var tp1 = SafePorts(doc, t1.id); var branchPort = tp1.First(q => ((q.pos - A0) - d * (q.pos - A0).DotProduct(d)).Length > rMain + 5);
                        string h1 = t1.id.Handle.ToString(), h2 = t2.id.Handle.ToString();
                        var link = ConnList(doc, t1.id).First(c => (c.h1 == h1 && c.p1 == branchPort.name) || (c.h2 == h1 && c.p2 == branchPort.name));
                        string prevH = h1, curH = link.h1 == h1 ? link.h2 : link.h1;
                        var chain = new List<ObjectId>();
                        for (int guard = 0; guard < 60 && curH != null && curH != h2; guard++)
                        {
                            var cid = IdFromHandle(doc.Database, curH);
                            if (!cid.ObjectClass.Name.Contains("Connector")) chain.Add(cid);
                            string nxt = null;
                            foreach (var c in ConnList(doc, cid)) { string o2 = c.h1 == curH ? c.h2 : c.h1; if (o2 != null && o2 != prevH) nxt = o2; }
                            prevH = curH; curH = nxt;
                        }
                        if (curH != h2) throw new InvalidOperationException("the chain from the first tee does not end at the last tee");
                        var b = new TBypass { Tee1 = h1, Tee2 = h2, TeeLat = Math.Max(t1.lat, t2.lat) };
                        var legs = new List<(Point3d s1, Point3d s2, double od)>(); var tops = new List<(Point3d s1, Point3d s2, double od)>();
                        double rbMax = 0;
                        foreach (var cid in chain)
                        {
                            b.Handles.Add(cid.Handle.ToString());
                            var cp = SafePorts(doc, cid);
                            if (cid.ObjectClass.Name == "AcPpDb3dPipe")
                            {
                                double od2; using (doc.LockDocument()) using (var tr = doc.Database.TransactionManager.StartTransaction()) { od2 = ((Pipe)tr.GetObject(cid, OpenMode.ForRead)).OuterDiameter; tr.Commit(); }
                                var s1 = cp.First(q => q.name == "S1").pos; var s2 = cp.First(q => q.name == "S2").pos;
                                var dir = (s2 - s1).GetNormal();
                                if (Math.Abs(dir.DotProduct(up)) > 0.9) legs.Add((s1, s2, od2)); else tops.Add((s1, s2, od2));
                                b.Cyls.Add((s1, s2, od2 / 2, "bypass pipe " + cid.Handle)); rbMax = Math.Max(rbMax, od2 / 2);
                            }
                        }
                        if (legs.Count != 2 || tops.Count < 1) throw new InvalidOperationException("expected two vertical legs and a horizontal run, found " + legs.Count + " / " + tops.Count);
                        legs = legs.OrderBy(l => (l.s1 - A0).DotProduct(d)).ToList();
                        b.RB = rbMax;
                        b.Leg1A = ((legs[0].s1 - A0).DotProduct(d) + (legs[0].s2 - A0).DotProduct(d)) / 2; b.Leg2A = ((legs[1].s1 - A0).DotProduct(d) + (legs[1].s2 - A0).DotProduct(d)) / 2;
                        b.LegBottomZ = Math.Min(Math.Min(legs[0].s1.Z, legs[0].s2.Z), Math.Min(legs[1].s1.Z, legs[1].s2.Z));
                        var topPts = tops.SelectMany(t => new[] { t.s1, t.s2 }).ToList();
                        double aMid = (b.Leg1A + b.Leg2A) / 2;
                        double zTop = topPts.Average(q => q.Z - d.Z * ((q - A0).DotProduct(d) - aMid));   // top axis height at aMid, slope removed
                        b.TopAxisV = VOfZ(aMid, zTop);
                        // wide parts on the top run: flanges and the valve (a valve counts as wide as the widest flange: its handwheel is above the tracer)
                        var wide = new List<(string h, double a1, double a2, double rho, bool valve)>();
                        foreach (var cid in chain.Where(c => c.ObjectClass.Name == "AcPpDb3dPipeInlineAsset"))
                        {
                            var cp = SafePorts(doc, cid); if (cp.Count == 0) continue;
                            var al = cp.Select(q => (q.pos - A0).DotProduct(d)).ToList();
                            if (!cp.All(q => Math.Abs(q.pos.Z - (zTop + d.Z * (((q.pos - A0).DotProduct(d)) - aMid))) < 12)) continue;    // on the top axis
                            string dd = DescById(doc, dlm, cid);
                            double lat = 0, below = 0;
                            using (doc.LockDocument()) using (var tr = doc.Database.TransactionManager.StartTransaction())
                            {
                                var ext = ((Entity)tr.GetObject(cid, OpenMode.ForRead)).GeometricExtents;
                                foreach (var x in new[] { ext.MinPoint.X, ext.MaxPoint.X }) foreach (var y in new[] { ext.MinPoint.Y, ext.MaxPoint.Y })
                                    lat = Math.Max(lat, Math.Abs((new Point3d(x, y, 0) - new Point3d(A0.X, A0.Y, 0)).DotProduct(sdir)));
                                below = zTop - ext.MinPoint.Z;
                                tr.Commit();
                            }
                            wide.Add((cid.Handle.ToString(), al.Min(), al.Max(), Math.Max(lat, below), ValveRx.IsMatch(dd)));
                        }
                        double flangeRho = wide.Where(w => !w.valve).Select(w => w.rho).DefaultIfEmpty(0).Max();
                        foreach (var w in wide)
                        {
                            double rho = w.valve && flangeRho > 0 ? flangeRho : w.rho;
                            double vbx = (b.RB + gap + rT) * Math.Cos(Math.PI / 4), lbx = (b.RB + gap + rT) * Math.Sin(Math.PI / 4);
                            double need = Math.Sqrt(Math.Max(0, (rho + gap + rT) * (rho + gap + rT) - vbx * vbx));
                            b.TopObst.Add(new TObst { H = w.h, Desc = w.valve ? "bypass valve" : "bypass flange", A1 = w.a1, A2 = w.a2, LatR = rho, LObs = need });
                            var cp2 = SafePorts(doc, IdFromHandle(doc.Database, w.h));
                            b.Cyls.Add((P(w.a1, 0, -b.TopAxisV), P(w.a2, 0, -b.TopAxisV), rho, "bypass part " + w.h));
                        }
                        foreach (var cid in chain.Where(c => c.ObjectClass.Name == "AcPpDb3dPipeInlineAsset"))
                        {
                            var cp = SafePorts(doc, cid);
                            if (cp.Count != 2 || cp[0].dir.DotProduct(cp[1].dir) > -0.99 || Math.Abs((cp[1].pos - cp[0].pos).GetNormal().DotProduct(up)) < 0.9) continue;   // vertical straight parts only
                            double rhoH = 0;
                            using (doc.LockDocument()) using (var tr = doc.Database.TransactionManager.StartTransaction())
                            {
                                var ext = ((Entity)tr.GetObject(cid, OpenMode.ForRead)).GeometricExtents;
                                var mid = cp[0].pos + (cp[1].pos - cp[0].pos) * 0.5;
                                foreach (var x in new[] { ext.MinPoint.X, ext.MaxPoint.X }) foreach (var y in new[] { ext.MinPoint.Y, ext.MaxPoint.Y })
                                    rhoH = Math.Max(rhoH, Math.Sqrt((x - mid.X) * (x - mid.X) + (y - mid.Y) * (y - mid.Y)));
                                tr.Commit();
                            }
                            b.Cyls.Add((cp[0].pos, cp[1].pos, rhoH, "bypass part " + cid.Handle));
                        }
                        byp = b;
                    }
                    else warnings.Add("A bypass needs two tees on the line; found " + tees.Count + ".");
                }
                catch (Exception be) { warnings.Add("The bypass was not recognised, the tracers pass under the main valve: " + be.Message); byp = null; }

            var distinctOd = pipes.Select(p => Math.Round(p.Od, 1)).Distinct().ToList();
            if (distinctOd.Count > 1) warnings.Add("The main pipes have different outer diameters (" + string.Join(", ", distinctOd) + "); the largest is used.");

            // ---- the S-shaped detour: two bends of angle theta, a multiple of 15 degrees (user rule), Delta = 2R(1-cos theta) + D sin theta ---
            // The largest theta in {60, 45, 30, 15} (multiples of 15, user rule) that leaves a straight piece D of at least min_straight between the two bends.
            void Detour(double delta, out double theta, out double diag, bool with40 = false, double room = double.MaxValue)
            {
                bool Fits(double t) => delta / Math.Tan(t) + 2 * R * Math.Tan(t / 2) <= room + 1e-6;
                // the two bends welded to each other (no straight piece), when the shift is exactly right for 15, 30, 45 or 60 degrees
                foreach (int deg0 in new[] { 60, 45, 30, 15 })
                {
                    double t0 = deg0 * Math.PI / 180;
                    if (Math.Abs(delta - 2 * R * (1 - Math.Cos(t0))) < 0.05 && Fits(t0)) { theta = t0; diag = 0; return; }
                }
                // the largest angle that leaves a straight piece of at least min_straight and fits the room
                foreach (int deg in new[] { 60, 45, 30, 15 })
                {
                    double t = deg * Math.PI / 180;
                    double dd = (delta - 2 * R * (1 - Math.Cos(t))) / Math.Sin(t);
                    if (dd >= minStraight - 1e-6 && Fits(t)) { theta = t; diag = dd; return; }
                }
                // no room for that: accept a shorter straight piece (the largest angle that fits)
                foreach (int deg in new[] { 60, 45, 30, 15 })
                {
                    double t = deg * Math.PI / 180;
                    double dd = (delta - 2 * R * (1 - Math.Cos(t))) / Math.Sin(t);
                    if (dd >= 0 && Fits(t)) { theta = t; diag = dd; return; }
                }
                // very small shift: 15 degrees with whatever straight piece the shift leaves (never negative)
                theta = 15 * Math.PI / 180;
                diag = Math.Max(0, (delta - 2 * R * (1 - Math.Cos(theta))) / Math.Sin(theta));
            }

            // clusters of obstacles that need a detour
            // A part needs a detour only if it is wider than the pipe itself (a tee body is as wide as the pipe: its branch points up, the tracers are below).
            bool Needs(TObst o) => o.Branch != null || (o.LObs > l0 + 1 && o.LatR > rMain + 1.0);
            // a vent / drain is a pseudo obstacle for the tracer that dips along it: the window covers its two 90 degree bends
            var branchObst = branches.Select(b => new TObst { H = "branch " + b.Name, Desc = "vent/drain", A1 = b.A + b.Shift - 2 * Rdip - minStraight, A2 = b.A + b.Shift + 2 * Rdip + minStraight, LatR = b.LDip, LObs = b.LDip, Branch = b }).ToList();
            double CompactTrans(double delta)   // the shortest stretch along the line one transition of the shift 'delta' takes (two bends and the straight piece between them, tangents included)
            {
                double best = double.MaxValue;
                foreach (int deg in new[] { 15, 30, 45, 60 })
                {
                    double t = deg * Math.PI / 180;
                    if ((delta - 2 * R * (1 - Math.Cos(t))) / Math.Sin(t) < 0) continue;
                    best = Math.Min(best, 2 * R * Math.Tan(t / 2) + delta / Math.Tan(t));
                }
                if (best == double.MaxValue) best = 2 * R * Math.Tan(Math.PI / 24) + delta / Math.Tan(Math.PI / 12);
                return best;
            }
            List<TCluster> BuildClusters(IEnumerable<TObst> list, double baseLat = double.NaN)
            {
                double bl0 = double.IsNaN(baseLat) ? l0 : baseLat;
                var res = new List<TCluster>();
                foreach (var o in list.Where(o => double.IsNaN(baseLat) ? Needs(o) : o.LObs > baseLat + 1).OrderBy(o => o.A1))
                {
                    if (res.Count > 0)
                    {
                        var c = res[res.Count - 1];
                        double lobsM = Math.Max(c.LObs, o.LObs);
                        // two detours may be separate if both transitions (out of the first, into the next) fit the gap in their most compact form
                        if (o.A1 - c.A2 < 2 * CompactTrans(lobsM - bl0) + 2 * pad)
                        {
                            c.A2 = Math.Max(c.A2, o.A2); c.LObs = lobsM; c.Hs.Add(o.H); if (o.Branch != null) c.Branches.Add(o.Branch);
                            continue;
                        }
                    }
                    var nc = new TCluster { A1 = o.A1, A2 = o.A2, LObs = o.LObs }; nc.Hs.Add(o.H); if (o.Branch != null) nc.Branches.Add(o.Branch);
                    res.Add(nc);
                }
                return res;
            }
            // The tracer that does NOT heat a branch must still pass it without touching: if a straight pass at the usual lateral offset comes too close to the branch
            // parts (a wide DN50 stub next to the tracer), it gets a plain detour (no dip) at that place.
            var passObst = new Dictionary<double, List<TObst>> { [+1] = new List<TObst>(), [-1] = new List<TObst>() };
            foreach (var br in branches)
            {
                double other = -br.Side;
                var cands2 = new List<double> { l0 };
                foreach (int dg in new[] { 15, 30, 45, 60 }) cands2.Add(l0 + 2 * R * (1 - Math.Cos(dg * Math.PI / 180)));
                for (int k = 1; k <= 20; k++) cands2.Add(l0 + 90 + 20 * k);
                foreach (double lat in cands2)
                {
                    bool ok = true;
                    for (double aa = br.A - 150; aa <= br.A + 150 && ok; aa += 10) ok = RectsClear(br, P(aa, other * lat, v0));
                    if (!ok) continue;
                    if (lat > l0 + 0.01) passObst[other].Add(new TObst { H = "pass " + br.Name, Desc = "branch stub", A1 = br.A - 90, A2 = br.A + 90, LatR = Math.Max(lat, rMain + 2), LObs = lat });
                    break;
                }
            }
            var clustersFor = new Dictionary<double, List<TCluster>>
            {
                [+1] = BuildClusters(obst.Concat(branchObst.Where(o => o.Branch.Side > 0)).Concat(passObst[+1])),
                [-1] = BuildClusters(obst.Concat(branchObst.Where(o => o.Branch.Side < 0)).Concat(passObst[-1]))
            };

            // The closing 180 degree bends lie BEYOND the ends of the line (their arc passes under the axis at less than the pipe radius, so inside the line it would cut the pipe):
            // the arc ends end_margin beyond the pipe end, the ports are tail_u further in.
            double aStart = -endMargin + tailU, aEnd = Ltot + endMargin - tailU;
            if (prefixVerts == null && aEnd - aStart < 2 * R) throw new InvalidOperationException("The line is too short (" + Math.Round(Ltot, 0) + " mm) for the tracing.");
            warnings.Add("The closing bends lie beyond the line: " + Math.Round(endMargin + rU, 0) + " mm past each pipe end (the line ends must be free there).");
            var detours = new List<object>();
            var dipCorners = new List<Point3d>();   // corners of the 90 degree bends of the dips: they use dip_radius

            // ---- the route of the tracer round the bypass (one tracer, bypass_side) -------------------------------------------------------
            // rises beside the main pipe (outside its wall), moves in to the side "under the bypass pipe at 45 degrees", runs under the top run (detours round
            // the flanges and the valve), comes down the other leg and returns to the main line. Between the tees this tracer is absent: the other one
            // keeps running under the main valve.
            double bLb = 0, bVb = 0, bLpass = 0, bVtop = 0, bAR1 = 0, bAR2 = 0, bVS1 = 0, bX1 = 0, bX2 = 0, bRt1 = 0, bWin1 = 0, bWin2 = 0;
            var bTop = new List<TCluster>();
            if (byp != null)
                try
                {
                    double bRoff = byp.RB + gap + rT; bLb = bRoff * Math.Sin(Math.PI / 4); bVb = bRoff * Math.Cos(Math.PI / 4);
                    bLpass = Math.Max(Math.Max(rMain, byp.TeeLat) + gap + rT, l0 + 5);
                    foreach (int dg in new[] { 15, 30, 45, 60 })
                    {
                        double nice = 2 * R * (1 - Math.Cos(dg * Math.PI / 180));
                        if (nice >= bLpass - l0 - 1e-9) { bLpass = l0 + nice; break; }
                    }
                    Detour(bLpass - l0, out double th1, out double dg1); bRt1 = R * Math.Tan(th1 / 2); bX1 = (bLpass - l0) / Math.Tan(th1);
                    Detour(bLpass - bLb, out double th2, out double dg2); bX2 = (bLpass - bLb) / Math.Tan(th2);
                    bAR1 = byp.Leg1A + bVb; bAR2 = byp.Leg2A - bVb;
                    bVtop = byp.TopAxisV + bVb;
                    bVS1 = VOfZ(bAR1, byp.LegBottomZ + 80);
                    if (bVS1 - bX2 - bVtop < 2 * R + minStraight) throw new InvalidOperationException("the legs of the bypass are too short for the tracer to move in and turn");
                    double c2a = bAR1 - R - bRt1 - minStraight, c1a = c2a - bX1;
                    double c3a = bAR2 + R + bRt1 + minStraight, c4a = c3a + bX1;
                    bWin1 = c1a - pad; bWin2 = c4a + pad;
                    bTop = BuildClusters(byp.TopObst, bLb);
                }
                catch (Exception bex) { warnings.Add("The tracer route round the bypass is not possible: " + bex.Message); byp = null; }

            // The tracer at lateral sign sigma as polylines in order of growing 'a'; a vent/drain splits it: ... corner, leg end | U | leg start, corner ...
            (List<List<Point3d>> paths, List<TItem> dips) Vertices(double sigma)
            {
                var paths = new List<List<Point3d>>(); var dips = new List<TItem>();
                var v = prefixVerts != null ? prefixVerts(sigma) : new List<Point3d> { P(aStart, sigma * l0, v0) };
                bool bypHere = byp != null && sigma == bypSide, bypDone = false;
                void EmitBypass()
                {
                    double c2 = bAR1 - R - bRt1 - minStraight, c1 = c2 - bX1;
                    v.Add(P(c1, sigma * l0, v0)); v.Add(P(c2, sigma * bLpass, v0)); v.Add(P(bAR1, sigma * bLpass, v0));
                    v.Add(P(bAR1, sigma * bLpass, bVS1)); v.Add(P(bAR1, sigma * bLb, bVS1 - bX2)); v.Add(P(bAR1, sigma * bLb, bVtop));
                    foreach (var tc in bTop)
                    {
                        double dlt = tc.LObs - bLb; Detour(dlt, out double tth, out double tdg);
                        double trt = R * Math.Tan(tth / 2), tx = dlt / Math.Tan(tth);
                        double t2 = tc.A1 - pad - trt, t1 = t2 - tx, t3 = tc.A2 + pad + trt, t4 = t3 + tx;
                        v.Add(P(t1, sigma * bLb, bVtop)); v.Add(P(t2, sigma * tc.LObs, bVtop)); v.Add(P(t3, sigma * tc.LObs, bVtop)); v.Add(P(t4, sigma * bLb, bVtop));
                    }
                    v.Add(P(bAR2, sigma * bLb, bVtop)); v.Add(P(bAR2, sigma * bLb, bVS1 - bX2)); v.Add(P(bAR2, sigma * bLpass, bVS1)); v.Add(P(bAR2, sigma * bLpass, v0));
                    double c3 = bAR2 + R + bRt1 + minStraight, c4 = c3 + bX1;
                    v.Add(P(c3, sigma * bLpass, v0)); v.Add(P(c4, sigma * l0, v0));
                }
                double prevEnd = prefixVerts != null ? 0 : aStart;       // where the previous feature of this tracer ends along the line
                var cls = clustersFor[sigma];
                for (int ci = 0; ci < cls.Count; ci++)
                {
                    var c = cls[ci];
                    if (bypHere && !bypDone && c.A1 >= bWin1) { EmitBypass(); bypDone = true; }
                    if (bypHere && c.A2 > bWin1 && c.A1 < bWin2) { prevEnd = bWin2; continue; }      // inside the bypass window this tracer is replaced by the route round the bypass
                    double delta = c.LObs - l0;
                    double nextB = ci + 1 < cls.Count ? (c.A2 + cls[ci + 1].A1) / 2 : aEnd;          // how far this detour may reach after the obstacle
                    if (bypHere && !bypDone && c.A2 < bWin1) nextB = Math.Min(nextB, bWin1);
                    Detour(delta, out double th, out double dg, c.Branches.Count > 0, Math.Min((c.A1 - pad) - prevEnd, nextB - (c.A2 + pad)));
                    double rt = R * Math.Tan(th / 2), x = delta / Math.Tan(th);
                    double c2 = c.A1 - pad - rt, c1 = c2 - x, c3 = c.A2 + pad + rt, c4 = c3 + x;
                    v.Add(P(c1, sigma * l0, v0)); v.Add(P(c2, sigma * c.LObs, v0));
                    foreach (var br in c.Branches.OrderBy(b => b.A))
                    {
                        double vApex = br.Down ? VOfZ(br.A, br.ZMin - overshoot) : VOfZ(br.A, br.ZMax + overshoot);
                        double vPort = br.Down ? vApex - (dipTail + Rdip) : vApex + (dipTail + Rdip);
                        if (br.Down ? vPort < v0 + Rdip + 0.5 : vPort > v0 - Rdip - 0.5)
                            throw new InvalidOperationException("The branch " + br.Name + " is too short for a dip: the legs of the tracer would have no room between the 90 degree bend and the closing bend. Reduce dip_tail or branch_overshoot.");
                        var p1 = P(br.A + br.Shift - Rdip, sigma * c.LObs, vPort); var p2 = P(br.A + br.Shift + Rdip, sigma * c.LObs, vPort);
                        dipCorners.Add(P(br.A + br.Shift - Rdip, sigma * c.LObs, v0)); dipCorners.Add(P(br.A + br.Shift + Rdip, sigma * c.LObs, v0));
                        v.Add(P(br.A + br.Shift - Rdip, sigma * c.LObs, v0)); v.Add(p1);
                        paths.Add(v);
                        dips.Add(new TItem { Kind = "u", P1 = p1, P2 = p2, X = br.Down ? up : up.Negate(), Y = (p2 - p1).GetNormal(), R = Rdip, L1 = dipTail, L2 = dipTail, Tracer = "dip" });
                        v = new List<Point3d> { p2, P(br.A + br.Shift + Rdip, sigma * c.LObs, v0) };
                    }
                    v.Add(P(c3, sigma * c.LObs, v0)); v.Add(P(c4, sigma * l0, v0));
                    prevEnd = c4 + rt;
                }
                if (bypHere && !bypDone) EmitBypass();
                v.Add(P(aEnd, sigma * l0, v0));
                paths.Add(v);
                return (paths, dips);
            }
            foreach (var sg in new[] { +1.0, -1.0 })
                foreach (var c in clustersFor[sg])
                {
                    Detour(c.LObs - l0, out double th, out double dg, c.Branches.Count > 0);
                    detours.Add(new { tracer = sg > 0 ? "+" : "-", obstacles = c.Hs, from_mm = Math.Round(c.A1, 1), to_mm = Math.Round(c.A2, 1), lateral = Math.Round(c.LObs, 1), bend_angle_deg = Math.Round(th * 180 / Math.PI, 2), straight_between_bends = Math.Round(dg, 1), branches = c.Branches.Select(b => b.Name).ToList() });
                }

            // ---- items of the closed loop -----------------------------------------------------------------------
            var items = new List<TItem>();
            // Handles (ручки) of the bends: ideally 100 mm on both sides, else 100/0, 0/100 or 0/0 when the straight piece is too short.
            // On every straight section between two bends the handles are filled first (100 each), the rest becomes a pipe.
            void Walk(List<Point3d> V, string tag)
            {
                var bends = new List<TItem>();
                var entry = new List<Point3d>(); var exit = new List<Point3d>();   // tangent points of every bend
                for (int i = 1; i < V.Count - 1; i++)
                {
                    var tin = (V[i] - V[i - 1]).GetNormal(); var tout = (V[i + 1] - V[i]).GetNormal();
                    double th = Math.Acos(Math.Max(-1, Math.Min(1, tin.DotProduct(tout))));
                    if (th < 1e-4) continue;
                    double rb = dipCorners.Any(c0 => c0.DistanceTo(V[i]) < 1e-6) ? Rdip : R;
                    var crn = cornerRs.Where(c0 => c0.p.DistanceTo(V[i]) < 1e-6).ToList(); if (crn.Count > 0) rb = crn[0].r;
                    double rt = rb * Math.Tan(th / 2);
                    bends.Add(new TItem { Kind = "bend", Corner = V[i], TIn = tin, TOut = tout, R = rb, Theta = th, L1 = 0, L2 = 0, Tracer = tag });
                    entry.Add(V[i] - tin * rt); exit.Add(V[i] + tout * rt);
                }
                double handle = handleLen;
                for (int k = 0; k <= bends.Count; k++)
                {
                    Point3d from = k == 0 ? V[0] : exit[k - 1];
                    Point3d to = k == bends.Count ? V[V.Count - 1] : entry[k];
                    double seg = from.DistanceTo(to);
                    var dir = seg > 1e-6 ? (to - from).GetNormal() : Vector3d.XAxis;
                    var prev = k == 0 ? null : bends[k - 1];      // null: the closing bend, which keeps its own handle
                    var next = k == bends.Count ? null : bends[k];
                    // the straight section must run in the direction of travel; if it runs backwards the bends of two detours overlap
                    var expDir = prev != null ? prev.TOut : (next != null ? next.TIn : (V[V.Count - 1] - V[0]).GetNormal());
                    if ((to - from).DotProduct(expDir) < -0.5)
                        throw new InvalidOperationException("The detours around the flanges and valves overlap near " + Fmt(from) + ": there is no room for the bends between them. Increase the spacing or reduce 'pad'.");
                    // handles in order of preference; the remaining pipe must be absent or at least min_straight long (cl. 6.8.3)
                    double l2p = 0, l1n = 0;
                    foreach (var cand in new[] { (handle, handle), (handle, 0.0), (0.0, handle), (0.0, 0.0) })
                    {
                        if ((prev == null && cand.Item1 > 0) || (next == null && cand.Item2 > 0)) continue;
                        double rest = seg - cand.Item1 - cand.Item2;
                        if (rest < -1e-6) continue;
                        if (rest <= 0.5 || rest >= minStraight - 1e-6) { l2p = cand.Item1; l1n = cand.Item2; break; }
                    }
                    if (prev != null) prev.L2 = l2p;
                    if (next != null) next.L1 = l1n;
                    var pa = from + dir * l2p; var pb = to - dir * l1n;
                    if (pa.DistanceTo(pb) > 0.5 && (pb - pa).DotProduct(dir) > 0) items.Add(new TItem { Kind = "pipe", P1 = pa, P2 = pb, Tracer = tag });
                    if (next != null) items.Add(next);
                }
            }
            var (pathsPlus, dipsPlus) = Vertices(+1); var (pathsMinus, dipsMinus) = Vertices(-1);
            var firstPlus = pathsPlus[0][0]; var firstMinus = pathsMinus[0][0];
            var lastPlus = pathsPlus[pathsPlus.Count - 1][pathsPlus[pathsPlus.Count - 1].Count - 1]; var lastMinus = pathsMinus[pathsMinus.Count - 1][pathsMinus[pathsMinus.Count - 1].Count - 1];
            // U at the start: S1 on the + side, outward direction +d; apex tailU + rU before the ports.
            items.Add(new TItem { Kind = "u", P1 = firstPlus, P2 = firstMinus, X = startDir, Y = (firstMinus - firstPlus).GetNormal(), R = rU, L1 = tailU, L2 = tailU, Tracer = "start" });
            for (int pi = 0; pi < pathsPlus.Count; pi++) { Walk(pathsPlus[pi], "+"); if (pi < dipsPlus.Count) items.Add(dipsPlus[pi]); }
            items.Add(new TItem { Kind = "u", P1 = lastPlus, P2 = lastMinus, X = d.Negate(), Y = (lastMinus - lastPlus).GetNormal(), R = rU, L1 = tailU, L2 = tailU, Tracer = "end" });
            for (int pi = pathsMinus.Count - 1; pi >= 0; pi--)
            {
                var rev = new List<Point3d>(pathsMinus[pi]); rev.Reverse();
                Walk(rev, "-");
                if (pi > 0) items.Add(dipsMinus[pi - 1]);
            }

            // minimum straight pieces between bend welds
            foreach (var it in items.Where(i => i.Kind == "pipe"))
            {
                double len = it.P1.DistanceTo(it.P2);
                if (len < minStraight - 1e-6) warnings.Add("A tracer pipe of " + Math.Round(len, 1) + " mm (tracer " + it.Tracer + ", near " + Fmt(it.P1) + ") is shorter than " + minStraight + " mm between bend welds (GOST 32569 cl. 6.8.3).");
                if (it.P1.DistanceTo(it.P2) < 1) warnings.Add("zero-length tracer pipe");
            }
            // a detour that does not fit between its neighbours shows up as a pipe that runs backwards
            foreach (var it in items.Where(i => i.Kind == "pipe"))
            {
                var dir = (it.P2 - it.P1);
                if (dir.DotProduct(it.Tracer == "-" ? d.Negate() : d) < -1e-6)
                    throw new InvalidOperationException("The detours around the flanges and valves overlap near " + Fmt(it.P1) + ": there is no room for the bends between them. Increase the spacing or reduce 'pad'.");
            }

            // ---- collisions: every point of every tracer part against the main pipe and the wide parts on it --------------
            // The distance from the tracer axis to the main axis must be at least (wall of the main pipe or of the wide part at that place) + gap + tracer radius.
            var collisions = new List<string>(); double minClear = double.MaxValue; int samples = 0;
            void CheckPoint(Point3d q, string what)
            {
                samples++;
                foreach (var br in branches)
                    foreach (var rc in br.Rects)
                        if (q.Z >= rc.z1 - rT - gap && q.Z <= rc.z2 + rT + gap)
                        {
                            double dx = Math.Max(Math.Max(rc.x1 - q.X, 0), q.X - rc.x2), dy = Math.Max(Math.Max(rc.y1 - q.Y, 0), q.Y - rc.y2);
                            double cl = Math.Sqrt(dx * dx + dy * dy) - rT;
                            if (cl < minClear) minClear = cl;
                            if (cl < gap - 0.1 && collisions.Count < 8) collisions.Add(what + " at " + Fmt(q) + " is " + Math.Round(cl, 2) + " mm from a part of the branch " + br.Name + " (needs " + gap + ")");
                        }
                foreach (var cy in extraCyls)
                {
                    var sg2 = cy.p2 - cy.p1; double len22 = sg2.DotProduct(sg2);
                    double tt2 = len22 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, (q - cy.p1).DotProduct(sg2) / len22));
                    double cl2 = (q - (cy.p1 + sg2 * tt2)).Length - rT - cy.rad;
                    if (cl2 < minClear) minClear = cl2;
                    if (cl2 < gap - 0.1 && collisions.Count < 8) collisions.Add(what + " at " + Fmt(q) + " is " + Math.Round(cl2, 2) + " mm from " + cy.what + " (needs " + gap + ")");
                }
                if (byp != null)
                    foreach (var cy in byp.Cyls)
                    {
                        var sg = cy.p2 - cy.p1; double len2 = sg.DotProduct(sg);
                        double tt = len2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, (q - cy.p1).DotProduct(sg) / len2));
                        double cl = (q - (cy.p1 + sg * tt)).Length - rT - cy.rad;
                        if (cl < minClear) minClear = cl;
                        if (cl < gap - 0.1 && collisions.Count < 8) collisions.Add(what + " at " + Fmt(q) + " is " + Math.Round(cl, 2) + " mm from " + cy.what + " (needs " + gap + ")");
                    }
                var v = q - A0; double a = v.DotProduct(d); double r = (v - d * a).Length;
                if (a < -0.5 || a > Ltot + 0.5) return;   // beyond the ends of the line there is no main pipe
                double wall = rMain;
                foreach (var o in obst) if (a >= o.A1 - 1e-6 && a <= o.A2 + 1e-6 && o.LatR > wall) wall = o.LatR;
                double clear = r - rT - wall;
                if (clear < minClear) minClear = clear;
                if (clear < gap - 0.1 && collisions.Count < 8)
                    collisions.Add(what + " at " + Fmt(q) + " is " + Math.Round(clear, 2) + " mm from the " + (wall > rMain + 1e-6 ? "wide part" : "main pipe") + " (needs " + gap + ")");
            }
            void CheckLine(Point3d p1, Point3d p2, string what)
            {
                int n = Math.Max(1, (int)Math.Ceiling(p1.DistanceTo(p2) / 40));
                for (int i = 0; i <= n; i++) CheckPoint(p1 + (p2 - p1) * ((double)i / n), what);
            }
            foreach (var it in items)
            {
                if (it.Kind == "pipe") CheckLine(it.P1, it.P2, "tracer " + it.Tracer + " pipe");
                else if (it.Kind == "bend")
                {
                    double rt = it.R * Math.Tan(it.Theta / 2);
                    var entryPt = it.Corner - it.TIn * rt; var exitPt = it.Corner + it.TOut * rt;
                    CheckLine(entryPt - it.TIn * it.L1, entryPt, "tracer " + it.Tracer + " bend handle");
                    CheckLine(exitPt, exitPt + it.TOut * it.L2, "tracer " + it.Tracer + " bend handle");
                    var center = it.Corner + (it.TOut - it.TIn).GetNormal() * (it.R / Math.Cos(it.Theta / 2));
                    var nrm = it.TIn.CrossProduct(it.TOut).GetNormal();
                    for (int k = 0; k <= 12; k++)
                        CheckPoint(entryPt.TransformBy(Matrix3d.Rotation(it.Theta * k / 12.0, nrm, center)), "tracer " + it.Tracer + " bend");
                }
                else
                {
                    var center = it.P1 - it.X * it.L1 + it.Y * it.R;
                    CheckLine(it.P1, it.P1 - it.X * it.L1, "closing bend handle"); CheckLine(it.P2, it.P2 - it.X * it.L2, "closing bend handle");
                    for (int k = 0; k <= 24; k++)
                    {
                        double phi = Math.PI * k / 24.0;
                        CheckPoint(center + (it.Y.Negate() * Math.Cos(phi) - it.X * Math.Sin(phi)) * it.R, "closing bend");
                    }
                }
            }
            if (collisions.Count > 0) warnings.Add("COLLISIONS with the main line: " + string.Join("; ", collisions));

            int nPipes = items.Count(i => i.Kind == "pipe"), nBends = items.Count(i => i.Kind == "bend"), nU = items.Count(i => i.Kind == "u");
            double pipeLen = items.Where(i => i.Kind == "pipe").Sum(i => i.P1.DistanceTo(i.P2));
            var plan = new
            {
                main_line = new { pipes = handles, length = Math.Round(Ltot, 1), start = Arr(A0), direction = Arr(d), outer_radius = rMain },
                tracer = new { spec = specName, pipe = DescOf(pipePart), od = odT, gap, radius = R, view = "3 (two tracers, 45 degrees below the axis on both sides)" },
                lateral_offset = Math.Round(l0, 2), vertical_offset = Math.Round(v0, 2), end_bend_radius = Math.Round(rU, 2),
                obstacles = obst.Select(o => new { o.H, o.Desc, from_mm = Math.Round(o.A1, 1), to_mm = Math.Round(o.A2, 1), lateral_radius = Math.Round(o.LatR, 1), tracer_lateral = Math.Round(o.LObs, 1), detour = Needs(o) }).ToList(),
                detours,
                branches = branches.Select(b => new { b.Name, at_mm = Math.Round(b.A, 1), direction = b.Down ? "down (drain)" : "up (vent)", parts = b.Handles, part_info = b.Info, radius = Math.Round(b.Rho, 1), tracer_lateral = Math.Round(b.LDip, 1), dip_side = b.Side > 0 ? "+" : "-", legs_shift_mm = b.Shift, lowest_or_highest_z = Math.Round(b.Down ? b.ZMin - overshoot : b.ZMax + overshoot, 1) }).ToList(),
                elbow_at_start = prefixInfo,
                bypass = byp == null ? null : new { tracer = bypSide > 0 ? "+" : "-", lateral_beside_main = Math.Round(bLpass, 1), lateral_under_bypass = Math.Round(bLb, 1), top_tracer_vertical_offset_below_axis = Math.Round(bVb, 1), rise_at_mm = Math.Round(bAR1, 1), descent_at_mm = Math.Round(bAR2, 1), tees = new[] { byp.Tee1, byp.Tee2 }, chain = byp.Handles, leg1_at_mm = Math.Round(byp.Leg1A, 1), leg2_at_mm = Math.Round(byp.Leg2A, 1), top_axis_above_main_mm = Math.Round(-byp.TopAxisV, 1), pipe_radius = byp.RB, tee_lateral_radius = Math.Round(byp.TeeLat, 1),
                    top_obstacles = byp.TopObst.Select(o => new { o.H, o.Desc, from_mm = Math.Round(o.A1, 1), to_mm = Math.Round(o.A2, 1), radius = Math.Round(o.LatR, 1), tracer_lateral_needed = Math.Round(o.LObs, 1) }).ToList() },
                collision_check = new { points_checked = samples, min_clearance_mm = Math.Round(minClear, 2), required_mm = gap, collisions },
                parts = new { pipes = nPipes, bends = nBends, closing_bends_180 = nU, welds = nPipes + nBends + nU, tracer_pipe_length_mm = Math.Round(pipeLen, 0) },
                warnings
            };
            return CommitTracing(args, doc, dlm, piping, items, pipePart, odT, specName, stage, plan, collisions, nPipes, nBends, nU);
        }

        // The common part of the tracing tools: dry run / collision refusal, creation of the bends and pipes, the joints by ConnectorWrapper, the checks.
        static object CommitTracing(JsonElement args, Document doc, DataLinksManager dlm, PipingProject piping, List<TItem> items, SpecPart pipePart, double odT, string specName,
                                    int stage, object plan, List<string> collisions, int nPipes, int nBends, int nU)
        {
            if (IsDry(args)) return new { dry_run = true, document = doc.Name, plan, note = "Nothing was changed. Repeat with dry_run=false to create the tracing." };
            // collisions that cannot be avoided do not stop the creation (route tool: the parts concerned are flagged and painted violet); other tools still refuse
            if (collisions.Count > 0 && !items.Any(i => i.Hit)) throw new InvalidOperationException("The tracing would collide with the main line, nothing was created: " + string.Join("; ", collisions));

            // ---- apply --------------------------------------------------------------------------------------------
            var before = ModelRows(doc, dlm);
            var added = new HashSet<int>();
            var log = new List<string>();
            // one log file per run (the last 10 are kept): after a crash of Plant 3D it shows the spec, the sizes and the last part that was being made
            string progressPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "plant3d_tracing_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            try
            {
                foreach (var old in new System.IO.DirectoryInfo(System.IO.Path.GetTempPath()).GetFiles("plant3d_tracing_*.txt").OrderByDescending(f => f.LastWriteTime).Skip(9)) { try { old.Delete(); } catch { } }
                System.IO.File.WriteAllText(progressPath, "start " + DateTime.Now.ToString("HH:mm:ss") + " spec=" + specName + " tracer=" + DescOf(pipePart) + " items=" + items.Count + " document=" + doc.Name + Environment.NewLine);
            }
            catch { }
            void Prog(string msg) { try { System.IO.File.AppendAllText(progressPath, msg + Environment.NewLine); } catch { } }
            try
            {
                int lineGroup; using (var ag = new PipingObjectAdder(piping.DataLinksManager3d, doc.Database)) lineGroup = ag.LineGroupId;
                var rowsConn = new List<int>();
                using (doc.LockDocument())
                {
                    using (var adder = new PipingObjectAdder(piping.DataLinksManager3d, doc.Database))
                    {
                        string progress = progressPath;
                        int pn = 0;
                        foreach (var it in items.Where(i => i.Kind == "pipe"))
                        {
                            try { System.IO.File.AppendAllText(progress, "pipe " + (pn++) + " tracer " + it.Tracer + " " + Fmt(it.P1) + " -> " + Fmt(it.P2) + " len " + Math.Round(it.P1.DistanceTo(it.P2), 1) + Environment.NewLine); } catch { }
                            it.Row = AddPipe(adder, pipePart, it.P1, it.P2, odT);
                            added.Add(it.Row);
                        }
                        try { System.IO.File.AppendAllText(progress, "pipes done" + Environment.NewLine); } catch { }
                    }
                    int bn = 0;
                    foreach (var it in items.Where(i => i.Kind != "pipe"))
                    {
                        Prog("bend " + (bn++) + " " + it.Kind + " tracer " + it.Tracer + " at " + Fmt(it.Kind == "bend" ? it.Corner : it.P1) + " R " + Math.Round(it.R, 1) + " theta " + Math.Round(it.Theta * 180 / Math.PI, 1) + " L1 " + it.L1 + " L2 " + it.L2);
                        var bp = new Autodesk.ProcessPower.PnP3dPipeRouting.BentPipe();
                        bp.PartSizeProperties = pipePart;
                        bp.SymbolId = ContentManager.GetContentManager().GetSymbol(pipePart, doc.Database);
                        bp.OuterDiameter = odT;
                        Vector3d x, y;
                        if (it.Kind == "bend")
                        {
                            bp.Radius = it.R; bp.Angle = it.Theta; bp.L1 = it.L1; bp.L2 = it.L2;
                            x = it.TIn.Negate();
                            y = (it.TOut - it.TIn * it.TIn.DotProduct(it.TOut)).GetNormal();
                            bp.Position = it.Corner;
                        }
                        else
                        {
                            bp.Radius = it.R; bp.Angle = Math.PI; bp.L1 = it.L1; bp.L2 = it.L2;
                            x = it.X; y = it.Y;
                            bp.Position = it.P1 - x * (it.L1 + it.R);   // the apex; the ports are L + R away along X
                        }
                        var z = x.CrossProduct(y).GetNormal();
                        if (!bp.SetOrientation(x, z)) throw new InvalidOperationException("SetOrientation rejected the axes of a bend near " + Fmt(it.Kind == "bend" ? it.Corner : it.P1));
                        it.Id = bp.Append(lineGroup);
                        it.Row = RowId(dlm, it.Id);
                        if (it.Row > 0) added.Add(it.Row);
                    }
                }
                Prog("bends done");
                foreach (var it in items.Where(i => i.Kind == "pipe")) it.Id = FindByRow(doc, dlm, it.Row);

                // every created part must have its ports where the plan says
                foreach (var it in items)
                {
                    var exp = new List<Point3d>();
                    if (it.Kind == "pipe") { exp.Add(it.P1); exp.Add(it.P2); }
                    else if (it.Kind == "bend") { double rt = it.R * Math.Tan(it.Theta / 2); exp.Add(it.Corner - it.TIn * (rt + it.L1)); exp.Add(it.Corner + it.TOut * (rt + it.L2)); }
                    else { exp.Add(it.P1); exp.Add(it.P2); }
                    var got = PortsOf(doc, it.Id);
                    foreach (var e in exp)
                        if (!got.Any(g => g.pos.DistanceTo(e) < 0.6))
                            throw new InvalidOperationException("A " + it.Kind + " was placed with its ports at " + string.Join(" ", got.Select(g => g.name + Fmt(g.pos))) + " instead of the planned " + Fmt(e) + ".");
                }

                if (stage == 1)
                {
                    string rb1 = RollbackSince(doc, dlm, added, before);
                    return new { plan, stage = 1, note = "Stage 1: pipes and bends were created, their ports checked, and everything was rolled back (" + rb1 + ").", log };
                }
                // joints: Plant 3D's own way (the routing class ConnectorWrapper, joint "Buttweld (RUS)"): it creates the connector with a typed weld element
                // and PL end conditions on both ports and links it to both parts, exactly like the connectors of the user's sample.
                // (A connector built by hand has no end condition on one port and Plant 3D then fails to connect "" with PL.)
                int lineGroupId;
                using (var ag = new PipingObjectAdder(piping.DataLinksManager3d, doc.Database)) lineGroupId = ag.LineGroupId;
                int madeConnectors = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    var A = items[i]; var B = items[(i + 1) % items.Count];
                    var pa = PortsOf(doc, A.Id); var pb = PortsOf(doc, B.Id);
                    double best = double.MaxValue; int ia = -1, ib = -1;
                    for (int x = 0; x < pa.Count; x++) for (int y = 0; y < pb.Count; y++)
                        { double dd = pa[x].pos.DistanceTo(pb[y].pos); if (dd < best) { best = dd; ia = x; ib = y; } }
                    if (best > 0.6) throw new InvalidOperationException("The parts " + i + " and " + ((i + 1) % items.Count) + " of the loop do not meet (closest ports " + Math.Round(best, 2) + " mm apart).");
                    if (pa[ia].dir.GetNormal().DotProduct(pb[ib].dir.GetNormal()) > -0.9) throw new InvalidOperationException("The ports at junction " + i + " do not face each other.");
                    Prog("joint " + i + " " + A.Kind + "->" + B.Kind);
                    using (doc.LockDocument())
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        var partA = (Part)tr.GetObject(A.Id, OpenMode.ForWrite); var partB = (Part)tr.GetObject(B.Id, OpenMode.ForWrite);
                        var pairA = new Pair(); pairA.ObjectId = A.Id; pairA.Port = partA.FindPort(pa[ia].name, PortType.All);
                        var pairB = new Pair(); pairB.ObjectId = B.Id; pairB.Port = partB.FindPort(pb[ib].name, PortType.All);
                        var cw = new Autodesk.ProcessPower.PnP3dPipeRouting.ConnectorWrapper();
                        cw.Spec = specName;
                        cw.Connect(pairA, pairB, WeldJointName);
                        cw.Append(pairA, pairB, lineGroupId);
                        tr.Commit();
                    }
                    {
                        // a joint must not damage the two parts: their ports stay where they were (Plant 3D now and then leaves NaN there)
                        var paAfter = PortsOf(doc, A.Id); var pbAfter = PortsOf(doc, B.Id);
                        bool badPort = paAfter.Concat(pbAfter).Any(g => double.IsNaN(g.pos.X) || double.IsNaN(g.pos.Y) || double.IsNaN(g.pos.Z) || double.IsInfinity(g.pos.X));
                        if (badPort) throw new InvalidOperationException("Joint " + i + " (" + A.Kind + " -> " + B.Kind + ") damaged its parts: before " + string.Join(" ", pa.Select(g => g.name + Fmt(g.pos))) + " | " + string.Join(" ", pb.Select(g => g.name + Fmt(g.pos))) + "; after " + string.Join(" ", paAfter.Select(g => g.name + Fmt(g.pos))) + " | " + string.Join(" ", pbAfter.Select(g => g.name + Fmt(g.pos))) + ".");
                    }
                }
                // Plant 3D now and then makes a second connector at a junction: keep one per junction
                {
                    string K(Point3d q) => Math.Round(q.X / 0.5) + "," + Math.Round(q.Y / 0.5) + "," + Math.Round(q.Z / 0.5);
                    var created = ModelRows(doc, dlm).Where(r => !before.Contains(r)).ToHashSet();
                    var groups = new Dictionary<string, List<ObjectId>>();
                    foreach (var o in PlantObjects(doc, dlm).Where(o => o.cls.Contains("Connector") && created.Contains(o.row)))
                        foreach (var q in SafePorts(doc, o.id).Take(1))
                        { var k = K(q.pos); if (!groups.ContainsKey(k)) groups[k] = new List<ObjectId>(); groups[k].Add(o.id); }
                    var extra = groups.Values.Where(g => g.Count > 1).SelectMany(g => g.Skip(1)).ToList();
                    if (extra.Count > 0) { EraseIds(doc, extra); log.Add("duplicate connectors erased: " + string.Join(",", extra.Select(x => x.Handle.ToString()))); }
                    madeConnectors = groups.Count;
                    log.Add("connectors made by ConnectorWrapper: " + madeConnectors + " of " + items.Count + " junctions");
                }
                if (stage == 2 && args.TryGetProperty("keep", out var kpj) && kpj.ValueKind == JsonValueKind.True)
                    return new { plan, stage = 2, kept = true, items = items.Select(i => i.Kind + ":" + i.Id.Handle).ToList(), note = "Stage 2 with keep: everything was LEFT in the drawing for inspection; delete it by hand afterwards.", log };
                if (stage == 2)
                {
                    string rb2 = RollbackSince(doc, dlm, added, before);
                    return new { plan, stage = 2, note = "Stage 2: parts and " + madeConnectors + " connectors were created and everything was rolled back (" + rb2 + ").", log };
                }
                var joints = new List<int>(new int[madeConnectors]);

                // optional colour (AutoCAD colour index; default 0 = leave it): setting the colour of Plant objects in this transaction is suspected of crashing Plant 3D (eNotOpenForWrite)
                int colorIdx = args.TryGetProperty("color", out var cj2) && cj2.ValueKind == JsonValueKind.Number ? cj2.GetInt32() : 0;
                if (colorIdx > 0)
                    using (doc.LockDocument())
                    using (var trc = doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (var it in items) { var ent = trc.GetObject(it.Id, OpenMode.ForWrite) as Entity; if (ent != null) ent.ColorIndex = colorIdx; }
                        trc.Commit();
                    }

                // nothing may stay open
                var open = new List<string>();
                foreach (var it in items)
                {
                    int links = ConnectionCount(doc, it.Id);
                    if (links < 2) open.Add(it.Id.Handle + " has " + links + " links");
                }
                if (open.Count > 0)
                {
                    var rep = new List<string>();
                    for (int i = 0; i < items.Count; i++)
                    {
                        var it = items[i];
                        rep.Add(i + ":" + it.Kind + " " + it.Id.Handle + " ports " + string.Join(",", PortsOf(doc, it.Id).Select(p => p.name + Fmt(p.pos))) + " links " +
                                string.Join(",", ConnList(doc, it.Id).Select(c => c.h1 + "." + c.p1 + "-" + c.h2 + "." + c.p2)));
                    }
                    throw new InvalidOperationException("Open ports remain: " + string.Join("; ", open) + " || " + string.Join(" || ", rep));
                }
                return new
                {
                    plan,
                    created = new { pipes = nPipes, bends = nBends, closing_bends = nU, welds = joints.Count, handles = items.Select(i => i.Id.Handle.ToString()).ToList(), collision_handles = items.Where(i => i.Hit).Select(i => i.Id.Handle.ToString()).ToList() },
                    log, note = "The tracing loop is closed (no inlet or outlet). Check it in the drawing."
                };
            }
            catch (Exception ex)
            {
                string rb = RollbackSince(doc, dlm, added, before);
                throw new InvalidOperationException("Tracing failed and was rolled back (" + rb + "): " + ex.Message + (log.Count > 0 ? " | " + string.Join(" | ", log.Take(6)) : ""));
            }
        }
    }
}
