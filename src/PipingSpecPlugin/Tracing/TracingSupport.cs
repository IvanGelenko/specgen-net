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
using Autodesk.ProcessPower.P3dProjectParts;
using Autodesk.ProcessPower.PlantInstance;
using Autodesk.ProcessPower.ProjectManager;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;
using Part = Autodesk.ProcessPower.PnP3dObjects.Part;
using Port = Autodesk.ProcessPower.PnP3dObjects.Port;

namespace PipingSpecPlugin.Tracing
{
    // Helpers of the tracing generator (read the active drawing, ports, connections, parts of a spec, create / erase parts).
    // Copied from the Plant 3D MCP plug-in, where the same generator was developed and tested on live drawings.
    internal static partial class TracingEngine
    {
        const string WeldJointName = "Buttweld (RUS)";

        sealed class SPipe
        {
            public ObjectId Id; public string H; public Point3d A, B; public Vector3d D; public double L, Od, Nd;
            public string Material; public string ValveA, ValveB; public List<double> Inner = new List<double>();
        }


        // ---- SetLayer (from BendTools.cs)
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

        // ---- ModelRows (from BranchTools.cs)
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

        // ---- DescOf (from BranchTools.cs)
        static string DescOf(SpecPart p) => (Prop(p, "PartSizeLongDesc") ?? Prop(p, "PartFamilyLongDesc"))?.ToString();

        // ---- PlantObjects (from ConnectorTools.cs)
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

        // ---- SafePorts (from ConnectorTools.cs)
        static List<(string name, Point3d pos, Vector3d dir)> SafePorts(Document doc, ObjectId id)
        {
            try { return PortsOf(doc, id); } catch (Exception) { return new List<(string, Point3d, Vector3d)>(); }
        }

        // ---- ActiveDocument (from PipelineTools.cs)
        static Document ActiveDocument() => AcApp.DocumentManager.MdiActiveDocument ?? throw new InvalidOperationException("No active document");

        // ---- IdFromHandle (from PipelineTools.cs)
        // Hex handle -> ObjectId in the active database. Accepts an optional 0x prefix; rejects anything else.
        internal static ObjectId IdFromHandle(Database db, string hex)
        {
            string s = (hex ?? "").Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            if (s.Length == 0 || s.Length > 15 || !long.TryParse(s, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out long h) || h <= 0)
                throw new ArgumentException("Bad handle '" + hex + "': expected a hexadecimal DWG handle such as 4E3A");
            try { return db.GetObjectId(false, new Handle(h), 0); }
            catch (Exception e) { throw new ArgumentException("Unknown handle '" + hex + "' in the active drawing: " + e.Message); }
        }

        // ---- IsDry (from PipelineTools.cs)
        static bool IsDry(JsonElement args) =>
            !(args.ValueKind == JsonValueKind.Object && args.TryGetProperty("dry_run", out var dr) && dr.ValueKind == JsonValueKind.False);

        // ---- Arr (from PipelineTools.cs)
        static double[] Arr(Point3d p) => new[] { Math.Round(p.X, 3), Math.Round(p.Y, 3), Math.Round(p.Z, 3) };

        // ---- Arr (from PipelineTools.cs)
        static double[] Arr(Vector3d v) => new[] { Math.Round(v.X, 4), Math.Round(v.Y, 4), Math.Round(v.Z, 4) };

        // ---- Fmt (from PipelineTools.cs)
        static string Fmt(Point3d p) => "(" + Math.Round(p.X, 1) + "," + Math.Round(p.Y, 1) + "," + Math.Round(p.Z, 1) + ")";

        // ---- Fmt (from PipelineTools.cs)
        static string Fmt(Vector3d v) => "(" + Math.Round(v.X, 2) + "," + Math.Round(v.Y, 2) + "," + Math.Round(v.Z, 2) + ")";

        // ---- PortsOf (from PipelineTools.cs)
        // Port positions as plain tuples (for geometry checks).
        static List<(string name, Point3d pos, Vector3d dir)> PortsOf(Document doc, ObjectId id)
        {
            var res = new List<(string, Point3d, Vector3d)>();
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var part = (Part)tr.GetObject(id, OpenMode.ForRead);
                var ports = part.GetPorts(PortType.All);
                for (int i = 0; i < ports.Count; i++) res.Add((ports[i].Name, ports[i].Position, ports[i].Direction));
                tr.Commit();
            }
            return res;
        }

        // ---- ConnectionCount (from PipelineTools.cs)
        static int ConnectionCount(Document doc, ObjectId id)
        {
            try
            {
                using (doc.LockDocument())
                using (var cm = new ConnectionManager())
                    return cm.GetConnections(id).Count;
            }
            catch (Exception) { return -1; }
        }

