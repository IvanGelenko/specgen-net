using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using IsoGenerator.Core;
namespace IsoGenerator.AutoCAD;
// Bake the library's canonical 2D curves through the isometric frame into a named block.
// Curved segments are sampled because an oblique projection turns circles into ellipses.
internal sealed class SymbolLibrary : IDisposable
{
    readonly Database source=new(false,true);
    public SymbolLibrary(string path){source.ReadDwgFile(path,FileOpenMode.OpenForReadAndAllShare,true,null);source.CloseInput(true);}
    public ObjectId CreateGlyph(SymbolGlyph glyph,Database target,Transaction transaction)
    {
        using var read=source.TransactionManager.StartOpenCloseTransaction();
        var blocks=(BlockTable)read.GetObject(source.BlockTableId,OpenMode.ForRead);
        if(!blocks.Has(glyph.Block))throw new InvalidDataException($"ISO block '{glyph.Block}' is missing in the symbol library");
        var definition=(BlockTableRecord)read.GetObject(blocks[glyph.Block],OpenMode.ForRead);
        var targetBlocks=(BlockTable)transaction.GetObject(target.BlockTableId,OpenMode.ForWrite);
        var output=new BlockTableRecord{Name=$"ISO-{glyph.SourceId}-{glyph.Block}{(glyph.Variant.Length>0?"-"+glyph.Variant:"")}"};
        var id=targetBlocks.Add(output);transaction.AddNewlyCreatedDBObject(output,true);
        if(glyph.LocalContours is {Count:>0})
        {
            foreach(var contour in glyph.LocalContours)
            {
                if(contour.Count<2)continue;
                var polyline=new Polyline();int vertex=0;
                foreach(var point in contour)
                {
                    if(!double.IsFinite(point.X)||!double.IsFinite(point.Y))throw new InvalidDataException($"{glyph.Block}: non-finite library contour");
                    polyline.AddVertexAt(vertex++,new Point2d(glyph.XAxis.X*point.X+glyph.YAxis.X*point.Y,glyph.XAxis.Y*point.X+glyph.YAxis.Y*point.Y),0,0,0);
                }
                polyline.Layer="0";polyline.ColorIndex=0;polyline.LineWeight=LineWeight.ByBlock;polyline.Linetype="ByBlock";
                output.AppendEntity(polyline);transaction.AddNewlyCreatedDBObject(polyline,true);
            }
            return id;
        }
        int count=0;
        Point2d Project(Point3d p)=>new(glyph.XAxis.X*p.X+glyph.YAxis.X*p.Y,glyph.XAxis.Y*p.X+glyph.YAxis.Y*p.Y);
        foreach(ObjectId entityId in definition)
        {
            var entity=read.GetObject(entityId,OpenMode.ForRead);
            var trace=Environment.GetEnvironmentVariable("ISO_RENDER_TRACE");if(!string.IsNullOrEmpty(trace))File.AppendAllText(trace,"  entity "+entity.GetType().Name+" "+entity.Handle+Environment.NewLine);
            if(entity is Wipeout or Hatch)continue; // No underlying centerline is drawn through component bodies.
            if(entity is not Curve curve)throw new InvalidDataException($"{glyph.Block}: unsupported library entity {entity.GetType().Name}");
            var result=new Polyline();int vertex=0;
            void Vertex(Point3d p){result.AddVertexAt(vertex++,Project(p),0,0,0);}
            if(curve is Polyline poly)
            {
                int segments=poly.Closed?poly.NumberOfVertices:poly.NumberOfVertices-1;
                for(int segment=0;segment<segments;segment++)
                {
                    int steps=Math.Abs(poly.GetBulgeAt(segment))<1e-10?1:24;
                    for(int step=0;step<steps;step++)Vertex(poly.GetPointAtParameter(segment+(double)step/steps));
                }
                if(!poly.Closed)Vertex(poly.EndPoint);
                result.Closed=poly.Closed;
            }
            else if(curve is Line){Vertex(curve.StartPoint);Vertex(curve.EndPoint);}
            else
            {
                int steps=curve is Circle?96:48;
                for(int i=0;i<=steps;i++)Vertex(curve.GetPointAtParameter(curve.StartParam+(curve.EndParam-curve.StartParam)*i/steps));
            }
            result.Layer="0";result.ColorIndex=0;result.LineWeight=LineWeight.ByBlock;result.Linetype="ByBlock";
            output.AppendEntity(result);transaction.AddNewlyCreatedDBObject(result,true);count++;
        }
        if(count==0)throw new InvalidDataException($"Empty symbol block {glyph.Block}");
        return id;
    }
    public void Dispose()=>source.Dispose();
}
