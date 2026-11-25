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
foreach(var sample in new[]{(Value:1.01,Text:"1,01"),(Value:1.2,Text:"1,20"),(Value:.001,Text:"0,00"),
    (Value:2.0,Text:"2,00"),(Value:1.234567,Text:"1,23"),(Value:1.235,Text:"1,24"),(Value:.005,Text:"0,01")})
{
    var row=new BomRow(1,"Pipe","","",sample.Value,"м",Array.Empty<string>());
    Assert(row.QuantityText==sample.Text,$"Expected {sample.Text} in the length cell, got {row.QuantityText}");
    Assert(row.Quantity==sample.Value,"Formatting must retain the original model length");
}
Part Pipe(string h,ModelPoint a,ModelPoint b)=>new("test.dwg",h,"AcPpDb3dPipe","L1",new[]{new Port("S1",a,new(-1,0,0)),new Port("S2",b,new(1,0,0))},new Dictionary<string,string>{{"PnPClassName","Pipe"},{"PartSizeLongDesc","Pipe 100"},{"Length","1000"}});
var first=Pipe("A",new(0,0,0),new(1000,0,0));var second=Pipe("B",new(1000,0,0),new(2000,0,0));
PipelineSnapshot Snapshot(Part[] parts,Connection[] links)=>new(1,"test.dwg",parts,links,Array.Empty<string>());
var coldDrawnPipe=first with{Properties=new Dictionary<string,string>(first.Properties)
    {["PartSizeLongDesc"]="Труба 32x3 ГОСТ 8734-75/В 09Г2С ГОСТ 8733-74"}};
var coldDrawnRow=SheetPlanner.Build(Snapshot(new[]{coldDrawnPipe},Array.Empty<Connection>())).BillOfMaterials.Single();
Assert(coldDrawnRow.Designation=="ГОСТ 8734-75"&&coldDrawnRow.Description=="Труба 32x3 В 09Г2С ГОСТ 8733-74",
    "Straight pipe BOM extracts the product standard and retains the material standard when DesignStd is empty");
var taggedPipe= coldDrawnPipe with{Properties=new Dictionary<string,string>(coldDrawnPipe.Properties){["Tag"]="P-01",["DesignStd"]="ГОСТ 8734-75"}};
var taggedPipeRow=SheetPlanner.Build(Snapshot(new[]{taggedPipe},Array.Empty<Connection>())).BillOfMaterials.Single();
Assert(taggedPipeRow.Designation=="P-01 / ГОСТ 8734-75"&&taggedPipeRow.Description==coldDrawnRow.Description,
    "Straight pipe BOM places the product standard beside an existing identifier exactly once");
foreach(int sign in new[]{-1,1})
{
    var neck=Pipe("vent-drain",new(0,0,0),new(0,0,100*sign));
    var valve=Pipe("terminal-valve",new(0,0,100*sign),new(0,0,150*sign)) with
        {Properties=new Dictionary<string,string>{{"PnPClassName","Valve"}}};
    var terminalJoint=new Connection(neck.Handle,"S2",valve.Handle,"S1");
    var terminal=Snapshot(new[]{neck,valve},new[]{terminalJoint});
    double PaperSpan(SchematicLayout layout)=>Math.Abs(layout.Project(neck.Ports[0].Position,neck.Handle).Y-
        layout.Project(neck.Ports[1].Position,neck.Handle).Y);
    Near(PaperSpan(new SchematicLayout(terminal)),20);
    Near(PaperSpan(new SchematicLayout(terminal,compactLevel:1,
        pipeExtensions:new Dictionary<string,double>{{neck.Handle,120}},fixedScale:.5)),20);
    var throughPipe=Pipe("downstream",new(0,0,150*sign),new(0,0,300*sign));
    var through=Snapshot(new[]{neck,valve,throughPipe},new[]{terminalJoint,new Connection(valve.Handle,"S2",throughPipe.Handle,"S1")});
    Assert(PaperSpan(new SchematicLayout(through,fixedScale:.5))>20,
        "A vertical through-run with an inline valve retains its normal paper scale");
}
var sideNeck=Pipe("side-neck",new(20,0,0),new(120,0,0));
var sideValve=Pipe("side-valve",new(120,0,0),new(170,0,0)) with
    {Properties=new Dictionary<string,string>{{"PnPClassName","Valve"}}};
var sideTee=new Part("test.dwg","side-tee","AcPpDb3dPipeInlineAsset","L1",new[]{
    new Port("S1",new(0,0,-30),new(0,0,-1)),new Port("S2",new(0,0,30),new(0,0,1)),
    new Port("S3",new(20,0,0),new(1,0,0))},new Dictionary<string,string>{{"PnPClassName","Tee"}});
var sideWeld=sideNeck with{Handle="side-weld",Kind="AcPpDb3dConnector",
    Ports=new[]{new Port("S1",new(20,0,0),new(-1,0,0)),new Port("S2",new(20,0,0),new(1,0,0))},
    Properties=new Dictionary<string,string>()};
var sideFixture=Snapshot(new[]{sideTee,sideWeld,sideNeck,sideValve},new[]{
    new Connection(sideTee.Handle,"S3",sideWeld.Handle,"S1"),new Connection(sideWeld.Handle,"S2",sideNeck.Handle,"S1"),
    new Connection(sideNeck.Handle,"S2",sideValve.Handle,"S1")});
double SidePaperSpan(PipelineSnapshot snapshot)
{
    var layout=new SchematicLayout(snapshot,fixedScale:.5);
    var neck=snapshot.Parts.Single(part=>part.Handle==sideNeck.Handle);
    var start=layout.Project(neck.Ports[0].Position,neck.Handle);
    var end=layout.Project(neck.Ports[1].Position,neck.Handle);
    return Math.Sqrt(Math.Pow(end.X-start.X,2)+Math.Pow(end.Y-start.Y,2));
}
Near(SidePaperSpan(sideFixture),20);
Port ShiftSidePort(Port port)=>port with{Position=port.Position with{X=port.Position.X+158}};
var longerSide=sideFixture with{Parts=new[]{sideTee,sideWeld,
    sideNeck with{Ports=new[]{sideNeck.Ports[0],ShiftSidePort(sideNeck.Ports[1])}},
    sideValve with{Ports=sideValve.Ports.Select(ShiftSidePort).ToArray()}}};
Near(SidePaperSpan(longerSide),20);
var tappedHost=Pipe("tapped-host",new(0,0,-150),new(0,0,150));
tappedHost=tappedHost with{Ports=tappedHost.Ports.Append(
    new Port("D1",new(20,0,0),new(1,0,0),"TAP")).ToArray()};
var tapFixture=sideFixture with{Parts=new[]{tappedHost,sideWeld,sideNeck,sideValve},
    Connections=sideFixture.Connections.Select(link=>link.FirstHandle==sideTee.Handle?
        link with{FirstHandle=tappedHost.Handle,FirstPort="D1"}:link).ToArray()};
Near(SidePaperSpan(tapFixture),20);
var takeoffReducer=Pipe("takeoff-reducer",new(20,0,0),new(60,0,0)) with
    {Properties=new Dictionary<string,string>{{"PnPClassName","Reducer"}}};
var reducedSideNeck=sideNeck with{Ports=new[]{sideNeck.Ports[0] with{Position=new(60,0,0)},sideNeck.Ports[1]}};
var reducedTap=Snapshot(new[]{tappedHost,takeoffReducer,reducedSideNeck,sideValve},new[]{
    new Connection(tappedHost.Handle,"D1",takeoffReducer.Handle,"S1"),
    new Connection(takeoffReducer.Handle,"S2",sideNeck.Handle,"S1"),
    new Connection(sideNeck.Handle,"S2",sideValve.Handle,"S1")});
Near(SidePaperSpan(reducedTap),20);
var straightHost=Pipe("side-host",new(-100,0,0),new(20,0,0));
var inlineFixture=sideFixture with{Parts=new[]{straightHost,sideWeld,sideNeck,sideValve},
    Connections=sideFixture.Connections.Select(link=>link.FirstHandle==sideTee.Handle?
        link with{FirstHandle=straightHost.Handle,FirstPort="S2"}:link).ToArray()};
Assert(SidePaperSpan(inlineFixture)>20,"A short inline valve connection is not a side tap");
var reducedInline=reducedTap with{Parts=new[]{straightHost,takeoffReducer,reducedSideNeck,sideValve},
    Connections=reducedTap.Connections.Select(link=>link.FirstHandle==tappedHost.Handle?
        link with{FirstHandle=straightHost.Handle,FirstPort="S2"}:link).ToArray()};
Assert(SidePaperSpan(reducedInline)>20,"An inline reducer does not make a terminal valve connection a side tap");
var overlapFirst=new Stroke("vertical-a","pipe",new SheetPoint[]{new(10,5),new(10,25)});
var overlapSecond=new Stroke("vertical-b","pipe",new SheetPoint[]{new(10,13),new(10,30)});
bool projectedOverlapRejected=false;
try{PipeCrossingBreaks.CheckProjectedOverlaps(new[]{overlapFirst,overlapSecond});}
catch(PipeProjectionOverlapException ex){projectedOverlapRejected=ex.SourceId=="vertical-a"&&ex.OtherPipe=="vertical-b";}
Assert(projectedOverlapRejected,"Distinct pipes with overlapping projected centerlines are rejected by their source handles");
PipeCrossingBreaks.CheckProjectedOverlaps(new[]{overlapFirst,
    overlapSecond with{Points=new SheetPoint[]{new(10,25),new(10,30)}},
    new Stroke("crossing","pipe",new SheetPoint[]{new(5,15),new(15,15)})});
var skewBend=new Part("test.dwg","skew-bend","AcPpDb3dPipeInlineAsset","L1",new[]{new Port("S1",new(0,0,0),new(-1,0,0)),new Port("S2",new(100,100,3),new(0,1,0))},
    new Dictionary<string,string>{["PnPClassName"]="PipeBend",["InsulationThickness"]="40"});
var skewBendSnapshot=Snapshot(new[]{skewBend},Array.Empty<Connection>());
void RejectSkew(Action plan,string consumer)
{
    try{plan();}catch(InvalidDataException ex) when(ex.Message.Contains(skewBend.Handle)){Assert(true,consumer);return;}
    Assert(false,consumer+" must reject nonintersecting elbow axes and identify the part");
}
RejectSkew(()=>PipelineTopology.Build(skewBendSnapshot),"Topology");
RejectSkew(()=>SheetPlanner.Build(skewBendSnapshot),"Sheet");
RejectSkew(()=>InsulationPlanner.Add(skewBendSnapshot,p=>new(p.X,p.Y),Array.Empty<SymbolGlyph>(),new List<Stroke>()),"Insulation");
// Two distinct horizontal runs overlap in view 0 but separate in view 1.
var viewFixture=Snapshot(new[]{Pipe("view-a",new(0,0,0),new(1000,0,0)),Pipe("view-b",new(100,100,-100),new(1100,100,-100))},Array.Empty<Connection>());
Assert(AutomaticView.Select(viewFixture)==1,"Automatic view avoids superimposed runs");
Assert(AutomaticView.Select(viewFixture with{Parts=viewFixture.Parts.Reverse().ToArray()})==1,"View is independent of part enumeration");
Assert(AutomaticView.Select(Snapshot(new[]{first},Array.Empty<Connection>()))==0,"Symmetric line retains stable default view");
using(var cancelledView=new CancellationTokenSource())
{
    cancelledView.Cancel();bool stopped=false;
    try{AutomaticView.Select(viewFixture,cancelledView.Token);}catch(OperationCanceledException){stopped=true;}
    Assert(stopped,"Automatic view supports cancellation");
}
// A valve-containing loop between two junctions is an indivisible preferred unit.
var unitParts=new[]{first with{Handle="uA",Properties=new Dictionary<string,string>{{"PnPClassName","Tee"}}},first with{Handle="uB",Properties=new Dictionary<string,string>{{"PnPClassName","Tee"}}},first with{Handle="uV",Properties=new Dictionary<string,string>{{"PnPClassName","Valve"}}},first with{Handle="uP"},first with{Handle="uIn"},first with{Handle="uOut"}};
Connection UL(string a,string b)=>new(a,"S1",b,"S2");
var unitSnapshot=Snapshot(unitParts,new[]{UL("uA","uV"),UL("uV","uB"),UL("uB","uP"),UL("uP","uA"),UL("uIn","uA"),UL("uB","uOut")});
var unitFound=InstallationUnits.Find(unitSnapshot);
Assert(unitFound.Count==1&&unitFound[0].Handles.SetEquals(new[]{"uA","uB","uV","uP"}),"Recognise bypass cycle without absorbing adjoining runs");
Assert(InstallationUnits.SplitCount(unitFound,new HashSet<string>{"uIn"},new HashSet<string>{"uA","uB","uV","uP","uOut"})==0,"External cut preserves bypass");
Assert(InstallationUnits.SplitCount(unitFound,new HashSet<string>{"uIn","uA","uV"},new HashSet<string>{"uB","uP","uOut"})==1,"Paired cuts through a bypass incur unit penalty");
Assert(InstallationUnits.Find(unitSnapshot with{Connections=unitSnapshot.Connections.Skip(1).ToArray()}).Count==0,"Open branch is not a bypass");
Assert(InstallationUnits.Find(unitSnapshot with{Parts=unitParts.Select(p=>p.Handle=="uV"?p with{Properties=new Dictionary<string,string>{{"PnPClassName","Pipe"}}}:p).ToArray()}).Count==0,"Plain pipe ring is not a valve bypass");
Assert(InstallationUnits.Find(unitSnapshot with{Connections=unitSnapshot.Connections.Concat(unitSnapshot.Connections.Select(c=>new Connection(c.SecondHandle,c.SecondPort,c.FirstHandle,c.FirstPort))).ToArray()}).Count==1,"Reverse duplicate connections do not invent cycles");
var separated=Snapshot(new[]{first,second},Array.Empty<Connection>());
Assert(SnapshotValidation.Validate(separated).ConnectedComponents==2,"Coincident ports must not invent a connection");
var linked=Snapshot(new[]{first,second},new[]{new Connection("A","S2","B","S1")});
Assert(SnapshotValidation.Validate(linked).ConnectedComponents==1,"Real topology connects components");
// Preserve a complete published partition while a valve is added inside one sheet.
var rememberedSource=linked with{RequiredCuts=linked.Connections};
RememberedLayout savedLayout=null;
var savedSheets=SheetSetPlanner.Build(rememberedSource,remember:layout=>savedLayout=layout);
Assert(savedSheets.Count==2&&savedLayout is not null,"Remembered layout retains two manually separated sheets");
var persisted=System.Text.Json.JsonSerializer.Deserialize<RememberedLayout>(
    System.Text.Json.JsonSerializer.Serialize(savedLayout))!;
