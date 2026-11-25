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
            var project=Autodesk.ProcessPower.PlantInstance.PlantApplication.CurrentProject??throw new InvalidOperationException("Откройте проект Plant 3D.");
            var piping=(Autodesk.ProcessPower.P3dProjectParts.PipingProject)project.ProjectParts["Piping"];
            string projectIso=Path.Combine(piping.ProjectDirectory,"Isometric");
            foreach(string filename in new[]{"IsoSkeyAcadBlockMap.xml","IsoSymbolStyles.dwg"})if(!File.Exists(Path.Combine(projectIso,filename)))throw new FileNotFoundException("Не найден файл изометрий активного проекта: "+Path.Combine(projectIso,filename));
            var catalog=AuditLineCatalog.Read(doc,logger.Log,forIsometric:true);
            var choices=catalog.Lines.Where(l=>l.Key!=AuditOptions.NoLine).Select(line=>new IsometricLineChoice(line.Key,
                LineDesignationParser.Parse(line.Key)?.NominalSize??string.Join(", ",catalog.Parts.Values.Where(p=>p.LineKey==line.Key&&!string.IsNullOrWhiteSpace(p.Diameter)).Select(p=>p.Diameter).Distinct()),line.Count)).ToArray();
            if(choices.Length==0){catalog.EnsureIsometricLinesReadable(null);System.Windows.Forms.MessageBox.Show("В текущем чертеже не найдены детали с номером линии.","Изометрии");return;}
            var settings=PluginSettings.Load();
            string outputFolder=Directory.Exists(settings.LastIsoOutputDirectory)?settings.LastIsoOutputDirectory:Path.Combine(Path.GetDirectoryName(doc.Name)!,"Изометрии");
            string defaultTemplate=Path.Combine(Path.GetDirectoryName(typeof(IsoGenerator.AutoCAD.GenerateCommand).Assembly.Location)!,"iso_template.dwg");
            string template=File.Exists(settings.LastIsoTemplatePath)?settings.LastIsoTemplatePath:defaultTemplate;
            string drawingKey=Path.GetFullPath(doc.Name).ToUpperInvariant();
            var selections=settings.IsoSelectedLinesByDrawing??=new();
            selections.TryGetValue(drawingKey,out var previousSelection);
            var savedViews=settings.IsoViewsByDrawing??=new();
            savedViews.TryGetValue(drawingKey,out var viewPreferences);
            using var dialog=new IsometricDialog(choices,doc.Name,outputFolder,template,settings.LastIsoSymbolScale,settings.LastIsoMinimumPipeLength,settings.IsoAdvanced,
                new IsoDocumentDetails(settings.LastIsoValvesDocument??"",settings.LastIsoSupportsDocument??"",
                    settings.LastIsoTitleTemplate??"",settings.LastIsoContentsTemplate??""),previousSelection,viewPreferences);
            var initialSelection=dialog.SelectedLines;
            var dialogResult=App.ShowModalDialog(dialog);
            var selectedLines=dialog.SelectedLines.ToArray();
            if(dialogResult!=System.Windows.Forms.DialogResult.OK)
            {
                if(!initialSelection.SequenceEqual(selectedLines,StringComparer.OrdinalIgnoreCase))
                {
                    selections[drawingKey]=selectedLines;
                    SaveSettings();
                }
                return;
            }
            catalog.EnsureIsometricLinesReadable(selectedLines);
            selections[drawingKey]=selectedLines;
            savedViews[drawingKey]=dialog.ViewPreferences;
            string selectedTemplate=dialog.TemplatePath;double selectedScale=dialog.SymbolScale;double selectedMinimum=dialog.MinimumPipeLength;
            var documents=dialog.Documents;
            settings.LastIsoTemplatePath=selectedTemplate;settings.LastIsoSymbolScale=selectedScale;settings.LastIsoMinimumPipeLength=selectedMinimum;settings.IsoAdvanced=dialog.AdvancedSettings;
            settings.LastIsoValvesDocument=documents.Valves;settings.LastIsoSupportsDocument=documents.Supports;
            settings.LastIsoTitleTemplate=documents.TitlePath;settings.LastIsoContentsTemplate=documents.ContentsPath;
            SaveSettings();
            void SaveSettings()
            {
                if(!settings.LoadFailed)try{settings.Save();}catch(System.Exception ex){logger.Log("Не удалось сохранить параметры изометрии: "+ex.Message);}
            }
            var partsByLine=catalog.Parts.Values.ToLookup(part=>part.LineKey,StringComparer.OrdinalIgnoreCase);
            var requests=selectedLines.Select(line=>
            {
                var selected=partsByLine[line];
                var hints=selected.ToDictionary(p=>p.Key,p=>(IReadOnlyDictionary<string,string>)new Dictionary<string,string>
                    {{"Tag",p.Tag??""},{"InsulationThickness",p.InsulationThickness??""}},StringComparer.OrdinalIgnoreCase);
                return new IsoGenerator.AutoCAD.GenerateCommand.LineRequest(line,selected.Select(p=>p.Key).ToArray(),hints,dialog.RememberLayout(line))
                {
                    ManualQuarterTurns=dialog.ManualQuarterTurns(line)
                };
            }).ToArray();
            string contentsTemplate=string.IsNullOrWhiteSpace(documents.ContentsPath)?IsometricDialog.DefaultContentsTemplate:documents.ContentsPath;
            var report=new IsoGenerator.Publishing.IsometricReportDetails(documents.TitlePath,contentsTemplate,dialog.TitleDetails);
            new IsoGenerator.AutoCAD.GenerateCommand().GenerateForLines(requests,dialog.OutputDirectory,projectIso,savedFolder=>
            {
                try
                {
                    var latest=PluginSettings.Load();if(latest.LoadFailed)return;
                    latest.LastIsoOutputDirectory=savedFolder;latest.Save();
                }
                catch(System.Exception ex){doc.Editor.WriteMessage("\n[Изометрия] Не удалось запомнить папку: "+ex.Message);}
            },dialog.QuarterTurns,dialog.ExcludeFlangeHardware,dialog.IgnoreBreakPoints,selectedTemplate,selectedScale,dialog.InstrumentCalloutsOnly,dialog.AutomaticView,documents.Valves,documents.Supports,dialog.TitleDetails.DocumentNumber,dialog.RecalculateLine,selectedMinimum,dialog.AdvancedSettings,report,drawingLines:catalog.DrawingLines());
        }
        catch(System.Exception ex){logger.Error(ex.ToString());doc.Editor.WriteMessage("\n[Изометрия] "+ex.Message);System.Windows.Forms.MessageBox.Show(ex.Message,"Изометрии");}
    }
}
