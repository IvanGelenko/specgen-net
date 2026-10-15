using System.Globalization;
using System.Text.RegularExpressions;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Pure string-parsing helpers for ВТ cell values that don't map
    /// 1:1 onto a P3dLineGroup field's shape. Split out from
    /// LineSyncService (which depends on Autodesk.ProcessPower.DataLinks
    /// and can't load outside AutoCAD) so this logic is unit-testable the
    /// same way VtReader / LineDesignationParser are.
    /// </summary>
    public static class VtValueParsing
    {
        /// <summary>
        /// Splits ВТ's "2,0/1,6"-style Испытания cell into the two
        /// separate numeric fields Plant 3D expects (TestD = прочность,
        /// TestW = плотность). Commas become decimal points; a cell
        /// without '/' is treated as TestD-only (some ВТ rows only carry
        /// one number). A segment that isn't a number comes back null —
        /// callers must not write an empty/garbage numeric value.
        /// </summary>
        public static (string testD, string testW) SplitTestValues(string vtCell)
        {
            if (string.IsNullOrWhiteSpace(vtCell)) return (null, null);
            var cleaned = vtCell.Replace(",", ".").Trim();
            var parts = cleaned.Split('/');
            string Num(string s)
            {
                s = (s ?? "").Trim().Trim('*'); // ВТ sometimes marks a value "1,4*" with a footnote asterisk (either side).
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                    ? d.ToString("0.####", CultureInfo.InvariantCulture)
                    : null;
            }
            if (parts.Length >= 2) return (Num(parts[0]), Num(parts[1]));
            return (Num(parts[0]), null);
        }

        // Matches the first run of digits (with an optional decimal
        // separator) immediately before a '%' — used to pull "10" out of
        // ВТ's "РД/УЗК 10%" into NDControl's numeric-only field.
        static readonly Regex NdPercentPattern = new Regex(@"(\d+(?:[.,]\d+)?)\s*%", RegexOptions.Compiled);

        public static string ExtractNdPercent(string vtCell)
        {
            if (string.IsNullOrWhiteSpace(vtCell)) return null;
            var m = NdPercentPattern.Match(vtCell);
            return m.Success ? m.Groups[1].Value.Replace(",", ".") : null;
        }

        /// <summary>
        /// ВТ's "Срок службы .../отбраковочная толщина стенки, лет/мм"
        /// column is itself a "years/mm" composite cell (e.g. "20/2") —
        /// only the first number (years, srok) is what gets written and
        /// used in the corrosion-allowance calculation below. A cell with
        /// no '/' is treated as years-only.
        /// </summary>
        public static string ExtractServiceLifeYears(string vtCell)
        {
            if (string.IsNullOrWhiteSpace(vtCell)) return null;
            var first = vtCell.Replace(",", ".").Trim().Split('/')[0].Trim();
            return double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d.ToString("0.####", CultureInfo.InvariantCulture)
                : null;
        }

        /// <summary>
        /// Прибавка на коррозию = Скорость коррозии (мм/год) × Срок
        /// службы (лет). Both inputs are ВТ cell text — corrosionRateCell
        /// is a plain number, serviceLifeCell may be the "years/mm"
        /// composite handled by ExtractServiceLifeYears. Returns null if
        /// either side doesn't parse, so callers never write a bogus 0
        /// for a missing input.
        /// </summary>
        public static string ComputeCorrosionAllowance(string corrosionRateCell, string serviceLifeCell)
        {
            if (!double.TryParse((corrosionRateCell ?? "").Replace(",", ".").Trim(),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
                return null;
            var years = ExtractServiceLifeYears(serviceLifeCell);
            if (years == null || !double.TryParse(years, NumberStyles.Float, CultureInfo.InvariantCulture, out var yearsNum))
                return null;
            return (rate * yearsNum).ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Compares two strings by alternating digit/non-digit runs,
        /// comparing digit runs as numbers (so "1.10" &gt; "1.9") and
        /// everything else ordinally. Nulls sort first. Used to order the
        /// "внутренняя ведомость" grid by Id in a way that matches ВТ's
        /// "раздел.номер" Id scheme (e.g. "1.1"…"1.13", "3.1", "3.2")
        /// without assuming every Id actually follows it — non-numeric
        /// Ids (e.g. "asdd", "P-22002") still get a stable, sensible
        /// order via the ordinal fallback.
        /// </summary>
        public static int NaturalCompare(string a, string b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            // ASCII digits only. char.IsDigit also accepts Arabic-Indic,
            // fullwidth etc. digits, and a run mixing scripts ("1٢") made the
            // order intransitive: digit runs compare by value but a digit
            // vs. a letter compares by code point, so "٩" > "z" > "1٢" > "٩".
            // List.Sort over a non-transitive comparer may throw or misorder.
            // Other scripts' digits are ordinary characters here.
            static bool IsDigit(char c) => c >= '0' && c <= '9';
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (IsDigit(a[i]) && IsDigit(b[j]))
                {
                    int startI = i, startJ = j;
                    while (i < a.Length && IsDigit(a[i])) i++;
                    while (j < b.Length && IsDigit(b[j])) j++;
                    // Compare numerically via length-then-value so
                    // arbitrarily long digit runs don't overflow: strip
                    // leading zeros first, then a longer remaining run is
                    // numerically larger; equal length falls back to
                    // ordinal digit-by-digit comparison.
                    var numA = a.Substring(startI, i - startI).TrimStart('0');
                    var numB = b.Substring(startJ, j - startJ).TrimStart('0');
                    if (numA.Length != numB.Length) return numA.Length.CompareTo(numB.Length);
                    int cmp = string.CompareOrdinal(numA, numB);
                    if (cmp != 0) return cmp;
                }
                else
                {
                    if (a[i] != b[j]) return a[i].CompareTo(b[j]);
                    i++; j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