var identical=SheetSetPlanner.Build(rememberedSource,remembered:persisted);
Assert(ReferenceEquals(identical,persisted.Sheets),"Unchanged model reuses the complete persisted layout");
var damaged=persisted with{Sheets=persisted.Sheets.Select((sheet,index)=>index==0
    ?sheet with{Snapshot=sheet.Snapshot with{Parts=Array.Empty<Part>()}}:sheet).ToArray()};
var repaired=SheetSetPlanner.Build(rememberedSource,remembered:damaged);
Assert(repaired.Count==savedSheets.Count&&repaired.Sum(sheet=>sheet.Snapshot.Parts.Count)==linked.Parts.Count,
    "A damaged remembered partition falls back to a full plan");
var addedValve=Pipe("new-valve",new(-100,0,0),new(0,0,0)) with
{
    Properties=new Dictionary<string,string>{{"PnPClassName","Valve"},{"Tag","GV-N"}, {"PartSizeLongDesc","Valve"}}
};
var changedLine=rememberedSource with
{
    Parts=rememberedSource.Parts.Append(addedValve).ToArray(),
    Connections=rememberedSource.Connections.Append(new Connection("new-valve","S2","A","S1")).ToArray()
};
var updatedSheets=SheetSetPlanner.Build(changedLine,remembered:persisted);
var updated=updatedSheets.Single(sheet=>sheet.Snapshot.Parts.Any(part=>part.Handle=="new-valve"));
Assert(updated.Snapshot.Parts.Any(part=>part.Handle=="A")&&updated.Scene.Annotations.Any(label=>label.Text=="GV-N"),
    "A new valve is assigned to the changed sheet and appears in that scene");
var untouched=updatedSheets.Single(sheet=>sheet.Snapshot.Parts.Any(part=>part.Handle=="B"));
Assert(System.Text.Json.JsonSerializer.Serialize(untouched.Scene)==
       System.Text.Json.JsonSerializer.Serialize(persisted.Sheets.Single(sheet=>sheet.Number==untouched.Number).Scene),
       "A valve added away from the transition leaves its neighbor scene byte-identical");
var alternateView=SheetSetPlanner.Build(rememberedSource,quarterTurns:1,remembered:persisted);
Assert(alternateView.All(sheet=>sheet.Scene.QuarterTurns==1),"A remembered partition is invalidated when the view changes");
var extraRows=Enumerable.Range(0,60).Select(i=>
{
    var accessory=Accessory("A#row-"+i,"A","Miscellaneous","1");
    return accessory with{Properties=new Dictionary<string,string>(accessory.Properties){["PartSizeLongDesc"]="Extra entry "+i}};
}).ToArray();
RememberedLayout expandedLayout=null;
var withOverflow=changedLine with{Accessories=extraRows};
var expandedSheets=SheetSetPlanner.Build(withOverflow,remembered:persisted,remember:layout=>expandedLayout=layout);
var lastDiagram=expandedSheets.Last();
Assert(expandedSheets.Count>2&&expandedSheets.Skip(1).SkipLast(1).All(s=>s.Scene.BomOnly)&&
       expandedSheets[0].Scene.Annotations.Any(a=>a.Role=="continuation"&&a.Text=="П1 / лист "+lastDiagram.Number),
       "Adding BOM pages keeps them after their geometry and updates the continuation");
Assert(System.Text.Json.JsonSerializer.Serialize(lastDiagram.Scene.Strokes)==
       System.Text.Json.JsonSerializer.Serialize(untouched.Scene.Strokes),"BOM renumbering preserves adjacent geometry");
var collapsedSheets=SheetSetPlanner.Build(changedLine,remembered:expandedLayout);
Assert(collapsedSheets.Count==2&&collapsedSheets[0].Scene.Annotations.Any(a=>a.Role=="continuation"&&a.Text=="П1 / лист 2"),
       "Removing BOM overflow restores the original continuation numbering");
var removedCut=SheetSetPlanner.Build(linked,remembered:persisted);
Assert(removedCut.Count==1,"Changing the manual boundary triggers a new partition");
var third=Pipe("C",new(2000,0,0),new(3000,0,0));
var routeLinks=linked.Connections.Append(new Connection("B","S2","C","S1")).ToArray();
var reverseOrder=Snapshot(new[]{third,first,second},routeLinks) with
{
    RequiredCuts=routeLinks,
    IsoMarkers=new[]{new ModelIsoMarker("start-C","C","Y1",new(3000,0,0),5)}
};
var routeSheets=SheetSetPlanner.Build(reverseOrder);
Assert(routeSheets.Select(sheet=>sheet.Snapshot.Parts.Single().Handle).SequenceEqual(new[]{"C","B","A"})&&
       routeSheets[0].Scene.Annotations.Any(label=>label.Role=="continuation"&&label.Text.EndsWith("лист 2")),
       "Sheet numbering follows connected sections from the start marker, independent of capture order");
var numberedDocument=new Dictionary<string,(int Offset,IReadOnlyList<PlannedSheet> Sheets)>(StringComparer.OrdinalIgnoreCase)
{
    ["150-KSN011-LS025C-HN"]=(2,routeSheets)
};
var documentScene=DocumentSheetReferences.Apply(routeSheets[0],"150-KSN011-LS025C-HN",2,numberedDocument);
Assert(documentScene.Annotations.Any(label=>label.Role=="continuation"&&
       label.Text.EndsWith("см. 150-KSN011-LS025C-HN(L2) лист 4")),
       "A continuation names the target drawing and uses the global page number");
var crossScene=new SheetScene("other",Array.Empty<Stroke>(),new[]{
    new Annotation("owner","external-line",new(100,100),"см. 150-KSN011-LS025C-HN\nОТМ. +0.000*",Anchor:new SheetPoint(100,100)),
    new Annotation("owner","external-line",new(200,200),"см. OUTSIDE",Anchor:new SheetPoint(200,200))},
    Array.Empty<PipeDimension>(),Array.Empty<BomRow>(),Array.Empty<string>(),0);
var crossSnapshot=linked with{ExternalLineContexts=new[]{
    new ExternalLineContext("owner","S2","150-KSN011-LS025C-HN","A","S1",new[]{first},Array.Empty<Connection>())}};
var crossPlan=new PlannedSheet(1,1,crossSnapshot,crossScene);
var crossNumbered=DocumentSheetReferences.Apply(crossPlan,"other",5,numberedDocument);
Assert(crossNumbered.Annotations.Any(label=>label.Text=="см. 150-KSN011-LS025C-HN(L3) лист 5\nОТМ. +0.000*")&&
       crossNumbered.Annotations.Any(label=>label.Text=="см. OUTSIDE"),
       "An external line reference finds the connected part on a later sheet and leaves outside documents intact");
var placedReturnAngle=new Annotation("return-bend","bend-angle",new(130,60),"180°",Anchor:new SheetPoint(100,60));
var bendDocumentScene=crossScene with
{
    Annotations=crossScene.Annotations.Append(placedReturnAngle).ToArray(),
    Strokes=new[]{2d,3,4.5,6,8,12,16,22}.Select(lift=>new Stroke("neighbor","pipe",
        new[]{new SheetPoint(80,61+lift),new SheetPoint(125,61+lift)})).ToArray()
};
var reflowedBend=DocumentSheetReferences.Apply(crossPlan with{Scene=bendDocumentScene},"other",5,numberedDocument);
Assert(reflowedBend.Annotations.Single(label=>label.Role=="bend-angle").Position==placedReturnAngle.Position&&
       reflowedBend.Annotations.Any(label=>label.Text.Contains("лист 5",StringComparison.Ordinal)),
       "Document reference reflow can reuse a clear return-bend angle position outside the above-anchor candidates");
var crowdedBendAngle=placedReturnAngle with{Position=new(102,63),Text="90°"};
var placedCrowdedBend=AnnotationPlacement.Place(new[]{crowdedBendAngle},bendDocumentScene.Strokes.ToList(),
    Array.Empty<SymbolGlyph>(),Array.Empty<PipeDimension>()).Single();
Assert(placedCrowdedBend.Anchor==crowdedBendAngle.Anchor&&placedCrowdedBend.Position.Y<60,
    "Bend angle can move below an occluded arc on initial placement while preserving its anchor");
var gap=second with{Ports=new[]{new Port("S1",new(1005,0,0),new(-1,0,0)),second.Ports[1]}};
Assert(SnapshotValidation.Validate(linked with{Parts=new[]{first,gap}}).Errors.Count>0,"Reject inconsistent connected ports");
Assert(SnapshotValidation.Validate(linked with{Parts=new[]{first,first}}).Errors.Count>0,"Reject duplicate handles");
var scene=SheetPlanner.Build(linked);
var flowMarker=new ModelIsoMarker("flow-test",first.Handle,"Y1",new(500,0,0),1,new(1,0,0));
var splitFlow=BreakPointPreparation.Apply(linked with{IsoMarkers=new[]{flowMarker with{Position=new ModelPoint(750,0,0)},new ModelIsoMarker("break-flow",first.Handle,"Y2",new(500,0,0),4)}},false);
Assert(splitFlow.IsoMarkers!.Single().HostHandle==first.Handle+"#ISO2","Flow marker follows its actual fragment when the host pipe is split");
var flowScene=SheetPlanner.Build(linked with{IsoMarkers=new[]{flowMarker}});
var missingDirection=SheetSetPlanner.Build(linked with{IsoMarkers=new[]{flowMarker with{Direction=null}}});
Assert(missingDirection.Count==1&&missingDirection[0].Scene.Warnings.Any(w=>w.Contains(flowMarker.Handle))&&
       missingDirection[0].Scene.Strokes.All(stroke=>stroke.Role!="flow-arrow"),
       "An incomplete flow marker leaves the sheet available and reports the skipped symbol");
