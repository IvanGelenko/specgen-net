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
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;
using Part = Autodesk.ProcessPower.PnP3dObjects.Part;

namespace Plant3dMcp
{
    // add_supports: КП supports (OST 36-146-88) on pipes: next to valves on both sides first, then at a fixed step;
    // at least 100 mm from the axis of a weld to the edge of the support (GOST 32569 cl. 10.4.3, 11.2.6).
    // Under every support the sample sub-structure is built: plate, 12П post, floor plate (Plant 3D commands PLANTSTEELPLATE / PLANTSTEELMEMBER,
    // there is no managed API for structure objects).
    static partial class PlantTools
    {
        sealed class SPipe
        {
            public ObjectId Id; public string H; public Point3d A, B; public Vector3d D; public double L, Od, Nd;
            public string Material; public string ValveA, ValveB; public List<double> Inner = new List<double>();
        }

        sealed class SItem { public SPipe Pipe; public double T; public Point3d P; public bool Anchor; public string Why; }

        static readonly Regex ValveRx = new Regex(@"Задвижк|Клапан|Затвор|Кран|Вентиль|Valve", RegexOptions.IgnoreCase);
        static readonly Regex FlangeRx = new Regex(@"Фланец|Flange", RegexOptions.IgnoreCase);

        static string DescById(Document doc, DataLinksManager dlm, ObjectId id)
        {
            int r = RowId(dlm, id);
            if (r <= 0) return "";
            var p = Props(dlm, r);
            return (p.TryGetValue("PartFamilyLongDesc", out var d) ? d : "") + " " + (p.TryGetValue("PartSizeLongDesc", out var s) ? s : "");
        }

        // Walks from a pipe end through connectors and flanges; returns the valve handle if the first real part behind them is a valve.
        static string ValveAtEnd(Document doc, DataLinksManager dlm, ObjectId pipe, string port)
        {
            try
            {
                string ph = pipe.Handle.ToString();
                var link = ConnList(doc, pipe).Where(c => (c.h1 == ph && c.p1 == port) || (c.h2 == ph && c.p2 == port)).ToList();
                if (link.Count != 1) return null;
                ObjectId prev = pipe, cur = IdFromHandle(doc.Database, link[0].h1 == ph ? link[0].h2 : link[0].h1);
                for (int i = 0; i < 6; i++)
                {
                    if (!cur.ObjectClass.Name.Contains("Connector"))
                    {
                        string d = DescById(doc, dlm, cur);
                        if (ValveRx.IsMatch(d)) return cur.Handle.ToString();
                        if (!FlangeRx.IsMatch(d)) return null;
                    }
                    var next = OtherOf(doc, cur, prev, out _, out _);
                    prev = cur; cur = next;
                }
            }
            catch (Exception) { }
            return null;
        }

        static SPipe ReadSPipe(Document doc, DataLinksManager dlm, ObjectId id, List<(ObjectId id, Point3d pos)> weldPoints)
        {
            var sp = new SPipe { Id = id, H = id.Handle.ToString() };
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var pipe = tr.GetObject(id, OpenMode.ForRead) as Pipe ?? throw new InvalidOperationException("Object " + id.Handle + " is not a Plant 3D pipe");
                sp.A = pipe.StartPoint; sp.B = pipe.EndPoint; sp.Od = pipe.OuterDiameter;
                tr.Commit();
            }
            sp.L = sp.A.DistanceTo(sp.B);
            sp.D = (sp.B - sp.A).GetNormal();
            var props = Props(dlm, RowId(dlm, id));
            props.TryGetValue("Material", out sp.Material);
            if (props.TryGetValue("NominalDiameter", out var nd)) double.TryParse(nd, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sp.Nd);
            foreach (var w in weldPoints)
            {
                var v = w.pos - sp.A; double t = v.DotProduct(sp.D);
                double perp = (v - sp.D * t).Length;
                if (perp <= sp.Od / 2 + 3 && t > 1 && t < sp.L - 1) sp.Inner.Add(t);
            }
            sp.ValveA = ValveAtEnd(doc, dlm, id, "S1");
            sp.ValveB = ValveAtEnd(doc, dlm, id, "S2");
            return sp;
        }

