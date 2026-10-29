using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.P3dProjectParts;
using Autodesk.ProcessPower.PnP3dObjects;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

namespace Plant3dMcp
{
    // Ports, deletion, connections and multi-segment pipelines (pipes + elbows). All write operations are two-phase.
    static partial class PlantTools
    {
        static Document ActiveDocument() => AcApp.DocumentManager.MdiActiveDocument ?? throw new InvalidOperationException("No active document");

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

        static bool IsDry(JsonElement args) =>
            !(args.ValueKind == JsonValueKind.Object && args.TryGetProperty("dry_run", out var dr) && dr.ValueKind == JsonValueKind.False);

        static double[] Arr(Point3d p) => new[] { Math.Round(p.X, 3), Math.Round(p.Y, 3), Math.Round(p.Z, 3) };
        static double[] Arr(Vector3d v) => new[] { Math.Round(v.X, 4), Math.Round(v.Y, 4), Math.Round(v.Z, 4) };
        static string Fmt(Point3d p) => "(" + Math.Round(p.X, 1) + "," + Math.Round(p.Y, 1) + "," + Math.Round(p.Z, 1) + ")";
        static string Fmt(Vector3d v) => "(" + Math.Round(v.X, 2) + "," + Math.Round(v.Y, 2) + "," + Math.Round(v.Z, 2) + ")";

        // ---- get_ports -------------------------------------------------------------------------------------------

        public static object GetPorts(JsonElement args)
        {
            var doc = ActiveDocument();
            var id = IdFromHandle(doc.Database, args.GetProperty("handle").GetString());
            return ReadPorts(doc, id);
        }

