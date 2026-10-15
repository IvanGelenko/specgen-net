using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PnP3dObjects;

namespace AuditSmoke
{
    public partial class Commands
    {
        // One line per model-space entity: type, Plant class / line / wall, and
        // what an explode chain turns it into (leaf types, solid count, errors).
        [CommandMethod("AUDITSHAPES")]
        public void Shapes()
        {
            var rows = new List<string>();
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            DataLinksManager dlm = null;
            try { dlm = DataLinksManager.GetManager(db); } catch (System.Exception ex) { rows.Add("DLM: " + ex.Message); }
            rows.Add($"INSUNITS={db.Insunits}");
            using var tr = db.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in space)
            {
                Entity entity;
                try { entity = tr.GetObject(id, OpenMode.ForRead) as Entity; }
                catch (System.Exception ex) { rows.Add($"{id.Handle} OPEN ERROR {ex.Message}"); continue; }
                if (entity == null) continue;
                var line = $"{id.Handle} {entity.GetType().Name} dxf={id.ObjectClass.DxfName} layer={entity.Layer}";
                if (entity is Part && dlm != null)
                {
                    try
                    {
                        var props = dlm.GetAllProperties(dlm.FindAcPpRowId(id), true);
                        string Get(string n) => props.FirstOrDefault(p => p.Key.Equals(n, StringComparison.OrdinalIgnoreCase)).Value;
                        line += $" class={Get("PnPClassName")} line={Get("LineNumberTag")} nd={Get("NominalDiameter")} wall={Get("WallThickness")} unit={Get("LengthUnit")} ins={Get("InsulationThickness")} desc={Get("PartSizeLongDesc")}";
                    }
                    catch (System.Exception ex) { line += " props-error=" + ex.Message; }
                }
                if (entity is BlockReference insert)
                {
                    var btr = (BlockTableRecord)tr.GetObject(insert.BlockTableRecord, OpenMode.ForRead);
                    line += $" block={btr.Name} xref={btr.IsFromExternalReference} overlay={btr.IsFromOverlayReference}";
                    if (btr.IsFromExternalReference) line += $" path={btr.PathName} status={btr.XrefStatus} unloaded={btr.IsUnloaded} pos={insert.Position} scale={insert.ScaleFactors} rot={insert.Rotation}";
                }
                if (entity is Pipe pipe) line += $" od={pipe.OuterDiameter} insD={pipe.InsulationDiameter}";
                else if (entity is InlineAsset asset) line += $" insD={asset.InsulationDiameter}";
                try { var e = entity.GeometricExtents; line += $" box=({e.MinPoint.X:0.#},{e.MinPoint.Y:0.#},{e.MinPoint.Z:0.#})-({e.MaxPoint.X:0.#},{e.MaxPoint.Y:0.#},{e.MaxPoint.Z:0.#})"; }
                catch (System.Exception ex) { line += " box-error=" + ex.Message; }
                var leaves = new Dictionary<string, int>();
                var errors = new List<string>();
                void Walk(Entity e, int depth)
                {
                    if (e is Solid3d) { leaves["Solid3d"] = leaves.GetValueOrDefault("Solid3d") + 1; return; }
                    if (e is BlockReference br && ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).IsFromExternalReference)
                    { leaves["XREF"] = leaves.GetValueOrDefault("XREF") + 1; return; }
                    if (depth >= 8) { errors.Add("depth"); return; }
                    using var pieces = new DBObjectCollection();
                    try
                    {
                        e.Explode(pieces);
                        if (pieces.Count == 0) errors.Add($"{e.GetType().Name}:empty");
                        foreach (DBObject piece in pieces)
                            if (piece is Entity child)
                            {
                                if (child is Solid3d || child is BlockReference) Walk(child, depth + 1);
                                else { var n = child.GetType().Name; leaves[n] = leaves.GetValueOrDefault(n) + 1; if (!(child is Curve || child is DBPoint || child is DBText || child is MText || child is Dimension || child is Hatch)) Walk(child, depth + 1); }
                            }
                    }
                    catch (System.Exception ex) { errors.Add($"{e.GetType().Name}:{ex.Message}"); }
                    finally { foreach (DBObject piece in pieces) piece.Dispose(); }
                }
                if (entity is Solid3d) leaves["Solid3d"] = 1; else Walk(entity, 0);
                line += " leaves=[" + string.Join(", ", leaves.Select(kv => $"{kv.Key}x{kv.Value}")) + "]";
                if (errors.Count > 0) line += " errors=[" + string.Join("; ", errors.Distinct().Take(6)) + "]";
                rows.Add(line);
            }
            File.WriteAllLines(Environment.GetEnvironmentVariable("AUDIT_SMOKE_OUTPUT") + ".shapes.txt", rows);
        }
    }
}
