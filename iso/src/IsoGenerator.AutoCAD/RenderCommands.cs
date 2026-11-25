using System.IO;
using System.Text.Json;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using IsoGenerator.Core;
using App = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(IsoGenerator.AutoCAD.RenderCommands))]
namespace IsoGenerator.AutoCAD;
public class RenderCommands
{
    [CommandMethod("ISORENDER",CommandFlags.Modal)]
    public void Render()
    {
        string input=Environment.GetEnvironmentVariable("ISO_SCENE_INPUT")??throw new InvalidOperationException("ISO_SCENE_INPUT missing");
        string output=Environment.GetEnvironmentVariable("ISO_DWG_OUTPUT")??throw new InvalidOperationException("ISO_DWG_OUTPUT missing");
        if(File.Exists(output))throw new IOException("Output exists; overwriting is disabled");
        string? trace=Environment.GetEnvironmentVariable("ISO_RENDER_TRACE");
        void Trace(string stage){if(!string.IsNullOrEmpty(trace))File.AppendAllText(trace,stage+Environment.NewLine);}
        Trace("read scene");
        var scene=JsonSerializer.Deserialize<SheetScene>(File.ReadAllText(input))??throw new InvalidDataException("No scene");
        using var profile=IsoAdvancedSettings.UseWorkerProfile();
        var tuning=IsoAdvancedSettings.WorkerSettings;
        scene=SheetComposition.Center(scene);
        string sheetNumber=Environment.GetEnvironmentVariable("ISO_SHEET_NUMBER")??"1";
        string stampSheetNumber=Environment.GetEnvironmentVariable("ISO_DOCUMENT_SHEET_NUMBER")??sheetNumber;
        static string EscapeMText(string text)=>text.Replace(((char)92).ToString(),new string((char)92,2)).Replace("{",((char)92)+"{").Replace("}",((char)92)+"}");
        string valvesDocument=Environment.GetEnvironmentVariable("ISO_VALVES_DOCUMENT")??"";
        string supportsDocument=Environment.GetEnvironmentVariable("ISO_SUPPORTS_DOCUMENT")??"";
        string stampDocument=Environment.GetEnvironmentVariable("ISO_STAMP_DOCUMENT")??"";
        var notes=new List<string>{"1.      * Размер для справок;"};
        if(!string.IsNullOrWhiteSpace(valvesDocument))notes.Add("2.      Трубопроводную арматуру см. "+valvesDocument+";");
        if(!string.IsNullOrWhiteSpace(supportsDocument))notes.Add("3.      Опоры трубопроводов см. "+supportsDocument+";");
        bool multipleSheets=int.TryParse(Environment.GetEnvironmentVariable("ISO_SHEET_COUNT"),out int sheetCount)&&sheetCount>1;
        string sheetSuffix=(multipleSheets||Environment.GetEnvironmentVariable("ISO_NUMBER_ALL_DRAWINGS")=="1")?"(L"+sheetNumber+")":"";
        notes.Add("4.      Схема соединений (монтажная): "+scene.Line+sheetSuffix+".");
        var doc=App.DocumentManager.MdiActiveDocument;var db=doc.Database;
        var dimensionIds=new List<ObjectId>();
        var dimensionBreakObjects=new List<ObjectId>();
        var leaderIds=new List<ObjectId>();
        ResultBuffer SourceData(string source)=>new(new TypedValue(1001,"ISOGENERATOR"),
            new TypedValue(1000,scene.OriginalHandles?.GetValueOrDefault(source,source)??source),
            new TypedValue(1000,scene.SourceDrawing),new TypedValue(1000,scene.SourceGuids?.GetValueOrDefault(source,"")??""));
        if(string.Equals(Path.GetFullPath(output),Path.GetFullPath(db.Filename),StringComparison.OrdinalIgnoreCase))throw new IOException("Cannot overwrite input template");
        using(var tr=db.TransactionManager.StartTransaction())
        {
            var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
            if(!bt.Has("Рамка_А2г"))throw new InvalidDataException("Expected A2 frame missing");
            foreach(var handle in new[]{"A13C","A141","A146"})
            {
                var oid=db.GetObjectId(false,new Handle(long.Parse(handle,NumberStyles.HexNumber,CultureInfo.InvariantCulture)),0);
                if(tr.GetObject(oid,OpenMode.ForRead) is not MText)throw new InvalidDataException($"Template text mapping changed: {handle}");
            }
            var textStyles=(TextStyleTable)tr.GetObject(db.TextStyleTableId,OpenMode.ForRead);
            ObjectId gostStyle;
            const string generatedTextStyle="ISO-ANNOTATION-TEXT";
            if(textStyles.Has(generatedTextStyle))gostStyle=textStyles[generatedTextStyle];
            else {textStyles.UpgradeOpen();var style=new TextStyleTableRecord{Name=generatedTextStyle};gostStyle=textStyles.Add(style);tr.AddNewlyCreatedDBObject(style,true);}
            var gost=(TextStyleTableRecord)tr.GetObject(gostStyle,OpenMode.ForWrite);
            gost.FileName="GOST_B.TTF";gost.BigFontFileName="";gost.TextSize=tuning.LeaderTextHeight;gost.XScale=1;gost.ObliquingAngle=Math.PI/12;
            db.Textstyle=gostStyle;
            ObjectId arrowId;
            if(bt.Has("ISO-ARROW-20"))arrowId=bt["ISO-ARROW-20"];
            else
            {
                bt.UpgradeOpen();var arrow=new BlockTableRecord{Name="ISO-ARROW-20"};arrowId=bt.Add(arrow);tr.AddNewlyCreatedDBObject(arrow,true);
                double halfWidth=Math.Tan(10*Math.PI/180);
                var triangle=new Solid(Point3d.Origin,new Point3d(-1,-halfWidth,0),new Point3d(-1,halfWidth,0),new Point3d(-1,halfWidth,0)){ColorIndex=0,LineWeight=LineWeight.ByBlock};
                arrow.AppendEntity(triangle);tr.AddNewlyCreatedDBObject(triangle,true);
            }
            var dimensionStyles=(DimStyleTable)tr.GetObject(db.DimStyleTableId,OpenMode.ForRead);
            ObjectId dimensionStyle;
            if(dimensionStyles.Has("Standard_on"))dimensionStyle=dimensionStyles["Standard_on"];
            else {dimensionStyles.UpgradeOpen();var style=new DimStyleTableRecord();style.CopyFrom(tr.GetObject(db.Dimstyle,OpenMode.ForRead));style.Name="Standard_on";dimensionStyle=dimensionStyles.Add(style);tr.AddNewlyCreatedDBObject(style,true);}
            ObjectId dimensionTextStyle;
            if(textStyles.Has("ISO-DIMENSION-TEXT"))dimensionTextStyle=textStyles["ISO-DIMENSION-TEXT"];
            else {if(!textStyles.IsWriteEnabled)textStyles.UpgradeOpen();var style=new TextStyleTableRecord{Name="ISO-DIMENSION-TEXT"};dimensionTextStyle=textStyles.Add(style);tr.AddNewlyCreatedDBObject(style,true);}
            var dimensionFont=(TextStyleTableRecord)tr.GetObject(dimensionTextStyle,OpenMode.ForWrite);
            dimensionFont.FileName="GOST_A.TTF";dimensionFont.TextSize=tuning.DimensionTextHeight;dimensionFont.XScale=1;dimensionFont.ObliquingAngle=0;
            var dimStyle=(DimStyleTableRecord)tr.GetObject(dimensionStyle,OpenMode.ForWrite);dimStyle.Dimtxsty=dimensionTextStyle;dimStyle.Dimtxt=tuning.DimensionTextHeight;dimStyle.Dimjust=0;dimStyle.Dimatfit=3;dimStyle.Dimtmove=2;dimStyle.Dimblk=arrowId;dimStyle.Dimasz=tuning.ArrowSize;dimStyle.Dimtsz=0;dimStyle.Dimsah=false;dimStyle.Dimscale=1;dimStyle.Dimtad=1;dimStyle.Dimexo=tuning.ExtensionOffset;dimStyle.Dimexe=tuning.ExtensionOvershoot;dimStyle.Dimdli=tuning.DimensionRowStep;
            db.Dimstyle=dimensionStyle;db.SetDimstyleData(dimStyle);
            var leaderStyles=(DBDictionary)tr.GetObject(db.MLeaderStyleDictionaryId,OpenMode.ForRead);
            ObjectId leaderStyleId;
            if(leaderStyles.Contains("Standard_m"))leaderStyleId=leaderStyles.GetAt("Standard_m");
            else
            {
                var created=new MLeaderStyle();created.CopyFrom(tr.GetObject(db.MLeaderstyle,OpenMode.ForRead));
                leaderStyleId=created.PostMLeaderStyleToDb(db,"Standard_m");tr.AddNewlyCreatedDBObject(created,true);
            }
            var leaderStyle=(MLeaderStyle)tr.GetObject(leaderStyleId,OpenMode.ForWrite);
            leaderStyle.ContentType=ContentType.MTextContent;leaderStyle.TextStyleId=gostStyle;leaderStyle.TextHeight=tuning.LeaderTextHeight;
            leaderStyle.ArrowSymbolId=arrowId;leaderStyle.ArrowSize=tuning.ArrowSize;leaderStyle.EnableLanding=true;leaderStyle.EnableDogleg=true;
            leaderStyle.DoglegLength=1;leaderStyle.LandingGap=1;leaderStyle.ExtendLeaderToText=true;
            leaderStyle.SetTextAttachmentType(TextAttachmentType.AttachmentBottomLine,LeaderDirectionType.LeftLeader);
            leaderStyle.SetTextAttachmentType(TextAttachmentType.AttachmentBottomLine,LeaderDirectionType.RightLeader);
            db.MLeaderstyle=leaderStyleId;
            var ms=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForWrite);
            Table? table=null;
            foreach(ObjectId id in ms)
            {
                if(tr.GetObject(id,OpenMode.ForRead) is Table t)
                {
                    if(table is not null)throw new InvalidDataException("Ambiguous template: multiple model tables");
                    table=t;
                }
            }
            if(table is null||table.Columns.Count!=5||table.Cells[0,0].TextString!="Спецификация трубопровода")throw new InvalidDataException("Expected five-column BOM missing");
            if(!BomCategories.Fits(scene.BillOfMaterials))throw new InvalidDataException("Single sheet BOM limit exceeded");
            var lineTypes=(LinetypeTable)tr.GetObject(db.LinetypeTableId,OpenMode.ForRead);
            const string contextType="ISO-CONTEXT-DASHED";
            if(!lineTypes.Has(contextType))
            {
                lineTypes.UpgradeOpen();var lt=new LinetypeTableRecord{Name=contextType,AsciiDescription="Adjacent sheet context",PatternLength=tuning.DashLength+tuning.DashGap,NumDashes=2};
                lt.SetDashLengthAt(0,tuning.DashLength);lt.SetDashLengthAt(1,-tuning.DashGap);lineTypes.Add(lt);tr.AddNewlyCreatedDBObject(lt,true);
            }
            var layers=(LayerTable)tr.GetObject(db.LayerTableId,OpenMode.ForRead);
            foreach(var name in new[]{"ISO-GEOMETRY","ISO-ANNOTATION","ISO-DIMENSION","ISO-OVERALL","ISO-WELD","ISO-CONTEXT","ISO-INSULATION"})if(!layers.Has(name))
            {
                if(!layers.IsWriteEnabled)layers.UpgradeOpen();
                var layer=new LayerTableRecord{Name=name,Color=Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci,7)};
                layers.Add(layer);tr.AddNewlyCreatedDBObject(layer,true);
            }
            var weldLayer=(LayerTableRecord)tr.GetObject(layers["ISO-WELD"],OpenMode.ForWrite);
            weldLayer.Color=Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci,1);
            var insulationLayer=(LayerTableRecord)tr.GetObject(layers["ISO-INSULATION"],OpenMode.ForWrite);insulationLayer.LinetypeObjectId=lineTypes[contextType];
            var contextLayer=(LayerTableRecord)tr.GetObject(layers["ISO-CONTEXT"],OpenMode.ForWrite);contextLayer.LinetypeObjectId=lineTypes[contextType];
            var reg=(RegAppTable)tr.GetObject(db.RegAppTableId,OpenMode.ForRead);
            if(!reg.Has("ISOGENERATOR")){reg.UpgradeOpen();var r=new RegAppTableRecord{Name="ISOGENERATOR"};reg.Add(r);tr.AddNewlyCreatedDBObject(r,true);}
            void Add(Entity e,string layer,string source)
            {
                e.Layer=layer;e.ColorIndex=256;e.LineWeight=layer=="ISO-GEOMETRY"?LineWeight.LineWeight050:LineWeight.LineWeight013;ms.AppendEntity(e);tr.AddNewlyCreatedDBObject(e,true);
                if(layer is "ISO-GEOMETRY" or "ISO-CONTEXT" or "ISO-WELD" or "ISO-DIMENSION" or "ISO-OVERALL")dimensionBreakObjects.Add(e.ObjectId);
                using var data=SourceData(source);e.XData=data;
            }
            Point3d Pt(SheetPoint p)=>new(p.X,p.Y,0);
            string plantFolder=Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!,"PLNT3D");
            ObjectId ImportPlantBlock(string file,string name)
            {
                using var source=new Database(false,true);
                source.ReadDwgFile(file,FileOpenMode.OpenForReadAndAllShare,true,null);
                source.CloseInput(true);
                using var sourceTransaction=source.TransactionManager.StartOpenCloseTransaction();
                var blocks=(BlockTable)sourceTransaction.GetObject(source.BlockTableId,OpenMode.ForRead);
                if(!blocks.Has(name))throw new InvalidDataException($"В {file} отсутствует штатный блок {name}.");
                var mapping=new IdMapping();
                source.WblockCloneObjects(new ObjectIdCollection(new[]{blocks[name]}),db.BlockTableId,mapping,DuplicateRecordCloning.Ignore,false);
                return mapping[blocks[name]].Value;
            }
            BlockReference? northReference=null;
            foreach(ObjectId entityId in ms)
            {
                if(tr.GetObject(entityId,OpenMode.ForRead) is not BlockReference candidate)continue;
                var definition=(BlockTableRecord)tr.GetObject(candidate.BlockTableRecord,OpenMode.ForRead);
                if(definition.Cast<ObjectId>().Any(id=>tr.GetObject(id,OpenMode.ForRead) is DBText text&&text.TextString.Trim()=="N")){northReference=candidate;break;}
            }
            if(northReference is not null)
            {
                var center=northReference.Position;
                northReference.UpgradeOpen();
                northReference.Erase();
                if(!scene.BomOnly)
                {
                    var template=Directory.EnumerateDirectories(plantFolder)
                        .Select(locale=>Path.Combine(locale,"ISOProjectTemplates","Metric","Final_A2","Iso.dwt"))
                        .FirstOrDefault(File.Exists)??throw new FileNotFoundException("Не найден штатный шаблон Plant 3D Final_A2.");
                    var north=new BlockReference(center,ImportPlantBlock(template,"NorthArrow"));
                    Add(north,"ISO-ANNOTATION","");
                    var direction=north.DynamicBlockReferencePropertyCollection.Cast<DynamicBlockReferenceProperty>()
                        .First(p=>p.PropertyName=="NorthArrowDirection");
                    var projected=IsometricProjection.Project(new ModelPoint(0,1,0),quarterTurns:scene.QuarterTurns);
                    string english=(projected.Y>0?"Upper":"Lower")+(projected.X>0?"Right":"Left");
                    string russian=(projected.Y>0?"Верхний":"Нижний")+(projected.X>0?"Правый":"Левый");
                    direction.Value=direction.GetAllowedValues().First(v=>v is string value&&(value==english||value==russian));
                }
            }
            double continuationLinetypeScale=tuning.ContinuationDashScale/Math.Max(.0001,db.Ltscale);
            ObjectId flowArrow=ObjectId.Null;
            Trace("strokes");
            foreach(var stroke in SheetComposition.DrawStrokes(scene))
            {
                if(stroke.Role is "elevation-leader" or "elevation-shelf" or "insulation" or "slope-indicator")continue;
                if(stroke.Role=="flow-shaft")continue;
                if(stroke.Role=="flow-arrow")
                {
                    if(flowArrow.IsNull)flowArrow=ImportPlantBlock(Path.Combine(plantFolder,"IsoSymbolStyles.dwg"),"FlowArrow");
                    // The scene stores the arrowhead as left base, tip, right base.
                    var tip=stroke.Points[1];
                    var baseCenter=new SheetPoint((stroke.Points[0].X+stroke.Points[2].X)/2,(stroke.Points[0].Y+stroke.Points[2].Y)/2);
                    double dx=tip.X-baseCenter.X,dy=tip.Y-baseCenter.Y;
                    // The stock solid triangle spans 0.3125 drawing units along +X.
                    double scale=3*Math.Sqrt(dx*dx+dy*dy)/.3125;
                    var arrow=new BlockReference(Pt(tip),flowArrow){Rotation=Math.Atan2(dy,dx),ScaleFactors=new Scale3d(scale)};
                    Add(arrow,"ISO-ANNOTATION",stroke.SourceId);
                    var order=(DrawOrderTable)tr.GetObject(ms.DrawOrderTableId,OpenMode.ForWrite);
                    order.MoveToTop(new ObjectIdCollection(new[]{arrow.ObjectId}));
                    continue;
                }
                if(stroke.Role is "slope-angle-arrow" or "slope-angle-arc" or "slope-projection-dimension" or "bend-dimension")continue;
                var pl=new Polyline();for(int i=0;i<stroke.Points.Count;i++)pl.AddVertexAt(i,new Point2d(stroke.Points[i].X,stroke.Points[i].Y),0,0,0);
                string layer=stroke.Role is "position-leader" or "slope-hatch"?"ISO-ANNOTATION":
                    stroke.Role.StartsWith("context-",StringComparison.Ordinal)?"ISO-CONTEXT":"ISO-GEOMETRY";
                Add(pl,layer,stroke.SourceId);
                if(stroke.Role=="flow-shaft"){pl.ConstantWidth=1;pl.LineWeight=LineWeight.LineWeight050;}
                if(stroke.Role=="continuation-break")pl.LineWeight=LineWeight.LineWeight018;
                if(stroke.Role.StartsWith("context-",StringComparison.Ordinal)){pl.LineWeight=LineWeight.LineWeight013;pl.LinetypeScale=continuationLinetypeScale;pl.Plinegen=true;}

                if(stroke.Role.StartsWith("slope-",StringComparison.Ordinal)||stroke.Role.StartsWith("elevation-",StringComparison.Ordinal))pl.LineWeight=LineWeight.LineWeight013;
                if(stroke.Role=="continuation-break")pl.LineWeight=LineWeight.LineWeight013;
            }
            var valveMasks=new List<ObjectId>();
            if(scene.Symbols is {Count:>0})
            {
                var path=Environment.GetEnvironmentVariable("ISO_SYMBOL_LIBRARY")??throw new InvalidOperationException("ISO_SYMBOL_LIBRARY missing");
                using var library=new SymbolLibrary(path);
                foreach(var glyph in scene.Symbols)
                {
                    if(glyph.Variant=="missing")
                    {
                        foreach(var contour in glyph.LocalContours??Array.Empty<IReadOnlyList<SheetPoint>>())
                        {
                            var shape=new Polyline();int index=0;
                            foreach(var point in contour)shape.AddVertexAt(index++,new Point2d(glyph.Origin.X+glyph.XAxis.X*point.X+glyph.YAxis.X*point.Y,glyph.Origin.Y+glyph.XAxis.Y*point.X+glyph.YAxis.Y*point.Y),0,0,0);
                            Add(shape,glyph.Context?"ISO-CONTEXT":"ISO-GEOMETRY",glyph.SourceId);
                            if(glyph.Context){shape.LinetypeScale=continuationLinetypeScale;shape.Plinegen=true;}
                        }
                        continue;
                    }
                    if(!glyph.Context&&IsoDefinition.Parse(glyph.Definition).Type=="VALVE"&&
                        (glyph.Variant.Length==0||glyph.Variant.StartsWith("end-",StringComparison.Ordinal))&&
                        glyph.LocalBounds is {Count:>0} bounds)
                    {
                        double x0=bounds.Min(p=>p.X),x1=bounds.Max(p=>p.X);
                        double y0=bounds.Min(p=>p.Y),y1=bounds.Max(p=>p.Y);
                        double determinant=glyph.XAxis.X*glyph.YAxis.Y-glyph.XAxis.Y*glyph.YAxis.X;
                        if(x1-x0>1e-8&&y1-y0>1e-8&&Math.Abs(determinant)>1e-8)
                        {
                            Point2d At(double x,double y)=>new(glyph.Origin.X+glyph.XAxis.X*x+glyph.YAxis.X*y,
                                glyph.Origin.Y+glyph.XAxis.Y*x+glyph.YAxis.Y*y);
                            var outline=new Point2dCollection(new[]{At(x0,y0),At(x1,y0),At(x1,y1),At(x0,y1),At(x0,y0)});
                            var mask=new Wipeout();
                            mask.SetFrom(outline,Vector3d.ZAxis);
                            Add(mask,"ISO-ANNOTATION",glyph.SourceId);
                            valveMasks.Add(mask.ObjectId);
                        }
                    }
                    Trace("symbol "+glyph.SourceId+" "+glyph.Block+" "+glyph.Variant);
                    var blockId=library.CreateGlyph(glyph,db,tr);
                    var reference=new BlockReference(Pt(glyph.Origin),blockId);
                    Add(reference,glyph.Context?"ISO-CONTEXT":"ISO-GEOMETRY",glyph.SourceId);

                    if(glyph.Context){reference.LineWeight=LineWeight.LineWeight013;reference.LinetypeScale=continuationLinetypeScale;}
                }
            }
            Trace("annotations");
            foreach(var label in scene.Annotations.Where(label=>label.Role is not ("title" or "status")))
            {
                if(label.Role=="bend-angle"&&label.Text=="90°")continue;
                if(label.Role=="slope-angle"&&label.AngularDimension is {} angle)
                {
                    var vertex=Pt(angle.Vertex);
                    var first=Pt(angle.FirstRay);var second=Pt(angle.SecondRay);
                    if((first-vertex).CrossProduct(second-vertex).Z<0)(first,second)=(second,first);
                    // Leave text at AutoCAD's default angular position so it cannot flip the arc to the reflex sector.
                    var dimension=new Point3AngularDimension(vertex,first,second,Pt(angle.ArcPoint),label.Text,dimensionStyle)
                    {
                        Dimtxt=tuning.DimensionTextHeight,Dimasz=tuning.ArrowSize,Dimtad=0,
                        Dimtih=false,Dimtoh=false,Dimtix=true,Dimatfit=0,Dimse1=true,Dimse2=true,
                        UsingDefaultTextPosition=true
                    };
                    Add(dimension,"ISO-DIMENSION",label.SourceId);
                    dimensionIds.Add(dimension.ObjectId);
                    continue;
                }
                if(label.Role=="slope-projection"&&label.Anchor is {} midpoint)
                {
                    var edge=scene.Strokes.Where(stroke=>stroke.Role=="slope-projection-dimension"&&stroke.SourceId==label.SourceId)
                        .OrderBy(stroke=>Math.Pow((stroke.Points[0].X+stroke.Points[1].X)/2-midpoint.X,2)+
                            Math.Pow((stroke.Points[0].Y+stroke.Points[1].Y)/2-midpoint.Y,2)).First();
                    var first=edge.Points[0];var second=edge.Points[1];
                    if(PipeDimension.ReverseTextAxis(second.X-first.X,second.Y-first.Y))(first,second)=(second,first);
                    var normal=new Vector3d(-Math.Sin(label.Rotation),Math.Cos(label.Rotation),0);
                    var text=Pt(label.Position)+new Vector3d(Math.Cos(label.Rotation),Math.Sin(label.Rotation),0)*
                        (label.Text.Length*1.05*tuning.DimensionTextHeight/3.5);
                    var offset=text-Pt(midpoint);
                    double side=offset.DotProduct(normal)>=0?1:-1;
                    var dimension=new AlignedDimension(Pt(first),Pt(second),text-normal*(side*tuning.TextLift),label.Text,dimensionStyle)
                    {
                        Dimtxt=tuning.DimensionTextHeight,Dimasz=tuning.ArrowSize,Dimtad=1,Dimtmove=2,Dimtix=true,
                        Dimexo=tuning.ExtensionOffset,Dimexe=tuning.ExtensionOvershoot,UsingDefaultTextPosition=false,TextPosition=text
                    };
                    Add(dimension,"ISO-DIMENSION",label.SourceId);
                    dimensionIds.Add(dimension.ObjectId);
                    continue;
                }
                if(label.Role=="slope-value"&&scene.Strokes.FirstOrDefault(stroke=>stroke.Role=="slope-indicator"&&stroke.SourceId==label.SourceId) is {} indicator)
                {
                    if(!bt.IsWriteEnabled)bt.UpgradeOpen();
                    var block=new BlockTableRecord{Name="*U"};
                    var blockId=bt.Add(block);
                    tr.AddNewlyCreatedDBObject(block,true);
                    var baselineA=indicator.Points[0];
                    var baselineB=indicator.Points[1];
                    double centerX=(baselineA.X+baselineB.X)/2,centerY=(baselineA.Y+baselineB.Y)/2;
                    var symbol=new Polyline{ColorIndex=0,LineWeight=LineWeight.ByBlock};
                    for(int i=0;i<indicator.Points.Count;i++)
                    {
                        var point=indicator.Points[i];
                        symbol.AddVertexAt(i,new Point2d(point.X-centerX,point.Y-centerY),0,0,0);
                    }
                    block.AppendEntity(symbol);tr.AddNewlyCreatedDBObject(symbol,true);
                    var direction=new Vector2d(Math.Cos(label.Rotation),Math.Sin(label.Rotation));
                    var normal=new Vector2d(-direction.Y,direction.X);
                    var value=new DBText{TextString=label.Text,TextStyleId=gostStyle,Height=tuning.LeaderTextHeight,
                        Rotation=label.Rotation,ColorIndex=0,LineWeight=LineWeight.ByBlock,
                        HorizontalMode=TextHorizontalMode.TextCenter,
                        AlignmentPoint=new Point3d(normal.X*2.8,normal.Y*2.8,0)};
                    block.AppendEntity(value);tr.AddNewlyCreatedDBObject(value,true);value.AdjustAlignment(db);
                    var origin=new SheetPoint(label.Position.X-normal.X*2.8,label.Position.Y-normal.Y*2.8);
                    Add(new BlockReference(Pt(origin),blockId),"ISO-ANNOTATION",label.SourceId);
                    continue;
                }
                if(label.Role is ("elevation" or "valve-tag" or "instrument-tag" or "external-line" or "support-type" or "continuation")&&label.Anchor is {} anchor&&label.LeaderKnee is {} knee)
                {
                    var leader=new MLeader();leader.SetDatabaseDefaults(db);leader.MLeaderStyle=leaderStyleId;
                    leader.ContentType=ContentType.MTextContent;leader.TextStyleId=gostStyle;leader.TextHeight=tuning.LeaderTextHeight;
                    leader.ArrowSymbolId=arrowId;leader.ArrowSize=tuning.ArrowSize;leader.EnableLanding=true;leader.EnableDogleg=true;leader.DoglegLength=tuning.LeaderLanding;leader.LandingGap=1;
                    bool external=label.Role=="external-line";
                    if(external)
                    {
                        leader.SetTextAttachmentType(TextAttachmentType.AttachmentBottomOfTopLine,LeaderDirectionType.LeftLeader);
                        leader.SetTextAttachmentType(TextAttachmentType.AttachmentBottomOfTopLine,LeaderDirectionType.RightLeader);
                    }
                    var textLines=label.Text.Split('\n');
                    var contentLines=external?textLines:textLines.Take(1);
                    using var content=new MText{Contents=string.Join(@"\P",contentLines.Select(text=>text.Replace("\\","\\\\").Replace("{","\\{").Replace("}","\\}"))),TextStyleId=gostStyle,TextHeight=tuning.LeaderTextHeight,Location=Pt(label.Position),Attachment=AttachmentPoint.BottomLeft};
                    leader.MText=content;
                    int cluster=leader.AddLeader();int line=leader.AddLeaderLine(cluster);
                    leader.AddFirstVertex(line,Pt(anchor));leader.AddLastVertex(line,Pt(knee));
                    leader.SetDogleg(cluster,new Vector3d(label.Position.X>=knee.X?1:-1,0,0));
                    // MLeader.TextLocation uses the top of the MText box, unlike the DBText baseline used by the planner.
                    leader.TextLocation=Pt(new SheetPoint(label.Position.X,label.Position.Y+4.8));
                    Add(leader,"ISO-ANNOTATION",label.SourceId);
                    leaderIds.Add(leader.ObjectId);
                    var following=external?Array.Empty<string>():textLines.Skip(1).ToArray();
                    for(int i=0;i<following.Length;i++)
                    {
                        var under=new DBText{Position=Pt(new SheetPoint(label.Position.X,label.Position.Y-3.3-i*4.8)),Height=tuning.LeaderTextHeight,TextString=following[i],TextStyleId=gostStyle,Oblique=Math.PI/12};
                        Add(under,"ISO-ANNOTATION",label.SourceId);
                    }
                }
                else
                {
                    var text=new DBText{Position=Pt(label.Position),Height=tuning.LeaderTextHeight,TextString=label.Text,TextStyleId=gostStyle,Rotation=label.Rotation,Oblique=label.Role=="slope-value"?label.Rotation:Math.PI/12};
                    Add(text,"ISO-ANNOTATION",label.SourceId);
                }
            }
            Trace("dimensions");
            foreach(var d in scene.Dimensions)
            {
                double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,len=Math.Sqrt(dx*dx+dy*dy);
                if(len<.01)continue;
                var mid=d.DimensionLinePoint is {} dp?Pt(dp):new Point3d((d.Start.X+d.End.X)/2-dy/len*7,(d.Start.Y+d.End.Y)/2+dx/len*7,0);
                bool reverse=PipeDimension.ReverseTextAxis(dx,dy);
                var dim=new AlignedDimension(Pt(reverse?d.End:d.Start),Pt(reverse?d.Start:d.End),mid,d.Label,dimensionStyle)
                    {Dimtxt=tuning.DimensionTextHeight,Dimasz=tuning.ArrowSize,Dimtsz=0,Dimtad=1,Dimexo=tuning.ExtensionOffset,Dimexe=tuning.ExtensionOvershoot};
                if(PipeDimension.IsVerticalTextAxis(dx,dy))dim.TextRotation=Math.PI/2;
                if(d.ExtensionDirection is {} ext)
                {
                    double angle=Math.Atan2(ext.Y,ext.X)-Math.Atan2(dy,dx);
                    angle=(angle%Math.PI+Math.PI)%Math.PI;dim.Oblique=angle;
                    // Isometric glyph stems follow the extension axis, rather than a fixed 15-degree slant.
                    double normalDot=(-dy*ext.X+dx*ext.Y)/len;
                    double axisDot=(dx*ext.X+dy*ext.Y)/len;
                    if(Math.Abs(normalDot)>1e-6)
                    {
                        double textAngle=Math.Atan(axisDot/normalDot)*180/Math.PI;
                        // On skewed runs a near-parallel witness can make the glyphs unreadable.
                        if(Math.Abs(textAngle)>45)textAngle=15;
                        dim.DimensionText="{\\Q"+textAngle.ToString("0.######",CultureInfo.InvariantCulture)+";"+d.Label+"}";
                    }
                }
                dim.Dimtmove=2;dim.Dimtix=true;dim.Dimjust=0;dim.Dimatfit=3;dim.UsingDefaultTextPosition=false;
                if(d.TextPosition is {} tp)
                {
                    // Explicit TextPosition is the ink center; the planner already places it above the line.
                    dim.TextPosition=Pt(tp);
                }
                Add(dim,d.Role=="overall"?"ISO-OVERALL":"ISO-DIMENSION",d.SourceId);
                dimensionIds.Add(dim.ObjectId);
                using(var metadata=dim.XData)
                {
                    var values=metadata.AsArray().Concat(new[]{new TypedValue(1000,d.Role),new TypedValue(1000,d.ChainId),new TypedValue(1040,d.Millimeters)}).ToArray();
                    dim.XData=new ResultBuffer(values);
                }
            }
            if(scene.Welds is {Count:>0})
            {
                var libraryPath=Environment.GetEnvironmentVariable("ISO_SYMBOL_LIBRARY")??throw new InvalidOperationException("ISO_SYMBOL_LIBRARY missing");
                using var sourceDb=new Database(false,true);sourceDb.ReadDwgFile(libraryPath,FileOpenMode.OpenForReadAndAllShare,true,null);sourceDb.CloseInput(true);
                using var sourceTransaction=sourceDb.TransactionManager.StartOpenCloseTransaction();
                var sourceBlocks=(BlockTable)sourceTransaction.GetObject(sourceDb.BlockTableId,OpenMode.ForRead);
                if(!sourceBlocks.Has("Buttweld"))throw new InvalidDataException("Buttweld symbol missing in library");
                var mapping=new IdMapping();sourceDb.WblockCloneObjects(new ObjectIdCollection(new[]{sourceBlocks["Buttweld"]}),db.BlockTableId,mapping,DuplicateRecordCloning.Ignore,false);
                var blockId=mapping[sourceBlocks["Buttweld"]].Value;

                var weldBlock=(BlockTableRecord)tr.GetObject(blockId,OpenMode.ForRead);
                foreach(ObjectId id in weldBlock)
                    if(tr.GetObject(id,OpenMode.ForWrite) is Entity element)element.ColorIndex=0;
                foreach(var weld in scene.Welds)
                {
                    var reference=new BlockReference(Pt(weld.Position),blockId){ScaleFactors=new Scale3d(15)};
                    Add(reference,"ISO-WELD",weld.SourceId);reference.LineWeight=LineWeight.LineWeight050;
                    var values=reference.XData.AsArray().Concat(new[]{new TypedValue(1000,"weld"),new TypedValue(1000,weld.FirstHandle+":"+weld.FirstPort),new TypedValue(1000,weld.SecondHandle+":"+weld.SecondPort),new TypedValue(1000,weld.JointType)}).ToArray();
                    reference.XData=new ResultBuffer(values);
                }
            }
            table.UpgradeOpen();if(scene.BomOnly)table.Cells[0,0].TextString="Спецификация трубопровода (продолжение)";table.BreakEnabled=false;if(table.Rows.Count>2)table.DeleteRows(2,table.Rows.Count-2);
            int displayRows=scene.BillOfMaterials.Count+scene.BillOfMaterials.Select(r=>r.Category).Distinct().Count();
            if(displayRows>0)table.InsertRows(2,BomCategories.ItemHeight,displayRows);
            string Quantity(BomRow row)=>row.QuantityText+" "+row.Unit;
            double quantityWidth=table.Columns[3].Width;
            foreach(var row in scene.BillOfMaterials)
            {
                using var text=new DBText{TextString=Quantity(row),TextStyleId=gostStyle,
                    Height=BomCategories.TextHeight,Oblique=Math.PI/12};
                var extents=text.GeometricExtents;
                quantityWidth=Math.Max(quantityWidth,Math.Ceiling(extents.MaxPoint.X-extents.MinPoint.X+4));
            }
            double extraWidth=quantityWidth-table.Columns[3].Width;
            if(extraWidth>0)
            {
                table.Columns[2].Width-=extraWidth;
                table.Columns[3].Width=quantityWidth;
            }
            int tableRow=2;
            foreach(var category in scene.BillOfMaterials.GroupBy(r=>r.Category).OrderBy(g=>g.Key))
            {
                table.Rows[tableRow].Height=BomCategories.HeadingHeight;
                for(int j=0;j<5;j++){table.Cells[tableRow,j].TextString="";table.Cells[tableRow,j].TextHeight=BomCategories.TextHeight;table.Cells[tableRow,j].TextStyleId=gostStyle;}
                table.Cells[tableRow,2].TextString="\\L"+BomCategories.Title(category.Key)+"\\l";
                table.Cells[tableRow,2].Alignment=CellAlignment.MiddleCenter;
                tableRow++;
                foreach(var row in category)
                {
                    string[] values={row.Position.ToString(CultureInfo.InvariantCulture),row.Designation,row.Description,Quantity(row),""};
                    for(int j=0;j<5;j++){table.Cells[tableRow,j].TextString=values[j];table.Cells[tableRow,j].TextHeight=BomCategories.TextHeight;table.Cells[tableRow,j].TextStyleId=gostStyle;}
                    tableRow++;
                }
            }
            for(int i=2;i<table.Rows.Count;i++)for(int j=0;j<table.Columns.Count;j++)
            {
                var borders=table.Cells[i,j].Borders;
                foreach(var border in new[]{borders.Top,borders.Bottom,borders.Left,borders.Right})
                {border.IsVisible=true;border.Color=Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci,7);border.LineWeight=LineWeight.LineWeight013;}
            }
            for(int i=0;i<2;i++)for(int j=0;j<5;j++){table.Cells[i,j].TextStyleId=gostStyle;table.Cells[i,j].TextHeight=BomCategories.TextHeight;}
            table.GenerateLayout();table.RecomputeTableBlock(true);
            foreach(DBDictionaryEntry entry in (DBDictionary)tr.GetObject(db.LayoutDictionaryId,OpenMode.ForRead))
            {
                var layout=(Layout)tr.GetObject(entry.Value,OpenMode.ForRead);if(layout.ModelType)continue;
                var paper=(BlockTableRecord)tr.GetObject(layout.BlockTableRecordId,OpenMode.ForRead);
                foreach(ObjectId id in paper)
                {
                    if(tr.GetObject(id,OpenMode.ForRead) is not MText text)continue;
                    string? value=text.Handle.ToString().ToUpperInvariant() switch
                    {
                        "A13C"=>string.Join(@"\P",notes.Select(EscapeMText)),
                        "A141"=>EscapeMText(stampDocument),
                        "A146"=>stampSheetNumber,
                        _=>null
                    };
                    if(value is not null){text.UpgradeOpen();text.Contents=value;}
                }
            }
            if(valveMasks.Count>0)
            {
                Autodesk.AutoCAD.ApplicationServices.Core.Application.SetSystemVariable("WIPEOUTFRAME",0);
                var order=(DrawOrderTable)tr.GetObject(ms.DrawOrderTableId,OpenMode.ForWrite);
                if(dimensionIds.Count>0)
                    order.MoveBelow(new ObjectIdCollection(dimensionIds.ToArray()),valveMasks[0]);
            }
            Trace("commit");
            tr.Commit();
        }
        if(dimensionIds.Count>0)
        {
            Trace("dimension breaks");
            using var selected=Autodesk.AutoCAD.EditorInput.SelectionSet.FromObjectIds(dimensionIds.ToArray());
            using var cutters=Autodesk.AutoCAD.EditorInput.SelectionSet.FromObjectIds(dimensionBreakObjects.ToArray());
            bool wasModel=db.TileMode;
            void SetLeadersErased(bool erased)
            {
                using var transaction=db.TransactionManager.StartTransaction();
                foreach(var id in leaderIds)
                    transaction.GetObject(id,OpenMode.ForWrite,openErased:!erased).Erase(erased);
                transaction.Commit();
            }
            SetLeadersErased(true);
            try{db.TileMode=true;doc.Editor.Command("_.DIMBREAK","_Multiple",selected,"",cutters,"");}
            finally
            {
                db.TileMode=wasModel;
                SetLeadersErased(false);
            }
        }
        // DIMBREAK runs before insulation entities exist in model space.
        using(var tr=db.TransactionManager.StartTransaction())
        {
            var ms=(BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db),OpenMode.ForWrite);
            var order=(DrawOrderTable)tr.GetObject(ms.DrawOrderTableId,OpenMode.ForWrite);
            var insulationIds=new ObjectIdCollection();
            foreach(var stroke in scene.Strokes.Where(stroke=>stroke.Role=="insulation"))
            {
                var contour=new Polyline();
                for(int i=0;i<stroke.Points.Count;i++)
                    contour.AddVertexAt(i,new Point2d(stroke.Points[i].X,stroke.Points[i].Y),0,0,0);
                contour.SetDatabaseDefaults(db);
                contour.Layer="ISO-INSULATION";
                contour.ColorIndex=256;
                contour.LineWeight=LineWeight.LineWeight013;
                contour.LinetypeScale=1/Math.Max(.0001,db.Ltscale);
                contour.Plinegen=true;
                insulationIds.Add(ms.AppendEntity(contour));
                tr.AddNewlyCreatedDBObject(contour,true);
                using var data=SourceData(stroke.SourceId);
                contour.XData=data;
            }
            if(insulationIds.Count>0)order.MoveToBottom(insulationIds);
            tr.Commit();
        }
        Trace("save");
        db.SaveAs(output,DwgVersion.Current);
        doc.Editor.WriteMessage($"\nISORENDER: {output}\n");
    }
}
