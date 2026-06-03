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
        /// Plant 3D does not always report an absent identifier as an empty
        /// string. For an untagged part the DataLinks "Tag" property can come
        /// back as a placeholder ("&lt;пусто&gt;", "&lt;none&gt;", "0", "?",
        /// the bare register code, etc.) or as the still-unfilled tag format
        /// template (anything containing '{' '}' or '%'). Treating those as a
        /// real identifier moved untagged parts onto a junk "placeholder"
        /// layer; we now treat them as "no identifier — leave the part where
        /// it is". This is pure/no-AutoCAD so it stays unit-testable.
        /// </summary>
        public static bool IsMeaninglessTag(string rawTag)
        {
            if (string.IsNullOrWhiteSpace(rawTag)) return true;

            var t = rawTag.Trim();

            // An unresolved tag-format template still carries its field
            // markers — '{Field}' or '%...%'. A real identifier never does.
            if (t.IndexOf('{') >= 0 || t.IndexOf('}') >= 0 || t.IndexOf('%') >= 0)
                return true;

            // Known literal placeholders Plant 3D / the UI substitute for
            // "no value". Matched case-insensitively; angle-bracket forms
            // (<пусто>, <none>) are covered by the markers below too.
            switch (t.ToLowerInvariant())
            {
                case "<пусто>":
                case "<none>":
                case "<empty>":
                case "none":
                case "n/a":
                case "0":
                case "?":
                    return true;
            }

            // A value that is nothing but angle-bracket placeholder text,
            // e.g. "<...>", is not a real tag either.
            if (t.StartsWith("<") && t.EndsWith(">"))
                return true;

            // The decisive check: an untagged part's Tag often comes back as
            // the bare separators of the tag-format template — e.g. "-",
            // "- -", "_/_", ".". Those carry no alphanumeric content, so they
            // identify nothing. A value with no letter or digit anywhere is
            // not a real identifier and must not create a layer like "_-_".
            bool hasAlphaNum = false;
            foreach (var ch in t)
            {
                if (char.IsLetterOrDigit(ch)) { hasAlphaNum = true; break; }
            }
            if (!hasAlphaNum) return true;

            return false;
        }

        /// <summary>
        /// Convert a raw Tag into a usable layer name, or return null if the
        /// tag is empty / whitespace / collapses to nothing usable. Callers
        /// treat a null result as "no identifier — skip this part" so we
        /// never create a junk layer named "_" or "".
        /// </summary>
        public static string SanitizeLayerName(string rawTag)
        {
            if (IsMeaninglessTag(rawTag)) return null;

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
