using System;
using System.Collections.Generic;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Groups drawing entities by a part property (SELECTBYSTATUS,
    /// SELECTBYLINE). One entity can carry several rows: a connection object
    /// (weld, flanged joint) is a P3dConnector row WITHOUT Status or line plus
    /// its sub-part rows — Buttweld, Gasket, BoltSet — that have them (0873
    /// model: every P3dConnector row blank, «Крепеж1_Статус» filled). Grouped
    /// by row, the weld landed in «Без статуса» while the Properties palette
    /// showed «Новое». An entity takes every non-empty value of its rows and is
    /// «empty» only when all of them are; counts are entities, not rows.
    /// </summary>
    public static class EntityValueGroups
    {
        public sealed class Group
        {
            public List<long> Handles { get; } = new List<long>();
            public int Count { get; set; }
        }

        /// <param name="rows">Each row's value and the entity handles it draws on.</param>
        public static Dictionary<string, Group> Build(IEnumerable<(string Value, IReadOnlyCollection<long> Handles)> rows, string nullKey)
        {
            var valuesByHandle = new Dictionary<long, List<string>>();
            var handleOrder = new List<long>();
            var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
            Group Of(string key)
            {
                if (!groups.TryGetValue(key, out var group)) groups[key] = group = new Group();
                return group;
            }

            foreach (var (value, handles) in rows)
            {
                var key = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                if (handles == null || handles.Count == 0)
                {
                    // No entity to select; still listed, as before.
                    Of(key ?? nullKey).Count++;
                    continue;
                }
                foreach (var handle in handles)
                {
                    if (!valuesByHandle.TryGetValue(handle, out var values))
                    {
                        valuesByHandle[handle] = values = new List<string>();
                        handleOrder.Add(handle);
                    }
                    if (key != null && !values.Contains(key)) values.Add(key);
                }
            }

            foreach (var handle in handleOrder)
            {
                var values = valuesByHandle[handle];
                if (values.Count == 0) values.Add(nullKey);
                foreach (var key in values)
                {
                    var group = Of(key);
                    group.Handles.Add(handle);
                    group.Count++;
                }
            }
            return groups;
        }
    }
}
