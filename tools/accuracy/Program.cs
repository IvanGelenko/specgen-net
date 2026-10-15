using System.Text.Json;
using PipingSpecPlugin.Spec;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

var jsonOptions = new JsonSerializerOptions {IncludeFields=true, WriteIndented=true};
var snapshotJson=File.ReadAllText(args[0]);
var snapshot = JsonSerializer.Deserialize<Snapshot>(snapshotJson, jsonOptions);
// Exercise the same property mapping used by the live DLM reader, rather
// than bypassing it with already-typed PartRecords.
using(var source=JsonDocument.Parse(snapshotJson))
{
    var mapped=new List<PartRecord>();
    foreach(var item in source.RootElement.GetProperty("Records").EnumerateArray())
    {
        string Value(string key)=>item.TryGetProperty(key,out var value) && value.ValueKind!=JsonValueKind.Null ? value.ToString() : null;
        var id=item.GetProperty("PnPID").GetInt32();
        var props=item.EnumerateObject().Select(p=>new KeyValuePair<string,string>(p.Name,
            p.Value.ValueKind==JsonValueKind.Null?null:p.Value.ToString()));
        var record=PartPropertyReader.Build(id,props,Value("LineNumberTag"));
        record.SourceDwgPath=Value("SourceDwgPath");
        mapped.Add(record);
    }
    snapshot.Records=mapped;
}
var failures = new List<string>();
foreach (var expected in snapshot.ExpectedPipes)
{
    var record = snapshot.Records.Single(r => r.PnPID == expected.Id);
    var actual = new SpecRowBuilder().Build(new[]{record}).Single();
    if (Math.Abs(actual.Qty - expected.Qty) > 1e-8 || Math.Abs(actual.Weight - expected.Weight) > 1e-8)
        failures.Add($"Pipe {expected.Id}: {actual.Qty}/{expected.Qty} m; {actual.Weight}/{expected.Weight} kg/m");
}
var bolts = snapshot.Records.Where(r=>r.PnPClassName=="BoltSet").ToList();
var boltRows = new SpecRowBuilder(new(){ExcludeConnectors=false}).Build(bolts);
if (boltRows.Sum(r=>r.Qty) != snapshot.ExpectedBoltPieces) failures.Add("Bolt piece total mismatch");
var pipes = snapshot.Records.Where(r=>r.PnPClassName=="Pipe").ToList();
var lengthChecks=new List<object>();
foreach(var check in snapshot.LengthCases)
{
    var selected=check.Drawing==null ? pipes : pipes.Where(p=>p.SourceDwgPath==check.Drawing).ToList();
    var options=new SpecBuilderOptions{GroupByLine=check.GroupByLine,
        Lines=check.Line==null?null:new[]{check.Line},
        Statuses=check.Status==null?null:new[]{check.Status==""?SpecConstants.NullStatus:check.Status}};
    var actual=new SpecRowBuilder(options).Build(selected);
    string RowKey(string name,string line,double qty)=>JsonSerializer.Serialize(new{name,line,qty});
    var expected=check.Rows.Select(r=>RowKey(r.Name,r.LineTag,r.Qty)).OrderBy(x=>x);
    if(!expected.SequenceEqual(actual.Select(r=>RowKey(r.Name,r.LineTag,r.Qty)).OrderBy(x=>x)))
        failures.Add($"Exact pipe position length mismatch: {check.Label}");
    lengthChecks.Add(new{check.Label,check.SourceMetres,OutputMetres=actual.Sum(r=>(decimal)r.Qty),Positions=actual.Count});
}
File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[0]),"pipe-length-checks.json"),
    JsonSerializer.Serialize(lengthChecks,jsonOptions));
var grouped = new SpecRowBuilder().Build(pipes);
var shuffled = new SpecRowBuilder().Build(pipes.AsEnumerable().Reverse());
string Key(SpecRow row) => JsonSerializer.Serialize(row,jsonOptions);
if (!grouped.Select(Key).OrderBy(x=>x).SequenceEqual(shuffled.Select(Key).OrderBy(x=>x)))
    failures.Add("Pipe aggregation depends on source order");
decimal raw = decimal.Parse(snapshot.RawPipeMetres,System.Globalization.CultureInfo.InvariantCulture);
decimal groupedLength = grouped.Sum(r=>(decimal)r.Qty);
if (groupedLength < raw || groupedLength >= raw + grouped.Count * .1m)
    failures.Add("Grouped pipe length is outside rounding bounds");
var statuses = snapshot.Records.Select(r=>r.Status??"").Distinct().ToList();
foreach(var status in statuses)
{
    var option=new SpecBuilderOptions{Statuses=new[]{status==""?SpecConstants.NullStatus:status}};
    var selectedBolts=new SpecRowBuilder(option).Build(bolts).Sum(r=>r.Qty);
    var expectedBolts=bolts.Where(r=>(r.Status??"")==status)
        .Sum(r=>double.Parse(r.Quantity,System.Globalization.CultureInfo.InvariantCulture));
    if(selectedBolts!=expectedBolts) failures.Add($"Status {status}: bolt quantity mismatch");
    var selectedPipes=new SpecRowBuilder(option).Build(pipes);
    var sourceLength=pipes.Where(r=>(r.Status??"")==status)
        .Sum(r=>decimal.Parse(r.Length,System.Globalization.CultureInfo.InvariantCulture)/1000m);
    var outputLength=selectedPipes.Sum(r=>(decimal)r.Qty);
    if(outputLength<sourceLength || outputLength>sourceLength+selectedPipes.Count*.1m)
        failures.Add($"Status {status}: pipe length mismatch");
}
var allBuilder = new SpecRowBuilder(new(){ExcludeValveCompanions=false});
var allRows = allBuilder.Build(snapshot.Records);
foreach (var count in snapshot.ExpectedPieceCounts)
    if (allRows.Where(r=>r.Category==count.Key).Sum(r=>r.Qty) != count.Value)
        failures.Add($"Category {count.Key}: piece total mismatch");

