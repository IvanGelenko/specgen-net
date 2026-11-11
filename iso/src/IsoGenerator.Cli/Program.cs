using System.Text.Json;
using IsoGenerator.Core;
try {
if(args.Length<2||args.Length>4)throw new ArgumentException("Usage: snapshot.json scene.json [symbol-map.xml] [--sheets]");
IProgress<PlanningProgress>? progress=Environment.GetEnvironmentVariable("ISO_LAYOUT_PROGRESS_LOG") is {} progressFile?new PlanningLog(progressFile):null;
var snapshot=JsonSerializer.Deserialize<PipelineSnapshot>(File.ReadAllText(args[0]))??throw new Exception("Missing snapshot");
var validation=SnapshotValidation.Validate(snapshot);
File.WriteAllText(Path.ChangeExtension(args[1],"validation.json"),JsonSerializer.Serialize(validation,new JsonSerializerOptions{WriteIndented=true}));
var catalogFile=args.Length>=3?Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2]))!,"IsoSymbolStyles.symbols.json"):"";
var catalog=File.Exists(catalogFile)?JsonSerializer.Deserialize<IsoBlockCatalog>(File.ReadAllText(catalogFile)):null;
double symbolScale=10;
if(Environment.GetEnvironmentVariable("ISO_SYMBOL_SCALE") is {} rawScale&&(!double.TryParse(rawScale,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out symbolScale)||!double.IsFinite(symbolScale)||symbolScale<=0))throw new ArgumentException("ISO_SYMBOL_SCALE must be finite and positive");
if(catalog is not null)catalog=catalog with{SymbolScale=symbolScale,Style=null};
var map=args.Length>=3?new IsoSymbolMap(File.ReadAllText(args[2]),catalog):null;
int quarterTurns=int.TryParse(Environment.GetEnvironmentVariable("ISO_QUARTER_TURNS"),out var turns)?turns:0;
if(args.Length==4)
{
    if(args[3]!="--sheets")throw new ArgumentException("Unknown option "+args[3]);
    var sheets=SheetSetPlanner.Build(snapshot,map,quarterTurns,Environment.GetEnvironmentVariable("ISO_EXCLUDE_FLANGE_HARDWARE")=="1",progress:progress,ignoreBreakPoints:Environment.GetEnvironmentVariable("ISO_IGNORE_BREAK_POINTS")=="1");
    foreach(var sheet in sheets)
    {
        var target=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!,Path.GetFileNameWithoutExtension(args[1])+$"-sheet-{sheet.Number:00}.json");
        File.WriteAllText(target,JsonSerializer.Serialize(sheet.Scene,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Sheet {sheet.Number}/{sheet.Total}: {target}");
    }
    return;
}
var plans=SheetSetPlanner.Build(snapshot,map,quarterTurns,Environment.GetEnvironmentVariable("ISO_EXCLUDE_FLANGE_HARDWARE")=="1",progress:progress,ignoreBreakPoints:Environment.GetEnvironmentVariable("ISO_IGNORE_BREAK_POINTS")=="1");
if(plans.Count!=1)throw new SheetCapacityException("Линии требуется несколько листов. Используйте --sheets.");
var scene=plans[0].Scene;
File.WriteAllText(args[1],JsonSerializer.Serialize(scene,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{scene.SourcePartCount} source objects; {scene.Strokes.Count} strokes; {scene.Dimensions.Count(d=>d.Role=="chain")} chain dimensions, {scene.Dimensions.Count(d=>d.Role=="overall")} overall dimensions, {scene.Welds?.Count??0} welds; {scene.BillOfMaterials.Count} BOM rows; {validation.ConnectedComponents} connected component(s); {scene.Warnings.Count} warnings; {DimensionRouting.CrossingCount(scene.Dimensions)} dimension crossings");



} catch(Exception ex) {Console.Error.WriteLine("Изометрия: "+(Environment.GetEnvironmentVariable("ISO_LAYOUT_TRACE")=="1"?ex.ToString():ex.Message));Environment.ExitCode=1;}

sealed class PlanningLog(string file):IProgress<PlanningProgress>{public void Report(PlanningProgress value)=>File.AppendAllText(file,$"{DateTime.UtcNow:O} variants={value.Variants} {value.Detail}\n");}
