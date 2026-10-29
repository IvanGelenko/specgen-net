using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PartsRepository;
using Autodesk.ProcessPower.PnP3dObjects;
using Autodesk.ProcessPower.P3dProjectParts;
using Autodesk.ProcessPower.PlantInstance;
using Autodesk.ProcessPower.ProjectManager;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

namespace Plant3dMcp
{
    // Read-only access to the Plant 3D project, its data links (properties) and specs.
    static partial class PlantTools
    {
        static PipingProject Piping()
        {
            var prj = PlantApplication.CurrentProject ?? throw new InvalidOperationException("No Plant 3D project is open");
            return (PipingProject)prj.ProjectParts["Piping"];
        }

        static DataLinksManager Dlm() => Piping().DataLinksManager;

        public static object ProjectInfo()
        {
            var cur = PlantApplication.CurrentProject ?? throw new InvalidOperationException("No Plant 3D project is open");
            var piping = Piping();
            var drawings = new List<string>();
            foreach (var f in piping.GetPnPDrawingFiles()) drawings.Add(f.ResolvedFilePath);
            return new
            {
                name = piping.ProjectName,
                directory = piping.ProjectDirectory,
                units = piping.ProjectUnitsType.ToString(),
                specs = piping.GetSpecNames().Cast<object>().Select(o => o.ToString()).ToList(),
                drawings
            };
        }

        // FindAcPpRowId throws (ObjectDoesNotHaveLink) for objects that carry no Plant data.
        static int RowId(DataLinksManager dlm, ObjectId id)
        {
            try { return dlm.FindAcPpRowId(id); }
            catch (Exception e) when (e.Message.Contains("ObjectDoesNotHaveLink")) { return 0; }
        }

        static Dictionary<string, string> Props(DataLinksManager dlm, int rowId)
        {
            var d = new Dictionary<string, string>();
            foreach (var kv in dlm.GetAllProperties(rowId, true)) d[kv.Key] = kv.Value;
            return d;
        }

        static readonly string[] Summary = { "Tag", "Size", "Spec", "LineNumberTag", "PartFamilyLongDesc", "Description", "Material" };

