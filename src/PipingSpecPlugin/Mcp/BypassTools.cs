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
    // create_bypass: a new DN150 run with two reducing tees and a DN50 bypass loop (reducer, pipe, elbow, pipe, flange, gate valve, flange,
    // pipe, elbow, pipe, reducer). Every joint gets a connector like in the reference assembly.
    static partial class PlantTools
    {
        sealed class TeeInfo { public int Row; public ObjectId Id; public Point3d Center, PortUp, PortDown, PortBranch; public double Hb, HalfUp, HalfDown; }

        // "159x6.0-108x5.0": main/big OD x wall, branch/small OD x wall.
        static bool TwoSizes(string desc, out double od1, out double w1, out double od2, out double w2)
        {
            od1 = w1 = od2 = w2 = 0;
            var m = Regex.Match(desc ?? "", @"(\d+(?:[.,]\d+)?)\s*[xх×]\s*(\d+(?:[.,]\d+)?)\s*-\s*(\d+(?:[.,]\d+)?)\s*[xх×]\s*(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            double P(int g) => double.Parse(m.Groups[g].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
            od1 = P(1); w1 = P(2); od2 = P(3); w2 = P(4);
            return true;
        }

        static Vector3d Perp(Vector3d v)
        {
            var t = Math.Abs(v.Z) < 0.9 ? Vector3d.ZAxis : Vector3d.XAxis;
            return v.CrossProduct(t).GetNormal();
        }

        // The port of a spec part with the largest nominal diameter (the large end of a reducer).
        static string LargePortName(SpecPart p)
        {
            string best = null; double bv = -1;
            foreach (string n in p.PortNames)
            {
                double v = 0;
                try { v = p.Port(n).NominalDiameter.Value; } catch (Exception) { }
                if (v > bv) { bv = v; best = n; }
            }
            return best;
        }

        // Length of a two-port part, measured by placing it far away and erasing it again.
        static double MeasureInline(Document doc, PipingObjectAdder adder, DataLinksManager dlm, SpecPart part, string label, List<string> log, HashSet<int> added,
                                    Func<string, bool> aEnd = null, string aPortName = null)
        {
            var m = PlaceInline(doc, adder, dlm, part, label + " (measure)", new Point3d(0, 0, -50000), Vector3d.XAxis, log, added, aEnd, aPortName);
            double len = m.PortA.DistanceTo(m.PortB);
            EraseIds(doc, new[] { m.Id });
            added.Remove(m.Row);
            return len;
        }

        static bool TeeFits(List<(string name, Point3d pos, Vector3d dir)> ports, Point3d center, Vector3d r, Vector3d u, Vector3d z0, out TeeInfo ti)
        {
            ti = null;
            if (ports.Count != 3) return false;
            for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                {
                    if (ports[i].dir.GetNormal().DotProduct(ports[j].dir.GetNormal()) > -0.99) continue;
                    int k = 3 - i - j;
                    var a = ports[i]; var b = ports[j]; var c = ports[k];
                    if ((a.pos - center).DotProduct(r) > (b.pos - center).DotProduct(r)) { var t = a; a = b; b = t; }   // a = upstream
                    bool onAxis(Point3d p) => Math.Abs((p - center).DotProduct(u)) < 0.5 && Math.Abs((p - center).DotProduct(z0)) < 0.5;
                    var cv = c.pos - center;
                    if (!onAxis(a.pos) || !onAxis(b.pos)) continue;
                    if (Math.Abs(cv.DotProduct(r)) > 0.5 || Math.Abs(cv.DotProduct(z0)) > 0.5 || cv.DotProduct(u) <= 0) continue;
                    if (!Aligned(c.dir, u) || !Aligned(a.dir, r.Negate()) || !Aligned(b.dir, r)) continue;
                    ti = new TeeInfo { Center = center, PortUp = a.pos, PortDown = b.pos, PortBranch = c.pos, Hb = cv.DotProduct(u), HalfUp = -(a.pos - center).DotProduct(r), HalfDown = (b.pos - center).DotProduct(r) };
                    return true;
                }
            return false;
        }

        // Places a three-port tee with its run ports on the run axis (direction r) and its branch pointing along u, centred on 'center'.
        static TeeInfo PlaceTee(Document doc, PipingObjectAdder adder, DataLinksManager dlm, SpecPart tee, string label, Point3d center, Vector3d r, Vector3d u, List<string> log, HashSet<int> added)
        {
            var z0 = r.CrossProduct(u).GetNormal();
            int AddAt(Point3d pos, Vector3d x, Vector3d zz)
            {
                var asset = new PipeInlineAsset();
                asset.SymbolId = ContentManager.GetContentManager().GetSymbol(tee, doc.Database);
                asset.Position = pos;
                if (!asset.SetOrientation(x, zz)) throw new InvalidOperationException(label + ": SetOrientation rejected the axes");
                int row = adder.Add(tee, asset);
                added.Add(row);
                return row;
            }

            int row1 = AddAt(center, r, z0);
            var id1 = FindByRow(doc, dlm, row1);
            var ports1 = PortsOf(doc, id1);
            log.Add(label + " trial ports: " + string.Join(" ", ports1.Select(p => p.name + "[" + EndCond(tee, p.name) + "]" + Fmt(p.pos) + "/" + Fmt(p.dir))));
            if (TeeFits(ports1, center, r, u, z0, out var ti)) { ti.Row = row1; ti.Id = id1; return ti; }

            string described = ports1.Count != 3 ? DescribePart(doc, id1) : null;
            EraseIds(doc, new[] { id1 });
            if (ports1.Count != 3) throw new InvalidOperationException(label + " has " + ports1.Count + " ports, expected 3: " + described);

            // Local geometry in the trial frame (origin = center, X = r, Y = u, Z = r x u).
            var toWorld = Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, center, r, u, z0);
            var toLocal = toWorld.Inverse();
            int ra = -1, rb = -1, rc = -1;
            for (int i = 0; i < 3 && ra < 0; i++)
                for (int j = i + 1; j < 3 && ra < 0; j++)
                    if (ports1[i].dir.GetNormal().DotProduct(ports1[j].dir.GetNormal()) < -0.99) { ra = i; rb = j; rc = 3 - i - j; }
            if (ra < 0) throw new InvalidOperationException(label + ": no pair of opposite run ports among " + string.Join(" ", ports1.Select(p => Fmt(p.dir))));
            Point3d LA = ports1[ra].pos.TransformBy(toLocal), LB = ports1[rb].pos.TransformBy(toLocal), LC = ports1[rc].pos.TransformBy(toLocal);
            Vector3d DC = ports1[rc].dir.TransformBy(toLocal);
            var e1 = (LB - LA).GetNormal();
            var e2 = (DC - e1 * DC.DotProduct(e1)).GetNormal();
            var e3 = e1.CrossProduct(e2);
            Point3d Lc = LA + e1 * (LC - LA).DotProduct(e1);   // foot of the branch on the run line
            var rt = Matrix3d.AlignCoordinateSystem(Lc, e1, e2, e3, center, r, u, z0);

            Point3d pos = Point3d.Origin.TransformBy(rt);
            Vector3d ax = Vector3d.XAxis.TransformBy(rt).GetNormal();
            Vector3d az = Vector3d.ZAxis.TransformBy(rt).GetNormal();
            int row2 = AddAt(pos, ax, az);
            var id2 = FindByRow(doc, dlm, row2);
            var ports2 = PortsOf(doc, id2);
            log.Add(label + " final ports: " + string.Join(" ", ports2.Select(p => p.name + Fmt(p.pos) + "/" + Fmt(p.dir))));
            if (!TeeFits(ports2, center, r, u, z0, out ti))
            {
                EraseIds(doc, new[] { id2 });
                throw new InvalidOperationException(label + " could not be placed on the run axis with its branch along the bypass axis; " + string.Join(" | ", log));
            }
            ti.Row = row2; ti.Id = id2;
            return ti;
        }

        sealed class JointPlan { public int Row; public ObjectId Prev, Next; public string Label; }

        public static object CreateBypass(JsonElement args)
        {
            var doc = ActiveDocument();
            string specName = (args.TryGetProperty("spec", out var sp) && sp.ValueKind == JsonValueKind.String ? sp.GetString() : null) ?? DefaultSpec;
            double runNd = args.TryGetProperty("nd", out var rnd) && rnd.ValueKind == JsonValueKind.Number ? rnd.GetDouble() : 150;
            double bypNd = args.TryGetProperty("bypass_nd", out var bnd) && bnd.ValueKind == JsonValueKind.Number ? bnd.GetDouble() : 50;
            double runLen = args.TryGetProperty("length", out var le) && le.ValueKind == JsonValueKind.Number ? le.GetDouble() : 10000;
            double slope = args.TryGetProperty("slope", out var sl) && sl.ValueKind == JsonValueKind.Number ? sl.GetDouble() : 0.002;
            double s1 = args.TryGetProperty("tee1", out var t1) && t1.ValueKind == JsonValueKind.Number ? t1.GetDouble() : 2000;
            double s2 = args.TryGetProperty("tee2", out var t2) && t2.ValueKind == JsonValueKind.Number ? t2.GetDouble() : 8000;
            double H = args.TryGetProperty("height", out var he) && he.ValueKind == JsonValueKind.Number ? he.GetDouble() : 600;
            string variant = NormVariant(args.TryGetProperty("variant", out var ve) && ve.ValueKind == JsonValueKind.String ? ve.GetString() : "В");
            string gasketType = args.TryGetProperty("gasket_type", out var gt) && gt.ValueKind == JsonValueKind.String ? gt.GetString() : "Д";
            double boltLen = args.TryGetProperty("bolt_length", out var bl0) && bl0.ValueKind == JsonValueKind.Number ? bl0.GetDouble() : 115;
            double[] st = Vec(args, "start", 0, -11000, 0);
            var start = new Point3d(st[0], st[1], st[2]);
            if (s1 <= 0 || s2 <= s1 || s2 >= runLen) throw new ArgumentException("need 0 < tee1 < tee2 < length");

            // Run axis r (sloped down along +X) and the bypass axis u (perpendicular to it, up).
            var r = new Vector3d(1, 0, -slope).GetNormal();
            var u = (Vector3d.ZAxis - r * Vector3d.ZAxis.DotProduct(r)).GetNormal();
            Point3d A(double s) => start + r * s;

            var piping = Piping();
            var sm = SpecManager.GetSpecManager();
            string key = LoadSpecKey(sm, piping, specName);

            // ---- part selection -------------------------------------------------------------------------------
            var runSel = ChoosePipe(CollectParts(sm, specName, key, "Pipe", runNd), null, key, runNd, out string runWallRule);
            SpecPart runPart = runSel[0].part; double runWall = runSel[0].wall, runOd = OuterDiameter(runPart);
            var bySel = ChoosePipe(CollectParts(sm, specName, key, "Pipe", bypNd), null, key, bypNd, out string byWallRule);
            SpecPart byPart = bySel[0].part; double byWall = bySel[0].wall, byOd = OuterDiameter(byPart);

            // Reducing tee: main OD = run OD, main wall >= run wall; its branch must have a reducer down to the bypass pipe whose
            // big-end wall >= the tee branch wall and small-end wall >= the bypass pipe wall (user rule: fittings are never thinner than the pipe).
            var teeCands = CollectParts(sm, specName, key, "Tee", runNd).Where(c => (c.desc ?? "").IndexOf("переходной", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var reducerCands = new List<(SpecPart part, double wall, string desc)>();
            foreach (double dn in new[] { 65.0, 80, 100, 125 })
                try { reducerCands.AddRange(CollectParts(sm, specName, key, "Reducer", dn)); } catch (Exception) { /* no reducers of this size */ }
            (SpecPart tee, string teeDesc, double brOd, double brWall, SpecPart red, string redDesc)? chosen = null;
            double bestScore = double.MaxValue;
            foreach (var tc in teeCands)
            {
                if (!TwoSizes(tc.desc, out double mOd, out double mWall, out double bOd, out double bWall)) continue;
                if (Math.Abs(mOd - runOd) > 0.5 || mWall + 1e-3 < runWall) continue;
                foreach (var rc in reducerCands)
                {
                    if (!TwoSizes(rc.desc, out double bigOd, out double bigWall, out double smOd, out double smWall)) continue;
                    if (Math.Abs(bigOd - bOd) > 0.5 || Math.Abs(smOd - byOd) > 0.5) continue;
                    if (bigWall + 1e-3 < bWall || smWall + 1e-3 < byWall) continue;
                    double score = mWall * 1000 + bWall * 100 + bigWall * 10 + smWall;   // thinnest allowed wall first
                    if (score < bestScore) { bestScore = score; chosen = (tc.part, tc.desc, bOd, bWall, rc.part, rc.desc); }
                }
            }
            if (chosen == null)
                throw new InvalidOperationException("No reducing tee for the DN" + runNd + " run (wall >= " + runWall + ") with a reducer to OD " + byOd + " (wall >= " + byWall + ") in the spec. Tees: " +
                    string.Join(" | ", teeCands.Select(c => c.desc)));
            var ch = chosen.Value;

            var elbowSel = CollectParts(sm, specName, key, "Elbow", bypNd).Where(c => Math.Abs(ElbowAngle(c.part, c.desc) - 90) < 0.05 && Math.Abs(c.wall - byWall) < 1e-3 && OdMatches(c.part, byOd)).ToList();
            if (elbowSel.Count != 1) throw new InvalidOperationException("Need exactly one 90-degree DN" + bypNd + " elbow with wall " + byWall + ", found " + elbowSel.Count);
            SpecPart elbowPart = elbowSel[0].part;
            double elbowR = ElbowRadius(elbowPart), te = elbowR;   // tangent of a 90-degree elbow = its radius

            SpecPart flangePart = PickByVariant(CollectParts(sm, specName, key, "Flange", bypNd), "flange", variant, FlangeVariant, null);
            SpecPart valvePart = PickByVariant(CollectParts(sm, specName, key, "Valve", bypNd), "gate (wedge) valve", variant, ValveVariant, "Задвижка клиновая");
            var hardware = PickFlangeHardware(sm, specName, key, bypNd, flangePart, gasketType);
            double gasketT = GasketThickness(hardware.gasket, 4.5);
            string redLarge = LargePortName(ch.red);

            // ---- rule checks and plan -------------------------------------------------------------------------
            double minVert = DesignRules.WeldSpacingMin(byOd, byWall);
            var notes = new List<string>
            {
                "Tee " + ch.teeDesc + ": main wall >= run wall " + runWall + " mm and reducer " + ch.redDesc + " wall >= the tee branch and the DN" + bypNd + " pipe (user rule: tees and reducers are never thinner than the connected pipe).",
                "Branches by tee + reducer (the spec's branch table symbol 'T' for 150/" + bypNd + "), as the user asked for the bypass.",
                "The top of the bypass loop is a high point of the line: GOST 32569 cl. 10.2.1 requires a vent there (DN" + DesignRules.VentDrainMinDn(bypNd) + " minimum, table 10.3); not created by this tool.",
                "Valve: handwheel opens counter-clockwise (cl. 8.7); a bypass valve is for closing the bypass, not for throttling (cl. 8.6).",
                "Run slope " + Math.Round(slope * 1000, 3) + " mm/m (" + (slope >= DesignRules.MinSlope - 1e-9 ? "meets" : "below") + " the 2 mm/m minimum of cl. 10.1.4); drains/vents of the run are separate (create_branch)."
            };
            var plan = new
            {
                design_notes = notes,
                run = new { start = Arr(start), end = Arr(A(runLen)), length = runLen, nd = runNd, wall = runWall, wall_rule = runWallRule ?? "single", pipe = runSel[0].desc },
                tees = new { positions_from_start = new[] { s1, s2 }, part = ch.teeDesc },
                bypass = new
                {
                    nd = bypNd, loop_height_above_run_axis = H, pipe = bySel[0].desc, wall = byWall, wall_rule = byWallRule ?? "single",
                    reducer = ch.redDesc, elbow = DescOf(elbowPart) + " (R " + elbowR + ")", flange = DescOf(flangePart), valve = DescOf(valvePart),
                    gasket = DescOf(hardware.gasket) + " (" + gasketT + " mm)", bolt_set = DescOf(hardware.bolts) + " (" + boltLen + " mm)",
                    min_pipe_between_welds = Math.Round(minVert, 1)
                },
                joints = "Buttweld connector at every weld joint; Flanged FL1-FL1 (RUS) connector (gasket + bolts) on both sides of the valve"
            };
            if (IsDry(args)) return new { dry_run = true, document = doc.Name, plan, note = "Nothing was changed. Lengths of the flanges and the valve are measured when placed; repeat with dry_run=false to create the bypass." };

            // ---- creation -------------------------------------------------------------------------------------
            var dlm = Dlm();
            var before = ModelRows(doc, dlm);
            var added = new HashSet<int>();
            var log = new List<string>();
            var joints = new List<JointPlan>();
            var parts = new List<(string label, int row, ObjectId id)>();
            try
            {
                var rowOf = new Dictionary<string, int>();
                var weldRows = new List<(int row, string prevKey, string nextKey, string label)>();
                var flangedRows = new List<(int row, string prevKey, string nextKey, string label)>();
                Inline red1, red2, flL, va, flR; TeeInfo tee1, tee2;
                double La, Lh;
                Point3d V1 = A(s1) + u * H, V2 = A(s2) + u * H;

                using (doc.LockDocument())
                using (var adder = new PipingObjectAdder(piping.DataLinksManager3d, doc.Database))
                {
                    // measure the flange and the valve first: the lengths decide the pipes beside them
                    double tf = MeasureInline(doc, adder, dlm, flangePart, "flange", log, added, ec => !string.Equals(ec, "FL", StringComparison.OrdinalIgnoreCase));
                    double Lv = MeasureInline(doc, adder, dlm, valvePart, "valve", log, added);

                    tee1 = PlaceTee(doc, adder, dlm, ch.tee, "tee 1", A(s1), r, u, log, added);
                    tee2 = PlaceTee(doc, adder, dlm, ch.tee, "tee 2", A(s2), r, u, log, added);
                    parts.Add(("tee 1", tee1.Row, tee1.Id)); parts.Add(("tee 2", tee2.Row, tee2.Id));

                    int AddP(string label, SpecPart part, Point3d a, Point3d b, double od, double wallMm)
                    {
                        double len = a.DistanceTo(b);
                        double need = DesignRules.WeldSpacingMin(od, wallMm);
                        if (len < need) throw new InvalidOperationException(label + " would be " + Math.Round(len, 1) + " mm long; GOST 32569 cl. 6.8.1 needs at least " + Math.Round(need, 1) + " mm between butt welds");
                        int row = AddPipe(adder, part, a, b, od); added.Add(row); rowOf[label] = row; return row;
                    }

                    // run pipes
                    AddP("run 0", runPart, A(0), tee1.PortUp, runOd, runWall);
                    AddP("run 1", runPart, tee1.PortDown, tee2.PortUp, runOd, runWall);
                    AddP("run 2", runPart, tee2.PortDown, A(runLen), runOd, runWall);

                    // reducers: large end on the tee
                    red1 = PlaceInline(doc, adder, dlm, ch.red, "reducer 1", tee1.PortBranch, u, log, added, null, redLarge);
                    red2 = PlaceInline(doc, adder, dlm, ch.red, "reducer 2", tee2.PortBranch, u, log, added, null, redLarge);

                    // elbows at the loop corners
                    var e1 = new ElbowPlan { Vertex = V1, Din = u, Dout = r, Angle = 90, Radius = elbowR, Tangent = te, Part = elbowPart, Desc = DescOf(elbowPart), TIn = V1 - u * te, TOut = V1 + r * te };
                    var e2 = new ElbowPlan { Vertex = V2, Din = r, Dout = u.Negate(), Angle = 90, Radius = elbowR, Tangent = te, Part = elbowPart, Desc = DescOf(elbowPart), TIn = V2 - r * te, TOut = V2 - u * te };
                    int rowE1 = PlaceElbow(doc, adder, dlm, e1, log, added);
                    int rowE2 = PlaceElbow(doc, adder, dlm, e2, log, added);
                    rowOf["elbow 1"] = rowE1; rowOf["elbow 2"] = rowE2;

                    // vertical legs
                    AddP("leg 1", byPart, red1.PortB, e1.TIn, byOd, byWall);
                    AddP("leg 2", byPart, e2.TOut, red2.PortB, byOd, byWall);

                    // horizontal chain: pipe, flange, valve, flange, pipe (symmetric)
                    Lh = e1.TOut.DistanceTo(e2.TIn);
                    La = (Lh - 2 * tf - 2 * gasketT - Lv) / 2;
                    Point3d pAend = e1.TOut + r * La;
                    AddP("pipe A", byPart, e1.TOut, pAend, byOd, byWall);
                    flL = PlaceInline(doc, adder, dlm, flangePart, "flange L", pAend, r, log, added, ec => !string.Equals(ec, "FL", StringComparison.OrdinalIgnoreCase));
                    Point3d valveStart = flL.PortB + r * gasketT;
                    va = PlaceInline(doc, adder, dlm, valvePart, "valve", valveStart, r, log, added);
                    Point3d flRStart = va.PortB + r * gasketT;
                    flR = PlaceInline(doc, adder, dlm, flangePart, "flange R", flRStart, r, log, added, ec => string.Equals(ec, "FL", StringComparison.OrdinalIgnoreCase));
                    AddP("pipe B", byPart, flR.PortB, e2.TIn, byOd, byWall);

                    // joint connectors
                    var tol = 0.0523598775598299; var tight = 0.00174532925199433;
                    int Weld(Point3d at, Vector3d travel, double nd, string label)
                    {
                        var wp = MakeWeldProps("Buttweld", "Buttweld", nd, specName, log);
                        return AddConnector(adder, "Buttweld", at, travel.Negate(), Perp(travel), tol, new List<(SubPart, PartSizeProperties)> { (new WeldSubPart(), wp) }, log, added);
                    }
                    int Flanged(Point3d atNext, Vector3d travel)
                    {
                        var gs = new BlockSubPart(); gs.SymbolId = ContentManager.GetContentManager().GetSymbol(hardware.gasket, doc.Database);
                        var bs = new BoltSetSubPart(); bs.Length = boltLen; bs.Offset = 0;
                        return AddConnector(adder, "Flanged FL1-FL1 (RUS)", atNext, travel.Negate(), Perp(travel), tight,
                            new List<(SubPart, PartSizeProperties)> { (gs, hardware.gasket), (bs, hardware.bolts) }, log, added);
                    }
                    weldRows.Add((Weld(tee1.PortUp, r, runNd, "run 0|tee 1"), "run 0", "tee 1", "run 0|tee 1"));
                    weldRows.Add((Weld(tee1.PortDown, r, runNd, "tee 1|run 1"), "tee 1", "run 1", "tee 1|run 1"));
                    weldRows.Add((Weld(tee2.PortUp, r, runNd, "run 1|tee 2"), "run 1", "tee 2", "run 1|tee 2"));
                    weldRows.Add((Weld(tee2.PortDown, r, runNd, "tee 2|run 2"), "tee 2", "run 2", "tee 2|run 2"));
                    weldRows.Add((Weld(tee1.PortBranch, u, 100, "tee 1|reducer 1"), "tee 1", "reducer 1", "tee 1|reducer 1"));
                    weldRows.Add((Weld(red1.PortB, u, bypNd, "reducer 1|leg 1"), "reducer 1", "leg 1", "reducer 1|leg 1"));
                    weldRows.Add((Weld(e1.TIn, u, bypNd, "leg 1|elbow 1"), "leg 1", "elbow 1", "leg 1|elbow 1"));
                    weldRows.Add((Weld(e1.TOut, r, bypNd, "elbow 1|pipe A"), "elbow 1", "pipe A", "elbow 1|pipe A"));
                    weldRows.Add((Weld(pAend, r, bypNd, "pipe A|flange L"), "pipe A", "flange L", "pipe A|flange L"));
                    flangedRows.Add((Flanged(valveStart, r), "flange L", "valve", "flange L|valve"));
                    flangedRows.Add((Flanged(flRStart, r), "valve", "flange R", "valve|flange R"));
                    weldRows.Add((Weld(flR.PortB, r, bypNd, "flange R|pipe B"), "flange R", "pipe B", "flange R|pipe B"));
                    weldRows.Add((Weld(e2.TIn, r, bypNd, "pipe B|elbow 2"), "pipe B", "elbow 2", "pipe B|elbow 2"));
                    weldRows.Add((Weld(e2.TOut, u.Negate(), bypNd, "elbow 2|leg 2"), "elbow 2", "leg 2", "elbow 2|leg 2"));
                    weldRows.Add((Weld(red2.PortB, u.Negate(), bypNd, "leg 2|reducer 2"), "leg 2", "reducer 2", "leg 2|reducer 2"));
                    weldRows.Add((Weld(tee2.PortBranch, u.Negate(), 100, "reducer 2|tee 2"), "reducer 2", "tee 2", "reducer 2|tee 2"));
                    rowOf["reducer 1"] = red1.Row; rowOf["reducer 2"] = red2.Row; rowOf["flange L"] = flL.Row; rowOf["valve"] = va.Row; rowOf["flange R"] = flR.Row;
                    rowOf["tee 1"] = tee1.Row; rowOf["tee 2"] = tee2.Row;
                }

                // resolve ids, then join every part to its connectors: part - connector - part
                var idOf = new Dictionary<string, ObjectId>();
                foreach (var kv in rowOf) idOf[kv.Key] = FindByRow(doc, dlm, kv.Value);
                var failed = new List<string>();
                var joined = new List<object>();
                void JoinAll(List<(int row, string prevKey, string nextKey, string label)> list)
                {
                    foreach (var j in list)
                    {
                        var cid = FindByRow(doc, dlm, j.row);
                        try { joined.Add(ConnectCore(doc, cid, idOf[j.nextKey], true, "S1")); } catch (Exception ex) { failed.Add(j.label + " (next): " + ex.Message); }
                        try { joined.Add(ConnectCore(doc, cid, idOf[j.prevKey], true, "S2")); } catch (Exception ex) { failed.Add(j.label + " (prev): " + ex.Message); }
                    }
                }
                JoinAll(weldRows); JoinAll(flangedRows);
                if (failed.Count > 0) throw new InvalidOperationException("joints failed: " + string.Join("; ", failed));

                var created = new List<object>();
                foreach (var r0 in added.OrderBy(x => x))
                {
                    try
                    {
                        var id = FindByRow(doc, dlm, r0);
                        var p = Props(dlm, r0);
                        p.TryGetValue("PartSizeLongDesc", out var d);
                        p.TryGetValue("JointType", out var jt);
                        created.Add(new { handle = id.Handle.ToString(), rowId = r0, kind = id.ObjectClass.Name.Replace("AcPpDb3d", ""), joint = jt, description = d, connections = ConnectionCount(doc, id) });
                    }
                    catch (Exception) { /* a measuring part that was erased */ }
                }
                var strays = ModelRows(doc, dlm).Where(x => !before.Contains(x) && !added.Contains(x)).ToList();
                return new
                {
                    plan, created, joints_connected = joined.Count, unexpected_objects = strays.Count == 0 ? null : strays,
                    measured = new { horizontal_chain = Math.Round(Lh, 1), pipe_each_side_of_the_valve = Math.Round(La, 1), tee_branch_height = Math.Round(tee1.Hb, 2), tee_run_half_length = Math.Round(tee1.HalfUp, 2) },
                    log,
                    note = "Every part should show 2 connections (tees 3, the two free ends of the run 1). 'unexpected_objects' lists data rows Plant 3D created on its own."
                };
            }
            catch (Exception ex)
            {
                string rb = RollbackSince(doc, dlm, added, before);
                throw new InvalidOperationException("Bypass failed and was rolled back (" + rb + "): " + ex.Message + (log.Count > 0 ? " | " + string.Join(" | ", log) : ""));
            }
        }
    }
}
