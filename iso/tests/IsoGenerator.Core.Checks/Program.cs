using IsoGenerator.Core;
try
{
int checks=0;
void Near(double a,double b) { checks++; if(Math.Abs(a-b)>1e-8) throw new Exception($"Expected {b}, got {a}"); }
var x=IsometricProjection.Project(new(100,0,0));
var y=IsometricProjection.Project(new(0,100,0));
var z=IsometricProjection.Project(new(0,0,100));
Near(x.X,-y.X); Near(x.Y,y.Y); Near(z.X,0); Near(z.Y,100);
Near(Math.Atan2(x.Y,x.X)*180/Math.PI,30);
var shifted=IsometricProjection.Project(new(110,220,330),new(10,20,30));
var original=IsometricProjection.Project(new(100,200,300));
Near(shifted.X,original.X); Near(shifted.Y,original.Y);
var rotated=IsometricProjection.Project(new(100,0,0),quarterTurns:1);
Near(rotated.X,y.X); Near(rotated.Y,y.Y);
Near(new ModelPoint(0,0,0).DistanceTo(new(300,400,0)),500);
var sourceLength=new DimensionValue("pipe-1",1000,true);
if(sourceLength.Label!="1000*") throw new Exception("Reference dimension label"); checks++;
// Projection foreshortens a horizontal pipe, but its engineering dimension stays 1000.
var projected=IsometricProjection.Project(new(1000,0,0));
if(Math.Abs(projected.X-sourceLength.Millimeters)<1) throw new Exception("Expected foreshortening"); checks++;


void Assert(bool value,string message){checks++;if(!value)throw new Exception(message);}
Part Pipe(string h,ModelPoint a,ModelPoint b)=>new("test.dwg",h,"AcPpDb3dPipe","L1",new[]{new Port("S1",a,new(-1,0,0)),new Port("S2",b,new(1,0,0))},new Dictionary<string,string>{{"PnPClassName","Pipe"},{"PartSizeLongDesc","Pipe 100"},{"Length","1000"}});
var first=Pipe("A",new(0,0,0),new(1000,0,0));var second=Pipe("B",new(1000,0,0),new(2000,0,0));
PipelineSnapshot Snapshot(Part[] parts,Connection[] links)=>new(1,"test.dwg",parts,links,Array.Empty<string>());
var separated=Snapshot(new[]{first,second},Array.Empty<Connection>());
Assert(SnapshotValidation.Validate(separated).ConnectedComponents==2,"Coincident ports must not invent a connection");
var linked=Snapshot(new[]{first,second},new[]{new Connection("A","S2","B","S1")});
Assert(SnapshotValidation.Validate(linked).ConnectedComponents==1,"Real topology connects components");
var gap=second with{Ports=new[]{new Port("S1",new(1005,0,0),new(-1,0,0)),second.Ports[1]}};
Assert(SnapshotValidation.Validate(linked with{Parts=new[]{first,gap}}).Errors.Count>0,"Reject inconsistent connected ports");
Assert(SnapshotValidation.Validate(linked with{Parts=new[]{first,first}}).Errors.Count>0,"Reject duplicate handles");
var scene=SheetPlanner.Build(linked);
Assert(scene.Dimensions.Count(d=>d.Role=="chain")==2&&scene.Dimensions.Count(d=>d.Role=="overall")==1,"Two chain links plus mandatory overall");
Assert(scene.Dimensions.Where(d=>d.Role=="chain").All(d=>Math.Abs(d.Millimeters-1000)<1e-8)&&Math.Abs(scene.Dimensions.Single(d=>d.Role=="overall").Millimeters-2000)<1e-8,"Dimensions and overall retain true lengths after fitting to paper");
Assert(scene.BillOfMaterials.Count==1&&Math.Abs(scene.BillOfMaterials[0].Quantity-2)<1e-8,"BOM sums meters once per source pipe");
Assert(scene.BillOfMaterials[0].Handles.Count==2,"BOM retains source mapping");
Assert(scene.Strokes.Count(s=>s.Role=="pipe")==2,"No artificial cross-part line");
var testRoot=Environment.GetEnvironmentVariable("ISO_TEST_PROJECT_ROOT")??Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
var file=Path.Combine(testRoot,"output/pipeline.json");
var fixtureCatalog=System.Text.Json.JsonSerializer.Deserialize<IsoBlockCatalog>(File.ReadAllText(Path.Combine(testRoot,"data/IsoSymbolStyles.symbols.json")))! with{SymbolScale=10};
if(File.Exists(file)){
    var live=System.Text.Json.JsonSerializer.Deserialize<PipelineSnapshot>(File.ReadAllText(file))!;
    Assert(SnapshotValidation.Validate(live).Errors.Count==0,"Captured model connectivity check");
    var liveScene=SheetPlanner.Build(live);
    Assert(liveScene.BillOfMaterials.Sum(r=>r.Handles.Count)==live.Parts.Count(p=>p.Kind!="AcPpDb3dConnector"),"Every captured physical part occurs once in BOM");
}


var definition=IsoDefinition.Parse(" SKEY=vtfl, TYPE=Valve ");
Assert(definition.Type=="VALVE"&&definition.Skey=="VTFL","ISO property parsing is order independent and normalized");
var map=new IsoSymbolMap("<IsoSkeyMap><SkeyMap SKEY='VT??' AcadBlock='Valve'/><SkeyMap SKEY='VTFL' AcadBlock='Specific'/><EndTypeMap SKEY='WW' AcadBlock='Weld'/></IsoSkeyMap>");
Assert(map.Resolve("VTFL")=="Specific","Exact SKEY overrides wildcard");
Assert(map.Resolve("VTBW")=="Valve","Wildcard matches SKEY");
Assert(map.Resolve("WW")==null,"End types are not component symbols");
Assert(map.Resolve("UNKNOWN")==null,"Unknown SKEY must not choose a similar symbol");
bool conflict=false;try{IsoDefinition.Parse("SKEY=VTFL,SKEY=FLWN");}catch(InvalidDataException){conflict=true;}
Assert(conflict,"Conflicting SKEY values must fail");
if(File.Exists(file)){
    var live=System.Text.Json.JsonSerializer.Deserialize<PipelineSnapshot>(File.ReadAllText(file))!;
    var xml=Path.Combine(Path.GetDirectoryName(file)!,"../assets/IsoSkeyAcadBlockMap.xml");
    var sceneWithSymbols=SheetPlanner.Build(live,symbolMap:new IsoSymbolMap(File.ReadAllText(xml),fixtureCatalog));
    Assert(sceneWithSymbols.Symbols!.Count(g=>g.Variant=="")==17,"Captured fittings must resolve to 17 library symbols");
    Assert(sceneWithSymbols.Strokes.All(s=>s.Role is "pipe" or "joint" or "tap-connection" or "flange-neck" or "insulation" or "elevation-leader" or "elevation-shelf"||s.Role.StartsWith("slope-",StringComparison.Ordinal)),"Library symbols replace placeholder fitting strokes");
}


foreach(var group in scene.Dimensions.GroupBy(d=>d.ChainId))
    Near(group.Where(d=>d.Role=="chain").Sum(d=>d.Millimeters),group.Single(d=>d.Role=="overall").Millimeters);
var separateDimensions=DimensionChainPlanner.Build(separated,Plain);
Assert(separateDimensions.Count==2&&separateDimensions.All(d=>d.Role=="overall"),"Coincident disconnected pipes get independent overall dimensions, not one chain");
var elbow=new Part("test.dwg","E","AcPpDb3dPipeInlineAsset","L1",new[]{new Port("S1",new(1000,0,0),new(-1,0,0)),new Port("S2",new(1100,100,0),new(0,1,0))},new Dictionary<string,string>{{"PnPClassName","Elbow"},{"PartSizeLongDesc","Elbow"}});
var afterBend=Pipe("B",new(1100,100,0),new(1100,1100,0));
var bendSnapshot=Snapshot(new[]{first,elbow,afterBend},new[]{new Connection("A","S2","E","S1"),new Connection("E","S2","B","S1")});
var bendScene=SheetPlanner.Build(bendSnapshot);
Assert(bendScene.Dimensions.Count==2&&bendScene.Dimensions.All(d=>d.Role=="overall"),"Turn splits two straight dimension runs");
Assert(bendScene.Dimensions.All(d=>Math.Abs(d.Millimeters-1100)<1e-8),"Overall goes to theoretical elbow axis, including tangent allowance");
var rotatedScene=SheetPlanner.Build(bendSnapshot,quarterTurns:1);
Assert(rotatedScene.Dimensions.All(d=>Math.Abs(d.Millimeters-1100)<1e-8),"View rotation does not change real dimension lengths");
var mainWithTap=first with{Ports=first.Ports.Concat(new[]{new Port("D1",new(500,0,-50),new(0,0,-1),"TAP")}).ToArray()};
var branchPipe=Pipe("C",new(500,0,-50),new(500,0,-250));
var tapScene=SheetPlanner.Build(Snapshot(new[]{mainWithTap,branchPipe},new[]{new Connection("A","D1","C","S1")}));
Assert(tapScene.Dimensions.Count(d=>d.Role=="overall")==2,"Tap branch gets its own overall");
Assert(tapScene.Dimensions.Any(d=>d.Role=="overall"&&Math.Abs(d.Millimeters-250)<1e-8),"Branch dimension starts at main pipe axis, not wall surface");
var weldA=first with{Ports=first.Ports.Select(p=>p with{EndCondition="PL"}).ToArray()};
var weldB=second with{Ports=second.Ports.Select(p=>p with{EndCondition="BV"}).ToArray()};
SheetPoint Plain(ModelPoint p)=>new(p.X,p.Y);
var weldSnapshot=Snapshot(new[]{weldA,weldB},new[]{new Connection("A","S2","B","S1"),new Connection("B","S1","A","S2")});
Assert(WeldPlanner.Build(weldSnapshot,Plain).Markers.Count==1,"Reverse connection records do not duplicate PL/BV weld");
Assert(WeldPlanner.Build(weldSnapshot with{Connections=Array.Empty<Connection>()},Plain).Markers.Count==0,"Unconnected coincident ports do not get a weld");
var flangedA=weldA with{Ports=weldA.Ports.Select(p=>p with{EndCondition="FL"}).ToArray()};
var flangedB=weldB with{Ports=weldB.Ports.Select(p=>p with{EndCondition="FL"}).ToArray()};
Assert(WeldPlanner.Build(weldSnapshot with{Parts=new[]{flangedA,flangedB}},Plain).Markers.Count==0,"FL/FL is not a welded joint");
Assert(WeldPlanner.Build(weldSnapshot with{Parts=new[]{weldA,flangedB}},Plain).Markers.Count==0,"PL/FL incompatible pair is not guessed as a weld");
var unknownB=weldB with{Ports=weldB.Ports.Select(p=>p with{EndCondition=""}).ToArray()};
var missingType=WeldPlanner.Build(weldSnapshot with{Parts=new[]{weldA,unknownB}},Plain);
Assert(missingType.Markers.Count==0&&missingType.Warnings.Count>0,"Unknown per-port end conditions are diagnosed");
var joint=new Part("test.dwg","J","AcPpDb3dConnector","L1",new[]{new Port("S1",new(1000,0,0),new(-1,0,0)),new Port("S2",new(1000,0,0),new(1,0,0))},new Dictionary<string,string>());
var withJoint=Snapshot(new[]{weldA,joint,weldB},new[]{new Connection("A","S2","J","S1"),new Connection("J","S2","B","S1")});
var throughJoint=WeldPlanner.Build(withJoint,Plain);
Assert(throughJoint.Markers.Count==1&&throughJoint.Markers[0].SourceId=="J","One connector produces one weld linked to the connector");
if(File.Exists(file)){
    var live=System.Text.Json.JsonSerializer.Deserialize<PipelineSnapshot>(File.ReadAllText(file))!;
    var liveScene=SheetPlanner.Build(live);
    Assert(liveScene.Welds!.Count==23,"Captured line has 23 welded joints, excluding six flanged joints");
    foreach(var chain in liveScene.Dimensions.GroupBy(d=>d.ChainId)){
        Assert(chain.Count(d=>d.Role=="overall")==1,"Every run must have exactly one overall");
        if(chain.Any(d=>d.Role=="chain"))Near(chain.Where(d=>d.Role=="chain").Sum(d=>d.Millimeters),chain.Single(d=>d.Role=="overall").Millimeters);
        var overall=chain.Single(d=>d.Role=="overall");
        Near(overall.ModelStart!.Value.DistanceTo(overall.ModelEnd!.Value),overall.Millimeters);
    }
    Assert(!liveScene.Dimensions.Any(d=>Math.Abs(d.Millimeters-4.5)<.01),"Gaskets do not get standalone dimensions");
    Assert(liveScene.Dimensions.Any(d=>d.TextPosition is not null),"Crowded dimension text is moved apart with leaders");
}


if(File.Exists(file)){
    var live=System.Text.Json.JsonSerializer.Deserialize<PipelineSnapshot>(File.ReadAllText(file))!;
    foreach(var chain in SheetPlanner.Build(live).Dimensions.GroupBy(d=>d.ChainId))
        foreach(var d in chain)Near(double.Parse(d.Label,System.Globalization.CultureInfo.InvariantCulture),Math.Ceiling(d.Millimeters-1e-8));
}



if(File.Exists(file)){
    var live=System.Text.Json.JsonSerializer.Deserialize<PipelineSnapshot>(File.ReadAllText(file))!;
    var layout=new SchematicLayout(live);
    Assert(layout.EnlargedEdges>0,"Short segments must be enlarged on paper");
    var symbolMap=new IsoSymbolMap(File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!,"../assets/IsoSkeyAcadBlockMap.xml")),fixtureCatalog);
    var readable=SheetPlanner.Build(live,symbolMap:symbolMap);
    Assert(readable.Symbols!.Count(g=>g.Variant.StartsWith("end-"))==8,"Each of four FL valves gets two end plates");
    Assert(readable.Symbols.Count(g=>g.Variant=="operator")==4,"Valve operators follow the project's spindle rules");
    foreach(var jointPart in live.Parts.Where(p=>p.Kind=="AcPpDb3dConnector")){
        var neighbors=live.Connections.SelectMany(c=>c.FirstHandle==jointPart.Handle?new[]{(H:c.SecondHandle,P:c.SecondPort)}:c.SecondHandle==jointPart.Handle?new[]{(H:c.FirstHandle,P:c.FirstPort)}:Array.Empty<(string H,string P)>()).Distinct().ToArray();
        if(neighbors.Length!=2)continue;
        var a=live.Parts.Single(p=>p.Handle==neighbors[0].H).Ports.Single(p=>p.Name==neighbors[0].P);
        var b=live.Parts.Single(p=>p.Handle==neighbors[1].H).Ports.Single(p=>p.Name==neighbors[1].P);
        if(a.EndCondition!="FL"||b.EndCondition!="FL")continue;
        Near(a.Position.DistanceTo(b.Position),4.5);
        var pa=layout.Project(a.Position);var pb=layout.Project(b.Position);
        Near(Math.Sqrt(Math.Pow(pa.X-pb.X,2)+Math.Pow(pa.Y-pb.Y,2)),1);
        Assert(!readable.Strokes.Any(s=>s.SourceId==jointPart.Handle),"2 mm gasket gap remains blank, not a separate dimensioned segment");
    }
    Assert(readable.Dimensions.Any(d=>d.Role=="chain"&&Math.Abs(d.Millimeters-409)<.01),"Valve assembly includes both flanges and gaskets in the real chain");
    Assert(readable.Dimensions.Count(d=>d.Role=="overall")==10,"Overall dimensions survive schematic stretching");
    // The first drain goes down from the main Y run; the main chain must be on the opposite side.
    var mainChain=readable.Dimensions.Single(d=>d.Role=="overall"&&Math.Abs(d.Millimeters-4364.814)<.01);
    var main=live.Parts.Single(p=>p.Handle=="164C3");var tap=main.Ports.Single(p=>p.Name=="D1");
    var s1=main.Ports.Single(p=>p.Name=="S1").Position;var s2=main.Ports.Single(p=>p.Name=="S2").Position;
    var axis=new ModelPoint(s2.X-s1.X,s2.Y-s1.Y,s2.Z-s1.Z);double length2=axis.X*axis.X+axis.Y*axis.Y+axis.Z*axis.Z;
    double t=((tap.Position.X-s1.X)*axis.X+(tap.Position.Y-s1.Y)*axis.Y+(tap.Position.Z-s1.Z)*axis.Z)/length2;
    var center=layout.Project(new(s1.X+axis.X*t,s1.Y+axis.Y*t,s1.Z+axis.Z*t));var branchVector=IsometricProjection.Project(new(tap.Position.X-(s1.X+axis.X*t),tap.Position.Y-(s1.Y+axis.Y*t),tap.Position.Z-(s1.Z+axis.Z*t)));var outPoint=new SheetPoint(center.X+branchVector.X,center.Y+branchVector.Y);
    var ex=mainChain.ExtensionDirection!.Value;
    Assert(ex.X*(outPoint.X-center.X)+ex.Y*(outPoint.Y-center.Y)<0,"Main dimension rows must face away from the drain");
}