var forwardArrow=flowScene.Strokes.Single(st=>st.Role=="flow-arrow");
var arrowTip=forwardArrow.Points[1];
var arrowBaseLeft=forwardArrow.Points[0];
var arrowBaseRight=forwardArrow.Points[2];
double arrowX=arrowTip.X-(arrowBaseLeft.X+arrowBaseRight.X)/2;
double arrowY=arrowTip.Y-(arrowBaseLeft.Y+arrowBaseRight.Y)/2;
Near(Math.Sqrt(arrowX*arrowX+arrowY*arrowY),2);
var backArrow=SheetPlanner.Build(linked with{IsoMarkers=new[]{flowMarker with{Direction=new ModelPoint(-1,0,0)}}}).Strokes.Single(st=>st.Role=="flow-arrow");
Assert(forwardArrow.Points[0].X<forwardArrow.Points[1].X&&backArrow.Points[0].X>backArrow.Points[1].X,"Flow arrow follows model direction and reverses when the marker reverses");
Assert(BreakPointPreparation.Apply(linked with{IsoMarkers=new[]{flowMarker}},true).IsoMarkers!.Count==1,"Ignoring break points does not discard flow markers");
var startTest=linked with{RequiredCuts=linked.Connections} with{IsoMarkers=new[]{new ModelIsoMarker("start-test",second.Handle,"Y1",new(1500,0,0),5)}};
var startSheets=SheetSetPlanner.Build(startTest);
Assert(startSheets[0].Snapshot.Parts.Any(p=>p.Handle==second.Handle),"Start marker puts its element on the first diagram sheet");
var detachedInstrument=new Part("test.dwg","kip-detached","AcPpDb3dPipeInlineAsset","L1",new[]{new Port("S1",new(2000,0,0),new(-1,0,0),"BV")},new Dictionary<string,string>{{"PnPClassName","Instrument"},{"Tag","PI-001"}});
var kipSource=linked with{Parts=linked.Parts.Append(detachedInstrument).ToArray()};
var kipPrepared=SheetConnectivity.AttachInstrumentCallouts(kipSource);
Assert(kipSource.Connections.Count==1&&!detachedInstrument.Properties.ContainsKey("IsoCalloutHost"),"Callout attachment does not mutate source snapshot");
Assert(kipPrepared.Parts.Single(p=>p.Handle==detachedInstrument.Handle).Properties["IsoCalloutHost"]==second.Handle,"Detached instrument attaches to exact free opposing port");
Assert(kipPrepared.Warnings.Any(w=>w.Contains("Нет соединения:")),"Missing Plant connection remains visible as a warning");
var kipSheets=SheetSetPlanner.Build(kipSource);
Assert(kipSheets.Count==1&&kipSheets[0].Scene.Annotations.Count(a=>a.Role=="instrument-tag")==1,"Detached instrument callout stays with pipeline instead of creating a blank sheet");
Assert(kipSheets[0].Scene.Welds!.Count==scene.Welds!.Count,"Layout-only callout does not invent a weld even with weldable end condition");
var ambiguousKip=kipSource with{Parts=kipSource.Parts.Append(second with{Handle="duplicate-host"}).ToArray()};
bool ambiguousKipRejected=false;try{SheetConnectivity.AttachInstrumentCallouts(ambiguousKip);}catch(InvalidDataException){ambiguousKipRejected=true;}
Assert(ambiguousKipRejected,"Coincident ambiguous hosts are never guessed for a KIP callout");
var remoteKip=kipSource with{Parts=linked.Parts.Append(detachedInstrument with{Ports=new[]{new Port("S1",new(2001,0,0),new(-1,0,0))}}).ToArray()};
bool remoteKipRejected=false;try{SheetConnectivity.AttachInstrumentCallouts(remoteKip);}catch(InvalidDataException){remoteKipRejected=true;}
Assert(remoteKipRejected,"Nearby noncoincident KIP cannot attach to arbitrary nearest pipe");
Assert(SheetSetPlanner.Build(kipSource,instrumentCalloutsOnly:false).Count==2,"Physical-instrument mode retains genuine disconnected components");
var manualLinked=linked with{RequiredCuts=linked.Connections.Select(c=>new Connection(c.SecondHandle,c.SecondPort,c.FirstHandle,c.FirstPort)).ToArray()};
var manualSheets=SheetSetPlanner.Build(manualLinked);
Assert(manualSheets.All(sh=>sh.Scene.Strokes.Count(st=>st.Role=="continuation-break")==1),"Each sheet boundary gets a zigzag break sign");
Assert(scene.Strokes.All(st=>st.Role!="continuation-break"),"Real unconnected endpoints do not receive sheet break signs");Assert(manualSheets.Count==2,"Explicit reversed cut survives splitting and optional merging");
bool rejectedManualMerge=false;
try{SheetSetIntegrity.Validate(manualLinked,new[]{new PlannedSheet(1,1,linked,scene)},false);}catch(InvalidDataException){rejectedManualMerge=true;}
Assert(rejectedManualMerge,"Final validation rejects a sheet set that ignores manual cut");
var orphanMarker=new ModelIsoMarker("manual-free",first.Handle,"marker",first.Ports[0].Position,BreakPointPreparation.BreakPointInformationType);
bool rejectedFreeMarker=false;
try{BreakPointPreparation.Apply(linked with{IsoMarkers=new[]{orphanMarker}},false);}catch(InvalidDataException){rejectedFreeMarker=true;}
Assert(rejectedFreeMarker,"Unusable endpoint marker cannot be silently ignored");
var interiorMarker=new ModelIsoMarker("manual-mid",first.Handle,"marker",new(500,0,0),BreakPointPreparation.BreakPointInformationType);
var preservedCuts=BreakPointPreparation.Apply(manualLinked with{IsoMarkers=new[]{interiorMarker}},false);
var recoveredBreak=linked with{IsoMarkers=new[]{interiorMarker with{HostHandle="",HostPort=""}}};
Assert(SheetSetPlanner.Build(recoveredBreak).Count==2,"An orphan break on one pipe axis remains mandatory");
bool ambiguousBreak=false;
try
{
    BreakPointPreparation.Apply(recoveredBreak with{Parts=linked.Parts.Append(first with{Handle="other-pipe"}).ToArray()},false);
}
catch(InvalidDataException error){ambiguousBreak=error.Message.Contains(interiorMarker.Handle);}
Assert(ambiguousBreak,"An ambiguous orphan break reports its handle instead of choosing an arbitrary pipe");
Assert(preservedCuts.RequiredCuts!.Count==2,"Native marker preserves existing explicit cut");
Assert(BreakPointPreparation.Apply(manualLinked with{IsoMarkers=new[]{interiorMarker}},true).RequiredCuts!.Count==0,"Only explicit ignore option disables break points");
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
    Assert(sceneWithSymbols.Strokes.All(s=>s.Role is "pipe" or "joint" or "tap-connection" or "flange-neck" or "insulation" or "elevation-leader" or "elevation-shelf" or "bend-dimension"||s.Role.StartsWith("slope-",StringComparison.Ordinal)),"Library symbols replace placeholder fitting strokes");
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
        foreach(var d in chain)Near(double.Parse(d.Label.TrimEnd('*'),System.Globalization.CultureInfo.InvariantCulture),Math.Ceiling(d.Millimeters-1e-8));
}



if(File.Exists(file)){
    var live=System.Text.Json.JsonSerializer.Deserialize<PipelineSnapshot>(File.ReadAllText(file))!;
    var layout=new SchematicLayout(live);
    Assert(layout.EnlargedEdges>0,"Short segments must be enlarged on paper");
    var symbolMap=new IsoSymbolMap(File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!,"../assets/IsoSkeyAcadBlockMap.xml")),fixtureCatalog);
    var readable=SheetPlanner.Build(live,symbolMap:symbolMap);
    Assert(readable.Symbols!.Count(g=>g.Variant.StartsWith("end-"))==8,"Each of four FL valves gets two end plates");
    foreach(var valve in live.Parts.Where(p=>p.Component==ComponentKind.Valve))
    {
        var op=readable.Symbols.SingleOrDefault(g=>g.SourceId==valve.Handle&&g.Variant=="operator");
        if(valve.OperatorDirection is not {} direction)
            Assert(op is null,"Missing model direction must not produce a fabricated operator");
        else if(op is not null)
        {
            var operatorProjection=IsometricProjection.Project(direction);
            Near(op.YAxis.X*operatorProjection.Y-op.YAxis.Y*operatorProjection.X,0);
            Assert(op.YAxis.X*operatorProjection.X+op.YAxis.Y*operatorProjection.Y>0,"Operator follows the model direction including its sign");
        }
    }
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
RememberedLayout compactTall=null;
var defaultTallSheets=SheetSetPlanner.Build(tallSnapshot,remember:layout=>compactTall=layout);
var tallSheets=SheetSetPlanner.Build(tallSnapshot,remembered:compactTall,minimumPipeLength:20);
Assert(tallSheets.Count is >=3 and <=4,$"40 intervals of 20 mm fit in three or four A2 sheets (actual {tallSheets.Count})");
Assert(defaultTallSheets.Count<tallSheets.Count,"A higher manual pipe minimum invalidates the remembered layout and uses more paper");
Assert(tallSheets.SelectMany(sheet=>sheet.Scene.Strokes).Where(stroke=>stroke.Role=="pipe").All(stroke=>
    Math.Sqrt(Math.Pow(stroke.Points[1].X-stroke.Points[0].X,2)+Math.Pow(stroke.Points[1].Y-stroke.Points[0].Y,2))>=20-1e-6),
    "The selected minimum is applied to the paper geometry");
Assert(tallSheets.SelectMany(s=>s.Snapshot.Parts).Select(p=>p.Handle).Distinct().Count()==40&&tallSheets.Sum(s=>s.Snapshot.Parts.Count)==40,"Every source component belongs to exactly one sheet");
Assert(tallSheets.Sum(s=>s.Scene.Welds!.Count)==39,"Split boundary welds are retained exactly once");
Assert(tallSheets.Sum(s=>s.Scene.Annotations.Count(a=>a.Text.Contains("/ лист")))==2*(tallSheets.Count-1),"Each sheet boundary has two reciprocal continuation references");
Near(tallSheets.Sum(s=>s.Scene.BillOfMaterials.Sum(b=>b.Quantity)),4);
Near(tallSheets.Sum(s=>s.Scene.Dimensions.Where(d=>d.Role=="chain").Sum(d=>d.Millimeters)),4000);
Assert(SheetSetPlanner.Build(linked).Count==1,"Small route stays on a single sheet");
var valveParts=tallParts.Select((p,i)=>i==20?p with{Properties=new Dictionary<string,string>(p.Properties){{"ContentIsoSymbolDefinition","TYPE=VALVE,SKEY=VTFL"}}}:p).ToArray();
valveParts[20]=valveParts[20] with{Properties=new Dictionary<string,string>(valveParts[20].Properties){["PnPClassName"]="Valve"}};
var valveSheets=SheetSetPlanner.Build(Snapshot(valveParts,tallLinks),minimumPipeLength:20);
Assert(valveSheets.Count is >=3 and <=4,"Route with central valve fits in three or four readable sheets");
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
Assert(!riseScene.Strokes.Any(s=>s.Role is "slope-angle-arc" or "slope-angle-arrow"),
    "An incline delegates the angle arc and arrowheads to the native dimension");
Assert(riseScene.Annotations.Single(a=>a.Role=="slope-angle").AngularDimension is not null,
    "A roomy hatch triangle carries a native angular dimension");
foreach(int side in new[]{1,-1})
{
    var vertex=new SheetPoint(100,140);
    var shallowHatch=new List<Stroke>{new("shallow-angle","slope-triangle",
        new SheetPoint[]{vertex,new(300,140),new(300,140+side*20)})};
    var angle=AnnotationPlacement.Place(new[]{new Annotation("shallow-angle","slope-angle",new(105,140),"3°",Anchor:vertex)},
        shallowHatch,Array.Empty<SymbolGlyph>(),Array.Empty<PipeDimension>()).Single();
    Assert(angle.AngularDimension is {} native&&native.ArcPoint.X>200&&native.ArcPoint.X<300&&
        (native.ArcPoint.Y-140)*side>0,"A shallow angle moves along the hatch until the arrows and degree text fit");
}
Assert(riseScene.Strokes.Count(s=>s.Role=="slope-right-angle")==1,"Incline triangle has a right angle mark");
var nearAxis=Pipe("near-axis",new(0,0,0),new(1000,0.5,0));
var slightlySkewed=Pipe("slightly-skewed",new(0,50,0),new(1000,53.5,0));
var smallDeviationStrokes=new List<Stroke>();
var smallDeviationLabels=new List<Annotation>();
SlopeNotation.Add(Snapshot(new[]{nearAxis,slightlySkewed},Array.Empty<Connection>()),
    p=>IsometricProjection.Project(p),0,smallDeviationStrokes,smallDeviationLabels,Array.Empty<PipeDimension>());
Assert(!smallDeviationLabels.Any(a=>a.SourceId==nearAxis.Handle&&a.Role=="slope-angle")&&
    !smallDeviationStrokes.Any(s=>s.SourceId==nearAxis.Handle&&s.Role=="slope-triangle")&&
    smallDeviationLabels.Any(a=>a.SourceId==slightlySkewed.Handle&&a.Role=="slope-angle"&&a.Text=="0.2°"),
    "Deviations invisible at the displayed precision omit 0° without dropping meaningful nearby angles");
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
// Reported KSN010 route: the native marker must produce two readable sheets at scale 10.
var ksnSnapshot=System.Text.Json.JsonSerializer.Deserialize<PipelineSnapshot>(File.ReadAllText(Path.Combine(testRoot,"tests/fixtures/ksn010-break.json")))!;
var ksnMap=new IsoSymbolMap(File.ReadAllText(Path.Combine(testRoot,"data/IsoSkeyAcadBlockMap.xml")),fixtureCatalog with{SymbolScale=10,Style=null});
var missingPlantProperties=ksnSnapshot with{Parts=ksnSnapshot.Parts.Select(p=>p.Handle=="F98"?p with{Properties=new Dictionary<string,string>()}:p).ToArray()};
bool rejectedMissingPlant=false;
try{SheetSetPlanner.Build(missingPlantProperties,ksnMap);}catch(InvalidDataException ex){rejectedMissingPlant=ex.Message.Contains("F98")&&ex.Message.Contains("Plant 3D");}
Assert(rejectedMissingPlant,"Missing Plant data is rejected before sheet splitting with an actionable diagnostic");
var ksnSheets=SheetSetPlanner.Build(ksnSnapshot,ksnMap,excludeFlangeHardware:true);
Assert(ksnSheets.Count(s=>!s.Scene.BomOnly)>=2,"KSN010 native break requires at least two diagram sheets");
Assert(ksnSheets[0].Scene.LayoutLevel!=3,"KSN010 uses available paper space instead of minimum geometry");
Assert(ksnSheets.SelectMany(sh=>sh.Scene.Symbols!).Where(g=>g.SourceId is "83E0" or "83EB").All(g=>g.YAxis.Y<0),"Elbow heel base plates extend below their attachment");
Assert(ksnSheets.All(s=>DimensionTextPlacement.CollisionCount(s.Scene.Dimensions,s.Scene.Strokes)==0),"KSN010 dimension text stays clear; line crossings are handled by native DIMBREAK");
Assert(ksnSheets.All(s=>!s.Scene.BillOfMaterials.Any(r=>r.Category==BomCategory.Instruments)),"KSN010 default output excludes instrument BOM positions");
var ksnInstrumentIds=ksnSnapshot.Parts.Where(p=>BomCategories.Classify(p)==BomCategory.Instruments).Select(p=>p.Handle).ToHashSet();
Assert(ksnSheets.SelectMany(s=>s.Scene.Symbols!).All(g=>!ksnInstrumentIds.Contains(g.SourceId)),"KSN010 default output excludes instrument bodies");
Assert(ksnSheets.SelectMany(s=>s.Scene.Annotations).Count(a=>a.Role=="instrument-tag")==ksnInstrumentIds.Count,"KSN010 retains one leader per instrument");
foreach(var sh in ksnSheets)
{
 var bounds=SheetComposition.Bounds(sh.Scene);
 Assert(bounds.Left>=SheetComposition.Paper.Left-1e-6&&bounds.Right<=SheetComposition.Paper.Right+1e-6&&bounds.Bottom>=SheetComposition.Paper.Bottom-1e-6&&bounds.Top<=SheetComposition.Paper.Top+1e-6,"KSN010 complete ink footprint fits A2");
}
var externalTapWelds=ksnSheets.SelectMany(sh=>sh.Scene.Welds!).Where(w=>w.JointType=="Tap"&&ksnSnapshot.ExternalLineContexts!.Any(c=>c.OwnerHandle==w.SourceId)).ToArray();
Assert(externalTapWelds.Length==2,"Both foreign-line tap connections get a weld on the receiving line");
Assert(externalTapWelds.All(w=>Math.Abs(w.ModelPosition!.Value.Z-820)<.001),"Foreign tap welds sit on the receiving pipe axis, not the surface");var ksnPrepared=BreakPointPreparation.Apply(ksnSnapshot,false);
var ksnOwner=ksnSheets.SelectMany((sh,i)=>sh.Snapshot.Parts.Select(p=>(p.Handle,Sheet:i))).ToDictionary(p=>p.Handle,p=>p.Sheet);
Assert(ksnPrepared.RequiredCuts!.All(c=>ksnOwner[c.FirstHandle]!=ksnOwner[c.SecondHandle]),"KSN010 native break remains a sheet boundary");

