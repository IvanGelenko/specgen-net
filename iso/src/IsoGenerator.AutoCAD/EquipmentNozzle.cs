using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PnP3dObjects;

namespace IsoGenerator.AutoCAD;

internal static class EquipmentNozzle
{
    internal static string Read(DataLinksManager manager,DataLinksManager? projectManager,ObjectId id,Port? port)
    {
        if(port is null)return "";
        var tags=Find(manager);
        if(tags.Length==0&&projectManager is not null&&!ReferenceEquals(manager,projectManager))
            tags=Find(projectManager);
        return tags.Length==1?tags[0]:"";

        string[] Find(DataLinksManager links)
        {
            var tags=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(int row in NozzleRows(links,id))
            {
                var values=links.GetAllProperties(row,true).ToDictionary(p=>p.Key,p=>p.Value??"",StringComparer.OrdinalIgnoreCase);
                if(!values.GetValueOrDefault("PnPClassName","").Equals("Nozzle",StringComparison.OrdinalIgnoreCase))continue;

                // Nozzle rows can all call their own port S1. Use the position of
                // the connected equipment port to identify the actual nozzle.
                if(!ReadCoordinate("Position X",out double x)||!ReadCoordinate("Position Y",out double y)||
                    !ReadCoordinate("Position Z",out double z))continue;
                var position=port.Position;
                double dx=x-position.X,dy=y-position.Y,dz=z-position.Z;
                if(dx*dx+dy*dy+dz*dz>.0001)continue;
                var tag=values.GetValueOrDefault("Tag","").Trim();
                if(tag.Length==0)
                {
                    var number=values.GetValueOrDefault("Number","").Trim();
                    var type=values.GetValueOrDefault("Type","").Trim();
                    if(number.Length>0)tag=type.Length>0?type+"-"+number:number;
                }
                if(tag.Length>0)tags.Add(tag);

                bool ReadCoordinate(string key,out double coordinate)
                {
                    string value=values.GetValueOrDefault(key,"");
                    return (double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out coordinate)||
                        double.TryParse(value,NumberStyles.Float,CultureInfo.GetCultureInfo("ru-RU"),out coordinate))&&double.IsFinite(coordinate);
                }
            }
            return tags.ToArray();
        }
    }

    static IEnumerable<int> NozzleRows(DataLinksManager links,ObjectId id)
    {
        var owner=links.MakeAcPpObjectId(id);
        if(owner.IsNull)yield break;
        var tables=links.GetPnPDatabase().Tables;
        if(tables.Contains("PnPDataLinks"))
        {
            // Use the persisted drawing identity. Subpart indexes on the
            // equipment wrapper need not enumerate its linked nozzle rows.
            int low=unchecked((int)owner.dbHandle),high=unchecked((int)(owner.dbHandle>>32));
            string filter=FormattableString.Invariant(
                $"DwgId = {owner.DwgId} AND DwgHandleLow = {low} AND DwgHandleHigh = {high} AND DwgSubIndex > 0 AND RowClassName = 'Nozzle'");
            var rows=tables["PnPDataLinks"].Select(filter);
            if(rows.Length>0)
            {
                foreach(var row in rows)yield return Convert.ToInt32(row["RowId"],CultureInfo.InvariantCulture);
                yield break;
            }
        }

        // Some project managers hide the system table. Find the same links
        // from the nozzle rows through the managed DataLinks API.
        foreach(int row in links.GetItemIdsByType("Nozzle"))
        {
            using var objects=links.FindAcPpObjectIds(row);
            foreach(PpObjectId nozzle in objects)
            {
                if(nozzle.DwgId!=owner.DwgId||nozzle.dbHandle!=owner.dbHandle||nozzle.SubIndex<=0)continue;
                yield return row;
                break;
            }
        }
    }
}
