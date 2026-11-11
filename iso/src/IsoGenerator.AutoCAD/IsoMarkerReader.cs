using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.ProcessPower.PnP3dObjects;
using IsoGenerator.Core;
using ModelPart=Autodesk.ProcessPower.PnP3dObjects.Part;
namespace IsoGenerator.AutoCAD;
internal static class IsoMarkerReader
{
    public static IReadOnlyList<ModelIsoMarker> Read(BlockTableRecord model,Transaction tr,CancellationToken token)
    {
        var result=new List<ModelIsoMarker>();using var manager=new ConnectionManager();
        foreach(ObjectId id in model)
        {
            token.ThrowIfCancellationRequested();if(id.ObjectClass.Name!="AcPpDb3dIsoSphereSymbol")continue;
            if(tr.GetObject(id,OpenMode.ForRead) is not ModelPart marker)continue;
            int kind=-1;
            if(!marker.ExtensionDictionary.IsNull)
            {
                var dictionary=(DBDictionary)tr.GetObject(marker.ExtensionDictionary,OpenMode.ForRead);
                if(dictionary.Contains("PnP3dInfo")&&tr.GetObject(dictionary.GetAt("PnP3dInfo"),OpenMode.ForRead) is Xrecord record)
                {using var data=record.Data;foreach(var value in data?.AsArray()??Array.Empty<Autodesk.AutoCAD.DatabaseServices.TypedValue>())if(value.TypeCode==90){kind=Convert.ToInt32(value.Value);break;}}
            }
            var ports=marker.GetPorts(PortType.All);if(ports.Count==0)continue;var point=ports[0].Position;
            var links=manager.GetConnections(id);
            for(int i=0;i<links.Count;i++)
            {
                var c=links[i];bool first=c.ObjectId1==id;var host=first?c.ObjectId2:c.ObjectId1;if(host.IsNull)continue;
                var port=first?c.Port2:c.Port1;
                result.Add(new(id.Handle.ToString(),host.Handle.ToString(),port.Name,new(point.X,point.Y,point.Z),kind));
            }
        }
        return result;
    }
}
