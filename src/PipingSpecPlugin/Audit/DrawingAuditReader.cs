using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PnP3dObjects;

namespace PipingSpecPlugin.Audit
{
    public static partial class DrawingAuditReader
    {
        public static AuditSnapshot Read(Document document, AuditOptions options, Action<string> progress = null,
            IReadOnlyDictionary<string, AuditPart> savedProperties = null)
        {
            var snapshot = new AuditSnapshot { Drawing = document.Name };
            var database = document.Database;
            DataLinksManager dlm = null;
            string linkError = "В чертеже недоступны данные Plant 3D.";
            try { dlm = DataLinksManager.GetManager(database); }
            catch (Exception ex) { linkError = ex.Message; }
            var collision = options.Enabled.Contains("collisions");
            // The other checks need the connections too: parts joined through a
            // connection object are not a clash (ConnectedJointPolicy), and a
            // tee / reducer is compared with the pipes welded to it.
            var connections = options.Enabled.Contains("connections") || collision || options.Enabled.Contains("wall");
            var shapes = new List<Shape>();
            using var docLock = document.LockDocument();
            using var transaction = database.TransactionManager.StartTransaction();
            using var manager = connections ? new ConnectionManager() : null;
            try
            {
                var space = (BlockTableRecord)transaction.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForRead);
                var count = 0;
                foreach (ObjectId id in space)
                {
                    if (HostApplicationServices.Current.UserBreak()) throw new OperationCanceledException("Аудит прерван пользователем.");
                    if (++count % 100 == 0) progress?.Invoke($"Прочитано объектов: {count}");
                    Entity entity;
                    try { entity = transaction.GetObject(id, OpenMode.ForRead) as Entity; }
                    catch (Exception ex) { AddReadIssue(snapshot, id.Handle.ToString(), ex.Message); continue; }
                    if (entity == null) continue;
                    AuditPart item = null;
                    if (entity is Part part)
                    {
                        item = new AuditPart { Key = id.Handle.ToString(), Description = id.ObjectClass.Name, ClassName = id.ObjectClass.Name };
                        snapshot.Parts.Add(item);
                        try
                        {
                            if (savedProperties != null && savedProperties.TryGetValue(item.Key, out var saved))
                            {
                                item.ClassName = string.IsNullOrWhiteSpace(saved.ClassName) ? part.GetType().Name : saved.ClassName;
                                item.Description = saved.Description;
                                item.Diameter = saved.Diameter;
                                item.WallRaw = saved.WallRaw;
                                item.LengthUnit = saved.LengthUnit;
                                item.WallMm = AuditRules.WallMillimetres(item.WallRaw, item.LengthUnit);
                                item.Ends = saved.Ends;
                                item.EndsError = saved.EndsError;
                                item.LineKey = saved.LineKey;
                                item.LineLabel = saved.LineLabel;
                            }
                            else
                            {
                                if (dlm == null) throw new InvalidOperationException(linkError);
                                var rowId = dlm.FindAcPpRowId(id);
                                var values = dlm.GetAllProperties(rowId, true).GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                                    .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
                                string Get(string name) => values.TryGetValue(name, out var v) ? v : "";
                                item.ClassName = string.IsNullOrWhiteSpace(Get("PnPClassName")) ? part.GetType().Name : Get("PnPClassName");
                                item.Description = Get("PartSizeLongDesc");
                                if (string.IsNullOrWhiteSpace(item.Description)) item.Description = Get("PartFamilyLongDesc");
                                item.Diameter = Get("NominalDiameter");
                                item.WallRaw = Get("WallThickness");
                                // The wall is in the part's catalogue unit, not in
                                // drawing units (INSUNITS). Converted before the
                                // line lookup, which may throw.
                                item.LengthUnit = Get("LengthUnit");
                                item.WallMm = AuditRules.WallMillimetres(item.WallRaw, item.LengthUnit);
                                if (AuditRules.IsTeeOrReducerClass(item.ClassName))
                                {
                                    try { item.Ends = ReadEnds(dlm, rowId, Get("PortName"), item.Diameter, item.WallRaw, item.LengthUnit); }
                                    catch (Exception ex) { item.EndsError = ex.Message; }
                                }
                                SetLine(item, rowId, dlm, Get("LineNumberTag"));
                                if (item.LineKey == null) item.LineLabel = AuditOptions.NoLineLabel;
                            }
                        }
                        catch (Exception ex)
                        {
                            item.LineUnknown = item.LineKey == null;
                            AddReadIssue(snapshot, item.Key, "Свойства: " + ex.Message);
                        }
                        item.IsPipeOrElbow = item.ClassName.Equals("Pipe", StringComparison.OrdinalIgnoreCase)
                            || item.ClassName.Contains("Elbow", StringComparison.OrdinalIgnoreCase)
                            || item.ClassName.Contains("PipeBend", StringComparison.OrdinalIgnoreCase)
                            || part.GetType().Name.Equals("Pipe", StringComparison.OrdinalIgnoreCase);
                        item.IsConnector = item.ClassName.Contains("Connector", StringComparison.OrdinalIgnoreCase)
                            || part.GetType().Name.Equals("Connector", StringComparison.OrdinalIgnoreCase);
                        item.IsPipe = AuditRules.IsPipeClass(item.ClassName) || part is Pipe;
                        item.IsTeeOrReducer = AuditRules.IsTeeOrReducerClass(item.ClassName);
                        if (connections) ReadConnections(item, part, id, manager);
                    }
                    if (collision) CollectCollisionEntity(entity, item, transaction, Matrix3d.Identity,
                        id.Handle.ToString(), id.Handle.ToString(), "", false, 0, new HashSet<ObjectId>(),
                        shapes, snapshot, options.SelectedLines == null || (item != null && options.Includes(item)));
                }
                // Native connections may be exposed in one direction only.
                var indexed = snapshot.Parts.ToDictionary(p => p.Key);
                foreach (var p in snapshot.Parts)
                    foreach (var key in p.Neighbours.ToArray())
                        if (indexed.TryGetValue(key, out var other)) other.Neighbours.Add(p.Key);
                if (collision) CheckCollisions(shapes, snapshot, indexed, progress);
                return snapshot;
            }
            finally
            {
                foreach (var shape in shapes) shape.Dispose();
                // Read-only audit: nothing is added to or committed in the drawing.
            }
        }