        // ---- EraseIds (from PipelineTools.cs)
        static void EraseIds(Document doc, IEnumerable<ObjectId> ids)
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    if (id.IsErased) continue;
                    ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase();
                }
                tr.Commit();
            }
        }

        // ---- EraseRows (from PipelineTools.cs)
        // Rollback by data-link row: erases every model-space entity whose row is in 'rows'. Does not depend on having
        // resolved the ObjectId of each new object, so a failure between Add and lookup cannot leave an orphan.
        static int EraseRows(Document doc, DataLinksManager dlm, ICollection<int> rows)
        {
            if (rows.Count == 0) return 0;
            int n = 0;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForRead);
                var victims = new List<ObjectId>();
                foreach (ObjectId id in ms)
                    if (rows.Contains(RowId(dlm, id))) victims.Add(id);
                foreach (var id in victims) { ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase(); n++; }
                tr.Commit();
            }
            return n;
        }

        // ---- RollbackSince (from PipelineTools.cs)
        // Rollback that does not trust its own bookkeeping: Plant 3D may re-create a part under a new row number (observed with a flange),
        // so after erasing the rows this call added, every Plant row that did not exist before the call is erased too.
        // Returns a one-line description including anything that could not be removed.
        static string RollbackSince(Document doc, DataLinksManager dlm, HashSet<int> added, HashSet<int> before)
        {
            var sb = new System.Text.StringBuilder();
            try { sb.Append("erased " + EraseRows(doc, dlm, added) + " of " + added.Count + " added objects"); }
            catch (Exception rex) { sb.Append("first pass failed (" + rex.Message + ")"); }
            try
            {
                var stray = ModelRows(doc, dlm).Where(r => !before.Contains(r)).ToList();
                if (stray.Count > 0)
                {
                    int n = EraseRows(doc, dlm, stray);
                    sb.Append("; " + stray.Count + " stray object(s) the manager had created or re-created, erased " + n);
                }
                var left = ModelRows(doc, dlm).Where(r => !before.Contains(r)).ToList();
                if (left.Count > 0) sb.Append("; LEFTOVER rows " + string.Join(",", left) + " could not be removed - delete them by hand");
            }
            catch (Exception rex) { sb.Append("; verification failed (" + rex.Message + ")"); }
            return sb.ToString();
        }

        // ---- FindByRow (from PipelineTools.cs)
        static ObjectId FindByRow(Document doc, DataLinksManager dlm, int rowId)
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                    if (RowId(dlm, id) == rowId) { tr.Commit(); return id; }
                tr.Commit();
            }
            throw new InvalidOperationException("New object with row " + rowId + " not found in model space");
        }

        // ---- AddPipe (from PipelineTools.cs)
        static int AddPipe(PipingObjectAdder adder, SpecPart part, Point3d a, Point3d b, double od)
        {
            var pipe = new Pipe();
            pipe.StartPoint = a;
            pipe.EndPoint = b;
            pipe.OuterDiameter = od;
            return adder.Add(part, pipe);
        }

        // ---- Piping (from PlantTools.cs)
        static PipingProject Piping()
        {
            var prj = PlantApplication.CurrentProject ?? throw new InvalidOperationException("No Plant 3D project is open");
            return (PipingProject)prj.ProjectParts["Piping"];
        }

        // ---- Dlm (from PlantTools.cs)
        static DataLinksManager Dlm() => Piping().DataLinksManager;

        // ---- RowId (from PlantTools.cs)
        // FindAcPpRowId throws (ObjectDoesNotHaveLink) for objects that carry no Plant data.
        static int RowId(DataLinksManager dlm, ObjectId id)
        {
            try { return dlm.FindAcPpRowId(id); }
            catch (Exception e) when (e.Message.Contains("ObjectDoesNotHaveLink")) { return 0; }
        }

        // ---- Props (from PlantTools.cs)
        static Dictionary<string, string> Props(DataLinksManager dlm, int rowId)
        {
            var d = new Dictionary<string, string>();
            foreach (var kv in dlm.GetAllProperties(rowId, true)) d[kv.Key] = kv.Value;
            return d;
        }

        // ---- CollectParts (from PlantTools.cs)
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

        // ---- Short (from PlantTools.cs)
        static string Short(string k) => k.Length > 40 ? "..." + k.Substring(k.Length - 37) : k;

        // ---- TrySelect (from PlantTools.cs)
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

        // ---- SameType (from PlantTools.cs)
        static bool SameType(SpecPart p, string type)
        {
            try { return string.Equals(p.Type, type, StringComparison.OrdinalIgnoreCase); } catch (Exception) { return false; }
        }

        // ---- ToDouble (from PlantTools.cs)
        static double ToDouble(object v) => v == null || v is DBNull ? 0 : Convert.ToDouble(v);

        // ---- OuterDiameter (from PlantTools.cs)
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

        // ---- WallOf (from PlantTools.cs)
        // Wall thickness: the property if present, else the second number of "159x5п".
        static double WallOf(SpecPart p)
        {
            double w = ToDouble(Prop(p, "WallThickness"));
            if (w > 0) return w;
            var m = SizeInDesc(p);
            return m != null ? double.Parse(m.Groups[2].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture) : 0;
        }

        // ---- SizeInDesc (from PlantTools.cs)
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

        // ---- Prop (from PlantTools.cs)
        // PropValue throws for a property the part does not have; treat that as null.
        static object Prop(PartSizeProperties p, string name)
        {
            foreach (string n in p.PropNames) if (n == name) return p.PropValue(name);
            return null;
        }

        // ---- LoadSpecKey (from PlantTools.cs)
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

        // ---- ValveRx (from SupportTools.cs)
        static readonly Regex ValveRx = new Regex(@"Задвижк|Клапан|Затвор|Кран|Вентиль|Valve", RegexOptions.IgnoreCase);

        // ---- FlangeRx (from SupportTools.cs)
        static readonly Regex FlangeRx = new Regex(@"Фланец|Flange", RegexOptions.IgnoreCase);

        // ---- DescById (from SupportTools.cs)
        static string DescById(Document doc, DataLinksManager dlm, ObjectId id)
        {
            int r = RowId(dlm, id);
            if (r <= 0) return "";
            var p = Props(dlm, r);
            return (p.TryGetValue("PartFamilyLongDesc", out var d) ? d : "") + " " + (p.TryGetValue("PartSizeLongDesc", out var s) ? s : "");
        }

        // ---- ValveAtEnd (from SupportTools.cs)
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

        // ---- ReadSPipe (from SupportTools.cs)
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

        // ---- PartPosition (from SupportTools.cs)
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

        // ---- SetLayer (from SupportTools.cs)
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

        // ---- ConnList (from ValveBypassTools.cs)
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

        // ---- OtherOf (from ValveBypassTools.cs)
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
    }
}
