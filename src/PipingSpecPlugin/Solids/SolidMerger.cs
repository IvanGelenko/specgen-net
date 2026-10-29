using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.BoundaryRepresentation;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.PnP3dObjects;

namespace PipingSpecPlugin.Solids
{
    public sealed class SolidMergeResult
    {
        public List<ObjectId> Created { get; } = new List<ObjectId>();
        public int Bodies { get; set; }
        public int SourceColors { get; set; }
        public int FaceColors { get; set; }
        public int PaintedFaces { get; set; }
        // Bodies whose faces could not be addressed: left in the layer's colour.
        public int UnpaintedBodies { get; set; }
        public int UniteFailures { get; set; }
        public int Erased { get; set; }
        // Selected objects that went into the body, and how many did not.
        public List<ObjectId> Converted { get; } = new List<ObjectId>();
        public int Unconverted { get; set; }
        public List<string> Notes { get; } = new List<string>();
    }

    // The core of PSTOSOLID, without prompts (the console smoke test calls
    // it too): the objects are taken apart down to their bodies — blocks,
    // xrefs and Plant 3D parts at any depth — each body's faces get the
    // colour the object had on screen, the bodies are united and the result
    // is added to the current space on the current layer. Inside the
    // caller's transaction: nothing is kept unless it commits.
    public static class SolidMerger
    {
        public static SolidMergeResult Merge(Database database, Transaction transaction, IEnumerable<ObjectId> ids, Action<string> log)
        {
            var result = new SolidMergeResult();
            var list = ids.ToList();
            var collector = new BodyCollector(database, transaction);
            try
            {
                foreach (var id in list)
                {
                    if (HostApplicationServices.Current.UserBreak()) throw new OperationCanceledException("Прервано пользователем.");
                    if (transaction.GetObject(id, OpenMode.ForRead) is Entity entity) collector.Add(entity);
                }
                result.Notes.AddRange(collector.Notes());
                result.Bodies = collector.Bodies.Count;
                result.SourceColors = collector.Bodies.Select(b => b.Color.ToString()).Distinct().Count();
                if (collector.Bodies.Count == 0) return result;

                // The bodies go into the drawing first: a face is addressed
                // through its path in the database (Brep of a body outside it
                // has no SubentityPath — the console smoke test). Faces next:
                // the united body keeps the faces' colours. Everything is in
                // the caller's transaction, so an abort leaves no trace.
                var space = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);
                foreach (var (solid, color) in collector.Bodies)
                {
                    solid.SetDatabaseDefaults(database);
                    solid.LayerId = database.Clayer;
                    // A face of no colour shows the layer's.
                    solid.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                    space.AppendEntity(solid);
                    transaction.AddNewlyCreatedDBObject(solid, true);
                    try { result.PaintedFaces += Paint(solid, color); }
                    catch (System.Exception first)
                    {
                        // A body Brep cannot read is often fixed by cleaning it
                        // (redundant edges / vertices); otherwise it still goes
                        // into the union, in the layer's colour.
                        // Then by face number (faces of a 3D solid are numbered
                        // from 1), without Brep.
                        var byIndex = PaintByIndex(solid, color);
                        if (byIndex > 0)
                        {
                            result.PaintedFaces += byIndex;
                            log?.Invoke($"Грани окрашены по номерам ({collector.Origin[solid]}): {byIndex}; Brep: {Describe(first)}.");
                        }
                        else if (++result.UnpaintedBodies <= 20)
                            log?.Invoke($"Грани тела не окрашены ({collector.Origin[solid]}): {Describe(first)}.");
                    }
                }
                if (result.UnpaintedBodies > 0)
                    result.Notes.Add($"Тел, у которых не удалось окрасить грани (остались цвета слоя): {result.UnpaintedBodies}.");
                var united = SolidLook.Unite(collector.Bodies.Select(b => b.Solid).ToList(), (into, body) =>
                {
                    if (HostApplicationServices.Current.UserBreak()) throw new OperationCanceledException("Прервано пользователем.");
                    try
                    {
                        into.BooleanOperation(BooleanOperationType.BoolUnite, body);
                        return true;
                    }
                    catch (System.Exception ex)
                    {
                        if (++result.UniteFailures <= 20) log?.Invoke("Объединение не удалось: " + ex.Message);
                        return false;
                    }
                });

                // The bodies united into another are empty now.
                var kept = new HashSet<ObjectId>(united.Select(s => s.ObjectId));
                foreach (var (solid, _) in collector.Bodies)
                    if (!kept.Contains(solid.ObjectId)) solid.Erase();
                foreach (var solid in united.Where(s => !s.IsNull)) result.Created.Add(solid.ObjectId);
                result.FaceColors = united.Where(s => !s.IsNull).SelectMany(s => FaceColorNames(s, log)).Distinct().Count();
                // Only what went into the body may be erased afterwards
                // (EraseOriginals): lines, texts and objects that gave no body
                // would be lost.
                result.Converted.AddRange(list.Where(collector.Converted.Contains));
                result.Unconverted = list.Count - result.Converted.Count;
                return result;
            }
            finally
            {
                // Bodies merged away (or never added) are not in the drawing.
                foreach (var (solid, _) in collector.Bodies)
                    if (!solid.IsDisposed && solid.ObjectId.IsNull) solid.Dispose();
            }
        }

