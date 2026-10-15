using System.Collections.Generic;

namespace PipingSpecPlugin.Audit
{
    // Parts joined through a Plant 3D connection object (weld, flanged joint,
    // stub-in) overlap by design: a branch sits in the header wall, an olet's
    // base in the pipe, a pipe end slightly inside a tee. Not a clash — the
    // user's rule (2026-10-14); so is the connection object against a part it
    // joins. Parts touching without a connection object stay reportable.
    public static class ConnectedJointPolicy
    {
        public static bool IgnoreOverlap(AuditPart a, AuditPart b, IReadOnlyDictionary<string, AuditPart> parts)
        {
            bool Linked(AuditPart x, AuditPart y) => x.Neighbours.Contains(y.Key) || y.Neighbours.Contains(x.Key);
            if ((a.IsConnector || b.IsConnector) && Linked(a, b)) return true;
            foreach (var key in a.Neighbours)
                if (parts.TryGetValue(key, out var joint) && joint.IsConnector && Linked(joint, b)) return true;
            foreach (var key in b.Neighbours)
                if (parts.TryGetValue(key, out var joint) && joint.IsConnector && Linked(joint, a)) return true;
            return false;
        }
    }
}