var externalIds=(ksnSnapshot.ExternalLineContexts??Array.Empty<ExternalLineContext>()).SelectMany(c=>c.Parts).Select(p=>p.Handle).ToHashSet();
Assert(ksnSheets.SelectMany(sh=>sh.Scene.Annotations).Count(a=>a.Role=="external-line")==2,"KSN010 references both connected HCD003 branches");
Assert(ksnSheets.SelectMany(sh=>sh.Scene.Annotations).Where(a=>a.Role=="external-line").All(a=>a.Text.StartsWith("см. 050-HCD003-LS016C-HN\nОТМ. ")),"Foreign line references use the actual ID and elevation without an invented sheet number");
Assert(!ksnSheets.SelectMany(sh=>sh.Scene.BillOfMaterials).SelectMany(row=>row.Handles).Any(externalIds.Contains),"Foreign continuation elements never enter KSN010 BOM quantities");
Assert(!ksnSheets.SelectMany(sh=>sh.Scene.Dimensions).Any(d=>externalIds.Contains(d.SourceId)),"Foreign continuation elements never acquire KSN010 dimensions");
Assert(ksnSheets.SelectMany(sh=>sh.Scene.Annotations).Where(a=>a.Role=="external-line").All(a=>a.Text.EndsWith("ОТМ. +0.820*")),"External tapping elevations reference the receiving pipe axis, not the lower surface");
var shownExternal=ksnSheets.SelectMany(sh=>sh.Scene.Strokes).Where(st=>st.Role.StartsWith("context-external-")).Select(st=>st.SourceId).Concat(ksnSheets.SelectMany(sh=>sh.Scene.Symbols!).Where(g=>g.Context&&externalIds.Contains(g.SourceId)).Select(g=>g.SourceId)).Distinct().ToArray();
Assert(shownExternal.Length==4,"Two foreign physical elements are shown at each of the two boundaries");
Assert(!ksnSheets.SelectMany(sh=>sh.Scene.Annotations).Any(a=>a.Role=="slope-value"),"No slope value is invented for the level pipes in KSN010");
var gentleSlope=first with{Handle="gentle-slope",Ports=new[]{new Port("S1",new(0,0,2),new(-1,0,0)),new Port("S2",new(1000,0,0),new(1,0,0))}};
var gentleScene=SheetPlanner.Build(Snapshot(new[]{gentleSlope},Array.Empty<Connection>()));
Assert(gentleScene.Annotations.Single(a=>a.Role=="slope-value").Text=="0,002","A two-mm fall over one metre prints a decimal slope");
Assert(!gentleScene.Annotations.Any(a=>a.Role=="slope-angle"),"A gentle fall uses a slope indicator instead of an oversized angle triangle");
var slopeArrow=gentleScene.Strokes.Single(st=>st.Role=="slope-indicator").Points;
var downhill=IsometricProjection.Project(new(1000,0,-2));
Assert((slopeArrow[1].X-slopeArrow[0].X)*downhill.X+(slopeArrow[1].Y-slopeArrow[0].Y)*downhill.Y>0,"Slope arrow points downhill irrespective of port ordering");


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
var hiddenValveTags=SheetSetPlanner.Build(taggedSource,risingMap,
    advancedSettings:new IsoAdvancedSettings{ShowValveIdentifierLeaders=false});
Assert(hiddenValveTags.SelectMany(sheet=>sheet.Scene.Annotations).All(label=>label.Role!="valve-tag")&&
    hiddenValveTags.SelectMany(sheet=>sheet.Scene.BillOfMaterials).Any(row=>row.Identifier=="BV-001"),
    "Disabling valve identifier leaders leaves the identifier in the BOM");
Assert(taggedSheets[0].Scene.Strokes.Any(s=>s.Role=="insulation"&&s.SourceId=="17A85"),"Elbows receive an insulation contour too");
foreach(int view in Enumerable.Range(0,4))
{
    var insulationLayout=new SchematicLayout(taggedSource,quarterTurns:view,symbolMap:risingMap);
    var insulationStrokes=new List<Stroke>();
    // Keep the real library glyphs: their local port coordinates need not be +/-0.5.
    var insulationGraph=PipelineTopology.Build(taggedSource);
    var glyphs=taggedSource.Parts.Where(p=>p.Properties.GetValueOrDefault("PnPClassName")=="Elbow").Select(part=>
    {
        var ends=part.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();
        var corner=insulationGraph.Nodes.First(n=>n.Source==part.Handle&&n.Kind==RouteNodeKind.Axis).Point;
        var block=risingMap.Resolve(IsoDefinition.Parse(part.Properties["ContentIsoSymbolDefinition"]).Skey)!;
        return IsoSymbolPlacement.Corner(part,block,fixtureCatalog,insulationLayout.Project(ends[0].Position,part.Handle),insulationLayout.Project(corner,part.Handle),insulationLayout.Project(ends[1].Position,part.Handle));
    }).ToArray();
    InsulationPlanner.Add(taggedSource,p=>insulationLayout.Project(p),glyphs,insulationStrokes,(p,h)=>insulationLayout.Project(p,h));
    foreach(var bendPart in taggedSource.Parts.Where(p=>p.Properties.GetValueOrDefault("PnPClassName")=="Elbow"))
    {
        var ends=bendPart.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach(var contour in insulationStrokes.Where(st=>st.SourceId==bendPart.Handle))
        foreach(int endpoint in new[]{0,1})
        {
            var actual=endpoint==0?contour.Points[0]:contour.Points[^1];
            var port=insulationLayout.Project(ends[endpoint].Position,bendPart.Handle);
            Near(Math.Sqrt(Math.Pow(actual.X-port.X,2)+Math.Pow(actual.Y-port.Y,2)),3);
        }
    }
}

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

// Instruments keep their source topology while their native symbol/BOM position can be hidden.
var instrumentPart=supportPart with{Handle="instrument",Kind="AcPpDb3dEquipment",Properties=new Dictionary<string,string>{{"PnPClassName","PressureGauge"},{"ContentIsoSymbolDefinition","TYPE=INSTRUMENT-DIAL,SKEY=IDFL"},{"Tag","PG-004"},{"PartSizeLongDesc","Pressure gauge"}}};
var instrumentSnapshot=Snapshot(new[]{supportPipe,instrumentPart},new[]{new Connection("A","D1","instrument","S1")});
var instrumentMap=new IsoSymbolMap("<Map><SkeyMap SKEY='IDFL' AcadBlock='ProjectSupport'/></Map>",new IsoBlockCatalog(new[]{supportDefinition}));
var instrumentHidden=SheetPlanner.Build(instrumentSnapshot,symbolMap:instrumentMap,instrumentCalloutsOnly:true);
Assert(!instrumentHidden.Symbols!.Any(g=>g.SourceId=="instrument"),"Callout-only instrument has no glyph");
Assert(!instrumentHidden.BillOfMaterials.Any(r=>r.Handles.Contains("instrument")),"Callout-only instrument has no BOM item");
var instrumentLabel=instrumentHidden.Annotations.Single(a=>a.SourceId=="instrument"&&a.Role=="instrument-tag");
Assert(instrumentLabel.Text=="PG-004"&&instrumentLabel.LeaderKnee is not null,"Instrument identifier uses a real leader");
Near(instrumentLabel.Anchor!.Value.X,supportAxis.X);Near(instrumentLabel.Anchor.Value.Y,supportAxis.Y);
Assert(instrumentHidden.SourcePartCount==instrumentSnapshot.Parts.Count,"Hidden instrument remains traceable in source inventory");
var instrumentVisible=SheetPlanner.Build(instrumentSnapshot,symbolMap:instrumentMap,instrumentCalloutsOnly:false);
Assert(instrumentVisible.Symbols!.Any(g=>g.SourceId=="instrument"),"Disabling callout-only mode restores the instrument glyph");
Assert(instrumentVisible.BillOfMaterials.Any(r=>r.Handles.Contains("instrument")),"Disabling callout-only mode restores the BOM item");
var unnamedInstrument=instrumentPart with{Properties=new Dictionary<string,string>(instrumentPart.Properties){["Tag"]="?-?-?"}};
var unnamedScene=SheetPlanner.Build(instrumentSnapshot with{Parts=new[]{supportPipe,unnamedInstrument}},symbolMap:instrumentMap,instrumentCalloutsOnly:true);
Assert(unnamedScene.Annotations.Any(a=>a.Role=="instrument-tag"&&a.Text=="КИП: ИД не задан"),"Missing instrument tags are explicit instead of invented");
Assert(unnamedScene.Warnings.Any(w=>w.Contains("не задан Tag")),"Missing instrument identifier produces a warning");
// A projected pipe crossing must never erase the symbol of its attached support.
var gapLower=Pipe("gap-lower",new(0,0,0),new(1000,0,0));
var gapUpper=Pipe("gap-upper",new(500,-500,1000),new(500,500,1000));
var crossingStrokes=new[]{new Stroke("gap-lower","pipe",new[]{new SheetPoint(0,0),new SheetPoint(100,0)}),new Stroke("gap-upper","pipe",new[]{new SheetPoint(50,-50),new SheetPoint(50,50)})};
Assert(PipeCrossingBreaks.Apply(crossingStrokes,new[]{gapLower,gapUpper}).Count(st=>st.SourceId=="gap-lower")==2,"An unobstructed lower crossing gets a normal pipe gap");
var gapSupport=supportPart with{Handle="gap-support",Ports=new[]{new Port("S1",new(500,0,0),new(0,0,1))}};
var gapSupportGlyph=new SymbolGlyph("gap-support","","TYPE=SUPPORT,SKEY=SUPD","ProjectSupport",new(50,0),new(4,0),new(0,4),LocalBounds:new[]{new SheetPoint(-.5,-.5),new SheetPoint(.5,-.5),new SheetPoint(.5,.5),new SheetPoint(-.5,.5)});
bool supportGapRejected=false;
try{PipeCrossingBreaks.Apply(crossingStrokes,new[]{gapLower,gapUpper,gapSupport},symbols:new[]{gapSupportGlyph});}
catch(SheetCapacityException ex){supportGapRejected=ex.SourceId=="gap-upper";}
Assert(supportGapRejected,"A crossing on a support requests spacing repair on the crossing pipe instead of cutting the support");

// A real support port on an elbow is part of that elbow's schematic component.
var elbowSupportPoint=new ModelPoint(-29.2893,29.2893,0);
var supportElbow=new Part("test.dwg","support-elbow","Elbow","L1",new[]{new Port("S1",new(-100,0,0),new(1,0,0)),new Port("S2",new(0,100,0),new(0,-1,0)),new Port("D1",elbowSupportPoint,new(0,0,-1))},new Dictionary<string,string>{{"PnPClassName","Elbow"}});
var elbowSupport=supportPart with{Ports=new[]{new Port("S1",elbowSupportPoint,new(0,0,1))}};
var elbowSupportSnapshot=Snapshot(new[]{supportElbow,elbowSupport},new[]{new Connection("support-elbow","D1","support","S1")});
var elbowSupportLayout=new SchematicLayout(elbowSupportSnapshot);
var elbowAnchor=elbowSupportLayout.Project(new(0,0,0),"support-elbow");
var supportAnchor=elbowSupportLayout.Project(elbowSupportPoint,"support");
Near(supportAnchor.X,elbowAnchor.X);Near(supportAnchor.Y,elbowAnchor.Y);
var elbowStart=elbowSupportLayout.Project(new(-100,0,0),"support-elbow");
var projectedAxis=IsometricProjection.Project(new(1,0,0));
Near((elbowAnchor.X-elbowStart.X)*projectedAxis.Y-(elbowAnchor.Y-elbowStart.Y)*projectedAxis.X,0);

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
Assert(weldFlangeScene.Dimensions.Any(d=>Math.Abs(d.Millimeters-1050)<.001),"A flange-only interval is included in the installation dimension chain");
var flangeDimensionsHiddenBom=SheetPlanner.Build(weldFlangeSnapshot,symbolMap:weldFlangeMap,excludeFlangeHardware:true);
Assert(flangeDimensionsHiddenBom.Dimensions.Any(d=>Math.Abs(d.Millimeters-1050)<.001),"Hiding a flange from BOM preserves its installation length");
var flangeBetweenPipes=Snapshot(new[]{Pipe("flange-upstream",new(-1000,0,0),new(0,0,0)),weldFlange,weldPipe},new[]{new Connection("flange-upstream","S2","weld-flange","S1"),new Connection("weld-flange","S2","weld-pipe","S1")});
var completeFlangeChain=DimensionChainPlanner.Build(flangeBetweenPipes,Plain);
Near(completeFlangeChain.Where(d=>d.Role=="chain").Sum(d=>d.Millimeters),2050);
Assert(completeFlangeChain.Any(d=>d.Role=="chain"&&Math.Abs(d.Millimeters-1050)<.001),"Flange length is merged into an adjacent labelled interval, never omitted between two pipe dimensions");
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
Assert(hardwareScene.BillOfMaterials.Single(r=>r.Category==BomCategory.Fasteners).Unit=="компл.",
    "Catalog fastener kits retain their unit");