if(File.Exists(file)){
 var live=System.Text.Json.JsonSerializer.Deserialize<PipelineSnapshot>(File.ReadAllText(file))!;
 var symbolMap=new IsoSymbolMap(File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!,"../assets/IsoSkeyAcadBlockMap.xml")),fixtureCatalog);
 var output=SheetPlanner.Build(live,symbolMap:symbolMap);
 foreach(var chain in output.Dimensions.GroupBy(d=>d.ChainId)){
  var overall=chain.Single(d=>d.Role=="overall");
  var innerDimension=chain.FirstOrDefault(d=>d.Role=="chain")??overall;
  double dx=innerDimension.End.X-innerDimension.Start.X,dy=innerDimension.End.Y-innerDimension.Start.Y,len=Math.Sqrt(dx*dx+dy*dy),nx=-dy/len,ny=dx/len;
  var line=innerDimension.DimensionLinePoint!.Value;
  Assert(Math.Abs((line.X-innerDimension.Start.X)*nx+(line.Y-innerDimension.Start.Y)*ny )>=10-1e-7,"First row must be at least 10 mm from axis/contour");
  if(innerDimension!=overall){var outer=overall.DimensionLinePoint!.Value;Assert(Math.Abs((outer.X-line.X)*nx+(outer.Y-line.Y)*ny )>=7-1e-7,"Parallel dimension rows must be at least 7 mm apart");}
  foreach(var handle in innerDimension.SourceIds??Array.Empty<string>())
   foreach(var g in output.Symbols!.Where(g=>g.SourceId==handle&&g.Variant=="operator")){
    var top=new SheetPoint(g.Origin.X+g.YAxis.X*.625,g.Origin.Y+g.YAxis.Y*.625);
    Assert(Math.Abs((line.X-top.X)*nx+(line.Y-top.Y)*ny )>=10-1e-7,"Dimension line must clear valve operator contour by 10 mm");
   }
 }
}
// Twenty short vertical pipes exceed the readable height of one A2 sheet.
var tallParts=Enumerable.Range(0,40).Select(i=>Pipe($"T{i:00}",new(0,0,i*100),new(0,0,(i+1)*100)) with
{
    Ports=new[]{new Port("S1",new(0,0,i*100),new(0,0,-1),"PL"),new Port("S2",new(0,0,(i+1)*100),new(0,0,1),"BV")}
}).ToArray();
var tallLinks=Enumerable.Range(0,39).Select(i=>new Connection(tallParts[i].Handle,"S2",tallParts[i+1].Handle,"S1")).ToArray();
var tallSnapshot=Snapshot(tallParts,tallLinks);
var tallSheets=SheetSetPlanner.Build(tallSnapshot);
Assert(tallSheets.Count==2,$"Oversize readable route becomes two sheets (actual {tallSheets.Count}: {string.Join(",",tallSheets.Select(s=>s.Snapshot.Parts.Count))})");
Assert(tallSheets.SelectMany(s=>s.Snapshot.Parts).Select(p=>p.Handle).Distinct().Count()==40&&tallSheets.Sum(s=>s.Snapshot.Parts.Count)==40,"Every source component belongs to exactly one sheet");
Assert(tallSheets.Sum(s=>s.Scene.Welds!.Count)==39,"Split boundary welds are retained exactly once");
Assert(tallSheets.Sum(s=>s.Scene.Annotations.Count(a=>a.Text.Contains("/ лист")))==2,"Both sheets contain reciprocal continuation references");
Near(tallSheets.Sum(s=>s.Scene.BillOfMaterials.Sum(b=>b.Quantity)),4);
Near(tallSheets.Sum(s=>s.Scene.Dimensions.Where(d=>d.Role=="chain").Sum(d=>d.Millimeters)),4000);
Assert(SheetSetPlanner.Build(linked).Count==1,"Small route stays on a single sheet");
var valveParts=tallParts.Select((p,i)=>i==20?p with{Properties=new Dictionary<string,string>(p.Properties){{"ContentIsoSymbolDefinition","TYPE=VALVE,SKEY=VTFL"}}}:p).ToArray();
valveParts[20]=valveParts[20] with{Properties=new Dictionary<string,string>(valveParts[20].Properties){["PnPClassName"]="Valve"}};
var valveSheets=SheetSetPlanner.Build(Snapshot(valveParts,tallLinks));
Assert(valveSheets.Count==2,"Route with central valve fits two sheets");
Assert(valveSheets.Any(s=>s.Snapshot.Parts.Any(p=>p.Handle=="T20")&&!s.Snapshot.Parts.Any(p=>p.Handle=="T19"))||valveSheets.Any(s=>s.Snapshot.Parts.Any(p=>p.Handle=="T20")&&!s.Snapshot.Parts.Any(p=>p.Handle=="T21")),"Prefer a split adjacent to the valve");
// Revised drawing rules: true dimensions remain fractional, displayed lengths round upward.
Assert(new PipeDimension("x",new(0,0),new(1,0),177.9).Label=="178","Round a pipe length upward");
Assert(new PipeDimension("x",new(0,0),new(1,0),209.5).Label=="210","Round assembly length upward");
if(File.Exists(file))
{
    var live=System.Text.Json.JsonSerializer.Deserialize<PipelineSnapshot>(File.ReadAllText(file))!;
    var rulesMap=new IsoSymbolMap(File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!,"../assets/IsoSkeyAcadBlockMap.xml")),fixtureCatalog);
    var output=SheetPlanner.Build(live,symbolMap:rulesMap);
    Assert(output.Dimensions.Any(d=>Math.Abs(d.Millimeters-209.5)<.01),"Drain valve includes 40 mm flange and gasket");
    var withoutHardware=SheetPlanner.Build(live,symbolMap:rulesMap,excludeFlangeHardware:true);
    Assert(output.Dimensions.Select(d=>(d.ModelStart,d.ModelEnd,d.Millimeters)).SequenceEqual(withoutHardware.Dimensions.Select(d=>(d.ModelStart,d.ModelEnd,d.Millimeters))),"BOM hardware exclusion preserves every engineering dimension including flanges");

    Assert(!output.Dimensions.Any(d=>Math.Abs(d.Millimeters-40)<.01||Math.Abs(d.Millimeters-60)<.01),"No standalone flange dimensions");
    Assert(output.Dimensions.Any(d=>Math.Abs(d.Millimeters-177.873)<.01),"Drain dimension combines tap offset and pipe to flange weld");
    Assert(!output.Strokes.Any(s=>s.Role=="tap-connection"),"Tap axis offset is accounted for without a visible connection stroke");
    var tapWeld=output.Welds!.Single(w=>w.JointType=="Tap"&&(w.FirstHandle=="164C3"||w.SecondHandle=="164C3"));
    var tapAxis=new SchematicLayout(live,symbolMap:rulesMap).Project(new(0,-1283.491,2000));Near(tapWeld.Position.X,tapAxis.X);Near(tapWeld.Position.Y,tapAxis.Y);
    var tapBranchHandle=tapWeld.FirstEndCondition=="TAP"?tapWeld.SecondHandle:tapWeld.FirstHandle;
    var tapBranchPort=tapWeld.FirstEndCondition=="TAP"?tapWeld.SecondPort:tapWeld.FirstPort;
    var connectedBranch=live.Parts.Single(p=>p.Handle==tapBranchHandle).Ports.Single(p=>p.Name==tapBranchPort);
    var branchOnPaper=new SchematicLayout(live,symbolMap:rulesMap).Project(connectedBranch.Position);
    Near(branchOnPaper.X,tapWeld.Position.X);Near(branchOnPaper.Y,tapWeld.Position.Y);
    var body=output.Symbols!.First(g=>g.SourceId=="16571"&&g.Variant=="");
    Near(Math.Sqrt(body.XAxis.X*body.XAxis.X+body.XAxis.Y*body.XAxis.Y),fixtureCatalog.SymbolScale);
    var flange=output.Symbols.First(g=>g.SourceId=="16579"&&g.Variant=="");
    Near(Math.Sqrt(flange.YAxis.X*flange.YAxis.X+flange.YAxis.Y*flange.YAxis.Y),Math.Sqrt(body.YAxis.X*body.YAxis.X+body.YAxis.Y*body.YAxis.Y));
    var poseA=live with{Parts=live.Parts.Select(p=>p.Handle=="16571"?p with{OperatorDirection=new(1,0,0)}:p).ToArray()};
    var poseB=live with{Parts=live.Parts.Select(p=>p.Handle=="16571"?p with{OperatorDirection=new(-1,0,0)}:p).ToArray()};
    var opA=SheetPlanner.Build(poseA,symbolMap:rulesMap).Symbols!.Single(g=>g.SourceId=="16571"&&g.Variant=="operator");
    var opB=SheetPlanner.Build(poseB,symbolMap:rulesMap).Symbols!.Single(g=>g.SourceId=="16571"&&g.Variant=="operator");
    Near(opA.YAxis.X,-opB.YAxis.X);Near(opA.YAxis.Y,-opB.YAxis.Y);
    Assert(output.Annotations.Any(a=>a.Role=="elevation"&&a.Text=="ОТМ. +2.000*"&&a.Anchor is not null&&a.LeaderKnee is not null),"Elevation in meters has a leader and shelf");
    Assert(output.Strokes.Any(s=>s.Role=="elevation-shelf"),"Elevation shelf is drawn");
}
var rise=Snapshot(new[]{Pipe("rise",new(0,0,1000),new(1000,0,2000))},Array.Empty<Connection>());
var riseScene=SheetPlanner.Build(rise);
Assert(riseScene.Annotations.Any(a=>a.Role=="slope-angle"&&a.Text=="45°"),"True vertical rise gets a 45 degree label");
Assert(riseScene.Strokes.Count(s=>s.Role=="slope-hatch")>=1,"Incline triangle has evenly spaced hatching");
Assert(riseScene.Annotations.Count(a=>a.Role=="elevation")==2,"Both ends of a rise have elevation marks");
var riseLine=riseScene.Strokes.Single(s=>s.Role=="pipe");
Near(Math.Atan2(riseLine.Points[1].Y-riseLine.Points[0].Y,riseLine.Points[1].X-riseLine.Points[0].X)*180/Math.PI,60);
Assert(Path.GetFileName(DrawingFileNames.Dwg("out","150-FL007-LS016B-HN",1,1))=="150-FL007-LS016B-HN.dwg","Single sheet file matches line number");
Assert(Path.GetFileName(DrawingFileNames.Dwg("out","150-FL007-LS016B-HN",2,3))=="150-FL007-LS016B-HN(L2).dwg","Multiple sheets use the requested L index");
Assert(DrawingFileNames.LineStem("LINE/1:2")=="LINE_1_2","Invalid Windows filename characters are replaced deterministically");
Assert(riseScene.Strokes.Count(s=>s.Role=="slope-angle-arrow")==2,"Angular dimension has two filled arrows");
Assert(riseScene.Strokes.Count(s=>s.Role=="slope-angle-arc")==1,"Incline has a projected angular dimension arc");
Assert(riseScene.Strokes.Count(s=>s.Role=="slope-right-angle")==1,"Incline triangle has a right angle mark");
if(File.Exists(file))
{
    var live=System.Text.Json.JsonSerializer.Deserialize<PipelineSnapshot>(File.ReadAllText(file))!;
    foreach(var d in SheetPlanner.Build(live).Dimensions)
    {
        double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,l=Math.Sqrt(dx*dx+dy*dy);dx/=l;dy/=l;
        if(dx<-.0001||(Math.Abs(dx)<.0001&&dy<0)){dx=-dx;dy=-dy;}
        var text=d.TextPosition!.Value;var line=d.DimensionLinePoint!.Value;
        Assert((text.X-line.X)*(-dy)+(text.Y-line.Y)*dx>=2.79,"Text stays above its dimension line in reading direction");
    }
}
Assert(tallSheets.All(s=>s.Scene.Strokes.Any(st=>st.Role=="context-pipe")),"Each split shows a fragment of the adjacent sheet");
foreach(var sheet in tallSheets)
{
    var own=sheet.Snapshot.Parts.Select(p=>p.Handle).ToHashSet();
    Assert(sheet.Scene.Strokes.Where(s=>s.Role.StartsWith("context-",StringComparison.Ordinal)).All(s=>!own.Contains(s.SourceId)),"Ghost geometry is from the adjacent sheet only");
    Assert(sheet.Scene.BillOfMaterials.SelectMany(b=>b.Handles).All(own.Contains),"Ghost context is excluded from the bill of materials");
}
var risingFixture=Path.Combine(testRoot,"tests/fixtures/rising-line.json");
var risingSource=System.Text.Json.JsonSerializer.Deserialize<PipelineSnapshot>(File.ReadAllText(risingFixture))!;
var risingMap=new IsoSymbolMap(File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!,"../assets/IsoSkeyAcadBlockMap.xml")),fixtureCatalog);
var risingSheets=SheetSetPlanner.Build(risingSource,risingMap);
Assert(risingSheets.Count==1,"The complete reported route fits one A2 sheet before considering a split");
Assert(risingSheets[0].Scene.Welds!.Count==23,"Single sheet keeps all actual welds");
Assert(risingSheets[0].Scene.Annotations.Any(a=>a.Role=="slope-angle"&&a.Text=="45°"),"Actual inclined route retains its true rise angle");
Assert(!risingSheets[0].Scene.Symbols!.Any(g=>g.Context),"One-sheet result has no adjacent-sheet context");
// Paper layout: fixed 5 mm symbols, longer pipes, and collision-aware centering of the complete scene.
var enlargedRaw=risingSheets[0].Scene;
var enlarged=SheetComposition.Center(enlargedRaw);
var again=SheetComposition.Center(enlarged);
var drawingBounds=SheetComposition.Bounds(enlarged);
Assert(drawingBounds.Left>=SheetComposition.Paper.Left-1e-6&&drawingBounds.Right<=SheetComposition.Paper.Right+1e-6&&drawingBounds.Bottom>=SheetComposition.Paper.Bottom-1e-6&&drawingBounds.Top<=SheetComposition.Paper.Top+1e-6,"Centered drawing fits inside paper working area");
Assert(!SheetComposition.Pieces(enlarged).Any(p=>SheetComposition.Reserved(BomCategories.BodyHeight(enlarged.BillOfMaterials)/14).Any(p.Overlaps)),"Drawing avoids BOM, notes and title block");
Near(SheetComposition.Bounds(again).Center.X,drawingBounds.Center.X);Near(SheetComposition.Bounds(again).Center.Y,drawingBounds.Center.Y);
// Larger fixed fittings may require an offset around the BOM. Centering must still be locally optimal.
foreach(var step in new[]{new SheetPoint(.5,0),new SheetPoint(-.5,0),new SheetPoint(0,.5),new SheetPoint(0,-.5)})
{
    var moved=SheetComposition.Pieces(enlarged).Select(b=>new DrawingBounds(b.Left+step.X,b.Bottom+step.Y,b.Right+step.X,b.Top+step.Y)).ToArray();
    bool feasible=moved.All(b=>b.Left>=SheetComposition.Paper.Left&&b.Right<=SheetComposition.Paper.Right&&b.Bottom>=SheetComposition.Paper.Bottom&&b.Top<=SheetComposition.Paper.Top)&&!moved.Any(b=>SheetComposition.Reserved(BomCategories.BodyHeight(enlarged.BillOfMaterials)/14).Any(b.Overlaps));
    double Score(double dx,double dy)=>Math.Pow(drawingBounds.Center.X+dx-SheetComposition.Paper.Center.X,2)+Math.Pow(drawingBounds.Center.Y+dy-SheetComposition.Paper.Center.Y,2);
    Assert(!feasible||Score(step.X,step.Y)>=Score(0,0)-1e-6,"Centering is optimal within the free sheet area");
}
double paperPipeTotal=0;
foreach(var pipe in enlarged.Strokes.Where(s=>s.Role=="pipe"))
{
    double length=pipe.Points.Zip(pipe.Points.Skip(1),(a,b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2))).Sum();
    Assert(length>0,"Pipe segments remain visible without a fixed 10 mm minimum");paperPipeTotal+=length;
}

