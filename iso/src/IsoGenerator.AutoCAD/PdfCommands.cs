using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using Autodesk.AutoCAD.Runtime;
using App=Autodesk.AutoCAD.ApplicationServices.Core.Application;
[assembly: CommandClass(typeof(IsoGenerator.AutoCAD.PdfCommands))]
namespace IsoGenerator.AutoCAD;
public class PdfCommands
{
    [CommandMethod("ISOMERGEPDF",CommandFlags.Modal)]
    public void Merge()
    {
        var input=Environment.GetEnvironmentVariable("ISO_PDF_PAGES_INPUT")??throw new InvalidOperationException("ISO_PDF_PAGES_INPUT missing");
        var output=Environment.GetEnvironmentVariable("ISO_MERGED_PDF_OUTPUT")??throw new InvalidOperationException("ISO_MERGED_PDF_OUTPUT missing");
        var pages=System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(input))??throw new InvalidDataException("Empty PDF list");
        IsoGenerator.Publishing.PdfSetPublisher.Merge(pages,output,Path.GetFileNameWithoutExtension(output));
    }
    [CommandMethod("ISOPDF",CommandFlags.Modal)]
    public void Plot()
    {
        string output=Environment.GetEnvironmentVariable("ISO_PDF_OUTPUT")??throw new InvalidOperationException("ISO_PDF_OUTPUT missing");
        if(File.Exists(output))throw new IOException("PDF output exists");
        App.SetSystemVariable("BACKGROUNDPLOT",0);
        var doc=App.DocumentManager.MdiActiveDocument;
        LayoutManager.Current.CurrentLayout="А2-гСПДС лист1";
        using var tr=doc.Database.TransactionManager.StartTransaction();
        var layout=(Layout)tr.GetObject(LayoutManager.Current.GetLayoutId(LayoutManager.Current.CurrentLayout),OpenMode.ForRead);
        using var settings=new PlotSettings(false);settings.CopyFrom(layout);
        var v=PlotSettingsValidator.Current;
        v.SetPlotConfigurationName(settings,IsoEnvironmentChecks.PdfDevice(),null);v.RefreshLists(settings);
        var media=v.GetCanonicalMediaNameList(settings).Cast<string>().FirstOrDefault(s=>s.Contains("594.00_x_420.00")&&s.Contains("full_bleed"))
            ??v.GetCanonicalMediaNameList(settings).Cast<string>().First(s=>s.Contains("594.00_x_420.00"));
        v.SetCanonicalMediaName(settings,media);
        v.SetPlotPaperUnits(settings,PlotPaperUnit.Millimeters);v.SetPlotRotation(settings,PlotRotation.Degrees000);
        v.SetPlotType(settings,Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
        v.SetPlotWindowArea(settings,new Extents2d(0,0,594,420));
        v.SetUseStandardScale(settings,true);v.SetStdScaleType(settings,StdScaleType.ScaleToFit);v.SetPlotCentered(settings,true);
        settings.PrintLineweights=true;
        using var info=new PlotInfo{Layout=layout.ObjectId,OverrideSettings=settings};
        using var validator=new PlotInfoValidator{MediaMatchingPolicy=MatchingPolicy.MatchEnabled};validator.Validate(info);
        if(PlotFactory.ProcessPlotState!=ProcessPlotState.NotPlotting)throw new InvalidOperationException("Plot already running");
        using var engine=PlotFactory.CreatePublishEngine();
        engine.BeginPlot(null,null);engine.BeginDocument(info,doc.Name,null,1,true,output);
        using var page=new PlotPageInfo();engine.BeginPage(page,info,true,null);
        engine.BeginGenerateGraphics(null);engine.EndGenerateGraphics(null);engine.EndPage(null);engine.EndDocument(null);engine.EndPlot(null);
        doc.Editor.WriteMessage($"\nISOPDF: {output}\n");
    }
}
