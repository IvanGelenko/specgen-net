using System.IO;
using System.Reflection;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using App=Autodesk.AutoCAD.ApplicationServices.Core.Application;
[assembly:CommandClass(typeof(IsoGenerator.AutoCAD.IsoMarkerCommands))]
namespace IsoGenerator.AutoCAD;
public sealed class IsoMarkerCommands
{
    [CommandMethod("ISODIMINSPECT",CommandFlags.Modal)]
    public void InspectDimensions()
    {
        var db=App.DocumentManager.MdiActiveDocument.Database;using var tr=db.TransactionManager.StartOpenCloseTransaction();
        var model=(BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db),OpenMode.ForRead);var rows=new List<object>();
        foreach(ObjectId id in model)
        {
            if(tr.GetObject(id,OpenMode.ForRead) is not AlignedDimension d)
            {
                if(tr.GetObject(id,OpenMode.ForRead) is not Entity entity||!entity.Layer.StartsWith("ISO-",StringComparison.Ordinal))continue;
                var segments=new List<object>();
                void Read(Entity current,int depth)
                {
                    if(current is Line line){segments.Add(new{a=new[]{line.StartPoint.X,line.StartPoint.Y},b=new[]{line.EndPoint.X,line.EndPoint.Y}});return;}
                    if(depth>3||current is Table or MText or DBText)return;
                    using var collection=new DBObjectCollection();try{current.Explode(collection);foreach(DBObject child in collection){using(child)if(child is Entity e)Read(e,depth+1);}}catch(Autodesk.AutoCAD.Runtime.Exception){}
                }
                Read(entity,0);if(segments.Count>0)rows.Add(new{handle=id.Handle.ToString(),text="geometry:"+entity.Layer+":"+entity.GetType().Name,lines=segments});continue;
            }
            var lines=new List<object>();var primitives=new List<object>();if(!d.DimBlockId.IsNull)foreach(ObjectId child in (BlockTableRecord)tr.GetObject(d.DimBlockId,OpenMode.ForRead))
                {var entity=tr.GetObject(child,OpenMode.ForRead);primitives.Add(new{type=entity.GetType().Name,text=entity is MText mt?mt.Contents:entity is DBText dt?dt.TextString:null});if(entity is Line line)lines.Add(new{a=new[]{line.StartPoint.X,line.StartPoint.Y},b=new[]{line.EndPoint.X,line.EndPoint.Y}});}
            rows.Add(new{handle=id.Handle.ToString(),text=d.DimensionText,d.Measurement,d.Dimtad,textAt=new[]{d.TextPosition.X,d.TextPosition.Y},lines,primitives});
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("ISO_DIM_INSPECT_OUTPUT")!,JsonSerializer.Serialize(rows));
    }    [CommandMethod("ISOMARKERINSPECT",CommandFlags.Modal)]
    public void Inspect()
    {
        try
        {
            var enums=typeof(Autodesk.ProcessPower.PnP3dObjects.Part).Assembly.GetTypes().Where(t=>t.IsEnum).Where(t=>Enum.GetNames(t).Any(n=>n.Contains("Break",StringComparison.OrdinalIgnoreCase)||n.Contains("Split",StringComparison.OrdinalIgnoreCase)))
                .Select(t=>new{type=t.FullName,values=Enum.GetValues(t).Cast<object>().Select(v=>new{name=v.ToString(),value=Convert.ToInt64(v)}).ToArray()}).ToArray();
            File.WriteAllText(Environment.GetEnvironmentVariable("ISO_MARKER_INSPECT_OUTPUT")+".enums.json",JsonSerializer.Serialize(enums,new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(System.Exception ex){File.WriteAllText(Environment.GetEnvironmentVariable("ISO_MARKER_INSPECT_OUTPUT")+".enums-error.txt",ex.ToString());}
        var doc=App.DocumentManager.MdiActiveDocument;using var tr=doc.Database.TransactionManager.StartOpenCloseTransaction();
        var model=(BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database),OpenMode.ForRead);var result=new List<object>();
        object? Value(object? value)=>value switch{null=>null,ObjectId id=>id.IsNull?null:new{handle=id.Handle.ToString(),type=tr.GetObject(id,OpenMode.ForRead).GetType().Name,name=tr.GetObject(id,OpenMode.ForRead) is BlockTableRecord b?b.Name:null},Point3d point=>new[]{point.X,point.Y,point.Z},Vector3d vector=>new[]{vector.X,vector.Y,vector.Z},_=>value.ToString()};
        object Dictionary(ObjectId id)
        {
            var entries=new Dictionary<string,object?>();if(id.IsNull)return entries;
            if(tr.GetObject(id,OpenMode.ForRead) is DBDictionary dictionary)foreach(DBDictionaryEntry entry in dictionary)
            {
                var item=tr.GetObject(entry.Value,OpenMode.ForRead);
                entries[entry.Key]=item is Xrecord record?record.Data?.AsArray().Select(v=>new{code=v.TypeCode,value=Value(v.Value)}).ToArray():item is DBDictionary?Dictionary(entry.Value):item.GetType().Name;
            }
            return entries;
        }
        foreach(ObjectId id in model)
        {
            if(!id.ObjectClass.Name.StartsWith("AcPpDb3dIso",StringComparison.Ordinal))continue;var entity=(Entity)tr.GetObject(id,OpenMode.ForRead);var properties=new Dictionary<string,object?>();
            foreach(var property in entity.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public))
            {
                if(property.GetIndexParameters().Length>0||property.GetMethod is null)continue;
                var type=property.PropertyType;if(!type.IsPrimitive&&!type.IsEnum&&type!=typeof(string)&&type!=typeof(ObjectId)&&type!=typeof(Point3d)&&type!=typeof(Vector3d))continue;
                try{properties[property.Name]=Value(property.GetValue(entity));}catch(System.Exception ex){properties[property.Name]="ERROR: "+ex.GetBaseException().Message;}
            }
            result.Add(new{handle=id.Handle.ToString(),type=entity.GetType().FullName,properties,extension=Dictionary(entity.ExtensionDictionary),xdata=entity.XData?.AsArray().Select(v=>new{code=v.TypeCode,value=Value(v.Value)}).ToArray()});
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("ISO_MARKER_INSPECT_OUTPUT")!,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
    }
}
