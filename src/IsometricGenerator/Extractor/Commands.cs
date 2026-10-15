using System.Text.Json;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.PnP3dObjects;
using System.Reflection;
using Autodesk.ProcessPower.DataLinks;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(IsometricGenerator.Extractor.Commands))]
namespace IsometricGenerator.Extractor;

public class Commands
{
    [CommandMethod("ISOEXTRACT", CommandFlags.Modal)]
    public void Extract()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var destination = Environment.GetEnvironmentVariable("ISO_EXTRACT_OUTPUT");
        if (string.IsNullOrWhiteSpace(destination)) throw new InvalidOperationException("ISO_EXTRACT_OUTPUT is missing.");
        var rows = new List<object>();
        var errors = new List<string>();
        var warnings = new List<string>();
        DataLinksManager? dlm = null;
        try { dlm = DataLinksManager.GetManager(doc.Database); }
        catch (System.Exception ex) { warnings.Add("Drawing properties: " + ex.Message); }
        using var tr = doc.Database.TransactionManager.StartOpenCloseTransaction();
        var space = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForRead);
        foreach (ObjectId id in space)
        {
            if (!id.ObjectClass.Name.StartsWith("AcPp", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is not Part part) { errors.Add($"{id.Handle}: unsupported {id.ObjectClass.Name}"); continue; }
                var ports = part.GetPorts(PortType.Both);
                var portRows = new List<object>();
                for (int i = 0; i < ports.Count; i++)
                {
                    var port = ports[i];
                    portRows.Add(new { name = port.Name, position = new[] {port.Position.X,port.Position.Y,port.Position.Z}, direction = new[] {port.Direction.X,port.Direction.Y,port.Direction.Z} });
                }
                var properties = new Dictionary<string,object?>();
                foreach (var p in part.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance))
                {
                    if (p.GetIndexParameters().Length != 0) continue;
                    if (p.PropertyType != typeof(string) && p.PropertyType != typeof(double) && p.PropertyType != typeof(int)) continue;
                    try { properties[p.Name] = p.GetValue(part); } catch { }
                }
                var attributes = new Dictionary<string,string>();
                int rowId = -1;
                if (dlm != null)
                {
                    try
                    {
                        rowId = dlm.FindAcPpRowId(id);
                        if (rowId >= 0) foreach (var pair in dlm.GetAllProperties(rowId,true)) attributes[pair.Key] = pair.Value;
                    }
                    catch (System.Exception ex) { warnings.Add($"{id.Handle}: properties: {ex.Message}"); }
                }
                rows.Add(new {handle=id.Handle.ToString(), rowId, className=id.ObjectClass.Name, managedType=part.GetType().FullName, ports=portRows, properties, attributes});
            }
            catch (System.Exception ex) { errors.Add($"{id.Handle}: {ex.Message}"); }
        }
        File.WriteAllText(destination,JsonSerializer.Serialize(new {schemaVersion=1,parts=rows,errors,warnings},new JsonSerializerOptions{WriteIndented=true}));
        doc.Editor.WriteMessage($"\nISOEXTRACT: {rows.Count} parts, {errors.Count} errors.\n");
    }
}
