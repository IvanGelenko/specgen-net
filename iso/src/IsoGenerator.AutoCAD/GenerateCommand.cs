using System.IO;
using System.Diagnostics;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using IsoGenerator.Core;
using IsoGenerator.Publishing;
using App=Autodesk.AutoCAD.ApplicationServices.Core.Application;
[assembly: CommandClass(typeof(IsoGenerator.AutoCAD.GenerateCommand))]
namespace IsoGenerator.AutoCAD;

public sealed class GenerateCommand
{
    // All source API reads run on AutoCAD's main thread in one read-only transaction.
    // Planning and rendering contain no AI calls or external network dependencies.
    [CommandMethod("ISOGENERATE",CommandFlags.Modal)]
    public void Generate()=>App.DocumentManager.MdiActiveDocument?.SendStringToExecute("PSISOMETRIC ",true,false,false);

    // Version each contract independently so unrelated rebuilds do not discard accepted work.
    const int LayoutVersion=19;
    const int DrawingVersion=5;
    const int DocumentVersion=3;
    static int busy;
    public void GenerateForLine(string line,IReadOnlyCollection<string> handles,string outputDirectory,IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>>? propertyHints=null,string? projectIsoDirectory=null,Action<string>? onCompleted=null,int quarterTurns=0,bool excludeFlangeHardware=false,bool ignoreBreakPoints=false,string? templatePath=null,double symbolScale=10,bool instrumentCalloutsOnly=true,bool automaticView=false,string valvesDocument="",string supportsDocument="",string stampDocument="",bool rememberLayout=false,bool recalculateLine=false,double minimumPipeLength=10,IsoAdvancedSettings? advancedSettings=null) =>
        GenerateForLines(new[]{new LineRequest(line,handles,propertyHints,rememberLayout)},outputDirectory,projectIsoDirectory,onCompleted,
            quarterTurns,excludeFlangeHardware,ignoreBreakPoints,templatePath,symbolScale,instrumentCalloutsOnly,automaticView,
            valvesDocument,supportsDocument,stampDocument,recalculateLine,minimumPipeLength,advancedSettings);


    public sealed record LineRequest(string Line,IReadOnlyCollection<string> Handles,
        IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>>? PropertyHints,bool RememberLayout);

    sealed record CapturedLine(LineRequest Request,PipelineSnapshot Snapshot,string Job);

    sealed record PlannedLine(CapturedLine Input,string LayoutKey,string RenderKey,int View,RememberedLayout? Layout,
        RememberedIsoOutput.Entry? Previous,IReadOnlyList<PlannedSheet> Sheets,string[] Titles,int Warnings);

    sealed record ArchivedLine(string Line,PipelineSnapshot Snapshot,IReadOnlyList<PlannedSheet> Sheets);
    sealed record AppendArchive(int Version,string Profile,string PdfHash,ArchivedLine[] Lines);

    sealed record PreparedLine(PlannedLine Plan,RememberedIsoOutput.Page[] Pages,int Reused)
    {
        public CapturedLine Input=>Plan.Input;
        public RememberedIsoOutput.Entry? Previous=>Plan.Previous;
    }