var gridLayout=new SchematicLayout(risingSource,symbolMap:risingMap);
foreach(var part in risingSource.Parts)
{
    string kind=part.Properties.GetValueOrDefault("PnPClassName","");
    if(kind is not ("Valve" or "Flange"))continue;
    var ends=part.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();
    if(ends.Length!=2)continue;
    var a=gridLayout.Project(ends[0].Position);var b=gridLayout.Project(ends[1].Position);
    var definitionFromLibrary=fixtureCatalog.Find(risingMap.Resolve(IsoDefinition.Parse(part.Properties.GetValueOrDefault("ContentIsoSymbolDefinition","")).Skey)!)!;
    var lp=definitionFromLibrary.Ports["Port1"];var rp=definitionFromLibrary.Ports["Port2"];
    Near(Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2)),Math.Sqrt(Math.Pow(lp.X-rp.X,2)+Math.Pow(lp.Y-rp.Y,2))*fixtureCatalog.SymbolScale);
}
foreach(var glyph in enlarged.Symbols!.Where(g=>g.Block is "Колено" or "Тройник"))
{
    Near(Math.Sqrt(glyph.XAxis.X*glyph.XAxis.X+glyph.XAxis.Y*glyph.XAxis.Y),fixtureCatalog.SymbolScale);
    Near(Math.Sqrt(glyph.YAxis.X*glyph.YAxis.X+glyph.YAxis.Y*glyph.YAxis.Y),fixtureCatalog.SymbolScale);
}
for(int i=0;i<enlarged.Symbols!.Count;i++)
{
    Assert(enlarged.Symbols[i].XAxis==enlargedRaw.Symbols![i].XAxis&&enlarged.Symbols[i].YAxis==enlargedRaw.Symbols[i].YAxis,"Centering translates symbols without scaling");
}
Assert(enlarged.Dimensions.Select(d=>d.Millimeters).SequenceEqual(enlargedRaw.Dimensions.Select(d=>d.Millimeters)),"Centering and schematic pipe extension preserve engineering dimensions");
// Compact dimension defaults and annotation/insulation policies.
var free=SheetPlanner.Build(linked);
double RowOffset(PipeDimension d){double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,l=Math.Sqrt(dx*dx+dy*dy);var p=d.DimensionLinePoint!.Value;return Math.Abs((p.X-d.Start.X)*(-dy/l)+(p.Y-d.Start.Y)*dx/l);}
Near(RowOffset(free.Dimensions.First(d=>d.Role=="chain")),10);
Near(RowOffset(free.Dimensions.Single(d=>d.Role=="overall"))-RowOffset(free.Dimensions.First(d=>d.Role=="chain")),7);
var insulated=first with{Properties=new Dictionary<string,string>(first.Properties){["InsulationThickness"]="40,0"}};
var insulatedScene=SheetPlanner.Build(Snapshot(new[]{insulated},Array.Empty<Connection>()));
Assert(insulatedScene.Strokes.Count(s=>s.Role=="insulation")==2,"Insulated pipe has two dashed parallel contours");
var uninsulated=insulated with{Properties=new Dictionary<string,string>(insulated.Properties){["InsulationThickness"]="0"}};
Assert(!SheetPlanner.Build(Snapshot(new[]{uninsulated},Array.Empty<Connection>())).Strokes.Any(s=>s.Role=="insulation"),"Zero insulation has no dashed contour");
var taggedSource=risingSource with{Parts=risingSource.Parts.Select(p=>p with{Properties=new Dictionary<string,string>(p.Properties){["InsulationThickness"]="40",["Tag"]=p.Handle=="16571"?"BV-001":"?-?"}}).ToArray()};
var taggedSheets=SheetSetPlanner.Build(taggedSource,risingMap);
Assert(taggedSheets.Count==1,"Insulation and a valve identifier stay on one sheet when room is available");
var identifier=taggedSheets[0].Scene.Annotations.Single(a=>a.Role=="valve-tag");
Assert(identifier.Text=="BV-001"&&identifier.Anchor is not null&&identifier.LeaderKnee is not null,"Valve identifier has an attached native leader plan");
Assert(!taggedSheets[0].Scene.Annotations.Any(a=>a.Role=="valve-tag"&&a.Text=="?-?"),"Plant placeholder identifiers are not annotated");
Assert(taggedSheets[0].Scene.Strokes.Any(s=>s.Role=="insulation"&&s.SourceId=="17A85"),"Elbows receive an insulation contour too");
var taggedBom=taggedSheets[0].Scene.BillOfMaterials.Single(b=>b.Handles.Contains("16571"));
Assert(taggedBom.Identifier=="BV-001"&&taggedBom.Designation=="BV-001","Component identifier populates the designation column");
Assert(taggedSheets[0].Scene.BillOfMaterials.Where(b=>b.Identifier.Length==0).All(b=>b.Designation==b.Standard),"Components without identifiers retain their standard designation");
var identifiedPipe=first with{Properties=new Dictionary<string,string>(first.Properties){["Tag"]="  P-101  ",["DesignStd"]="ГОСТ 8732"}};
var identifiedBom=SheetPlanner.Build(Snapshot(new[]{identifiedPipe},Array.Empty<Connection>())).BillOfMaterials.Single();
Assert(identifiedBom.Designation=="P-101"&&identifiedBom.Standard=="ГОСТ 8732","Identifier rule applies beyond valves and preserves source standard");
Assert(PartIdentifier.Read(identifiedPipe with{Properties=new Dictionary<string,string>{{"Tag","?-?"}}})=="","Unassigned Plant tag is excluded from designations");
// Terminal blind plates retain their specified paper thickness, with no weld-neck geometry.
var plateDefinition=new IsoBlockDefinition("ProjectPlate",new Dictionary<string,SheetPoint>{{"Port1",new(-.0625,0)},{"Port2",new(.0625,0)}},new(-.0625,-.25),new(.0625,.25));
var plateMap=new IsoSymbolMap("<Map><SkeyMap SKEY='FLBL' AcadBlock='ProjectPlate'/></Map>",new IsoBlockCatalog(new[]{plateDefinition}));
foreach(var direction in new[]{new ModelPoint(0,0,1),new ModelPoint(1,0,0)})
{
    var cap=new Part("test.dwg","cap","AcPpDb3dInlineAsset","L1",new[]{new Port("S1",new(0,0,0),direction)},new Dictionary<string,string>{{"PnPClassName","BlindFlange"},{"ContentIsoSymbolDefinition","TYPE=FLANGE-BLIND,SKEY=FLBL"}});
    var plateScene=SheetPlanner.Build(Snapshot(new[]{cap},Array.Empty<Connection>()),symbolMap:plateMap);
    var plate=plateScene.Symbols!.Single(g=>g.SourceId=="cap");
    Assert(plate.Block=="ProjectPlate","Blind plate uses the project SKEY mapping even with a custom block name");
    Near(Math.Sqrt(plate.XAxis.X*plate.XAxis.X+plate.XAxis.Y*plate.XAxis.Y)*.125,.125*plateMap.Catalog!.SymbolScale);
    Near(Math.Sqrt(plate.YAxis.X*plate.YAxis.X+plate.YAxis.Y*plate.YAxis.Y)*.5,.5*plateMap.Catalog!.SymbolScale);
    Assert(!plateScene.Strokes.Any(s=>s.SourceId=="cap"&&s.Role=="flange-neck"),"Blind plate has no neck");
}
foreach(var pair in new[]{("Pipe",BomCategory.Pipes),("Elbow",BomCategory.Fittings),("Tee",BomCategory.Fittings),("Reducer",BomCategory.Fittings),("Support",BomCategory.Supports),("Valve",BomCategory.Valves),("PressureGauge",BomCategory.Instruments),("BlindFlange",BomCategory.Flanges),("Gasket",BomCategory.Gaskets),("BoltSet",BomCategory.Fasteners),("Unclassified",BomCategory.Miscellaneous)})
    Assert(BomCategories.Classify(first with{Properties=new Dictionary<string,string>{{"PnPClassName",pair.Item1}}})==pair.Item2,"BOM category: "+pair.Item1);