        // Wall per end of a tee / reducer. The part's own row describes its
        // first end (S1); the other ends are Plant 3D Port rows (PartPort) —
        // a reducer 159x8-89x6 keeps 8 mm on S1 and 6 mm on S2.
        internal static List<AuditEnd> ReadEnds(DataLinksManager dlm, int rowId, string firstName, string diameter, string wall, string unit)
        {
            var ends = new List<AuditEnd> { new AuditEnd { Name = string.IsNullOrWhiteSpace(firstName) ? "S1" : firstName.Trim(),
                Diameter = diameter, WallRaw = wall, LengthUnit = unit } };
            var related = dlm.GetRelatedRowIds("PartPort", "Part", rowId, "Port");
            if (related == null) return ends;
            foreach (int portRow in related)
            {
                var values = dlm.GetAllProperties(portRow, true);
                string Get(string name) => values.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
                var name = Get("PortName");
                if (ends.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
                ends.Add(new AuditEnd { Name = name, Diameter = Get("NominalDiameter"), WallRaw = Get("WallThickness"), LengthUnit = Get("LengthUnit") });
            }
            return ends;
        }

        static void SetLine(AuditPart item, int rowId, DataLinksManager dlm, string fallback)
        {
            var related = dlm.GetRelatedRowIds("P3dLineGroupPartRelationship", "Part", rowId, "LineGroup");
            var groups = new List<int>();
            if (related != null) foreach (int id in related) groups.Add(id);
            if (groups.Distinct().Count() > 1) throw new InvalidOperationException("Деталь связана с несколькими группами линий.");
            if (groups.Count > 0)
            {
                var props = dlm.GetAllProperties(groups[0], true);
                var tag = props.FirstOrDefault(p => p.Key.Equals("Tag", StringComparison.OrdinalIgnoreCase)).Value?.Trim();
                if (!string.IsNullOrWhiteSpace(tag)) fallback = tag;
            }
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                item.LineKey = fallback.Trim();
                item.LineLabel = fallback.Trim();
            }
        }

