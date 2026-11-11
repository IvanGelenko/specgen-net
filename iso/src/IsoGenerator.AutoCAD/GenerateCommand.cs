using System.IO;
using System.Text;
using System.Diagnostics;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PnP3dObjects;
using IsoGenerator.Core;
using App=Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Connection=IsoGenerator.Core.Connection;
using ModelPart=IsoGenerator.Core.Part;
using PlantPart=Autodesk.ProcessPower.PnP3dObjects.Part;
using PlantPort=Autodesk.ProcessPower.PnP3dObjects.Port;
using Dialog=System.Windows.Forms.SaveFileDialog;
[assembly: CommandClass(typeof(IsoGenerator.AutoCAD.GenerateCommand))]
namespace IsoGenerator.AutoCAD;

public sealed class GenerateCommand
{
    // All source API reads run on AutoCAD's main thread in one read-only transaction.
    // Planning and rendering contain no AI calls or external network dependencies.
    [CommandMethod("ISOGENERATE",CommandFlags.Modal)]
    public void Generate()=>App.DocumentManager.MdiActiveDocument?.SendStringToExecute("PSISOMETRIC ",true,false,false);

    static int busy;
    public void GenerateForLine(string line,IReadOnlyCollection<string> handles,string outputDirectory,IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>>? propertyHints=null,string? projectIsoDirectory=null,Action<string>? onCompleted=null,int quarterTurns=0,bool excludeFlangeHardware=false,bool ignoreBreakPoints=false,string? templatePath=null,double symbolScale=10)
    {
        var doc=App.DocumentManager.MdiActiveDocument;if(doc is null)return;
        if(Interlocked.CompareExchange(ref busy,1,0)!=0){System.Windows.Forms.MessageBox.Show("Создание изометрии уже выполняется. Дождитесь завершения или отмените его в окне прогресса.","Изометрии");return;}
        IsoProgressWindow? view=null;string job="";
        var dispatcher=System.Windows.Threading.Dispatcher.CurrentDispatcher;
        try
        {
            view=IsoProgressWindow.Start(line,App.MainWindow.Handle);var token=view.Token;
            if(!double.IsFinite(symbolScale)||symbolScale<=0)throw new InvalidDataException("Масштаб элементов должен быть положительным числом.");
            if(string.IsNullOrWhiteSpace(templatePath)||!File.Exists(templatePath))throw new FileNotFoundException("Выберите существующую локальную копию шаблона изометрии.",templatePath);
            templatePath=Path.GetFullPath(templatePath);
            using(var templateDb=new Database(false,true))
            {
                templateDb.ReadDwgFile(templatePath,FileOpenMode.OpenForReadAndAllShare,true,null);templateDb.CloseInput(true);
                if(templateDb.Insunits!=UnitsValue.Millimeters)throw new InvalidDataException("Шаблон должен быть в миллиметрах.");
                using var templateTr=templateDb.TransactionManager.StartOpenCloseTransaction();
                var blocks=(BlockTable)templateTr.GetObject(templateDb.BlockTableId,OpenMode.ForRead);
                if(!blocks.Has("Рамка_А2г"))throw new InvalidDataException("Выбрана другая структура шаблона. Укажите локальную копию шаблона изометрии SpecGen А2.");
            }
            _=IsoEnvironmentChecks.PdfDevice();
            var snapshot=Capture(doc.Database,doc.Name,ObjectId.Null,line,handles,propertyHints,cancellationToken:token,report:count=>view.Report("Этап 1 из 6 — чтение модели",$"Прочитано объектов: {count}"));
            token.ThrowIfCancellationRequested();
            if(string.IsNullOrWhiteSpace(projectIsoDirectory))throw new InvalidOperationException("Не определена папка Isometric активного проекта.");
            string folder=Path.GetFullPath(outputDirectory);job=Path.Combine(folder,".iso-jobs",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(job);
            string library=Path.Combine(job,"IsoSymbolStyles.dwg"),mapping=Path.Combine(job,"IsoSkeyAcadBlockMap.xml");
            File.Copy(Path.Combine(projectIsoDirectory,"IsoSymbolStyles.dwg"),library);File.Copy(Path.Combine(projectIsoDirectory,"IsoSkeyAcadBlockMap.xml"),mapping);
            File.WriteAllText(Path.Combine(job,"snapshot.json"),JsonSerializer.Serialize(snapshot,new JsonSerializerOptions{WriteIndented=true}));
            File.WriteAllText(Path.Combine(job,"run-settings.json"),JsonSerializer.Serialize(new{line,quarterTurns,excludeFlangeHardware,ignoreBreakPoints,templatePath,symbolScale,startedUtc=DateTimeOffset.UtcNow,sourceDrawing=snapshot.SourceDrawing,sourceObjects=snapshot.Parts.Count},new JsonSerializerOptions{WriteIndented=true}));
            view.Report("Этап 2 из 6 — подготовка обозначений","Чтение библиотеки символов проекта…");
            string jobTemplate=Path.Combine(job,"iso_template.dwg");File.Copy(templatePath,jobTemplate);
            var catalog=SymbolCatalogCommands.Read(library,token,count=>view.Report("Этап 2 из 6 — подготовка обозначений",$"Прочитано блоков: {count}")) with{SymbolScale=symbolScale,Style=null};
            string assembly=typeof(GenerateCommand).Assembly.Location;
            string engine=Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!,"accoreconsole.exe");
            if(!File.Exists(engine))throw new FileNotFoundException("Не найден AutoCAD Core Console",engine);
            var progress=view;
            _=Task.Run(async()=>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    File.WriteAllText(Path.Combine(job,"symbols.json"),JsonSerializer.Serialize(catalog));
                    var map=new IsoSymbolMap(File.ReadAllText(mapping),catalog);
                    var warnings=SnapshotValidation.Validate(snapshot).Warnings.Where(w=>w.StartsWith("Нет соединения:",StringComparison.Ordinal)).ToArray();
                    if(warnings.Length>0)File.WriteAllLines(Path.Combine(job,"warnings.txt"),warnings);
                    progress.Report("Этап 3 из 6 — расчёт компоновки","Подбор размеров участков и границ листов…");
                    var observer=new PlanningObserver(p=>progress.Report("Этап 3 из 6 — расчёт компоновки",p.Detail+$". Проверено вариантов: {p.Variants}"));
                    var sheets=SheetSetPlanner.Build(snapshot,symbolMap:map,quarterTurns:quarterTurns,excludeFlangeHardware:excludeFlangeHardware,cancellationToken:token,progress:observer,ignoreBreakPoints:ignoreBreakPoints);
                    token.ThrowIfCancellationRequested();
                    var names=DrawingFileNames.OutputSet(folder,line,sheets.Count);bool overwrite=false;
                    if(names.Drawings.Append(names.Pdf).Any(File.Exists))
                    {
                        progress.Report("Выбор сохранения","Файлы уже существуют. Выберите замену или новый индекс.");
                        var choice=await progress.ChooseOutput(folder);
                        if(choice!=System.Windows.Forms.DialogResult.Yes&&choice!=System.Windows.Forms.DialogResult.No)throw new OperationCanceledException(token);
                        overwrite=choice==System.Windows.Forms.DialogResult.Yes;if(!overwrite)names=DrawingFileNames.IndexedOutputSet(folder,line,sheets.Count);
                    }
                    var pages=new List<string>();var drawings=new List<string>();
                    for(int i=0;i<sheets.Count;i++)
                    {
                        token.ThrowIfCancellationRequested();
                        progress.Report("Этап 4 из 6 — выпуск листов",$"Создаётся лист {i+1} из {sheets.Count}. Готово: {i}",i,sheets.Count);
                        string work=Path.Combine(job,$"sheet-{i+1:00}");Directory.CreateDirectory(work);
                        string scene=Path.Combine(work,"scene.json"),working=Path.Combine(work,"working.dwg"),script=Path.Combine(work,"generate.scr");
                        string drawing=Path.Combine(work,"output.dwg"),pdf=Path.Combine(work,"sheet.pdf");drawings.Add(drawing);pages.Add(pdf);
                        await File.WriteAllTextAsync(scene,JsonSerializer.Serialize(sheets[i].Scene,new JsonSerializerOptions{WriteIndented=true}),token);
                        File.Copy(jobTemplate,working);
                        File.WriteAllLines(script,new[]{"SECURELOAD","0","_NETLOAD","\""+assembly+"\"","ISORENDER","ISOPDF","_QUIT","_Y"},new UTF8Encoding(true));
                        var start=new ProcessStartInfo(engine){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=work,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.Unicode,StandardErrorEncoding=Encoding.Unicode};
                        foreach(var arg in new[]{"/i",working,"/s",script,"/l","en-US"})start.ArgumentList.Add(arg);
                        start.Environment["ISO_SCENE_INPUT"]=scene;start.Environment["ISO_DWG_OUTPUT"]=drawing;start.Environment["ISO_PDF_OUTPUT"]=pdf;start.Environment["ISO_SYMBOL_LIBRARY"]=library;
                        start.Environment["ISO_RENDER_TRACE"]=Path.Combine(work,"render-progress.log");start.Environment["ISO_SHEET_NUMBER"]=(i+1).ToString();start.Environment["ISO_SHEET_COUNT"]=sheets.Count.ToString();
                        using var process=Process.Start(start)??throw new InvalidOperationException("Не удалось запустить выпуск.");
                        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
                        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromMinutes(5));
                        try{await process.WaitForExitAsync(timeout.Token);}
                        catch(OperationCanceledException)
                        {
                            try{if(!process.HasExited)process.Kill(entireProcessTree:true);}catch(InvalidOperationException){}
                            await process.WaitForExitAsync();
                            await File.WriteAllTextAsync(Path.Combine(work,"console.log"),(await stdout)+"\n"+(await stderr));
                            token.ThrowIfCancellationRequested();throw new IOException($"Превышено время выпуска листа {i+1} (5 минут).");
                        }
                        await File.WriteAllTextAsync(Path.Combine(work,"console.log"),(await stdout)+"\n"+(await stderr));
                        token.ThrowIfCancellationRequested();
                        if(process.ExitCode!=0||!File.Exists(drawing)||!File.Exists(pdf))throw new IOException($"Не выпущен лист {i+1}.");
                        progress.Report("Этап 4 из 6 — выпуск листов",$"Готово листов: {i+1} из {sheets.Count}",i+1,sheets.Count);
                    }
                    token.ThrowIfCancellationRequested();progress.Report("Этап 5 из 6 — общий PDF",$"Объединение {pages.Count} страниц…");
                    string merged=Path.Combine(job,"merged.pdf");IsoGenerator.Publishing.PdfSetPublisher.Merge(pages,merged,line);
                    token.ThrowIfCancellationRequested();await progress.BeginPublication();
                    IsoGenerator.Publishing.OutputSetPublisher.Publish(drawings.Append(merged).ToArray(),names.Drawings.Append(names.Pdf).ToArray(),overwrite,Path.Combine(job,"previous-output"),overwrite?DrawingFileNames.ObsoleteDrawings(names.Pdf,names.Drawings):Array.Empty<string>());
                    progress.Finish("Изометрия создана",$"Листов: {sheets.Count}. DWG и общий PDF сохранены.\n"+folder+(warnings.Length>0?$"\nПредупреждений о соединениях: {warnings.Length}. Отчёт: "+Path.Combine(job,"warnings.txt"):""),true,folder);
                    _=dispatcher.BeginInvoke(new Action(()=>{try{onCompleted?.Invoke(folder);doc.Editor.WriteMessage($"\n[Изометрия] Готово. Листов: {sheets.Count}. PDF: {names.Pdf}\n");}catch(System.Exception){}}));
                }
                catch(OperationCanceledException){progress.Finish("Создание отменено","Итоговые файлы не изменены. Промежуточные данные сохранены для диагностики.");}
                catch(System.Exception ex){WriteFailure(job,ex);progress.Finish("Не удалось создать изометрию",ex.Message+"\nДиагностика: "+job,false,job);}
                finally{Interlocked.Exchange(ref busy,0);}
            });
        }
        catch(OperationCanceledException){Interlocked.Exchange(ref busy,0);view?.Finish("Создание отменено","Итоговые файлы не изменены.");}
        catch(System.Exception ex)
        {
            Interlocked.Exchange(ref busy,0);WriteFailure(job,ex);
            if(view is not null)view.Finish("Не удалось создать изометрию",ex.Message+(job.Length>0?"\nДиагностика: "+job:""),false,Directory.Exists(job)?job:null);
            else System.Windows.Forms.MessageBox.Show(ex.Message,"Изометрии");
        }
    }
    static void WriteFailure(string job,System.Exception error)
    {
        try{if(Directory.Exists(job))File.WriteAllText(Path.Combine(job,"error.log"),error.ToString());}catch(System.Exception){}
    }
    sealed class PlanningObserver(Action<PlanningProgress> report):IProgress<PlanningProgress>{public void Report(PlanningProgress value)=>report(value);}
    static ModelPoint Point(Autodesk.AutoCAD.Geometry.Point3d p)=>new(p.X,p.Y,p.Z);
    static ModelPoint Vector(Autodesk.AutoCAD.Geometry.Vector3d p)=>new(p.X,p.Y,p.Z);
    internal static PipelineSnapshot Capture(Database db,string filename,ObjectId seedId,string? selectedLine=null,IReadOnlyCollection<string>? selectedHandles=null,IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>>? propertyHints=null,string? projectIsoDirectory=null,CancellationToken cancellationToken=default,Action<int>? report=null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(db.Insunits!=UnitsValue.Millimeters)throw new InvalidOperationException("Поддерживаются чертежи в миллиметрах.");
        using var tr=db.TransactionManager.StartOpenCloseTransaction();
        if(!seedId.IsNull&&tr.GetObject(seedId,OpenMode.ForRead) is not PlantPart)throw new InvalidOperationException("Выберите компонент Plant 3D.");
        var project=Autodesk.ProcessPower.PlantInstance.PlantApplication.CurrentProject??throw new InvalidOperationException("Откройте проект Plant 3D.");
        var manager=((Autodesk.ProcessPower.P3dProjectParts.PipingProject)project.ProjectParts["Piping"]).DataLinksManager;
        Dictionary<string,string> Properties(ObjectId id)
        {
            // A Plant-derived entity can exist without a project data row (for example a detached component).
            // Only this expected lookup failure means "no properties"; other read failures must stop capture.
            int row;
            try{row=manager.FindAcPpRowId(id);}
            catch(System.Exception ex) when(ex.Message.Contains("ObjectDoesNotHaveLink",StringComparison.OrdinalIgnoreCase)){return new(StringComparer.OrdinalIgnoreCase);}
            return row>0?manager.GetAllProperties(row,true).ToDictionary(p=>p.Key,p=>p.Value??"",StringComparer.OrdinalIgnoreCase):new();
        }
        var seed=seedId.IsNull?new Dictionary<string,string>():Properties(seedId);
        if(selectedLine is null&&seed.Count==0)throw new InvalidOperationException("Выбранный компонент не связан с базой проекта Plant 3D. Выберите трубу из открытого проекта.");
        string line=selectedLine??seed.GetValueOrDefault("LineNumberTag","");
        var scope=selectedHandles?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(string.IsNullOrWhiteSpace(line))throw new InvalidOperationException("У выбранной детали не заполнен номер линии.");
        var propertyCache=new Dictionary<ObjectId,Dictionary<string,string>>();var queue=new Queue<ObjectId>();var warnings=new List<string>();
        var model=(BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db),OpenMode.ForRead);
        foreach(ObjectId id in model)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(tr.GetObject(id,OpenMode.ForRead) is not PlantPart||id.ObjectClass.Name=="AcPpDb3dConnector")continue;
            if(scope is not null&&!scope.Contains(id.Handle.ToString()))continue;
            Dictionary<string,string> props;
            try{props=Properties(id);}catch(System.Exception ex){throw new InvalidDataException($"Не прочитаны свойства {id.Handle}: {ex.Message}");}
            if(propertyHints?.TryGetValue(id.Handle.ToString(),out var hints)==true)foreach(var pair in hints)props[pair.Key]=pair.Value;
            if(scope is not null)props["LineNumberTag"]=line;
            if(props.GetValueOrDefault("LineNumberTag")==line){propertyCache[id]=props;queue.Enqueue(id);}
        }
        var seen=new HashSet<ObjectId>();var parts=new List<ModelPart>();var accessories=new List<ModelPart>();var links=new Dictionary<string,Connection>();
        using var connections=new ConnectionManager();
        while(queue.Count>0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id=queue.Dequeue();if(!seen.Add(id))continue;
            if(seen.Count%25==1)report?.Invoke(seen.Count);
            var part=(PlantPart)tr.GetObject(id,OpenMode.ForRead);var ports=part.GetPorts(PortType.All);var readPorts=new List<IsoGenerator.Core.Port>();
            for(int i=0;i<ports.Count;i++)
            {
                PlantPort port=ports[i];string end="";
                try{end=part.PortProperties(port.Name)?.EndCondition??"";}catch(System.Exception ex){warnings.Add($"{id.Handle}:{port.Name}: {ex.Message}");}
                if(end.Length==0)end=port.EndType??"";
                readPorts.Add(new(port.Name,Point(port.Position),Vector(port.Direction),end));
            }
            parts.Add(new(filename,id.Handle.ToString(),id.ObjectClass.Name,line,readPorts,propertyCache.GetValueOrDefault(id)??new(),part is InlineAsset asset?Vector(asset.ZAxis):null));
            if(part is Connector connector)
            {
                for(int index=0;index<connector.NumSubParts;index++)
                {
                    var subpart=connector.GetSubPart(index);if(subpart is WeldSubPart)continue;
                    var properties=subpart.PartSizeProperties;
                    if(properties is null)continue;
                    var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    foreach(var propertyName in properties.PropNames)
                    {if(propertyName is not string key||string.IsNullOrWhiteSpace(key))continue;var value=properties.PropValue(key);if(value is not null&&value is not DBNull)values[key]=Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)??"";}
                    string kind=properties.Type??"";
                    if(kind.Length==0)kind=subpart is BoltSetSubPart?"BoltSet":values.GetValueOrDefault("PnPClassName","");
                    values["PnPClassName"]=kind;values["ConnectorHandle"]=id.Handle.ToString();
                    if(string.IsNullOrWhiteSpace(values.GetValueOrDefault("DesignStd")))values["DesignStd"]=values.GetValueOrDefault("CompatibleStandard","");
                    if(subpart is BoltSetSubPart bolts)
                    {
                        values["Length"]=bolts.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        values["IsoQuantity"]=values.GetValueOrDefault("NumberInSet","1");values["IsoQuantityUnit"]="компл.";
                        if(!values.ContainsKey("NumberInSet"))warnings.Add($"{id.Handle}: у крепёжного комплекта нет NumberInSet; учтён один комплект соединения.");
                    }
                    else {values["IsoQuantity"]="1";values["IsoQuantityUnit"]="шт.";}
                    accessories.Add(new(filename,id.Handle+"#"+index,"ConnectorSubPart",line,Array.Empty<IsoGenerator.Core.Port>(),values));
                }
            }
            var attached=connections.GetConnections(id);
            for(int i=0;i<attached.Count;i++)
            {
                var c=attached[i];
                if(c.ObjectId1.IsNull||c.ObjectId2.IsNull){warnings.Add($"{id.Handle}: внешняя или неполная связь");continue;}
                var connection=new Connection(c.ObjectId1.Handle.ToString(),c.Port1.Name,c.ObjectId2.Handle.ToString(),c.Port2.Name);
                string key=string.Join("|",new[]{connection.FirstHandle+":"+connection.FirstPort,connection.SecondHandle+":"+connection.SecondPort}.OrderBy(s=>s,StringComparer.Ordinal));links[key]=connection;
                foreach(var other in new[]{c.ObjectId1,c.ObjectId2})if(other.Database==db&&other.ObjectClass.Name=="AcPpDb3dConnector"&&!seen.Contains(other))queue.Enqueue(other);
            }
        }
        var markers=IsoMarkerReader.Read(model,tr,cancellationToken).Where(m=>parts.Any(p=>p.Handle==m.HostHandle)).ToArray();
        return new(1,filename,parts,links.Values.ToArray(),warnings,accessories,markers);
    }
}
