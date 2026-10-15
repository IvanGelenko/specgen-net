using System.Text;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Makes arbitrary catalogue / DLM text safe for a WordprocessingML
    /// <c>w:t</c>. XML 1.0 forbids most C0 control characters (U+0001–U+0008,
    /// U+000B, U+000C, U+000E–U+001F), U+FFFE / U+FFFF and unpaired
    /// surrogates; OpenXml 2.20 writes through an XmlWriter with
    /// CheckCharacters on, so a single such character in a part description
    /// (copy-pasted into Spec Editor, imported from Excel, …) threw
    /// "'\x01', hexadecimal value 0x01, is an invalid character" at
    /// Document.Save() and the WHOLE spec was lost with a cryptic error.
    /// Pure string logic — covered by xUnit.
    /// </summary>
    public static class XmlText
    {
        /// <summary>
        /// Drops characters XML 1.0 cannot carry. Tab, CR and LF are legal
        /// and kept (callers decide what a line break means for them).
        /// </summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            StringBuilder sb = null;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                bool keep;
                int width = 1;
                if (char.IsHighSurrogate(c))
                {
                    keep = i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
                    if (keep) width = 2;
                }
                else if (char.IsLowSurrogate(c)) keep = false;           // unpaired
                else keep = c == '\t' || c == '\n' || c == '\r'
                         || (c >= ' ' && c != '￾' && c != '￿');
                if (keep)
                {
                    sb?.Append(text, i, width);
                }
                else if (sb == null)
                {
                    sb = new StringBuilder(text.Length);
                    sb.Append(text, 0, i);
                }
                i += width - 1;
            }
            return sb == null ? text : sb.ToString();
        }

        /// <summary>
        /// One-line cell text for the spec table: XML-safe, and tab / CR / LF
        /// become a single space. A raw line feed inside <c>w:t</c> is not a
        /// line break in Word (it is rendered as whitespace at best), so a
        /// multi-line catalogue value would otherwise glue words together or
        /// show differently in Word and LibreOffice.
        /// </summary>
        public static string SingleLine(string text)
        {
            var s = Clean(text);
            if (s.IndexOfAny(new[] { '\t', '\r', '\n' }) < 0) return s;
            var sb = new StringBuilder(s.Length);
            var lastSpace = false;
            foreach (var c in s)
            {
                var isBreak = c == '\t' || c == '\r' || c == '\n';
                if (isBreak)
                {
                    if (!lastSpace) sb.Append(' ');
                    lastSpace = true;
                    continue;
                }
                sb.Append(c);
                lastSpace = c == ' ';
            }
            return sb.ToString();
        }
    }
}