        static void ReadConnections(AuditPart item, Part part, ObjectId id, ConnectionManager manager)
        {
            try
            {
                var ports = part.GetPorts(PortType.Both);
                if (ports == null) throw new InvalidOperationException("GetPorts вернул null.");
                for (int i = 0; i < ports.Count; i++)
                {
                    var port = ports[i];
                    if (port == null || port.IsNull || port.IsSymbolic) continue;
                    var result = new AuditPort { Name = string.IsNullOrEmpty(port.Name) ? $"#{i + 1}" : port.Name };
                    item.Ports.Add(result);
                    using var pair = new Pair { ObjectId = id, Port = port };
                    result.Connected = manager.IsConnected(pair);
                }
                var links = manager.GetConnections(id);
                if (links != null)
                    for (int i = 0; ; i++)
                    {
                        Connection link;
                        // Native collection has no Count; its indexer signals the end.
                        try { link = links[i]; } catch (IndexOutOfRangeException) { break; }
                        catch (ArgumentOutOfRangeException) { break; }
                        if (link == null) break;
                        var other = link.ObjectId1 == id ? link.ObjectId2 : link.ObjectId1;
                        if (!other.IsNull) item.Neighbours.Add(other.Handle.ToString());
                    }
                item.ConnectionsRead = true;
            }
            catch (Exception ex)
            {
                // ConnectivityRule reports it once, with this reason. A separate
                // read issue as well made every such part count twice.
                item.ConnectionsError = ex.Message;
            }
        }

        sealed class Shape : IDisposable
        {
            public AuditPart Part;
            public bool Selected;
            public Extents3d Bounds;
            public List<Solid3d> Solids = new();
            public List<Solid3d> BareSolids;
            public void Dispose()
            {
                foreach (var solid in Solids) solid.Dispose();
                if (BareSolids != null) foreach (var solid in BareSolids) solid.Dispose();
            }
        }

        // 2D / annotative objects have no volume to intersect. Plant adds
        // DBPoints for its connection ports when exploded — markers, not
        // missing solid geometry (verified on native DWG). The types after
        // Hatch could not be exploded into solids either (an MLeader's arrows
        // are 2D SOLIDs), so each one was reported «не проверено».
        // A readable name for an object that is not a Plant part, for clash
        // messages: «ImpCurve» meant nothing (a Plant 3D structure beam).
        internal static string Describe(Entity entity)
        {
            var dxf = entity.ObjectId.IsNull ? "" : (entity.ObjectId.ObjectClass?.DxfName ?? "");
            string kind;
            if (entity is Solid3d) kind = "3D-тело";
            else if (entity is SubDMesh) kind = "Сетка";
            else if (dxf.StartsWith("ACPPSTRUCTURE", StringComparison.OrdinalIgnoreCase))
            {
                var member = new[] { ("BEAM", "балка"), ("COLUMN", "колонна"), ("BRAC", "связь"), ("FOOTING", "фундамент"), ("PLATE", "пластина"),
                    ("GRATING", "настил"), ("STAIR", "лестница"), ("RAILING", "ограждение"), ("LADDER", "стремянка") }
                    .FirstOrDefault(m => dxf.Contains(m.Item1, StringComparison.OrdinalIgnoreCase)).Item2;
                kind = member == null ? "Металлоконструкция Plant 3D" : $"Металлоконструкция Plant 3D ({member})";
            }
            else kind = string.IsNullOrEmpty(dxf) ? entity.GetType().Name : $"{entity.GetType().Name} ({dxf})";
            return $"{kind}, слой «{entity.Layer}»";
        }

        static bool IsFlat(Entity entity) =>
            IsPlainCurve(entity) || entity is DBPoint || entity is DBText || entity is MText || entity is Dimension || entity is Hatch
            || entity is Solid || entity is Trace || entity is Autodesk.AutoCAD.DatabaseServices.Shape || entity is Image
            || entity is Ole2Frame || entity is MLeader || entity is FeatureControlFrame || entity is UnderlayReference || entity is Viewport;

        // AutoCAD's own curves only. A custom object derived from a curve — a
        // Plant 3D structure beam or column (ImpCurve, ACPPSTRUCTUREBEAM) —
        // has a body: it used to be skipped as «2D», so a pipe through a beam
        // was never reported (temp.dwg). It is exploded like everything else.
        static bool IsPlainCurve(Entity entity) =>
            entity is Line || entity is Arc || entity is Circle || entity is Ellipse || entity is Polyline || entity is Polyline2d
            || entity is Polyline3d || entity is Spline || entity is Ray || entity is Xline || entity is Leader;

