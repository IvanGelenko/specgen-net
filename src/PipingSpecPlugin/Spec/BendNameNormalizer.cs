using System.Globalization;
using System.Text.RegularExpressions;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Canonical form of a PipeBend ("Изгиб трубы …") description, used both
    /// as the bucket-key name and as the displayed name in the spec.
    ///
    /// The catalogue builds the bend description from the bend's own
    /// geometry, e.g.
    ///   "Изгиб трубы, радиус гиба = 40 мм, угол гиба = 180°, Д1=0 мм,
    ///    Д2=100мм, длина=325.7 мм, труба 25,0x4,0 ст.20"
    /// or, in the template actually used in the user's project,
    ///   "ИЗГИБ ТРУБЫ: Р = 130, У = 180 град, Д1 = 100, Д2 = 100,
    ///    ДЛИНА ТРУБЫ = 608.407, Труба 25х4 ГОСТ 8734-75/В 20 ГОСТ 8733-74"
    /// Two things make physically identical bends land on different rows:
    ///
    ///  1. Д1/Д2 are the straight tails on the inlet/outlet side, so their
    ///     order only reflects the routing direction. The same bend routed
    ///     the other way reads "Д1=100 мм, Д2=0мм". For procurement it is
    ///     the same item, so the pair is sorted (smaller value first) —
    ///     the key becomes symmetric and the display stays readable and
    ///     deterministic (it no longer depends on DWG read order).
    ///  2. "длина=…" is a computed arc length and carries float noise
    ///     (325.7 vs 325.70 vs 325.7000001). It is rounded to one decimal
    ///     place (AwayFromZero, in decimal arithmetic — no binary
    ///     325.75 → 325.7 surprises) and written without trailing zeros.
    ///
    /// Only the number tokens are rewritten; the surrounding catalogue text
    /// and spacing are kept as-is, so descriptions that carry none of these
    /// fields pass through unchanged. Pure string logic, no AutoCAD
    /// dependency — covered by xUnit.
    /// </summary>
    public static class BendNameNormalizer
    {
        const string Num = @"\d+(?:[.,]\d+)?";

        // Cyrillic "Д" as in the Russian catalogue; Latin "D" accepted too in
        // case a catalogue was authored with a lookalike letter. The
        // look-behind keeps "ДД1=" or "AD1=" from matching.
        static readonly Regex D1 = new Regex(@"(?<![\p{L}\d])([ДD]1\s*=\s*)(" + Num + ")",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Regex D2 = new Regex(@"(?<![\p{L}\d])([ДD]2\s*=\s*)(" + Num + ")",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        // The length token differs between description templates: the sample
        // the first version was written against had "длина=325.7", but the
        // real project template reads "ДЛИНА ТРУБЫ = 608.407" (two words,
        // spaces around "="). Missing the second form meant no rounding at
        // all in production. Accept "длина" plus at most ONE qualifying word
        // ("трубы", "гиба", …) before "=" — any case, any whitespace.
        static readonly Regex LengthField = new Regex(@"(?<![\p{L}\d])(длина(?:\s+\p{L}+)?\s*=\s*)(" + Num + ")",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var s = LengthField.Replace(name, m =>
            {
                var v = ParseDecimal(m.Groups[2].Value);
                if (v == null) return m.Value;
                var rounded = decimal.Round(v.Value, 1, System.MidpointRounding.AwayFromZero);
                return m.Groups[1].Value + Format(rounded, m.Groups[2].Value);
            });

            // Swap only when each tail appears exactly once — anything else is
            // a description format we don't understand, leave it alone.
            var m1s = D1.Matches(s);
            var m2s = D2.Matches(s);
            if (m1s.Count != 1 || m2s.Count != 1) return s;
            var m1 = m1s[0];
            var m2 = m2s[0];
            var v1 = ParseDecimal(m1.Groups[2].Value);
            var v2 = ParseDecimal(m2.Groups[2].Value);
            if (v1 == null || v2 == null || v1.Value <= v2.Value) return s;

            // Д1 > Д2 → swap the number tokens. Replace the later match first
            // so the earlier match's index stays valid.
            var t1 = m1.Groups[2];
            var t2 = m2.Groups[2];
            var first = t1.Index < t2.Index ? t1 : t2;
            var second = t1.Index < t2.Index ? t2 : t1;
            var firstText = first.Value;
            var secondText = second.Value;
            s = s.Remove(second.Index, second.Length).Insert(second.Index, firstText);
            s = s.Remove(first.Index, first.Length).Insert(first.Index, secondText);
            return s;
        }

        static decimal? ParseDecimal(string text)
            => decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number,
                   CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null;

        // Keep the catalogue's own decimal separator (the sample texts mix
        // "325.7" and "25,0x4,0"), drop trailing zeros: 325.70 → 325.7,
        // 325.0 → 325.
        static string Format(decimal value, string original)
        {
            var text = value.ToString("0.#", CultureInfo.InvariantCulture);
            return original.Contains(',') ? text.Replace('.', ',') : text;
        }
    }
}
