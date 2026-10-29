using System;
using System.Collections.Generic;

namespace PipingSpecPlugin.Solids
{
    public enum ColorSource { ByLayer, ByBlock, Own }

    // The layer and colour an object is seen with.
    public readonly struct Look<TColor>
    {
        public Look(string layer, TColor color) { Layer = layer; Color = color; }
        public string Layer { get; }
        public TColor Color { get; }
    }

    // What PSTOSOLID paints a body's faces with: the colour the object had on
    // screen. AutoCAD's rules for what sits inside a block or an xref (or a
    // Plant 3D part, exploded): «ПоБлоку» takes the insertion's colour, an
    // object on layer «0» lives on the insertion's layer, «ПоСлою» takes the
    // colour of the layer it lives on.
    public static class SolidLook
    {
        // parent: the insertion's look; null at the top of the selection.
        // byBlockAtTop: «ПоБлоку» outside any block (AutoCAD draws it white).
        public static Look<TColor> Resolve<TColor>(ColorSource source, TColor own, string layer, Look<TColor>? parent,
            Func<string, TColor> layerColor, TColor byBlockAtTop)
        {
            var effective = parent.HasValue && string.Equals(layer, "0", StringComparison.OrdinalIgnoreCase) ? parent.Value.Layer : layer;
            TColor color;
            switch (source)
            {
                case ColorSource.ByBlock: color = parent.HasValue ? parent.Value.Color : byBlockAtTop; break;
                case ColorSource.ByLayer: color = layerColor(effective); break;
                default: color = own; break;
            }
            return new Look<TColor>(effective, color);
        }

        // An xref's layer «L» is «xref|L» in the host — where its colour (and
        // on/off state) is kept; «0» stays «0».
        public static string HostLayerName(string layer, string xref) =>
            string.IsNullOrEmpty(xref) || string.Equals(layer, "0", StringComparison.OrdinalIgnoreCase) ? layer : xref + "|" + layer;

        // Unites a list pairwise (balanced, so each body is in log2(n)
        // booleans instead of n). unite(a, b) merges b into a and returns
        // true, or returns false and both stay — the bodies that could not
        // be united are left as separate bodies, not lost.
        public static List<T> Unite<T>(IList<T> items, Func<T, T, bool> unite)
        {
            var level = new List<List<T>>();
            foreach (var item in items) level.Add(new List<T> { item });
            while (level.Count > 1)
            {
                var next = new List<List<T>>();
                for (int i = 0; i < level.Count; i += 2)
                {
                    if (i + 1 == level.Count) { next.Add(level[i]); break; }
                    var left = level[i];
                    var right = level[i + 1];
                    var merged = new List<T>(left);
                    // The right group's bodies go into the first of the left
                    // (one that failed before gets another body to try with);
                    // what does not unite stays as is.
                    foreach (var body in right)
                        if (!unite(merged[0], body)) merged.Add(body);
                    next.Add(merged);
                }
                level = next;
            }
            return level.Count == 0 ? new List<T>() : level[0];
        }
    }
}