    public void GenerateForLines(IReadOnlyList<LineRequest> lines,string outputDirectory,string? projectIsoDirectory=null,
        Action<string>? onCompleted=null,int quarterTurns=0,bool excludeFlangeHardware=false,bool ignoreBreakPoints=false,
        string? templatePath=null,double symbolScale=10,bool instrumentCalloutsOnly=true,bool automaticView=false,
        string valvesDocument="",string supportsDocument="",string stampDocument="",bool recalculateLine=false,
        double minimumPipeLength=10,IsoAdvancedSettings? advancedSettings=null,IsometricReportDetails? report=null,IReadOnlyDictionary<string,string>? drawingLines=null)
    {
        var doc=App.DocumentManager.MdiActiveDocument;if(doc is null)return;
        if(Interlocked.CompareExchange(ref busy,1,0)!=0){System.Windows.Forms.MessageBox.Show("Создание изометрии уже выполняется. Дождитесь завершения или отмените его в окне прогресса.","Изометрии");return;}
        IsoProgressWindow? view=null;string job="";
        var dispatcher=System.Windows.Threading.Dispatcher.CurrentDispatcher;
        try
        {
            if(lines.Count==0)throw new InvalidDataException("Выберите хотя бы одну линию.");
            if(report is not null)stampDocument=report.Title.DocumentNumber;
            if(lines.Count>1&&string.IsNullOrWhiteSpace(stampDocument))
                throw new InvalidDataException("Для общего PDF заполните поле «Документ для штампа».");
            string pdfStem=string.IsNullOrWhiteSpace(stampDocument)
                ?DrawingFileNames.LineStem(lines[0].Line):DrawingFileNames.DocumentStem(stampDocument);
            string publication=lines.Count==1?lines[0].Line:$"{lines.Count} линий · {pdfStem}";
            view=IsoProgressWindow.Start(publication,App.MainWindow.Handle);var token=view.Token;
            advancedSettings??=new IsoAdvancedSettings();advancedSettings.Validate();
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
            if(string.IsNullOrWhiteSpace(projectIsoDirectory))throw new InvalidOperationException("Не определена папка Isometric активного проекта.");
            string folder=Path.GetFullPath(outputDirectory);
            // The Core Console must receive local script and working DWG paths.
            job=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IsoGenerator","Jobs",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(job);
            if(report is not null)
            {
                string titleSource=Path.Combine(job,"title-template.docx"),contentsSource=Path.Combine(job,"contents-template.docx");
                File.Copy(report.TitleTemplate,titleSource);
                File.Copy(report.ContentsTemplate,contentsSource);
                if(IsometricDocumentTemplates.ReadTitle(titleSource)!=report.Title)
                    throw new InvalidDataException("Титульный лист изменился. Откройте генератор заново, чтобы обновить реквизиты.");
                report=report with{TitleTemplate=titleSource,ContentsTemplate=contentsSource};
                IsometricDocumentTemplates.ValidateContents(report,stampDocument);
            }
            string library=Path.Combine(job,"IsoSymbolStyles.dwg"),mapping=Path.Combine(job,"IsoSkeyAcadBlockMap.xml");
            File.Copy(Path.Combine(projectIsoDirectory,"IsoSymbolStyles.dwg"),library);
            File.Copy(Path.Combine(projectIsoDirectory,"IsoSkeyAcadBlockMap.xml"),mapping);
            string jobTemplate=Path.Combine(job,"iso_template.dwg");
            File.Copy(templatePath,jobTemplate);
            var captured=new List<CapturedLine>();
            foreach(var request in lines)
            {
                string line=request.Line;
                string lineJob=lines.Count==1?job:Path.Combine(job,$"line-{captured.Count+1:00}");
                Directory.CreateDirectory(lineJob);
                var snapshot=PlantSnapshotReader.Capture(doc.Database,doc.Name,ObjectId.Null,line,request.Handles,request.PropertyHints,
                    drawingLines:drawingLines,cancellationToken:token,report:count=>view.Report("Этап 1 из 6 — чтение модели",$"{line} · прочитано объектов: {count}"));
                File.WriteAllText(Path.Combine(lineJob,"snapshot.json"),JsonSerializer.Serialize(snapshot,new JsonSerializerOptions{WriteIndented=true}));
                File.WriteAllText(Path.Combine(lineJob,"run-settings.json"),JsonSerializer.Serialize(new{line,quarterTurns,automaticView,excludeFlangeHardware,ignoreBreakPoints,instrumentCalloutsOnly,templatePath,symbolScale,minimumPipeLength,advancedSettings,valvesDocument,supportsDocument,stampDocument,startedUtc=DateTimeOffset.UtcNow,sourceDrawing=snapshot.SourceDrawing,sourceObjects=snapshot.Parts.Count},new JsonSerializerOptions{WriteIndented=true}));
                captured.Add(new(request,snapshot,lineJob));
            }
            token.ThrowIfCancellationRequested();
            view.Report("Этап 2 из 6 — подготовка обозначений","Чтение библиотеки символов проекта…");
            var catalog=SymbolCatalogCommands.Read(library,token,count=>view.Report("Этап 2 из 6 — подготовка обозначений",$"Прочитано блоков: {count}")) with{SymbolScale=symbolScale,Style=null};
            File.WriteAllText(Path.Combine(job,"symbols.json"),JsonSerializer.Serialize(catalog));
            var map=new IsoSymbolMap(File.ReadAllText(mapping),catalog);
            string assembly=typeof(GenerateCommand).Assembly.Location;
            string engine=Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!,"accoreconsole.exe");
            if(!File.Exists(engine))throw new FileNotFoundException("Не найден AutoCAD Core Console",engine);
            var progress=view;
            void Report(string stage,string line,string detail,int completed=0,int total=0)=>
                progress.Report(stage,$"{line} · {detail}",completed,total);

            PlannedLine Plan(CapturedLine input)
            {
                var snapshot=input.Snapshot;
                string line=input.Request.Line,lineJob=input.Job;
                bool rememberLayout=input.Request.RememberLayout;
                token.ThrowIfCancellationRequested();
                var captureWarnings=snapshot.Warnings.Concat(SnapshotValidation.Validate(snapshot).Warnings).Distinct().ToArray();
                string warningPath=Path.Combine(lineJob,"warnings.txt");
                if(captureWarnings.Length>0)File.WriteAllLines(warningPath,captureWarnings);
                Report("Этап 3 из 6 — расчёт компоновки",line,"Подбор размеров участков и границ листов…");
                string rejectionLog=Path.Combine(lineJob,"layout-rejections.jsonl");
                var observer=new PlanningObserver(p=>
                {
                    if(p.Rejection is not null)
                    {
                        try{File.AppendAllText(rejectionLog,JsonSerializer.Serialize(p)+Environment.NewLine);}
                        catch(IOException){}
                        catch(UnauthorizedAccessException){}
                        return;
                    }
                    Report("Этап 3 из 6 — расчёт компоновки",line,p.Detail+$". Проверено вариантов: {p.Variants}");
                });
                string layoutKey=rememberLayout?JsonSerializer.Serialize(new
                {
                    LayoutVersion,
                    Template=RememberedIsoOutput.HashFile(jobTemplate),
                    Library=RememberedIsoOutput.HashFile(library),
                    Mapping=RememberedIsoOutput.HashFile(mapping),
                    automaticView,quarterTurns,excludeFlangeHardware,ignoreBreakPoints,instrumentCalloutsOnly,symbolScale,minimumPipeLength,advancedSettings
                }):"";
                string renderKey=JsonSerializer.Serialize(new{DrawingVersion,valvesDocument,supportsDocument,stampDocument});
                var previous=rememberLayout&&!recalculateLine?RememberedIsoOutput.Read(snapshot.SourceDrawing,line):null;
                if(previous?.LayoutKey!=layoutKey)previous=null;
                var options=new LineLayoutOptions(automaticView,quarterTurns,excludeFlangeHardware,
                    ignoreBreakPoints,instrumentCalloutsOnly,minimumPipeLength,advancedSettings);
                var result=LineViewPlanner.Plan(snapshot,map,options,previous?.Layout,previous?.QuarterTurns,rememberLayout,
                    token,observer,turn=>Report("Подбор ракурса",line,$"Проверяется ракурс {turn+1}."),
                    (_,message)=>File.AppendAllLines(Path.Combine(lineJob,"view-failures.log"),new[]{message}));
                var sheets=result.Sheets;
                File.WriteAllText(Path.Combine(lineJob,"view-selection.json"),JsonSerializer.Serialize(new{automaticView,quarterTurns=result.QuarterTurns}));
                token.ThrowIfCancellationRequested();
                var warnings=captureWarnings.Concat(sheets.SelectMany(sheet=>sheet.Scene.Warnings))
                    .Where(warning=>!warning.StartsWith("Schematic layout:",StringComparison.Ordinal)&&
                                    !warning.StartsWith("CONTROL PREVIEW:",StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal).ToArray();
                if(warnings.Length>0)File.WriteAllLines(warningPath,warnings);
                var titles=DrawingTitles(line,sheets,lines.Count>1);
                return new(input,layoutKey,renderKey,result.QuarterTurns,result.Remembered,previous,sheets,titles,warnings.Length);
            }

            static string[] DrawingTitles(string line,IReadOnlyList<PlannedSheet> sheets,bool alwaysNumber)=>
                sheets.Select((sheet,i)=>
                {
                    string drawingName=Path.GetFileNameWithoutExtension(DrawingFileNames.Dwg("",line,i+1,sheets.Count,alwaysNumber:alwaysNumber));
                    return sheet.Scene.BomOnly?"Спецификация трубопровода (продолжение): "+drawingName:
                        "Изометрические чертежи. Схема соединений (монтажная): "+drawingName;
                }).ToArray();

            bool numberAllDrawings=lines.Count>1;
            async Task<PreparedLine> Prepare(PlannedLine plan,IReadOnlyList<DocumentDrawing>? documentPages)
            {
                var sheets=plan.Sheets;
                string line=plan.Input.Request.Line,lineJob=plan.Input.Job;
                var previous=plan.Previous;
                var scenes=documentPages?.Select(page=>page.Scene).ToArray()??sheets.Select(sheet=>sheet.Scene).ToArray();
                var numberingKeys=scenes.Select((scene,i)=>documentPages is null?null:JsonSerializer.Serialize(new
                {
                    Page=documentPages[i].PageNumber,NumberAllDrawings=numberAllDrawings,
                    References=scene.Annotations.Where(label=>label.Role is "continuation" or "external-line").Select(label=>label.Text)
                })).ToArray();
                var cached=new RememberedIsoOutput.Page?[sheets.Count];
                if(previous is not null&&previous.RenderKey==plan.RenderKey&&
                   (previous.Pages.Length>1)==(sheets.Count>1))
                    for(int i=0;i<Math.Min(sheets.Count,previous.Pages.Length);i++)
                    {
                        var candidate=previous.Pages[i];
                        if(candidate.NumberingKey!=numberingKeys[i]||
                           SceneFingerprint(previous.Layout.Sheets[i].Scene)!=SceneFingerprint(sheets[i].Scene))continue;
                        if(RememberedIsoOutput.Matches(candidate.Drawing,candidate.DrawingHash)&&
                           RememberedIsoOutput.Matches(candidate.Pdf,candidate.PdfHash))cached[i]=candidate;
                    }
                var pages=new List<string>();var drawings=new List<string>();
                for(int i=0;i<sheets.Count;i++)
                {
                    token.ThrowIfCancellationRequested();
                    Report("Этап 4 из 6 — выпуск листов",line,$"Создаётся лист {i+1} из {sheets.Count}. Готово: {i}",i,sheets.Count);
                    if(cached[i] is {} existing)
                    {
                        drawings.Add(existing.Drawing);pages.Add(existing.Pdf);
                        continue;
                    }
                    string work=Path.Combine(lineJob,$"sheet-{i+1:00}");Directory.CreateDirectory(work);
                    string scene=Path.Combine(work,"scene.json");
                    string drawing=Path.Combine(work,"output.dwg"),pdf=Path.Combine(work,"sheet.pdf");drawings.Add(drawing);pages.Add(pdf);
                    await File.WriteAllTextAsync(scene,JsonSerializer.Serialize(scenes[i],new JsonSerializerOptions{WriteIndented=true}),token);
                    var start=NativeIsoWorker.Configure(engine,assembly,jobTemplate,work,"ISORENDER","ISOPDF");
                    start.Environment["ISO_SCENE_INPUT"]=scene;start.Environment["ISO_DWG_OUTPUT"]=drawing;start.Environment["ISO_PDF_OUTPUT"]=pdf;start.Environment["ISO_SYMBOL_LIBRARY"]=library;
                    start.Environment["ISO_RENDER_TRACE"]=Path.Combine(work,"render-progress.log");start.Environment["ISO_SHEET_NUMBER"]=(i+1).ToString();start.Environment["ISO_SHEET_COUNT"]=sheets.Count.ToString();
                    if(documentPages is not null)
                    {
                        start.Environment["ISO_DOCUMENT_SHEET_NUMBER"]=documentPages[i].PageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        if(numberAllDrawings)start.Environment["ISO_NUMBER_ALL_DRAWINGS"]="1";
                    }
                    start.Environment["ISO_VALVES_DOCUMENT"]=valvesDocument;
                    start.Environment["ISO_SUPPORTS_DOCUMENT"]=supportsDocument;
                    start.Environment["ISO_STAMP_DOCUMENT"]=stampDocument;
                    advancedSettings.SetWorkerProfile(start.Environment);
                    await NativeIsoWorker.Run(start,TimeSpan.FromSeconds(advancedSettings.RenderTimeoutSeconds),
                        $"выпуск листа {i+1}",token,drawing,pdf);
                    Report("Этап 4 из 6 — выпуск листов",line,$"Готово листов: {i+1} из {sheets.Count}",i+1,sheets.Count);
                }
                var preparedPages=drawings.Select((drawing,i)=>cached[i]??new RememberedIsoOutput.Page(
                    drawing,pages[i],RememberedIsoOutput.HashFile(drawing),RememberedIsoOutput.HashFile(pages[i]),numberingKeys[i])).ToArray();
                return new(plan,preparedPages,cached.Count(page=>page is not null));
            }
            static InvalidOperationException LineFailure(CapturedLine input,System.Exception error)
            {
                WriteFailure(input.Job,error);
                return new InvalidOperationException($"{input.Request.Line} — {error.Message}",error);
            }
            static PlannedLine ForLine(CapturedLine input,Func<PlannedLine> action)
            {
                try{return action();}
                catch(System.Exception ex) when(ex is not OperationCanceledException){throw LineFailure(input,ex);}
            }
            _=Task.Run(async()=>
            {
                try
                {
                    using var profile=IsoAdvancedSettings.Use(advancedSettings);
                    var planned=captured.Select(input=>ForLine(input,()=>Plan(input))).ToArray();
                    var sheetCounts=planned.Select(line=>(Line:line.Input.Request.Line,Sheets:line.Sheets.Count)).ToArray();
                    var comparer=StringComparer.OrdinalIgnoreCase;
                    string PublicationKey()=>JsonSerializer.Serialize(new
                    {
                        DocumentVersion,
                        Lines=planned.Select(line=>new{line.Input.Request.Line,line.LayoutKey,line.RenderKey,
                            Scenes=line.Sheets.Select(sheet=>SceneFingerprint(sheet.Scene))}),
                        Title=report is null?null:RememberedIsoOutput.HashFile(report.TitleTemplate),
                        Contents=report is null?null:RememberedIsoOutput.HashFile(report.ContentsTemplate),
                        Date=report?.StampDate
                    });
                    var previousPdf=planned[0].Previous?.PublishedPdf;
                    int index=0;
                    if(previousPdf is not null&&comparer.Equals(Path.GetDirectoryName(previousPdf),folder)&&
                       planned.All(line=>comparer.Equals(line.Previous?.PublishedPdf,previousPdf)))
                    {
                        string pattern="^"+System.Text.RegularExpressions.Regex.Escape(pdfStem)+@" \(([1-9]\d*)\)$";
                        var match=System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(previousPdf),pattern,
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                        if(match.Success)int.TryParse(match.Groups[1].Value,out index);
                    }
                    (string[] Drawings,string Pdf,string[] Documents) OutputNames(int outputIndex)
                    {
                        var (drawings,pdf)=DrawingFileNames.DocumentOutputSet(folder,sheetCounts,pdfStem,outputIndex);
                        return (drawings,pdf,report is null?Array.Empty<string>():DrawingFileNames.ReportDocuments(pdf));
                    }
                    var names=OutputNames(index);
                    bool overwrite=false;
                    string? appendPdfHash=null;
                    string ArchivePath(string pdf)=>pdf+".specgen-lines.json";
                    string archiveProfile=JsonSerializer.Serialize(new
                    {
                        Library=RememberedIsoOutput.HashFile(library),
                        Template=RememberedIsoOutput.HashFile(jobTemplate),
                        Settings=advancedSettings,valvesDocument,supportsDocument
                    });
                    AppendArchive? ReadAppendArchive(string pdf)
                    {
                        string archive=ArchivePath(pdf);
                        if(report is null||!File.Exists(pdf)||!OutputSetPublisher.IsOwnedOutput(pdf,archive))return null;
                        try
                        {
                            return JsonSerializer.Deserialize<AppendArchive>(File.ReadAllText(archive)) is {Version:1,Lines.Length:>0} saved&&
                                saved.Profile==archiveProfile&&RememberedIsoOutput.Matches(pdf,saved.PdfHash)?saved:null;
                        }
                        catch(System.Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){return null;}
                    }

                    bool Conflicts()=>names.Drawings.Append(names.Pdf).Concat(names.Documents)
                        .Append(OutputSetPublisher.ManifestPath(names.Pdf)).Append(ArchivePath(names.Pdf)).Any(File.Exists);
                    if(!recalculateLine&&planned.All(line=>line.Input.Request.RememberLayout)&&
                       OutputSetPublisher.IsCurrent(names.Pdf,PublicationKey(),names.Drawings.Append(names.Pdf).Concat(names.Documents)
                           .Concat(report is null?Array.Empty<string>():new[]{ArchivePath(names.Pdf)}).ToArray()))
                    {
                        progress.Finish("Комплект актуален",$"Все {sheetCounts.Sum(line=>line.Sheets)} листов совпадают с сохранённым выпуском.",true,folder);
                        return;
                    }
                    if(Conflicts())
                    {
                        var saved=ReadAppendArchive(names.Pdf);
                        progress.Report("Выбор сохранения",saved is null?"Файлы уже существуют. Выберите замену или новый индекс.":
                            "Файлы уже существуют. Можно добавить линии с пересчётом содержания.");
                        var choice=await progress.ChooseOutput(folder,saved is not null);
                        if(choice==System.Windows.Forms.DialogResult.Yes)overwrite=true;
                        else if(choice==System.Windows.Forms.DialogResult.No)names=IndexedNames();
                        else if(choice==System.Windows.Forms.DialogResult.Retry&&saved is not null)
                        {
                            var selected=planned.Select(item=>item.Input.Request.Line).ToHashSet(comparer);
                            var combined=new List<PlannedLine>();
                            foreach(var prior in saved.Lines)
                            {
                                if(selected.Contains(prior.Line))
                                    throw new InvalidDataException($"Линия {prior.Line} уже входит в комплект. Для добавления отметьте только новые линии.");
                                var work=Path.Combine(job,$"archive-{combined.Count+1:00}");
                                var input=new CapturedLine(new(prior.Line,Array.Empty<string>(),null,false),prior.Snapshot,work);
                                combined.Add(new(input,"","",0,null,null,prior.Sheets,Array.Empty<string>(),0));
                            }
                            combined.AddRange(planned);
                            numberAllDrawings=combined.Count>1;
                            planned=combined.Select(item=>item with
                            {
                                Titles=DrawingTitles(item.Input.Request.Line,item.Sheets,numberAllDrawings)
                            }).ToArray();
                            sheetCounts=planned.Select(item=>(Line:item.Input.Request.Line,Sheets:item.Sheets.Count)).ToArray();
                            names=OutputNames(index);
                            appendPdfHash=saved.PdfHash;
                            overwrite=true;
                        }
                        else throw new OperationCanceledException(token);
                    }
                    string publicationKey=PublicationKey();
                    (string[] Drawings,string Pdf,string[] Documents) IndexedNames()
                    {
                        for(int next=1;;next++)
                        {
                            var candidate=OutputNames(next);
                            if(!candidate.Drawings.Append(candidate.Pdf).Concat(candidate.Documents).Append(OutputSetPublisher.ManifestPath(candidate.Pdf)).Append(ArchivePath(candidate.Pdf)).Any(File.Exists))
                            {index=next;return candidate;}
                        }
                    }
                    IsometricReport.Pages? front=null;
                    IReadOnlyDictionary<string,IReadOnlyList<DocumentDrawing>>? document=null;
                    if(report is not null)
                    {
                        progress.Report("Подготовка содержания","Определяем сквозные номера листов…");
                        front=await IsometricReport.RenderInWorker(report,stampDocument,planned.SelectMany(line=>line.Titles).ToArray(),
                            job,TimeSpan.FromSeconds(Math.Max(60,advancedSettings.RenderTimeoutSeconds)),token);
                        document=DocumentDrawingPlan.Build(planned.Select(line=>(line.Input.Request.Line,line.Sheets)).ToArray(),
                            frontPages:1+front.ContentsPageCount);
                    }
                    var prepared=new List<PreparedLine>();
                    foreach(var plan in planned)
                    {
                        try
                        {
                            prepared.Add(await Prepare(plan,document?[plan.Input.Request.Line]));
                        }
                        catch(System.Exception ex) when(ex is not OperationCanceledException){throw LineFailure(plan.Input,ex);}
                    }
                    var pages=prepared.SelectMany(line=>line.Pages).ToArray();
                    bool ReusePublishedDrawing(int i)=>RememberedIsoOutput.Matches(names.Drawings[i],pages[i].DrawingHash);
                    token.ThrowIfCancellationRequested();
                    progress.Report("Этап 5 из 6 — общий PDF",$"Объединение {pages.Length} страниц…");
                    string merged=Path.Combine(job,"merged.pdf");
                    var drawingPdfs=pages.Select(page=>page.Pdf).ToArray();
                    string[] stagedDocuments=Array.Empty<string>();
                    if(front is null)PdfSetPublisher.Merge(drawingPdfs,merged,pdfStem);
                    else
                    {
                        PdfSetPublisher.MergeReport(front.CoverPdf,front.ContentsPdf,drawingPdfs,merged,stampDocument);
                        string archiveStage=Path.Combine(job,"append-data.json");
                        File.WriteAllText(archiveStage,JsonSerializer.Serialize(new AppendArchive(1,archiveProfile,
                            RememberedIsoOutput.HashFile(merged),planned.Select(item=>new ArchivedLine(
                                item.Input.Request.Line,item.Input.Snapshot,item.Sheets)).ToArray())));
                        stagedDocuments=new[]{front.TitleDocx,front.ContentsDocx,archiveStage};
                    }
                    token.ThrowIfCancellationRequested();
                    await progress.BeginPublication();
                    Directory.CreateDirectory(folder);
                    while(true)
                    {
                        try
                        {
                            var retained=Enumerable.Range(0,pages.Length).Where(ReusePublishedDrawing)
                                .ToDictionary(i=>names.Drawings[i],i=>pages[i].DrawingHash,comparer);
                            var changed=Enumerable.Range(0,pages.Length).Where(i=>!retained.ContainsKey(names.Drawings[i])).ToArray();
                            OutputSetPublisher.PublishDocument(names.Pdf,
                                changed.Select(i=>pages[i].Drawing).Append(merged).Concat(stagedDocuments).ToArray(),
                                changed.Select(i=>names.Drawings[i]).Append(names.Pdf).Concat(names.Documents)
                                    .Concat(front is null?Array.Empty<string>():new[]{ArchivePath(names.Pdf)}).ToArray(),overwrite,
                                Path.Combine(job,"previous-output"),releaseKey:publicationKey,retainedOutputs:retained,expectedPreviousPdfHash:appendPdfHash);
                            break;
                        }
                        catch(OutputOwnershipConflictException ex) when(appendPdfHash is null)
                        {
                            progress.Report("Сохранение с новым индексом",ex.Message);
                            names=IndexedNames();overwrite=false;
                        }
                        catch(System.Exception ex) when(appendPdfHash is null&&ex is (IOException or UnauthorizedAccessException))
                        {
                            var choice=System.Windows.Forms.MessageBox.Show(
                                ex.Message+"\n\nЗакройте файл и нажмите «Повтор».\nЛибо нажмите «Отмена», чтобы сохранить комплект с новым индексом.",
                                "Файл занят",System.Windows.Forms.MessageBoxButtons.RetryCancel,System.Windows.Forms.MessageBoxIcon.Warning);
                            if(choice==System.Windows.Forms.DialogResult.Retry)continue;
                            if(!overwrite)throw;
                            names=IndexedNames();overwrite=false;
                        }
                    }
                    int warnings=prepared.Sum(line=>line.Plan.Warnings);
                    foreach(var line in prepared)
                    {
                        string drawing=line.Input.Snapshot.SourceDrawing,key=line.Input.Request.Line;
                        if(line.Input.Request.Handles.Count==0)continue;
                        try
                        {
                            RememberedIsoOutput.MarkPublished(drawing,key,line.Input.Request.RememberLayout);
                            if(line.Plan.Layout is {} layout)
                                RememberedIsoOutput.Save(drawing,key,new(line.Plan.LayoutKey,line.Plan.RenderKey,line.Plan.View,layout,
                                    line.Pages,names.Pdf));
                        }
                        catch(System.Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException)
                        {
                            warnings++;
                            try{File.AppendAllLines(Path.Combine(line.Input.Job,"warnings.txt"),new[]{"Не удалось запомнить выпуск: "+ex.Message});}
                            catch(IOException){}
                            catch(UnauthorizedAccessException){}
                        }
                    }
                    progress.Finish("Изометрии созданы",$"Линий: {prepared.Count}. Листов: {pages.Length}. Использовано готовых листов: {prepared.Sum(line=>line.Reused)}.\n"+
                        names.Pdf+(names.Documents.Length>0?"\nТитульный лист и содержание сохранены в DOCX.":"")+(warnings>0?$"\nПредупреждений: {warnings}. Диагностика: "+job:""),true,folder);
                    _=dispatcher.BeginInvoke(new Action(()=>{try{onCompleted?.Invoke(folder);doc.Editor.WriteMessage($"\n[Изометрия] Готово. Листов: {pages.Length}. PDF: {names.Pdf}\n");}catch(System.Exception){}}));
                }
                catch(OperationCanceledException){progress.Finish("Создание отменено","Итоговые файлы не изменены. Промежуточные данные сохранены для диагностики.");}
                catch(System.Exception ex){WriteFailure(job,ex);progress.Finish("Не удалось создать изометрии",ex.Message+"\nДиагностика: "+job,false,job);}
                finally{Interlocked.Exchange(ref busy,0);}
            });
        }
        catch(OperationCanceledException){Interlocked.Exchange(ref busy,0);view?.Finish("Создание отменено","Итоговые файлы не изменены.");}
        catch(System.Exception ex)
        {
            Interlocked.Exchange(ref busy,0);WriteFailure(job,ex);
            if(view is not null)view.Finish("Не удалось создать изометрии",ex.Message+(job.Length>0?"\nДиагностика: "+job:""),false,Directory.Exists(job)?job:null);
            else System.Windows.Forms.MessageBox.Show(ex.Message,"Изометрии");
        }
    }
    static string SceneFingerprint(SheetScene scene)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(scene with
        {
            Annotations=scene.Annotations.Where(a=>a.Role is not ("title" or "status")).ToArray(),
            Warnings=Array.Empty<string>()
        })));

    static void WriteFailure(string job,System.Exception error)
    {
        try{if(Directory.Exists(job))File.WriteAllText(Path.Combine(job,"error.log"),error.ToString());}catch(System.Exception){}
    }
    sealed class PlanningObserver(Action<PlanningProgress> report):IProgress<PlanningProgress>{public void Report(PlanningProgress value)=>report(value);}
}