        // After the body is committed: the objects it was made of, through
        // ERASE — Plant 3D parts refuse Entity.Erase (eCannotBeErasedByCaller,
        // console smoke test) and take their connection objects with them
        // when erased the way the user would. Inside the calling command, so
        // one ОТМЕНИТЬ undoes it with the body.
        public static void EraseOriginals(Autodesk.AutoCAD.EditorInput.Editor editor, SolidMergeResult result, Action<string> log)
        {
            if (result.Converted.Count > 0)
            {
                try { editor.Command("_.ERASE", Autodesk.AutoCAD.EditorInput.SelectionSet.FromObjectIds(result.Converted.ToArray()), ""); }
                catch (System.Exception ex) { log?.Invoke("ERASE: " + ex.Message); }
            }
            var left = result.Converted.Count(id => !id.IsErased);
            result.Erased = result.Converted.Count - left;
            if (left > 0) result.Notes.Add($"Не удалось удалить объектов: {left}.");
            if (result.Unconverted > 0) result.Notes.Add($"Не удалено объектов без 3D-тел (линии, тексты и т. п.): {result.Unconverted}.");
        }

        static int PaintByIndex(Solid3d solid, Color color)
        {
            int count = 0;
            if (!solid.IsWriteEnabled) solid.UpgradeOpen();
            for (int i = 1; i <= 100000; i++)
            {
                var id = new SubentityId(SubentityType.Face, new IntPtr(i));
                try { solid.SetSubentityColor(id, color); count++; }
                catch { break; }
            }
            return count;
        }

        static string Describe(System.Exception ex) =>
            ex is Autodesk.AutoCAD.BoundaryRepresentation.Exception brep ? "Brep " + brep.ErrorStatus : ex.GetType().Name + " " + ex.Message;

        // A body in the drawing, through its path: only then do its faces
        // have the SubentityPath colours are set by (Brep of the entity
        // itself threw for every face — console smoke test).
        public static Brep BrepOf(Solid3d solid) =>
            new Brep(new FullSubentityPath(new[] { solid.ObjectId }, new SubentityId(SubentityType.Null, IntPtr.Zero)));

        // Faces of no colour of their own (or «ПоСлою»/«ПоБлоку») get the
        // object's colour; a face coloured on purpose keeps it.
        // The face ids are read before the first colour is set: a change of
        // the body leaves an open Brep stale and only its first face was
        // painted (console smoke test, 3 faces of 18).
        static int Paint(Solid3d solid, Color color)
        {
            int count = 0;
            var faces = new List<SubentityId>();
            // Brep opens the body itself: open for write by our transaction it
            // failed for some bodies (Brep error 80, eWasOpenForWrite).
            if (solid.IsWriteEnabled) solid.DowngradeOpen();
            using (var brep = BrepOf(solid))
                foreach (var face in brep.Faces) faces.Add(face.SubentityPath.SubentId);
            solid.UpgradeOpen();
            foreach (var id in faces)
            {
                Color own = null;
                try { own = solid.GetSubentityColor(id); } catch { }
                if (own != null && !own.IsByLayer && !own.IsByBlock && !own.IsNone) continue;
                solid.SetSubentityColor(id, color);
                count++;
            }
            return count;
        }

        static IEnumerable<string> FaceColorNames(Solid3d solid, Action<string> log)
        {
            var colors = new List<string>();
            try
            {
                using var brep = SolidMerger.BrepOf(solid);
                foreach (var face in brep.Faces)
                    try { colors.Add(solid.GetSubentityColor(face.SubentityPath.SubentId)?.ToString() ?? ""); } catch { }
            }
            catch (System.Exception ex) { log?.Invoke("Цвета граней итогового тела не прочитаны: " + ex.GetType().Name + " " + ex.Message); }
            return colors;
        }

