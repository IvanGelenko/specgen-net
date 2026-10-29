using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Geometry;

namespace Plant3dMcp
{
    // Design rules taken from GOST 32569-2013 (see RULES-GOST-32569.md). Each helper names its clause.
    static class DesignRules
    {
        // Clause 6.8.1: distance between adjacent circumferential butt welds, mm: >= 3*s and >= 100 (OD<=219), 250 (OD<=550), 400 (OD>550).
        public static double WeldSpacingMin(double od, double wall)
        {
            double floor = od <= 219.0 + 0.05 ? 100 : od <= 550.0 + 0.05 ? 250 : 400;
            return Math.Max(floor, 3 * wall);
        }

        // Clause 6.8.4: clearance from the outer surface of a welded-in branch to the start of a bend or a transverse butt weld.
        public static double BranchClearanceMin(double branchOd) => branchOd < 100 ? Math.Max(branchOd, 50) : 100;

        // Table 10.3 (clause 10.2.7): minimum DN of drain stubs and vents.
        public static int VentDrainMinDn(double dn)
        {
            if (dn <= 80) return 15;
            if (dn <= 150) return 20;
            if (dn <= 300) return 25;
            if (dn <= 450) return 32;
            if (dn <= 700) return 40;
            return 50;
        }

        // Table 10.1 (clause 10.2.4): DN of a drain pocket for continuous drainage; null below DN100 (no pockets).
        public static int? DrainPocketDn(double dn)
        {
            if (dn < 100) return null;
            if (dn <= 125) return 50;
            if (dn <= 175) return 80;
            if (dn <= 250) return 100;
            if (dn <= 350) return 150;
            if (dn <= 450) return 200;
            if (dn <= 600) return 250;
            if (dn <= 800) return 300;
            return 350;
        }

        // Clause 6.4.1 / 3.1.17: a "steep" (krutoizognuty) elbow has a bend radius of at most 1.5 DN.
        public const double SteepElbowRadiusFactor = 1.5;

        // Clause 10.1.4: slope = drop / horizontal run along the pipe, positive downhill from a to b. Null for a vertical pipe.
        public static double? Slope(Point3d a, Point3d b)
        {
            double run = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (run < 1e-6) return null;
            return (a.Z - b.Z) / run;
        }

        public const double MinSlope = 0.002;   // clause 10.1.4: liquids, gas along the flow; 0.003 gas against the flow, 0.005 acids/alkalis

        public static string SlopeNote(string what, double? slope)
        {
            if (slope == null) return null;
            double s = slope.Value;
            if (Math.Abs(s) < 1e-9) return what + ": no slope. GOST 32569 cl. 10.1.4 requires a slope of at least 0.002 (2 mm/m) so the line can be drained; no slope is allowed only with justification and drainage measures.";
            if (s < 0) return what + ": falls toward its START (slope " + Math.Round(-s * 1000, 3) + " mm/m the other way); check the intended flow direction.";
            if (s < MinSlope - 1e-9) return what + ": slope " + Math.Round(s * 1000, 3) + " mm/m is below the minimum 2 mm/m (cl. 10.1.4; 3 mm/m for gas against the flow, 5 mm/m for acids and alkalis).";
            return null;
        }

        // Clause 10.2.1: vents at upper points and drains at lower points. Returns notes for the high and low points of a polyline
        // (vertices plus both ends); 'dn' is the nominal diameter of the line.
        public static IEnumerable<string> VentDrainNotes(IList<Point3d> pts, double dn)
        {
            if (pts.Count < 2) yield break;
            double maxZ = pts.Max(p => p.Z), minZ = pts.Min(p => p.Z);
            string vent = "vent (air release) of at least DN" + VentDrainMinDn(dn) + " (table 10.3)";
            string drain = "drain of at least DN" + VentDrainMinDn(dn) + " (table 10.3)" + (DrainPocketDn(dn) is int pk ? ", and a drain pocket DN" + pk + " if continuous drainage is needed (table 10.1)" : "");
            if (maxZ - minZ < 1e-6)
            {
                yield return "Horizontal line: GOST 32569 cl. 10.2.1 still requires a drain (low point) and a vent (high point) on every pipeline; the project decides where.";
                yield break;
            }
            foreach (var p in pts.Where(p => Math.Abs(p.Z - maxZ) < 1e-6).Take(3))
                yield return "High point (" + Math.Round(p.X, 1) + ", " + Math.Round(p.Y, 1) + ", " + Math.Round(p.Z, 1) + "): cl. 10.2.1 requires a " + vent + ".";
            foreach (var p in pts.Where(p => Math.Abs(p.Z - minZ) < 1e-6).Take(3))
                yield return "Low point (" + Math.Round(p.X, 1) + ", " + Math.Round(p.Y, 1) + ", " + Math.Round(p.Z, 1) + "): cl. 10.2.1 requires a " + drain + ".";
        }
    }
}
