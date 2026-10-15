using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Source data for an insulation calculation program (INSULSPEC): per
    /// line, the insulated straight pipes (metres) and fittings — elbows,
    /// pipe bends, tees (pieces); only parts whose own insulation thickness is
    /// above zero (<see cref="IsInsulated"/>). Counted exactly like the specification:
    /// <see cref="Prepare"/> only selects and reshapes the records, then
    /// SpecRowBuilder aggregates them with the spec's filters, names
    /// («Полное описание (размер)»), grouping by line and rounding (pipe
    /// metres up to 0.1 m).
    ///
    /// A pipe bend «ИЗГИБ ТРУБЫ: Р = 90, У = 90 град, Д1 = 100, Д2 = 100,
    /// ДЛИНА ТРУБЫ = 341.3717, Труба 25х4 …» is split in two: its straight
    /// tails Д1 + Д2 are added to the pipe of the same description on the
    /// line, and the bend itself is listed in pieces as «ИЗГИБ ТРУБЫ 25х4,
    /// Р = 90, У = 90 град, ДЛИНА = 141.3717» (the arc, ДЛИНА ТРУБЫ − Д1 − Д2).
    /// Pure logic — covered by xUnit.
    /// </summary>
    public static class InsulationTakeoff
    {
        public const string Pipe = "Pipe", Bend = "Bend", Elbow = "Elbow", Tee = "Tee";

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Regex WsRun = new Regex(@"\s+", RegexOptions.Compiled);

        /// <summary>Pipe / Bend / Elbow / Tee, or null for every other part.</summary>
        public static string KindOf(PartRecord r)
        {
            if (r == null) return null;
            if (r.IsPipeBend) return Bend;
            switch ((r.PnPClassName ?? "").Trim().ToLowerInvariant())
            {
                case "pipe": return Pipe;
                case "elbow": return Elbow;
                case "tee": return Tee;
                default: return null;
            }
        }

        /// <summary>
        /// The part itself carries insulation: its own «Толщина изоляции» is
        /// above zero — exactly what Plant 3D draws. The line's thickness / type
        /// is not used: an uninsulated stretch of an insulated line must not
        /// count.
        /// </summary>
        public static bool IsInsulated(PartRecord r) => SpecNumber.Parse(r?.InsulationThickness) > 0;

        public sealed class Prepared
        {
            public List<PartRecord> Records = new();
            public List<ReviewItem> ReviewItems = new();
            public List<string> Trace = new();
        }

        public static Prepared Prepare(IEnumerable<PartRecord> records)
        {
            var result = new Prepared();
            var input = (records ?? Enumerable.Empty<PartRecord>())
                .Where(r => KindOf(r) != null && IsInsulated(r))
                .ToList();

            // The pipe a bend's tails belong to: same line, same description.
            // The tails are part of the (insulated) bend, so they count even
            // when the neighbouring pipe is not insulated — then they become a
            // pipe row of their own.
            var pipes = new Dictionary<(string, string), PartRecord>();
            foreach (var r in input.Where(r => KindOf(r) == Pipe))
                pipes.TryAdd((LineOf(r), PipeKey(Description(r))), r);

            // Tail records get ids no real part has (scan ids are drawing
            // rowIds, or negative after DrawingPartIds.Remap).
            var nextId = int.MaxValue;
            foreach (var r in input)
            {
                if (KindOf(r) != Bend)
                {
                    result.Records.Add(r);
                    continue;
                }
                var bend = BendDescription.Parse(Description(r));
                if (bend == null)
                {
                    result.Records.Add(r);
                    result.ReviewItems.Add(ReviewItem.ForPart(r,
                        "Описание изгиба не разобрано (нет Р или У) — изгиб выведен как есть, Д1 и Д2 к трубе не прибавлены",
                        Description(r)));
                    continue;
                }

                var listed = r.Clone();
                listed.PartSizeLongDesc = bend.DisplayName;
                result.Records.Add(listed);

                var target = "";
                if (bend.TailsMm > 0)
                {
                    PartRecord tail;
                    if (bend.PipeText != null && pipes.TryGetValue((LineOf(r), PipeKey(bend.PipeText)), out var pipe))
                    {
                        // A copy of the real pipe, so the tails land in exactly
                        // its spec row (same name, material, wall, …).
                        tail = pipe.Clone();
                        var mmPerUnit = (SpecRowBuilder.LengthFactor(pipe.LengthUnit) ?? 0.001) * 1000.0;
                        var length = bend.TailsMm / mmPerUnit;
                        var pipeLength = SpecNumber.Parse(pipe.Length);
                        var pipeWeight = SpecNumber.Parse(pipe.Weight);
                        tail.Length = length.ToString("R", Inv);
                        tail.Weight = pipeLength > 0 && pipeWeight > 0
                            ? (pipeWeight.Value * length / pipeLength.Value).ToString("R", Inv) : null;
                        target = Description(pipe);
                    }
                    else
                    {
                        // No such pipe on the line: the tails are a pipe of
                        // their own, described by the bend's pipe text.
                        tail = r.Clone();
                        tail.PnPClassName = "Pipe";
                        tail.PartCategory = "Pipe";
                        tail.ContentIsoSymbolDefinition = null;
                        tail.PartSizeLongDesc = bend.PipeText ?? Description(r);
                        tail.Length = bend.TailsMm.ToString("R", Inv);
                        tail.LengthUnit = "mm";
                        tail.Weight = null;
                        target = tail.PartSizeLongDesc + " (новая строка)";
                    }
                    tail.PnPID = nextId--;
                    tail.ReadWarnings = null;
                    result.Records.Add(tail);
                }
                result.Trace.Add(string.Format(Inv, "  bend#{0} line='{1}' '{2}' → '{3}'; Д1+Д2={4:0.###} мм → {5}",
                    r.SourcePnPID != 0 ? r.SourcePnPID : r.PnPID, LineOf(r), Description(r), bend.DisplayName,
                    bend.TailsMm, bend.TailsMm > 0 ? target : "—"));
            }
            return result;
        }

        /// <summary>
        /// SpecRowBuilder's review items that matter for this output: mass is
        /// not part of it, so its caveats (no mass, unknown mass unit, mass
        /// spread) would only be noise.
        /// </summary>
        public static IEnumerable<ReviewItem> WithoutMassNotes(IEnumerable<ReviewItem> items)
            => (items ?? Enumerable.Empty<ReviewItem>())
               .Where(i => (i?.Reason ?? "").IndexOf("масс", StringComparison.OrdinalIgnoreCase) < 0);

        static string LineOf(PartRecord r)
            => string.IsNullOrEmpty(r.LineNumberTag) ? SpecConstants.NullLine : r.LineNumberTag;

        static string Description(PartRecord r)
        {
            foreach (var s in new[] { r.PartSizeLongDesc, r.PartFamilyLongDesc, r.ShortDescription })
                if (!string.IsNullOrWhiteSpace(s)) return WsRun.Replace(s, " ").Trim();
            return "";
        }

        /// <summary>Matching key of a pipe description: case, «х»/«x»/«×» and spacing ignored.</summary>
        internal static string PipeKey(string text)
            => WsRun.Replace((text ?? "").ToLowerInvariant().Replace('х', 'x').Replace('×', 'x'), " ")
                   .Trim().TrimEnd(',', ';', '.', ' ');
    }

    /// <summary>
    /// The parameters a pipe bend description carries — «ИЗГИБ ТРУБЫ: Р = 90,
    /// У = 90 град, Д1 = 100, Д2 = 100, ДЛИНА ТРУБЫ = 341.3717, Труба 25х4 …»
    /// (also the «радиус гиба = … угол гиба = … длина = …» wording). Values
    /// in millimetres / degrees.
    /// </summary>
    public sealed class BendDescription
    {
        const string Num = @"\d+(?:[.,]\d+)?";
        const RegexOptions Opt = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        static readonly Regex RadiusRx = new Regex(@"(?<!\p{L})(?:Р|R|радиус(?:\s+гиба)?)\s*=\s*(" + Num + ")", Opt);
        static readonly Regex AngleRx = new Regex(@"(?<!\p{L})(?:У|угол(?:\s+гиба)?)\s*=\s*(" + Num + ")", Opt);
        static readonly Regex Tail1Rx = new Regex(@"(?<![\p{L}\d])[ДD]1\s*=\s*(" + Num + ")", Opt);
        static readonly Regex Tail2Rx = new Regex(@"(?<![\p{L}\d])[ДD]2\s*=\s*(" + Num + ")", Opt);
        static readonly Regex LengthRx = new Regex(@"(?<!\p{L})длина(?:\s+трубы)?\s*=\s*(" + Num + ")", Opt);
        static readonly Regex SizeRx = new Regex(Num + @"\s*[xхXХ×]\s*" + Num, Opt);

        public double RadiusMm, AngleDeg, Tail1Mm, Tail2Mm;
        public double ArcMm;
        public string PipeText;   // «Труба 25х4 ГОСТ …», null when absent
        public string Size;       // «25х4», null when absent

        public double TailsMm => Tail1Mm + Tail2Mm;

        public string DisplayName
            => "ИЗГИБ ТРУБЫ" + (Size != null ? " " + Size : "")
               + ", Р = " + N(RadiusMm) + ", У = " + N(AngleDeg) + " град, ДЛИНА = " + N(ArcMm);

        /// <summary>null when the radius or the angle is missing.</summary>
        public static BendDescription Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var radius = Value(RadiusRx, text);
            var angle = Value(AngleRx, text);
            if (!(radius > 0) || !(angle > 0)) return null;
            var d = new BendDescription
            {
                RadiusMm = radius.Value,
                AngleDeg = angle.Value,
                Tail1Mm = Value(Tail1Rx, text) ?? 0,
                Tail2Mm = Value(Tail2Rx, text) ?? 0,
            };
            var length = Value(LengthRx, text);
            var arc = length - d.TailsMm;
            d.ArcMm = arc > 0 ? arc.Value : radius.Value * angle.Value * Math.PI / 180.0;

            // «Труба …» — the pipe the bend is made of. «ИЗГИБ ТРУБЫ» itself
            // never matches: «трубы» ≠ «труба».
            var at = text.LastIndexOf("труба", StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
                d.PipeText = text.Substring(at).Trim().TrimEnd(',', ';', '.', ' ');
            var size = SizeRx.Match(d.PipeText ?? text);
            if (size.Success) d.Size = size.Value.Replace(" ", "");
            return d;
        }

        static double? Value(Regex rx, string text)
        {
            var m = rx.Match(text);
            return m.Success ? SpecNumber.Parse(m.Groups[1].Value) : null;
        }

        static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
