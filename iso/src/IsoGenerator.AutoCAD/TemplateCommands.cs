using System.IO;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using App = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(IsoGenerator.AutoCAD.TemplateCommands))]
namespace IsoGenerator.AutoCAD;

public class TemplateCommands
{
    [CommandMethod("ISOINSPECT", CommandFlags.Modal)]
    public void Inspect()
    {
        var destination = Environment.GetEnvironmentVariable("ISO_INSPECT_OUTPUT");
        if (string.IsNullOrWhiteSpace(destination))
            throw new InvalidOperationException("Set ISO_INSPECT_OUTPUT to the report filename.");
        var db = App.DocumentManager.MdiActiveDocument.Database;
        using var tr = db.TransactionManager.StartOpenCloseTransaction();
        var layers = new List<object>();
        foreach (ObjectId id in (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead))
        {
            var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            layers.Add(new { l.Name, colorIndex = l.Color.ColorIndex, l.IsOff, l.IsFrozen, l.IsLocked, l.IsPlottable });
        }
        var styles = new List<object>();
        foreach (ObjectId id in (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead))
        {
            var s = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
            styles.Add(new { s.Name, s.FileName, s.BigFontFileName, s.TextSize, s.XScale, s.ObliquingAngle });
        }
        var blocks = new List<object>();
        var warnings = new List<string>();
        foreach (ObjectId id in (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))
        {
            var b = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            var entities = new List<object>();
            foreach (ObjectId eid in b)
            {
                var e = (Entity)tr.GetObject(eid, OpenMode.ForRead);
                object? details = e switch
                {
                    AttributeDefinition a => new { a.Tag, a.Prompt, a.TextString, a.Constant, position = Point(a.Position), a.Height },
                    DBText t => new { t.TextString, position = Point(t.Position), t.Height, t.Rotation, t.Oblique, textStyle = ((TextStyleTableRecord)tr.GetObject(t.TextStyleId,OpenMode.ForRead)).Name },
                    MText m => new { m.Contents, position = Point(m.Location), m.TextHeight, m.Width },
                    MLeader leader => new {style=((MLeaderStyle)tr.GetObject(leader.MLeaderStyle,OpenMode.ForRead)).Name,text=leader.MText?.Contents,textPosition=Point(leader.TextLocation),leader.LeaderCount,leader.LeaderLineCount},
                    Dimension d => DimensionData(d),
                    Line l => new { start = Point(l.StartPoint), end = Point(l.EndPoint) },
                    Polyline p => new { p.Closed, vertices = Enumerable.Range(0, p.NumberOfVertices).Select(i => new { point = Point(p.GetPoint3dAt(i)), bulge = p.GetBulgeAt(i) }).ToArray() },
                    Viewport v => new { center = Point(v.CenterPoint), v.Width, v.Height, v.CustomScale, v.ViewHeight, v.TwistAngle, viewTarget = Point(v.ViewTarget), viewCenter = new[] { v.ViewCenter.X, v.ViewCenter.Y } },
                    Table t => TableData(t),
                    BlockReference r => Reference(r, tr),
                    _ => null
                };
                object? bounds = null;
                try { var x = e.GeometricExtents; bounds = new { min = Point(x.MinPoint), max = Point(x.MaxPoint) }; }
                catch (System.Exception ex) { warnings.Add($"{e.Handle}: extents: {ex.Message}"); }
                entities.Add(new { handle = e.Handle.ToString(), type = e.GetType().Name, e.Layer, e.ColorIndex, e.LineWeight, bounds, details, sourceLink = e.GetXDataForApplication("ISOGENERATOR")?.AsArray().Where(v=>v.TypeCode==1000).Select(v=>v.Value).ToArray() });
            }
            blocks.Add(new { b.Name, b.IsLayout, b.IsFromExternalReference, entities });
        }
        var layouts = new List<object>();
        foreach (DBDictionaryEntry entry in (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead))
        {
            var l = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
            layouts.Add(new { l.LayoutName, l.ModelType, l.TabOrder, l.PlotConfigurationName,
                l.CanonicalMediaName, l.PlotPaperUnits, l.PlotRotation,
                paper = new[] { l.PlotPaperSize.X, l.PlotPaperSize.Y } });
        }
        var report = new { schemaVersion = 1, source = db.Filename, units = db.Insunits.ToString(),
            currentTextStyle=((TextStyleTableRecord)tr.GetObject(db.Textstyle,OpenMode.ForRead)).Name,
            currentLeaderStyle=((MLeaderStyle)tr.GetObject(db.MLeaderstyle,OpenMode.ForRead)).Name,
            currentDimensionStyle=((DimStyleTableRecord)tr.GetObject(db.Dimstyle,OpenMode.ForRead)).Name,
            layers, textStyles = styles, layouts, blocks, warnings };
        File.WriteAllText(destination, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        App.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\nISOINSPECT: {blocks.Count} blocks, {layers.Count} layers. Report: {destination}\n");
    }
    static object DimensionData(Dimension d)
    {
        object? anchors=d switch
        {
            RotatedDimension r=>new { start=Point(r.XLine1Point), end=Point(r.XLine2Point), dimensionLine=Point(r.DimLinePoint), r.Rotation, r.Oblique },
            AlignedDimension a=>new { start=Point(a.XLine1Point), end=Point(a.XLine2Point), dimensionLine=Point(a.DimLinePoint), a.Oblique },
            _=>null
        };
        return new { d.DimensionText, d.Measurement, d.DimensionStyleName, textPosition=Point(d.TextPosition),d.TextRotation,d.Dimasz,d.Dimtsz,d.UsingDefaultTextPosition,anchors };
    }
    static double[] Point(Autodesk.AutoCAD.Geometry.Point3d p) => new[] { p.X, p.Y, p.Z };
    static object Reference(BlockReference r, Transaction tr)
    {
        var b = (BlockTableRecord)tr.GetObject(r.BlockTableRecord, OpenMode.ForRead);
        var attributes = new List<object>();
        foreach (ObjectId aid in r.AttributeCollection)
        {
            var a = (AttributeReference)tr.GetObject(aid, OpenMode.ForRead);
            attributes.Add(new { a.Tag, a.TextString, position = Point(a.Position) });
        }
        return new { block = b.Name, position = Point(r.Position), r.Rotation,
            scale = new[] { r.ScaleFactors.X, r.ScaleFactors.Y, r.ScaleFactors.Z }, attributes };
    }
    static object TableData(Table t)
    {
        var rows = new List<string[]>();
        for (int i = 0; i < t.Rows.Count; i++)
            rows.Add(Enumerable.Range(0, t.Columns.Count).Select(j => t.Cells[i, j].TextString).ToArray());
        return new { rows, textHeights=Enumerable.Range(0,t.Rows.Count).Select(i=>Enumerable.Range(0,t.Columns.Count).Select(j=>t.Cells[i,j].TextHeight).ToArray()).ToArray() };
    }
}
