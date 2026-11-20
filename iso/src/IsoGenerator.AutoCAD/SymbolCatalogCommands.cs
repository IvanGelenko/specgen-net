using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using IsoGenerator.Core;
[assembly:CommandClass(typeof(IsoGenerator.AutoCAD.SymbolCatalogCommands))]
namespace IsoGenerator.AutoCAD;
public sealed class SymbolCatalogCommands
{
    [CommandMethod("ISOSYMBOLCATALOG",CommandFlags.Modal)]
    public void Export()
    {
        var input=Environment.GetEnvironmentVariable("ISO_SYMBOL_LIBRARY")??throw new InvalidOperationException("ISO_SYMBOL_LIBRARY missing");
        var output=Environment.GetEnvironmentVariable("ISO_SYMBOL_CATALOG_OUTPUT")??throw new InvalidOperationException("ISO_SYMBOL_CATALOG_OUTPUT missing");
        try{File.WriteAllText(output,JsonSerializer.Serialize(Read(input),new JsonSerializerOptions{WriteIndented=true}));}catch(System.Exception ex){File.WriteAllText(output+".error",ex.ToString());throw;}
    }
    public static IsoBlockCatalog Read(string filename,CancellationToken cancellationToken=default,Action<int>? report=null)
    {
        using var db=new Database(false,true);db.ReadDwgFile(filename,FileOpenMode.OpenForReadAndAllShare,true,null);db.CloseInput(true);
        using var tr=db.TransactionManager.StartTransaction();
        var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);var model=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForWrite);
        var definitions=new List<IsoBlockDefinition>();
        var rejected=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(ObjectId id in bt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(definitions.Count%20==0)report?.Invoke(definitions.Count);
            var block=(BlockTableRecord)tr.GetObject(id,OpenMode.ForRead);if(block.IsLayout||block.IsAnonymous||block.IsFromExternalReference)continue;
            var points=new List<Point3d>();var contours=new List<IReadOnlyList<SheetPoint>>();
            var active=new HashSet<ObjectId>();
            bool complex=false;
            void ReadCurves(BlockTableRecord definition,Matrix3d transform,int depth)
            {
                if(depth>24||!active.Add(definition.ObjectId)){complex=true;return;}
                foreach(ObjectId entityId in definition)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if(tr.GetObject(entityId,OpenMode.ForRead) is not Entity entity||!entity.Visible)continue;
                    if(entity is BlockReference reference)
                    {
                        var nested=(BlockTableRecord)tr.GetObject(reference.BlockTableRecord,OpenMode.ForRead);
                        ReadCurves(nested,transform*reference.BlockTransform,depth+1);
                    }
                    else if(entity is Curve curve)
                    {
                        try
                        {
                            var path=new List<SheetPoint>();
                            void Add(Point3d p)
                            {
                                var at=p.TransformBy(transform);
                                if(!double.IsFinite(at.X)||!double.IsFinite(at.Y))
                                    throw new InvalidDataException("Некорректная точка в блоке "+block.Name);
                                path.Add(new(at.X,at.Y));
                            }
                            if(curve is Polyline poly)
                            {
                                int segments=poly.Closed?poly.NumberOfVertices:poly.NumberOfVertices-1;
                                for(int segment=0;segment<segments;segment++)
                                {
                                    int next=(segment+1)%poly.NumberOfVertices;
                                    var start=poly.GetPoint3dAt(segment);
                                    if(start.DistanceTo(poly.GetPoint3dAt(next))<1e-9)continue;
                                    Add(start);
                                    int steps=Math.Abs(poly.GetBulgeAt(segment))<1e-10?1:24;
                                    for(int step=1;step<steps;step++)Add(poly.GetPointAtParameter(segment+(double)step/steps));
                                }
                                if(poly.NumberOfVertices>0)Add(poly.GetPoint3dAt(poly.Closed?0:poly.NumberOfVertices-1));
                            }
                            else if(curve is Line){Add(curve.StartPoint);Add(curve.EndPoint);}
                            else for(int i=0;i<=64;i++)
                                Add(curve.GetPointAtParameter(curve.StartParam+(curve.EndParam-curve.StartParam)*i/64));
                            if(path.Count<2){complex=true;continue;}
                            contours.Add(path);
                            points.AddRange(path.Select(point=>new Point3d(point.X,point.Y,0)));
                        }
                        catch(System.Exception ex) when(ex is Autodesk.AutoCAD.Runtime.Exception or InvalidDataException)
                        {complex=true;}
                    }
                    else if(entity is not (Wipeout or Hatch or AttributeDefinition or DBPoint))
                    complex=true;
                }
                active.Remove(definition.ObjectId);
            }
            ReadCurves(block,Matrix3d.Identity,0);
            // A partial block would obscure the failure; use the planner's identified placeholder instead.
            if(complex||points.Count==0)
            {
                rejected[block.Name]=complex?"не удалось прочитать контур целиком":"в блоке нет читаемых видимых контуров";
                continue;
            }
            var ports=new Dictionary<string,SheetPoint>(StringComparer.OrdinalIgnoreCase);
            using var reference=new BlockReference(Point3d.Origin,id);model.AppendEntity(reference);tr.AddNewlyCreatedDBObject(reference,true);
            if(reference.IsDynamicBlock)
            {
                foreach(DynamicBlockReferenceProperty property in reference.DynamicBlockReferencePropertyCollection)
                {
                    var match=Regex.Match(property.PropertyName,@"^(Port\d+|Center\s*Point|CP)(?:\s+([XYZ]))?$",RegexOptions.IgnoreCase);if(!match.Success)continue;
                    if(match.Groups[2].Value.Equals("Z",StringComparison.OrdinalIgnoreCase))continue;
                    string key=(match.Groups[1].Value.StartsWith("Center",StringComparison.OrdinalIgnoreCase)||match.Groups[1].Value.Equals("CP",StringComparison.OrdinalIgnoreCase))?"CenterPoint":match.Groups[1].Value;var old=ports.GetValueOrDefault(key);
                    if(property.Value is Point3d point)ports[key]=new(point.X,point.Y);
                    else if(property.Value is double n)ports[key]=match.Groups[2].Value.ToUpperInvariant()=="Y"?new(old.X,n):new(n,old.Y);
                }
            }
            reference.Erase();
            definitions.Add(new(block.Name,ports,new(points.Min(p=>p.X),points.Min(p=>p.Y)),new(points.Max(p=>p.X),points.Max(p=>p.Y)),contours));
        }
        // The source DWG is never saved: temporary references exist only in this disposable database.
        return new(definitions,RejectedBlocks:rejected);
    }
}