        public static object ListParts(JsonElement args)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument ?? throw new InvalidOperationException("No active document");
            bool obj = args.ValueKind == JsonValueKind.Object;
            int limit = Tools.Limit(args, 100);
            string line = obj && args.TryGetProperty("line", out var ln) ? ln.GetString() : null;
            var dlm = Dlm();
            var items = new List<object>();
            int total = 0;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    int rowId = RowId(dlm, id);
                    if (rowId <= 0) continue;
                    var p = Props(dlm, rowId);
                    if (line != null && (!p.TryGetValue("LineNumberTag", out var lt) || lt != line)) continue;
                    total++;
                    if (items.Count >= limit) continue;
                    var sel = new Dictionary<string, string>();
                    foreach (var k in Summary) if (p.TryGetValue(k, out var v)) sel[k] = v;
                    items.Add(new { handle = id.Handle.ToString(), cls = id.ObjectClass.Name, rowId, props = sel });
                }
                tr.Commit();
            }
            return new { total, shown = items.Count, items };
        }

        public static object PartProperties(JsonElement args)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument ?? throw new InvalidOperationException("No active document");
            string hs = args.GetProperty("handle").GetString();
            var id = IdFromHandle(doc.Database, hs);
            var dlm = Dlm();
            int rowId = RowId(dlm, id);
            if (rowId <= 0) throw new InvalidOperationException("Object " + hs + " has no Plant data");
            return new { handle = hs, cls = id.ObjectClass.Name, rowId, properties = Props(dlm, rowId) };
        }

        // Mini-catalog (spec) used when the caller names none: group "Мини-каталоги труб", PN16, 09Г2С.
        const string DefaultSpec = "!_Rus-PipeAndFittings_09Г2С_PN16";

        // Creates a straight spec pipe. Modifies the active drawing.
        public static object CreatePipe(JsonElement args)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument ?? throw new InvalidOperationException("No active document");
            string spec = (args.TryGetProperty("spec", out var sp) && sp.ValueKind == JsonValueKind.String ? sp.GetString() : null) ?? DefaultSpec;
            double nd = args.GetProperty("nd").GetDouble();
            double length = args.GetProperty("length").GetDouble();
            double[] s = Vec(args, "start", 0, 0, 0);
            double[] dir = Vec(args, "direction", 1, 0, 0);
            double n = Math.Sqrt(dir[0] * dir[0] + dir[1] * dir[1] + dir[2] * dir[2]);
            if (n < 1e-9 || length <= 0) throw new ArgumentException("direction must be non-zero and length positive");

            var piping = Piping();
            var sm = SpecManager.GetSpecManager();
            string specName = spec;
            spec = LoadSpecKey(sm, piping, spec);

            // Collect all candidates; ambiguity is an error, never a silent choice.
            double? wall = args.TryGetProperty("wall", out var w) ? w.GetDouble() : (double?)null;
            var candidates = ChoosePipe(CollectParts(sm, specName, spec, "Pipe", nd), wall, spec, nd, out string wallRule);
            SpecPart part = candidates[0].part;
            int matches = 1;

            double od = OuterDiameter(part);
            if (od <= 0) throw new InvalidOperationException("Spec part has no outer diameter");
            var p0 = new Autodesk.AutoCAD.Geometry.Point3d(s[0], s[1], s[2]);
            var p1 = new Autodesk.AutoCAD.Geometry.Point3d(s[0] + dir[0] / n * length, s[1] + dir[1] / n * length, s[2] + dir[2] / n * length);

            // GOST 32569 rules (see RULES-GOST-32569.md): weld spacing (6.8.1), slope (10.1.4), vents/drains (10.2.1, table 10.3).
            double weldMin = DesignRules.WeldSpacingMin(od, candidates[0].wall);
            bool relax = args.TryGetProperty("relax_weld_spacing", out var rw) && rw.ValueKind == JsonValueKind.True;
            if (length < weldMin && !relax)
                throw new InvalidOperationException("The pipe is " + length + " mm long; GOST 32569 cl. 6.8.1 requires at least " + Math.Round(weldMin, 1) +
                    " mm between circumferential butt welds (3*wall, and 100/250/400 mm by OD). Pass relax_weld_spacing=true only for a justified case.");
            var notes = new List<string>();
            if (length < weldMin) notes.Add("relax_weld_spacing=true: shorter than GOST 32569 cl. 6.8.1 (" + Math.Round(weldMin, 1) + " mm). Record the justification.");
            var slopeNote = DesignRules.SlopeNote("The pipe", DesignRules.Slope(p0, p1));
            if (slopeNote != null) notes.Add(slopeNote);
            notes.AddRange(DesignRules.VentDrainNotes(new[] { p0, p1 }, nd));

            // Two-phase: unless dry_run is explicitly false the plan is returned and nothing is modified.
            bool dry = !(args.TryGetProperty("dry_run", out var dr) && dr.ValueKind == JsonValueKind.False);
            if (dry)
                return new
                {
                    dry_run = true, design_notes = notes, spec, nd, length, od, wall = candidates[0].wall, wall_rule = wallRule ?? "only pipe of this size in the spec",
                    start = new[] { p0.X, p0.Y, p0.Z }, end = new[] { p1.X, p1.Y, p1.Z },
                    description = candidates[0].desc,
                    note = "Nothing was changed. Repeat the call with dry_run=false to create the pipe."
                };

            int rowId;
            using (doc.LockDocument())
            {
                var pipe = new Autodesk.ProcessPower.PnP3dObjects.Pipe();
                pipe.StartPoint = p0;
                pipe.EndPoint = p1;
                pipe.OuterDiameter = od;
                using (var adder = new Autodesk.ProcessPower.PnP3dObjects.PipingObjectAdder(piping.DataLinksManager3d, doc.Database))
                    rowId = adder.Add(part, pipe);
            }

            // Locate the new entity by its row id.
            var dlm = Dlm();
            string handle = null;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                    if (RowId(dlm, id) == rowId) { handle = id.Handle.ToString(); break; }
                tr.Commit();
            }
            return new
            {
                handle, rowId, spec, nd, length, od, design_notes = notes,
                start = new[] { p0.X, p0.Y, p0.Z }, end = new[] { p1.X, p1.Y, p1.Z },
                description = Prop(part, "PartFamilyLongDesc")?.ToString(),
                specMatches = matches
            };
        }

        // SelectParts threw InvalidSpecPartException for ("Pipe", NominalDiameter) on this project, so several
        // strategies are tried and every failure is reported with the spec's available part types.
        // Wall: explicit 'wall' wins; otherwise the user's default rule (table 5.6 s_min + 2 mm, rounded up);
        // a spec with exactly one candidate is taken as is. Returns exactly one candidate or throws.
        static List<(SpecPart part, double wall, string desc)> ChoosePipe(List<(SpecPart part, double wall, string desc)> candidates, double? wall, string spec, double nd, out string wallRule)
        {
            if (candidates.Count == 0) throw new InvalidOperationException("Spec " + spec + " has no Pipe with DN" + nd);
            wallRule = null;
            if (wall.HasValue)
            {
                double wv = wall.Value;
                candidates = candidates.Where(c => Math.Abs(c.wall - wv) < 1e-3).ToList();
                wallRule = "explicit";
            }
            else if (candidates.Count > 1)
            {
                double def = DefaultWall(OuterDiameter(candidates[0].part));
                candidates = candidates.Where(c => Math.Abs(c.wall - def) < 1e-3).ToList();
                wallRule = "default rule: s_min (table 5.6) + 2 mm rounded up = " + def + " mm";
            }
            if (candidates.Count != 1)
                throw new InvalidOperationException("Ambiguous or no match for DN" + nd + " in " + spec + " (" + (wallRule ?? "single") + "); pass 'wall'. Spec candidates were filtered to: " +
                    string.Join(" | ", candidates.Select(c => "s=" + c.wall + " " + c.desc)));
            return candidates;
        }

        static List<(SpecPart part, double wall, string desc)> CollectParts(SpecManager sm, string specName, string loadedKey, string type, double nd)
        {
            var diag = new List<string>();
            // LoadSpec accepted 'loadedKey' (a full path) but GetAvailablePartTypes/SelectParts may be keyed by another form of the name.
            var keys = new List<string> { specName, loadedKey };
            try { keys.Add(System.IO.Path.GetFileNameWithoutExtension(loadedKey)); } catch { }
            try { keys.Add(System.IO.Path.GetFileName(loadedKey)); } catch { }
            try { keys.Add(System.IO.Path.ChangeExtension(loadedKey, ".pspc")); } catch { }

            foreach (string spec in keys.Where(k => !string.IsNullOrEmpty(k)).Distinct())
            {
                string[] types = Array.Empty<string>();
                try { types = sm.GetAvailablePartTypes(spec) ?? types; } catch (Exception e) { diag.Add("[" + Short(spec) + "] GetAvailablePartTypes threw " + e.GetType().Name); }
                bool has = false;
                try { has = sm.HasType(spec, type); } catch (Exception e) { diag.Add("[" + Short(spec) + "] HasType threw " + e.GetType().Name); }
                diag.Add("[" + Short(spec) + "] types=" + types.Length + " Has" + type + "=" + has);

                var typeKeys = new List<string> { type };
                typeKeys.AddRange(types.Where(t => t.IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0 && t != type));
                var r = TrySelect(sm, spec, typeKeys, nd, diag);
                if (r != null) return r;
            }
            throw new InvalidOperationException("No " + type + " of DN" + nd + " could be selected. " + string.Join("; ", diag));
        }

        static string Short(string k) => k.Length > 40 ? "..." + k.Substring(k.Length - 37) : k;

        static List<(SpecPart part, double wall, string desc)> TrySelect(SpecManager sm, string spec, List<string> typeKeys, double nd, List<string> diag)
        {
            // Results of every matching type key are united (e.g. "Elbow" and a separate "Elbow45" type).
            var all = new List<(SpecPart part, double wall, string desc)>();
            foreach (string type in typeKeys.Distinct())
            {
                // Strategy A: size-filtered selection. Strategy B: all parts of the type, filtered by hand.
                for (int strat = 0; strat < 2; strat++)
                {
                    string label = "[" + Short(spec) + "] " + type + "/" + (strat == 0 ? "byND" : "all");
                    var found = new List<(SpecPart part, double wall, string desc)>();
                    try
                    {
                        using (var reader = strat == 0 ? sm.SelectParts(spec, type, new NominalDiameter("mm", nd)) : sm.SelectParts(spec, type))
                        {
                            while (reader.Next())
                            {
                                var p = new SpecPart();
                                p.AssignFrom(reader.Current);
                                if (strat == 1)
                                {
                                    object size = Prop(p, "NominalDiameter");
                                    if (size == null || size is DBNull || Math.Abs(Convert.ToDouble(size) - nd) > 1e-6) continue;
                                }
                                found.Add((p, WallOf(p), (Prop(p, "PartSizeLongDesc") ?? Prop(p, "PartFamilyLongDesc"))?.ToString()));
                            }
                        }
                        // Extra type keys (e.g. "PipeSupport" next to "Pipe") may hold parts of another kind: keep only parts whose own type is the requested one.
                        if (type != typeKeys[0]) found = found.Where(f => SameType(f.part, typeKeys[0])).ToList();
                        diag.Add(label + " -> " + found.Count);
                        if (found.Count > 0) { all.AddRange(found); break; }   // this type is done; go to the next type key
                    }
                    catch (Exception e) { diag.Add(label + " threw " + e.GetType().Name); }
                }
            }
            return all.Count > 0 ? all : null;
        }

        static bool SameType(SpecPart p, string type)
        {
            try { return string.Equals(p.Type, type, StringComparison.OrdinalIgnoreCase); } catch (Exception) { return false; }
        }

        static double ToDouble(object v) => v == null || v is DBNull ? 0 : Convert.ToDouble(v);

        // Outer diameter of a spec pipe: the property if present, else the first number of a description like "Труба 159x5п ...".
        static double OuterDiameter(SpecPart p)
        {
            foreach (var n in new[] { "MatchingPipeOd", "OuterDiameter", "OD", "PipeOd" })
            {
                double d = ToDouble(Prop(p, n));
                if (d > 0) return d;
            }
            var m = SizeInDesc(p);
            if (m != null) return double.Parse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
            throw new InvalidOperationException("Cannot determine outer diameter. Part properties: " + string.Join(", ", p.PropNames.Cast<string>()));
        }

        // Wall thickness: the property if present, else the second number of "159x5п".
        static double WallOf(SpecPart p)
        {
            double w = ToDouble(Prop(p, "WallThickness"));
            if (w > 0) return w;
            var m = SizeInDesc(p);
            return m != null ? double.Parse(m.Groups[2].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture) : 0;
        }

        static System.Text.RegularExpressions.Match SizeInDesc(SpecPart p)
        {
            foreach (var n in new[] { "PartSizeLongDesc", "ShortDescription", "PartFamilyLongDesc" })
            {
                string s = Prop(p, n)?.ToString();
                if (string.IsNullOrEmpty(s)) continue;
                // "159x5п": OD x wall. Steel grades such as "08х18н10т" look similar, so a match counts only if it is a
                // plausible pipe size (OD >= 10 mm, wall < OD and <= 60 mm).
                // Not glued to letters/digits on the left (so "Ст20х13" is skipped); a letter right after the wall ("159x5п") is fine.
                // Several plausible matches: the largest outer diameter wins ("20х13" next to "159x5").
                System.Text.RegularExpressions.Match best = null; double bestOd = 0;
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(s, @"(?<![\p{L}\d.,])(\d+(?:[.,]\d+)?)\s*[xх×]\s*(\d+(?:[.,]\d+)?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    double od = double.Parse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
                    double wl = double.Parse(m.Groups[2].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
                    if (od >= 10 && wl > 0 && wl < od && wl <= 60 && od > bestOd) { best = m; bestOd = od; }
                }
                if (best != null) return best;
            }
            return null;
        }

        // PropValue throws for a property the part does not have; treat that as null.
        static object Prop(PartSizeProperties p, string name)
        {
            foreach (string n in p.PropNames) if (n == name) return p.PropValue(name);
            return null;
        }

        // Default wall thickness by outer diameter Da (mm): minimum rejection thickness from table 5.6
        // (<=25:1.0, <=57:1.5, <=114:2.0, <=219:2.5, <=325:3.0, <=377:3.5, >426:4.0) plus 2 mm, rounded up.
        // The table has no row for 377 < Da <= 426; the next row (4.0) is used there.
        static double DefaultWall(double da)
        {
            const double eps = 0.05;   // ODs are stored as floats; 114.0000001 must still count as 114
            double smin = da <= 25 + eps ? 1.0 : da <= 57 + eps ? 1.5 : da <= 114 + eps ? 2.0 : da <= 219 + eps ? 2.5 : da <= 325 + eps ? 3.0 : da <= 377 + eps ? 3.5 : 4.0;
            return Math.Ceiling(smin + 2.0);
        }

        // SpecManager.LoadSpec by bare name failed on this project, so try the other plausible keys and
        // report what was tried. Returns the key that LoadSpec accepted (SelectParts must use the same one).
        static string LoadSpecKey(SpecManager sm, PipingProject piping, string spec)
        {
            var tried = new List<string>();
            string fileName = null;
            try { fileName = piping.GetSpecFileName(spec); } catch (Exception e) { tried.Add("GetSpecFileName threw " + e.Message); }
            bool exists = false;
            try { exists = piping.SpecExists(spec); } catch { }

            var keys = new List<string> { spec };
            if (!string.IsNullOrEmpty(fileName))
            {
                keys.Add(fileName);
                keys.Add(System.IO.Path.GetFileNameWithoutExtension(fileName));
                if (!System.IO.Path.IsPathRooted(fileName)) keys.Add(System.IO.Path.Combine(piping.ProjectDirectory, fileName));
            }
            foreach (var k in keys.Distinct())
            {
                bool ok;
                try { ok = sm.LoadSpec(k); } catch (Exception e) { tried.Add("'" + k + "' threw " + e.Message); continue; }
                tried.Add("'" + k + "' -> " + ok);
                if (ok) return k;
            }
            throw new InvalidOperationException("Cannot load spec " + spec + " (SpecExists=" + exists + ", SpecFileName='" + fileName + "'). Tried: " + string.Join("; ", tried));
        }

        static double[] Vec(JsonElement args, string name, double x, double y, double z)
        {
            if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return new[] { x, y, z };
            var a = v.EnumerateArray().Select(e => e.GetDouble()).ToArray();
            if (a.Length != 3) throw new ArgumentException(name + " must be [x,y,z]");
            return a;
        }

        public static object ListSpecs()
        {
            var piping = Piping();
            return piping.GetSpecNames().Cast<object>().Select(o => o.ToString()).ToList();
        }
    }
}