var fastener=Accessory("b3","A","BoltSet","1");
var fastenerProperties=new Dictionary<string,string>(fastener.Properties){{"NumberInSet","8"}};
Near(BomQuantities.Amount(fastener with{Properties=fastenerProperties}),8);
fastenerProperties["NumberInSet"]="7.5";
bool invalidFastenersRejected=false;
try{BomQuantities.Amount(fastener with{Properties=fastenerProperties});}
catch(InvalidDataException){invalidFastenersRejected=true;}
Assert(invalidFastenersRejected,"A fractional NumberInSet cannot silently become a kit quantity");
foreach(var badScene in new[]
{
    hardwareScene with{BillOfMaterials=hardwareScene.BillOfMaterials.Where(row=>row.Unit!="м").ToArray()},
    hardwareScene with{BillOfMaterials=hardwareScene.BillOfMaterials.Select(row=>row.Unit=="м"?row with{Quantity=row.Quantity+.001}:row).ToArray()}
})
{
    bool rejected=false;
    try{SheetSetIntegrity.Validate(hardwareSnapshot,new[]{new PlannedSheet(1,1,hardwareSnapshot,badScene)},false);}
    catch(InvalidDataException){rejected=true;}
    Assert(rejected,"Publication rejects missing pipe positions and a 1 mm discrepancy in a BOM row");
}
Assert(hardwareScene.Dimensions.Select(d=>d.Millimeters).SequenceEqual(scene.Dimensions.Select(d=>d.Millimeters)),"Gaskets and fasteners in BOM do not create extra dimension chains");
Assert(!hardwareScene.Strokes.Any(s=>s.SourceId is "g1" or "g2" or "b1" or "b2"),"Connector accessories do not create schematic geometry");
var splitHardware=SheetSetPlanner.Build(tallSnapshot with{Accessories=new[]{Accessory("left-gasket","T00","Gasket","1"),Accessory("right-gasket","T39","Gasket","1")}});
Near(splitHardware.Sum(s=>s.Scene.BillOfMaterials.Where(r=>r.Category==BomCategory.Gaskets).Sum(r=>r.Quantity)),2);
Assert(splitHardware.SelectMany(s=>s.Snapshot.Accessories??Array.Empty<Part>()).Select(p=>p.Handle).Distinct().Count()==2,"Accessories belong to their owner's sheet exactly once");
var filteredHardware=SheetPlanner.Build(hardwareSnapshot,excludeFlangeHardware:true);
Assert(filteredHardware.BillOfMaterials.SelectMany(r=>r.Handles).ToHashSet().SetEquals(hardwareScene.BillOfMaterials.SelectMany(r=>r.Handles)),"Valve hardware exclusion preserves accessories without a valve connection");
Assert(filteredHardware.Dimensions.Select(d=>d.Millimeters).SequenceEqual(hardwareScene.Dimensions.Select(d=>d.Millimeters)),"Hardware BOM exclusion preserves engineering dimensions");
var filteredFlange=SheetPlanner.Build(weldFlangeSnapshot,symbolMap:weldFlangeMap,excludeFlangeHardware:true);
Assert(filteredFlange.BillOfMaterials.Any(r=>r.Handles.Contains("weld-flange")),"A flange attached only to a pipe remains in the BOM");
Assert(filteredFlange.Symbols!.Count==weldFlangeScene.Symbols!.Count&&filteredFlange.Welds!.Count==weldFlangeScene.Welds!.Count,"Flange BOM exclusion preserves flange symbols and welds");
Assert(filteredFlange.Annotations.Any(a=>a.SourceId=="weld-flange"&&a.Role=="position"),"A standalone flange keeps its BOM position");
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
var witnessSize=new PipeDimension("near",new(0,0),new(50,0),50,Role:"overall",ChainId:"witness-test",
    DimensionLinePoint:new(0,55),ExtensionDirection:new(0,1),TextPosition:new(25,57.8));
var witnessBase=new[]{new Stroke("neighbor","pipe",new[]{new SheetPoint(-15,20),new SheetPoint(65,20)})};
Assert(DimensionRouting.LongWitnessThroughParallelRun(new[]{witnessSize},witnessBase) is not null,
    "A long witness line through a different parallel pipe is rejected");
var clearWitness=DimensionRouting.Route(new[]{witnessSize},witnessBase,Array.Empty<SymbolGlyph>(),0);
Assert(DimensionRouting.LongWitnessThroughParallelRun(clearWitness,witnessBase) is null,
    "Dimension routing can move the chain away from the other pipe");
var routed=DimensionRouting.Route(intersectingDimensions,Array.Empty<Stroke>(),Array.Empty<SymbolGlyph>(),0);
Assert(DimensionRouting.CrossingCount(routed)==0,"Whole-chain routing removes proper baseline and extension crossings");
Assert(routed.Select(d=>d.Millimeters).SequenceEqual(intersectingDimensions.Select(d=>d.Millimeters)),"Dimension routing never changes engineering values");
var documentNames=DrawingFileNames.DocumentOutputSet("out",
    new[]{("200-KSN010-LS016C-HN",2),("150-KSN011-LS025C-HN",1)},DrawingFileNames.DocumentStem("873.РД-003-ТХМ1.ИЧ.pdf"));
Assert(documentNames.Drawings.Select(Path.GetFileName).SequenceEqual(new[]{
    "200-KSN010-LS016C-HN(L1).dwg","200-KSN010-LS016C-HN(L2).dwg","150-KSN011-LS025C-HN(L1).dwg"})&&
    Path.GetFileName(documentNames.Pdf)=="873.РД-003-ТХМ1.ИЧ.pdf","A batch uses complete line numbers for DWGs and the document number for its single PDF");
var indexedWordFiles=DrawingFileNames.ReportDocuments(Path.Combine("out","873.РД-003-ТХМ1.ИЧ (1).pdf"));
Assert(indexedWordFiles.Select(Path.GetFileName).SequenceEqual(new[]{
    "873.РД-003-ТХМ1.ИЧ (1) — Титульный лист.docx","873.РД-003-ТХМ1.ИЧ (1) — Содержание.docx"}),
    "Word documents share the PDF stem and the publication index");
var fittedRow=new PipeDimension("pipe",new(0,0),new(60,0),750,Role:"chain",ChainId:"fitting-gap",
    DimensionLinePoint:new(0,10),ExtensionDirection:new(0,1),TextPosition:new(30,12.8));
var nearFlange=new SymbolGlyph("flange","","TYPE=FLANGE","",new(26,10.4),new(1,0),new(0,1),
    LocalContours:new[]{(IReadOnlyList<SheetPoint>)new SheetPoint[]{new(0,0),new(8,0)}});
Assert(!DimensionRouting.FitsFittingClearance(new[]{fittedRow},new[]{nearFlange}),
    "A parallel symbol contour is an obstacle even when the dimension line does not cross it");
var clearFitting=DimensionRouting.Route(new[]{fittedRow},Array.Empty<Stroke>(),new[]{nearFlange},0);
Assert(DimensionRouting.FitsFittingClearance(clearFitting,new[]{nearFlange})&&
    clearFitting[0].Start==fittedRow.Start&&clearFitting[0].End==fittedRow.End&&clearFitting[0].Millimeters==750,
    "Moving the row to clear a nearby flange preserves both anchors and the dimension value");
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
Assert(SheetPlanner.Build(fgbSnapshot,symbolMap:weldFlangeMap,excludeFlangeHardware:true).Annotations.Any(a=>a.Role=="position-group"),"A standalone flange keeps its grouped hardware position");
Part KitPart(string handle,string kind,int start,int end,string firstEnd="FL",string secondEnd="FL",string definition="")=>
    new("test.dwg",handle,kind==""?"AcPpDb3dConnector":"AcPpDb3dInlineAsset","L1",
        new[]{new Port("S1",new(start,0,0),new(-1,0,0),firstEnd),new Port("S2",new(end,0,0),new(1,0,0),secondEnd)},
        new Dictionary<string,string>{{"PnPClassName",kind},{"ContentIsoSymbolDefinition",definition}});
var kitParts=new[]{
    Pipe("kit-inlet",new(-1000,0,0),new(0,0,0)),
    KitPart("kit-flange-a","Flange",0,80,"BV"),
    KitPart("kit-joint-a","",80,85),
    KitPart("kit-valve","Valve",85,385),
    KitPart("kit-joint-b","",385,390),
    KitPart("kit-blind","BlindDisk",390,420,definition:"TYPE=MISC-COMPONENT,SKEY=SB"),
    KitPart("kit-joint-c","",420,425),
    KitPart("kit-flange-b","Flange",425,505,secondEnd:"BV"),
    Pipe("kit-spacer",new(505,0,0),new(1505,0,0)),
    KitPart("pair-flange-a","Flange",1505,1585,"BV"),
    KitPart("pair-joint","",1585,1590),
    KitPart("pair-flange-b","Flange",1590,1670,secondEnd:"BV"),
    Pipe("kit-outlet",new(1670,0,0),new(2670,0,0))};
var kitLinks=kitParts.Zip(kitParts.Skip(1),(a,b)=>new Connection(a.Handle,"S2",b.Handle,"S1")).ToArray();
var kitAccessories=new[]{"kit-joint-a","kit-joint-b","kit-joint-c","pair-joint"}
    .SelectMany(joint=>new[]{Accessory(joint+"-gasket",joint,"Gasket","1"),Accessory(joint+"-bolts",joint,"BoltSet","8")}).ToArray();
var kitSnapshot=Snapshot(kitParts,kitLinks) with{Accessories=kitAccessories};
var kitExcluded=new[]{"kit-flange-a","kit-flange-b","kit-blind"}.Concat(kitAccessories
    .Where(p=>p.Handle.StartsWith("kit-",StringComparison.Ordinal)).Select(p=>p.Handle)).ToHashSet();
var completeKit=SheetPlanner.Build(kitSnapshot);
var compactKit=SheetPlanner.Build(kitSnapshot,excludeFlangeHardware:true);
Assert(completeKit.BillOfMaterials.SelectMany(row=>row.Handles).Where(kitExcluded.Contains).ToHashSet().SetEquals(kitExcluded),
    "Disabling valve kit exclusion counts every flange, blind, gasket and fastener");
Assert(compactKit.BillOfMaterials.SelectMany(row=>row.Handles).ToHashSet().SetEquals(
    completeKit.BillOfMaterials.SelectMany(row=>row.Handles).Where(handle=>!kitExcluded.Contains(handle))),
    "Valve kit exclusion follows the valve through the blind and keeps both flanges and hardware of the independent pipe joint");
Assert(compactKit.Dimensions.Select(d=>d.Millimeters).SequenceEqual(completeKit.Dimensions.Select(d=>d.Millimeters)),
    "Valve kit BOM exclusion preserves engineering dimensions");
foreach(string endCondition in new[]{"WF","LUG"})
{
    var specialValve=kitParts[3] with{Ports=kitParts[3].Ports.Select(port=>port with{EndCondition=endCondition}).ToArray()};
    var specialKit=kitSnapshot with{Parts=kitParts.Select(part=>part.Handle==specialValve.Handle?specialValve:part).ToArray()};
    Assert(SheetPlanner.Build(specialKit,excludeFlangeHardware:true).BillOfMaterials.SelectMany(row=>row.Handles).ToHashSet().SetEquals(
        compactKit.BillOfMaterials.SelectMany(row=>row.Handles)),"Wafer and lugged valves exclude their mating flanged hardware");
}
var splitKit=SheetSetPlanner.Build(kitSnapshot with{RequiredCuts=new[]{kitLinks[5]}},excludeFlangeHardware:true);
Assert(splitKit.Count>=2&&splitKit.SelectMany(sheet=>sheet.Scene.BillOfMaterials).SelectMany(row=>row.Handles).ToHashSet().SetEquals(
    compactKit.BillOfMaterials.SelectMany(row=>row.Handles)),
    "Splitting through a valve kit does not restore excluded items or remove an independent flange pair");
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
var forcedLoop=valveLoop with{RequiredCuts=new[]{valveLoop.Connections[0]}};
var forcedLoopSheets=SheetSetPlanner.Build(forcedLoop);
var forcedLoopOwners=forcedLoopSheets.SelectMany((sheet,index)=>sheet.Snapshot.Parts.Select(p=>(p.Handle,Index:index))).ToDictionary(p=>p.Handle,p=>p.Index);
Assert(forcedLoopOwners[forcedLoop.RequiredCuts[0].FirstHandle]!=forcedLoopOwners[forcedLoop.RequiredCuts[0].SecondHandle],"A manual cut on a cycle forces another boundary and stays mandatory");
var coupledLoops=LoopFixture(new ModelPoint[]{new(0,0,0),new(1000,0,0),new(1000,1000,0),new(0,1000,0),new(2000,0,0),new(2000,1000,0)},new[]{(0,1),(1,2),(2,3),(3,0),(1,4),(4,5),(5,2)});
Assert(new SchematicLayout(coupledLoops).ClosedLoops==2,"Coupled cycles with a shared pipe are solved together");
var boundaryTall=tallSnapshot with{Connections=tallSnapshot.Connections.Append(new Connection("T00","S1","outside-line","S2")).ToArray()};
Assert(SheetSetPlanner.Build(boundaryTall).Count==defaultTallSheets.Count,"A boundary object outside the selected line does not break adjacent-sheet context");
var continuityParts=new[]{
    Pipe("run-left",new(0,0,0),new(100,0,0)),Pipe("run-middle",new(100,0,0),new(200,0,0)),Pipe("run-right",new(200,0,0),new(300,0,0)),
    Pipe("parallel",new(0,50,0),new(300,50,0))};
