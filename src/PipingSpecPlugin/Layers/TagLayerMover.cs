using System.Text;

namespace PipingSpecPlugin.Layers
{
    /// <summary>
    /// Pure helpers for the "разнести по слоям" command (MOVEBYTAG): turn a
    /// Plant 3D part Tag (e.g. "gv-001") into a valid AutoCAD layer name.
    /// No AutoCAD types here so the logic is unit-testable on any box.
    /// </summary>
    public static class TagLayerMover
    {
        // Characters AutoCAD forbids in symbol-table (layer) names. Anything
        // here is replaced with '_'. Control chars are handled separately.
        // Ref: a layer name may not contain < > / \ " : ; ? * | , = `
        const string ForbiddenChars = "<>/\\\":;?*|,=`";

        // AutoCAD's hard limit on symbol-table name length.
        public const int MaxLayerNameLength = 255;

        /// <summary>
        /// Convert a raw Tag into a usable layer name, or return null if the
        /// tag is empty / whitespace / collapses to nothing usable. Callers
        /// treat a null result as "no identifier — skip this part" so we
        /// never create a junk layer named "_" or "".
        /// </summary>
        public static string SanitizeLayerName(string rawTag)
        {
            if (string.IsNullOrWhiteSpace(rawTag)) return null;

            var trimmed = rawTag.Trim();
            var sb = new StringBuilder(trimmed.Length);
            bool hasRealChar = false;

            foreach (var ch in trimmed)
            {
                if (char.IsControl(ch))
                {
                    sb.Append('_');
                    continue;
                }
                if (ForbiddenChars.IndexOf(ch) >= 0)
                {
                    sb.Append('_');
                    continue;
                }
                sb.Append(ch);
                hasRealChar = true;
            }

            // All characters were forbidden/control → nothing meaningful to
            // name a layer after. Treat as "no tag".
            if (!hasRealChar) return null;

            var name = sb.ToString();
            if (name.Length > MaxLayerNameLength)
                name = name.Substring(0, MaxLayerNameLength);

            // A name of only dots/spaces is rejected by AutoCAD; guard it.
            if (string.IsNullOrWhiteSpace(name.Replace(".", "")))
                return null;

            return name;
        }
    }
}
