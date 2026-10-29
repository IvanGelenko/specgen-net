using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    // create_valve_bypass: a DN50 bypass around an EXISTING valve of a run (flange - valve - flange), joined to the run through two reducing tees.
    // New objects are created first and verified without touching the run; only then the two run pipes next to the valve are shortened.
    static partial class PlantTools
    {
        // Connections of an object as plain strings: (handle1, port1, handle2, port2).
        static List<(string h1, string p1, string h2, string p2)> ConnList(Document doc, ObjectId id)
        {
            var res = new List<(string, string, string, string)>();
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            using (var cm = new ConnectionManager())
            {
                var cons = cm.GetConnections(id);
                for (int i = 0; i < cons.Count; i++)
                {
                    var c = cons[i];
                    string H(ObjectId o) { try { return o.IsNull ? null : o.Handle.ToString(); } catch (Exception) { return null; } }
                    string N(Func<Port> g) { try { return g()?.Name; } catch (Exception) { return null; } }
                    res.Add((H(c.ObjectId1), N(() => c.Port1), H(c.ObjectId2), N(() => c.Port2)));
                }
                tr.Commit();
            }
            return res;
        }

        // The single neighbour of 'id' that is not 'exclude'. Returns its id and the port name on the NEIGHBOUR's side; ownPort is the port on id's side.
        static ObjectId OtherOf(Document doc, ObjectId id, ObjectId exclude, out string otherPort, out string ownPort)
        {
            string me = id.Handle.ToString(), ex = exclude.IsNull ? null : exclude.Handle.ToString();
            var found = new List<(string h, string theirPort, string myPort)>();
            foreach (var c in ConnList(doc, id))
            {
                bool first = c.h1 == me;
                string other = first ? c.h2 : c.h1;
                if (other == null || other == ex) continue;
                found.Add((other, first ? c.p2 : c.p1, first ? c.p1 : c.p2));
            }
            if (found.Count != 1) throw new InvalidOperationException("Expected exactly one other neighbour of " + me + ", found " + found.Count);
            otherPort = found[0].theirPort; ownPort = found[0].myPort;
            return IdFromHandle(doc.Database, found[0].h);
        }

        sealed class ValveSide { public ObjectId FlangedConn, Flange, WeldConn, Pipe; public string PipePort; public Point3d W; }

        // valve port -> flanged connector -> flange -> weld connector -> pipe
        static ValveSide WalkValveSide(Document doc, ObjectId valve, string valvePort)
        {
            var s = new ValveSide();
            string vh = valve.Handle.ToString();
            var link = ConnList(doc, valve).Where(c => (c.h1 == vh && c.p1 == valvePort) || (c.h2 == vh && c.p2 == valvePort)).ToList();
            if (link.Count != 1) throw new InvalidOperationException("Valve port " + valvePort + " has " + link.Count + " connections, expected 1");
            s.FlangedConn = IdFromHandle(doc.Database, link[0].h1 == vh ? link[0].h2 : link[0].h1);
            s.Flange = OtherOf(doc, s.FlangedConn, valve, out _, out _);
            s.WeldConn = OtherOf(doc, s.Flange, s.FlangedConn, out _, out _);
            s.Pipe = OtherOf(doc, s.WeldConn, s.Flange, out string pipePort, out _);
            s.PipePort = pipePort;
            s.W = PortsOf(doc, s.Pipe).First(p => p.name == pipePort).pos;
            return s;
        }

        static void SetPipeEnd(Document doc, ObjectId pipeId, bool start, Point3d p)
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var pipe = (Pipe)tr.GetObject(pipeId, OpenMode.ForWrite);
                if (start) pipe.StartPoint = p; else pipe.EndPoint = p;
                tr.Commit();
            }
        }

        static void Disconnect(Document doc, ObjectId a, ObjectId b)
        {
            using (doc.LockDocument())
            using (var cm = new ConnectionManager())
                cm.Disconnect(a, b);
        }

        public static object CreateValveBypass(JsonElement args)
        {
            var doc = ActiveDocument();
            var valveId = IdFromHandle(doc.Database, args.GetProperty("valve_handle").GetString());
            string specName = (args.TryGetProperty("spec", out var sp) && sp.ValueKind == JsonValueKind.String ? sp.GetString() : null) ?? DefaultSpec;
            double bypNd = args.TryGetProperty("bypass_nd", out var bnd) && bnd.ValueKind == JsonValueKind.Number ? bnd.GetDouble() : 50;
            double clearance = args.TryGetProperty("tee_clearance", out var tcl) && tcl.ValueKind == JsonValueKind.Number ? tcl.GetDouble() : 400;
            double Hreq = args.TryGetProperty("height", out var he) && he.ValueKind == JsonValueKind.Number ? he.GetDouble() : 0;
            string variant = NormVariant(args.TryGetProperty("variant", out var ve) && ve.ValueKind == JsonValueKind.String ? ve.GetString() : "В");
            string gasketType = args.TryGetProperty("gasket_type", out var gt) && gt.ValueKind == JsonValueKind.String ? gt.GetString() : "Д";
            double boltLen = args.TryGetProperty("bolt_length", out var bl0) && bl0.ValueKind == JsonValueKind.Number ? bl0.GetDouble() : 125;

            // ---- the existing assembly ---------------------------------------------------------------------
            var vPorts = PortsOf(doc, valveId);
            if (vPorts.Count != 2) throw new InvalidOperationException("The valve must have two ports, it has " + vPorts.Count);
            var p1v = vPorts.First(p => p.name == "S1").pos; var p2v = vPorts.First(p => p.name == "S2").pos;
            var r = (p2v - p1v).GetNormal();
            var u = (Vector3d.ZAxis - r * Vector3d.ZAxis.DotProduct(r)).GetNormal();
            var left = WalkValveSide(doc, valveId, "S1");
            var right = WalkValveSide(doc, valveId, "S2");
            Point3d W1 = left.W, W2 = right.W;
            if (((W2 - W1).CrossProduct(r)).Length > 1.0) throw new InvalidOperationException("The pipes on both sides of the valve are not on one axis");
            double sW2 = (W2 - W1).DotProduct(r);
            if (sW2 <= 0) throw new InvalidOperationException("Unexpected order of the pipes along the valve axis");
            Point3d A(double s) => W1 + r * s;

            double runOd, runWall, runNd, pipeLeftFar, pipeRightFar;
            Point3d leftFar, rightFar;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var pl = (Pipe)tr.GetObject(left.Pipe, OpenMode.ForRead);
                var pr = (Pipe)tr.GetObject(right.Pipe, OpenMode.ForRead);
                runOd = pl.OuterDiameter;
                if (Math.Abs(pr.OuterDiameter - pl.OuterDiameter) > 0.5)
                    throw new InvalidOperationException("The pipes on the two sides of the valve have different outer diameters (" + Math.Round(pl.OuterDiameter, 1) + " and " + Math.Round(pr.OuterDiameter, 1) + " mm); the bypass tees are chosen for one size. Not supported.");
                leftFar = left.PipePort == "S2" ? pl.StartPoint : pl.EndPoint;
                rightFar = right.PipePort == "S1" ? pr.EndPoint : pr.StartPoint;
                tr.Commit();
            }
            pipeLeftFar = (W1 - leftFar).DotProduct(r); pipeRightFar = (rightFar - W2).DotProduct(r);
            var dlm = Dlm();
            var lp = Props(dlm, RowId(dlm, left.Pipe));
            runWall = double.Parse(lp["WallThickness"], System.Globalization.CultureInfo.InvariantCulture);
            runNd = double.Parse(lp["NominalDiameter"], System.Globalization.CultureInfo.InvariantCulture);

            // ---- part selection (same rules as create_bypass) -------------------------------------------------
            var piping = Piping();
            var sm = SpecManager.GetSpecManager();
            string key = LoadSpecKey(sm, piping, specName);
            var runSel = ChoosePipe(CollectParts(sm, specName, key, "Pipe", runNd), runWall, key, runNd, out _);
            SpecPart runPart = runSel[0].part;
            var bySel = ChoosePipe(CollectParts(sm, specName, key, "Pipe", bypNd), null, key, bypNd, out string byWallRule);
            SpecPart byPart = bySel[0].part; double byWall = bySel[0].wall, byOd = OuterDiameter(byPart);

            var teeCands = CollectParts(sm, specName, key, "Tee", runNd).Where(c => (c.desc ?? "").IndexOf("переходной", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var reducerCands = new List<(SpecPart part, double wall, string desc)>();
            foreach (double dn in new[] { 65.0, 80, 100, 125 })
                try { reducerCands.AddRange(CollectParts(sm, specName, key, "Reducer", dn)); } catch (Exception) { }
            (SpecPart tee, string teeDesc, SpecPart red, string redDesc)? chosen = null;
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
                    double score = mWall * 1000 + bWall * 100 + bigWall * 10 + smWall;
                    if (score < bestScore) { bestScore = score; chosen = (tc.part, tc.desc, rc.part, rc.desc); }
                }
            }
            if (chosen == null) throw new InvalidOperationException("No reducing tee for the DN" + runNd + " run (wall >= " + runWall + ") with a reducer to OD " + byOd + " in the spec");
            var ch = chosen.Value;
            var elbowSel = CollectParts(sm, specName, key, "Elbow", bypNd).Where(c => Math.Abs(ElbowAngle(c.part, c.desc) - 90) < 0.05 && Math.Abs(c.wall - byWall) < 1e-3 && OdMatches(c.part, byOd)).ToList();
            if (elbowSel.Count != 1) throw new InvalidOperationException("Need exactly one 90-degree DN" + bypNd + " elbow with wall " + byWall + ", found " + elbowSel.Count);
            SpecPart elbowPart = elbowSel[0].part;
            double elbowR = ElbowRadius(elbowPart), te = elbowR;
            SpecPart flangePart = PickByVariant(CollectParts(sm, specName, key, "Flange", bypNd), "flange", variant, FlangeVariant, null);
            SpecPart valvePart = PickByVariant(CollectParts(sm, specName, key, "Valve", bypNd), "gate (wedge) valve", variant, ValveVariant, "Задвижка клиновая");
            var hardware = PickFlangeHardware(sm, specName, key, bypNd, flangePart, gasketType);
            double gasketT = GasketThickness(hardware.gasket, 4.5);
            string redLarge = LargePortName(ch.red);

            // loop height: clear the existing valve (its body and handwheel) by 300 mm unless given
            double valveTop = 0;
            try
            {
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ext = ((Entity)tr.GetObject(valveId, OpenMode.ForRead)).GeometricExtents;
                    foreach (var x in new[] { ext.MinPoint.X, ext.MaxPoint.X }) foreach (var y in new[] { ext.MinPoint.Y, ext.MaxPoint.Y }) foreach (var z in new[] { ext.MinPoint.Z, ext.MaxPoint.Z })
                        valveTop = Math.Max(valveTop, (new Point3d(x, y, z) - W1).DotProduct(u));
                    tr.Commit();
                }
            }
            catch (Exception) { valveTop = 0; }
            double H = Hreq > 0 ? Hreq : Math.Max(600, valveTop + 300);

            var notes = new List<string>
            {
                "Bypass around the existing DN" + runNd + " valve " + valveId.Handle + ": two reducing tees " + ch.teeDesc + " (wall >= pipe wall), reducers " + ch.redDesc + ".",
                "The run pipes " + left.Pipe.Handle + " and " + right.Pipe.Handle + " are shortened next to the valve, short new pipes of " + clearance + " mm reach the existing flanges, and the connectors of those two weld joints are moved to the new pipes.",
                "Loop height above the run axis " + Math.Round(H, 0) + " mm (the existing valve reaches " + Math.Round(valveTop, 0) + " mm).",
                "The top of the bypass loop is a high point: GOST 32569 cl. 10.2.1 requires a vent there (not created by this tool).",
                "Bolt length " + boltLen + " mm is an estimate for DN" + bypNd + " (proportion of the DN25 reference); check it."
            };
            var plan = new
            {
                design_notes = notes,
                existing = new
                {
                    valve = valveId.Handle.ToString(), axis_direction = Arr(r),
                    left = new { pipe = left.Pipe.Handle.ToString(), flange = left.Flange.Handle.ToString(), weld_point = Arr(W1), pipe_length_before_the_weld = Math.Round(pipeLeftFar, 1) },
                    right = new { pipe = right.Pipe.Handle.ToString(), flange = right.Flange.Handle.ToString(), weld_point = Arr(W2), pipe_length_after_the_weld = Math.Round(pipeRightFar, 1) }
                },
                parts = new { tee = ch.teeDesc, reducer = ch.redDesc, pipe = bySel[0].desc, elbow = DescOf(elbowPart), flange = DescOf(flangePart), valve = DescOf(valvePart), gasket = DescOf(hardware.gasket), bolt_set = DescOf(hardware.bolts) },
                geometry = new { tee_clearance_to_existing_flange_weld = clearance, loop_height = Math.Round(H, 0) },
                will_modify = new[] { left.Pipe.Handle.ToString() + " (end moved to tee 1)", right.Pipe.Handle.ToString() + " (start moved to tee 2)", left.WeldConn.Handle.ToString() + " and " + right.WeldConn.Handle.ToString() + " (re-attached to the new short pipes)" }
            };
            if (IsDry(args)) return new { dry_run = true, document = doc.Name, plan, note = "Nothing was changed. SAVE the drawing first: this operation modifies two existing pipes and two connectors. Repeat with dry_run=false to create the bypass." };

            // ---- creation: everything new first, nothing existing touched -------------------------------------
            var before = ModelRows(doc, dlm);
            var added = new HashSet<int>();
            var log = new List<string>();
            var undo = new Stack<Action>();
            try
            {
                var rowOf = new Dictionary<string, int>();
                var weldRows = new List<(int row, string prevKey, string nextKey, string label)>();
                var flangedRows = new List<(int row, string prevKey, string nextKey, string label)>();
                int rowCa, rowCd;
                Inline red1, red2, flL, va, flR; TeeInfo tee1, tee2;
                double La, Lh;

                using (doc.LockDocument())
                using (var adder = new PipingObjectAdder(piping.DataLinksManager3d, doc.Database))
                {
                    double tf = MeasureInline(doc, adder, dlm, flangePart, "flange", log, added, ec => !string.Equals(ec, "FL", StringComparison.OrdinalIgnoreCase));
                    double Lv = MeasureInline(doc, adder, dlm, valvePart, "valve", log, added);
                    // measure a tee far away, then place the real ones so that their ports are 'clearance' from the existing flange welds
                    var probe = PlaceTee(doc, adder, dlm, ch.tee, "tee (measure)", new Point3d(0, 0, -60000), r, u, log, added);
                    double halfUp = probe.HalfUp, halfDown = probe.HalfDown;
                    EraseIds(doc, new[] { probe.Id }); added.Remove(probe.Row);

                    double s1 = -(clearance + halfDown), s2 = sW2 + clearance + halfUp;
                    double minRun = DesignRules.WeldSpacingMin(runOd, runWall);
                    if (pipeLeftFar + s1 - halfUp < minRun) throw new InvalidOperationException("The left pipe would be only " + Math.Round(pipeLeftFar + s1 - halfUp, 1) + " mm long after the tee; at least " + Math.Round(minRun, 1) + " mm is needed");
                    if (pipeRightFar - (s2 + halfDown - sW2) < minRun) throw new InvalidOperationException("The right pipe would be too short after the tee");
                    if (clearance < minRun) throw new InvalidOperationException("tee_clearance " + clearance + " mm is below the minimum " + Math.Round(minRun, 1) + " mm between welds (GOST 32569 cl. 6.8.1)");

                    tee1 = PlaceTee(doc, adder, dlm, ch.tee, "tee 1", A(s1), r, u, log, added);
                    tee2 = PlaceTee(doc, adder, dlm, ch.tee, "tee 2", A(s2), r, u, log, added);
                    rowOf["tee 1"] = tee1.Row; rowOf["tee 2"] = tee2.Row;

                    int AddP(string label, SpecPart part, Point3d a, Point3d b, double od, double wallMm)
                    {
                        double len = a.DistanceTo(b);
                        double need = DesignRules.WeldSpacingMin(od, wallMm);
                        if (len < need) throw new InvalidOperationException(label + " would be " + Math.Round(len, 1) + " mm long; GOST 32569 cl. 6.8.1 needs at least " + Math.Round(need, 1) + " mm between butt welds");
                        int row = AddPipe(adder, part, a, b, od); added.Add(row); rowOf[label] = row; return row;
                    }
                    // short new run pipes from the tees to the existing flange welds
                    AddP("new pipe 1", runPart, tee1.PortDown, W1, runOd, runWall);
                    AddP("new pipe 2", runPart, W2, tee2.PortUp, runOd, runWall);

                    red1 = PlaceInline(doc, adder, dlm, ch.red, "reducer 1", tee1.PortBranch, u, log, added, null, redLarge);
                    red2 = PlaceInline(doc, adder, dlm, ch.red, "reducer 2", tee2.PortBranch, u, log, added, null, redLarge);
                    Point3d V1 = A(s1) + u * H, V2 = A(s2) + u * H;
                    var e1 = new ElbowPlan { Vertex = V1, Din = u, Dout = r, Angle = 90, Radius = elbowR, Tangent = te, Part = elbowPart, Desc = DescOf(elbowPart), TIn = V1 - u * te, TOut = V1 + r * te };
                    var e2 = new ElbowPlan { Vertex = V2, Din = r, Dout = u.Negate(), Angle = 90, Radius = elbowR, Tangent = te, Part = elbowPart, Desc = DescOf(elbowPart), TIn = V2 - r * te, TOut = V2 - u * te };
                    rowOf["elbow 1"] = PlaceElbow(doc, adder, dlm, e1, log, added);
                    rowOf["elbow 2"] = PlaceElbow(doc, adder, dlm, e2, log, added);
                    AddP("leg 1", byPart, red1.PortB, e1.TIn, byOd, byWall);
                    AddP("leg 2", byPart, e2.TOut, red2.PortB, byOd, byWall);

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
                    rowOf["reducer 1"] = red1.Row; rowOf["reducer 2"] = red2.Row; rowOf["flange L"] = flL.Row; rowOf["valve"] = va.Row; rowOf["flange R"] = flR.Row;

                    var tol = 0.0523598775598299; var tight = 0.00174532925199433;
                    int Weld(Point3d at, Vector3d travel, double nd)
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
                    // run side: Ca joins the shortened left pipe to tee 1 (connected after the trim), Cd joins tee 2 to the shortened right pipe
                    rowCa = Weld(tee1.PortUp, r, runNd);
                    weldRows.Add((Weld(tee1.PortDown, r, runNd), "tee 1", "new pipe 1", "tee 1|new pipe 1"));
                    weldRows.Add((Weld(tee2.PortUp, r, runNd), "new pipe 2", "tee 2", "new pipe 2|tee 2"));
                    rowCd = Weld(tee2.PortDown, r, runNd);
                    // bypass loop
                    weldRows.Add((Weld(tee1.PortBranch, u, 100), "tee 1", "reducer 1", "tee 1|reducer 1"));
                    weldRows.Add((Weld(red1.PortB, u, bypNd), "reducer 1", "leg 1", "reducer 1|leg 1"));
                    weldRows.Add((Weld(e1.TIn, u, bypNd), "leg 1", "elbow 1", "leg 1|elbow 1"));
                    weldRows.Add((Weld(e1.TOut, r, bypNd), "elbow 1", "pipe A", "elbow 1|pipe A"));
                    weldRows.Add((Weld(pAend, r, bypNd), "pipe A", "flange L", "pipe A|flange L"));
                    flangedRows.Add((Flanged(valveStart, r), "flange L", "valve", "flange L|valve"));
                    flangedRows.Add((Flanged(flRStart, r), "valve", "flange R", "valve|flange R"));
                    weldRows.Add((Weld(flR.PortB, r, bypNd), "flange R", "pipe B", "flange R|pipe B"));
                    weldRows.Add((Weld(e2.TIn, r, bypNd), "pipe B", "elbow 2", "pipe B|elbow 2"));
                    weldRows.Add((Weld(e2.TOut, u.Negate(), bypNd), "elbow 2", "leg 2", "elbow 2|leg 2"));
                    weldRows.Add((Weld(red2.PortB, u.Negate(), bypNd), "leg 2", "reducer 2", "leg 2|reducer 2"));
                    weldRows.Add((Weld(tee2.PortBranch, u.Negate(), 100), "reducer 2", "tee 2", "reducer 2|tee 2"));
                }

                var idOf = new Dictionary<string, ObjectId>();
                foreach (var kv in rowOf) idOf[kv.Key] = FindByRow(doc, dlm, kv.Value);
                var failed = new List<string>();
                int links = 0;
                void JoinAll(List<(int row, string prevKey, string nextKey, string label)> list)
                {
                    foreach (var j in list)
                    {
                        var cid = FindByRow(doc, dlm, j.row);
                        try { ConnectCore(doc, cid, idOf[j.nextKey], true, "S1"); links++; } catch (Exception ex) { failed.Add(j.label + " (next): " + ex.Message); }
                        try { ConnectCore(doc, cid, idOf[j.prevKey], true, "S2"); links++; } catch (Exception ex) { failed.Add(j.label + " (prev): " + ex.Message); }
                    }
                }
                JoinAll(weldRows); JoinAll(flangedRows);
                var idCa = FindByRow(doc, dlm, rowCa); var idCd = FindByRow(doc, dlm, rowCd);
                try { ConnectCore(doc, idCa, idOf["tee 1"], true, "S1"); links++; } catch (Exception ex) { failed.Add("Ca (tee 1): " + ex.Message); }
                try { ConnectCore(doc, idCd, idOf["tee 2"], true, "S2"); links++; } catch (Exception ex) { failed.Add("Cd (tee 2): " + ex.Message); }
                if (failed.Count > 0) throw new InvalidOperationException("new joints failed: " + string.Join("; ", failed));
                log.Add("all new objects created and joined (" + links + " links); now modifying the existing run");

                // ---- modification of the existing run (undo stack on failure) -----------------------------------
                Point3d newLeftEnd = tee1.PortUp;        // upstream run port of tee 1
                Point3d newRightStart = tee2.PortDown;   // downstream run port of tee 2
                bool leftIsEnd = left.PipePort == "S2", rightIsStart = right.PipePort == "S1";
                Point3d oldLeft = W1, oldRight = W2;

                Disconnect(doc, left.Pipe, left.WeldConn);
                undo.Push(() => ConnectCore(doc, left.WeldConn, left.Pipe, true, "S2"));
                SetPipeEnd(doc, left.Pipe, !leftIsEnd, newLeftEnd);
                undo.Pop(); undo.Push(() => { SetPipeEnd(doc, left.Pipe, !leftIsEnd, oldLeft); ConnectCore(doc, left.WeldConn, left.Pipe, true, "S2"); });
                ConnectCore(doc, left.WeldConn, idOf["new pipe 1"], true, "S2");
                undo.Push(() => Disconnect(doc, left.WeldConn, idOf["new pipe 1"]));
                ConnectCore(doc, idCa, left.Pipe, true, "S2");
                undo.Push(() => Disconnect(doc, idCa, left.Pipe));

                Disconnect(doc, right.Pipe, right.WeldConn);
                undo.Push(() => ConnectCore(doc, right.WeldConn, right.Pipe, true, "S2"));
                SetPipeEnd(doc, right.Pipe, rightIsStart, newRightStart);
                undo.Pop(); undo.Push(() => { SetPipeEnd(doc, right.Pipe, rightIsStart, oldRight); ConnectCore(doc, right.WeldConn, right.Pipe, true, "S2"); });
                ConnectCore(doc, right.WeldConn, idOf["new pipe 2"], true, "S2");
                undo.Push(() => Disconnect(doc, right.WeldConn, idOf["new pipe 2"]));
                ConnectCore(doc, idCd, right.Pipe, true, "S1");
                undo.Push(() => Disconnect(doc, idCd, right.Pipe));

                // verification of the modified run
                var checks = new List<string>();
                void Expect(string what, bool ok) { if (!ok) checks.Add(what); }
                Expect("left pipe end moved to tee 1", PortsOf(doc, left.Pipe).Any(p => p.pos.DistanceTo(newLeftEnd) < 0.5));
                Expect("right pipe start moved to tee 2", PortsOf(doc, right.Pipe).Any(p => p.pos.DistanceTo(newRightStart) < 0.5));
                Expect("old weld connector (left) now on new pipe 1", ConnList(doc, left.WeldConn).Any(c => c.h1 == idOf["new pipe 1"].Handle.ToString() || c.h2 == idOf["new pipe 1"].Handle.ToString()));
                Expect("old weld connector (right) now on new pipe 2", ConnList(doc, right.WeldConn).Any(c => c.h1 == idOf["new pipe 2"].Handle.ToString() || c.h2 == idOf["new pipe 2"].Handle.ToString()));
                if (checks.Count > 0) throw new InvalidOperationException("verification of the modified run failed: " + string.Join("; ", checks));

                // Plant 3D may have created parts/connectors of its own where the tees touch the existing pipes: remove the duplicates, restore links.
                List<string> cleanup;
                try { cleanup = DedupeAndRepair(doc, dlm, added); }
                catch (Exception cx) { cleanup = new List<string> { "cleanup failed: " + cx.Message }; }
                log.AddRange(cleanup);

                var created = new List<object>();
                foreach (var r0 in added.OrderBy(x => x))
                {
                    try
                    {
                        var id = FindByRow(doc, dlm, r0);
                        var p = Props(dlm, r0);
                        p.TryGetValue("PartSizeLongDesc", out var d); p.TryGetValue("JointType", out var jt);
                        created.Add(new { handle = id.Handle.ToString(), kind = id.ObjectClass.Name.Replace("AcPpDb3d", ""), joint = jt, description = d, connections = ConnectionCount(doc, id) });
                    }
                    catch (Exception) { }
                }
                var strays = ModelRows(doc, dlm).Where(x => !before.Contains(x) && !added.Contains(x)).ToList();
                return new
                {
                    plan, created, modified = new { left_pipe = left.Pipe.Handle.ToString(), right_pipe = right.Pipe.Handle.ToString(), moved_connectors = new[] { left.WeldConn.Handle.ToString(), right.WeldConn.Handle.ToString() } },
                    unexpected_objects = strays.Count == 0 ? null : strays, log,
                    note = "Existing run pipes were shortened and their weld connectors moved to the new short pipes. 'unexpected_objects' lists data rows Plant 3D created on its own."
                };
            }
            catch (Exception ex)
            {
                // 1. undo what was done to the existing run, newest first
                var undoLog = new List<string>();
                while (undo.Count > 0)
                {
                    try { undo.Pop()(); }
                    catch (Exception ux) { undoLog.Add("UNDO STEP FAILED: " + ux.Message); }
                }
                // 2. erase everything new
                string rb = RollbackSince(doc, dlm, added, before);
                throw new InvalidOperationException("Valve bypass failed; the existing run was restored" + (undoLog.Count > 0 ? " EXCEPT: " + string.Join("; ", undoLog) + " - reload the saved drawing" : "") +
                    " and the new objects were rolled back (" + rb + "): " + ex.Message + (log.Count > 0 ? " | " + string.Join(" | ", log) : ""));
            }
        }
    }
}
