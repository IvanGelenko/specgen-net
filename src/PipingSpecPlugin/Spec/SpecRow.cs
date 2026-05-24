namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Aggregated spec row — one line in the resulting .docx table.
    /// Mirrors the Python edition's SpecRow dataclass field-for-field;
    /// keep names in sync so any future template work transfers cleanly.
    /// </summary>
    public class SpecRow
    {
        public string Category;
        public string Name;
        public string TypeMark;
        public string ItemCode;
        public string Manufacturer;
        public string Unit;        // "шт." or "м"
        public double Qty;         // meters for pipes, pieces otherwise
        public double Weight;      // mass of one unit, kg
        public string Note = "";
        public string LineTag = "";  // P3dLineGroup tag or empty
    }
}
