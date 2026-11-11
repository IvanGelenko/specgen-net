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
        scene=SheetComposition.Center(scene);
        var doc=App.DocumentManager.MdiActiveDocument;var db=doc.Database;
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
            if(textStyles.Has("Standard_m"))gostStyle=textStyles["Standard_m"];
            else {textStyles.UpgradeOpen();var style=new TextStyleTableRecord{Name="Standard_m"};gostStyle=textStyles.Add(style);tr.AddNewlyCreatedDBObject(style,true);}
            var gost=(TextStyleTableRecord)tr.GetObject(gostStyle,OpenMode.ForWrite);
            gost.FileName="GOST_B.TTF";gost.BigFontFileName="";gost.TextSize=3.5;gost.XScale=1;gost.ObliquingAngle=Math.PI/12;
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
            var dimStyle=(DimStyleTableRecord)tr.GetObject(dimensionStyle,OpenMode.ForWrite);dimStyle.Dimtxsty=gostStyle;dimStyle.Dimtxt=3.5;dimStyle.Dimjust=0;dimStyle.Dimatfit=3;dimStyle.Dimtmove=2;dimStyle.Dimblk=arrowId;dimStyle.Dimasz=2.5;dimStyle.Dimtsz=0;dimStyle.Dimsah=false;dimStyle.Dimscale=1;dimStyle.Dimtad=1;dimStyle.Dimexo=1;dimStyle.Dimexe=1;dimStyle.Dimdli=7;
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
            leaderStyle.ContentType=ContentType.MTextContent;leaderStyle.TextStyleId=gostStyle;leaderStyle.TextHeight=3.5;
            leaderStyle.ArrowSymbolId=arrowId;leaderStyle.ArrowSize=2.5;leaderStyle.EnableLanding=true;leaderStyle.EnableDogleg=true;
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
                lineTypes.UpgradeOpen();var lt=new LinetypeTableRecord{Name=contextType,AsciiDescription="Adjacent sheet context",PatternLength=3,NumDashes=2};
                lt.SetDashLengthAt(0,2);lt.SetDashLengthAt(1,-1);lineTypes.Add(lt);tr.AddNewlyCreatedDBObject(lt,true);
            }
            var layers=(LayerTable)tr.GetObject(db.LayerTableId,OpenMode.ForRead);
            foreach(var name in new[]{"ISO-GEOMETRY","ISO-ANNOTATION","ISO-DIMENSION","ISO-OVERALL","ISO-WELD","ISO-CONTEXT","ISO-INSULATION"})if(!layers.Has(name))
            {
                if(!layers.IsWriteEnabled)layers.UpgradeOpen();
                var layer=new LayerTableRecord{Name=name,Color=Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci,7)};
                layers.Add(layer);tr.AddNewlyCreatedDBObject(layer,true);
            }
            var insulationLayer=(LayerTableRecord)tr.GetObject(layers["ISO-INSULATION"],OpenMode.ForWrite);insulationLayer.LinetypeObjectId=lineTypes[contextType];
            var contextLayer=(LayerTableRecord)tr.GetObject(layers["ISO-CONTEXT"],OpenMode.ForWrite);contextLayer.LinetypeObjectId=lineTypes[contextType];
            var reg=(RegAppTable)tr.GetObject(db.RegAppTableId,OpenMode.ForRead);
            if(!reg.Has("ISOGENERATOR")){reg.UpgradeOpen();var r=new RegAppTableRecord{Name="ISOGENERATOR"};reg.Add(r);tr.AddNewlyCreatedDBObject(r,true);}
            void Add(Entity e,string layer,string source)
            {
                e.Layer=layer;e.ColorIndex=256;e.LineWeight=layer=="ISO-GEOMETRY"?LineWeight.LineWeight035:LineWeight.LineWeight013;ms.AppendEntity(e);tr.AddNewlyCreatedDBObject(e,true);
                e.XData=new ResultBuffer(new TypedValue(1001,"ISOGENERATOR"),new TypedValue(1000,scene.OriginalHandles?.GetValueOrDefault(source,source)??source),new TypedValue(1000,scene.SourceDrawing),new TypedValue(1000,scene.SourceGuids?.GetValueOrDefault(source,"")??""));
            }
            Point3d Pt(SheetPoint p)=>new(p.X,p.Y,0);
            // Keep N upright while the direction indicator follows the same model rotation as the drawing.
            BlockReference? northReference=null;
            foreach(ObjectId entityId in ms)
            {
                if(tr.GetObject(entityId,OpenMode.ForRead) is not BlockReference candidate)continue;
                var definition=(BlockTableRecord)tr.GetObject(candidate.BlockTableRecord,OpenMode.ForRead);
                if(definition.Cast<ObjectId>().Any(id=>tr.GetObject(id,OpenMode.ForRead) is DBText text&&text.TextString.Trim()=="N")){northReference=candidate;break;}
            }
            if(scene.BomOnly&&northReference is not null){northReference.UpgradeOpen();northReference.Erase();northReference=null;}
            if(northReference is not null)
            {
                var center=northReference.Position;northReference.UpgradeOpen();northReference.Erase();
                Add(new Ellipse(center,Vector3d.ZAxis,new Vector3d(10,0,0),.6,0,2*Math.PI),"ISO-ANNOTATION","");
                var northText=new DBText{Position=center,HorizontalMode=TextHorizontalMode.TextCenter,VerticalMode=TextVerticalMode.TextVerticalMid,AlignmentPoint=center,TextString="N",Height=8,TextStyleId=gostStyle,Oblique=0};
                Add(northText,"ISO-ANNOTATION","");northText.AdjustAlignment(db);
                var projectedNorth=IsometricProjection.Project(new ModelPoint(0,1,0),quarterTurns:scene.QuarterTurns);
                double length=Math.Sqrt(projectedNorth.X*projectedNorth.X+projectedNorth.Y*projectedNorth.Y),ux=projectedNorth.X/length,uy=projectedNorth.Y/length;
                double radius=1/Math.Sqrt(ux*ux/100+uy*uy/36);
                Point3d Along(double distance,double offset=0)=>new(center.X+ux*distance-uy*offset,center.Y+uy*distance+ux*offset,0);
                Add(new Line(Along(radius),Along(13)),"ISO-ANNOTATION","");
                double half=3*Math.Tan(Math.PI/18);
                Add(new Solid(Along(16),Along(13,half),Along(13,-half),Along(13,-half)),"ISO-ANNOTATION","");
            }
            var fittingSources=(scene.FittingSourceIds??scene.BillOfMaterials.Where(r=>r.Category==BomCategory.Fittings).SelectMany(r=>r.Handles).ToArray()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Trace("strokes");
            foreach(var stroke in scene.Strokes)
            {
                if(stroke.Role is "elevation-leader" or "elevation-shelf")continue;
                if(stroke.Role=="slope-angle-arrow")
                {
                    var arrow=new Solid(Pt(stroke.Points[0]),Pt(stroke.Points[1]),Pt(stroke.Points[2]),Pt(stroke.Points[2]));
                    Add(arrow,"ISO-ANNOTATION",stroke.SourceId);continue;
                }
                var pl=new Polyline();for(int i=0;i<stroke.Points.Count;i++)pl.AddVertexAt(i,new Point2d(stroke.Points[i].X,stroke.Points[i].Y),0,0,0);
                Add(pl,stroke.Role=="insulation"?"ISO-INSULATION":stroke.Role.StartsWith("context-",StringComparison.Ordinal)?"ISO-CONTEXT":"ISO-GEOMETRY",stroke.SourceId);
                if(stroke.Role=="insulation"){pl.LineWeight=LineWeight.LineWeight013;pl.LinetypeScale=1/Math.Max(.0001,db.Ltscale);pl.Plinegen=true;}
                if(stroke.Role.StartsWith("context-",StringComparison.Ordinal)){pl.LineWeight=LineWeight.LineWeight013;pl.LinetypeScale=1/Math.Max(.0001,db.Ltscale);}
                if(stroke.Role=="pipe"||(fittingSources.Contains(stroke.SourceId)&&stroke.Role!="insulation"&&!stroke.Role.StartsWith("context-",StringComparison.Ordinal)))pl.LineWeight=LineWeight.LineWeight050;
                else if(stroke.Role.StartsWith("slope-",StringComparison.Ordinal)||stroke.Role.StartsWith("elevation-",StringComparison.Ordinal))pl.LineWeight=LineWeight.LineWeight013;
            }
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
                        }
                        continue;
                    }
                    Trace("symbol "+glyph.SourceId+" "+glyph.Block+" "+glyph.Variant);
                    var blockId=library.CreateGlyph(glyph,db,tr);
                    var reference=new BlockReference(Pt(glyph.Origin),blockId);
                    Add(reference,glyph.Context?"ISO-CONTEXT":"ISO-GEOMETRY",glyph.SourceId);
                    if(fittingSources.Contains(glyph.SourceId))reference.LineWeight=LineWeight.LineWeight050;
                    if(glyph.Context){reference.LineWeight=LineWeight.LineWeight013;reference.LinetypeScale=1/Math.Max(.0001,db.Ltscale);}
                }
            }
            Trace("annotations");
            foreach(var label in scene.Annotations)
            {
                if(label.Role is ("elevation" or "valve-tag" or "support-type" or "continuation")&&label.Anchor is {} anchor&&label.LeaderKnee is {} knee)
                {
                    var leader=new MLeader();leader.SetDatabaseDefaults(db);leader.MLeaderStyle=leaderStyleId;
                    leader.ContentType=ContentType.MTextContent;leader.TextStyleId=gostStyle;leader.TextHeight=3.5;
                    leader.ArrowSymbolId=arrowId;leader.ArrowSize=2.5;leader.EnableLanding=true;leader.EnableDogleg=true;leader.DoglegLength=1;leader.LandingGap=1;
                    using var content=new MText{Contents=label.Text.Replace("\\","\\\\").Replace("{","\\{").Replace("}","\\}"),TextStyleId=gostStyle,TextHeight=3.5,Location=Pt(label.Position),Attachment=AttachmentPoint.BottomLeft};
                    leader.MText=content;
                    int cluster=leader.AddLeader();int line=leader.AddLeaderLine(cluster);
                    leader.AddFirstVertex(line,Pt(anchor));leader.AddLastVertex(line,Pt(knee));
                    leader.SetDogleg(cluster,new Vector3d(knee.X>=anchor.X?1:-1,0,0));
                    // MLeader.TextLocation uses the top of the MText box, unlike the DBText baseline used by the planner.
                    leader.TextLocation=Pt(new SheetPoint(label.Position.X,label.Position.Y+4.8));
                    Add(leader,"ISO-ANNOTATION",label.SourceId);
                }
                else
                {
                    var text=new DBText{Position=Pt(label.Position),Height=3.5,TextString=label.Text,TextStyleId=gostStyle,Oblique=Math.PI/12};
                    Add(text,"ISO-ANNOTATION",label.SourceId);
                }
            }
            Trace("dimensions");
            foreach(var d in scene.Dimensions)
            {
                double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,len=Math.Sqrt(dx*dx+dy*dy);
                if(len<.01)continue;
                var mid=d.DimensionLinePoint is {} dp?Pt(dp):new Point3d((d.Start.X+d.End.X)/2-dy/len*7,(d.Start.Y+d.End.Y)/2+dx/len*7,0);
                var dim=new AlignedDimension(Pt(d.Start),Pt(d.End),mid,d.Label,dimensionStyle)
                    {Dimtxt=3.5,Dimasz=2.5,Dimtsz=0,Dimtad=1,Dimexo=1,Dimexe=1};
                if(d.ExtensionDirection is {} ext)
                {
                    double angle=Math.Atan2(ext.Y,ext.X)-Math.Atan2(dy,dx);
                    angle=(angle%Math.PI+Math.PI)%Math.PI;dim.Oblique=angle;
                }
                dim.Dimtmove=2;dim.Dimtix=true;dim.Dimjust=0;dim.Dimatfit=3;dim.UsingDefaultTextPosition=false;
                if(d.TextPosition is {} tp)
                {
                    // Explicit TextPosition is the ink center; the planner already places it above the line.
                    dim.TextPosition=Pt(tp);
                }
                Add(dim,d.Role=="overall"?"ISO-OVERALL":"ISO-DIMENSION",d.SourceId);
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
                    string[] values={row.Position.ToString(CultureInfo.InvariantCulture),row.Designation,row.Description,row.QuantityText+" "+row.Unit,""};
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
                        "A13C"=>"1. Контрольный лист. Не для монтажа.\\P2. Цепочки и габариты рассчитаны по модели.\\P3. "+(scene.ExcludeFlangeHardware?"Фланцы, прокладки и крепёж исключены из спецификации по настройке.":"Прокладки и крепёж включены в спецификацию по данным соединителей Plant."),
                        "A141"=>scene.Line+(int.TryParse(Environment.GetEnvironmentVariable("ISO_SHEET_COUNT"),out int count)&&count>1?"(L"+Environment.GetEnvironmentVariable("ISO_SHEET_NUMBER")+")":""), "A146"=>Environment.GetEnvironmentVariable("ISO_SHEET_NUMBER")??"1",_=>null
                    };
                    if(value is not null){text.UpgradeOpen();text.Contents=value;text.TextStyleId=gostStyle;text.TextHeight=3.5;}
                }
            }
            Trace("commit");
            tr.Commit();
        }
        Trace("save");
        db.SaveAs(output,DwgVersion.Current);
        doc.Editor.WriteMessage($"\nISORENDER: {output}\n");
    }
}
