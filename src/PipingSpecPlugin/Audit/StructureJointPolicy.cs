using System.Collections.Generic;
using System.Linq;

namespace PipingSpecPlugin.Audit
{
    // Plant 3D structure members are drawn axis to axis: a beam runs into the
    // column at its node and the bodies overlap there by design. An overlap
    // that holds the end of a member's axis is such a node, not a clash; a
    // member passing through another one mid-span holds no end and is a clash.
    public static class StructureJointPolicy
    {
        // box: minX, minY, minZ, maxX, maxY, maxZ of the overlap region;
        // ends: member axis end points {x, y, z}; slack widens the box (same
        // units) so an end lying on the region's face counts.
        public static bool IsNode(double[] box, IEnumerable<double[]> ends, double slack)
        {
            if (box == null || box.Length != 6 || ends == null) return false;
            return ends.Any(p => p != null && p.Length == 3
                && p[0] >= box[0] - slack && p[0] <= box[3] + slack
                && p[1] >= box[1] - slack && p[1] <= box[4] + slack
                && p[2] >= box[2] - slack && p[2] <= box[5] + slack);
        }
    }
}
