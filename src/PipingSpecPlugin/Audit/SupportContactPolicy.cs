namespace PipingSpecPlugin.Audit
{
    public static class SupportContactPolicy
    {
        public static bool IgnoreContact(AuditPart a, AuditPart b) =>
            (a.IsSupport && b.IsPipingComponent && a.SupportedPartKeys.Contains(b.Key)) ||
            (b.IsSupport && a.IsPipingComponent && b.SupportedPartKeys.Contains(a.Key));
    }
}
