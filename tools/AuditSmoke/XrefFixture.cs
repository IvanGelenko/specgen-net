using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace AuditSmoke
{
    public partial class Commands
    {
        [CommandMethod("AUDITXREFFIXTURE")]
        public void XrefFixture()
        {
            var root = Environment.GetEnvironmentVariable("AUDIT_XREF_FIXTURE_ROOT");
            Directory.CreateDirectory(root);
            string FileName(string name) => Path.Combine(root, name + ".dwg");
            using (var leaf = new Database(true, true))
            {
                leaf.Insunits = UnitsValue.Millimeters;
                AddCube(leaf, new Point3d(10, 0, 0), 2, 4, 6);
                leaf.SaveAs(FileName("leaf"), DwgVersion.Current);
            }
            File.Copy(FileName("leaf"), FileName("unloaded"), true);
            File.Copy(FileName("leaf"), FileName("missing"), true);
            using (var outer = new Database(true, true))
            {
                outer.Insunits = UnitsValue.Millimeters;
                AddReference(outer, FileName("leaf"), "nested_leaf", new Point3d(0, 30, 0), Math.PI / 2, new Scale3d(1), false);
                outer.SaveAs(FileName("outer"), DwgVersion.Current);
            }
            var expected = new List<string>();
            using (var host = new Database(true, true))
            {
                host.Insunits = UnitsValue.Millimeters;
                // Leaf's centre is exactly (10,0,0), independent of CreateBox's origin.
                AddReference(host, FileName("leaf"), "direct_overlay", new Point3d(100, 0, 0), 0, new Scale3d(1), true);
                AddReference(host, FileName("leaf"), "rotated_scaled", new Point3d(200, 0, 0), Math.PI / 2, new Scale3d(2), false);
                AddReference(host, FileName("outer"), "outer_a", new Point3d(500, 0, 0), Math.PI / 2, new Scale3d(2), false);
                AddReference(host, FileName("outer"), "outer_b", new Point3d(1000, 0, 0), Math.PI / 2, new Scale3d(2), false);
                foreach (var point in new[] { new Point3d(110, 0, 0), new Point3d(200, 20, 0), new Point3d(420, 0, 0), new Point3d(920, 0, 0) })
                    expected.Add(AddCube(host, point, 1, 1, 1));
                // Negative controls: would collide if rotation/parent composition were lost.
                AddCube(host, new Point3d(220, 0, 0), 1, 1, 1);
                AddCube(host, new Point3d(500, 40, 0), 1, 1, 1);
                var unloaded = AddReference(host, FileName("unloaded"), "unloaded_ref", new Point3d(1500, 0, 0), 0, new Scale3d(1), false);
                host.UnloadXrefs(new ObjectIdCollection(new[] { unloaded }));
                AddReference(host, FileName("missing"), "missing_ref", new Point3d(1600, 0, 0), 0, new Scale3d(1), false);
                AddReference(host, FileName("leaf"), "nonuniform_ref", new Point3d(1700, 0, 0), 0, new Scale3d(2, 3, 1), false);
                host.SaveAs(FileName("host"), DwgVersion.Current);
            }
            // A deliberately broken path is part of the fixture, never a project file.
            File.Move(FileName("missing"), FileName("missing") + ".held", true);
            File.WriteAllText(Path.Combine(root, "expected.json"), System.Text.Json.JsonSerializer.Serialize(expected));
        }

        static ObjectId AddReference(Database db, string file, string name, Point3d point, double rotation, Scale3d scale, bool overlay)
        {
            var definition = overlay ? db.OverlayXref(file, name) : db.AttachXref(file, name);
            using var tr = db.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var reference = new BlockReference(point, definition) { Rotation = rotation, ScaleFactors = scale };
            space.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true); tr.Commit();
            return definition;
        }

        static string AddCube(Database db, Point3d centre, double x, double y, double z)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var solid = new Solid3d(); solid.CreateBox(x, y, z);
            solid.TransformBy(Matrix3d.Displacement(centre - solid.MassProperties.Centroid));
            space.AppendEntity(solid); tr.AddNewlyCreatedDBObject(solid, true); tr.Commit();
            return solid.Handle.ToString();
        }
    }
}