        // Takes the selection apart into bodies with the colour each was seen
        // with. Blocks and xrefs are walked through their definitions (an
        // xref cannot be exploded), anything else is exploded until only
        // bodies are left.
        sealed class BodyCollector
        {
            const int MaxDepth = 32;
            readonly Database _host;
            readonly Transaction _transaction;
            readonly Dictionary<ObjectId, (Color Color, bool Hidden)> _layers = new();
            readonly Color _white = Color.FromColorIndex(ColorMethod.ByAci, 7);
            public List<(Solid3d Solid, Color Color)> Bodies { get; } = new();
            // Which selected object a body came from, for the log.
            public Dictionary<Solid3d, string> Origin { get; } = new();
            string _top;
            ObjectId _topId;
            // Selected objects that gave at least one body.
            public HashSet<ObjectId> Converted { get; } = new();
            int _flat, _hidden, _empty;
            readonly Dictionary<string, int> _failed = new(StringComparer.Ordinal);

            public BodyCollector(Database host, Transaction transaction) { _host = host; _transaction = transaction; }

            public void Add(Entity entity)
            {
                _top = $"{entity.GetType().Name} [{entity.Handle}]";
                _topId = entity.ObjectId;
                Visit(entity, Matrix3d.Identity, null, null, _transaction, 0);
            }

            public IEnumerable<string> Notes()
            {
                if (_flat > 0) yield return $"Пропущено плоских объектов (линии, тексты, размеры, штриховки): {_flat}.";
                if (_hidden > 0) yield return $"Пропущено объектов на выключенных/замороженных слоях: {_hidden}.";
                if (_empty > 0) yield return $"Объектов без объёма (символы сварных швов и т. п.): {_empty}.";
                foreach (var pair in _failed) yield return $"Не преобразовано в тело — {pair.Key}: {pair.Value}.";
            }

            void Fail(string why) => _failed[why] = (_failed.TryGetValue(why, out var n) ? n : 0) + 1;

            void Visit(Entity entity, Matrix3d toHost, Look<Color>? parent, string xref, Transaction transaction, int depth)
            {
                if (depth > MaxDepth) { Fail("превышена глубина вложенности"); return; }
                var layer = SolidLook.HostLayerName(entity.Layer, xref);
                var source = entity.Color.IsByLayer ? ColorSource.ByLayer : entity.Color.IsByBlock ? ColorSource.ByBlock : ColorSource.Own;
                var look = SolidLook.Resolve(source, entity.Color, layer, parent, name => LayerOf(name, entity.LayerId).Color, _white);
                if (LayerOf(look.Layer, entity.LayerId).Hidden) { _hidden++; return; }

                switch (entity)
                {
                    case Solid3d solid:
                        AddBody((Solid3d)solid.Clone(), toHost, look.Color);
                        return;
                    case SubDMesh mesh:
                        Solid3d converted;
                        try { converted = mesh.ConvertToSolid(false, false); }
                        catch { Fail("незамкнутая сеть (MESH)"); return; }
                        AddBody(converted, toHost, look.Color);
                        return;
                    case MInsertBlock array:
                        Explode(array, toHost, look, xref, transaction, depth);
                        return;
                    case BlockReference block:
                        WalkBlock(block, toHost, look, xref, transaction, depth);
                        return;
                }
                if (IsFlat(entity)) { _flat++; return; }
                // A Plant 3D pipe / fitting explodes with its insulation
                // envelope around it, which would swallow the pipe and its
                // colour: the body is taken without it.
                if ((entity is Pipe pipe && pipe.InsulationDiameter > pipe.OuterDiameter + 1e-9) || (entity is InlineAsset asset && asset.InsulationDiameter > 0))
                {
                    using var bare = (Entity)entity.Clone();
                    if (bare is Pipe barePipe) barePipe.InsulationDiameter = 0;
                    else if (bare is InlineAsset bareAsset) bareAsset.InsulationDiameter = 0;
                    Explode(bare, toHost, look, xref, transaction, depth);
                    return;
                }
                Explode(entity, toHost, look, xref, transaction, depth);
            }

