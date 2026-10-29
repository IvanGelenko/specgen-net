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
using Part = Autodesk.ProcessPower.PnP3dObjects.Part;
using Port = Autodesk.ProcessPower.PnP3dObjects.Port;

namespace Plant3dMcp
{
    // create_branch: a branch pipe welded straight into a run pipe (no tee), a flange and a flanged valve on it.
    static partial class PlantTools
    {
        // Lookalike letters in catalog text: Cyrillic В/Е and Latin B/E mean the same "исполнение".
        static string NormVariant(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim().ToUpperInvariant();
            return s.Replace('В', 'B').Replace('Е', 'E').Replace('Ф', 'F');
        }

        static string ValveVariant(string desc)
        {
            var m = Regex.Match(desc ?? "", @"исп\.\s*([A-Za-zА-Яа-я])", RegexOptions.IgnoreCase);
            return m.Success ? NormVariant(m.Groups[1].Value) : "";
        }

        static string FlangeVariant(string desc)
        {
            var m = Regex.Match(desc ?? "", @"-1-([A-Za-zА-Яа-я])-", RegexOptions.IgnoreCase);
            return m.Success ? NormVariant(m.Groups[1].Value) : "";
        }

        static HashSet<int> ModelRows(Document doc, DataLinksManager dlm)
        {
            var rows = new HashSet<int>();
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForRead);
                foreach (ObjectId id in ms) { int r = RowId(dlm, id); if (r > 0) rows.Add(r); }
                tr.Commit();
            }
            return rows;
        }

        // A two-port inline part (flange, valve) placed along 'dir' with its first port on 'start'. The pose is measured from a
        // trial placement (ports are read back) and verified, exactly as for elbows. Returns the row, id and the far port.
        sealed class Inline { public int Row; public ObjectId Id; public Point3d PortA, PortB; public string Desc; }

        // End condition of a spec port ("BV", "FL", ...); null if unknown.
        static string EndCond(SpecPart p, string portName)
        {
            try { return p.Port(portName)?.EndCondition; } catch (Exception) { return null; }
        }

        // 'aEnd' decides which port goes on the start point by its spec end condition (a weld-neck flange must put its weld end on the pipe,
        // its hub pointing at the pipe). With null the two ports are ordered by geometry only (valves, which are symmetric).
        static Inline PlaceInline(Document doc, PipingObjectAdder adder, DataLinksManager dlm, SpecPart part, string label, Point3d start, Vector3d dir, List<string> log, HashSet<int> added, Func<string, bool> aEnd = null, string aPortName = null)
        {
            var reference = Math.Abs(dir.Z) < 0.9 ? Vector3d.ZAxis : Vector3d.XAxis;
            var z0 = dir.CrossProduct(reference).GetNormal();
            var y0 = z0.CrossProduct(dir).GetNormal();

            int AddAt(Point3d pos, Vector3d x, Vector3d zz)
            {
                var asset = new PipeInlineAsset();
                asset.SymbolId = ContentManager.GetContentManager().GetSymbol(part, doc.Database);
                asset.Position = pos;
                if (!asset.SetOrientation(x, zz)) throw new InvalidOperationException(label + ": SetOrientation rejected the axes");
                int row = adder.Add(part, asset);
                added.Add(row);
                return row;
            }

            // Orders two ports so that A's outward direction points away from B; returns false if the part does not look like a straight 2-port part.
            bool Pick(List<(string name, Point3d pos, Vector3d dir)> ports, out (string name, Point3d pos, Vector3d dir) a, out (string name, Point3d pos, Vector3d dir) b)
            {
                a = default; b = default;
                if (ports.Count < 2) return false;
                if (aPortName != null)   // e.g. the large end of a reducer, which must face the previous part
                {
                    var named = ports.Where(p => p.name == aPortName).ToList();
                    if (named.Count == 1)
                    {
                        var first = named[0];
                        a = first;
                        b = ports.Where(p => p.name != first.name).OrderByDescending(p => p.pos.DistanceTo(first.pos)).First();
                        return first.pos.DistanceTo(b.pos) > 1.0;
                    }
                }
                if (aEnd != null)
                {
                    var flagged = ports.Where(p => aEnd(EndCond(part, p.name))).ToList();
                    if (flagged.Count == 1)
                    {
                        var first = flagged[0];   // copy: an out parameter cannot be captured by a lambda
                        a = first;
                        b = ports.Where(p => p.name != first.name).OrderByDescending(p => p.pos.DistanceTo(first.pos)).First();
                        return first.pos.DistanceTo(b.pos) > 1.0;
                    }
                }
                double best = -1; int bi = 0, bj = 1;
                for (int i = 0; i < ports.Count; i++)
                    for (int j = i + 1; j < ports.Count; j++)
                    {
                        double d = ports[i].pos.DistanceTo(ports[j].pos);
                        if (d > best) { best = d; bi = i; bj = j; }
                    }
                a = ports[bi]; b = ports[bj];
                if (a.dir.DotProduct(b.pos - a.pos) > 0) { var t = a; a = b; b = t; }
                return best > 1.0;
            }

            int row1 = AddAt(start, dir, z0);
            var id1 = FindByRow(doc, dlm, row1);
            var ports1 = PortsOf(doc, id1);
            log.Add(label + " trial ports: " + string.Join(" ", ports1.Select(p => p.name + "[" + EndCond(part, p.name) + "]" + Fmt(p.pos) + "/" + Fmt(p.dir))));
            string described = ports1.Count < 2 ? DescribePart(doc, id1) : null;
            if (!Pick(ports1, out var A, out var B))
            {
                EraseIds(doc, new[] { id1 });
                throw new InvalidOperationException(label + " has no usable pair of ports (" + ports1.Count + " ports)" + (described != null ? ": " + described : ""));
            }
            double len = A.pos.DistanceTo(B.pos);

            bool Fits(Point3d a, Point3d b, Vector3d da, Vector3d db) =>
                Near(a, start) && (b - a).DotProduct(dir) > len - 0.5 && Aligned(da, dir.Negate()) && Aligned(db, dir);
            if (Fits(A.pos, B.pos, A.dir, B.dir))
                return new Inline { Row = row1, Id = id1, PortA = A.pos, PortB = B.pos };

            EraseIds(doc, new[] { id1 });

            // Local geometry measured in the trial frame; then the frame that puts A on 'start' and B along 'dir'.
            var toWorld = Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, start, dir, y0, z0);
            var toLocal = toWorld.Inverse();
            Point3d LA = A.pos.TransformBy(toLocal), LB = B.pos.TransformBy(toLocal);
            var e1 = (LB - LA).GetNormal();
            var refLocal = Math.Abs(e1.Z) < 0.9 ? Vector3d.ZAxis : Vector3d.XAxis;
            var e2 = (refLocal - e1 * refLocal.DotProduct(e1)).GetNormal();
            var e3 = e1.CrossProduct(e2);
            var f1 = dir; var f2 = y0; var f3 = f1.CrossProduct(f2);
            var rt = Matrix3d.AlignCoordinateSystem(LA, e1, e2, e3, start, f1, f2, f3);

            Point3d pos = Point3d.Origin.TransformBy(rt);
            Vector3d ax = Vector3d.XAxis.TransformBy(rt).GetNormal();
            Vector3d az = Vector3d.ZAxis.TransformBy(rt).GetNormal();
            int row2 = AddAt(pos, ax, az);
            var id2 = FindByRow(doc, dlm, row2);
            var ports2 = PortsOf(doc, id2);
            log.Add(label + " final ports: " + string.Join(" ", ports2.Select(p => p.name + Fmt(p.pos) + "/" + Fmt(p.dir))));
            if (!Pick(ports2, out var A2, out var B2) || !Fits(A2.pos, B2.pos, A2.dir, B2.dir))
            {
                EraseIds(doc, new[] { id2 });
                throw new InvalidOperationException(label + " could not be placed on the axis at " + Fmt(start) + "; " + string.Join(" | ", log));
            }
            return new Inline { Row = row2, Id = id2, PortA = A2.pos, PortB = B2.pos };
        }

        // A one-port part (blind flange) placed with its port on 'at' and its port direction pointing along 'outward' (toward the mating part).
        // Measured from a trial placement and verified, like the two-port parts.
        static Inline PlaceCap(Document doc, PipingObjectAdder adder, DataLinksManager dlm, SpecPart part, string label, Point3d at, Vector3d outward, Vector3d axis, List<string> log, HashSet<int> added)
        {
            var reference = Math.Abs(axis.Z) < 0.9 ? Vector3d.ZAxis : Vector3d.XAxis;
            var z0 = axis.CrossProduct(reference).GetNormal();
            var y0 = z0.CrossProduct(axis).GetNormal();

            int AddAt(Point3d pos, Vector3d x, Vector3d zz)
            {
                var asset = new PipeInlineAsset();
                asset.SymbolId = ContentManager.GetContentManager().GetSymbol(part, doc.Database);
                asset.Position = pos;
                if (!asset.SetOrientation(x, zz)) throw new InvalidOperationException(label + ": SetOrientation rejected the axes");
                int row = adder.Add(part, asset);
                added.Add(row);
                return row;
            }

            bool Fits(List<(string name, Point3d pos, Vector3d dir)> ports) =>
                ports.Count == 1 && Near(ports[0].pos, at) && Aligned(ports[0].dir, outward);

            int row1 = AddAt(at, axis, z0);
            var id1 = FindByRow(doc, dlm, row1);
            var ports1 = PortsOf(doc, id1);
            log.Add(label + " trial ports: " + string.Join(" ", ports1.Select(p => p.name + "[" + EndCond(part, p.name) + "]" + Fmt(p.pos) + "/" + Fmt(p.dir))));
            if (Fits(ports1)) return new Inline { Row = row1, Id = id1, PortA = ports1[0].pos, PortB = ports1[0].pos };

            string described = ports1.Count != 1 ? DescribePart(doc, id1) : null;
            EraseIds(doc, new[] { id1 });
            if (ports1.Count != 1) throw new InvalidOperationException(label + " has " + ports1.Count + " ports, expected 1: " + described);

            var toWorld = Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, at, axis, y0, z0);
            var toLocal = toWorld.Inverse();
            Point3d LA = ports1[0].pos.TransformBy(toLocal);
            Vector3d DL = ports1[0].dir.TransformBy(toLocal);
            if (DL.Length < 1e-9) throw new InvalidOperationException(label + ": the port has no direction");
            var e1 = DL.GetNormal();
            var refLocal = Math.Abs(e1.Z) < 0.9 ? Vector3d.ZAxis : Vector3d.XAxis;
            var e2 = (refLocal - e1 * refLocal.DotProduct(e1)).GetNormal();
            var e3 = e1.CrossProduct(e2);
            var f1 = outward.GetNormal();
            var ref2 = Math.Abs(f1.Z) < 0.9 ? Vector3d.ZAxis : Vector3d.XAxis;
            var f2 = (ref2 - f1 * ref2.DotProduct(f1)).GetNormal();
            var f3 = f1.CrossProduct(f2);
            var rt = Matrix3d.AlignCoordinateSystem(LA, e1, e2, e3, at, f1, f2, f3);

            Point3d pos = Point3d.Origin.TransformBy(rt);
            Vector3d ax = Vector3d.XAxis.TransformBy(rt).GetNormal();
            Vector3d az = Vector3d.ZAxis.TransformBy(rt).GetNormal();
            int row2 = AddAt(pos, ax, az);
            var id2 = FindByRow(doc, dlm, row2);
            var ports2 = PortsOf(doc, id2);
            log.Add(label + " final ports: " + string.Join(" ", ports2.Select(p => p.name + Fmt(p.pos) + "/" + Fmt(p.dir))));
            if (!Fits(ports2))
            {
                EraseIds(doc, new[] { id2 });
                throw new InvalidOperationException(label + " could not be placed with its port at " + Fmt(at) + " facing " + Fmt(outward) + "; " + string.Join(" | ", log));
            }
            return new Inline { Row = row2, Id = id2, PortA = ports2[0].pos, PortB = ports2[0].pos };
        }

        static string BlindVariant(string desc)
        {
            var m = Regex.Match(desc ?? "", @"-16-([A-Za-zА-Яа-я])-", RegexOptions.IgnoreCase);
            return m.Success ? NormVariant(m.Groups[1].Value) : "";
        }

        static SpecPart PickByVariant(List<(SpecPart part, double wall, string desc)> cands, string what, string variant, Func<string, string> variantOf, string mustContain)
        {
            var list = cands.Where(c => (mustContain == null || (c.desc ?? "").IndexOf(mustContain, StringComparison.OrdinalIgnoreCase) >= 0)
                                        && (variant == null || variantOf(c.desc) == variant)).ToList();
            if (list.Count != 1)
                throw new InvalidOperationException("Need exactly one " + what + (variant != null ? " of variant " + variant : "") + ", found " + list.Count + ". Available: " +
                    string.Join(" | ", cands.Select(c => c.desc)));
            return list[0].part;
        }

        static string DescOf(SpecPart p) => (Prop(p, "PartSizeLongDesc") ?? Prop(p, "PartFamilyLongDesc"))?.ToString();

        // Dynamic port on the run pipe at the point where the branch is welded in (needed to connect a pipe to the side of a pipe).
        static string AddDynamicPort(Document doc, ObjectId runId, Point3d at, Vector3d dir, ObjectId branchId, string endType = null)
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var run = (Part)tr.GetObject(runId, OpenMode.ForWrite);
                var branch = (Part)tr.GetObject(branchId, OpenMode.ForRead);
                var bports = branch.GetPorts(PortType.All);
                if (bports.Count == 0) throw new InvalidOperationException("The branch pipe has no ports");
                var bp = bports[0];
                for (int i = 1; i < bports.Count; i++) if (bports[i].Position.DistanceTo(at) < bp.Position.DistanceTo(at)) bp = bports[i];
                var port = new Port();
                port.Name = run.GenerateDynamicPortName(false);
                port.Position = at;
                port.Direction = dir;
                port.IsDynamic = true;
                port.NominalDiameter = bp.NominalDiameter;
                port.EndType = endType ?? bp.EndType;   // "TAP" for a branch welded into the side of a pipe (end conditions of the Tap joint)
                if (!run.AddPort(port)) throw new InvalidOperationException("The run pipe rejected the dynamic port");
                tr.Commit();
                return port.Name;
            }
        }

        static void RemoveDynamicPort(Document doc, ObjectId runId, string name)
        {
            if (name == null) return;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                ((Part)tr.GetObject(runId, OpenMode.ForWrite)).RemovePort(name);
                tr.Commit();
            }
        }

        public static object CreateBranch(JsonElement args)
        {
            var doc = ActiveDocument();
            var runId = IdFromHandle(doc.Database, args.GetProperty("run_handle").GetString());
            string specName = (args.TryGetProperty("spec", out var sp) && sp.ValueKind == JsonValueKind.String ? sp.GetString() : null) ?? DefaultSpec;
            double nd = args.TryGetProperty("nd", out var ndEl) && ndEl.ValueKind == JsonValueKind.Number ? ndEl.GetDouble() : 25;
            double dist = args.TryGetProperty("distance_from_high_end", out var de) && de.ValueKind == JsonValueKind.Number ? de.GetDouble() : 500;
            double stub = args.TryGetProperty("stub_length", out var se) && se.ValueKind == JsonValueKind.Number ? se.GetDouble() : 100;   // user rule: the pipe of vents and drains is 100 mm
            double? wall = args.TryGetProperty("wall", out var we) && we.ValueKind == JsonValueKind.Number ? we.GetDouble() : (double?)null;
            string variant = NormVariant(args.TryGetProperty("variant", out var ve) && ve.ValueKind == JsonValueKind.String ? ve.GetString() : "В");
            // Default branch axis: perpendicular to the run in its vertical plane, pointing up (as in the reference assembly).
            bool hasDir = args.TryGetProperty("direction", out var dirEl) && dirEl.ValueKind == JsonValueKind.Array;
            double[] dv = Vec(args, "direction", 0, 0, 1);
            var dir = new Vector3d(dv[0], dv[1], dv[2]);
            if (dir.Length < 1e-9) throw new ArgumentException("direction must be non-zero");
            dir = dir.GetNormal();
            if (stub < 50) throw new ArgumentException("stub_length must be at least 50 mm");
            bool relaxWeld = args.TryGetProperty("relax_weld_spacing", out var rws) && rws.ValueKind == JsonValueKind.True;

            // The run pipe: ends, direction, which end is highest.
            Point3d s, e; double runOd;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var run = tr.GetObject(runId, OpenMode.ForRead) as Pipe ?? throw new InvalidOperationException("run_handle is not a Plant 3D pipe");
                s = run.StartPoint; e = run.EndPoint; runOd = run.OuterDiameter;
                tr.Commit();
            }
            Point3d high = e.Z > s.Z + 1e-6 ? e : s;
            Point3d low = ReferenceEquals(high, e) || high == e ? s : e;
            if (high == s && Math.Abs(e.Z - s.Z) <= 1e-6) low = e;
            var runDir = (low - high).GetNormal();
            double runLen = high.DistanceTo(low);
            if (!hasDir)
            {
                var perp = Vector3d.ZAxis - runDir * Vector3d.ZAxis.DotProduct(runDir);
                dir = perp.Length < 1e-6 ? Vector3d.XAxis : perp.GetNormal();
            }
            if (Math.Abs(runDir.DotProduct(dir)) > 0.99) throw new ArgumentException("the branch direction is parallel to the run pipe");
            Point3d P = high + runDir * dist;   // on the run axis

            var piping = Piping();
            var sm = SpecManager.GetSpecManager();
            string key = LoadSpecKey(sm, piping, specName);

            var pipeSel = ChoosePipe(CollectParts(sm, specName, key, "Pipe", nd), wall, key, nd, out string wallRule);
            SpecPart pipePart = pipeSel[0].part;
            double od = OuterDiameter(pipePart);
            if (od >= runOd) throw new InvalidOperationException("The branch (OD " + od + ") must be smaller than the run (OD " + runOd + ")");

            // GOST 32569 cl. 6.8.4: clearance from the outer surface of the welded-in branch to the start of a bend or a transverse butt weld
            // (here the ends of the run pipe, which are welds or flanges).
            double clearance = DesignRules.BranchClearanceMin(od);
            double toHigh = dist - od / 2, toLow = runLen - dist - od / 2;
            if (toHigh < clearance || toLow < clearance)
                throw new ArgumentException("The branch is " + Math.Round(Math.Min(toHigh, toLow), 1) + " mm from the nearest end of the run pipe; GOST 32569 cl. 6.8.4 requires at least " + clearance +
                    " mm from the outer surface of the branch to a weld or bend. Use distance_from_high_end between " + Math.Round(clearance + od / 2, 1) + " and " + Math.Round(runLen - clearance - od / 2, 1) + " mm.");

            var notes = new List<string>();
            double weldMin = DesignRules.WeldSpacingMin(od, pipeSel[0].wall);
            if (stub < weldMin && !relaxWeld)
                throw new ArgumentException("stub_length " + stub + " mm is below the minimum " + Math.Round(weldMin, 1) + " mm between the branch weld and the flange weld (GOST 32569 cl. 6.8.1); the user rule for vents and drains is 100 mm. Pass relax_weld_spacing=true only for a justified case.");
            notes.Add("Pipe length of the vent: " + stub + " mm (user rule: 100 mm for vents and drains; GOST 32569 cl. 6.8.1 minimum between butt welds for OD<=219 is 100 mm).");
            double runNd = 0;
            try { var rp = Props(Dlm(), RowId(Dlm(), runId)); if (rp.TryGetValue("NominalDiameter", out var rn)) double.TryParse(rn, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out runNd); } catch (Exception) { }
            if (runNd > 0)
            {
                int minDn = DesignRules.VentDrainMinDn(runNd);
                if (nd < minDn) notes.Add("Branch DN" + nd + " is below the minimum DN" + minDn + " for a vent/drain on a DN" + runNd + " line (GOST 32569 table 10.3, cl. 10.2.7).");
                else notes.Add("Branch DN" + nd + " meets the minimum DN" + minDn + " for a vent/drain on a DN" + runNd + " line (table 10.3).");
            }
            if (Math.Abs(high.Z - low.Z) > 1e-6)
            {
                if (dist <= runLen / 2) notes.Add("Placed near the highest point of the run (cl. 10.2.1: vents at upper points).");
                else notes.Add("Placed near the lowest point of the run (cl. 10.2.1: drains at lower points).");
            }
            notes.Add("Gate valve: handwheel opens counter-clockwise, closes clockwise; open/closed position indicator required (cl. 8.7, 8.8). Do not use it for throttling (cl. 8.6).");
            notes.Add("Hand drive must be at most 1.6 m above the floor/working platform (cl. 10.3.13); the valve top will be about " + Math.Round(stub + 200, 0) + " mm above the run axis plus the valve height: check it against the platform level.");
            notes.Add("The DN" + nd + " branch is welded straight into the DN" + (runNd > 0 ? runNd.ToString() : "?") + " pipe without a tee (cl. 6.3.1, fig. 6.2a): allowed when the weakening is covered by the strength reserve; no stiffening ribs (cl. 6.3.1).");
            SpecPart flangePart = PickByVariant(CollectParts(sm, specName, key, "Flange", nd), "flange", variant, FlangeVariant, null);
            SpecPart valvePart = PickByVariant(CollectParts(sm, specName, key, "Valve", nd), "gate (wedge) valve", variant, ValveVariant, "Задвижка клиновая");
            // The open end of the valve must not stay open: a blind flange closes it.
            SpecPart blindPart = PickByVariant(CollectParts(sm, specName, key, "BlindFlange", nd), "blind flange", variant, BlindVariant, null);
            string gasketType = args.TryGetProperty("gasket_type", out var gt) && gt.ValueKind == JsonValueKind.String ? gt.GetString() : "Д";
            var hardware = PickFlangeHardware(sm, specName, key, nd, flangePart, gasketType);
            double gasketT = GasketThickness(hardware.gasket, 4.5);
            double boltLen = args.TryGetProperty("bolt_length", out var bl0) && bl0.ValueKind == JsonValueKind.Number ? bl0.GetDouble() : 115;   // copied from the reference assembly (DN25 PN16, M12x80)

            // The branch starts where the edge of its outer surface meets the run's surface (the Tap point of the reference assembly).
            double Rrun = runOd / 2, rBr = od / 2;
            double h0 = Math.Sqrt(Math.Max(0, Rrun * Rrun - rBr * rBr));
            Point3d T = P + dir * h0;

            var plan = new
            {
                design_notes = notes,
                run = new { handle = runId.Handle.ToString(), highEnd = Arr(high), lowEnd = Arr(low), length = Math.Round(runLen, 1) },
                run_axis_point = Arr(P), tap_point = Arr(T), tap_offset_from_run_axis = Math.Round(h0, 3), direction = Arr(dir), nd, variant,
                pipe = new { description = pipeSel[0].desc, wall = pipeSel[0].wall, wall_rule = wallRule ?? "only pipe of this size in the spec", length = stub },
                flange = DescOf(flangePart),
                valve = DescOf(valvePart),
                blind_flange = DescOf(blindPart),
                gasket = DescOf(hardware.gasket) + " (thickness " + gasketT + " mm)",
                bolt_set = DescOf(hardware.bolts) + " (drawn length " + boltLen + " mm)",
                joints = new[] { "Tap (run surface -> branch pipe)", "Buttweld (pipe -> flange)", "Flanged FL1-FL1 (RUS) (flange -> valve)", "Flanged FL1-FL1 (RUS) (valve -> blind flange)" },
                joint_to_run = "welded straight into the run pipe, Tap joint, no tee (user decision; the spec's branch table allows only a tee or reducer for 150/25)",
                note = "Joint connectors are built as in the user's reference assembly. Flange and valve lengths are measured when placed."
            };
            if (IsDry(args)) return new { dry_run = true, document = doc.Name, plan, note = "Nothing was changed. Repeat with dry_run=false to create the branch." };

            var dlm = Dlm();
            var added = new HashSet<int>();
            var log = new List<string>();
            string dynName = null;
            var before = ModelRows(doc, dlm);
            try
            {
                int rowPipe, rowTap, rowButt, rowFl1, rowFl2; Inline fl, va, bl;
                Point3d pipeEnd = T + dir * stub;
                using (doc.LockDocument())
                using (var adder = new PipingObjectAdder(piping.DataLinksManager3d, doc.Database))
                {
                    rowPipe = AddPipe(adder, pipePart, T, pipeEnd, od);
                    added.Add(rowPipe);
                    // Weld-neck flange: the weld end (any end condition except FL) goes on the pipe, the hub toward the pipe.
                    fl = PlaceInline(doc, adder, dlm, flangePart, "flange", pipeEnd, dir, log, added, ec => !string.Equals(ec, "FL", StringComparison.OrdinalIgnoreCase));
                    // A flanged joint leaves a gap for the gasket (its thickness), exactly as in the reference assembly.
                    Point3d V1 = fl.PortB + dir * gasketT;
                    va = PlaceInline(doc, adder, dlm, valvePart, "valve", V1, dir, log, added);
                    Point3d Bp = va.PortB + dir * gasketT;
                    bl = PlaceCap(doc, adder, dlm, blindPart, "blind flange", Bp, dir.Negate(), dir, log, added);

                    // Joint connectors (reference: vent-reference.json). Local X of a connector runs from port S1 toward port S2.
                    var runPerp = (runDir - dir * runDir.DotProduct(dir)).GetNormal();
                    const double tight = 0.00174532925199433, weldTol = 0.0523598775598299;   // 0.1 and 3 degrees, from the project's joint definitions
                    var tapWeld = MakeWeldProps("TapWeld", "Tapweld", nd, specName, log);
                    rowTap = AddConnector(adder, "Tap", T, dir, runPerp.Negate(), tight,
                        new List<(SubPart, PartSizeProperties)> { (new WeldSubPart(), tapWeld) }, log, added);
                    var buttWeld = MakeWeldProps("Buttweld", "Buttweld", nd, specName, log);
                    rowButt = AddConnector(adder, "Buttweld", pipeEnd, dir.Negate(), runPerp, weldTol,
                        new List<(SubPart, PartSizeProperties)> { (new WeldSubPart(), buttWeld) }, log, added);
                    List<(SubPart, PartSizeProperties)> Hardware()
                    {
                        var gs = new BlockSubPart(); gs.SymbolId = ContentManager.GetContentManager().GetSymbol(hardware.gasket, doc.Database);
                        var bs = new BoltSetSubPart(); bs.Length = boltLen; bs.Offset = 0;
                        return new List<(SubPart, PartSizeProperties)> { (gs, hardware.gasket), (bs, hardware.bolts) };
                    }
                    rowFl1 = AddConnector(adder, "Flanged FL1-FL1 (RUS)", V1, dir.Negate(), runPerp, tight, Hardware(), log, added);
                    rowFl2 = AddConnector(adder, "Flanged FL1-FL1 (RUS)", Bp, dir.Negate(), runPerp, tight, Hardware(), log, added);
                }
                var idPipe = FindByRow(doc, dlm, rowPipe);
                var idTap = FindByRow(doc, dlm, rowTap);
                var idButt = FindByRow(doc, dlm, rowButt);
                var idFl1 = FindByRow(doc, dlm, rowFl1);
                var idFl2 = FindByRow(doc, dlm, rowFl2);

                string Snap()
                {
                    string Line(string n, ObjectId id) { try { return n + " " + string.Join(" ", PortsOf(doc, id).Select(p => p.name + Fmt(p.pos))); } catch (Exception sx) { return n + " <" + sx.GetType().Name + ">"; } }
                    return Line("run", runId) + " | " + Line("branch pipe", idPipe) + " | " + Line("flange", fl.Id) + " | " + Line("valve", va.Id) + " | " + Line("blind flange", bl.Id);
                }
                log.Add("before dynamic port: " + Snap());
                dynName = AddDynamicPort(doc, runId, T, dir, idPipe, "TAP");
                log.Add("after dynamic port '" + dynName + "' at " + Fmt(T) + ": " + Snap());

                // Try every joint even if one fails, so the diagnostics show the whole picture; any failure rolls everything back.
                var joints = new List<object>();
                var failedJoints = new List<string>();
                void Join(string label, ObjectId a, string portA, ObjectId b)
                {
                    try { joints.Add(ConnectCore(doc, a, b, true, portA)); }
                    catch (Exception jx) { failedJoints.Add(label + ": " + jx.Message); }
                }
                // Parts are joined THROUGH their connectors: part - connector - part.
                Join("tap.S1<->run (dynamic port)", idTap, "S1", runId);
                Join("tap.S2<->branch pipe", idTap, "S2", idPipe);
                Join("buttweld.S1<->flange", idButt, "S1", fl.Id);
                Join("buttweld.S2<->branch pipe", idButt, "S2", idPipe);
                Join("flanged#1.S1<->valve", idFl1, "S1", va.Id);
                Join("flanged#1.S2<->flange", idFl1, "S2", fl.Id);
                Join("flanged#2.S1<->blind flange", idFl2, "S1", bl.Id);
                Join("flanged#2.S2<->valve", idFl2, "S2", va.Id);
                log.Add("after joints: " + Snap());
                if (failedJoints.Count > 0) throw new InvalidOperationException("joints failed: " + string.Join("; ", failedJoints));

                // Whatever the connection manager created on its own (gasket, bolts) belongs to this call too.
                var after = ModelRows(doc, dlm);
                var extras = after.Where(r => !before.Contains(r) && !added.Contains(r)).ToList();
                foreach (var r in extras) added.Add(r);

                // Plant 3D sometimes adds a Tap connector of its own at the same point (observed once: one with real weld data, "Tapweld").
                // Keep exactly one: the typed one if it is connected on both sides, otherwise ours.
                try
                {
                    var taps = new List<(int row, ObjectId id, int weldId, int cons)>();
                    foreach (var r in added.ToList())
                    {
                        Dictionary<string, string> pr; try { pr = Props(dlm, r); } catch (Exception) { continue; }
                        if (!pr.TryGetValue("JointType", out var jt) || jt != "Tap") continue;
                        var cid = FindByRow(doc, dlm, r);
                        taps.Add((r, cid, WeldPartId(doc, cid), ConnectionCount(doc, cid)));
                    }
                    if (taps.Count > 1)
                    {
                        log.Add("duplicate Tap connectors: " + string.Join(", ", taps.Select(t => t.id.Handle + "(weldPart=" + t.weldId + ", connections=" + t.cons + ")")));
                        var keep = taps.FirstOrDefault(t => t.weldId > 0 && t.cons >= 2);
                        if (keep.id.IsNull) keep = taps.First(t => t.row == rowTap);
                        var drop = taps.Where(t => t.row != keep.row).Select(t => t.row).ToList();
                        EraseRows(doc, dlm, drop);
                        foreach (var r in drop) added.Remove(r);
                        log.Add("kept Tap connector " + keep.id.Handle + ", erased " + drop.Count + " duplicate(s)");
                        // Erasing a connector also removes the dynamic port it was attached to (observed), which leaves the kept connector
                        // pointing at a port that no longer exists. Put the port back and join the kept connector to it again.
                        bool hasPort = PortsOf(doc, runId).Any(p => p.name.StartsWith("D") && p.pos.DistanceTo(T) < 1.0);
                        if (!hasPort)
                        {
                            dynName = AddDynamicPort(doc, runId, T, dir, idPipe, "TAP");
                            log.Add("dynamic port restored as '" + dynName + "' after erasing the duplicate");
                            joints.Add(ConnectCore(doc, keep.id, runId, true, "S1"));
                        }
                        var runCons = ReadConnections(doc, runId);
                        log.Add("run connections after the duplicate check: " + string.Join(" | ", runCons.Select(c => c.ToString())));
                    }
                }
                catch (Exception dx) { log.Add("duplicate check failed: " + dx.Message); }

                var created = new List<object>();
                foreach (var r in added.OrderBy(x => x))
                {
                    try
                    {
                        var id = FindByRow(doc, dlm, r);
                        var p = Props(dlm, r);
                        p.TryGetValue("PartSizeLongDesc", out var d);
                        created.Add(new { handle = id.Handle.ToString(), rowId = r, cls = id.ObjectClass.Name, plantClass = p.TryGetValue("PnPClassName", out var pc) ? pc : null, description = d, connections = ConnectionCount(doc, id) });
                    }
                    catch (Exception) { /* a trial row that was erased */ }
                }
                return new
                {
                    plan, created, joints, auto_created = extras.Count, dynamic_port = dynName,
                    top = Arr(va.PortB), height_above_run_axis = Math.Round((va.PortB - P).DotProduct(dir), 1), log,
                    note = "'auto_created' counts objects the connection manager added itself (gasket, bolts)."
                };
            }
            catch (Exception ex)
            {
                string rb = RollbackSince(doc, dlm, added, before);
                string pr = "";
                try { RemoveDynamicPort(doc, runId, dynName); if (dynName != null) pr = "; dynamic port removed"; } catch (Exception pex) { pr = "; DYNAMIC PORT " + dynName + " NOT removed: " + pex.Message; }
                throw new InvalidOperationException("Branch failed and was rolled back (" + rb + pr + "): " + ex.Message + (log.Count > 0 ? " | " + string.Join(" | ", log) : ""));
            }
        }
    }
}
