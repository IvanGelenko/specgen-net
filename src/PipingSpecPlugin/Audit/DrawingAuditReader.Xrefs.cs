using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PnP3dObjects;

namespace PipingSpecPlugin.Audit
{
    public static partial class DrawingAuditReader
    {
        // Walk block definitions instead of exploding xref inserts: explode is
        // unavailable for unresolved refs and loses instance/source identity.
        // Each insertion gets its own path and accumulated transform.
        static void CollectCollisionEntity(Entity entity, AuditPart item, Transaction transaction,
            Matrix3d toHost, string path, string selectionKey, string source, bool insideXref,
            int depth, HashSet<ObjectId> ancestors, List<Shape> shapes, AuditSnapshot snapshot, bool selected)
        {
            if (HostApplicationServices.Current.UserBreak()) throw new OperationCanceledException("Аудит прерван пользователем.");
            var info = item ?? new AuditPart { Key = path, SelectionKey = selectionKey,
                Source = source, Description = string.IsNullOrEmpty(source) ? Describe(entity) : $"{source} · {Describe(entity)} [{entity.Handle}]" };
            if (entity is not BlockReference block)
            {
                ReadSupportContacts(entity, info, snapshot);
                if (item == null && entity is Part && !entity.ObjectId.IsNull)
                {
                    // Resolve the source drawing's line number when available.
                    // Missing metadata never suppresses an insulation collision.
                    try
                    {
                        var links = DataLinksManager.GetManager(entity.Database);
                        int row = links.FindAcPpRowId(entity.ObjectId);
                        var properties = links.GetAllProperties(row, true);
                        string Get(string name) => properties.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
                        // Name the part in clash messages, not just its class.
                        var size = Get("PartSizeLongDesc");
                        if (!string.IsNullOrWhiteSpace(size))
                            info.Description = string.IsNullOrEmpty(source) ? size.Trim() : $"{source} · {size.Trim()} [{entity.Handle}]";
                        SetLine(info, row, links, Get("LineNumberTag"));
                        // A line of another drawing: in the «Линия» column it
                        // read like a line of this one. The key stays as is
                        // (InsulationJointPolicy compares the line numbers).
                        if (insideXref && !string.IsNullOrWhiteSpace(info.LineLabel))
                            info.LineLabel = $"{info.LineLabel} (ссылка {XrefName(source)})";
                    }
                    catch { /* Unknown line: compare all available geometry. */ }
                }
                CollectShape(entity, info, transaction, shapes, snapshot, selected && !insideXref, toHost);
                return;
            }

            BlockTableRecord record = null;
            bool added = false;
            bool isXref = false;
            try
            {
                record = (BlockTableRecord)transaction.GetObject(block.BlockTableRecord, OpenMode.ForRead);
                isXref = record.IsFromExternalReference;
                // An overlay nested inside another xref is not inserted in the
                // host. Direct overlays are checked just like attachments.
                if (insideXref && record.IsFromOverlayReference) return;
                var nextSource = isXref ? (string.IsNullOrEmpty(source) ? "" : source + " → ") + record.Name + " (" + record.PathName + ")" : source;
                info.Source = nextSource;
                info.Description = nextSource;
                if (depth >= 32 || !ancestors.Add(record.ObjectId))
                    throw new InvalidOperationException("Циклическая ссылка или превышена глубина вложенности 32.");
                added = true;
                var nextTransform = toHost * block.BlockTransform;
                // The definition is walked once, at the first copy's transform:
                // the other copies of a MINSERT array were skipped silently.
                if (block is MInsertBlock array && array.Rows * array.Columns > 1)
                    snapshot.GeometryIssues.Add(AuditRules.Issue("collisions", "Коллизии", "Не проверено",
                        $"Массив блоков MINSERT «{record.Name}» ({array.Rows}×{array.Columns}): коллизии проверены только для первой копии.", new[] { info }));
                if (isXref)
                {
                    if (record.IsUnloaded || record.XrefStatus != XrefStatus.Resolved)
                        throw new InvalidOperationException($"Ссылка недоступна ({record.XrefStatus}). Загрузите/восстановите её и повторите аудит.");
                    var xref = record.GetXrefDatabase(false);
                    if (xref == null) throw new InvalidOperationException("Не удалось получить геометрию загруженной ссылки.");
                    // This DB belongs to AutoCAD; only the transaction is ours.
                    using var xrefTransaction = xref.TransactionManager.StartOpenCloseTransaction();
                    var model = (BlockTableRecord)xrefTransaction.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(xref), OpenMode.ForRead);
                    snapshot.XrefInstancesChecked++;
                    WalkCollisionBlock(model, xrefTransaction, nextTransform, path, selectionKey, nextSource,
                        true, depth + 1, ancestors, shapes, snapshot, false);
                }
                else
                {
                    WalkCollisionBlock(record, transaction, nextTransform, path, selectionKey, nextSource,
                        insideXref, depth + 1, ancestors, shapes, snapshot, selected);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                if (isXref) snapshot.XrefInstancesUnavailable++;
                snapshot.GeometryIssues.Add(AuditRules.Issue("collisions", "Коллизии", "Не проверено",
                    $"{info.Source ?? path}: {ex.Message}", new[] { info }));
            }
            finally { if (added) ancestors.Remove(record.ObjectId); }
        }

        // «outer (…\outer.dwg) → leaf (…\leaf.dwg)» → «leaf».
        static string XrefName(string source)
        {
            var last = (source ?? "").Split('→').Last().Trim();
            var cut = last.IndexOf(" (", StringComparison.Ordinal);
            return cut > 0 ? last.Substring(0, cut) : last;
        }

        static void WalkCollisionBlock(BlockTableRecord record, Transaction transaction, Matrix3d toHost,
            string path, string selectionKey, string source, bool insideXref, int depth,
            HashSet<ObjectId> ancestors, List<Shape> shapes, AuditSnapshot snapshot, bool selected)
        {
            foreach (ObjectId childId in record)
            {
                try
                {
                    var child = transaction.GetObject(childId, OpenMode.ForRead) as Entity;
                    if (child == null) continue;
                    CollectCollisionEntity(child, null, transaction, toHost, path + "/" + childId.Handle,
                        selectionKey, source, insideXref, depth, ancestors, shapes, snapshot, selected);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    snapshot.GeometryIssues.Add(AuditRules.Issue("collisions", "Коллизии", "Не проверено",
                        $"Не удалось прочитать элемент ссылки/блока: {ex.Message}", new[] {
                            new AuditPart { Key = path + "/" + childId.Handle, SelectionKey = selectionKey, Source = source } }));
                }
            }
        }
    }
}
