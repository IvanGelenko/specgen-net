using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using App=Autodesk.AutoCAD.ApplicationServices.Core.Application;
[assembly: CommandClass(typeof(IsoGenerator.AutoCAD.SourceCommands))]
namespace IsoGenerator.AutoCAD;
public class SourceCommands
{
    [CommandMethod("ISOINFO",CommandFlags.Modal)]
    public void Info()
    {
        var doc=App.DocumentManager.MdiActiveDocument;
        var picked=doc.Editor.GetEntity("\nВыберите элемент изометрии: ");
        if(picked.Status!=PromptStatus.OK)return;
        using var tr=doc.Database.TransactionManager.StartOpenCloseTransaction();
        var entity=(Entity)tr.GetObject(picked.ObjectId,OpenMode.ForRead);
        using var data=entity.GetXDataForApplication("ISOGENERATOR");
        var values=data?.AsArray().Where(v=>v.TypeCode==1000).Select(v=>(string)v.Value).ToArray();
        if(values is null||values.Length<3){doc.Editor.WriteMessage("\nУ объекта нет связи с исходной моделью.");return;}
        doc.Editor.WriteMessage($"\nЧертёж модели: {values[1]}\nHandle: {values[0]}\nGUID: {values[2]}\n");
    }
}
