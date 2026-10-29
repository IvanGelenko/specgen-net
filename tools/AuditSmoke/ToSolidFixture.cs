using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.BoundaryRepresentation;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PipingSpecPlugin.Solids;

namespace AuditSmoke
{
    public partial class Commands
    {
        // PSTOSOLID core on the open drawing, nothing saved (transactions
        // aborted): 1) a fixture of blocks with known colours — ByBlock,
        // ByLayer on layer 0, own colour, nested insert, hidden layer, RGB —
        // must become one body with exactly those face colours; 2) the whole
        // model space (Plant 3D parts, structures, xrefs) is merged and
        // counted. Result: AUDIT_SMOKE_OUTPUT.
        [Autodesk.AutoCAD.Runtime.CommandMethod("TOSOLIDSMOKE")]
        public void ToSolid()
        {
            var path = Environment.GetEnvironmentVariable("AUDIT_SMOKE_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "tosolid-smoke.txt");
            var lines = new List<string>();
            try
            {
                var db = Application.DocumentManager.MdiActiveDocument.Database;
                var all = new List<ObjectId>();
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead)) all.Add(id);
                    tr.Commit();
                }

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                    ObjectId Layer(string name, short aci, bool off)
                    {
                        var layer = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, aci), IsOff = off };
                        var id = layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true); return id;
                    }
                    var green = Layer("ts_green", 3, false);
                    var hidden = Layer("ts_hidden", 4, true);
                    var zero = layers["0"];
                    var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    Solid3d Box(double x, Color color, ObjectId layer)
                    {
                        var box = new Solid3d(); box.SetDatabaseDefaults(db); box.CreateBox(10, 10, 10);
                        box.TransformBy(Matrix3d.Displacement(new Vector3d(x, 0, 0)));
                        box.Color = color; box.LayerId = layer; return box;
                    }
                    ObjectId Define(string name, params Entity[] content)
                    {
                        var record = new BlockTableRecord { Name = name };
                        var id = blocks.Add(record); tr.AddNewlyCreatedDBObject(record, true);
                        foreach (var e in content) { record.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); }
                        return id;
                    }
                    var byBlock = Color.FromColorIndex(ColorMethod.ByBlock, 0);
                    var byLayer = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                    var inner = Define("TS_BLOCK",
                        Box(0, byBlock, zero),                                   // → the insertion's colour
                        Box(20, byLayer, zero),                                  // → the insertion's layer colour
                        Box(40, Color.FromColorIndex(ColorMethod.ByAci, 5), zero), // blue
                        Box(60, Color.FromColorIndex(ColorMethod.ByAci, 2), hidden)); // off: skipped
                    var nested = new BlockReference(Point3d.Origin, inner) { Color = byBlock, LayerId = zero };
                    var outer = Define("TS_OUTER", nested);
                    var space = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    ObjectId Insert(ObjectId block, double y, Color color, double shift = 0)
                    {
                        // Overlapping copies, so the union is one lump.
                        var reference = new BlockReference(new Point3d(5000 + shift, y, shift), block) { Color = color, LayerId = green };
                        var id = space.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true); return id;
                    }
                    var fixture = new List<ObjectId>
                    {
                        Insert(inner, 0, Color.FromColorIndex(ColorMethod.ByAci, 1)),   // red
                        Insert(outer, 5, Color.FromColorIndex(ColorMethod.ByAci, 6), 3),   // magenta, through the nested ByBlock
                    };
                    var rgb = Box(5000, Color.FromRgb(20, 70, 130), green);
                    rgb.TransformBy(Matrix3d.Displacement(new Vector3d(6, 8, 7))); // no face in a plane of another (coplanar faces merge into one colour)
                    fixture.Add(space.AppendEntity(rgb)); tr.AddNewlyCreatedDBObject(rgb, true);

                    var expected = new[] { Color.FromColorIndex(ColorMethod.ByAci, 1), Color.FromColorIndex(ColorMethod.ByAci, 3), Color.FromColorIndex(ColorMethod.ByAci, 5),
                        Color.FromColorIndex(ColorMethod.ByAci, 6), Color.FromRgb(20, 70, 130) }.Select(c => c.ToString()).OrderBy(s => s).ToList();
                    var result = SolidMerger.Merge(db, tr, fixture, l => lines.Add("log: " + l));
                    var faces = new List<string>();
                    foreach (var id in result.Created)
                    {
                        var solid = (Solid3d)tr.GetObject(id, OpenMode.ForRead);
                        using var brep = SolidMerger.BrepOf(solid);
                        foreach (var face in brep.Faces)
                            try { faces.Add(solid.GetSubentityColor(face.SubentityPath.SubentId).ToString()); }
                            catch (System.Exception ex) { faces.Add("(нет: " + ex.Message + ")"); }
                    }
                    var got = faces.Distinct().OrderBy(s => s).ToList();
                    lines.Add("face colours: " + string.Join("; ", faces.GroupBy(f => f).Select(g => $"{g.Key} ×{g.Count()}")));
                    lines.Add($"fixture: created {result.Created.Count}, bodies {result.Bodies} (expected 7), faces {faces.Count}, colours [{string.Join("; ", got)}]");
                    lines.Add($"fixture: expected [{string.Join("; ", expected)}]");
                    lines.Add("fixture notes: " + string.Join(" | ", result.Notes));
                    lines.Add(result.Created.Count == 1 && result.Bodies == 7 && got.SequenceEqual(expected) ? "FIXTURE OK" : "FIXTURE FAILED");
                    // Not committed: the drawing is left as it was.
                }

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var started = DateTime.Now;
                    var result = SolidMerger.Merge(db, tr, all, l => lines.Add("log: " + l));
                    lines.Add($"model: objects {all.Count}, bodies {result.Bodies}, created {result.Created.Count}, source colours {result.SourceColors}, face colours {result.FaceColors}, painted faces {result.PaintedFaces}, unite failures {result.UniteFailures}, {(DateTime.Now - started).TotalSeconds:0.0} s");
                    lines.Add("model notes: " + string.Join(" | ", result.Notes));
                }

                // 3) «Удалить»: committed and saved beside the output file.
                SolidMergeResult merged;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    merged = SolidMerger.Merge(db, tr, all, l => lines.Add("log: " + l));
                    tr.Commit();
                }
                SolidMerger.EraseOriginals(Application.DocumentManager.MdiActiveDocument.Editor, merged, l => lines.Add("log: " + l));
                lines.Add($"erase: created {merged.Created.Count}, erased {merged.Erased} of {all.Count} ({merged.Converted.Count} converted); notes: {string.Join(" | ", merged.Notes)}");
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var kinds = new Dictionary<string, int>();
                    foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead))
                    {
                        var name = id.ObjectClass.DxfName;
                        kinds[name] = (kinds.TryGetValue(name, out var n) ? n : 0) + 1;
                    }
                    lines.Add("after erase, model space: " + string.Join(", ", kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}")));
                    tr.Commit();
                }
                var saved = Path.Combine(Path.GetDirectoryName(path), "tosolid-result.dwg");
                db.SaveAs(saved, DwgVersion.Current);
                lines.Add("saved " + saved);
            }
            catch (System.Exception ex) { lines.Add("ERROR " + ex); }
            File.WriteAllLines(path, lines);
        }
    }
}
