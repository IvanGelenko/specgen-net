using System.Text.RegularExpressions;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Decoded segments of a Plant 3D line designation such as
    /// "150-KSN001-LS016C-HN". Any field the regex couldn't extract stays
    /// null — callers treat null exactly like an empty ВТ cell (no change
    /// queued), not as an error, since some lines use shorter/older tag
    /// formats (e.g. "P-22002") that don't carry this encoding at all.
    /// </summary>
    public class LineDesignationParts
    {
        public string NominalSize;       // "150" -> Size2
        public string EnvironmentCode;   // "KSN" -> Код_трубопровода
        public string LineNumber;        // "001" -> Номер (only three-digit numbers are synced)
        public string MaterialCode;      // "LS" -> Material_pipe
        public string PressureCode;      // "016" -> Ysl_pressure
        public string FlangeCode;        // "C" -> flanges (optional segment)
        public string InsulationCode;    // "H" -> InsulationType
        public string SputnikCode;       // "N" -> Sputnik
    }

    /// <summary>
    /// Parses the composite line designation the user's catalogue encodes
    /// into P3dLineGroup.Tag when the "номер линии" tag format is active
    /// (see PnPTagFormat in Piping.dcf):
    ///
    ///   #(Size2)-#(Код_трубопровода)#(Номер)-#(Material_pipe)#(Ysl_pressure)#(flanges)-#(InsulationType)#(Heat_type)
    ///
    /// Example: "150-KSN001-LS016C-HN"
    ///   150 = NominalSize, KSN = EnvironmentCode, 001 = LineNumber,
    ///   LS = MaterialCode, 016 = PressureCode, C = FlangeCode (optional),
    ///   H = InsulationCode, N = SputnikCode.
    ///
    /// ВТ's "Обозначение" column carries this same designation (plus an
    /// optional " (комментарий)" suffix Plant 3D's tag never has), so this
    /// parser is what lets СИНХРОНИЗАЦИЯ ВТ fill Size2 / Код_трубопровода /
    /// Material_pipe / Ysl_pressure / flanges / InsulationType / Sputnik
    /// even though ВТ has no separate column for any of them.
    /// </summary>
    public static class LineDesignationParser
    {
        // Size - environment + line number - material + pressure + flange - insulation + heating.
        // Pressure accepts two or three digits; flange accepts one or two letters.
        // Legacy free-form line numbers remain readable, but only three digits are synced.
        static readonly Regex Pattern = new Regex(
            @"^\s*(?<size>\d+)-(?<env>[A-Za-z]+)(?<num>[^-]*)-(?<mat>[A-Za-z]{2})(?<press>[0-9]{2,3})(?<flange>[A-Za-z]{1,2})?-(?<ins>[A-Za-z])(?<sput>[A-Za-z])\s*(?:\(.*)?$",
            RegexOptions.Compiled);

        static readonly Regex WsRun = new Regex(@"\s+", RegexOptions.Compiled);

        /// <summary>P3dLineGroup.Tag value for a ВТ "Обозначение" cell: the first line with
        /// every parenthesised remark removed. An unclosed "(" drops the rest of the line,
        /// matching how <see cref="Pattern"/> treats a trailing comment.</summary>
        public static string TagOf(string designation)
        {
            if (string.IsNullOrWhiteSpace(designation)) return "";
            var line = designation.Split('\n')[0];
            var sb = new System.Text.StringBuilder(line.Length);
            var depth = 0;
            foreach (var ch in line)
            {
                if (ch == '(') { depth++; continue; }
                if (ch == ')') { if (depth > 0) depth--; continue; }
                if (depth == 0) sb.Append(ch);
            }
            return WsRun.Replace(sb.ToString(), " ").Trim();
        }

        /// <summary>Extract exactly three digits after the environment code, preserving zeros.
        /// Independent of the remaining designation so unknown catalogue suffixes cannot
        /// prevent a valid line number from being synchronized.</summary>
        public static string ParseSyncLineNumber(string designation)
        {
            if (string.IsNullOrWhiteSpace(designation)) return null;
            var match = Regex.Match(designation.Split('\n')[0].Trim(),
                @"^[0-9]+-[A-Za-z]+(?<number>[0-9]{3})-");
            return match.Success ? match.Groups["number"].Value : null;
        }

        public static LineDesignationParts Parse(string designation)
        {
            if (string.IsNullOrWhiteSpace(designation)) return null;
            // Multi-Id cells (several designations separated by newlines)
            // are handled by matching against the Обозначение only via the
            // Tag column already resolved per-line — this parser always
            // receives one designation at a time, take the first line as
            // a defensive trim in case a caller passes the raw cell.
            var firstLine = designation.Split('\n')[0].Trim();
            var m = Pattern.Match(firstLine);
            if (!m.Success) return null;

            return new LineDesignationParts
            {
                NominalSize = m.Groups["size"].Value,
                EnvironmentCode = m.Groups["env"].Value.ToUpperInvariant(),
                LineNumber = m.Groups["num"].Value,
                MaterialCode = m.Groups["mat"].Value.ToUpperInvariant(),
                PressureCode = m.Groups["press"].Value,
                FlangeCode = m.Groups["flange"].Success ? m.Groups["flange"].Value.ToUpperInvariant() : null,
                InsulationCode = m.Groups["ins"].Value.ToUpperInvariant(),
                SputnikCode = m.Groups["sput"].Value.ToUpperInvariant(),
            };
        }
    }
}