var continuity=new SheetRunContinuity(Snapshot(continuityParts,new[]{new Connection("run-left","S2","run-middle","S1"),new Connection("run-middle","S2","run-right","S1")}));
var detachedRun=new SheetRunContinuity(Snapshot(continuityParts,Array.Empty<Connection>()));
Assert(!detachedRun.HasInteriorGap(new[]{"run-left","run-right"}),"Disconnected collinear pipes do not form a run");
var familyPipes=continuityParts.Take(2).Select(p=>p with{Properties=new Dictionary<string,string>(p.Properties){["PartSizeLongDesc"]="",["PartFamilyLongDesc"]=p.Handle}}).ToArray();
Assert(SheetPlanner.Build(Snapshot(familyPipes,new[]{new Connection("run-left","S2","run-middle","S1")})).BillOfMaterials.Count==2,"Different displayed family descriptions get distinct BOM positions");
var catalogBend=new Part("test.dwg","catalog-bend","AcPpDb3dPipeBend","L1",
    new[]{new Port("S1",new(0,0,0),new(-1,0,0)),new Port("S2",new(0,80,0),new(-1,0,0))},
    new Dictionary<string,string>{["PnPClassName"]="PipeBend",["PartSizeLongDesc"]=
        "ИЗГИБ ТРУБЫ, Р = 40, У = 180 град, Д1 = 100, Д2 = 100, ДЛИНА ТРУБЫ=325.6637, Труба 25x4 ГОСТ 8734-75/В 20 ГОСТ 8733-74"});
var catalogBendRow=SheetPlanner.Build(Snapshot(new[]{catalogBend},Array.Empty<Connection>())).BillOfMaterials.Single();
Assert(catalogBendRow.Description=="ИЗГИБ ТРУБЫ, Р=40, α=180°, L=325.6637, Труба 25x4 В 20 ГОСТ 8733-74",
    "Pipe bend BOM shows compact catalog dimensions and preserves the material standard");
Assert(catalogBendRow.Designation=="ГОСТ 8734-75","The pipe standard populates the bend BOM designation");
var taggedCatalogBend=catalogBend with{Properties=new Dictionary<string,string>(catalogBend.Properties)
    { ["Tag"]="PB-01",["DesignStd"]="ГОСТ 8733-74" }};
var taggedCatalogBendRow=SheetPlanner.Build(Snapshot(new[]{taggedCatalogBend},Array.Empty<Connection>())).BillOfMaterials.Single();
Assert(taggedCatalogBendRow.Designation=="PB-01 / ГОСТ 8734-75",
    "The pipe standard remains in the bend designation alongside its tag");
Assert(taggedCatalogBendRow.Description==catalogBendRow.Description,
    "The material standard remains in the bend description when present in DesignStd");
Assert(continuity.HasInteriorGap(new[]{"run-left","run-right","parallel"}),"A connected bypass must not hide an omitted middle of a straight run");
Assert(!continuity.HasInteriorGap(new[]{"run-left","run-middle"}),"A sheet boundary at the end of a straight run is allowed");
Assert(!continuity.HasInteriorGap(new[]{"run-left","run-middle","run-right"}),"The complete run has no interior gap");
var tapHost=Pipe("tap-host",new(0,0,1000),new(1000,0,1000));
tapHost=tapHost with{Ports=tapHost.Ports.Append(new Port("D1",new(500,0,900),new(0,0,-1),"TAP")).ToArray()};
var tapBranch=Pipe("tap-branch",new(500,0,0),new(500,0,900));
var wholeTap=Snapshot(new[]{tapHost,tapBranch},new[]{new Connection("tap-host","D1","tap-branch","S2")});
var tapFragment=TapDimensionReferences.Apply(wholeTap,wholeTap with{Parts=new[]{tapBranch},Connections=Array.Empty<Connection>()});
Assert(tapFragment.Parts.Count==1&&tapFragment.BoundaryTapAxes!.Count==1,"Receiving pipe axis survives on the branch sheet without duplicating its physical part");
foreach(int turn in Enumerable.Range(0,4))
{
    var tapLayout=new SchematicLayout(tapFragment,quarterTurns:turn);
    var tapDimensions=DimensionChainPlanner.Build(tapFragment,p=>IsometricProjection.Project(p,quarterTurns:turn),quarterTurns:turn,projectSource:tapLayout.Project);
    Assert(tapDimensions.Any(d=>Math.Abs(d.Millimeters-1000)<.001&&(d.ModelStart!.Value.Z==1000||d.ModelEnd!.Value.Z==1000)),"Branch dimension terminates at receiving pipe axis on another sheet");
    Assert(!tapDimensions.Any(d=>Math.Abs(d.Millimeters-900)<.001),"Branch surface is not a dimension endpoint");
}
var ringNodes=Enumerable.Range(0,120).Select(i=>{int side=i/30,k=i%30;return side switch{0=>new ModelPoint(k*100,0,0),1=>new(3000,k*100,0),2=>new(3000-k*100,3000,0),_=>new(0,3000-k*100,0)};}).ToArray();
var ringSnapshot=LoopFixture(ringNodes,Enumerable.Range(0,120).Select(i=>(i,(i+1)%120)).ToArray());
var ringSheets=SheetSetPlanner.Build(ringSnapshot);
Assert(ringSheets.Count>=2,"An oversized ring can be partitioned at two pipe connections");
Assert(ringSheets.Sum(s=>s.Snapshot.Parts.Count)==120&&ringSheets.SelectMany(s=>s.Snapshot.Parts).Select(p=>p.Handle).Distinct().Count()==120,"Ring partition preserves every component exactly once");
var slopedSupportPipe=Pipe("support-run",new(0,0,0),new(1000,0,10));slopedSupportPipe=slopedSupportPipe with{Ports=slopedSupportPipe.Ports.Append(new Port("D1",new(500,0,5),new(1,0,0))).ToArray()};
// Adjacent internal stations also keep dimension intervals on the paper grid.
var shortStationPipe=Pipe("short-stations",new(0,0,0),new(1,0,0));
shortStationPipe=shortStationPipe with{Ports=shortStationPipe.Ports.Concat(new[]{new Port("D1",new(.25,0,0),new(1,0,0)),new Port("D2",new(.75,0,0),new(1,0,0))}).ToArray()};
var stationLayout=new SchematicLayout(Snapshot(new[]{shortStationPipe},Array.Empty<Connection>()),compactLevel:3);
var stationStart=stationLayout.Project(new(0,0,0),shortStationPipe.Handle);var stationEnd=stationLayout.Project(new(1,0,0),shortStationPipe.Handle);
Near(Math.Sqrt(Math.Pow(stationEnd.X-stationStart.X,2)+Math.Pow(stationEnd.Y-stationStart.Y,2)),30);
var stationMid=stationLayout.Project(new(.25,0,0),shortStationPipe.Handle);
Near(Math.Sqrt(Math.Pow(stationMid.X-stationStart.X,2)+Math.Pow(stationMid.Y-stationStart.Y,2)),10);
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
var paginationFirst=overflowSheets[0] with{Number=1,Scene=overflowSheets[0].Scene with{BillOfMaterials=overflowRows,Annotations=new[]{new Annotation("","title",default,"old title"),new Annotation("A","continuation",default,"П1 / лист 2")}}};
var paginationSecond=overflowSheets[0] with{Number=2,Scene=overflowSheets[0].Scene with{BillOfMaterials=overflowRows,Annotations=new[]{new Annotation("","title",default,"old title"),new Annotation("B","continuation",default,"П1 / лист 1")}}};
var interleaved=BomPagination.Apply(new[]{paginationFirst,paginationSecond});
int secondDiagram=interleaved.ToList().FindIndex(s=>s.Number>1&&!s.Scene.BomOnly);
Assert(secondDiagram==BomPagination.Pages(overflowRows).Count,"All first diagram BOM pages precede second diagram");
Assert(interleaved.Skip(1).Take(secondDiagram-1).All(s=>s.Scene.BomOnly)&&interleaved.Skip(secondDiagram+1).All(s=>s.Scene.BomOnly),"Each diagram is followed immediately by its own BOM continuation pages");
Assert(interleaved[0].Scene.Annotations.Single(a=>a.Role=="continuation").Text=="П1 / лист "+(secondDiagram+1),"Forward diagram reference is renumbered after inserted BOM pages");
Assert(interleaved[secondDiagram].Scene.Annotations.Single(a=>a.Role=="continuation").Text=="П1 / лист 1","Backward diagram reference retains correct target");
Assert(interleaved[secondDiagram+1].Scene.Annotations.Single(a=>a.Role=="title").Text.Contains("продолжение спецификации листа "+(secondDiagram+1)+" —"),"BOM continuation names the final owner sheet number");
Assert(interleaved.Select((s,i)=>s.Number==i+1&&s.Total==interleaved.Count).All(x=>x),"Interleaved set has consecutive final numbering");
var sideDimension=new PipeDimension("side-test",new(0,0),new(0,100),850,ChainId:"side-test",DimensionLinePoint:new(-10,0),ExtensionDirection:new(-1,0),TextPosition:new(-12.8,50));
var sideContour=new[]{new SheetPoint(-15,30),new SheetPoint(-5,30),new SheetPoint(-5,70),new SheetPoint(-15,70),new SheetPoint(-15,30)};
var sideSymbol=new SymbolGlyph("side-valve","","","valve-test",default,new(1,0),new(0,1),LocalBounds:sideContour.Take(4).ToArray(),LocalContours:new[]{(IReadOnlyList<SheetPoint>)sideContour});
var sideRouted=DimensionRouting.Route(new[]{sideDimension},Array.Empty<Stroke>(),new[]{sideSymbol},0).Single();
Assert(!DimensionRouting.GetSegments(new[]{sideRouted}).Where(segment=>segment.Baseline).Any(segment=>sideContour.Zip(sideContour.Skip(1),(a,b)=>new DimensionRouting.Segment(a,b,false)).Any(edge=>DimensionRouting.CheckCross(segment,edge))),"Vertical dimension baseline avoids a valve contour even when there is no pipe-axis crossing");
var planeHost=Pipe("plane-host",new(0,-500,0),new(0,500,0)) with{Properties=new Dictionary<string,string>{{"PnPClassName","Pipe"},{"NominalDiameter","150"}}};
var planeFlange=Pipe("plane-flange",new(0,0,100),new(0,0,110)) with{Properties=new Dictionary<string,string>{{"PnPClassName","Flange"},{"NominalDiameter","25"}}};
var planeSnapshot=Snapshot(new[]{planeHost,planeFlange},new[]{new Connection(planeHost.Handle,"S1",planeFlange.Handle,"S1")});
for(int turn=0;turn<4;turn++)
{
 var actual=IsoSymbolPlacement.FlangeTransverse(planeSnapshot,planeFlange,new(0,0,1),turn);var expected=IsometricProjection.Project(new(0,1,0),quarterTurns:turn);
 Near(actual.X*expected.Y-actual.Y*expected.X,0);
}
var remoteLabels=AnnotationPlacement.Place(new[]{new Annotation("remote-valve","valve-tag",new(100,100),"GV-TEST",Anchor:new(100,100)),new Annotation("remote-level","elevation",new(180,100),"ОТМ. +1.055",Anchor:new(180,100))},new List<Stroke>(),Array.Empty<SymbolGlyph>(),Array.Empty<PipeDimension>());
Assert(remoteLabels.All(a=>a.Anchor is {} anchor&&a.LeaderKnee is {} knee&&Math.Sqrt(Math.Pow(knee.X-anchor.X,2)+Math.Pow(knee.Y-anchor.Y,2))>=9.99),"Valve and elevation shelves are moved away from their geometry");var junctionStrokes=new List<Stroke>{new("A","insulation",new SheetPoint[]{new(-20,3),new(20,3)}),new("B","insulation",new SheetPoint[]{new(3,0),new(3,20)})};
InsulationJunctions.Trim(linked with{Parts=new[]{first with{Ports=first.Ports.Select(port=>port.Name=="S2"?port with{EndCondition="TAP"}:port).ToArray()},second}},junctionStrokes,new[]{("A",new SheetPoint(-20,0),new SheetPoint(20,0)),("B",new SheetPoint(0,0),new SheetPoint(0,20))},3);
Assert(junctionStrokes.Count(st=>st.SourceId=="A")==2&&junctionStrokes.Where(st=>st.SourceId=="B").All(st=>st.Points.Min(p=>p.Y)>=2.99),"Connected insulation junction trims crossing internal edges");
var unconnectedJunctionStrokes=new List<Stroke>{new("A","insulation",new SheetPoint[]{new(-20,3),new(20,3)})};
InsulationJunctions.Trim(separated,unconnectedJunctionStrokes,new[]{("A",new SheetPoint(-20,0),new SheetPoint(20,0)),("B",new SheetPoint(0,0),new SheetPoint(0,20))},3);
Assert(unconnectedJunctionStrokes.Count==1&&unconnectedJunctionStrokes[0].Points.Count==2,"Independent projected crossings do not merge insulation");
var positionLabel=new Annotation("position-test","position",new(102,103),"6",Anchor:new(100,100));
var positionBefore=AnnotationPlacement.Place(new[]{positionLabel},new List<Stroke>(),Array.Empty<SymbolGlyph>(),Array.Empty<PipeDimension>()).Single();
var positionBox=AnnotationPlacement.Bounds(positionBefore);double insulationX=(positionBox.Left+positionBox.Right)/2;
var positionAfter=AnnotationPlacement.Place(new[]{positionLabel},new List<Stroke>{new("position-test","insulation",new SheetPoint[]{new(insulationX,positionBox.Bottom-10),new(insulationX,positionBox.Top+10)})},Array.Empty<SymbolGlyph>(),Array.Empty<PipeDimension>()).Single();
Assert(positionAfter.Position!=positionBefore.Position,"Position number moves away from insulation line");var alignA=new PipeDimension("align-a",new(0,0),new(50,30),100,ChainId:"align-a",DimensionLinePoint:new(0,12),ExtensionDirection:new(0,1),ModelStart:new(0,0,0),ModelEnd:new(100,0,0));
var alignB=new PipeDimension("align-b",new(50,30),new(100,0),100,ChainId:"align-b",DimensionLinePoint:new(50,55),ExtensionDirection:new(0,1),ModelStart:new(100,0,0),ModelEnd:new(100,100,0));
var alignedPair=DimensionRowAlignment.AlignAdjacent(new[]{alignA,alignB});
Near(alignedPair[0].DimensionLinePoint!.Value.Y-alignedPair[0].Start.Y,alignedPair[1].DimensionLinePoint!.Value.Y-alignedPair[1].Start.Y);
Assert(alignedPair.All(d=>d.ExtensionDirection==new SheetPoint(0,1)),"Joined rows align without changing isometric extension planes");
var alignmentWall=new Stroke("alignment-obstacle","pipe",new SheetPoint[]{new(25,35),new(25,45)});
var guardedPair=DimensionRowAlignment.AlignAdjacent(new[]{alignA,alignB},new[]{alignmentWall},Array.Empty<SymbolGlyph>());
Assert(guardedPair[0]==alignA&&guardedPair[1]==alignB,"Alignment yields to a clear placement when a common lane would cross a pipe");
var nearbyB=alignB with{DimensionLinePoint=new(50,44)};
var distantC=new PipeDimension("align-c",new(100,0),new(150,30),100,ChainId:"align-c",
    DimensionLinePoint:new(100,39),ExtensionDirection:new(0,1),ModelStart:new(100,100,0),ModelEnd:new(200,100,0));
