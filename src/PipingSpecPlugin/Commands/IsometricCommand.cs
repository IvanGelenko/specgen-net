using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.Runtime;
using PipingSpecPlugin.Audit;
using PipingSpecPlugin.Settings;
using PipingSpecPlugin.Spec;
using PipingSpecPlugin.UI;
using App=Autodesk.AutoCAD.ApplicationServices.Application;
namespace PipingSpecPlugin.Commands;
public sealed class IsometricCommand
{
    [CommandMethod("PSISOMETRIC",CommandFlags.Modal)]
    public void Run()
    {
        var doc=App.DocumentManager.MdiActiveDocument;if(doc==null)return;
        using var logger=new RunLogger("PSISOMETRIC");
        try
        {
            if(!DrawingSaveGuard.EnsureSavedForDiskRead(doc,doc.Database,"Изометрии",logger.Log))return;
            // Exactly the same fresh saved-DWG reader, resolved line tags and stable handles as audit / SELECTBYLINE.
            var project=Autodesk.ProcessPower.PlantInstance.PlantApplication.CurrentProject??throw new InvalidOperationException("Откройте проект Plant 3D.");
            var piping=(Autodesk.ProcessPower.P3dProjectParts.PipingProject)project.ProjectParts["Piping"];
            string projectIso=Path.Combine(piping.ProjectDirectory,"Isometric");
            foreach(string filename in new[]{"IsoSkeyAcadBlockMap.xml","IsoSymbolStyles.dwg"})if(!File.Exists(Path.Combine(projectIso,filename)))throw new FileNotFoundException("Не найден файл изометрий активного проекта: "+Path.Combine(projectIso,filename));
            var catalog=AuditLineCatalog.Read(doc,logger.Log);
            var choices=catalog.Lines.Where(l=>l.Key!=AuditOptions.NoLine).Select(line=>new IsometricLineChoice(line.Key,
                LineDesignationParser.Parse(line.Key)?.NominalSize??string.Join(", ",catalog.Parts.Values.Where(p=>p.LineKey==line.Key&&!string.IsNullOrWhiteSpace(p.Diameter)).Select(p=>p.Diameter).Distinct()),line.Count)).ToArray();
            if(choices.Length==0){System.Windows.Forms.MessageBox.Show("В текущем чертеже не найдены детали с номером линии.","Изометрии");return;}
            var settings=PluginSettings.Load();
            string outputFolder=Directory.Exists(settings.LastIsoOutputDirectory)?settings.LastIsoOutputDirectory:Path.Combine(Path.GetDirectoryName(doc.Name)!,"Изометрии");
            string defaultTemplate=Path.Combine(Path.GetDirectoryName(typeof(IsoGenerator.AutoCAD.GenerateCommand).Assembly.Location)!,"iso_template.dwg");
            string template=File.Exists(settings.LastIsoTemplatePath)?settings.LastIsoTemplatePath:defaultTemplate;
            using var dialog=new IsometricDialog(choices,doc.Name,outputFolder,template,settings.LastIsoSymbolScale);
            if(App.ShowModalDialog(dialog)!=System.Windows.Forms.DialogResult.OK)return;
            string selectedTemplate=dialog.TemplatePath;double selectedScale=dialog.SymbolScale;
            settings.LastIsoTemplatePath=selectedTemplate;settings.LastIsoSymbolScale=selectedScale;
            if(!settings.LoadFailed)try{settings.Save();}catch(System.Exception ex){logger.Log("Не удалось сохранить параметры изометрии: "+ex.Message);}
            var selected=catalog.Parts.Values.Where(p=>p.LineKey==dialog.SelectedLine).ToArray();
            var hints=selected.ToDictionary(p=>p.Key,p=>(IReadOnlyDictionary<string,string>)new Dictionary<string,string>{{"Tag",p.Tag??""},{"InsulationThickness",p.InsulationThickness??""}},StringComparer.OrdinalIgnoreCase);
            new IsoGenerator.AutoCAD.GenerateCommand().GenerateForLine(dialog.SelectedLine,selected.Select(p=>p.Key).ToArray(),dialog.OutputDirectory,hints,projectIso,savedFolder=>
            {
                try
                {
                    var latest=PluginSettings.Load();if(latest.LoadFailed)return;
                    latest.LastIsoOutputDirectory=savedFolder;latest.LastIsoTemplatePath=selectedTemplate;latest.LastIsoSymbolScale=selectedScale;latest.Save();
                }
                catch(System.Exception ex){doc.Editor.WriteMessage("\n[Изометрия] Не удалось запомнить папку: "+ex.Message);}
            },dialog.QuarterTurns,dialog.ExcludeFlangeHardware,dialog.IgnoreBreakPoints,selectedTemplate,selectedScale);
        }
        catch(System.Exception ex){logger.Error(ex.ToString());doc.Editor.WriteMessage("\n[Изометрия] "+ex.Message);System.Windows.Forms.MessageBox.Show(ex.Message,"Изометрии");}
    }
}
