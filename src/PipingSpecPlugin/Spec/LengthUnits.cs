namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Metres per unit of a Plant 3D catalogue «LengthUnit» — the unit of a
    /// part's Length, WallThickness, MatchingPipeOd (not the drawing's
    /// INSUNITS). Blank = mm, the Plant 3D metric default. null = unknown:
    /// never silently treated as millimetres. One table for the
    /// specification, the bend folding and the audit (it used to be copied).
    /// </summary>
    public static class LengthUnits
    {
        public static double? MetresPerUnit(string unit)
        {
            switch ((unit ?? "").Trim().ToLowerInvariant())
            {
                case "": case "мм": case "mm": return 0.001;
                case "см": case "cm": return 0.01;
                case "м": case "m": return 1.0;
                case "in": return 0.0254;
                case "ft": return 0.3048;
                default: return null;
            }
        }
    }
}