        static Point3d PartPosition(Document doc, ObjectId id)
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var p = ((Part)tr.GetObject(id, OpenMode.ForRead)).Position;
                tr.Commit();
                return p;
            }
        }

        static List<SItem> PlanSupports(List<SPipe> pipes, List<Point3d> existing, double hw, double margin, double step, List<string> warnings)
        {
            var res = new List<SItem>();
            double clear = margin + hw;
            foreach (var sp in pipes)
            {
                double lo = clear, hi = sp.L - clear;
                if (lo > hi + 1e-6)
                {
                    warnings.Add("Pipe " + sp.H + " (" + Math.Round(sp.L, 1) + " mm) is too short for a support: the edge must be " + margin + " mm from a weld on both sides (needs at least " + Math.Round(2 * clear, 1) + " mm)." +
                                 (sp.ValveA != null || sp.ValveB != null ? " It is next to a valve: put the support on a neighbouring pipe or lengthen this one." : ""));
                    continue;
                }
                var anchors = new List<double>();
                if (sp.ValveA != null) anchors.Add(lo);
                if (sp.ValveB != null) anchors.Add(hi);
                bool hasValve = anchors.Count > 0;
                if (!hasValve) anchors.Add(lo);

                var ts = new SortedDictionary<double, bool>();   // t -> anchor
                foreach (double a in anchors)
                {
                    ts[Math.Round(a, 3)] = hasValve;
                    for (double t = a + step; t <= hi + 1e-6; t += step) if (!ts.ContainsKey(Math.Round(t, 3))) ts[Math.Round(t, 3)] = false;
                    for (double t = a - step; t >= lo - 1e-6; t -= step) if (!ts.ContainsKey(Math.Round(t, 3))) ts[Math.Round(t, 3)] = false;
                }
                // Drop step points that crowd an anchor (closer than half a step).
                var keep = ts.Where(kv => kv.Value || !ts.Any(o => o.Value && Math.Abs(o.Key - kv.Key) < step / 2)).ToList();

                foreach (var kv in keep)
                {
                    double t = kv.Key;
                    foreach (double s in sp.Inner)
                        if (Math.Abs(t - s) < clear - 1e-6)
                        {
                            double c1 = s - clear, c2 = s + clear;
                            double alt = Math.Abs(c1 - t) <= Math.Abs(c2 - t) ? c1 : c2;
                            if (alt < lo || alt > hi) alt = alt == c1 ? c2 : c1;
                            if (alt < lo || alt > hi || sp.Inner.Any(o => Math.Abs(alt - o) < clear - 1e-6)) { warnings.Add("Pipe " + sp.H + ": a support at " + Math.Round(t, 0) + " mm falls on a branch weld at " + Math.Round(s, 0) + " mm and has no free place nearby; skipped."); t = double.NaN; break; }
                            warnings.Add("Pipe " + sp.H + ": support moved from " + Math.Round(t, 0) + " to " + Math.Round(alt, 0) + " mm to keep " + margin + " mm from the branch weld at " + Math.Round(s, 0) + " mm.");
                            t = alt;
                        }
                    if (double.IsNaN(t)) continue;
                    var p = sp.A + sp.D * t;
                    if (existing.Any(e => (e - p).DotProduct(sp.D) is double al && Math.Abs(al) < 300 && ((e - p) - sp.D * al).Length < sp.Od)) { warnings.Add("Pipe " + sp.H + ": a support already stands at " + Math.Round(t, 0) + " mm; skipped."); continue; }
                    res.Add(new SItem { Pipe = sp, T = t, P = p, Anchor = kv.Value, Why = kv.Value ? "valve " + (Math.Abs(t - lo) < 1e-3 ? sp.ValveA : sp.ValveB) : "step" });
                }
            }
            // On one straight line the pipes are planned separately; a step support that crowds its neighbours is dropped
            // when the remaining gap between those neighbours still does not exceed the step.
            if (pipes.Count > 1 && pipes.All(p => p.D.CrossProduct(pipes[0].D).Length < 1e-3 && (p.A - pipes[0].A).CrossProduct(pipes[0].D).Length < Math.Max(p.Od, 1)))
            {
                var d0 = pipes[0].D;
                var sorted = res.OrderBy(i => (i.P - pipes[0].A).DotProduct(d0)).ToList();
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        if (sorted[i].Anchor || i == 0 || i == sorted.Count - 1) continue;
                        double gap = (sorted[i + 1].P - sorted[i - 1].P).DotProduct(d0);
                        double crowd = Math.Min((sorted[i].P - sorted[i - 1].P).DotProduct(d0), (sorted[i + 1].P - sorted[i].P).DotProduct(d0));
                        if (gap <= step && crowd < step / 2) { sorted.RemoveAt(i); changed = true; break; }
                    }
                }
                // Span rule across pipes: no span between neighbouring supports may exceed the step; add supports where it does.
                for (int i = 0; i + 1 < sorted.Count; i++)
                {
                    double gap = (sorted[i + 1].P - sorted[i].P).DotProduct(d0);
                    if (gap <= step + 1e-6) continue;
                    int n = (int)Math.Ceiling(gap / step) - 1;
                    for (int k = 1; k <= n; k++)
                    {
                        var target = sorted[i].P + d0 * (gap * k / (n + 1));
                        var host = pipes.FirstOrDefault(p => { double t = (target - p.A).DotProduct(p.D); return t >= clear - 1e-6 && t <= p.L - clear + 1e-6 && p.Inner.All(s => Math.Abs(t - s) >= clear - 1e-6); });
                        if (host == null) { warnings.Add("The span after the support at " + Fmt(sorted[i].P) + " is " + Math.Round(gap, 0) + " mm (more than " + step + ") and no pipe has a free place for another support there."); break; }
                        double th = (target - host.A).DotProduct(host.D);
                        sorted.Insert(i + k, new SItem { Pipe = host, T = th, P = host.A + host.D * th, Anchor = false, Why = "span" });
                    }
                }
                res = sorted;
            }
            else if (pipes.Count > 1) warnings.Add("The pipes are not on one straight line: the span between supports of different pipes is not checked.");
            return res;
        }

        static string SupportSpecFor(string material)
        {
            string m = (material ?? "").ToUpperInvariant().Replace('C', 'С').Replace('S', 'С').Replace('G', 'Г');   // Latin lookalikes of Cyrillic letters
            if (m.Contains("09Г2С")) return "!_Опоры ОСТ 36-146-88 09Г2С";
            m = m.Replace('C', 'С').Replace('T', 'Т').Replace('G', 'Г');   // Latin lookalikes
            if (Regex.IsMatch(m.Trim(), @"^(СТ\.?\s*|СТАЛЬ\s*)?20(?![0-9А-ЯA-Z])")) return "!_Опоры ОСТ 36-146-88 ВСт3пс";
            throw new InvalidOperationException("Cannot choose the support spec for pipe material '" + material + "'. Rule: 09Г2С -> '!_Опоры ОСТ 36-146-88 09Г2С', steel 20 -> '!_Опоры ОСТ 36-146-88 ВСт3пс'. Pass 'spec'.");
        }

        public static object AddSupports(JsonElement args)
        {
            if (args.TryGetProperty("job", out var jb) && jb.ValueKind == JsonValueKind.String) return SteelJobStatus(jb.GetString());

            var doc = ActiveDocument();
            var dlm = Dlm();
            var piping = Piping();
            double step = args.TryGetProperty("step", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetDouble() : 3000;
            double margin = args.TryGetProperty("weld_margin", out var wm) && wm.ValueKind == JsonValueKind.Number ? wm.GetDouble() : 100;
            double hwArg = args.TryGetProperty("support_half_width", out var hwj) && hwj.ValueKind == JsonValueKind.Number ? hwj.GetDouble() : 75;
            double supH = args.TryGetProperty("support_height", out var shj) && shj.ValueKind == JsonValueKind.Number ? shj.GetDouble() : 100;
            double floorZ = args.TryGetProperty("floor_z", out var fz) && fz.ValueKind == JsonValueKind.Number ? fz.GetDouble() : 0;
            bool withStructure = !(args.TryGetProperty("structure", out var ws) && ws.ValueKind == JsonValueKind.False);
            string family = args.TryGetProperty("support_family", out var sf) && sf.ValueKind == JsonValueKind.String ? sf.GetString() : "КП-АС11";
            string kind = args.TryGetProperty("support_kind", out var sk) && sk.ValueKind == JsonValueKind.String ? sk.GetString() : "скользящая";   // the sample: КП-АС11 "подвижная скользящая"
            string specArg = args.TryGetProperty("spec", out var sa) && sa.ValueKind == JsonValueKind.String ? sa.GetString() : null;
            if (step < 500) throw new ArgumentException("step must be at least 500 mm");
            if (margin < 100) throw new ArgumentException("weld_margin below 100 mm violates GOST 32569 cl. 10.4.3");
            var handles = args.GetProperty("pipes").EnumerateArray().Select(e => e.GetString()).Where(h => !string.IsNullOrWhiteSpace(h)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (SteelBusy && !IsDry(args)) throw new InvalidOperationException("The previous add_supports is still creating plates and posts in the background; wait for it (call with its job id) before starting another.");
            if (handles.Count == 0 || handles.Count > 40) throw new ArgumentException("pipes: 1..40 handles");

            // Welds the supports must keep away from: connector ports lying on a pipe (pipe-end welds are handled by the pipe ends themselves).
            var objs = PlantObjects(doc, dlm);
            var weldPoints = new List<(ObjectId, Point3d)>();
            foreach (var o in objs.Where(o => o.cls.Contains("Connector")))
                foreach (var p in SafePorts(doc, o.id)) weldPoints.Add((o.id, p.pos));
            var existing = new List<Point3d>();
            foreach (var o in objs.Where(o => o.cls.Contains("Support") && !o.cls.Contains("Structure")))
                try { existing.Add(PartPosition(doc, o.id)); } catch (Exception) { }

            var pipes = handles.Select(h => ReadSPipe(doc, dlm, IdFromHandle(doc.Database, h), weldPoints)).ToList();
            // The rule 'supports at every valve, both sides': neighbours of valves are reported if they are not in the list.
            var warnings = new List<string>();
            var fixedWarnings = new List<string>();
            foreach (var sp in pipes)
            {
                if (sp.ValveA != null) fixedWarnings.AddRange(OtherSideNote(doc, dlm, sp, "S1", sp.ValveA, handles));
                if (sp.ValveB != null) fixedWarnings.AddRange(OtherSideNote(doc, dlm, sp, "S2", sp.ValveB, handles));
            }
            foreach (var sp in pipes)
                if (Math.Abs(sp.D.Z) > 0.05) fixedWarnings.Add("Pipe " + sp.H + " is not horizontal (slope " + Math.Round(sp.D.Z * 100, 1) + " %): the support height and the post length are computed for a horizontal pipe and are only approximate.");
            fixedWarnings = fixedWarnings.Distinct().ToList();

            double hw = hwArg;
            var items = PlanSupports(pipes, existing, hw, margin, step, warnings);

            // Parts per material.
            var sm = SpecManager.GetSpecManager();
            var partFor = new Dictionary<string, (SpecPart part, string spec, string desc)>();
            void EnsureParts()
            {
            foreach (var sp in items.Select(i => i.Pipe).Distinct())
            {
                string spec = specArg ?? SupportSpecFor(sp.Material);
                string k = spec + "|" + sp.Nd;
                if (partFor.ContainsKey(k)) continue;
                string key = LoadSpecKey(sm, piping, spec);
                var cands = CollectParts(sm, spec, key, "Support", sp.Nd).Where(c => (c.desc ?? "").Contains("КП")).ToList();
                var picked = cands.Where(c => (c.desc ?? "").Contains(family) && (c.desc ?? "").Contains(kind)).ToList();
                if (picked.Count != 1)
                    throw new InvalidOperationException("Expected exactly one КП support '" + family + "' / '" + kind + "' of DN" + sp.Nd + " in " + spec + ", found " + picked.Count + ". КП parts of this size: " + string.Join(" | ", cands.Select(c => c.desc)) + ". Pass support_family / support_kind.");
                partFor[k] = (picked[0].part, spec, picked[0].desc);
            }
            }
            EnsureParts();

            // Sub-structure geometry (the user's sample): plate top = pipe axis - R - support height; post from floor plate top (floorZ + 10) to the underside of the upper plate.
            const double plateT = 5, floorPlateTop = 10, plateSize = 250, postOffset = 26;
            object Structure(SItem it)
            {
                double plateTop = it.P.Z - it.Pipe.Od / 2 - supH;
                double postTop = plateTop - plateT;
                double postLen = postTop - (floorZ + floorPlateTop);
                return new
                {
                    plate_top_z = Math.Round(plateTop, 2), upper_plate = "250x250x" + plateT, post = "12П, " + Math.Round(postLen, 1) + " mm",
                    floor_plate = "250x250x" + plateT + " at Z=" + (floorZ + floorPlateTop - plateT) + ".." + (floorZ + floorPlateTop), feasible = postLen > 100
                };
            }

            // Rebuilt after a re-plan so the answer never shows stale positions.
            object MakePlan() => new
            {
                step, weld_margin = margin, support_half_width_assumed = hw, support_height = supH, floor_z = floorZ, family,
                supports = items.Select(it => new
                {
                    pipe = it.Pipe.H, material = it.Pipe.Material, spec = partFor[(specArg ?? SupportSpecFor(it.Pipe.Material)) + "|" + it.Pipe.Nd].spec,
                    part = partFor[(specArg ?? SupportSpecFor(it.Pipe.Material)) + "|" + it.Pipe.Nd].desc,
                    distance_from_S1 = Math.Round(it.T, 1), point = Arr(it.P), reason = it.Why, structure = withStructure ? Structure(it) : null
                }).ToList(),
                warnings = fixedWarnings.Concat(warnings).ToList()
            };
            var plan = MakePlan();
            if (IsDry(args))
                return new { dry_run = true, document = doc.Name, plan, note = "Nothing was changed. The support half-width is an assumption until a support is placed and measured; apply re-plans with the measured value. Repeat with dry_run=false." };

            // ---- apply -----------------------------------------------------------------------------------------
            var added = new HashSet<int>();
            var before = ModelRows(doc, dlm);
            var log = new List<string>();
            var placed = new List<(SItem it, ObjectId id)>();
            var dynPorts = new List<(ObjectId pipe, string name)>();
            bool connect = !(args.TryGetProperty("connect", out var cj) && cj.ValueKind == JsonValueKind.False);
            // Undo of everything this call added, run only outside the PipingObjectAdder; reports what could not be removed.
            string Undo()
            {
                string rb = RollbackSince(doc, dlm, added, before);
                var bad = new List<string>();
                foreach (var dp in dynPorts) try { RemoveDynamicPort(doc, dp.pipe, dp.name); } catch (Exception dex) { bad.Add(dp.pipe.Handle + "." + dp.name + ": " + dex.Message); }
                return rb + (bad.Count > 0 ? "; DYNAMIC PORTS NOT REMOVED (remove by hand): " + string.Join(", ", bad) : "");
            }
            try
            {
                bool needReplan; int attempt = 0;
                do
                {
                    needReplan = false; double maxHalf = 0;
                    placed.Clear();
                    using (doc.LockDocument())
                    using (var adder = new PipingObjectAdder(piping.DataLinksManager3d, doc.Database))
                    {
                        for (int i = 0; i < items.Count; i++)
                        {
                            var it = items[i];
                            var pp = partFor[(specArg ?? SupportSpecFor(it.Pipe.Material)) + "|" + it.Pipe.Nd].part;
                            var sup = new Support();
                            sup.SymbolId = ContentManager.GetContentManager().GetSymbol(pp, doc.Database);
                            sup.Position = it.P;
                            int row = adder.Add(pp, sup);
                            added.Add(row);
                            var id = FindByRow(doc, dlm, row);
                            double half = FitSupport(doc, id, it, log);
                            placed.Add((it, id));
                            if (half > hw + 1)
                            {
                                if (attempt >= 2) throw new InvalidOperationException("Support at " + Fmt(it.P) + " is wider than planned (" + Math.Round(half, 1) + " > " + hw + " mm): the 100 mm rule would be violated.");
                                // A support measured wider than assumed: stop, undo (after the adder is released) and re-plan with the real value.
                                maxHalf = Math.Max(maxHalf, half); needReplan = true; break;
                            }
                            if (connect)
                            {
                                // As in the user's connected sample: a dynamic port Dn on the pipe at the axis point, joined to the support's S1.
                                var sp0 = PortsOf(doc, id);
                                if (sp0.Count != 1) throw new InvalidOperationException("Support " + id.Handle + " has " + sp0.Count + " ports, expected 1");
                                string dyn = AddDynamicPort(doc, it.Pipe.Id, it.P, sp0[0].dir.Negate(), id);
                                dynPorts.Add((it.Pipe.Id, dyn));
                                ConnectCore(doc, id, it.Pipe.Id, true, null);
                                log.Add("support " + id.Handle + " connected to pipe " + it.Pipe.H + "." + dyn);
                            }
                        }
                    }
                    if (needReplan)
                    {
                        hw = Math.Ceiling(maxHalf);
                        log.Add("measured half-width " + Math.Round(maxHalf, 1) + " mm; undone: " + Undo() + "; re-planning with " + hw);
                        added.Clear(); placed.Clear(); dynPorts.Clear();
                        warnings.Clear();
                        items = PlanSupports(pipes, existing, hw, margin, step, warnings);
                        EnsureParts();
                        plan = MakePlan();
                        attempt++;
                    }
                } while (needReplan);

                var steel = new List<(Point3d a, Point3d b)>(); var plates = new List<(Point3d a, Point3d b)>(); var posts = new List<(Point3d a, Point3d b)>();
                if (withStructure)
                    foreach (var pl in placed)
                    {
                        var it = pl.it;
                        double plateTop = it.P.Z - it.Pipe.Od / 2 - supH;
                        double postTop = plateTop - plateT;
                        double postLen = postTop - (floorZ + floorPlateTop);
                        if (postLen < 100) { warnings.Add("Support at " + Fmt(it.P) + ": the post would be only " + Math.Round(postLen, 1) + " mm (pipe too low for the sample sub-structure); NO sub-structure was made for it."); continue; }
                        var h = new Vector3d(it.Pipe.D.X, it.Pipe.D.Y, 0); h = h.Length < 1e-6 ? Vector3d.XAxis : h.GetNormal();
                        var c = new Point3d(it.P.X, it.P.Y, 0);
                        double hs = plateSize / 2;
                        plates.Add((new Point3d(c.X - hs, c.Y - hs, plateTop), new Point3d(c.X + hs, c.Y + hs, plateTop)));
                        plates.Add((new Point3d(c.X - hs, c.Y - hs, floorZ + floorPlateTop), new Point3d(c.X + hs, c.Y + hs, floorZ + floorPlateTop)));
                        var pc = c + h * postOffset;
                        posts.Add((new Point3d(pc.X, pc.Y, floorZ + floorPlateTop), new Point3d(pc.X, pc.Y, postTop)));
                    }

                // The answer is prepared before the background job starts: nothing after the start may fail and trigger an undo under it.
                var created = placed.Select(pl => new
                {
                    handle = pl.id.Handle.ToString(), pipe = pl.it.Pipe.H, distance_from_S1 = Math.Round(pl.it.T, 1),
                    position = Arr(PartPosition(doc, pl.id)), reason = pl.it.Why
                }).ToList();
                string jobId = withStructure && (plates.Count + posts.Count) > 0 ? StartSteelJob(doc, plates, posts) : null;
                return new
                {
                    plan, created, warnings = fixedWarnings.Concat(warnings).ToList(), log, structure_job = jobId,
                    note = jobId != null ? "Supports are placed. The plates and posts are created by Plant 3D commands in the background; call add_supports with {\"job\":\"" + jobId + "\"} to see the progress."
                                         : "Supports are placed."
                };
            }
            catch (Exception ex)
            {
                string rb = Undo();
                throw new InvalidOperationException("add_supports failed and was rolled back (" + rb + "): " + ex.Message + (log.Count > 0 ? " | " + string.Join(" | ", log) : ""));
            }
        }

        static IEnumerable<string> OtherSideNote(Document doc, DataLinksManager dlm, SPipe sp, string port, string valve, List<string> handles)
        {
            var res = new List<string>();
            try
            {
                var vid = IdFromHandle(doc.Database, valve);
                foreach (var vp in new[] { "S1", "S2" })
                {
                    var side = WalkValveSide(doc, vid, vp);
                    string h = side.Pipe.Handle.ToString();
                    if (!handles.Contains(h, StringComparer.OrdinalIgnoreCase)) res.Add("Valve " + valve + ": the pipe " + h + " on its " + vp + " side is not in 'pipes'; the rule needs a support on both sides of a valve, add it to the list.");
                }
            }
            catch (Exception e) { res.Add("Valve " + valve + ": could not check its other side (" + e.Message + ")."); }
            return res.Distinct();
        }

        // Puts the support on the pipe axis point and turns it along the pipe; returns its half extent along the pipe (mm).
        static double FitSupport(Document doc, ObjectId id, SItem it, List<string> log)
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var s = (Support)tr.GetObject(id, OpenMode.ForWrite);
                var ports = s.GetPorts(PortType.All);
                string pdesc = string.Join(" ", Enumerable.Range(0, ports.Count).Select(i => ports[i].Name + Fmt(ports[i].Position) + "/" + Fmt(ports[i].Direction)));
                log.Add("support " + id.Handle + " trial: pos " + Fmt(s.Position) + " X" + Fmt(s.XAxis) + " Z" + Fmt(s.ZAxis) + " ports " + pdesc);
                if (s.Position.DistanceTo(it.P) > 0.5) s.Position = it.P;
                var h = new Vector3d(it.Pipe.D.X, it.Pipe.D.Y, 0);
                if (h.Length > 1e-6)
                {
                    var xa = new Vector3d(s.XAxis.X, s.XAxis.Y, 0);
                    if (xa.Length > 1e-6)
                    {
                        double d = Math.Atan2(h.Y, h.X) - Math.Atan2(xa.Y, xa.X);
                        while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI;
                        if (Math.Abs(d) > 1e-3) { s.Rotation = s.Rotation + d; log.Add("support " + id.Handle + " rotated by " + Math.Round(d * 180 / Math.PI, 1) + " deg"); }
                    }
                }
                tr.Commit();
            }
            // Measured in a separate transaction, after the new position and rotation are committed.
            double half = 0;
            using (doc.LockDocument())
            using (var tr2 = doc.Database.TransactionManager.StartTransaction())
            {
                try
                {
                    var ext = ((Support)tr2.GetObject(id, OpenMode.ForRead)).GeometricExtents;
                    foreach (var x in new[] { ext.MinPoint.X, ext.MaxPoint.X }) foreach (var y in new[] { ext.MinPoint.Y, ext.MaxPoint.Y }) foreach (var z in new[] { ext.MinPoint.Z, ext.MaxPoint.Z })
                        half = Math.Max(half, Math.Abs((new Point3d(x, y, z) - it.P).DotProduct(it.Pipe.D)));
                }
                catch (Exception e) { log.Add("support extents unavailable: " + e.Message); }
                tr2.Commit();
            }
            return half;
        }

        // ---- background job: plates and posts through the Plant 3D steel commands -------------------------------------

        sealed class SteelJob { public string Id; public string State = "running"; public List<string> Log = new List<string>(); public List<string> Created = new List<string>(); public string Error; public int Total, Done; }
        static readonly Dictionary<string, SteelJob> Jobs = new Dictionary<string, SteelJob>();
        static volatile bool SteelBusy;   // one background job at a time (it runs real Plant 3D commands)

        static object SteelJobStatus(string id)
        {
            if (!Jobs.TryGetValue(id, out var j)) throw new ArgumentException("Unknown job " + id);
            return new { job = j.Id, state = j.State, done = j.Done, total = j.Total, created = j.Created, error = j.Error, log = j.Log };
        }

        static List<(ObjectId id, string cls)> SteelObjects(Document doc)
        {
            var res = new List<(ObjectId, string)>();
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForRead);
                foreach (ObjectId id in ms) { string c = id.ObjectClass.Name; if (c.Contains("Structure")) res.Add((id, c)); }
                tr.Commit();
            }
            return res;
        }

        static void SetLayer(Document doc, ObjectId id, string layer)
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(layer))
                {
                    lt.UpgradeOpen();
                    var rec = new LayerTableRecord { Name = layer };
                    lt.Add(rec); tr.AddNewlyCreatedDBObject(rec, true);
                }
                ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Layer = layer;
                tr.Commit();
            }
        }

        static string StartSteelJob(Document doc, List<(Point3d a, Point3d b)> plates, List<(Point3d a, Point3d b)> posts)
        {
            var job = new SteelJob { Id = Guid.NewGuid().ToString("N").Substring(0, 8), Total = plates.Count + posts.Count };
            Jobs[job.Id] = job;
            SteelBusy = true;
            try
            {
            AcApp.DocumentManager.ExecuteInCommandContextAsync(async _ =>
            {
                object oldSnap = null;
                try
                {
                    if (AcApp.DocumentManager.MdiActiveDocument != doc) throw new InvalidOperationException("The active drawing changed before the plates and posts were made; nothing was created for them.");
                    oldSnap = AcApp.GetSystemVariable("OSMODE");
                    AcApp.SetSystemVariable("OSMODE", 0);
                    var ed = doc.Editor;
                    async System.Threading.Tasks.Task Run(string cmd, string layer, params object[] a)
                    {
                        var known = new HashSet<ObjectId>(SteelObjects(doc).Select(x => x.id));
                        await ed.CommandAsync(a.Prepend(cmd).ToArray());
                        string status = "finished";
                        var fresh = SteelObjects(doc).Where(x => !known.Contains(x.id)).ToList();
                        job.Log.Add(cmd + " -> " + status + ", new objects: " + string.Join(",", fresh.Select(f => f.id.Handle + ":" + f.cls)));
                        if (fresh.Count == 0) throw new InvalidOperationException(cmd + " created nothing (status " + status + ")");
                        foreach (var f in fresh)
                        {
                            try { SetLayer(doc, f.id, layer); } catch (Exception le) { job.Log.Add("layer " + layer + " not set: " + le.Message); }
                            try
                            {
                                var p = Props(Dlm(), RowId(Dlm(), f.id));
                                job.Created.Add(f.id.Handle + " " + f.cls + " " + (p.TryGetValue("PartSizeLongDesc", out var d) ? d : "") +
                                                (p.TryGetValue("BOS", out var b) ? " BOS=" + b : "") + (p.TryGetValue("TOS", out var t) ? " TOS=" + t : ""));
                            }
                            catch (Exception) { job.Created.Add(f.id.Handle + " " + f.cls); }
                        }
                        job.Done++;
                    }
                    foreach (var p in plates) await Run("PLANTSTEELPLATE", "листы", p.a, p.b);
                    foreach (var m in posts) await Run("PLANTSTEELMEMBER", "подопорныеМК", m.a, m.b, "");
                    job.State = "done";
                }
                catch (Exception e) { job.State = "failed"; job.Error = e.GetType().Name + ": " + e.Message; }
                finally { try { if (oldSnap != null) AcApp.SetSystemVariable("OSMODE", oldSnap); } catch (Exception) { } SteelBusy = false; }
            }, null);
            }
            catch (Exception)
            {
                // The command context never started: do not leave the guard on forever.
                SteelBusy = false; job.State = "failed"; job.Error = "could not start the background command context";
                throw;
            }
            return job.Id;
        }
    }
}