Assert(taggedSheets[0].Scene.BillOfMaterials.First().Category==BomCategory.Pipes,"Pipes come first in the BOM");
Assert(taggedSheets[0].Scene.BillOfMaterials.Select(r=>(int)r.Category).SequenceEqual(taggedSheets[0].Scene.BillOfMaterials.Select(r=>(int)r.Category).OrderBy(c=>c)),"BOM categories follow the specified order");
Assert(taggedSheets[0].Scene.BillOfMaterials.Select(r=>r.Position).SequenceEqual(Enumerable.Range(1,taggedSheets[0].Scene.BillOfMaterials.Count)),"Category headings do not interrupt item numbering");
Assert(taggedSheets[0].Scene.Annotations.Where(a=>a.Role=="elevation").All(a=>a.Text.EndsWith("*")&&!a.Text.Contains(',')),"Elevation labels use a decimal point and a trailing reference asterisk");
Near(BomCategories.BodyHeight(new[]{identifiedBom}),21);
var largerElbowLayout=new SchematicLayout(Snapshot(new[]{elbow},Array.Empty<Connection>()));
var elbowAxis=largerElbowLayout.Project(new(1100,0,0));
foreach(var port in elbow.Ports)
{
    var point=largerElbowLayout.Project(port.Position);
    Near(Math.Sqrt(Math.Pow(point.X-elbowAxis.X,2)+Math.Pow(point.Y-elbowAxis.Y,2)),10);
}
var gridTee=new Part("test.dwg","grid-tee","AcPpDb3dInlineAsset","L1",new[]{new Port("S1",new(-100,0,0),new(-1,0,0)),new Port("S2",new(100,0,0),new(1,0,0)),new Port("S3",new(0,0,100),new(0,0,1))},new Dictionary<string,string>{{"PnPClassName","Tee"}});
var largerTeeLayout=new SchematicLayout(Snapshot(new[]{gridTee},Array.Empty<Connection>()));
var teeCenter=largerTeeLayout.Project(new(0,0,0));
foreach(var port in gridTee.Ports)
{
    var point=largerTeeLayout.Project(port.Position);
    Near(Math.Sqrt(Math.Pow(point.X-teeCenter.X,2)+Math.Pow(point.Y-teeCenter.Y,2)),10);
}
// Rigid tee shoulders are included in the overall, not separate cramped chain labels.
var nativeTee=gridTee with{Properties=new Dictionary<string,string>(gridTee.Properties){["ContentIsoSymbolDefinition"]="TYPE=TEE,SKEY=TEBW"}};
var nativeTeeMap=new IsoSymbolMap(File.ReadAllText(Path.Combine(testRoot,"data/IsoSkeyAcadBlockMap.xml")),fixtureCatalog);
var nativeTeeScene=SheetPlanner.Build(Snapshot(new[]{nativeTee},Array.Empty<Connection>()),symbolMap:nativeTeeMap);
Assert(!nativeTeeScene.Dimensions.Any(d=>d.Role=="chain"),"Tee-only shoulders do not create overlapping chain labels");
Assert(nativeTeeScene.Dimensions.Any(d=>d.Role=="overall"&&Math.Abs(d.Millimeters-200)<.01),"Real tee run length remains in overall dimension");
// A support is an attachment at CP, including libraries whose CP differs from the insertion point.
var supportPipe=first with{Ports=first.Ports.Concat(new[]{new Port("D1",new(500,0,0),new(1,0,0))}).ToArray()};
var supportPart=new Part("test.dwg","support","AcPpDb3dSupport","L1",new[]{new Port("S1",new(500,0,0),new(1,0,0))},new Dictionary<string,string>{{"PnPClassName","Support"},{"ContentIsoSymbolDefinition","TYPE=SUPPORT,SKEY=SUPD"},{"PartSizeLongDesc","Support"}});
var supportDefinition=new IsoBlockDefinition("ProjectSupport",new Dictionary<string,SheetPoint>{{"CenterPoint",new(1,2)}},new(.75,1.75),new(1.25,2.25));
var supportMap=new IsoSymbolMap("<Map><SkeyMap SKEY='SUPD' AcadBlock='ProjectSupport'/></Map>",new IsoBlockCatalog(new[]{supportDefinition}));
var supportSnapshot=Snapshot(new[]{supportPipe,supportPart},new[]{new Connection("A","D1","support","S1")});
var supportScene=SheetPlanner.Build(supportSnapshot,symbolMap:supportMap);
var supportGlyph=supportScene.Symbols!.Single(g=>g.SourceId=="support");
var supportAxis=new SchematicLayout(supportSnapshot).Project(new(500,0,0));
Near(supportGlyph.Origin.X+supportGlyph.XAxis.X+2*supportGlyph.YAxis.X,supportAxis.X);
Near(supportGlyph.Origin.Y+supportGlyph.XAxis.Y+2*supportGlyph.YAxis.Y,supportAxis.Y);
Assert(supportGlyph.Block=="ProjectSupport","Support symbol follows the project SKEY mapping");
Assert(supportScene.BillOfMaterials.Single(r=>r.Handles.Contains("support")).Category==BomCategory.Supports,"Support belongs to its BOM category");
Assert(!supportScene.Strokes.Any(s=>s.SourceId=="support"),"Support has no invented terminal pipe or neck");
Near(supportScene.Dimensions.Where(d=>d.Role=="chain").Sum(d=>d.Millimeters),1000);
var supportTyped=supportPart with{Properties=new Dictionary<string,string>(supportPart.Properties){["underSupportType"]="  С21  "}};
var supportTypedScene=SheetPlanner.Build(supportSnapshot with{Parts=new[]{supportPipe,supportTyped}},symbolMap:supportMap);
var supportLeader=supportTypedScene.Annotations.Single(a=>a.Role=="support-type");
Assert(supportLeader.Text=="С21"&&supportLeader.LeaderKnee is not null,"Support construction type gets a native leader plan");
Near(supportLeader.Anchor!.Value.X,supportAxis.X);Near(supportLeader.Anchor.Value.Y,supportAxis.Y);
Assert(!supportScene.Annotations.Any(a=>a.Role=="support-type"),"Empty support construction type does not generate a leader");
var supportPlaceholder=supportTyped with{Properties=new Dictionary<string,string>(supportTyped.Properties){["underSupportType"]="?"}};
Assert(!SheetPlanner.Build(supportSnapshot with{Parts=new[]{supportPipe,supportPlaceholder}},symbolMap:supportMap).Annotations.Any(a=>a.Role=="support-type"),"Unassigned support type does not generate a leader");
var missingPart=new Part("test.dwg","missing-instrument","AcPpDb3dInlineAsset","L1",new[]{new Port("S1",new(0,0,0),new(0,0,-1))},new Dictionary<string,string>{{"PnPClassName","Instrument"},{"ContentIsoSymbolDefinition","TYPE=INSTRUMENT-DIAL,SKEY=IDSC"},{"PartSizeLongDesc","Pressure sensor"}});
foreach(var missingMap in new[]{new IsoSymbolMap("<Map/>",new IsoBlockCatalog(Array.Empty<IsoBlockDefinition>())),new IsoSymbolMap("<Map><SkeyMap SKEY='IDSC' AcadBlock='AbsentBlock'/></Map>",new IsoBlockCatalog(Array.Empty<IsoBlockDefinition>()))})
{
    var missingScene=SheetPlanner.Build(Snapshot(new[]{missingPart},Array.Empty<Connection>()),symbolMap:missingMap);
    var missingGlyph=missingScene.Symbols!.Single();
    Assert(missingGlyph.Variant=="missing"&&missingGlyph.LocalContours!.Count==3,"Missing SKEY or library block becomes square and two diagonals");
    var bounds=missingGlyph.LocalBounds!.Select(p=>new SheetPoint(missingGlyph.Origin.X+missingGlyph.XAxis.X*p.X+missingGlyph.YAxis.X*p.Y,missingGlyph.Origin.Y+missingGlyph.XAxis.Y*p.X+missingGlyph.YAxis.Y*p.Y)).ToArray();
    Near(bounds.Max(p=>p.X)-bounds.Min(p=>p.X),10);Near(bounds.Max(p=>p.Y)-bounds.Min(p=>p.Y),10);
    Assert(missingScene.BillOfMaterials.Single().Category==BomCategory.Instruments,"Unknown instrument remains in the BOM");
    Assert(missingScene.Warnings.Any(w=>w.Contains("IDSC")&&w.Contains("10×10")),"Placeholder warning retains the missing ISO definition");
    Assert(missingScene.Annotations.Any(a=>a.SourceId==missingPart.Handle&&a.Role=="position"),"Placeholder retains the item position");
}
var missingInline=first with{Kind="AcPpDb3dInlineAsset",Properties=new Dictionary<string,string>{{"PnPClassName","UnknownComponent"},{"ContentIsoSymbolDefinition","TYPE=UNKNOWN,SKEY=ZZZZ"}}};
var missingInlineScene=SheetPlanner.Build(Snapshot(new[]{missingInline},Array.Empty<Connection>()),symbolMap:new IsoSymbolMap("<Map/>",new IsoBlockCatalog(Array.Empty<IsoBlockDefinition>())));
Assert(missingInlineScene.Symbols!.Single().Variant=="missing","Unknown inline component also gets a fallback square");
Assert(missingInlineScene.BillOfMaterials.Single().Handles.Contains("A"),"Unknown inline component remains accounted for in the BOM");
// Real stations survive small skew instead of projecting an artificial point off the model graph.
var skewEnd=new ModelPoint(2000,.5,0);
var skewPipe=Pipe("skew",new(1000,0,0),skewEnd);
var skewSnapshot=Snapshot(new[]{first,skewPipe},new[]{new Connection("A","S2","skew","S1")});
var skewScene=SheetPlanner.Build(skewSnapshot);
Assert(skewScene.Dimensions.Any(d=>d.ModelEnd==skewEnd||d.ModelStart==skewEnd),"Slightly skewed run dimensions retain actual model nodes");
var separateLayout=new SchematicLayout(separated);
var firstEnd=separateLayout.Project(new(1000,0,0),"A");var secondStart=separateLayout.Project(new(1000,0,0),"B");
Assert(Math.Sqrt(Math.Pow(firstEnd.X-secondStart.X,2)+Math.Pow(firstEnd.Y-secondStart.Y,2))>1,"Coincident unconnected ports retain separate source-specific positions");
var separateScene=SheetPlanner.Build(separated);
Assert(separateScene.Strokes.Count(s=>s.Role=="pipe")==2,"Source-specific mapping handles coincident independent components without inventing a link");
Assert(separateScene.Welds!.Count==0,"Coincident independent components do not acquire a false weld");
var naturalPlate=new IsoBlockDefinition("ThinPlate",new Dictionary<string,SheetPoint>{{"Port1",new(-.05,0)},{"Port2",new(.05,0)}},new(-.05,-.25),new(.05,.45));
var naturalMap=new IsoSymbolMap("<Map><SkeyMap SKEY='SB' AcadBlock='ThinPlate'/></Map>",new IsoBlockCatalog(new[]{naturalPlate}));
var naturalPart=first with{Kind="AcPpDb3dInlineAsset",Properties=new Dictionary<string,string>{{"PnPClassName","BlindDisk"},{"ContentIsoSymbolDefinition","TYPE=MISC-COMPONENT,SKEY=SB"}}};
var naturalSnapshot=Snapshot(new[]{naturalPart},Array.Empty<Connection>());
var naturalLayout=new SchematicLayout(naturalSnapshot,symbolMap:naturalMap);
var naturalA=naturalLayout.Project(naturalPart.Ports[0].Position);var naturalB=naturalLayout.Project(naturalPart.Ports[1].Position);
Near(Math.Sqrt(Math.Pow(naturalB.X-naturalA.X,2)+Math.Pow(naturalB.Y-naturalA.Y,2)),.1*naturalMap.Catalog!.SymbolScale);
foreach(int turn in Enumerable.Range(0,4))
{
    var view=SheetSetPlanner.Build(linked,quarterTurns:turn);
    Assert(view.Count==1&&view[0].Scene.QuarterTurns==turn,"Selected isometric rotation is retained in the scene");
    Near(view[0].Scene.Dimensions.Where(d=>d.Role=="chain").Sum(d=>d.Millimeters),2000);
}
var weldFlange=new Part("test.dwg","weld-flange","AcPpDb3dInlineAsset","L1",new[]{new Port("S1",new(0,0,0),new(-1,0,0),"FL"),new Port("S2",new(50,0,0),new(1,0,0),"BV")},new Dictionary<string,string>{{"PnPClassName","Flange"},{"ContentIsoSymbolDefinition","TYPE=FLANGE,SKEY=FLWN"}});
var weldPipe=Pipe("weld-pipe",new(50,0,0),new(1050,0,0));weldPipe=weldPipe with{Ports=weldPipe.Ports.Select(p=>p with{EndCondition="BV"}).ToArray()};
var weldFlangeDefinition=new IsoBlockDefinition("LibraryFlange",new Dictionary<string,SheetPoint>{{"Port1",new(-.15625,0)},{"Port2",new(.15625,0)}},new(-.15625,-.25),new(.15625,.25));
var weldFlangeMap=new IsoSymbolMap("<Map><SkeyMap SKEY='FLWN' AcadBlock='LibraryFlange'/></Map>",new IsoBlockCatalog(new[]{weldFlangeDefinition}));
var weldFlangeSnapshot=Snapshot(new[]{weldFlange,weldPipe},new[]{new Connection("weld-flange","S2","weld-pipe","S1")});
var weldFlangeScene=SheetPlanner.Build(weldFlangeSnapshot,symbolMap:weldFlangeMap);
var weldFlangeGlyph=weldFlangeScene.Symbols!.Single(g=>g.SourceId=="weld-flange");
var flangeWeld=weldFlangeScene.Welds!.Single();
Near(weldFlangeGlyph.Origin.X+weldFlangeGlyph.XAxis.X*.15625,flangeWeld.Position.X);
Near(weldFlangeGlyph.Origin.Y+weldFlangeGlyph.XAxis.Y*.15625,flangeWeld.Position.Y);
Assert(!weldFlangeScene.Strokes.Any(s=>s.Role=="flange-neck"),"Weld is on the flange library port with no added neck line");
var insulationPipe=insulatedScene.Strokes.Single(s=>s.Role=="pipe");
foreach(var contour in insulatedScene.Strokes.Where(s=>s.Role=="insulation"))
    Near(Math.Sqrt(Math.Pow(contour.Points[0].X-insulationPipe.Points[0].X,2)+Math.Pow(contour.Points[0].Y-insulationPipe.Points[0].Y,2)),3);