var farLaneBlockers=new[]{new Stroke("far-a","pipe",new SheetPoint[]{new(25,50),new(25,58)}),
    new Stroke("far-b","pipe",new SheetPoint[]{new(75,50),new(75,58)})};
var partialAlignment=DimensionRowAlignment.AlignAdjacent(new[]{alignA,nearbyB,distantC},farLaneBlockers);
Near(partialAlignment[0].DimensionLinePoint!.Value.Y-partialAlignment[0].Start.Y,14);
Assert(partialAlignment[1]==nearbyB&&partialAlignment[2]==distantC,
    "An obstructed family still aligns the adjacent pair that can safely share an arrow tip");
var oppositeA=alignA with{DimensionLinePoint=new(0,-18)};
var oppositePair=DimensionRowAlignment.AlignAdjacent(new[]{oppositeA,alignB});
Assert(oppositePair[0]==oppositeA&&oppositePair[1]==alignB,"Adjacent runs retain opposite routing sides instead of pulling a clear lower row through the pipe");
var lowerB=alignB with{DimensionLinePoint=new(50,5)};
var lowerPair=DimensionRowAlignment.AlignAdjacent(new[]{oppositeA,lowerB});
Near(lowerPair[0].DimensionLinePoint!.Value.Y-lowerPair[0].Start.Y,-25);
Near(lowerPair[1].DimensionLinePoint!.Value.Y-lowerPair[1].Start.Y,-25);
Assert(lowerPair.All(d=>d.ExtensionDirection==new SheetPoint(0,1)),"Lower adjacent rows align outward within the original dimension plane");

Assert((alignA with{ReferenceOnly=true}).Label=="100*","Reference dimensions display an asterisk");var referenceValve=first with{Properties=new Dictionary<string,string>(first.Properties){["PnPClassName"]="Valve"}};
var referenceScene=SheetPlanner.Build(linked with{Parts=new[]{referenceValve,second}});
Assert(referenceScene.Dimensions.Any(d=>d.ReferenceOnly&&d.Role=="chain"),"Valve installation dimension is reference-only");
Assert(referenceScene.Dimensions.Where(d=>d.Role=="overall").All(d=>d.ReferenceOnly&&d.Label.EndsWith('*')),"Overall dimension inherits reference status from valve interval");
Near((fixtureCatalog with{SymbolScale=15}).ScaleFor(supportPart),10);
Near((fixtureCatalog with{SymbolScale=10}).ScaleFor(supportPart),10);
var corridorDimension=new PipeDimension("lower-run",new(0,0),new(100,0),100,ChainId:"lower-run",DimensionLinePoint:new(0,10),ExtensionDirection:new(0,1),TextPosition:new(50,12.8));
var corridorRows=DimensionRouting.Route(new[]{corridorDimension},new[]{new Stroke("upper-run","pipe",new SheetPoint[]{new(0,20),new(100,20)})},Array.Empty<SymbolGlyph>(),0);
Assert(corridorRows[0].DimensionLinePoint!.Value.Y<0||corridorRows[0].DimensionLinePoint.Value.Y>23,"Dimension row moves outside the space between parallel runs");var continuationFixture=Snapshot(new[]{first},Array.Empty<Connection>());var continuationLayout=new SchematicLayout(continuationFixture);
var continuationAt=continuationLayout.Project(first.Ports[1].Position,first.Handle);var continuationEnd=new SheetPoint(continuationAt.X+12,continuationAt.Y);
var continuationScene=SheetPlanner.Build(continuationFixture,continuations:new[]{new SheetContinuation(first.Handle,"S2","П1",2)},contextStrokes:new[]{new Stroke("next-sheet","context-pipe",new[]{continuationAt,continuationEnd})});
var continuationBreak=continuationScene.Strokes.Single(st=>st.Role=="continuation-break");
Near((continuationBreak.Points[0].X+continuationBreak.Points[^1].X)/2,continuationEnd.X);Near((continuationBreak.Points[0].Y+continuationBreak.Points[^1].Y)/2,continuationEnd.Y);
Assert(flowScene.Strokes.Count(st=>st.Role=="flow-shaft")==1,"Flow arrow includes its bold shaft");var orderChain=new PipeDimension("order",new(0,0),new(50,0),248,Role:"chain",ChainId:"order",DimensionLinePoint:new(0,-15),ExtensionDirection:new(0,1),TextPosition:new(25,-12.2));
var orderOverall=orderChain with{Role="overall",Millimeters=844,End=new(100,0),DimensionLinePoint=new(0,20),TextPosition=new(50,22.8)};
var orderedRows=DimensionRowAlignment.EnforceChainOrder(new[]{orderChain,orderOverall});
Assert(orderedRows[1].DimensionLinePoint!.Value.Y<=-22,"Overall follows a lower chain on its outer side with 7 mm spacing");
var orderedUpper=DimensionRowAlignment.EnforceChainOrder(new[]{orderChain with{DimensionLinePoint=new(0,15)},orderOverall with{DimensionLinePoint=new(0,-20)}});
Assert(orderedUpper[1].DimensionLinePoint!.Value.Y>=22,"Overall follows an upper chain on its outer side");
Assert(orderedRows.All(d=>d.Millimeters is 248 or 844),"Row ordering preserves engineering dimensions");var numericInsulation=new Stroke("insulation-only","insulation",new SheetPoint[]{new(0,0),new(400,400)});
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
var budget=new PlanningExecution(CancellationToken.None,null,TimeSpan.FromMinutes(1),2,10);
budget.Report("first candidate",true);
budget.Report("last candidate",true);
budget.Check();
bool budgetStopped=false;
try{budget.Report("extra candidate",true);}
catch(PlanningLimitException){budgetStopped=true;}
Assert(budgetStopped,"The variant budget admits the last candidate and rejects the next attempt");
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
Assert(BomCategories.Classify(first with{Properties=new Dictionary<string,string>{{"PnPClassName","FlowMeter"},{"ContentIsoSymbolDefinition","TYPE=INSTRUMENT"}}})==BomCategory.Valves,"Flowmeters belong to pipeline valves even with an instrument ISO type");
Assert(BomCategories.Classify(first with{Properties=new Dictionary<string,string>{{"PnPClassName","ControlValve"},{"ContentIsoSymbolDefinition","TYPE=INSTRUMENT"}}})==BomCategory.Valves,"Instrumented control valves remain pipeline valves");
foreach(string instrument in new[]{"PressureGauge","Sensor","Thermometer"})Assert(BomCategories.Classify(first with{Properties=new Dictionary<string,string>{{"PnPClassName",instrument}}})==BomCategory.Instruments,"Sensors, gauges and thermometers remain instruments");
var diameterParts=new[]{100,25,10}.Select((dn,i)=>Pipe("diameter-"+dn,new(0,i*1000,0),new(1302,i*1000,0)) with{Properties=new Dictionary<string,string>{{"PnPClassName","Pipe"},{"NominalDiameter",dn.ToString()},{"Size",dn.ToString()},{"PartSizeLongDesc","DN"+dn}}}).ToArray();
var diameterScene=SheetPlanner.Build(Snapshot(diameterParts,Array.Empty<Connection>()));
Assert(diameterScene.BillOfMaterials.Select(r=>r.Description).SequenceEqual(new[]{"DN10","DN25","DN100"}),"Pipe BOM sorts numeric diameter, not string or handle order");
Assert(diameterScene.BillOfMaterials.All(r=>r.QuantityText=="1,30"&&Math.Abs(r.Quantity-1.302)<1e-8),"Only displayed quantities round aggregated model lengths");
var minimalPipe=Pipe("readable-short",new(0,0,0),new(1,0,0));var minimalLayout=new SchematicLayout(Snapshot(new[]{minimalPipe},Array.Empty<Connection>()),compactLevel:3);
var minimalStart=minimalLayout.Project(minimalPipe.Ports[0].Position,minimalPipe.Handle);var minimalEnd=minimalLayout.Project(minimalPipe.Ports[1].Position,minimalPipe.Handle);
double minimalPaperLength=Math.Sqrt(Math.Pow(minimalEnd.X-minimalStart.X,2)+Math.Pow(minimalEnd.Y-minimalStart.Y,2));
Near(minimalPaperLength,10);
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
var enclosure=new List<Stroke>{new("enclosure","component",new SheetPoint[]{new(89,91),new(111,91),new(111,109),new(89,109),new(89,91)})};
var enclosureGlyph=new SymbolGlyph("enclosure","","","TestBox",new(100,100),new(20,0),new(0,32));
var crossingLeader=AnnotationPlacement.Place(new[]{new Annotation("leader","valve-tag",new(100,100),"ID",Anchor:new(100,100))},enclosure,new[]{enclosureGlyph},Array.Empty<PipeDimension>());
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
var footprintScene=scene with
{
    Strokes=new[]{new Stroke("run","pipe",new SheetPoint[]{new(0,0),new(80,40)})},
    Symbols=Array.Empty<SymbolGlyph>()
};
var longCallout=footprintScene with
{
    Strokes=footprintScene.Strokes.Append(new Stroke("tag","elevation-leader",
        new SheetPoint[]{new(20,10),new(350,300)})).ToArray()
};
Near(LayoutQuality.Footprint(footprintScene),LayoutQuality.Footprint(longCallout));
var expandedRun=footprintScene with
{
    Strokes=new[]{new Stroke("run","pipe",new SheetPoint[]{new(0,0),new(100,50)})}
};
Assert(LayoutQuality.Footprint(expandedRun)>LayoutQuality.Footprint(footprintScene),
    "Use of the free field is measured by pipe geometry and fittings, not by a long leader");
Assert(PipeDimension.IsVerticalTextAxis(-0.2,100)&&!PipeDimension.ReverseTextAxis(-0.2,100)&&
    PipeDimension.ReverseTextAxis(0.2,-100),"Nearly vertical dimension text reads upward from either endpoint order");
var pipeBendPart=new Part("test.dwg","bend1","AcPpDb3dPipeInlineAsset","L1",
    new[]{new Port("S1",new(0,0,0),new(-1,0,0)),new Port("S2",new(100,100,0),new(0,1,0))},
    new Dictionary<string,string>{{"PnPClassName","PipeBend"},{"ContentIsoSymbolDefinition","TYPE=BEND,SKEY=PBBW"}});
Assert(pipeBendPart.Component==ComponentKind.Elbow,"PipeBend is treated as Elbow for corner layout and symbol placement");
var pipeBendScene=SheetPlanner.Build(Snapshot(new[]{pipeBendPart},Array.Empty<Connection>()));
Assert(!pipeBendScene.Annotations.Any(a=>a.SourceId==pipeBendPart.Handle&&a.Role=="bend-angle")&&
    !pipeBendScene.Strokes.Any(stroke=>stroke.SourceId==pipeBendPart.Handle&&stroke.Role=="bend-dimension"),
    "A 90-degree pipe bend gets no degree label or dimension arc");
Assert(pipeBendScene.Dimensions.Count==2&&pipeBendScene.Dimensions.All(d=>d.Millimeters==100&&
    (d.ModelStart==new ModelPoint(100,0,0)||d.ModelEnd==new ModelPoint(100,0,0))),
    "A standalone pipe bend gets both axis lengths to the model intersection of its tangents");
var catalogElbow=pipeBendPart with{Properties=new Dictionary<string,string>{{"PnPClassName","Elbow"}}};
Assert(SheetPlanner.Build(Snapshot(new[]{catalogElbow},Array.Empty<Connection>())).Dimensions.Count==0,
    "A standalone catalogue elbow acquires no separate bend lengths");
