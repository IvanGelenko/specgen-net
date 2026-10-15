using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using PipingSpecPlugin.Audit;

namespace PipingSpecPlugin.Layers
{
    public sealed class LayerColorResult
    {
        public int Changed { get; set; }
        public int ValveLineColors { get; set; }
        public List<string> Warnings { get; } = new();
    }

    public static class LayerColorService
    {
        public static LayerColorResult Apply(Document document, IReadOnlyDictionary<string, AuditPart> properties)
        {
            var result = new LayerColorResult();
            var db = document.Database;
            using var docLock = document.LockDocument();
            using var tr = db.TransactionManager.StartTransaction();
            var entities = new Dictionary<string, Entity>();
            var visited = new HashSet<ObjectId>();
            void ReadBlock(ObjectId id)
            {
                if (!visited.Add(id)) return;
                var block = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (block.IsFromExternalReference || block.IsDependent) return;
                foreach (ObjectId entityId in block)
                {
                    var entity = tr.GetObject(entityId, OpenMode.ForRead) as Entity;
                    if (entity == null) continue;
                    entities[entity.Handle.ToString()] = entity;
                    if (entity is BlockReference reference)
                    {
                        foreach (ObjectId attributeId in reference.AttributeCollection)
                        {
                            var attribute = tr.GetObject(attributeId, OpenMode.ForRead) as Entity;
                            if (attribute != null) entities[attribute.Handle.ToString()] = attribute;
                        }
                        ReadBlock(reference.BlockTableRecord);
                    }
                }
            }
            ReadBlock(SymbolUtilityServices.GetBlockModelSpaceId(db));
            var layers = entities.Values.Select(e => e.LayerId).Distinct()
                .Select(id => (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead)).ToDictionary(l => l.ObjectId.Handle.ToString());
            string ColorKey(Color color) => $"{color.ColorMethod}:{color.ColorIndex}:{color.ColorValue.ToArgb()}";
            var candidates = new List<LineLayerPart>();
            foreach (var (key, entity) in entities)
            {
                if (!properties.TryGetValue(key, out var data)) continue;
                var layer = layers[entity.LayerId.Handle.ToString()];
                candidates.Add(new LineLayerPart { Key = key, ClassName = data.ClassName, Line = data.LineKey,
                    Layer = layer.ObjectId.Handle.ToString(), Color = ColorKey(layer.Color) });
            }
            var valves = LayerColorPlanner.Build(candidates).ToDictionary(c => c.Key);
            foreach (var (key, entity) in entities)
            {
                if (HostApplicationServices.Current.UserBreak()) throw new OperationCanceledException("Изменение цвета отменено; изменения не применены.");
                Color desired = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                if (valves.TryGetValue(key, out var choice))
                {
                    if (choice.Warning != null) result.Warnings.Add($"Клапан [{key}]: {choice.Warning}");
                    if (choice.SourceLayer != null && choice.SourceLayer != entity.LayerId.Handle.ToString())
                    {
                        desired = layers[choice.SourceLayer].Color;
                        result.ValveLineColors++;
                    }
                }
                if (ColorKey(entity.Color) == ColorKey(desired)) continue;
                // The layer remains locked if it was locked. The explicit command
                // changes colors only and never changes layer membership or locks.
                var writable = (Entity)tr.GetObject(entity.ObjectId, OpenMode.ForWrite, false, true);
                writable.Color = desired;
                result.Changed++;
            }
            tr.Commit();
            return result;
        }
    }
}