Part Accessory(string handle,string owner,string kind,string quantity)=>new("test.dwg",handle,"ConnectorSubPart","L1",Array.Empty<Port>(),new Dictionary<string,string>{{"ConnectorHandle",owner},{"PnPClassName",kind},{"PartSizeLongDesc",kind},{"IsoQuantity",quantity},{"IsoQuantityUnit",kind=="BoltSet"?"компл.":"шт."}});
var hardwareSnapshot=linked with{Accessories=new[]{Accessory("g1","A","Gasket","1"),Accessory("g2","B","Gasket","1"),Accessory("b1","A","BoltSet","8"),Accessory("b2","B","BoltSet","12")}};
var hardwareScene=SheetPlanner.Build(hardwareSnapshot);
Near(hardwareScene.BillOfMaterials.Single(r=>r.Category==BomCategory.Gaskets).Quantity,2);
Near(hardwareScene.BillOfMaterials.Single(r=>r.Category==BomCategory.Fasteners).Quantity,20);
Assert(hardwareScene.Dimensions.Select(d=>d.Millimeters).SequenceEqual(scene.Dimensions.Select(d=>d.Millimeters)),"Gaskets and fasteners in BOM do not create extra dimension chains");
Assert(!hardwareScene.Strokes.Any(s=>s.SourceId is "g1" or "g2" or "b1" or "b2"),"Connector accessories do not create schematic geometry");
var splitHardware=SheetSetPlanner.Build(tallSnapshot with{Accessories=new[]{Accessory("left-gasket","T00","Gasket","1"),Accessory("right-gasket","T39","Gasket","1")}});
Near(splitHardware.Sum(s=>s.Scene.BillOfMaterials.Where(r=>r.Category==BomCategory.Gaskets).Sum(r=>r.Quantity)),2);
Assert(splitHardware.SelectMany(s=>s.Snapshot.Accessories??Array.Empty<Part>()).Select(p=>p.Handle).Distinct().Count()==2,"Accessories belong to their owner's sheet exactly once");
var filteredHardware=SheetPlanner.Build(hardwareSnapshot,excludeFlangeHardware:true);
Assert(filteredHardware.BillOfMaterials.All(r=>r.Category is not (BomCategory.Gaskets or BomCategory.Fasteners)),"Hardware exclusion removes only BOM rows");
Assert(filteredHardware.Dimensions.Select(d=>d.Millimeters).SequenceEqual(hardwareScene.Dimensions.Select(d=>d.Millimeters)),"Hardware BOM exclusion preserves engineering dimensions");
var filteredFlange=SheetPlanner.Build(weldFlangeSnapshot,symbolMap:weldFlangeMap,excludeFlangeHardware:true);
Assert(filteredFlange.BillOfMaterials.All(r=>r.Kind!="Flange"),"Flange BOM exclusion removes flange positions");
Assert(filteredFlange.Symbols!.Count==weldFlangeScene.Symbols!.Count&&filteredFlange.Welds!.Count==weldFlangeScene.Welds!.Count,"Flange BOM exclusion preserves flange symbols and welds");
Assert(!filteredFlange.Annotations.Any(a=>a.SourceId=="weld-flange"&&a.Role=="position"),"Excluded flange has no orphaned BOM position");
Assert(filteredFlange.ExcludeFlangeHardware,"BOM exclusion is recorded in the published scene");
foreach(int turn in Enumerable.Range(0,4))
{
    var insulatedElbow=elbow with{Properties=new Dictionary<string,string>(elbow.Properties){["InsulationThickness"]="40"}};
    var smoothInsulationScene=SheetPlanner.Build(Snapshot(new[]{insulatedElbow},Array.Empty<Connection>()),quarterTurns:turn);
    var axisPath=smoothInsulationScene.Strokes.Single(s=>s.Role=="elbow").Points;
    foreach(var contour in smoothInsulationScene.Strokes.Where(s=>s.Role=="insulation"))
    {
        Assert(contour.Points.Count>=32,"Each insulated elbow has a sampled smooth bend");
        Assert(contour.Points.All(p=>double.IsFinite(p.X)&&double.IsFinite(p.Y)),"Inner and outer bend offsets remain finite in every view");
        Near(Math.Sqrt(Math.Pow(contour.Points[0].X-axisPath[0].X,2)+Math.Pow(contour.Points[0].Y-axisPath[0].Y,2)),3);
        Near(Math.Sqrt(Math.Pow(contour.Points[^1].X-axisPath[^1].X,2)+Math.Pow(contour.Points[^1].Y-axisPath[^1].Y,2)),3);
        bool smooth=true;
        for(int i=1;i<contour.Points.Count-1;i++)
        {
            var a=contour.Points[i-1];var b=contour.Points[i];var c=contour.Points[i+1];
            double ux=b.X-a.X,uy=b.Y-a.Y,vx=c.X-b.X,vy=c.Y-b.Y,den=Math.Sqrt((ux*ux+uy*uy)*(vx*vx+vy*vy));
            if(den>1e-10&&(ux*vx+uy*vy)/den<Math.Cos(Math.PI/18))smooth=false;
        }
        Assert(smooth,"Insulation enters and exits the elbow without a sharp corner");
    }
}
var intersectingDimensions=new[]{new PipeDimension("one",new(0,0),new(100,0),100,"overall","one",new(50,10),new(0,1),TextPosition:new(50,12.8)),new PipeDimension("two",new(50,-50),new(50,50),100,"overall","two",new(40,0),new(-1,0),TextPosition:new(37.2,0))};
Assert(DimensionRouting.CrossingCount(intersectingDimensions)>0,"Regression fixture initially has crossing dimension lines");
var routed=DimensionRouting.Route(intersectingDimensions,Array.Empty<Stroke>(),Array.Empty<SymbolGlyph>(),0);
Assert(DimensionRouting.CrossingCount(routed)==0,"Whole-chain routing removes proper baseline and extension crossings");
Assert(routed.Select(d=>d.Millimeters).SequenceEqual(intersectingDimensions.Select(d=>d.Millimeters)),"Dimension routing never changes engineering values");
var setNames=DrawingFileNames.OutputSet("out","LINE",2,1);
Assert(Path.GetFileName(setNames.Drawings[0])=="LINE (1)(L1).dwg"&&Path.GetFileName(setNames.Pdf)=="LINE (1).pdf","One suffix applies consistently to the whole drawing and PDF set");
var occupiedNames=DrawingFileNames.OutputSet("out","LINE",2,1).Drawings.Take(1).ToHashSet();
var nextSet=DrawingFileNames.IndexedOutputSet("out","LINE",2,occupiedNames.Contains);
Assert(Path.GetFileName(nextSet.Pdf)=="LINE (2).pdf","An occupied DWG advances the index of the complete set");
foreach(var triangle in riseScene.Strokes.Where(s=>s.Role=="slope-triangle"))
    Assert(triangle.Points[0]==riseScene.Strokes.Single(s=>s.Role=="pipe").Points[0]&&triangle.Points[^1]==riseScene.Strokes.Single(s=>s.Role=="pipe").Points[^1],"Hatch density does not change the original slope triangle geometry");
