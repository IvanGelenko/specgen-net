using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;

namespace PipingSpecPlugin.Layers
{
    public sealed class LayerSelectionChoice
    {
        public string Name { get; init; }
        public ObjectId[] Ids { get; init; }
        public override string ToString() => $"{Name}  ({Ids.Length})";
    }

    public static class LayerSelectionService
    {
        public static List<LayerSelectionChoice> Read(Database database)
        {
            using var transaction = database.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForRead);
            var groups = new Dictionary<string, List<ObjectId>>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId id in space)
            {
                if (id.IsErased || transaction.GetObject(id, OpenMode.ForRead) is not Entity entity) continue;
                if (!groups.TryGetValue(entity.Layer, out var ids)) groups[entity.Layer] = ids = new List<ObjectId>();
                ids.Add(id);
            }
            return groups.OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => new LayerSelectionChoice { Name = g.Key, Ids = g.Value.ToArray() }).ToList();
        }
    }
}
