using System;
using System.Collections.Generic;
using System.Linq;

namespace PipingSpecPlugin.Layers
{
    public sealed class LineLayerPart
    {
        public string Key { get; set; }
        public string Line { get; set; }
        public string ClassName { get; set; }
        public string Layer { get; set; }
        public string Color { get; set; }
    }
    public sealed class ValveColorChoice
    {
        public string Key { get; set; }
        public string SourceLayer { get; set; }
        public string Warning { get; set; }
    }
    public static class LayerColorPlanner
    {
        public static List<ValveColorChoice> Build(IEnumerable<LineLayerPart> parts)
        {
            var all = parts.ToList();
            var pipesByLine = all.Where(p => !string.IsNullOrWhiteSpace(p.Line) &&
                (string.Equals(p.ClassName, "Pipe", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(p.ClassName, "PipeBend", StringComparison.OrdinalIgnoreCase)))
                .GroupBy(p => p.Line, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            var result = new List<ValveColorChoice>();
            foreach (var valve in all.Where(p => (p.ClassName ?? "").Contains("Valve", StringComparison.OrdinalIgnoreCase)))
            {
                var choice = new ValveColorChoice { Key = valve.Key };
                if (string.IsNullOrWhiteSpace(valve.Line)) choice.Warning = "Не задан номер линии; назначен цвет собственного слоя.";
                else if (!pipesByLine.TryGetValue(valve.Line, out var pipes)) choice.Warning = $"На линии «{valve.Line}» не найдены трубы; назначен цвет собственного слоя.";
                else if (pipes.Select(p => p.Color).Distinct(StringComparer.Ordinal).Count() != 1)
                    choice.Warning = $"Трубы линии «{valve.Line}» находятся на слоях с разными цветами; назначен цвет собственного слоя.";
                else
                {
                    // Prefer its own layer when present so the valve stays ByLayer.
                    choice.SourceLayer = pipes.FirstOrDefault(p => p.Layer == valve.Layer)?.Layer ?? pipes[0].Layer;
                }
                result.Add(choice);
            }
            return result;
        }
    }
}