var obstructedDimension=intersectingDimensions.Take(1).ToArray();
var unobstructedDimension=DimensionRouting.Route(obstructedDimension,new[]{new Stroke("other-pipe","pipe",new SheetPoint[]{new(50,5),new(50,15)})},Array.Empty<SymbolGlyph>(),0);
Assert(unobstructedDimension[0].DimensionLinePoint!.Value.Y<0,"Even a single dimension row chooses the free side of another pipe");
var fgbJoint=new Part("test.dwg","FGJ","AcPpDb3dConnector","L1",new[]{new Port("S1",new(-4.5,0,0),new(-1,0,0)),new Port("S2",new(0,0,0),new(1,0,0))},new Dictionary<string,string>());
var fgbSnapshot=weldFlangeSnapshot with{Parts=weldFlangeSnapshot.Parts.Append(fgbJoint).ToArray(),Connections=weldFlangeSnapshot.Connections.Append(new Connection("FGJ","S2","weld-flange","S1")).ToArray(),Accessories=new[]{Accessory("FGJ#gasket","FGJ","Gasket","1"),Accessory("FGJ#bolts","FGJ","BoltSet","8")}};
var fgbScene=SheetPlanner.Build(fgbSnapshot,symbolMap:weldFlangeMap);
int flangeNumber=fgbScene.BillOfMaterials.Single(r=>r.Kind=="Flange").Position,boltNumber=fgbScene.BillOfMaterials.Single(r=>r.Kind=="BoltSet").Position,gasketNumber=fgbScene.BillOfMaterials.Single(r=>r.Kind=="Gasket").Position;
Assert(fgbScene.Annotations.Any(a=>a.Role=="position-group"&&a.Text==$"{flangeNumber},{boltNumber},{gasketNumber}"),"Flange, bolt set and gasket share their current BOM numbers in the requested order");
Assert(!fgbScene.Annotations.Any(a=>a.Role=="position"&&a.SourceId=="weld-flange"),"Grouped flange position replaces its single numeric label");
Assert(!SheetPlanner.Build(fgbSnapshot,symbolMap:weldFlangeMap,excludeFlangeHardware:true).Annotations.Any(a=>a.Role=="position-group"),"Excluded hardware has no grouped numeric annotations");
PipelineSnapshot LoopFixture(ModelPoint[] nodes,(int A,int B)[] links,int valve=-1)
{
    var parts=links.Select((e,i)=>Pipe("loop-"+i,nodes[e.A],nodes[e.B])).ToArray();
    if(valve>=0)parts[valve]=parts[valve] with{Properties=new Dictionary<string,string>{{"PnPClassName","Valve"}}};
    var joins=new List<Connection>();
    for(int node=0;node<nodes.Length;node++)
    {
        var ports=links.SelectMany((e,i)=>new[]{(Node:e.A,Handle:parts[i].Handle,Port:"S1"),(Node:e.B,Handle:parts[i].Handle,Port:"S2")}).Where(p=>p.Node==node).ToArray();
        foreach(var next in ports.Skip(1))joins.Add(new(ports[0].Handle,ports[0].Port,next.Handle,next.Port));
    }
    return Snapshot(parts,joins.ToArray());
}
var valveLoop=LoopFixture(new ModelPoint[]{new(0,0,0),new(500,0,0),new(600,0,0),new(1000,0,0),new(1000,1000,0),new(0,1000,0)},new[]{(0,1),(1,2),(2,3),(3,4),(4,5),(5,0)},1);
foreach(int turn in Enumerable.Range(0,4))
{
    var loopLayout=new SchematicLayout(valveLoop,quarterTurns:turn);
    Assert(loopLayout.ClosedLoops==1,"A ring is handled as one constrained cycle");
    foreach(var connection in valveLoop.Connections)
    {
        var a=valveLoop.Parts.Single(p=>p.Handle==connection.FirstHandle).Ports.Single(p=>p.Name==connection.FirstPort);
        var b=valveLoop.Parts.Single(p=>p.Handle==connection.SecondHandle).Ports.Single(p=>p.Name==connection.SecondPort);
        var pa=loopLayout.Project(a.Position,connection.FirstHandle);var pb=loopLayout.Project(b.Position,connection.SecondHandle);Near(pa.X,pb.X);Near(pa.Y,pb.Y);
    }
    foreach(var part in valveLoop.Parts)
    {
        var a=loopLayout.Project(part.Ports[0].Position,part.Handle);var b=loopLayout.Project(part.Ports[1].Position,part.Handle);double length=Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));
        if(part.Properties["PnPClassName"]=="Valve")Near(length,10);else Assert(length>=4.1-1e-7,"Cycle solver preserves space for at least one dimension digit");
    }
}
var coupledLoops=LoopFixture(new ModelPoint[]{new(0,0,0),new(1000,0,0),new(1000,1000,0),new(0,1000,0),new(2000,0,0),new(2000,1000,0)},new[]{(0,1),(1,2),(2,3),(3,0),(1,4),(4,5),(5,2)});
Assert(new SchematicLayout(coupledLoops).ClosedLoops==2,"Coupled cycles with a shared pipe are solved together");
var boundaryTall=tallSnapshot with{Connections=tallSnapshot.Connections.Append(new Connection("T00","S1","outside-line","S2")).ToArray()};
Assert(SheetSetPlanner.Build(boundaryTall).Count==2,"A boundary object outside the selected line does not break adjacent-sheet context");
var ringNodes=Enumerable.Range(0,120).Select(i=>{int side=i/30,k=i%30;return side switch{0=>new ModelPoint(k*100,0,0),1=>new(3000,k*100,0),2=>new(3000-k*100,3000,0),_=>new(0,3000-k*100,0)};}).ToArray();
var ringSnapshot=LoopFixture(ringNodes,Enumerable.Range(0,120).Select(i=>(i,(i+1)%120)).ToArray());
var ringSheets=SheetSetPlanner.Build(ringSnapshot);
Assert(ringSheets.Count>=2,"An oversized ring can be partitioned at two pipe connections");
Assert(ringSheets.Sum(s=>s.Snapshot.Parts.Count)==120&&ringSheets.SelectMany(s=>s.Snapshot.Parts).Select(p=>p.Handle).Distinct().Count()==120,"Ring partition preserves every component exactly once");
var slopedSupportPipe=Pipe("support-run",new(0,0,0),new(1000,0,10));slopedSupportPipe=slopedSupportPipe with{Ports=slopedSupportPipe.Ports.Append(new Port("D1",new(500,0,5),new(1,0,0))).ToArray()};
// Internal support/tap stations share the physical pipe minimum instead of multiplying it.
var shortStationPipe=Pipe("short-stations",new(0,0,0),new(1,0,0));
shortStationPipe=shortStationPipe with{Ports=shortStationPipe.Ports.Concat(new[]{new Port("D1",new(.25,0,0),new(1,0,0)),new Port("D2",new(.75,0,0),new(1,0,0))}).ToArray()};
var stationLayout=new SchematicLayout(Snapshot(new[]{shortStationPipe},Array.Empty<Connection>()),compactLevel:3);
var stationStart=stationLayout.Project(new(0,0,0),shortStationPipe.Handle);var stationEnd=stationLayout.Project(new(1,0,0),shortStationPipe.Handle);
Assert(Math.Sqrt(Math.Pow(stationEnd.X-stationStart.X,2)+Math.Pow(stationEnd.Y-stationStart.Y,2))>=10&&Math.Sqrt(Math.Pow(stationEnd.X-stationStart.X,2)+Math.Pow(stationEnd.Y-stationStart.Y,2))<30,"Internal stations reserve text space without assigning each a physical 10 mm minimum");
var stationMid=stationLayout.Project(new(.25,0,0),shortStationPipe.Handle);
Assert(Math.Sqrt(Math.Pow(stationMid.X-stationStart.X,2)+Math.Pow(stationMid.Y-stationStart.Y,2))>=4.1-1e-7&&Math.Sqrt(Math.Pow(stationMid.X-stationStart.X,2)+Math.Pow(stationMid.Y-stationStart.Y,2))<10,"A station interval only grows enough to accommodate its dimension label");
var slopeSupport=supportPart with{Ports=new[]{new Port("S1",new(500,0,5),new(1,0,0))}};
var supportHeights=SheetPlanner.Build(Snapshot(new[]{slopedSupportPipe,slopeSupport},new[]{new Connection("support-run","D1","support","S1")}),symbolMap:supportMap);
Assert(!supportHeights.Annotations.Any(a=>a.Role=="elevation"&&a.Text.Contains("0.005")),"Support attachment does not generate an extra intermediate pipe elevation");
Assert(supportHeights.Annotations.Any(a=>a.Role=="elevation"&&a.Text.Contains("0.010")),"True sloping-pipe end elevation remains annotated");
var checkOutput=Environment.GetEnvironmentVariable("ISO_CHECK_OUTPUT");
if(!string.IsNullOrEmpty(checkOutput))
{
    Directory.CreateDirectory(checkOutput);
    foreach(var sheet in valveSheets)File.WriteAllText(Path.Combine(checkOutput,$"sheet-{sheet.Number}.json"),System.Text.Json.JsonSerializer.Serialize(sheet.Scene));
}
var attachedPipe=tallSnapshot.Parts.First(p=>p.Properties.GetValueOrDefault("PnPClassName")=="Pipe");
var attachedPort=attachedPipe.Ports.First();
var attachedSupport=supportPart with{Handle="attached-support",SourceDrawing=attachedPipe.SourceDrawing,Line=attachedPipe.Line,Ports=new[]{new Port("S1",attachedPort.Position,new(1,0,0))}};
var supportedTall=tallSnapshot with{Parts=tallSnapshot.Parts.Append(attachedSupport).ToArray(),Connections=tallSnapshot.Connections.Append(new Connection(attachedPipe.Handle,attachedPort.Name,attachedSupport.Handle,"S1")).ToArray()};
var supportedSheets=SheetSetPlanner.Build(supportedTall,supportMap);
Assert(supportedSheets.Single(s=>s.Snapshot.Parts.Any(p=>p.Handle==attachedSupport.Handle)).Snapshot.Parts.Any(p=>p.Handle==attachedPipe.Handle),"A support remains on the sheet containing its own pipe");
Assert(supportedSheets.Sum(s=>s.Snapshot.Parts.Count)==supportedTall.Parts.Count,"Repacking preserves each source object exactly once");
var nonfiniteDirection=slopedSupportPipe with{Ports=slopedSupportPipe.Ports.Select((p,i)=>i==0?p with{Direction=new(double.NaN,0,0)}:p).ToArray()};
Assert(SnapshotValidation.Validate(Snapshot(new[]{nonfiniteDirection},Array.Empty<Connection>())).Errors.Count>0,"Invalid direction is diagnosed before it can propagate NaN into layout");
var originalWeldFlange=SheetPlanner.Build(weldFlangeSnapshot,symbolMap:weldFlangeMap);
var probe=originalWeldFlange.Dimensions.First();
var probeCenter=probe.TextPosition!.Value;
var ghostContour=new SheetPoint[]{new(-2,-2),new(2,-2),new(2,2),new(-2,2),new(-2,-2)};
var ghost=new SymbolGlyph("adjacent-page","","","test-contour",probeCenter,new(1,0),new(0,1),Context:true,LocalBounds:ghostContour.Take(4).ToArray(),LocalContours:new[]{(IReadOnlyList<SheetPoint>)ghostContour});
var withGhost=SheetPlanner.Build(weldFlangeSnapshot,symbolMap:weldFlangeMap,contextSymbols:new[]{ghost});
Assert(withGhost.Dimensions.All(d=>Math.Abs(d.TextPosition!.Value.X-probeCenter.X)>2||Math.Abs(d.TextPosition!.Value.Y-probeCenter.Y)>2),"Dimension text avoids real continuation symbol contours");
string familyTest=Path.Combine(Path.GetTempPath(),"specgen-names-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(familyTest);
foreach(string name in new[]{"LINE(L1).dwg","LINE(L7).dwg","LINE.dwg","LINE (1)(L7).dwg","OTHER(L7).dwg","LINE(L7).dwg.bak"})File.WriteAllText(Path.Combine(familyTest,name),"");
var oldFamily=DrawingFileNames.ObsoleteDrawings(Path.Combine(familyTest,"LINE.pdf"),new[]{Path.Combine(familyTest,"LINE(L1).dwg")}).Select(Path.GetFileName).OrderBy(x=>x).ToArray();
Assert(oldFamily.SequenceEqual(new[]{"LINE(L7).dwg","LINE.dwg"}.OrderBy(x=>x)),"Retirement selects only obsolete exact-family DWGs, retaining indexed and unrelated files");
var overflowAccessories=Enumerable.Range(0,60).Select(i=>
{
    var accessory=Accessory("FGJ#extra-"+i,"FGJ","Miscellaneous","1");
    return accessory with{Properties=new Dictionary<string,string>(accessory.Properties){["PartSizeLongDesc"]="Extra component "+i}};
}).ToArray();
var overflowSnapshot=fgbSnapshot with{Accessories=fgbSnapshot.Accessories!.Concat(overflowAccessories).ToArray()};
var overflowSheets=SheetSetPlanner.Build(overflowSnapshot,weldFlangeMap);
Assert(overflowSheets.Count(s=>!s.Scene.BomOnly)==1,"BOM overflow does not split fitting geometry");
Assert(overflowSheets.Count(s=>s.Scene.BomOnly)>=2,"Large BOM continues across multiple clean sheets");
Assert(overflowSheets.All(s=>BomCategories.Fits(s.Scene.BillOfMaterials)),"Every paginated table includes category heading space");
Assert(overflowSheets.Where(s=>s.Scene.BomOnly).All(s=>s.Snapshot.Parts.Count==0&&s.Scene.Strokes.Count==0&&s.Scene.Dimensions.Count==0&&s.Scene.Symbols!.Count==0&&s.Scene.Welds!.Count==0),"BOM-only sheets contain no pipeline graphics or duplicate source parts");
var overflowRows=overflowSheets.SelectMany(s=>s.Scene.BillOfMaterials).ToArray();
Assert(overflowRows.Select(r=>r.Position).Distinct().Count()==overflowRows.Length&&overflowRows.SelectMany(r=>r.Handles).Count(h=>h.StartsWith("FGJ#extra-"))==60,"Overflow preserves all row positions and every accessory exactly once");
Assert(overflowSheets.Select((s,i)=>s.Number==i+1&&s.Total==overflowSheets.Count).All(x=>x),"Geometry and BOM sheets share consecutive total numbering");
var numericInsulation=new Stroke("insulation-only","insulation",new SheetPoint[]{new(0,0),new(400,400)});
var noInsulation=SheetPlanner.Build(weldFlangeSnapshot,symbolMap:weldFlangeMap);
var withInsulationCrossing=SheetPlanner.Build(weldFlangeSnapshot,symbolMap:weldFlangeMap,contextStrokes:new[]{numericInsulation});
Assert(System.Text.Json.JsonSerializer.Serialize(noInsulation.Dimensions)==System.Text.Json.JsonSerializer.Serialize(withInsulationCrossing.Dimensions),"Insulation crossing does not move dimension baselines or text");
Assert(System.Text.Json.JsonSerializer.Serialize(noInsulation.Annotations)==System.Text.Json.JsonSerializer.Serialize(withInsulationCrossing.Annotations),"Insulation crossing does not move annotations");
// A cancellation is cooperative and scoped to this run; it must not poison later layouts.
using(var preCancelled=new CancellationTokenSource())
{
    preCancelled.Cancel();bool cancelled=false;try{SheetSetPlanner.Build(linked,cancellationToken:preCancelled.Token);}catch(OperationCanceledException){cancelled=true;}
    Assert(cancelled,"Already-cancelled planning exits immediately");
}
using(var duringPlanning=new CancellationTokenSource())
{
    bool cancelled=false;int reports=0;
    try{SheetSetPlanner.Build(tallSnapshot,cancellationToken:duringPlanning.Token,progress:new CallbackProgress<PlanningProgress>(_=>{reports++;duringPlanning.Cancel();}));}catch(OperationCanceledException){cancelled=true;}
    Assert(cancelled&&reports>0,"Planning reports progress and honors cancellation during the search");
}
Assert(SheetSetPlanner.Build(linked).Count==1,"A cancelled run leaves no cancellation state in the next run");
for(int turn=0;turn<4;turn++)foreach(string skey in new[]{"SUPS","SUPN"})
{
    var turned=supportPart with{Properties=new Dictionary<string,string>(supportPart.Properties){["ContentIsoSymbolDefinition"]="TYPE=SUPPORT,SKEY="+skey}};
    var normal=SheetPlanner.Build(supportSnapshot,quarterTurns:turn,symbolMap:supportMap).Symbols!.Single(g=>g.SourceId=="support");
    var flippedMap=new IsoSymbolMap($"<Map><SkeyMap SKEY='{skey}' AcadBlock='ProjectSupport'/></Map>",supportMap.Catalog);
    var flipped=SheetPlanner.Build(supportSnapshot with{Parts=supportSnapshot.Parts.Select(p=>p.Handle==supportPart.Handle?turned:p).ToArray()},quarterTurns:turn,symbolMap:flippedMap).Symbols!.Single(g=>g.SourceId=="support");
    Near(flipped.XAxis.X,-normal.XAxis.X);Near(flipped.XAxis.Y,-normal.XAxis.Y);Near(flipped.YAxis.X,-normal.YAxis.X);Near(flipped.YAxis.Y,-normal.YAxis.Y);
    Near(flipped.Origin.X+flipped.XAxis.X+2*flipped.YAxis.X,normal.Origin.X+normal.XAxis.X+2*normal.YAxis.X);Near(flipped.Origin.Y+flipped.XAxis.Y+2*flipped.YAxis.Y,normal.Origin.Y+normal.XAxis.Y+2*normal.YAxis.Y);
}
foreach(var d in tallSheets.SelectMany(s=>s.Scene.Dimensions))
{
    double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,l=Math.Sqrt(dx*dx+dy*dy);var ex=d.ExtensionDirection!.Value;var line=d.DimensionLinePoint!.Value;
    var mid=new SheetPoint((d.Start.X+d.End.X)/2,(d.Start.Y+d.End.Y)/2);double nx=-dy/l,ny=dx/l;
    double t=((line.X-mid.X)*nx+(line.Y-mid.Y)*ny)/(ex.X*nx+ex.Y*ny);
    if(dx<-.0001||(Math.Abs(dx)<.0001&&dy<0)){nx=-nx;ny=-ny;}
    Near(d.TextPosition!.Value.X,mid.X+ex.X*t+nx*2.8);Near(d.TextPosition.Value.Y,mid.Y+ex.Y*t+ny*2.8);
}
var nearUnconnected=Snapshot(new[]{Pipe("free-one",new(0,0,0),new(1000,0,0)),Pipe("free-two",new(1000.005,0,0),new(2000,0,0))},Array.Empty<Connection>());
Assert(SnapshotValidation.Validate(nearUnconnected).Warnings.Any(w=>w.Contains("Нет соединения:")),"Near-coincident unconnected opposing ports are explicitly diagnosed without inventing a connection");
// Exhausting the search budget must escape capacity-fallback catches and clear per-run state.
var executionType=typeof(SheetSetPlanner).Assembly.GetType("IsoGenerator.Core.PlanningExecution")!;
using(var budgetScope=(IDisposable)Activator.CreateInstance(executionType,new object[]{CancellationToken.None,null})!)
{
    var reportMethod=executionType.GetMethod("Report")!;bool budgetStopped=false;
    try{for(int i=0;i<514;i++)reportMethod.Invoke(null,new object[]{"budget regression",true});}
    catch(System.Reflection.TargetInvocationException ex) when(ex.InnerException is PlanningLimitException){budgetStopped=true;}
    Assert(budgetStopped,"Variant budget terminates search instead of being swallowed as sheet capacity");
}
Assert(SheetSetPlanner.Build(linked).Count==1,"Budget exhaustion does not poison the next planning run");
Assert(SheetSetPlanner.Build(separated).Count==2,"Disconnected pipe pieces never share a drawing sheet");
var cpOnlySupport=supportPart with{Ports=new[]{new Port("S1",new(500,0,0),new(1,0,0))}};
var cpOnlySheets=SheetSetPlanner.Build(Snapshot(new[]{first,cpOnlySupport},Array.Empty<Connection>()),symbolMap:supportMap);
Assert(cpOnlySheets.Count==1&&cpOnlySheets[0].Snapshot.Connections.Any(c=>c.FirstHandle==first.Handle&&c.SecondHandle==cpOnlySupport.Handle),"An unlinked support on one unique pipe axis stays attached to that pipe by CP");
Assert(!cpOnlySheets.SelectMany(s=>s.Scene.Welds??Array.Empty<WeldMarker>()).Any(w=>w.FirstHandle==cpOnlySupport.Handle||w.SecondHandle==cpOnlySupport.Handle),"A support attachment never creates a process weld");
bool ambiguousSupport=false;
try{SheetSetPlanner.Build(Snapshot(new[]{first,first with{Handle="overlapping-pipe"},cpOnlySupport},Array.Empty<Connection>()),symbolMap:supportMap);}
catch(InvalidDataException ex){ambiguousSupport=ex.Message.Contains("однозначно");}
Assert(ambiguousSupport,"An ambiguous support host is reported instead of attaching to an arbitrary pipe");
var uniformStyle=IsoStyleSettings.Parse("<IsoConfigDefinition><Themes><Theme Name='Default'><Symbols Scale='10'/></Theme><Theme Name='Small Bore Piping' Enabled='true'><Symbols Scale='15'/></Theme></Themes><FieldFilter Name='SmallBorePiping' Fields='25'/><BranchPatternTheme Name='Vent/Drain Piping' MaxComponentCount='6' MaxPipeCount='1'/></IsoConfigDefinition>");
foreach(string diameter in new[]{"15","25","32","150"})
{
    var sized=first with{Properties=new Dictionary<string,string>(first.Properties){["NominalDiameter"]=diameter}};
    Near(uniformStyle.ScaleFor(sized),10);
    Near((fixtureCatalog with{Style=uniformStyle}).ScaleFor(sized),10);
}
Assert(uniformStyle.SmallBoreDiameter==25&&uniformStyle.VentDrainMaxComponents==6&&uniformStyle.VentDrainMaxPipes==1,"Uniform symbol scaling preserves vent/drain recognition settings");
foreach(var sample in new[]{(Value:1.01,Text:"1.1"),(Value:1.2,Text:"1.2"),(Value:1.2000000000000002,Text:"1.2"),(Value:.001,Text:"0.1"),(Value:2.0,Text:"2.0")})
{
 var row=new BomRow(1,"Pipe","","",sample.Value,"м",Array.Empty<string>());Assert(row.QuantityText==sample.Text,"Meter quantities round upward to tenths for display");Near(row.Quantity,sample.Value);
}
Assert(BomCategories.Classify(first with{Properties=new Dictionary<string,string>{{"PnPClassName","FlowMeter"},{"ContentIsoSymbolDefinition","TYPE=INSTRUMENT"}}})==BomCategory.Valves,"Flowmeters belong to pipeline valves even with an instrument ISO type");
Assert(BomCategories.Classify(first with{Properties=new Dictionary<string,string>{{"PnPClassName","ControlValve"},{"ContentIsoSymbolDefinition","TYPE=INSTRUMENT"}}})==BomCategory.Valves,"Instrumented control valves remain pipeline valves");
foreach(string instrument in new[]{"PressureGauge","Sensor","Thermometer"})Assert(BomCategories.Classify(first with{Properties=new Dictionary<string,string>{{"PnPClassName",instrument}}})==BomCategory.Instruments,"Sensors, gauges and thermometers remain instruments");
var diameterParts=new[]{100,25,10}.Select((dn,i)=>Pipe("diameter-"+dn,new(0,i*1000,0),new(1302,i*1000,0)) with{Properties=new Dictionary<string,string>{{"PnPClassName","Pipe"},{"NominalDiameter",dn.ToString()},{"Size",dn.ToString()},{"PartSizeLongDesc","DN"+dn}}}).ToArray();
var diameterScene=SheetPlanner.Build(Snapshot(diameterParts,Array.Empty<Connection>()));
Assert(diameterScene.BillOfMaterials.Select(r=>r.Description).SequenceEqual(new[]{"DN10","DN25","DN100"}),"Pipe BOM sorts numeric diameter, not string or handle order");
Assert(diameterScene.BillOfMaterials.All(r=>r.QuantityText=="1.4"&&Math.Abs(r.Quantity-1.302)<1e-8),"Displayed meter rounding does not change aggregated model lengths");
var minimalPipe=Pipe("readable-short",new(0,0,0),new(1,0,0));var minimalLayout=new SchematicLayout(Snapshot(new[]{minimalPipe},Array.Empty<Connection>()),compactLevel:3);
var minimalStart=minimalLayout.Project(minimalPipe.Ports[0].Position,minimalPipe.Handle);var minimalEnd=minimalLayout.Project(minimalPipe.Ports[1].Position,minimalPipe.Handle);
double minimalPaperLength=Math.Sqrt(Math.Pow(minimalEnd.X-minimalStart.X,2)+Math.Pow(minimalEnd.Y-minimalStart.Y,2));
Assert(minimalPaperLength>=4.1-1e-8&&minimalPaperLength<10,"A short pipe uses only required text space, not a hard 10 mm minimum");
foreach(var label in diameterScene.Annotations.Where(a=>a.LeaderKnee is not null&&a.Anchor is not null))Assert(Math.Sqrt(Math.Pow(label.LeaderKnee!.Value.X-label.Anchor!.Value.X,2)+Math.Pow(label.LeaderKnee.Value.Y-label.Anchor.Value.Y,2))<=30.000001,"Leader reach remains within 30 mm");
for(int turn=0;turn<4;turn++)
{
 var planeScene=SheetPlanner.Build(linked,quarterTurns:turn);
 foreach(var dimension in planeScene.Dimensions)
 {
  Near(dimension.ExtensionDirection!.Value.X,0);Near(dimension.ExtensionDirection.Value.Y,1);
  double dx=dimension.End.X-dimension.Start.X,dy=dimension.End.Y-dimension.Start.Y;
  var point=dimension.DimensionLinePoint!.Value;
  double tA=(point.X-dimension.Start.X)*(-dy)+(point.Y-dimension.Start.Y)*dx;
  double tB=(point.X-dimension.End.X)*(-dy)+(point.Y-dimension.End.Y)*dx;
  var a=new SheetPoint(dimension.Start.X,dimension.Start.Y+tA/dx);var b=new SheetPoint(dimension.End.X,dimension.End.Y+tB/dx);
  Near((b.X-a.X)*dy-(b.Y-a.Y)*dx,0);
 }
}
Assert(BomCategories.Classify(first with{Properties=new Dictionary<string,string>{{"PnPClassName","Elbow"},{"ContentIsoSymbolDefinition","TYPE=BEND,SKEY=BEBW"}}})==BomCategory.Fittings,"A purchased elbow with ISO TYPE=BEND remains a fitting, not a pipe");
Assert(BomCategories.Classify(first with{Properties=new Dictionary<string,string>{{"PnPClassName","PipeBend"},{"ContentIsoSymbolDefinition","TYPE=BEND"}}})==BomCategory.Pipes,"A true bent pipe remains in the pipe category");
Near((fixtureCatalog with{SymbolScale=7.5,Style=new IsoStyleSettings(99)}).ScaleFor(first),7.5);
var leaderPlacement=typeof(SheetSetPlanner).Assembly.GetType("IsoGenerator.Core.AnnotationPlacement")!.GetMethod("Place")!;
var enclosure=new List<Stroke>{new("enclosure","component",new SheetPoint[]{new(89,91),new(111,91),new(111,109),new(89,109),new(89,91)})};
var enclosureGlyph=new SymbolGlyph("enclosure","","","TestBox",new(100,100),new(20,0),new(0,32));
var crossingLeader=(IReadOnlyList<Annotation>)leaderPlacement.Invoke(null,new object[]{new[]{new Annotation("leader","valve-tag",new(100,100),"ID",Anchor:new(100,100))},enclosure,new[]{enclosureGlyph},Array.Empty<PipeDimension>()})!;
Assert(crossingLeader.Count==1&&crossingLeader[0].LeaderKnee is not null,"A leader may cross the enclosing component outline to reach readable text");
var knee=crossingLeader[0].LeaderKnee!.Value;
bool LeaderCross(SheetPoint a,SheetPoint b,SheetPoint c,SheetPoint d)
{
 double ux=b.X-a.X,uy=b.Y-a.Y,vx=d.X-c.X,vy=d.Y-c.Y,den=ux*vy-uy*vx;if(Math.Abs(den)<1e-8)return false;
 double t=((c.X-a.X)*vy-(c.Y-a.Y)*vx)/den,u=((c.X-a.X)*uy-(c.Y-a.Y)*ux)/den;
 return t>1e-6&&t<1-1e-6&&u>1e-6&&u<1-1e-6;
}
var wallSegments=enclosure[0].Points.Zip(enclosure[0].Points.Skip(1),(a,b)=>(a,b)).ToArray();
var leaderSegments=enclosure.Skip(1).SelectMany(stroke=>stroke.Points.Zip(stroke.Points.Skip(1),(a,b)=>(a,b))).ToArray();
Assert(leaderSegments.Any(l=>wallSegments.Any(w=>LeaderCross(l.a,l.b,w.a,w.b)))&&Math.Sqrt(Math.Pow(knee.X-100,2)+Math.Pow(knee.Y-100,2))<=30.000001,"A leader or its landing may cross the component outline within the 30 mm reach");
Near(BomCategories.TextHeight,3);
Assert(BomCategories.Classify(first with{Properties=new Dictionary<string,string>{{"PnPClassName","InlineInstrument"},{"ContentIsoSymbolDefinition","TYPE=INSTRUMENT,SKEY=IIFL"},{"PartFamilyLongDesc","Расходомер электромагнитный"}}})==BomCategory.Valves,"A generic instrument class with flowmeter catalog data belongs to valves");
Assert(BomCategories.Classify(first with{Properties=new Dictionary<string,string>{{"PnPClassName","Sensor"},{"ContentIsoSymbolDefinition","TYPE=INSTRUMENT"},{"PartFamilyLongDesc","Датчик расходомера"}}})==BomCategory.Instruments,"An actual sensor remains an instrument, even when fitted to a flowmeter");
Console.WriteLine($"Passed {checks} checks.");


}
catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}

sealed class CallbackProgress<T>(Action<T> callback):IProgress<T>{public void Report(T value)=>callback(value);}
