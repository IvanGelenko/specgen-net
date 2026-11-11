using System.Text.Json;
using IsoGenerator.Core;
var source=JsonSerializer.Deserialize<PipelineSnapshot>(File.ReadAllText(args[0]))!;
if(Environment.GetEnvironmentVariable("ISO_PROBE_BREAKS")=="1")
{
 var assembly=typeof(SheetSetPlanner).Assembly;
 source=(PipelineSnapshot)assembly.GetType("IsoGenerator.Core.BreakPointPreparation")!.GetMethod("Apply")!.Invoke(null,new object[]{source,false})!;
 source=(PipelineSnapshot)assembly.GetType("IsoGenerator.Core.SheetConnectivity")!.GetMethod("AttachSupports")!.Invoke(null,new object[]{source})!;
}
var catalog=JsonSerializer.Deserialize<IsoBlockCatalog>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[1])!,"IsoSymbolStyles.symbols.json")))!;
catalog=catalog with{SymbolScale=double.TryParse(Environment.GetEnvironmentVariable("ISO_SYMBOL_SCALE"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double userScale)?userScale:10,Style=null};
var map=new IsoSymbolMap(File.ReadAllText(args[1]),catalog);var all=source.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
var adjacency=all.Keys.ToDictionary(h=>h,_=>new HashSet<string>(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
foreach(var c in source.Connections.Except(source.RequiredCuts??Array.Empty<Connection>()))if(adjacency.TryGetValue(c.FirstHandle,out var a)&&adjacency.TryGetValue(c.SecondHandle,out var b)){a.Add(c.SecondHandle);b.Add(c.FirstHandle);}
var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var components=new List<HashSet<string>>();
foreach(var h in all.Keys)if(seen.Add(h))
{var part=new HashSet<string>(StringComparer.OrdinalIgnoreCase){h};var q=new Queue<string>();q.Enqueue(h);while(q.Count>0)foreach(var n in adjacency[q.Dequeue()])if(seen.Add(n)){part.Add(n);q.Enqueue(n);}components.Add(part);}
Directory.CreateDirectory(args[2]);int number=0;
Console.WriteLine("Connected sections: "+components.Count+"; parts per section: "+string.Join(", ",components.Select(c=>c.Count)));
File.WriteAllText(Path.Combine(args[2],"prepared.json"),JsonSerializer.Serialize(source,new JsonSerializerOptions{WriteIndented=true}));
if(Environment.GetEnvironmentVariable("ISO_PROBE_PREPARE_ONLY")=="1")return;
foreach(var component in components.OrderByDescending(c=>c.Count))
{
 number++;var sub=source with{Parts=source.Parts.Where(p=>component.Contains(p.Handle)).ToArray(),Connections=source.Connections.Where(c=>component.Contains(c.FirstHandle)&&component.Contains(c.SecondHandle)).ToArray(),Accessories=source.Accessories?.Where(p=>component.Contains(p.Properties.GetValueOrDefault("ConnectorHandle", ""))).ToArray()};
 for(int level=0;level<=3;level++)
 {
  var timer=System.Diagnostics.Stopwatch.StartNew();
  try
  {
   var scene=SheetComposition.Center(SheetPlanner.Build(sub,symbolMap:map,excludeFlangeHardware:true,compactLevel:level));
   File.WriteAllText(Path.Combine(args[2],$"component-{number}-level-{level}.json"),JsonSerializer.Serialize(scene,new JsonSerializerOptions{WriteIndented=true}));
   Console.WriteLine($"PASS component={number}, parts={component.Count}, level={level}, bounds={SheetComposition.Bounds(scene)}, seconds={timer.Elapsed.TotalSeconds:F2}");
  }
  catch(Exception ex){Console.WriteLine($"FAIL component={number}, parts={component.Count}, level={level}, seconds={timer.Elapsed.TotalSeconds:F2}: {ex.Message}");}
 }
}