// Exercise the final document boundary as well as the numeric core. The
// fixture intentionally has one reserved row followed by another section.
var temporary = Path.Combine(Path.GetTempPath(), "spec-accuracy-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    var template = Path.Combine(temporary,"template.docx");
    var output = Path.Combine(temporary,"output.docx");
    TableCell Cell(string text) => new(new Paragraph(new Run(new Text(text))));
    using (var doc = WordprocessingDocument.Create(template,WordprocessingDocumentType.Document))
    {
        var part=doc.AddMainDocumentPart();
        part.Document=new Document(new Body(new Table(
            new TableRow(Enumerable.Range(0,9).Select(i=>Cell("Column "+i))),
            new TableRow(Cell(SpecConstants.MaterialsSectionHeader)),
            new TableRow(Enumerable.Range(0,9).Select(i=>Cell(""))),
            new TableRow(Cell("Next section")))));
        part.Document.Save();
    }
    foreach (var byLine in new[]{false,true})
    {
        var documentRows=byLine ? new SpecRowBuilder(new(){GroupByLine=true}).Build(snapshot.Records) : allRows;
        SpecTemplateWriter.Write(template,output,documentRows,byLine);
        using var doc=WordprocessingDocument.Open(output,false);
        var table=doc.MainDocumentPart.Document.Body.Elements<Table>().Single();
        var data=table.Elements<TableRow>().Select(r=>r.Elements<TableCell>().Select(c=>c.InnerText).ToArray())
            .Where(c=>c.Length==9 && int.TryParse(c[0],out _)).ToList();
        if(data.Count!=documentRows.Count) failures.Add("Word lost specification rows");
        if(table.Elements<TableRow>().Last().InnerText!="Next section") failures.Add("Word crossed section boundary");
        string F(double value,int digits)=>value.ToString("F"+digits,System.Globalization.CultureInfo.InvariantCulture);
        string Pack(IEnumerable<string> cells)=>JsonSerializer.Serialize(cells.ToArray());
        var expected=documentRows.Select(r=>Pack(new[]{r.Name??"",r.TypeMark??"",r.ItemCode??"",r.Manufacturer??"",
            r.Unit??"",F(r.Qty,r.Unit=="м"?1:0),r.Weight>0?F(r.Weight,3):"",r.Note??""})).OrderBy(x=>x);
        if(!expected.SequenceEqual(data.Select(c=>Pack(c.Skip(1))).OrderBy(x=>x))) failures.Add("Word changed specification values");
        if(!data.Select(c=>int.Parse(c[0])).SequenceEqual(Enumerable.Range(1,data.Count))) failures.Add("Word numbering mismatch");
    }
}
finally {Directory.Delete(temporary,true);}
var report = new {PipesChecked=snapshot.ExpectedPipes.Count, BoltSetsChecked=bolts.Count,
    BoltPieces=boltRows.Sum(r=>r.Qty),RawPipeMetres=raw,GroupedPipeMetres=groupedLength,
    PipeGroups=grouped.Count, SpecificationRows=allRows.Count,
    PieceCounts=snapshot.ExpectedPieceCounts, StatusesChecked=statuses.Count, ExactPipeLengthCases=snapshot.LengthCases.Count,
    WordRowsChecked=allRows.Count, WordGroupingModesChecked=2,
    Warnings=allBuilder.Warnings, Drops=allBuilder.DropReasons, Failures=failures};
File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[0]),"verification.json"),JsonSerializer.Serialize(report,jsonOptions));
Console.WriteLine(JsonSerializer.Serialize(new {report.PipesChecked,report.BoltSetsChecked,report.BoltPieces,
    report.RawPipeMetres,report.GroupedPipeMetres,report.PipeGroups,report.ExactPipeLengthCases,report.SpecificationRows,Failures=failures},jsonOptions));
Environment.ExitCode=failures.Count==0 ? 0 : 1;

public class Snapshot
{
    public List<PartRecord> Records;
    public List<ExpectedPipe> ExpectedPipes;
    public double ExpectedBoltPieces;
    public string RawPipeMetres;
    public Dictionary<string,int> ExpectedPieceCounts;
    public List<LengthCase> LengthCases;
}
public class ExpectedPipe { public int Id; public double Qty; public double Weight; }
public class LengthCase
{
    public string Label,Line,Status,Drawing,SourceMetres;
    public bool GroupByLine;
    public List<ExpectedLengthRow> Rows;
}
public class ExpectedLengthRow { public string Name,LineTag; public double Qty; }