            void AddBody(Solid3d solid, Matrix3d toHost, Color color)
            {
                try
                {
                    // A body with history (SOLIDHIST) fails every boolean.
                    try { solid.RecordHistory = false; } catch { }
                    if (!toHost.IsEqualTo(Matrix3d.Identity)) solid.TransformBy(toHost);
                    // An empty body (no faces) has nothing to unite.
                    if (solid.IsNull) { solid.Dispose(); _empty++; return; }
                    Bodies.Add((solid, color));
                    Origin[solid] = _top;
                    Converted.Add(_topId);
                }
                catch (System.Exception ex)
                {
                    solid.Dispose();
                    Fail(ex.Message.IndexOf("NonUniformly", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "тело во вставке с разным масштабом по осям" : "тело не перенесено (" + ex.Message + ")");
                }
            }

            void Explode(Entity entity, Matrix3d toHost, Look<Color> look, string xref, Transaction transaction, int depth)
            {
                using var pieces = new DBObjectCollection();
                try { entity.Explode(pieces); }
                catch { Fail(Kind(entity)); return; }
                try
                {
                    if (pieces.Count == 0) { _empty++; return; }
                    foreach (DBObject piece in pieces)
                        if (piece is Entity child) Visit(child, toHost, look, xref, transaction, depth + 1);
                }
                finally { foreach (DBObject piece in pieces) piece.Dispose(); }
            }

            void WalkBlock(BlockReference block, Matrix3d toHost, Look<Color> look, string xref, Transaction transaction, int depth)
            {
                var record = (BlockTableRecord)transaction.GetObject(block.BlockTableRecord, OpenMode.ForRead);
                var inner = toHost * block.BlockTransform;
                if (!record.IsFromExternalReference)
                {
                    foreach (ObjectId id in record)
                        if (transaction.GetObject(id, OpenMode.ForRead) is Entity child && child is not AttributeDefinition)
                            Visit(child, inner, look, xref, transaction, depth + 1);
                    return;
                }
                if (record.IsUnloaded || record.XrefStatus != XrefStatus.Resolved) { Fail($"внешняя ссылка «{record.Name}» не загружена"); return; }
                var database = record.GetXrefDatabase(false);
                if (database == null) { Fail($"внешняя ссылка «{record.Name}» недоступна"); return; }
                // The xref's database belongs to AutoCAD; only the transaction is ours.
                using var xrefTransaction = database.TransactionManager.StartOpenCloseTransaction();
                var model = (BlockTableRecord)xrefTransaction.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForRead);
                foreach (ObjectId id in model)
                    if (xrefTransaction.GetObject(id, OpenMode.ForRead) is Entity child)
                        Visit(child, inner, look, record.Name, xrefTransaction, depth + 1);
            }

            // The layer's colour and whether it is off or frozen: the host's
            // record (an xref's «xref|layer» included), else the object's own.
            (Color Color, bool Hidden) LayerOf(string name, ObjectId fallback)
            {
                var table = (LayerTable)_transaction.GetObject(_host.LayerTableId, OpenMode.ForRead);
                var id = table.Has(name) ? table[name] : fallback;
                if (_layers.TryGetValue(id, out var known)) return known;
                (Color, bool) value = (_white, false);
                try
                {
                    using var read = id.Database.TransactionManager.StartOpenCloseTransaction();
                    var record = (LayerTableRecord)read.GetObject(id, OpenMode.ForRead);
                    value = (record.Color, record.IsOff || record.IsFrozen);
                }
                catch { }
                _layers[id] = value;
                return value;
            }

            // AutoCAD's own curves only: a Plant 3D structure member is a
            // curve too (ImpCurve) and has a body (same as the audit).
            static bool IsFlat(Entity entity) =>
                entity is Line || entity is Arc || entity is Circle || entity is Ellipse || entity is Polyline || entity is Polyline2d
                || entity is Polyline3d || entity is Spline || entity is Ray || entity is Xline || entity is DBPoint || entity is DBText || entity is MText || entity is Dimension || entity is Hatch
                || entity is Solid || entity is Trace || entity is Autodesk.AutoCAD.DatabaseServices.Shape || entity is RasterImage
                || entity is Ole2Frame || entity is MLeader || entity is Leader || entity is FeatureControlFrame || entity is UnderlayReference
                || entity is Viewport || entity is AttributeReference || entity is Table;

            static string Kind(Entity entity)
            {
                var dxf = entity.ObjectId.IsNull ? "" : entity.ObjectId.ObjectClass?.DxfName ?? "";
                return entity switch
                {
                    Autodesk.AutoCAD.DatabaseServices.Surface => "поверхность",
                    Region => "область (REGION)",
                    PolyFaceMesh or PolygonMesh => "сеть многогранников",
                    Body => "тело ACIS (BODY)",
                    _ => string.IsNullOrEmpty(dxf) ? entity.GetType().Name : $"{entity.GetType().Name} ({dxf})",
                };
            }
        }
    }
}