        static void CollectShape(Entity entity, AuditPart item, Transaction transaction, List<Shape> shapes, AuditSnapshot snapshot, bool selected, Matrix3d transform)
        {
            if (IsFlat(entity)) return;
            var shape = new Shape { Selected = selected, Part = item ?? new AuditPart { Key = entity.Handle.ToString(), Description = entity.GetType().Name } };
            try
            {
                bool incomplete = false;
                ExtractSolids(entity, shape.Solids, transaction, 0, ref incomplete);
                if ((entity is Pipe pipe && pipe.InsulationDiameter > pipe.OuterDiameter + 1e-9)
                    || (entity is InlineAsset asset && asset.InsulationDiameter > 0))
                {
                    // Explode includes the insulation envelope. Strip it only on
                    // a transient clone; retain both envelopes for real obstacles.
                    using var bareEntity = (Entity)entity.Clone();
                    if (bareEntity is Pipe barePipe) barePipe.InsulationDiameter = 0;
                    else if (bareEntity is InlineAsset bareAsset) bareAsset.InsulationDiameter = 0;
                    shape.BareSolids = new List<Solid3d>();
                    bool bareIncomplete = false;
                    ExtractSolids(bareEntity, shape.BareSolids, transaction, 0, ref bareIncomplete);
                    if (bareIncomplete || shape.BareSolids.Count == 0)
                        throw new InvalidOperationException("Не удалось отделить изоляцию от геометрии детали.");
                    foreach (var bare in shape.BareSolids) bare.TransformBy(transform);
                }
                foreach (var solid in shape.Solids) solid.TransformBy(transform);
                if (shape.Solids.Count == 0)
                {
                    if (incomplete) throw new InvalidOperationException("Геометрия не представлена доступными 3D-телами.");
                    snapshot.NonVolumetricCount++;
                    return; // Symbolic welds and 2D-only blocks have no volume to intersect.
                }
                shape.Bounds = shape.Solids[0].GeometricExtents;
                foreach (var solid in shape.Solids.Skip(1)) shape.Bounds.AddExtents(solid.GeometricExtents);
                shapes.Add(shape);
                if (incomplete) snapshot.GeometryIssues.Add(AuditRules.Issue("collisions", "Коллизии", "Не проверено",
                    "Часть геометрии не удалось преобразовать в 3D-тела; проверка элемента неполная.", new[] { shape.Part }));
            }
            catch (Exception ex)
            {
                shape.Dispose();
                var message = ex.Message.Contains("eCannotScaleNonUniformly", StringComparison.OrdinalIgnoreCase)
                    ? "AutoCAD не позволяет преобразовать это 3D-тело при неравномерном масштабе по осям. Коллизии для него не проверены; используйте одинаковый масштаб X/Y/Z."
                    : ex.Message;
                snapshot.GeometryIssues.Add(AuditRules.Issue("collisions", "Коллизии", "Не проверено", message, new[] { shape.Part }));
            }
        }

        static void ExtractSolids(Entity entity, List<Solid3d> solids, Transaction transaction, int depth, ref bool incomplete)
        {
            if (entity is Solid3d solid)
            {
                var copy = (Solid3d)solid.Clone();
                // A solid modelled with history (RecordHistory, e.g. BOX with
                // SOLIDHIST=1) keeps it in the clone, and every boolean on it
                // failed with eWasOpenForWrite — the pair went «не проверено»
                // (temp.dwg). The copy only needs the body.
                try { copy.RecordHistory = false; } catch { }
                solids.Add(copy);
                return;
            }
            if (IsFlat(entity)) return;
            if (entity is SubDMesh mesh)
            {
                // A closed mesh (imported equipment, MESH bodies) becomes a
                // solid; an open one cannot be tested and is reported.
                try { solids.Add(mesh.ConvertToSolid(false, false)); }
                catch { incomplete = true; }
                return;
            }
            if (entity is BlockReference block &&
                ((BlockTableRecord)transaction.GetObject(block.BlockTableRecord, OpenMode.ForRead)).IsFromExternalReference)
            { incomplete = true; return; }
            if (depth >= 8) { incomplete = true; return; }
            using var pieces = new DBObjectCollection();
            try
            {
                entity.Explode(pieces);
                if (pieces.Count == 0) incomplete = true;
                foreach (DBObject piece in pieces)
                    if (piece is Entity child) ExtractSolids(child, solids, transaction, depth + 1, ref incomplete);
            }
            catch { incomplete = true; }
            finally { foreach (DBObject piece in pieces) piece.Dispose(); }
        }

        static bool Overlaps(Extents3d a, Extents3d b) =>
            a.MaxPoint.X > b.MinPoint.X && b.MaxPoint.X > a.MinPoint.X &&
            a.MaxPoint.Y > b.MinPoint.Y && b.MaxPoint.Y > a.MinPoint.Y &&
            a.MaxPoint.Z > b.MinPoint.Z && b.MaxPoint.Z > a.MinPoint.Z;