var bendOnRise=pipeBendPart with
{
    Ports=new[]{new Port("S1",new(0,0,0),new(-1,0,-1)),new Port("S2",new(150,0,50),new(1,0,0))}
};
var pipeBeforeBend=Pipe("rise-to-bend",new(-500,0,-500),new(0,0,0)) with
{
    Ports=new[]{new Port("S1",new(-500,0,-500),new(-1,0,-1)),new Port("S2",new(0,0,0),new(1,0,1))}
};
var bendOnRiseScene=SheetPlanner.Build(Snapshot(new[]{bendOnRise,pipeBeforeBend},
    new[]{new Connection(pipeBeforeBend.Handle,"S2",bendOnRise.Handle,"S1")}));
Assert(bendOnRiseScene.Annotations.Any(a=>a.SourceId==pipeBeforeBend.Handle&&a.Role=="slope-angle"&&a.Text=="45°")&&
    bendOnRiseScene.Annotations.Any(a=>a.SourceId==bendOnRise.Handle&&a.Role=="bend-angle"&&a.Text=="45°")&&
    !bendOnRiseScene.Strokes.Any(stroke=>stroke.SourceId==bendOnRise.Handle&&stroke.Role=="bend-dimension"),
    "A pipe bend keeps its own angle when the attached pipe also has an incline dimension");
double tracingRise=50*Math.Sqrt(3);
var tracingBend1=pipeBendPart with
{
    Handle="tracing-bend-1",
    Ports=new[]{new Port("S1",new(0,0,-100),new(0,0,-1)),new Port("S2",new(25,0,tracingRise/2),new(1,0,Math.Sqrt(3)))}
};
var tracingBend2=pipeBendPart with
{
    Handle="tracing-bend-2",
    Ports=new[]{new Port("S1",tracingBend1.Ports[1].Position,new(-1,0,-Math.Sqrt(3))),
        new Port("S2",new(50,0,tracingRise+100),new(0,0,1))}
};
var inclinedBends=Snapshot(new[]{tracingBend1,tracingBend2},
    new[]{new Connection(tracingBend1.Handle,"S2",tracingBend2.Handle,"S1")});
SheetPoint ProjectTracingBends(ModelPoint point)=>IsometricProjection.Project(point);
var tracingDeviationStrokes=new List<Stroke>();
var tracingDeviationLabels=new List<Annotation>();
SlopeNotation.Add(inclinedBends,ProjectTracingBends,0,tracingDeviationStrokes,tracingDeviationLabels,
    DimensionChainPlanner.Build(inclinedBends,ProjectTracingBends));
Assert(tracingDeviationStrokes.Count(stroke=>stroke.Role=="slope-triangle")==1&&
    tracingDeviationStrokes.Any(stroke=>stroke.Role=="slope-hatch")&&
    tracingDeviationLabels.Any(label=>label.Role=="slope-angle"&&label.Text=="60°"),
    "Two connected pipe bends produce one hatched deviation plane for their inclined common tangent");
var returnBend=new Part("test.dwg","bend180","AcPpDb3dPipeInlineAsset","L1",
    new[]{new Port("S1",new(0,0,0),new(1,0,0)),new Port("S2",new(0,80,0),new(1,0,0))},
    new Dictionary<string,string>{{"PnPClassName","PipeBend"},{"ContentIsoSymbolDefinition","TYPE=BEND,SKEY=BUBW"}});
var returnTop=PipelineTopology.Build(Snapshot(new[]{returnBend},Array.Empty<Connection>()));
Assert(returnTop.Edges.Count==3&&returnTop.Nodes.Count(n=>n.Kind==RouteNodeKind.Axis)==2,"180-degree return bend creates two corner axis nodes and 3 edges");
var returnDimensions=DimensionChainPlanner.Build(Snapshot(new[]{returnBend},Array.Empty<Connection>()),
    point=>IsometricProjection.Project(point));
Assert(returnDimensions.Count==1&&returnDimensions[0].Millimeters==80,
    "A return bend receives its transverse spacing without standalone sizes on artificial tangent legs");
var dipBend=returnBend with{Ports=new[]{new Port("S1",new(0,0,40),new(0,0,1)),new Port("S2",new(0,80,40),new(0,0,1))}};
var dipLeft=Pipe("dip-left",new(0,0,40),new(0,0,90));
var dipRight=Pipe("dip-right",new(0,80,40),new(0,80,90));
var dipEntry=returnBend with{Handle="dip-entry",Ports=new[]{new Port("S1",new(0,0,90),new(0,0,-1)),new Port("S2",new(0,-40,130),new(0,-1,0))}};
var dipExit=returnBend with{Handle="dip-exit",Ports=new[]{new Port("S1",new(0,80,90),new(0,0,-1)),new Port("S2",new(0,120,130),new(0,1,0))}};
var dip=Snapshot(new[]{dipBend,dipLeft,dipRight,dipEntry,dipExit},new[]{new Connection(dipBend.Handle,"S1",dipLeft.Handle,"S1"),
    new Connection(dipBend.Handle,"S2",dipRight.Handle,"S1"),new Connection(dipLeft.Handle,"S2",dipEntry.Handle,"S1"),
    new Connection(dipRight.Handle,"S2",dipExit.Handle,"S1")});
var dipLayout=new SchematicLayout(dip,fixedScale:.2,pipeExtensions:new Dictionary<string,double>{{dipLeft.Handle,60}});
double DipLegLength(Part leg)=>Math.Abs(dipLayout.Project(leg.Ports[0].Position,leg.Handle).Y-
    dipLayout.Project(leg.Ports[1].Position,leg.Handle).Y);
Assert(DipLegLength(dipLeft)<=20&&Math.Abs(DipLegLength(dipLeft)-DipLegLength(dipRight))<.01,
    "Symmetric return legs stay compact and level even when clearance requests a unilateral extension");
IsoBlockDefinition OutletBend(string name,double rise)=>new(name,
    new Dictionary<string,SheetPoint>{{"Port1",new(0,-rise)},{"Port2",new(5,0)},{"CenterPoint",new(0,0)}},
    new(0,-rise),new(5,0));
var sizedDip=dip with{Parts=new[]{dipBend,dipLeft,dipRight,
    dipEntry with{Properties=new Dictionary<string,string>(dipEntry.Properties){["ContentIsoSymbolDefinition"]="TYPE=BEND,SKEY=ENTRY"}},
    dipExit with{Properties=new Dictionary<string,string>(dipExit.Properties){["ContentIsoSymbolDefinition"]="TYPE=BEND,SKEY=EXIT"}}}};
var dipSymbols=new IsoSymbolMap("<Map/>",new IsoBlockCatalog(new[]{OutletBend("ENTRY",5),OutletBend("EXIT",32)}));
foreach(int turns in Enumerable.Range(0,4))
{
    var sizedLayout=new SchematicLayout(sizedDip,quarterTurns:turns,symbolMap:dipSymbols,fixedScale:.2);
    var start=sizedLayout.Project(dipEntry.Ports[1].Position,dipEntry.Handle);
    var end=sizedLayout.Project(dipExit.Ports[1].Position,dipExit.Handle);
    var planDirection=IsometricProjection.Project(new(0,1,0),quarterTurns:turns);
    Assert(Math.Abs(end.Y-start.Y-(end.X-start.X)*planDirection.Y/planDirection.X)<.01,
        $"Equal-elevation return outlets stay on the same projected axis with unequal elbow symbols (turns {turns})");
    double VerticalPipeSpan(Part leg)=>Math.Abs(sizedLayout.Project(leg.Ports[0].Position,leg.Handle).Y-
        sizedLayout.Project(leg.Ports[1].Position,leg.Handle).Y);
    Near(VerticalPipeSpan(dipRight),10);
    Near(VerticalPipeSpan(dipLeft),37);
}
var bendDef=new IsoBlockDefinition("Bend-180return",
    new Dictionary<string,SheetPoint>{{"Port1",new(-0.75,0)},{"Port3",new(-0.75,0.9375)},{"CenterPoint",new(0,0)}},
    new SheetPoint(-0.75,0),new SheetPoint(0,0.9375));
var testCatalog=new IsoBlockCatalog(new[]{bendDef});
var pA=IsometricProjection.Project(new ModelPoint(0,0,0));
var pB=IsometricProjection.Project(new ModelPoint(0,80,0));
var pCorner=IsometricProjection.Project(new ModelPoint(-40,40,0));
var bendGlyph=IsoSymbolPlacement.Corner(returnBend,"Bend-180return",testCatalog,pA,pCorner,pB);
Assert(Math.Abs(bendGlyph.XAxis.Y/bendGlyph.XAxis.X-1.0/Math.Sqrt(3))<1e-6,"Bend-180return X axis aligns with the connected pipe in isometric plane (not horizontal)");
var placedP1=new SheetPoint(bendGlyph.Origin.X+bendGlyph.XAxis.X*(-0.75),bendGlyph.Origin.Y+bendGlyph.XAxis.Y*(-0.75));
var placedP3=new SheetPoint(bendGlyph.Origin.X+bendGlyph.XAxis.X*(-0.75)+bendGlyph.YAxis.X*0.9375,bendGlyph.Origin.Y+bendGlyph.XAxis.Y*(-0.75)+bendGlyph.YAxis.Y*0.9375);
var placedApex=new SheetPoint(bendGlyph.Origin.X+bendGlyph.YAxis.X*0.46875,bendGlyph.Origin.Y+bendGlyph.YAxis.Y*0.46875);
Assert(Math.Abs(placedP1.X-pA.X)<1e-6&&Math.Abs(placedP1.Y-pA.Y)<1e-6,"Bend-180return Port1 connects exactly to pipe A");
Assert(Math.Abs(placedP3.X-pB.X)<1e-6&&Math.Abs(placedP3.Y-pB.Y)<1e-6,"Bend-180return Port3 connects exactly to pipe B");
Assert(Math.Abs(placedApex.X-pCorner.X)<1e-6&&Math.Abs(placedApex.Y-pCorner.Y)<1e-6,"Bend-180return apex connects exactly to corner axis");
var insStrokes=new List<Stroke>();
var insPart=returnBend with{Properties=new Dictionary<string,string>(returnBend.Properties){["InsulationThickness"]="50"}};
var insSnap=Snapshot(new[]{insPart},Array.Empty<Connection>());
InsulationPlanner.Add(insSnap,p=>IsometricProjection.Project(p),new[]{bendGlyph},insStrokes);
Assert(insStrokes.Count==2,"Insulated return bend has 2 contours");
foreach(var s in insStrokes)
{
    var paActual=s.Points[0];var pbActual=s.Points[^1];
    Near(Math.Sqrt(Math.Pow(paActual.X-pA.X,2)+Math.Pow(paActual.Y-pA.Y,2)),3);
    Near(Math.Sqrt(Math.Pow(pbActual.X-pB.X,2)+Math.Pow(pbActual.Y-pB.Y,2)),3);
}
var bendDefWithContours=new IsoBlockDefinition("Bend-180return-contours",
    new Dictionary<string,SheetPoint>{{"Port1",new(-0.75,0)},{"Port3",new(-0.75,0.9375)},{"CenterPoint",new(0,0)}},
    new SheetPoint(-0.75,0),new SheetPoint(0,0.9375),
    new[]{new SheetPoint[]{new(-0.75,0),new(-0.375,0.1),new(0,0.46875),new(-0.375,0.8375),new(-0.75,0.9375)}});
var cat2=new IsoBlockCatalog(new[]{bendDefWithContours});
var glyph2=IsoSymbolPlacement.Corner(returnBend,"Bend-180return-contours",cat2,pA,pCorner,pB);
var strokes2=new List<Stroke>();
InsulationPlanner.Add(insSnap,p=>IsometricProjection.Project(p),new[]{glyph2},strokes2);
Assert(strokes2.Count==2,"Insulated return bend with block contours has 2 contours");
foreach(var s in strokes2)
{
    Near(Math.Sqrt(Math.Pow(s.Points[0].X-pA.X,2)+Math.Pow(s.Points[0].Y-pA.Y,2)),3);
    Near(Math.Sqrt(Math.Pow(s.Points[^1].X-pB.X,2)+Math.Pow(s.Points[^1].Y-pB.Y,2)),3);
}
var verticalNg=Pipe("ng-pipe",new(0,0,0),new(0,0,1300));
var elbowNg=new Part("test.dwg","ng-elbow","AcPpDb3dPipeInlineAsset","L1",
    new[]{new Port("S1",new(0,0,1300),new(0,-.029,-.99958)),
          new Port("S2",new(0,-73,1377.18),new(0,-.99958,.029))},
    new Dictionary<string,string>{{"PnPClassName","Elbow"}});
var ngDimensions=DimensionChainPlanner.Build(Snapshot(new[]{elbowNg,verticalNg},
    new[]{new Connection("ng-pipe","S2","ng-elbow","S1")}),p=>IsometricProjection.Project(p));
Assert(ngDimensions.Any(d=>d.Role=="overall"&&d.SourceIds!.Contains("ng-elbow")&&
    d.SourceIds.Contains("ng-pipe")&&d.Millimeters>1374&&d.Millimeters<1376),
    "The upper elbow contributes its vertical rise to the overall dimension");
Assert(SheetPlanner.RemoveRepeatedStandards("Труба ГОСТ 8732-2025/ В ГОСТ 8731-2025","ГОСТ 8732–2025")==
    "Труба В ГОСТ 8731-2025","Remove only the standard present in designation");
Assert(SheetPlanner.RemoveRepeatedStandards("Опора-ОСТ 36-146-88","ОСТ 36-146-88")=="Опора",
    "Remove duplicate OST and its delimiter");
Assert(SheetPlanner.RemoveRepeatedStandards("Отвод ГОСТ 17375-2001","GV-110")=="Отвод ГОСТ 17375-2001",
    "Keep the standard when the designation is a tag");
Console.WriteLine($"Passed {checks} checks.");


}
catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}

sealed class CallbackProgress<T>(Action<T> callback):IProgress<T>{public void Report(T value)=>callback(value);}
