using System;

namespace PipingSpecPlugin.Audit
{
    // Insulation-only overlap of two parts of one (filled-in) line is not a
    // clash — between non-adjacent pieces of the line too. Confirmed by the
    // user (2026-10-14); not a gap to «fix».
    public static class InsulationJointPolicy
    {
        public static bool IgnoreInsulationOverlap(AuditPart a, AuditPart b) =>
            !string.IsNullOrWhiteSpace(a.LineKey) && !string.IsNullOrWhiteSpace(b.LineKey) &&
            string.Equals(a.LineKey.Trim(), b.LineKey.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