        static void CheckCollisions(List<Shape> shapes, AuditSnapshot snapshot, IReadOnlyDictionary<string, AuditPart> parts, Action<string> progress)
        {
            // Sweep on X before expensive solid intersections; each unordered pair once.
            var sorted = shapes.OrderBy(s => s.Bounds.MinPoint.X).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (HostApplicationServices.Current.UserBreak()) throw new OperationCanceledException("Аудит прерван пользователем.");
                if (i % 25 == 0) progress?.Invoke($"Коллизии: {i} из {sorted.Count} объектов");
                var a = sorted[i];
                for (int j = i + 1; j < sorted.Count && sorted[j].Bounds.MinPoint.X < a.Bounds.MaxPoint.X; j++)
                {
                    var b = sorted[j];
                    if (!a.Selected && !b.Selected) continue;
                    // Pieces of one block insertion are one object, not a clash
                    // (entities of the host have distinct selection handles).
                    if (a.Part.SelectionKey != null && string.Equals(a.Part.SelectionKey, b.Part.SelectionKey, StringComparison.Ordinal)) continue;
                    if (!Overlaps(a.Bounds, b.Bounds)) continue;
                    if (SupportContactPolicy.IgnoreContact(a.Part, b.Part))
                    {
                        snapshot.SupportContactsIgnored++;
                        continue;
                    }
                    snapshot.CollisionPairsChecked++;
                    var joint = ConnectedJointPolicy.IgnoreOverlap(a.Part, b.Part, parts);
                    try
                    {
                        bool collision = SolidOverlap(a.BareSolids ?? a.Solids, b.BareSolids ?? b.Solids);
                        bool insulation = false;
                        if (!collision && (a.BareSolids != null || b.BareSolids != null)
                            && !InsulationJointPolicy.IgnoreInsulationOverlap(a.Part, b.Part))
                            collision = insulation = SolidOverlap(a.Solids, b.Solids);
                        if (!collision) continue;
                        // Counted only when they really overlap: the number of
                        // joints merely touching would mean nothing to the user.
                        if (joint) { snapshot.ConnectedJointsIgnored++; continue; }
                        var clash = AuditRules.Issue("collisions", "Коллизии", "Пересечение",
                            $"Обнаружено пересечение {(insulation ? "с участием изоляции" : "объёмов")}: {a.Part.Description} ↔ {b.Part.Description}. Проверьте, не является ли оно предусмотренным сопряжением.", new[] { a.Part, b.Part });
                        clash.Bounds = Overlap(a.Bounds, b.Bounds);
                        snapshot.GeometryIssues.Add(clash);
                    }
                    catch (Exception ex)
                    {
                        // A joint is not a clash whatever its geometry, so a
                        // failed check of one leaves nothing unchecked.
                        if (joint) continue;
                        var failed = AuditRules.Issue("collisions", "Коллизии", "Не проверено", "Ошибка проверки пары: " + ex.Message, new[] { a.Part, b.Part });
                        failed.Bounds = Overlap(a.Bounds, b.Bounds);
                        snapshot.GeometryIssues.Add(failed);
                    }
                }
            }
        }

        // The pair's boxes overlap (checked before), so this box is not empty.
        static double[] Overlap(Extents3d a, Extents3d b) => new[]
        {
            Math.Max(a.MinPoint.X, b.MinPoint.X), Math.Max(a.MinPoint.Y, b.MinPoint.Y), Math.Max(a.MinPoint.Z, b.MinPoint.Z),
            Math.Min(a.MaxPoint.X, b.MaxPoint.X), Math.Min(a.MaxPoint.Y, b.MaxPoint.Y), Math.Min(a.MaxPoint.Z, b.MaxPoint.Z),
        };

        static bool SolidOverlap(List<Solid3d> a, List<Solid3d> b)
        {
            foreach (var left in a)
                foreach (var right in b)
                {
                    if (!Overlaps(left.GeometricExtents, right.GeometricExtents)) continue;
                    using var first = (Solid3d)left.Clone();
                    using var second = (Solid3d)right.Clone();
                    if (!first.CheckInterference(second)) continue;
                    first.BooleanOperation(BooleanOperationType.BoolIntersect, second);
                    if (first.MassProperties.Volume > 1e-9) return true;
                }
            return false;
        }

        static void AddReadIssue(AuditSnapshot snapshot, string key, string message) => snapshot.ReadIssues.Add(new AuditIssue
        { RuleId = "read", Check = "Чтение данных", Severity = "Не проверено", Message = message, Keys = new List<string> { key } });
    }
}
