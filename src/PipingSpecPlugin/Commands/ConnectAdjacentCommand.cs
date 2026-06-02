using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PnP3dObjects;
using PipingSpecPlugin.Settings;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace PipingSpecPlugin.Commands
{
    /// <summary>
    /// CONNECTALL — bulk version of Plant 3D's native "connect to adjacent"
    /// (PLANTCONNECTTOADJACENT). Walks every Plant 3D part in the active
    /// drawing, and for every free port that physically coincides with a
    /// free port of another part, creates a P3dPartConnection between them.
    ///
    /// Why this exists: the spec generator builds its topology from
    /// P3dPartConnection edges (see PartReader.BuildTopology). Parts that
    /// the modeller dropped next to each other but never explicitly
    /// connected have no edge — so bend-fold and valve-companion discovery
    /// miss them. Running CONNECTALL once wires the whole sheet up so the
    /// spec sees the real network.
    ///
    /// Mutating command: single document lock + one transaction so the
    /// entire pass reverts under a single Undo, with per-port try/catch so
    /// one bad joint never aborts the run. Mirrors MOVEBYTAG's structure.
    ///
    /// IMPORTANT lifetime rule: Port / PortCollection are DisposableWrapper
    /// objects over native memory tied to the GetPorts call that produced
    /// them. We must NOT cache a live Port across the scan and reuse it
    /// later — by then its backing collection is gone and Connect() would
    /// touch freed memory (AutoCAD hard-crash, not a catchable exception).
    /// So pass 1 records only value data (position, port name, entity id),
    /// and pass 2 re-fetches the live Port by name immediately before
    /// Connect, inside the same transaction.
    /// </summary>
    public class ConnectAdjacentCommand
    {
        // Coincident (weld / socket) ports sit at exactly the same point;
        // this absorbs floating-point noise from the modelling math.
        const double PositionTolerance = 0.5;

        // Flanged joints don't put two ports at the same point: the flange
        // faces are separated by the gasket/weld gap, and Plant 3D models
        // that gap as each port's EngagementLength. Two ports mate when they
        // face each other and the gap between them is within the sum of their
        // engagement lengths plus this slack. The native "connect to
        // adjacent" uses the same engagement-based test.
        const double EngagementSlack = 1.0;

        // Two port directions count as opposing when the cosine of the angle
        // between them is below this (i.e. > ~154°). Mating ports point into
        // each other, so their unit directions are nearly antiparallel.
        const double OpposingDirCos = -0.9;

        // Grid cell size. Must be ≥ the largest gap we'll bridge so a mating
        // pair never lands more than one cell apart. Flange engagement is
        // tens of mm at most for large bore; 200 mm is a safe ceiling and
        // still keeps buckets small on real drawings.
        const double CellSize = 200.0;

        [CommandMethod("CONNECTALL")]
        public void Run()
        {
            using var logger = new RunLogger();

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                WinForms.MessageBox.Show(
                    "Нет открытого чертежа. Откройте чертёж Plant 3D перед запуском.",
                    "Соединить смежные",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            var ed = doc.Editor;
            void Say(string line)
            {
                logger.Log(line);
                try { ed.WriteMessage("\n" + line); } catch { }
            }
            void Trace(string line) => logger.Log(line);

            Say($"[Соединить смежные] Запуск, лог: {logger.Path}");

            var db = doc.Database;

            // Bail early on a non-Plant-3D drawing, same as MOVEBYTAG — a
            // ConnectionManager would still construct on a plain DWG and the
            // pass would silently find nothing, leaving the user puzzled.
            DataLinksManager dlm;
            try { dlm = DataLinksManager.GetManager(db); }
            catch (System.Exception ex)
            {
                Say("[Соединить смежные] Не удалось получить DataLinksManager: " + ex.Message);
                return;
            }
            if (dlm == null)
            {
                const string msg = "Активный чертёж не содержит данных Plant 3D.";
                Say("[Соединить смежные] " + msg);
                WinForms.MessageBox.Show(msg, "Соединить смежные",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            ConnectionManager cm;
            try { cm = new ConnectionManager(); }
            catch (System.Exception ex)
            {
                Say("[Соединить смежные] Не удалось создать ConnectionManager: " + ex.Message);
                return;
            }

            int connectionsMade = 0;
            int partsScanned = 0;
            int freePorts = 0;
            int skippedError = 0;
            string failReason = null;   // set when the pass aborts mid-way

            // Lock the document — creating connections writes to the DWG, so
            // without the lock we'd hit eLockViolation. One transaction for
            // the whole pass keeps it atomic under a single Undo.
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                    if (msId.IsNull)
                    {
                        failReason = "Не найдено пространство модели.";
                    }
                    else
                    {
                        var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

                        var grid = CollectFreePorts(tr, ms, cm,
                            ref partsScanned, ref freePorts, ref skippedError, Trace);
                        Trace($"Деталей: {partsScanned}; свободных портов: {freePorts}.");

                        connectionsMade = ConnectPass(tr, cm, grid,
                            ref skippedError, Trace);
                    }

                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    failReason = ex.Message;
                    Trace(ex.StackTrace ?? "");
                    try { tr.Abort(); } catch { }
                }
            }

            if (failReason != null)
            {
                Say("[Соединить смежные] Ошибка: " + failReason);
                WinForms.MessageBox.Show(
                    "Не удалось выполнить операцию:\n\n" + failReason +
                    "\n\nПодробности в логе:\n" + logger.Path,
                    "Соединить смежные",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
                return;
            }

            var summary =
                $"Создано соединений: {connectionsMade}. "
              + $"Деталей просмотрено: {partsScanned}, свободных портов: {freePorts}."
              + (skippedError > 0 ? $" Ошибок: {skippedError} (см. лог)." : "");
            Say("[Соединить смежные] " + summary);
            WinForms.MessageBox.Show(summary, "Соединить смежные",
                WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
        }

        // ---- pass 1: index every free port by position --------------------

        /// <summary>
        /// Walk model space and record every free, physical port into a
        /// spatial grid keyed by rounded position. Records value data only
        /// (see the lifetime rule on the class) — never a live Port.
        /// </summary>
        static Dictionary<(long, long, long), List<PortRef>> CollectFreePorts(
            Transaction tr, BlockTableRecord ms, ConnectionManager cm,
            ref int partsScanned, ref int freePorts, ref int skippedError,
            Action<string> trace)
        {
            var grid = new Dictionary<(long, long, long), List<PortRef>>();

            foreach (ObjectId entId in ms)
            {
                // Fast filter: only Plant 3D entities (class "AcPp*").
                var className = entId.ObjectClass?.Name;
                if (className == null
                    || !className.StartsWith("AcPp", StringComparison.OrdinalIgnoreCase))
                    continue;

                Part part;
                try { part = tr.GetObject(entId, OpenMode.ForRead) as Part; }
                catch { continue; }
                if (part == null) continue;

                partsScanned++;

                PortCollection ports;
                try { ports = part.GetPorts(PortType.Both); }
                catch (System.Exception ex)
                {
                    trace($"GetPorts не удался ({className}): {ex.GetType().Name}: {ex.Message}");
                    skippedError++;
                    continue;
                }
                if (ports == null) continue;

                // PortCollection exposes only an indexer (no Count) — walk
                // indices until it throws, same idiom as ConnectionCollection
                // in PartReader.
                for (int i = 0; ; i++)
                {
                    Port port;
                    try { port = ports[i]; }
                    catch { break; }
                    if (port == null) break;

                    bool isNull = false;
                    try { isNull = port.IsNull; } catch { }
                    if (isNull) continue;

                    // Skip symbolic ports — they don't represent a physical
                    // make-up point and the native connect ignores them too.
                    bool isSymbolic = false;
                    try { isSymbolic = port.IsSymbolic; } catch { }
                    if (isSymbolic) continue;

                    string portName;
                    try { portName = port.Name; } catch { continue; }
                    if (string.IsNullOrEmpty(portName)) continue;  // can't re-fetch later

                    // IsConnected needs a Pair; build a throwaway one here.
                    // The Pair (and the Port it wraps) is discarded with this
                    // loop iteration — only the value data below survives.
                    bool connected = false;
                    try
                    {
                        var probe = new Pair { ObjectId = entId, Port = port };
                        connected = cm.IsConnected(probe);
                    }
                    catch { }
                    if (connected) continue;

                    Point3d pos;
                    try { pos = port.Position; } catch { continue; }

                    // Direction and engagement drive the flange-gap match in
                    // FindMates. Missing direction (zero vector) falls back to
                    // a pure coincidence test there.
                    Vector3d dir = new Vector3d(0, 0, 0);
                    try { dir = port.Direction; } catch { }
                    double engage = 0.0;
                    try { engage = port.EngagementLength; } catch { }

                    freePorts++;
                    var key = CellKey(pos);
                    if (!grid.TryGetValue(key, out var list))
                        grid[key] = list = new List<PortRef>();
                    list.Add(new PortRef
                    {
                        EntId = entId,
                        PortName = portName,
                        Position = pos,
                        Direction = dir,
                        Engagement = engage,
                    });
                }
            }

            return grid;
        }

        // ---- pass 2: connect coincident free ports ------------------------

        /// <summary>
        /// For each free port, gather every OTHER free port at the same point
        /// (a multi-way junction like a tee shows up as 3+ coincident ports)
        /// and connect them all to the anchor port. Re-fetches live Ports by
        /// name right before Connect so no stale wrapper is ever used.
        /// </summary>
        static int ConnectPass(Transaction tr, ConnectionManager cm,
            Dictionary<(long, long, long), List<PortRef>> grid,
            ref int skippedError, Action<string> trace)
        {
            int made = 0;
            var used = new HashSet<PortRef>();

            foreach (var kv in grid)
            {
                foreach (var anchor in kv.Value)
                {
                    if (used.Contains(anchor)) continue;

                    var mates = FindMates(anchor, grid, used);
                    if (mates.Count == 0) continue;

                    // Resolve the anchor's live Port once; reuse for every
                    // mate at this junction.
                    var anchorPort = ResolvePort(tr, anchor, trace);
                    if (anchorPort == null) { skippedError++; continue; }
                    var anchorPair = new Pair { ObjectId = anchor.EntId, Port = anchorPort };

                    bool anchorUsed = false;
                    foreach (var mate in mates)
                    {
                        if (used.Contains(mate)) continue;

                        var matePort = ResolvePort(tr, mate, trace);
                        if (matePort == null) { skippedError++; continue; }
                        var matePair = new Pair { ObjectId = mate.EntId, Port = matePort };

                        bool canConnect = false;
                        try { canConnect = cm.CanConnect(anchorPair, matePair); }
                        catch (System.Exception ex)
                        {
                            trace($"CanConnect бросил: {ex.GetType().Name}: {ex.Message}");
                        }
                        if (!canConnect) continue;

                        try
                        {
                            cm.Connect(anchorPair, matePair);
                            made++;
                            used.Add(mate);
                            anchorUsed = true;
                        }
                        catch (System.Exception ex)
                        {
                            skippedError++;
                            trace($"Connect не удался ({anchor.PortName}↔{mate.PortName}): "
                                + $"{ex.GetType().Name}: {ex.Message}");
                        }
                    }

                    if (anchorUsed) used.Add(anchor);
                }
            }

            return made;
        }

        /// <summary>
        /// Re-open the part for WRITE and return the named live Port. Write
        /// access matters: Connect mutates both parts, and an object opened
        /// only for read makes Connect fail with eNotOpenForWrite. Returns
        /// null (logged) if the part or port can't be resolved.
        /// </summary>
        static Port ResolvePort(Transaction tr, PortRef pref, Action<string> trace)
        {
            Part part;
            try { part = tr.GetObject(pref.EntId, OpenMode.ForWrite) as Part; }
            catch (System.Exception ex)
            {
                trace($"Не удалось открыть деталь на запись ({pref.PortName}): "
                    + $"{ex.GetType().Name}: {ex.Message}");
                return null;
            }
            if (part == null) return null;

            try
            {
                var ports = part.GetPorts(PortType.Both);
                if (ports == null) return null;
                var p = ports[pref.PortName];   // string indexer
                if (p == null || p.IsNull) return null;
                return p;
            }
            catch (System.Exception ex)
            {
                trace($"Не удалось получить порт '{pref.PortName}': "
                    + $"{ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Collect every free port of a DIFFERENT part that mates with
        /// <paramref name="anchor"/>. A port mates when it is either
        /// coincident (weld/socket joints) or facing the anchor across a gap
        /// no larger than the two ports' engagement lengths (flanged joints).
        /// Scans the 3×3×3 grid neighbourhood so a mate one cell away is still
        /// found. Bore/end-type compatibility is left to CanConnect.
        /// </summary>
        static List<PortRef> FindMates(PortRef anchor,
            Dictionary<(long, long, long), List<PortRef>> grid,
            HashSet<PortRef> used)
        {
            var result = new List<PortRef>();
            var (cx, cy, cz) = CellKey(anchor.Position);
            for (long dx = -1; dx <= 1; dx++)
            for (long dy = -1; dy <= 1; dy++)
            for (long dz = -1; dz <= 1; dz++)
            {
                if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var bucket))
                    continue;
                foreach (var b in bucket)
                {
                    if (ReferenceEquals(b, anchor)) continue;
                    if (used.Contains(b)) continue;
                    // A part never connects to itself.
                    if (b.EntId == anchor.EntId) continue;
                    if (Mates(anchor, b)) result.Add(b);
                }
            }
            return result;
        }

        /// <summary>
        /// True when two free ports physically make up. Handles both joint
        /// styles:
        ///   • coincident — positions within PositionTolerance (weld/socket);
        ///   • flanged — ports face each other (directions antiparallel) and
        ///     the centre-to-centre gap is within the sum of engagements.
        /// When either direction is unknown (zero vector) we can only fall
        /// back to the coincidence test.
        /// </summary>
        static bool Mates(PortRef a, PortRef b)
        {
            double gap = a.Position.DistanceTo(b.Position);
            if (gap <= PositionTolerance) return true;

            const double minDirLen = 1e-6;
            if (a.Direction.Length < minDirLen || b.Direction.Length < minDirLen)
                return false;

            // Opposing faces: unit directions roughly antiparallel.
            var ua = a.Direction.GetNormal();
            var ub = b.Direction.GetNormal();
            if (ua.DotProduct(ub) > OpposingDirCos) return false;

            // The ports must actually face across the gap, not sit side by
            // side: the vector from a to b should run along a's outward
            // direction. Reject lateral near-misses (parallel pipes touching).
            var aToB = b.Position - a.Position;
            if (aToB.DotProduct(ua) <= 0) return false;

            return gap <= a.Engagement + b.Engagement + EngagementSlack;
        }

        // Cell size is the max gap we bridge, so any mating pair lands in the
        // same or an adjacent cell — hence FindMates' 3×3×3 scan.
        static (long, long, long) CellKey(Point3d p)
            => ((long)Math.Floor(p.X / CellSize),
                (long)Math.Floor(p.Y / CellSize),
                (long)Math.Floor(p.Z / CellSize));

        sealed class PortRef
        {
            public ObjectId EntId;
            public string PortName;
            public Point3d Position;
            public Vector3d Direction;   // ZeroVector when unknown
            public double Engagement;    // 0 for hard-coincident ports
        }
    }
}