        static object ReadPorts(Document doc, ObjectId id)
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var part = tr.GetObject(id, OpenMode.ForRead) as Part ?? throw new InvalidOperationException("Object is not a Plant 3D part");
                var ports = part.GetPorts(PortType.All);
                var list = new List<object>();
                for (int i = 0; i < ports.Count; i++)
                {
                    var p = ports[i];
                    list.Add(new { name = p.Name, position = Arr(p.Position), direction = Arr(p.Direction), endType = p.EndType });
                }
                tr.Commit();
                return new { handle = id.Handle.ToString(), cls = id.ObjectClass.Name, position = Arr(part.Position), ports = list };
            }
        }

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

        // Everything the API will tell about a placed part; used to learn the conventions when a check fails.
        static string DescribePart(Document doc, ObjectId id)
        {
            var sb = new System.Text.StringBuilder();
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                sb.Append(id.ObjectClass.Name);
                try { var x = ent.GeometricExtents; sb.Append(" extents=" + Fmt(x.MinPoint) + ".." + Fmt(x.MaxPoint)); } catch (Exception e) { sb.Append(" extents:" + e.GetType().Name); }
                if (ent is Part part)
                {
                    try { sb.Append(" pos=" + Fmt(part.Position)); } catch { }
                    foreach (PortType t in Enum.GetValues(typeof(PortType)))
                    {
                        try
                        {
                            var ports = part.GetPorts(t);
                            sb.Append(" " + t + "=" + ports.Count);
                            for (int i = 0; i < ports.Count && i < 4; i++) sb.Append("{" + ports[i].Name + Fmt(ports[i].Position) + "/" + Fmt(ports[i].Direction) + "}");
                        }
                        catch (Exception e) { sb.Append(" " + t + ":" + e.GetType().Name); }
                    }
                    try { var psp = part.PartSizeProperties; sb.Append(" specPorts=[" + string.Join(",", psp.PortNames.Cast<string>()) + "] count=" + psp.PortCount); } catch (Exception e) { sb.Append(" psp:" + e.GetType().Name); }
                }
                tr.Commit();
            }
            return sb.ToString();
        }

        // ---- get_connections (read-only) -------------------------------------------------------------------------

        // The connections of a part or connector: which object and port it is joined to.
        public static object GetConnections(JsonElement args)
        {
            var doc = ActiveDocument();
            var id = IdFromHandle(doc.Database, args.GetProperty("handle").GetString());
            return ReadConnections(doc, id);
        }

        static List<object> ReadConnections(Document doc, ObjectId id)
        {
            var list = new List<object>();
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            using (var cm = new ConnectionManager())
            {
                var cons = cm.GetConnections(id);
                for (int i = 0; i < cons.Count; i++)
                {
                    var c = cons[i];
                    string Hd(ObjectId o) { try { return o.IsNull ? null : o.Handle.ToString(); } catch (Exception) { return null; } }
                    string Pn(Func<Port> g) { try { return g()?.Name; } catch (Exception) { return null; } }
                    list.Add(new { object1 = Hd(c.ObjectId1), port1 = Pn(() => c.Port1), object2 = Hd(c.ObjectId2), port2 = Pn(() => c.Port2), external = SafeBool(() => c.IsExternal) });
                }
                tr.Commit();
            }
            return list;
        }

        static bool SafeBool(Func<bool> f) { try { return f(); } catch (Exception) { return false; } }

        // ---- inspect_connector (read-only) -----------------------------------------------------------------------

        // Everything the API shows about a joint connector (gasket, bolts, weld...), to reproduce what Plant 3D itself builds.
        public static object InspectConnector(JsonElement args)
        {
            var doc = ActiveDocument();
            var id = IdFromHandle(doc.Database, args.GetProperty("handle").GetString());
            var dlm = Dlm();
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var c = tr.GetObject(id, OpenMode.ForRead) as Connector ?? throw new InvalidOperationException("Object is not a connector");
                var subs = new List<object>();
                var all = c.AllSubParts;
                for (int i = 0; i < c.NumSubParts; i++)
                {
                    var sp = c.GetSubPart(i);
                    var info = new Dictionary<string, object> { ["index"] = i, ["type"] = sp.GetType().Name };
                    try { info["rotation"] = sp.Rotation; info["mirror"] = sp.MirrorFlag; } catch (Exception) { }
                    try { info["persistent"] = sp.IsPersistent; if (!sp.Id.IsNull) { info["objectId"] = sp.Id.Handle.ToString(); info["objectClass"] = sp.Id.ObjectClass.Name; } } catch (Exception e) { info["idError"] = e.GetType().Name; }
                    try
                    {
                        var ports = sp.GetPorts(PortType.All);
                        info["ports"] = Enumerable.Range(0, ports.Count).Select(k => ports[k].Name + Fmt(ports[k].Position) + "/" + Fmt(ports[k].Direction)).ToList();
                    }
                    catch (Exception e) { info["portsError"] = e.GetType().Name; }
                    if (sp is BoltSetSubPart bs) { info["boltLength"] = bs.Length; info["boltOffset"] = bs.Offset; }
                    if (sp is BlockSubPart bk) { try { info["symbolId"] = bk.SymbolId.IsNull ? null : bk.SymbolId.Handle.ToString(); } catch (Exception) { } }
                    if (sp is WeldSubPart ws) info["weldWidth"] = ws.Width;
                    try
                    {
                        var psp = sp.PartSizeProperties;
                        var p = new Dictionary<string, object> { ["name"] = psp.Name, ["type"] = psp.Type, ["spec"] = psp.Spec, ["domain"] = psp.Domain, ["partId"] = psp.PartId, ["guid"] = psp.PartGuid, ["nominalDiameter"] = psp.NominalDiameter.ToString() };
                        var props = new Dictionary<string, string>();
                        foreach (string n in psp.PropNames) { try { var v = psp.PropValue(n); if (v != null && !(v is DBNull) && v.ToString() != "") props[n] = v.ToString(); } catch (Exception) { } }
                        p["props"] = props;
                        info["partSizeProperties"] = p;
                    }
                    catch (Exception e) { info["partSizePropertiesError"] = e.GetType().Name + ": " + e.Message; }
                    subs.Add(info);
                }
                var ports0 = c.GetPorts(PortType.All);
                var result = new Dictionary<string, object>
                {
                    ["handle"] = id.Handle.ToString(), ["position"] = Arr(c.Position), ["numSubParts"] = c.NumSubParts,
                    ["xAxis"] = Arr(c.XAxis), ["zAxis"] = Arr(c.ZAxis), ["slopeTolerance"] = c.SlopeTolerance, ["offsetTolerance"] = c.OffsetTolerance,
                    ["ports"] = Enumerable.Range(0, ports0.Count).Select(k => ports0[k].Name + Fmt(ports0[k].Position) + "/" + Fmt(ports0[k].Direction)).ToList(),
                    ["subParts"] = subs
                };
                try { var rp = Props(dlm, RowId(dlm, id)); result["data"] = rp.Where(kv => !string.IsNullOrEmpty(kv.Value) && !kv.Key.StartsWith("PnP")).ToDictionary(kv => kv.Key, kv => kv.Value); } catch (Exception) { }
                tr.Commit();
                return result;
            }
        }

        // ---- connect_parts ---------------------------------------------------------------------------------------

        // Connects the two closest ports of two parts (they must coincide within 1 mm) with Plant 3D's own connection manager.
        public static object ConnectParts(JsonElement args)
        {
            var doc = ActiveDocument();
            var a = IdFromHandle(doc.Database, args.GetProperty("handleA").GetString());
            var b = IdFromHandle(doc.Database, args.GetProperty("handleB").GetString());
            if (a == b) throw new ArgumentException("handleA and handleB are the same object");
            bool dry = IsDry(args);
            string portA = args.TryGetProperty("portA", out var pa) && pa.ValueKind == JsonValueKind.String ? pa.GetString() : null;   // fixes the port of the first object (a connector has two at one point)
            var r = ConnectCore(doc, a, b, !dry, portA);
            return dry ? (object)new { dry_run = true, result = r, note = "Nothing was changed. Repeat with dry_run=false to connect." } : r;
        }

        // really=false only inspects (objects opened for read); really=true connects and throws if the result is not connected.
        // portNameA (optional) fixes the port of A to use (a connector has two ports at nearly the same place); the port of B is the nearest to it.
        static object ConnectCore(Document doc, ObjectId idA, ObjectId idB, bool really, string portNameA = null)
        {
            var mode = really ? OpenMode.ForWrite : OpenMode.ForRead;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var a = tr.GetObject(idA, mode) as Part ?? throw new InvalidOperationException("First object is not a Plant 3D part");
                var b = tr.GetObject(idB, mode) as Part ?? throw new InvalidOperationException("Second object is not a Plant 3D part");
                var pa = a.GetPorts(PortType.All);
                var pb = b.GetPorts(PortType.All);
                if (pa.Count == 0 || pb.Count == 0)
                    throw new InvalidOperationException("A part has no ports (" + idA.Handle + ": " + pa.Count + ", " + idB.Handle + ": " + pb.Count + ")");
                double best = double.MaxValue; int ia = -1, ib = -1;
                for (int i = 0; i < pa.Count; i++)
                {
                    if (portNameA != null && pa[i].Name != portNameA) continue;
                    for (int j = 0; j < pb.Count; j++)
                    {
                        double d = pa[i].Position.DistanceTo(pb[j].Position);
                        if (d < best) { best = d; ia = i; ib = j; }
                    }
                }
                if (ia < 0) throw new InvalidOperationException("Part " + idA.Handle + " has no port named '" + portNameA + "'");
                if (best > 1.0)
                    throw new InvalidOperationException("The closest ports are " + Math.Round(best, 2) + " mm apart; they must coincide to be connected. " +
                        idA.Handle + " (" + idA.ObjectClass.Name + ") ports: " + string.Join(" ", Enumerable.Range(0, pa.Count).Select(i => pa[i].Name + Fmt(pa[i].Position) + (pa[i].IsDynamic ? "[dyn]" : ""))) + " | " +
                        idB.Handle + " (" + idB.ObjectClass.Name + ") ports: " + string.Join(" ", Enumerable.Range(0, pb.Count).Select(i => pb[i].Name + Fmt(pb[i].Position) + (pb[i].IsDynamic ? "[dyn]" : ""))));

                var pairA = new Pair(); pairA.ObjectId = idA; pairA.Port = pa[ia];
                var pairB = new Pair(); pairB.ObjectId = idB; pairB.Port = pb[ib];
                string portA = pa[ia].Name, portB = pb[ib].Name;
                bool can, connected = false;
                using (var cm = new ConnectionManager())
                {
                    can = cm.CanConnect(pairA, pairB);
                    if (can && really) { cm.Connect(pairA, pairB); connected = cm.IsConnected(pairA); }
                }
                if (really && !connected)
                    throw new InvalidOperationException("Ports " + idA.Handle + "." + portA + " and " + idB.Handle + "." + portB + " were not connected (CanConnect=" + can + ")");
                tr.Commit();
                return new { handleA = idA.Handle.ToString(), portA, handleB = idB.Handle.ToString(), portB, distance = Math.Round(best, 3), can_connect = can, connected };
            }
        }

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

        // ---- delete_objects --------------------------------------------------------------------------------------

        public static object DeleteObjects(JsonElement args)
        {
            var doc = ActiveDocument();
            // A dry run names the drawing; the real call may echo it so that switching drawings in between cannot redirect the delete.
            if (args.TryGetProperty("document", out var dn) && dn.ValueKind == JsonValueKind.String && !string.Equals(dn.GetString(), doc.Name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The active drawing is '" + doc.Name + "', not '" + dn.GetString() + "'; nothing was deleted");
            var hs = args.GetProperty("handles").EnumerateArray().Select(e => e.GetString()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (hs.Count == 0 || hs.Count > 50) throw new ArgumentException("handles must contain 1..50 distinct handles");
            var dlm = Dlm();
            var plan = new List<object>();
            var ids = new List<ObjectId>();
            var msId = SymbolUtilityServices.GetBlockModelSpaceId(doc.Database);
            foreach (var h in hs)
            {
                var id = IdFromHandle(doc.Database, h);
                if (id.IsErased) throw new InvalidOperationException("Handle " + h + " is already erased");
                if (id.IsNull || id.Database != doc.Database) throw new InvalidOperationException("Handle " + h + " does not belong to the active drawing");
                int row = RowId(dlm, id);
                if (row <= 0) throw new InvalidOperationException("Handle " + h + " is not a Plant 3D object; refusing to delete it");
                using (var tr0 = doc.Database.TransactionManager.StartTransaction())
                {
                    // Checked while planning so the dry run reports it too (and nothing is half-erased later).
                    if (((Entity)tr0.GetObject(id, OpenMode.ForRead)).OwnerId != msId) throw new InvalidOperationException("Handle " + h + " is not in model space; refusing to delete it");
                    tr0.Commit();
                }
                var props = Props(dlm, row);
                props.TryGetValue("PartSizeLongDesc", out var desc);
                plan.Add(new { handle = h, cls = id.ObjectClass.Name, rowId = row, description = desc, connections = ConnectionCount(doc, id) });
                ids.Add(id);
            }
            if (IsDry(args))
                return new { dry_run = true, document = doc.Name, will_delete = plan, note = "Nothing was changed. Repeat with dry_run=false (optionally echo \"document\") to delete. 'connections' shows neighbours that will lose a joint." };

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                    if (ent.OwnerId != msId) throw new InvalidOperationException("Handle " + id.Handle + " is not in model space; nothing was deleted");
                    ent.Erase();
                }
                tr.Commit();
            }
            return new { deleted = plan };
        }

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

        // ---- helpers for pipelines -------------------------------------------------------------------------------

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

        static int AddPipe(PipingObjectAdder adder, SpecPart part, Point3d a, Point3d b, double od)
        {
            var pipe = new Pipe();
            pipe.StartPoint = a;
            pipe.EndPoint = b;
            pipe.OuterDiameter = od;
            return adder.Add(part, pipe);
        }

        static double ElbowAngle(SpecPart p, string desc)
        {
            double a = ToDouble(Prop(p, "PathAngle"));
            if (a > 0) return a;
            // "Отвод 90-159x5": the number right after the word, not followed by an "x" size separator.
            // The number must end where the digits end (no backtracking into "159" -> "15") and must not be a size like "159x5".
            var m = Regex.Match(desc ?? "", @"Отвод\s+(\d+(?:[.,]\d+)?)(?![\d.,]|\s*[xхХ×])", RegexOptions.IgnoreCase);
            if (m.Success) return double.Parse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
            m = Regex.Match(Prop(p, "ContentGeometryParamDefinition")?.ToString() ?? "", @"\bA=(\d+(?:\.\d+)?)");
            if (m.Success) return double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            throw new InvalidOperationException("Cannot determine elbow angle. Properties: " + string.Join(", ", p.PropNames.Cast<string>()));
        }

        static double ElbowRadius(SpecPart p)
        {
            double r = ToDouble(Prop(p, "CurveRadius"));
            if (r > 0) return r;
            var m = Regex.Match(Prop(p, "ContentGeometryParamDefinition")?.ToString() ?? "", @"\bR=(\d+(?:\.\d+)?)");
            if (m.Success) return double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            throw new InvalidOperationException("Cannot determine elbow radius. Properties: " + string.Join(", ", p.PropNames.Cast<string>()));
        }

        sealed class ElbowPlan
        {
            public Point3d Vertex, TIn, TOut;
            public Vector3d Din, Dout;
            public double Angle, Radius, Tangent;
            public SpecPart Part;
            public string Desc;
        }

        static bool Near(Point3d a, Point3d b, double tol = 0.5) => a.DistanceTo(b) <= tol;

        // Port directions point away from the part toward the neighbouring pipe (verified on a live elbow).
        static bool Aligned(Vector3d d, Vector3d expected) =>
            d.Length > 1e-9 && d.GetNormal().DotProduct(expected) > Math.Cos(2.0 * Math.PI / 180.0);

        // The elbow fits when its two ports sit on the tangent points AND point away from the bend, i.e. the arc
        // bulges toward the vertex (a mirrored placement has the same port positions but wrong directions).
        static bool PortsFit(List<(string name, Point3d pos, Vector3d dir)> ports, ElbowPlan e)
        {
            if (ports.Count != 2) return false;
            for (int k = 0; k < 2; k++)
            {
                var a = ports[k]; var b = ports[1 - k];
                if (Near(a.pos, e.TIn) && Near(b.pos, e.TOut) && Aligned(a.dir, e.Din.Negate()) && Aligned(b.dir, e.Dout)) return true;
            }
            return false;
        }

        // Places an elbow so that its two ports coincide with TIn/TOut. The local geometry is measured from a trial
        // placement (so no assumption about the part's origin or axes is needed) and the part is then re-placed.
        // Every row added here is recorded in 'added' immediately, so the caller's rollback sees it even if a later
        // step throws. Returns the row id of the final elbow.
        static int PlaceElbow(Document doc, PipingObjectAdder adder, DataLinksManager dlm, ElbowPlan e, List<string> log, HashSet<int> added)
        {
            var normal = e.Din.CrossProduct(e.Dout).GetNormal();
            var y0 = normal.CrossProduct(e.Din).GetNormal();

            int AddAt(Point3d pos, Vector3d x, Vector3d z)
            {
                var asset = new PipeInlineAsset();
                // The 3D body is a block "symbol" built by the content manager; without it the asset is an empty record with no ports.
                asset.SymbolId = ContentManager.GetContentManager().GetSymbol(e.Part, doc.Database);
                asset.Position = pos;
                if (!asset.SetOrientation(x, z)) throw new InvalidOperationException("SetOrientation rejected the axes");
                int row = adder.Add(e.Part, asset);
                added.Add(row);
                return row;
            }

            // Trial: origin at the vertex, X along the incoming direction, Z along the bend normal.
            int row1 = AddAt(e.Vertex, e.Din, normal);
            var id1 = FindByRow(doc, dlm, row1);
            var ports1 = PortsOf(doc, id1);
            log.Add("trial ports: " + string.Join(" ", ports1.Select(p => p.name + Fmt(p.pos) + "/" + Fmt(p.dir))));
            if (PortsFit(ports1, e)) return row1;

            string described = ports1.Count != 2 ? DescribePart(doc, id1) : null;
            EraseIds(doc, new[] { id1 });   // the trial is gone before anything below can throw
            if (ports1.Count != 2)
                throw new InvalidOperationException("Elbow has " + ports1.Count + " ports, expected 2. Trial pose: pos=" + Fmt(e.Vertex) + " x=" + Fmt(e.Din) + " z=" + Fmt(normal) + ". Placed part: " + described);

            // Local geometry (coordinates in the trial frame) of the two ports.
            var toWorld = Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, e.Vertex, e.Din, y0, normal);
            var toLocal = toWorld.Inverse();
            Point3d L0 = ports1[0].pos.TransformBy(toLocal), L1 = ports1[1].pos.TransformBy(toLocal);
            Vector3d D0 = ports1[0].dir.TransformBy(toLocal), D1 = ports1[1].dir.TransformBy(toLocal);

            // Expected world directions: outward from the elbow toward the neighbouring pipes.
            Vector3d W0 = e.Din.Negate(), W1 = e.Dout;
            var l1 = L1 - L0; var w1 = e.TOut - e.TIn;
            if (l1.Length < 1e-6 || Math.Abs(l1.Length - w1.Length) > 1.0)
                throw new InvalidOperationException("Elbow port spacing " + Math.Round(l1.Length, 2) + " does not match the expected " + Math.Round(w1.Length, 2) +
                    " mm (wrong radius or port positions are not world coordinates); " + string.Join(" | ", log));
            var cross = D0.CrossProduct(D1);
            if (cross.Length < 1e-6) throw new InvalidOperationException("Elbow port directions are parallel; cannot derive its frame; " + string.Join(" | ", log));
            var el1 = l1.GetNormal(); var el3 = cross.GetNormal(); var el2 = el3.CrossProduct(el1);
            var wf1 = w1.GetNormal(); var wf3 = W0.CrossProduct(W1).GetNormal(); var wf2 = wf3.CrossProduct(wf1);
            var rt = Matrix3d.AlignCoordinateSystem(L0, el1, el2, el3, e.TIn, wf1, wf2, wf3);   // trial-local -> world

            // 'rt' maps the asset's own local coordinates (as measured in the trial) to the wanted world position,
            // so the corrected asset ECS is rt itself: origin, X axis and Z axis are its images.
            Point3d pos = Point3d.Origin.TransformBy(rt);
            Vector3d ax = Vector3d.XAxis.TransformBy(rt).GetNormal();
            Vector3d az = Vector3d.ZAxis.TransformBy(rt).GetNormal();
            int row2 = AddAt(pos, ax, az);
            var id2 = FindByRow(doc, dlm, row2);
            var ports2 = PortsOf(doc, id2);
            log.Add("final ports: " + string.Join(" ", ports2.Select(p => p.name + Fmt(p.pos) + "/" + Fmt(p.dir))));
            if (!PortsFit(ports2, e))
            {
                EraseIds(doc, new[] { id2 });
                throw new InvalidOperationException("Elbow could not be placed on its tangent points " + Fmt(e.TIn) + " / " + Fmt(e.TOut) + " with the right orientation; " + string.Join(" | ", log));
            }
            return row2;
        }

        // An elbow whose outer diameter cannot be determined is rejected, not waved through: a 168 mm elbow on a 159 mm pipe is worse than an error.
        static bool OdMatches(SpecPart p, double od)
        {
            try { return Math.Abs(OuterDiameter(p) - od) < 0.5; } catch (Exception) { return false; }
        }

        // ---- create_pipeline -------------------------------------------------------------------------------------

        public static object CreatePipeline(JsonElement args)
        {
            var doc = ActiveDocument();
            string specName = (args.TryGetProperty("spec", out var sp) && sp.ValueKind == JsonValueKind.String ? sp.GetString() : null) ?? DefaultSpec;
            double nd = args.GetProperty("nd").GetDouble();
            double? wall = args.TryGetProperty("wall", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetDouble() : (double?)null;
            var pts = args.GetProperty("points").EnumerateArray().Select(a =>
            {
                var v = a.EnumerateArray().Select(x => x.GetDouble()).ToArray();
                if (v.Length != 3) throw new ArgumentException("each point must be [x,y,z]");
                return new Point3d(v[0], v[1], v[2]);
            }).ToList();
            if (pts.Count < 2 || pts.Count > 50) throw new ArgumentException("points must contain 2..50 vertices");

            var piping = Piping();
            var sm = SpecManager.GetSpecManager();
            string key = LoadSpecKey(sm, piping, specName);

            // Pipe: explicit wall, else the default rule.
            var pipeSel = ChoosePipe(CollectParts(sm, specName, key, "Pipe", nd), wall, key, nd, out string wallRule);
            SpecPart pipePart = pipeSel[0].part;
            double pipeWall = pipeSel[0].wall;
            double od = OuterDiameter(pipePart);

            // Shortest straight pipe the project allows (project setting), never below 1 mm.
            double minLen = 1.0;
            try { minLen = Math.Max(1.0, Convert.ToDouble(piping.MinimumPipeLength)); } catch (Exception) { }

            var dirs = new List<Vector3d>();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var v = pts[i + 1] - pts[i];
                if (v.Length < 1e-6) throw new ArgumentException("points " + i + " and " + (i + 1) + " coincide");
                dirs.Add(v.GetNormal());
            }

            // Elbows at every turn: same wall and outer diameter as the pipe; the tangent length uses the elbow's own
            // angle, and the geometric turn must equal it to 0.05 deg (so pipes stay on the port axes).
            var elbows = new List<ElbowPlan>();
            List<(SpecPart part, double wall, string desc)> elbowCands = pts.Count > 2 ? CollectParts(sm, specName, key, "Elbow", nd) : null;
            for (int i = 1; i + 1 < pts.Count; i++)
            {
                Vector3d din = dirs[i - 1], dout = dirs[i];
                double ang = Math.Acos(Math.Max(-1, Math.Min(1, din.DotProduct(dout)))) * 180 / Math.PI;
                if (ang < 1) throw new ArgumentException("vertex " + i + " is collinear with its neighbours: remove it");
                if (ang > 179) throw new ArgumentException("vertex " + i + " reverses the run (180 deg); no elbow can do that");
                var match = elbowCands.Where(c => Math.Abs(ElbowAngle(c.part, c.desc) - ang) < 0.05 && Math.Abs(c.wall - pipeWall) < 1e-3 && OdMatches(c.part, od)).ToList();
                if (match.Count != 1)
                    throw new InvalidOperationException("Need exactly one elbow of " + Math.Round(ang, 2) + " deg (tolerance 0.05), wall " + pipeWall + " mm, OD " + od + " in the spec, found " + match.Count +
                        ". Available: " + string.Join(" | ", elbowCands.Select(c => c.desc)));
                double partAngle = ElbowAngle(match[0].part, match[0].desc);
                double r = ElbowRadius(match[0].part);
                double t = r * Math.Tan(partAngle * Math.PI / 360.0);
                elbows.Add(new ElbowPlan
                {
                    Vertex = pts[i], Din = din, Dout = dout, Angle = partAngle, Radius = r, Tangent = t, Part = match[0].part, Desc = match[0].desc,
                    TIn = pts[i] - din * t, TOut = pts[i] + dout * t
                });
            }

            // GOST 32569 cl. 6.8.1: a straight pipe between two circumferential butt welds is at least max(3*s, 100/250/400 mm by OD).
            // Only an explicit relax_weld_spacing=true (a justified case) lowers this to the project minimum.
            double weldMin = DesignRules.WeldSpacingMin(od, pipeWall);
            bool relax = args.TryGetProperty("relax_weld_spacing", out var rw) && rw.ValueKind == JsonValueKind.True;
            double segMin = relax ? minLen : Math.Max(minLen, weldMin);
            var notes = new List<string>();
            if (relax) notes.Add("relax_weld_spacing=true: segments are only held to the project minimum pipe length (" + minLen + " mm), not to GOST 32569 cl. 6.8.1 (" + Math.Round(weldMin, 1) + " mm). Record the justification.");

            var pipes = new List<(Point3d a, Point3d b)>();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Point3d a = pts[i] + dirs[i] * (i > 0 ? elbows[i - 1].Tangent : 0);
                Point3d b = pts[i + 1] - dirs[i] * (i + 2 < pts.Count ? elbows[i].Tangent : 0);
                double len = (b - a).DotProduct(dirs[i]);
                if (len < segMin)
                    throw new InvalidOperationException("Segment " + i + " would be " + Math.Round(len, 1) + " mm long after the elbows; the minimum is " + Math.Round(segMin, 1) + " mm" +
                        (segMin > minLen ? " (GOST 32569 cl. 6.8.1: not less than 3*wall and not less than " + (od <= 219.05 ? 100 : od <= 550.05 ? 250 : 400) + " mm between butt welds; pass relax_weld_spacing=true only for a justified case)" : " (project minimum pipe length)"));
                pipes.Add((a, b));
                var sl = DesignRules.SlopeNote("Pipe " + i, DesignRules.Slope(a, b));
                if (sl != null) notes.Add(sl);
            }
            foreach (var e in elbows)
                if (e.Radius > DesignRules.SteepElbowRadiusFactor * nd + 0.5)
                    notes.Add("Elbow at " + Fmt(e.Vertex) + " has a bend radius " + e.Radius + " mm > 1.5*DN: a bent (not steep) elbow; GOST 32569 cl. 6.4.1-6.4.2 allows it only for minimum hydraulic resistance or DN<=25.");
            notes.AddRange(DesignRules.VentDrainNotes(pts, nd));

            var plan = new
            {
                design_notes = notes,
                spec = key, nd, od, wall = pipeWall, wall_rule = wallRule ?? "only pipe of this size in the spec",
                pipe = pipeSel[0].desc,
                pipes = pipes.Select((p, i) => new { index = i, start = Arr(p.a), end = Arr(p.b), length = Math.Round(p.a.DistanceTo(p.b), 1) }).ToList(),
                elbows = elbows.Select(e => new { vertex = Arr(e.Vertex), angle = Math.Round(e.Angle, 1), radius = e.Radius, tangent = Math.Round(e.Tangent, 1), description = e.Desc, ports = new[] { Arr(e.TIn), Arr(e.TOut) } }).ToList()
            };
            if (IsDry(args)) return new { dry_run = true, document = doc.Name, plan, note = "Nothing was changed. Repeat with dry_run=false to create the pipeline." };

            var dlm = Dlm();
            var before = ModelRows(doc, dlm);        // rows that existed before this call
            var added = new HashSet<int>();          // every row this call has added, for rollback
            var order = new List<(string kind, int row)>();
            var log = new List<string>();
            var ids = new List<ObjectId>();
            try
            {
                // Lock first, adder inside: the adder is disposed while the lock is still held.
                using (doc.LockDocument())
                using (var adder = new PipingObjectAdder(piping.DataLinksManager3d, doc.Database))
                {
                    foreach (var e in elbows) order.Add(("elbow", PlaceElbow(doc, adder, dlm, e, log, added)));
                    foreach (var p in pipes)
                    {
                        int row = AddPipe(adder, pipePart, p.a, p.b, od);
                        added.Add(row);
                        order.Add(("pipe", row));
                    }
                }
                foreach (var o in order) ids.Add(FindByRow(doc, dlm, o.row));

                // Join neighbours at the elbow ports: pipe i - elbow i - pipe i+1. A run with an open joint is not a run: roll back.
                var joints = new List<object>();
                var failed = new List<string>();
                for (int i = 0; i < elbows.Count; i++)
                {
                    var elbowId = ids[i];
                    foreach (var pipeId in new[] { ids[elbows.Count + i], ids[elbows.Count + i + 1] })
                    {
                        try { joints.Add(ConnectCore(doc, pipeId, elbowId, true)); }
                        catch (Exception ex) { failed.Add(pipeId.Handle + "<->" + elbowId.Handle + ": " + ex.Message); }
                    }
                }
                if (failed.Count > 0) throw new InvalidOperationException("joints not connected: " + string.Join("; ", failed));

                var objects = ids.Select((id, i) => new { kind = order[i].kind, handle = id.Handle.ToString(), rowId = order[i].row, connections = ConnectionCount(doc, id) }).ToList();
                // Expected joints per object (count semantics confirmed live: an elbow 2, inner pipe 2, end pipe 1):
                // an elbow always joins two pipes; pipe i touches an elbow at each end that is not a run end.
                int pipeCount = pipes.Count;
                var bad = objects.Select((o, i) => new { o, expected = o.kind == "elbow" ? 2 : ((i - elbows.Count > 0 ? 1 : 0) + (i - elbows.Count < pipeCount - 1 ? 1 : 0)) })
                                 .Where(x => x.o.connections >= 0 && x.o.connections != x.expected)
                                 .Select(x => new { x.o.handle, connections = x.o.connections, x.expected }).ToList();
                if (bad.Count > 0) throw new InvalidOperationException("connection check failed: " + string.Join(", ", bad.Select(b => b.handle + " has " + b.connections + ", expected " + b.expected)));
                return new { plan, objects, joints, log, note = "'connections' is the number of connections of each object (-1 = could not be read). Elbows and inner pipes have 2, end pipes 1." };
            }
            catch (Exception ex)
            {
                // Leave the drawing as it was: erase every row this call added, and every Plant row that did not exist before the call.
                string rb = RollbackSince(doc, dlm, added, before);
                throw new InvalidOperationException("Pipeline failed and was rolled back (" + rb + "): " + ex.Message + (log.Count > 0 ? " | " + string.Join(" | ", log) : ""));
            }
        }
    }
}
