# Настройки генератора изометрий

Окно «Глубокие настройки…» в диалоге генерации редактирует профиль из таблицы ниже. При запуске генерации профиль сохраняется в пользовательских настройках SpecGen и записывается в run-settings.json задания. Изменение профиля требует пересчёта сохранённой компоновки. «По умолчанию» восстанавливает исходные значения.

Миллиметры в таблице относятся к листу. Минимальная длина трубы на листе задаётся в верхней части окна «Глубокие настройки». По умолчанию 10 мм, диапазон 1–100 000 мм. Масштаб элементов, ракурс, исключение крепежа и игнорирование ручных разрывов доступны в основном окне. Размерные числа берутся из геометрии модели.

## Параметры профиля



### Трубы и символы

| Параметр | По умолчанию | Диапазон | Поле JSON |
|---|---:|---:|---|
| Шаг длин труб на листе, мм | 10 | 1–50 | `PipeGridStep` |
| Разрыв трубы при перекрытии, мм | 6 | 0.1–25 | `PipeGap` |
| Отступ линии изоляции, мм | 3 | 0.1–20 | `InsulationOffset` |
| Делитель масштаба опор | 2 | 1–10 | `SupportScaleDivisor` |

### Размеры

| Параметр | По умолчанию | Диапазон | Поле JSON |
|---|---:|---:|---|
| Отступ размерной линии от трубы и арматуры, мм | 10 | 1–50 | `DimensionClearance` |
| Шаг размерных рядов, мм | 7 | 2–30 | `DimensionRowStep` |
| Дополнительные ряды на каждой стороне | 8 | 1–18 | `DimensionLaneCount` |
| Подъём размерного числа над линией, мм | 2.8 | 1–10 | `TextLift` |
| Высота размерного числа в DWG, мм | 3.5 | 1–10 | `DimensionTextHeight` |
| Длина размерной стрелки и стрелки выноски, мм | 2.5 | 0.5–10 | `ArrowSize` |
| Зазор от начала выносной линии до детали, мм | 1 | 0–10 | `ExtensionOffset` |
| Выход выносной линии за размер, мм | 1 | 0–10 | `ExtensionOvershoot` |
| Порог длинной выносной линии размера, мм | 35 | 5–100 | `LongExtensionThreshold` |

### Выноски и подписи

| Параметр | По умолчанию | Диапазон | Поле JSON |
|---|---:|---:|---|
| Предпочтительная длина выноски, мм | 15 | 3–60 | `LeaderTarget` |
| Длина, после которой выноска получает штраф, мм | 20 | 5–100 | `LeaderSoftLimit` |
| Верхняя граница поиска обычной выноски, мм | 96 | 10–150 | `LeaderMaxReach` |
| Высота текста выносок и позиций в DWG, мм | 3.5 | 1–10 | `LeaderTextHeight` |
| Вес пересечения линии выноски | 0.6 | 0–10 | `LeaderCrossingWeight` |
| Вес пересечения выноски арматуры | 0.35 | 0–10 | `ValveLeaderCrossingWeight` |
| Длина колена полки в DWG, мм | 1 | 0–10 | `LeaderLanding` |

### Лист

| Параметр | По умолчанию | Диапазон | Поле JSON |
|---|---:|---:|---|
| Граница рабочего поля слева, мм | 25 | 0–590 | `PaperLeft` |
| Граница рабочего поля справа, мм | 393 | 1–594 | `PaperRight` |
| Граница рабочего поля снизу, мм | 25 | 0–419 | `PaperBottom` |
| Граница рабочего поля сверху, мм | 370 | 1–420 | `PaperTop` |

### Спецификация

| Параметр | По умолчанию | Диапазон | Поле JSON |
|---|---:|---:|---|
| Высота строки спецификации, мм | 14 | 2–30 | `BomRowHeight` |
| Высота заголовка раздела, мм | 7 | 2–20 | `BomHeadingHeight` |
| Высота текста спецификации, мм | 3 | 1–10 | `BomTextHeight` |
| Допустимая высота тела спецификации, мм | 330 | 20–380 | `BomBodyLimit` |

### Поиск

| Параметр | По умолчанию | Диапазон | Поле JSON |
|---|---:|---:|---|
| Лимит одного ракурса, с | 120 | 5–600 | `ViewTimeSeconds` |
| Общий лимит автоподбора, с | 180 | 5–1200 | `TotalViewTimeSeconds` |
| Лимит выпуска одного листа, с | 300 | 10–1800 | `RenderTimeoutSeconds` |
| Лимит вариантов компоновки | 512 | 32–4096 | `PlanningVariants` |
| Ширина поиска маршрута размеров | 24 | 4–72 | `DimensionBeamWidth` |
| Лимит перебора при пересечениях размеров | 500 | 20–3000 | `DimensionBacktracking` |
| Шаг локальной поправки трубы, мм | 10 | 1–50 | `PipeAdjustment` |

### Переходы

| Параметр | По умолчанию | Диапазон | Поле JSON |
|---|---:|---:|---|
| Длина пунктирного продолжения, мм | 12 | 2–45 | `ContinuationPipeLength` |
| Масштаб штрихов линии продолжения | 2.5 | 0.5–5 | `ContinuationDashScale` |
| Длина штриха пунктира, мм | 2 | 0.2–12 | `DashLength` |
| Просвет между штрихами, мм | 1 | 0.1–12 | `DashGap` |

## Настройки, связанные с шаблоном и исходными данными

- Форма А2 (594 × 420 мм), рамка, основная надпись и исходная структура таблицы связаны с шаблоном DWG. Рабочие границы поля профиля должны соответствовать шаблону. [Печать PDF](src/IsoGenerator.AutoCAD/PdfCommands.cs) · [Рендерер](src/IsoGenerator.AutoCAD/RenderCommands.cs)
- Формы знаков и сопоставления SKEY поступают из библиотек проекта Plant. Линии, категории, позиции, уклоны, метки и реальные размеры поступают из модели. [Чтение модели](src/IsoGenerator.AutoCAD/GenerateCommand.cs) · [Библиотека](src/IsoGenerator.Core/IsoSymbols.cs)
- Рабочая компоновка использует профиль и явный масштаб. Парсер наследуемого IsoConfig существует отдельно. [IsoStyleSettings](src/IsoGenerator.Core/IsoStyleSettings.cs)

## Числовые литералы по исходникам

Указатель включает внутренние допуски, веса, массивы вариантов, технические идентификаторы и параметры шаблона. Для анализа приведены строки исходников. Редактируемые поля перечислены в таблице выше.

### GenerateCommand.cs

- [Строка 28](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L28) `public void GenerateForLine(string line,IReadOnlyCollection<string> handles,string outputDirectory,IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>>? propertyHints=null,string? projectIsoDirectory=null,Action<…`
- [Строка 31](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L31) `if(Interlocked.CompareExchange(ref busy,1,0)!=0){System.Windows.Forms.MessageBox.Show("Создание изометрии уже выполняется. Дождитесь завершения или отмените его в окне прогресса.","Изометрии");return;}`
- [Строка 38](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L38) `if(!double.IsFinite(symbolScale)||symbolScale<=0)throw new InvalidDataException("Масштаб элементов должен быть положительным числом.");`
- [Строка 50](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L50) `var snapshot=Capture(doc.Database,doc.Name,ObjectId.Null,line,handles,propertyHints,cancellationToken:token,report:count=>view.Report("Этап 1 из 6 — чтение модели",$"Прочитано объектов: {count}"));`
- [Строка 61](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L61) `view.Report("Этап 2 из 6 — подготовка обозначений","Чтение библиотеки символов проекта…");`
- [Строка 63](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L63) `var catalog=SymbolCatalogCommands.Read(library,token,count=>view.Report("Этап 2 из 6 — подготовка обозначений",$"Прочитано блоков: {count}")) with{SymbolScale=symbolScale,Style=null};`
- [Строка 77](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L77) `if(captureWarnings.Length>0)File.WriteAllLines(warningPath,captureWarnings);`
- [Строка 78](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L78) `progress.Report("Этап 3 из 6 — расчёт компоновки","Подбор размеров участков и границ листов…");`
- [Строка 79](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L79) `var observer=new PlanningObserver(p=>progress.Report("Этап 3 из 6 — расчёт компоновки",p.Detail+$". Проверено вариантов: {p.Variants}"));`
- [Строка 94](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L94) `if(previous is not null){views.Remove(previous.QuarterTurns);views.Insert(0,previous.QuarterTurns);}`
- [Строка 97](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L97) `int selectedView=views[0];`
- [Строка 98](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L98) `int manualCuts=ignoreBreakPoints?0:`
- [Строка 102](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L102) `(snapshot.RequiredCuts?.Count??0);`
- [Строка 109](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L109) `progress.Report("Подбор ракурса",$"Проверяется ракурс {turn+1}.");`
- [Строка 125](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L125) `if(!automaticView||previous?.QuarterTurns==turn||manualCuts==0||geometrySheets<=manualCuts+1)`
- [Строка 131](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L131) `viewFailures.Add($"Ракурс {turn+1}: {ex.Message}");`
- [Строка 143](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L143) `if(warnings.Length>0)File.WriteAllLines(warningPath,warnings);`
- [Строка 151](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L151) `(previous.Pages.Length>1)==(sheets.Count>1))`
- [Строка 152](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L152) `for(int i=0;i<Math.Min(sheets.Count,previous.Pages.Length);i++)`
- [Строка 167](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L167) `"^"+System.Text.RegularExpressions.Regex.Escape(baseStem)+@" \(([1-9]\d*)\)$",`
- [Строка 169](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L169) `if(indexed.Success&&int.TryParse(indexed.Groups[1].Value,out int index))`
- [Строка 174](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L174) `string changedPages=string.Join(", ",Enumerable.Range(1,sheets.Count).Where(number=>cached[number-1] is null));`
- [Строка 175](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L175) `progress.Report("Проверка сохранённого выпуска",changedPages.Length==0`
- [Строка 184](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L184) `Enumerable.Range(0,sheets.Count).All(ReusePublishedDrawing))`
- [Строка 202](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L202) `while(locked.Length>0)`
- [Строка 229](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L229) `for(int i=0;i<sheets.Count;i++)`
- [Строка 232](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L232) `progress.Report("Этап 4 из 6 — выпуск листов",$"Создаётся лист {i+1} из {sheets.Count}. Готово: {i}",i,sheets.Count);`
- [Строка 238](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L238) `string work=Path.Combine(job,$"sheet-{i+1:00}");Directory.CreateDirectory(work);`
- [Строка 243](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L243) `File.WriteAllLines(script,new[]{"SECURELOAD","0","_NETLOAD","\""+assembly+"\"","ISORENDER","ISOPDF","_QUIT","_Y"},new UTF8Encoding(true));`
- [Строка 247](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L247) `start.Environment["ISO_RENDER_TRACE"]=Path.Combine(work,"render-progress.log");start.Environment["ISO_SHEET_NUMBER"]=(i+1).ToString();start.Environment["ISO_SHEET_COUNT"]=sheets.Count.ToString();`
- [Строка 261](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L261) `token.ThrowIfCancellationRequested();throw new IOException($"Превышено время выпуска листа {i+1} (5 минут).");`
- [Строка 265](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L265) `if(process.ExitCode!=0||!File.Exists(drawing)||!File.Exists(pdf))`
- [Строка 268](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L268) `throw new IOException($"Не выпущен лист {i+1}. Код завершения Core Console: {process.ExitCode}."+(missing.Length>0?$" Не созданы: {missing}.":"")+$" Журнал: {Path.Combine(work,"console.log")}");`
- [Строка 270](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L270) `progress.Report("Этап 4 из 6 — выпуск листов",$"Готово листов: {i+1} из {sheets.Count}",i+1,sheets.Count);`
- [Строка 272](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L272) `token.ThrowIfCancellationRequested();progress.Report("Этап 5 из 6 — общий PDF",$"Объединение {pages.Count} страниц…");`
- [Строка 280](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L280) `var retained=Enumerable.Range(0,drawings.Count).Where(ReusePublishedDrawing)`
- [Строка 282](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L282) `var changed=Enumerable.Range(0,drawings.Count).Where(index=>!retained.ContainsKey(names.Drawings[index])).ToArray();`
- [Строка 321](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L321) `progress.Finish("Изометрия создана",$"Листов: {sheets.Count}. Использовано готовых листов: {reused}. DWG и общий PDF сохранены.\n"+folder+(warnings.Length>0?$"\nПредупреждений: {warnings.Length}. Отчёт: "+warningPath:""),t…`
- [Строка 326](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L326) `finally{Interlocked.Exchange(ref busy,0);}`
- [Строка 329](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L329) `catch(OperationCanceledException){Interlocked.Exchange(ref busy,0);view?.Finish("Создание отменено","Итоговые файлы не изменены.");}`
- [Строка 332](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L332) `Interlocked.Exchange(ref busy,0);WriteFailure(job,ex);`
- [Строка 333](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L333) `if(view is not null)view.Finish("Не удалось создать изометрию",ex.Message+(job.Length>0?"\nДиагностика: "+job:""),false,Directory.Exists(job)?job:null);`
- [Строка 361](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L361) `return row>0?manager.GetAllProperties(row,true).ToDictionary(p=>p.Key,p=>p.Value??"",StringComparer.OrdinalIgnoreCase):new();`
- [Строка 364](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L364) `if(selectedLine is null&&seed.Count==0)throw new InvalidOperationException("Выбранный компонент не связан с базой проекта Plant 3D. Выберите трубу из открытого проекта.");`
- [Строка 398](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L398) `while(queue.Count>0)`
- [Строка 402](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L402) `if(seen.Count%25==1)report?.Invoke(seen.Count);`
- [Строка 404](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L404) `for(int i=0;i<ports.Count;i++)`
- [Строка 408](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L408) `if(end.Length==0)end=port.EndType??"";`
- [Строка 414](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L414) `for(int index=0;index<connector.NumSubParts;index++)`
- [Строка 423](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L423) `if(kind.Length==0)kind=subpart is BoltSetSubPart?"BoltSet":values.GetValueOrDefault("PnPClassName","");`
- [Строка 429](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L429) `values["IsoQuantity"]=values.GetValueOrDefault("NumberInSet","1");values["IsoQuantityUnit"]="компл.";`
- [Строка 432](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L432) `else {values["IsoQuantity"]="1";values["IsoQuantityUnit"]="шт.";}`
- [Строка 437](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L437) `for(int i=0;i<attached.Count;i++)`
- [Строка 448](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L448) `(marker.HostHandle.Length==0&&BreakPointPreparation.HostCandidates(parts,marker.Position).Count>0)).ToArray();`
- [Строка 456](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L456) `ObjectId FindHandle(string handle)=>db.GetObjectId(false,new Handle(long.Parse(handle,System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture)),0);`
- [Строка 466](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L466) `if(otherLine.Length==0||otherLine.Equals(line,StringComparison.OrdinalIgnoreCase))continue;`
- [Строка 468](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L468) `var visited=new HashSet<ObjectId>();int physicalCount=0;`
- [Строка 469](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L469) `while(pending.Count>0&&physicalCount<2&&visited.Count<24)`
- [Строка 479](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L479) `for(int k=0;k<itemPorts.Count;k++)`
- [Строка 484](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L484) `for(int k=0;k<neighbors.Count;k++)`
- [Строка 491](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L491) `if(physicalCount>0)externalContexts.Add(new(local,localPort,otherLine,remote,remotePort,remoteParts,remoteLinks.Distinct().ToArray()));`
- [Строка 493](src/IsoGenerator.AutoCAD/GenerateCommand.cs#L493) `return new(1,filename,parts,links.Values.ToArray(),warnings,accessories,markers,ExternalLineContexts:externalContexts);`

### IsoMarkerCommands.cs

- [Строка 26](src/IsoGenerator.AutoCAD/IsoMarkerCommands.cs#L26) `if(depth>3||current is Table or MText or DBText)return;`
- [Строка 27](src/IsoGenerator.AutoCAD/IsoMarkerCommands.cs#L27) `using var collection=new DBObjectCollection();try{current.Explode(collection);foreach(DBObject child in collection){using(child)if(child is Entity e)Read(e,depth+1);}}catch(Autodesk.AutoCAD.Runtime.Exception){}`
- [Строка 29](src/IsoGenerator.AutoCAD/IsoMarkerCommands.cs#L29) `Read(entity,0);if(segments.Count>0)rows.Add(new{handle=id.Handle.ToString(),text="geometry:"+entity.Layer+":"+entity.GetType().Name,lines=segments});continue;`
- [Строка 64](src/IsoGenerator.AutoCAD/IsoMarkerCommands.cs#L64) `if(property.GetIndexParameters().Length>0||property.GetMethod is null)continue;`

### IsoMarkerReader.cs

- [Строка 15](src/IsoGenerator.AutoCAD/IsoMarkerReader.cs#L15) `int kind=-1;`
- [Строка 20](src/IsoGenerator.AutoCAD/IsoMarkerReader.cs#L20) `{using var data=record.Data;foreach(var value in data?.AsArray()??Array.Empty<Autodesk.AutoCAD.DatabaseServices.TypedValue>())if(value.TypeCode==90){kind=Convert.ToInt32(value.Value);break;}}`
- [Строка 23](src/IsoGenerator.AutoCAD/IsoMarkerReader.cs#L23) `if(ports.Count==0)`
- [Строка 29](src/IsoGenerator.AutoCAD/IsoMarkerReader.cs#L29) `var point=ports[0].Position;`
- [Строка 32](src/IsoGenerator.AutoCAD/IsoMarkerReader.cs#L32) `for(int i=0;i<links.Count;i++)`

### IsoProgressWindow.cs

- [Строка 24](src/IsoGenerator.AutoCAD/IsoProgressWindow.cs#L24) `public void Report(string stage,string detail,int completed=0,int total=0)=>Post(()=>form.Report(stage,detail,completed,total));`
- [Строка 38](src/IsoGenerator.AutoCAD/IsoProgressWindow.cs#L38) `readonly System.Windows.Forms.Timer timer=new(){Interval=500};readonly Stopwatch clock=Stopwatch.StartNew();readonly CancellationTokenSource cancellation;`
- [Строка 42](src/IsoGenerator.AutoCAD/IsoProgressWindow.cs#L42) `this.cancellation=cancellation;Text="Создание изометрии";ClientSize=new Size(560,275);Font=new Font("Segoe UI",10);`
- [Строка 44](src/IsoGenerator.AutoCAD/IsoProgressWindow.cs#L44) `Controls.Add(new Label{Text="Линия: "+line,Left=20,Top=18,Width=520,Height=28,AutoEllipsis=true});`
- [Строка 45](src/IsoGenerator.AutoCAD/IsoProgressWindow.cs#L45) `stage.SetBounds(20,55,520,28);stage.Font=new Font(Font,FontStyle.Bold);detail.SetBounds(20,90,520,55);`
- [Строка 46](src/IsoGenerator.AutoCAD/IsoProgressWindow.cs#L46) `bar.SetBounds(20,150,520,22);bar.Style=ProgressBarStyle.Marquee;bar.MarqueeAnimationSpeed=30;`
- [Строка 47](src/IsoGenerator.AutoCAD/IsoProgressWindow.cs#L47) `elapsed.Text="Прошло: 00:00:00";elapsed.SetBounds(20,185,350,24);cancel.SetBounds(410,225,130,32);cancel.Text="Отмена";cancel.Click+=(_,_)=>{if(running)RequestCancel();else Close();};`
- [Строка 48](src/IsoGenerator.AutoCAD/IsoProgressWindow.cs#L48) `open.SetBounds(255,225,145,32);open.Text="Открыть папку";open.Visible=false;open.Click+=(_,_)=>{if(folder is not null)try{Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}catch(Exception ex){MessageBox.Sh…`
- [Строка 51](src/IsoGenerator.AutoCAD/IsoProgressWindow.cs#L51) `FormClosed+=(_,_)=>{timer.Dispose();cancellation.Dispose();};Report("Этап 1 из 6 — чтение модели","Подготовка данных трубопровода…",0,0);`
- [Строка 58](src/IsoGenerator.AutoCAD/IsoProgressWindow.cs#L58) `bar.Style=total>0?ProgressBarStyle.Continuous:ProgressBarStyle.Marquee;`
- [Строка 59](src/IsoGenerator.AutoCAD/IsoProgressWindow.cs#L59) `if(total>0){bar.Maximum=total;bar.Value=Math.Clamp(completed,0,total);}`
- [Строка 61](src/IsoGenerator.AutoCAD/IsoProgressWindow.cs#L61) `public void Publishing(){canCancel=false;cancel.Enabled=false;stage.Text="Этап 6 из 6 — сохранение комплекта";detail.Text="Сохраняем DWG и общий PDF…";bar.Style=ProgressBarStyle.Marquee;}`
- [Строка 64](src/IsoGenerator.AutoCAD/IsoProgressWindow.cs#L64) `running=false;canCancel=false;clock.Stop();timer.Stop();stage.Text=title;detail.Text=text;bar.Style=ProgressBarStyle.Continuous;bar.Maximum=100;bar.Value=success?100:0;`

### OutputConflictDialog.cs

- [Строка 7](src/IsoGenerator.AutoCAD/OutputConflictDialog.cs#L7) `using var form=new Form{Text="Файлы изометрии уже существуют",ClientSize=new System.Drawing.Size(475,170),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterScreen,MinimizeBox=false,MaximizeB…`
- [Строка 8](src/IsoGenerator.AutoCAD/OutputConflictDialog.cs#L8) `form.Controls.Add(new Label{Text="В папке уже есть файлы с этим номером линии.\nВыберите способ сохранения нового комплекта:\n"+folder,Left=18,Top=18,Width=438,Height=85,AutoEllipsis=true});`
- [Строка 9](src/IsoGenerator.AutoCAD/OutputConflictDialog.cs#L9) `var replace=new Button{Text="Заменить",Left=18,Top=120,Width=125,Height=32,DialogResult=DialogResult.Yes};`
- [Строка 10](src/IsoGenerator.AutoCAD/OutputConflictDialog.cs#L10) `var indexed=new Button{Text="Добавить индекс",Left=155,Top=120,Width=165,Height=32,DialogResult=DialogResult.No};`
- [Строка 11](src/IsoGenerator.AutoCAD/OutputConflictDialog.cs#L11) `var cancel=new Button{Text="Отмена",Left=332,Top=120,Width=125,Height=32,DialogResult=DialogResult.Cancel};`

### PdfCommands.cs

- [Строка 24](src/IsoGenerator.AutoCAD/PdfCommands.cs#L24) `App.SetSystemVariable("BACKGROUNDPLOT",0);`
- [Строка 32](src/IsoGenerator.AutoCAD/PdfCommands.cs#L32) `var media=v.GetCanonicalMediaNameList(settings).Cast<string>().FirstOrDefault(s=>s.Contains("594.00_x_420.00")&&s.Contains("full_bleed"))`
- [Строка 33](src/IsoGenerator.AutoCAD/PdfCommands.cs#L33) `??v.GetCanonicalMediaNameList(settings).Cast<string>().First(s=>s.Contains("594.00_x_420.00"));`
- [Строка 37](src/IsoGenerator.AutoCAD/PdfCommands.cs#L37) `v.SetPlotWindowArea(settings,new Extents2d(0,0,594,420));`
- [Строка 44](src/IsoGenerator.AutoCAD/PdfCommands.cs#L44) `engine.BeginPlot(null,null);engine.BeginDocument(info,doc.Name,null,1,true,output);`

### RememberedIsoOutput.cs

- [Строка 41](src/IsoGenerator.AutoCAD/RememberedIsoOutput.cs#L41) `settings.TryGetProperty("minimumPipeLength",out var length)?length.GetDouble():10);`
- [Строка 54](src/IsoGenerator.AutoCAD/RememberedIsoOutput.cs#L54) `entry.Layout.Sheets is not {Count:>0}||entry.Pages?.Length!=entry.Layout.Sheets.Count||`
- [Строка 55](src/IsoGenerator.AutoCAD/RememberedIsoOutput.cs#L55) `string.IsNullOrWhiteSpace(entry.PublishedPdf)||entry.QuarterTurns is <0 or >3)return null;`
- [Строка 56](src/IsoGenerator.AutoCAD/RememberedIsoOutput.cs#L56) `for(int i=0;i<entry.Pages.Length;i++)`
- [Строка 60](src/IsoGenerator.AutoCAD/RememberedIsoOutput.cs#L60) `if(sheet?.Scene is null||sheet.Snapshot?.Parts is null||sheet.Number!=i+1||`

### RenderCommands.cs

- [Строка 27](src/IsoGenerator.AutoCAD/RenderCommands.cs#L27) `string sheetNumber=Environment.GetEnvironmentVariable("ISO_SHEET_NUMBER")??"1";`
- [Строка 28](src/IsoGenerator.AutoCAD/RenderCommands.cs#L28) `static string EscapeMText(string text)=>text.Replace(((char)92).ToString(),new string((char)92,2)).Replace("{",((char)92)+"{").Replace("}",((char)92)+"}");`
- [Строка 32](src/IsoGenerator.AutoCAD/RenderCommands.cs#L32) `var notes=new List<string>{"1.      * Размер для справок;"};`
- [Строка 33](src/IsoGenerator.AutoCAD/RenderCommands.cs#L33) `if(!string.IsNullOrWhiteSpace(valvesDocument))notes.Add("2.      Трубопроводную арматуру см. "+valvesDocument+";");`
- [Строка 34](src/IsoGenerator.AutoCAD/RenderCommands.cs#L34) `if(!string.IsNullOrWhiteSpace(supportsDocument))notes.Add("3.      Опоры трубопроводов см. "+supportsDocument+";");`
- [Строка 35](src/IsoGenerator.AutoCAD/RenderCommands.cs#L35) `bool multipleSheets=int.TryParse(Environment.GetEnvironmentVariable("ISO_SHEET_COUNT"),out int sheetCount)&&sheetCount>1;`
- [Строка 37](src/IsoGenerator.AutoCAD/RenderCommands.cs#L37) `notes.Add("4.      Схема соединений (монтажная): "+scene.Line+sheetSuffix+".");`
- [Строка 42](src/IsoGenerator.AutoCAD/RenderCommands.cs#L42) `ResultBuffer SourceData(string source)=>new(new TypedValue(1001,"ISOGENERATOR"),`
- [Строка 43](src/IsoGenerator.AutoCAD/RenderCommands.cs#L43) `new TypedValue(1000,scene.OriginalHandles?.GetValueOrDefault(source,source)??source),`
- [Строка 44](src/IsoGenerator.AutoCAD/RenderCommands.cs#L44) `new TypedValue(1000,scene.SourceDrawing),new TypedValue(1000,scene.SourceGuids?.GetValueOrDefault(source,"")??""));`
- [Строка 52](src/IsoGenerator.AutoCAD/RenderCommands.cs#L52) `var oid=db.GetObjectId(false,new Handle(long.Parse(handle,NumberStyles.HexNumber,CultureInfo.InvariantCulture)),0);`
- [Строка 60](src/IsoGenerator.AutoCAD/RenderCommands.cs#L60) `gost.FileName="GOST_B.TTF";gost.BigFontFileName="";gost.TextSize=tuning.LeaderTextHeight;gost.XScale=1;gost.ObliquingAngle=Math.PI/12;`
- [Строка 63](src/IsoGenerator.AutoCAD/RenderCommands.cs#L63) `if(bt.Has("ISO-ARROW-20"))arrowId=bt["ISO-ARROW-20"];`
- [Строка 66](src/IsoGenerator.AutoCAD/RenderCommands.cs#L66) `bt.UpgradeOpen();var arrow=new BlockTableRecord{Name="ISO-ARROW-20"};arrowId=bt.Add(arrow);tr.AddNewlyCreatedDBObject(arrow,true);`
- [Строка 67](src/IsoGenerator.AutoCAD/RenderCommands.cs#L67) `double halfWidth=Math.Tan(10*Math.PI/180);`
- [Строка 68](src/IsoGenerator.AutoCAD/RenderCommands.cs#L68) `var triangle=new Solid(Point3d.Origin,new Point3d(-1,-halfWidth,0),new Point3d(-1,halfWidth,0),new Point3d(-1,halfWidth,0)){ColorIndex=0,LineWeight=LineWeight.ByBlock};`
- [Строка 79](src/IsoGenerator.AutoCAD/RenderCommands.cs#L79) `dimensionFont.FileName="GOST_A.TTF";dimensionFont.TextSize=tuning.DimensionTextHeight;dimensionFont.XScale=1;dimensionFont.ObliquingAngle=0;`
- [Строка 80](src/IsoGenerator.AutoCAD/RenderCommands.cs#L80) `var dimStyle=(DimStyleTableRecord)tr.GetObject(dimensionStyle,OpenMode.ForWrite);dimStyle.Dimtxsty=dimensionTextStyle;dimStyle.Dimtxt=tuning.DimensionTextHeight;dimStyle.Dimjust=0;dimStyle.Dimatfit=3;dimStyle.Dimtmove=2;di…`
- [Строка 93](src/IsoGenerator.AutoCAD/RenderCommands.cs#L93) `leaderStyle.DoglegLength=1;leaderStyle.LandingGap=1;leaderStyle.ExtendLeaderToText=true;`
- [Строка 107](src/IsoGenerator.AutoCAD/RenderCommands.cs#L107) `if(table is null||table.Columns.Count!=5||table.Cells[0,0].TextString!="Спецификация трубопровода")throw new InvalidDataException("Expected five-column BOM missing");`
- [Строка 113](src/IsoGenerator.AutoCAD/RenderCommands.cs#L113) `lineTypes.UpgradeOpen();var lt=new LinetypeTableRecord{Name=contextType,AsciiDescription="Adjacent sheet context",PatternLength=tuning.DashLength+tuning.DashGap,NumDashes=2};`
- [Строка 114](src/IsoGenerator.AutoCAD/RenderCommands.cs#L114) `lt.SetDashLengthAt(0,tuning.DashLength);lt.SetDashLengthAt(1,-tuning.DashGap);lineTypes.Add(lt);tr.AddNewlyCreatedDBObject(lt,true);`
- [Строка 120](src/IsoGenerator.AutoCAD/RenderCommands.cs#L120) `var layer=new LayerTableRecord{Name=name,Color=Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci,7)};`
- [Строка 124](src/IsoGenerator.AutoCAD/RenderCommands.cs#L124) `weldLayer.Color=Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci,1);`
- [Строка 131](src/IsoGenerator.AutoCAD/RenderCommands.cs#L131) `e.Layer=layer;e.ColorIndex=256;e.LineWeight=layer=="ISO-GEOMETRY"?LineWeight.LineWeight050:LineWeight.LineWeight013;ms.AppendEntity(e);tr.AddNewlyCreatedDBObject(e,true);`
- [Строка 135](src/IsoGenerator.AutoCAD/RenderCommands.cs#L135) `Point3d Pt(SheetPoint p)=>new(p.X,p.Y,0);`
- [Строка 170](src/IsoGenerator.AutoCAD/RenderCommands.cs#L170) `var projected=IsometricProjection.Project(new ModelPoint(0,1,0),quarterTurns:scene.QuarterTurns);`
- [Строка 171](src/IsoGenerator.AutoCAD/RenderCommands.cs#L171) `string english=(projected.Y>0?"Upper":"Lower")+(projected.X>0?"Right":"Left");`
- [Строка 172](src/IsoGenerator.AutoCAD/RenderCommands.cs#L172) `string russian=(projected.Y>0?"Верхний":"Нижний")+(projected.X>0?"Правый":"Левый");`
- [Строка 186](src/IsoGenerator.AutoCAD/RenderCommands.cs#L186) `var tip=stroke.Points[1];`
- [Строка 187](src/IsoGenerator.AutoCAD/RenderCommands.cs#L187) `var baseCenter=new SheetPoint((stroke.Points[0].X+stroke.Points[2].X)/2,(stroke.Points[0].Y+stroke.Points[2].Y)/2);`
- [Строка 190](src/IsoGenerator.AutoCAD/RenderCommands.cs#L190) `double scale=3*Math.Sqrt(dx*dx+dy*dy)/.3125;`
- [Строка 199](src/IsoGenerator.AutoCAD/RenderCommands.cs#L199) `var arrow=new Solid(Pt(stroke.Points[0]),Pt(stroke.Points[1]),Pt(stroke.Points[2]),Pt(stroke.Points[2]));`
- [Строка 202](src/IsoGenerator.AutoCAD/RenderCommands.cs#L202) `var pl=new Polyline();for(int i=0;i<stroke.Points.Count;i++)pl.AddVertexAt(i,new Point2d(stroke.Points[i].X,stroke.Points[i].Y),0,0,0);`
- [Строка 206](src/IsoGenerator.AutoCAD/RenderCommands.cs#L206) `if(stroke.Role=="flow-shaft"){pl.ConstantWidth=1;pl.LineWeight=LineWeight.LineWeight050;}`
- [Строка 208](src/IsoGenerator.AutoCAD/RenderCommands.cs#L208) `if(stroke.Role.StartsWith("context-",StringComparison.Ordinal)){pl.LineWeight=LineWeight.LineWeight013;pl.LinetypeScale=1/Math.Max(.0001,db.Ltscale);pl.Plinegen=true;}`
- [Строка 213](src/IsoGenerator.AutoCAD/RenderCommands.cs#L213) `if(scene.Symbols is {Count:>0})`
- [Строка 223](src/IsoGenerator.AutoCAD/RenderCommands.cs#L223) `var shape=new Polyline();int index=0;`
- [Строка 224](src/IsoGenerator.AutoCAD/RenderCommands.cs#L224) `foreach(var point in contour)shape.AddVertexAt(index++,new Point2d(glyph.Origin.X+glyph.XAxis.X*point.X+glyph.YAxis.X*point.Y,glyph.Origin.Y+glyph.XAxis.Y*point.X+glyph.YAxis.Y*point.Y),0,0,0);`
- [Строка 234](src/IsoGenerator.AutoCAD/RenderCommands.cs#L234) `if(glyph.Context){reference.LineWeight=LineWeight.LineWeight013;reference.LinetypeScale=1/Math.Max(.0001,db.Ltscale);}`
- [Строка 246](src/IsoGenerator.AutoCAD/RenderCommands.cs#L246) `var baselineA=indicator.Points[0];`
- [Строка 247](src/IsoGenerator.AutoCAD/RenderCommands.cs#L247) `var baselineB=indicator.Points[1];`
- [Строка 248](src/IsoGenerator.AutoCAD/RenderCommands.cs#L248) `double centerX=(baselineA.X+baselineB.X)/2,centerY=(baselineA.Y+baselineB.Y)/2;`
- [Строка 249](src/IsoGenerator.AutoCAD/RenderCommands.cs#L249) `var symbol=new Polyline{ColorIndex=0,LineWeight=LineWeight.ByBlock};`
- [Строка 250](src/IsoGenerator.AutoCAD/RenderCommands.cs#L250) `for(int i=0;i<indicator.Points.Count;i++)`
- [Строка 253](src/IsoGenerator.AutoCAD/RenderCommands.cs#L253) `symbol.AddVertexAt(i,new Point2d(point.X-centerX,point.Y-centerY),0,0,0);`
- [Строка 259](src/IsoGenerator.AutoCAD/RenderCommands.cs#L259) `Rotation=label.Rotation,ColorIndex=0,LineWeight=LineWeight.ByBlock,`
- [Строка 261](src/IsoGenerator.AutoCAD/RenderCommands.cs#L261) `AlignmentPoint=new Point3d(normal.X*2.8,normal.Y*2.8,0)};`
- [Строка 263](src/IsoGenerator.AutoCAD/RenderCommands.cs#L263) `var origin=new SheetPoint(label.Position.X-normal.X*2.8,label.Position.Y-normal.Y*2.8);`
- [Строка 271](src/IsoGenerator.AutoCAD/RenderCommands.cs#L271) `leader.ArrowSymbolId=arrowId;leader.ArrowSize=tuning.ArrowSize;leader.EnableLanding=true;leader.EnableDogleg=true;leader.DoglegLength=tuning.LeaderLanding;leader.LandingGap=1;`
- [Строка 279](src/IsoGenerator.AutoCAD/RenderCommands.cs#L279) `var contentLines=external?textLines:textLines.Take(1);`
- [Строка 284](src/IsoGenerator.AutoCAD/RenderCommands.cs#L284) `leader.SetDogleg(cluster,new Vector3d(label.Position.X>=knee.X?1:-1,0,0));`
- [Строка 286](src/IsoGenerator.AutoCAD/RenderCommands.cs#L286) `leader.TextLocation=Pt(new SheetPoint(label.Position.X,label.Position.Y+4.8));`
- [Строка 289](src/IsoGenerator.AutoCAD/RenderCommands.cs#L289) `var following=external?Array.Empty<string>():textLines.Skip(1).ToArray();`
- [Строка 290](src/IsoGenerator.AutoCAD/RenderCommands.cs#L290) `for(int i=0;i<following.Length;i++)`
- [Строка 292](src/IsoGenerator.AutoCAD/RenderCommands.cs#L292) `var under=new DBText{Position=Pt(new SheetPoint(label.Position.X,label.Position.Y-3.3-i*4.8)),Height=tuning.LeaderTextHeight,TextString=following[i],TextStyleId=gostStyle,Oblique=Math.PI/12};`
- [Строка 298](src/IsoGenerator.AutoCAD/RenderCommands.cs#L298) `var text=new DBText{Position=Pt(label.Position),Height=tuning.LeaderTextHeight,TextString=label.Text,TextStyleId=gostStyle,Rotation=label.Rotation,Oblique=label.Role=="slope-value"?label.Rotation:Math.PI/12};`
- [Строка 306](src/IsoGenerator.AutoCAD/RenderCommands.cs#L306) `if(len<.01)continue;`
- [Строка 307](src/IsoGenerator.AutoCAD/RenderCommands.cs#L307) `var mid=d.DimensionLinePoint is {} dp?Pt(dp):new Point3d((d.Start.X+d.End.X)/2-dy/len*7,(d.Start.Y+d.End.Y)/2+dx/len*7,0);`
- [Строка 309](src/IsoGenerator.AutoCAD/RenderCommands.cs#L309) `{Dimtxt=tuning.DimensionTextHeight,Dimasz=tuning.ArrowSize,Dimtsz=0,Dimtad=1,Dimexo=tuning.ExtensionOffset,Dimexe=tuning.ExtensionOvershoot};`
- [Строка 317](src/IsoGenerator.AutoCAD/RenderCommands.cs#L317) `if(Math.Abs(normalDot)>1e-6)`
- [Строка 319](src/IsoGenerator.AutoCAD/RenderCommands.cs#L319) `double textAngle=Math.Atan(axisDot/normalDot)*180/Math.PI;`
- [Строка 320](src/IsoGenerator.AutoCAD/RenderCommands.cs#L320) `dim.DimensionText="{\\Q"+textAngle.ToString("0.######",CultureInfo.InvariantCulture)+";"+d.Label+"}";`
- [Строка 323](src/IsoGenerator.AutoCAD/RenderCommands.cs#L323) `dim.Dimtmove=2;dim.Dimtix=true;dim.Dimjust=0;dim.Dimatfit=3;dim.UsingDefaultTextPosition=false;`
- [Строка 333](src/IsoGenerator.AutoCAD/RenderCommands.cs#L333) `var values=metadata.AsArray().Concat(new[]{new TypedValue(1000,d.Role),new TypedValue(1000,d.ChainId),new TypedValue(1040,d.Millimeters)}).ToArray();`
- [Строка 337](src/IsoGenerator.AutoCAD/RenderCommands.cs#L337) `if(scene.Welds is {Count:>0})`
- [Строка 349](src/IsoGenerator.AutoCAD/RenderCommands.cs#L349) `if(tr.GetObject(id,OpenMode.ForWrite) is Entity element)element.ColorIndex=0;`
- [Строка 352](src/IsoGenerator.AutoCAD/RenderCommands.cs#L352) `var reference=new BlockReference(Pt(weld.Position),blockId){ScaleFactors=new Scale3d(15)};`
- [Строка 354](src/IsoGenerator.AutoCAD/RenderCommands.cs#L354) `var values=reference.XData.AsArray().Concat(new[]{new TypedValue(1000,"weld"),new TypedValue(1000,weld.FirstHandle+":"+weld.FirstPort),new TypedValue(1000,weld.SecondHandle+":"+weld.SecondPort),new TypedValue(1000,weld.Joi…`
- [Строка 358](src/IsoGenerator.AutoCAD/RenderCommands.cs#L358) `table.UpgradeOpen();if(scene.BomOnly)table.Cells[0,0].TextString="Спецификация трубопровода (продолжение)";table.BreakEnabled=false;if(table.Rows.Count>2)table.DeleteRows(2,table.Rows.Count-2);`
- [Строка 360](src/IsoGenerator.AutoCAD/RenderCommands.cs#L360) `if(displayRows>0)table.InsertRows(2,BomCategories.ItemHeight,displayRows);`
- [Строка 361](src/IsoGenerator.AutoCAD/RenderCommands.cs#L361) `int tableRow=2;`
- [Строка 365](src/IsoGenerator.AutoCAD/RenderCommands.cs#L365) `for(int j=0;j<5;j++){table.Cells[tableRow,j].TextString="";table.Cells[tableRow,j].TextHeight=BomCategories.TextHeight;table.Cells[tableRow,j].TextStyleId=gostStyle;}`
- [Строка 366](src/IsoGenerator.AutoCAD/RenderCommands.cs#L366) `table.Cells[tableRow,2].TextString="\\L"+BomCategories.Title(category.Key)+"\\l";`
- [Строка 367](src/IsoGenerator.AutoCAD/RenderCommands.cs#L367) `table.Cells[tableRow,2].Alignment=CellAlignment.MiddleCenter;`
- [Строка 372](src/IsoGenerator.AutoCAD/RenderCommands.cs#L372) `for(int j=0;j<5;j++){table.Cells[tableRow,j].TextString=values[j];table.Cells[tableRow,j].TextHeight=BomCategories.TextHeight;table.Cells[tableRow,j].TextStyleId=gostStyle;}`
- [Строка 376](src/IsoGenerator.AutoCAD/RenderCommands.cs#L376) `for(int i=2;i<table.Rows.Count;i++)for(int j=0;j<table.Columns.Count;j++)`
- [Строка 380](src/IsoGenerator.AutoCAD/RenderCommands.cs#L380) `{border.IsVisible=true;border.Color=Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci,7);border.LineWeight=LineWeight.LineWeight013;}`
- [Строка 382](src/IsoGenerator.AutoCAD/RenderCommands.cs#L382) `for(int i=0;i<2;i++)for(int j=0;j<5;j++){table.Cells[i,j].TextStyleId=gostStyle;table.Cells[i,j].TextHeight=BomCategories.TextHeight;}`
- [Строка 404](src/IsoGenerator.AutoCAD/RenderCommands.cs#L404) `if(dimensionIds.Count>0)`
- [Строка 434](src/IsoGenerator.AutoCAD/RenderCommands.cs#L434) `for(int i=0;i<stroke.Points.Count;i++)`
- [Строка 435](src/IsoGenerator.AutoCAD/RenderCommands.cs#L435) `contour.AddVertexAt(i,new Point2d(stroke.Points[i].X,stroke.Points[i].Y),0,0,0);`
- [Строка 438](src/IsoGenerator.AutoCAD/RenderCommands.cs#L438) `contour.ColorIndex=256;`
- [Строка 440](src/IsoGenerator.AutoCAD/RenderCommands.cs#L440) `contour.LinetypeScale=1/Math.Max(.0001,db.Ltscale);`
- [Строка 447](src/IsoGenerator.AutoCAD/RenderCommands.cs#L447) `if(insulationIds.Count>0)order.MoveToBottom(insulationIds);`

### SourceCommands.cs

- [Строка 18](src/IsoGenerator.AutoCAD/SourceCommands.cs#L18) `var values=data?.AsArray().Where(v=>v.TypeCode==1000).Select(v=>(string)v.Value).ToArray();`
- [Строка 19](src/IsoGenerator.AutoCAD/SourceCommands.cs#L19) `if(values is null||values.Length<3){doc.Editor.WriteMessage("\nУ объекта нет связи с исходной моделью.");return;}`
- [Строка 20](src/IsoGenerator.AutoCAD/SourceCommands.cs#L20) `doc.Editor.WriteMessage($"\nЧертёж модели: {values[1]}\nHandle: {values[0]}\nGUID: {values[2]}\n");`

### SymbolCatalogCommands.cs

- [Строка 28](src/IsoGenerator.AutoCAD/SymbolCatalogCommands.cs#L28) `if(definitions.Count%20==0)report?.Invoke(definitions.Count);`
- [Строка 35](src/IsoGenerator.AutoCAD/SymbolCatalogCommands.cs#L35) `if(depth>24||!active.Add(definition.ObjectId)){complex=true;return;}`
- [Строка 43](src/IsoGenerator.AutoCAD/SymbolCatalogCommands.cs#L43) `ReadCurves(nested,transform*reference.BlockTransform,depth+1);`
- [Строка 59](src/IsoGenerator.AutoCAD/SymbolCatalogCommands.cs#L59) `int segments=poly.Closed?poly.NumberOfVertices:poly.NumberOfVertices-1;`
- [Строка 60](src/IsoGenerator.AutoCAD/SymbolCatalogCommands.cs#L60) `for(int segment=0;segment<segments;segment++)`
- [Строка 62](src/IsoGenerator.AutoCAD/SymbolCatalogCommands.cs#L62) `int steps=Math.Abs(poly.GetBulgeAt(segment))<1e-10?1:24;`
- [Строка 63](src/IsoGenerator.AutoCAD/SymbolCatalogCommands.cs#L63) `for(int step=0;step<steps;step++)Add(poly.GetPointAtParameter(segment+(double)step/steps));`
- [Строка 68](src/IsoGenerator.AutoCAD/SymbolCatalogCommands.cs#L68) `else for(int i=0;i<=64;i++)`
- [Строка 69](src/IsoGenerator.AutoCAD/SymbolCatalogCommands.cs#L69) `Add(curve.GetPointAtParameter(curve.StartParam+(curve.EndParam-curve.StartParam)*i/64));`
- [Строка 70](src/IsoGenerator.AutoCAD/SymbolCatalogCommands.cs#L70) `if(path.Count<2){complex=true;continue;}`
- [Строка 72](src/IsoGenerator.AutoCAD/SymbolCatalogCommands.cs#L72) `points.AddRange(path.Select(point=>new Point3d(point.X,point.Y,0)));`
- [Строка 82](src/IsoGenerator.AutoCAD/SymbolCatalogCommands.cs#L82) `ReadCurves(block,Matrix3d.Identity,0);`
- [Строка 84](src/IsoGenerator.AutoCAD/SymbolCatalogCommands.cs#L84) `if(complex||points.Count==0)continue;`
- [Строка 92](src/IsoGenerator.AutoCAD/SymbolCatalogCommands.cs#L92) `if(match.Groups[2].Value.Equals("Z",StringComparison.OrdinalIgnoreCase))continue;`
- [Строка 93](src/IsoGenerator.AutoCAD/SymbolCatalogCommands.cs#L93) `string key=(match.Groups[1].Value.StartsWith("Center",StringComparison.OrdinalIgnoreCase)||match.Groups[1].Value.Equals("CP",StringComparison.OrdinalIgnoreCase))?"CenterPoint":match.Groups[1].Value;var old=ports.GetValueOr…`
- [Строка 95](src/IsoGenerator.AutoCAD/SymbolCatalogCommands.cs#L95) `else if(property.Value is double n)ports[key]=match.Groups[2].Value.ToUpperInvariant()=="Y"?new(old.X,n):new(n,old.Y);`

### SymbolLibrary.cs

- [Строка 19](src/IsoGenerator.AutoCAD/SymbolLibrary.cs#L19) `var output=new BlockTableRecord{Name=$"ISO-{glyph.SourceId}-{glyph.Block}{(glyph.Variant.Length>0?"-"+glyph.Variant:"")}"};`
- [Строка 21](src/IsoGenerator.AutoCAD/SymbolLibrary.cs#L21) `if(glyph.LocalContours is {Count:>0})`
- [Строка 25](src/IsoGenerator.AutoCAD/SymbolLibrary.cs#L25) `if(contour.Count<2)continue;`
- [Строка 26](src/IsoGenerator.AutoCAD/SymbolLibrary.cs#L26) `var polyline=new Polyline();int vertex=0;`
- [Строка 30](src/IsoGenerator.AutoCAD/SymbolLibrary.cs#L30) `polyline.AddVertexAt(vertex++,new Point2d(glyph.XAxis.X*point.X+glyph.YAxis.X*point.Y,glyph.XAxis.Y*point.X+glyph.YAxis.Y*point.Y),0,0,0);`
- [Строка 32](src/IsoGenerator.AutoCAD/SymbolLibrary.cs#L32) `polyline.Layer="0";polyline.ColorIndex=0;polyline.LineWeight=LineWeight.ByBlock;polyline.Linetype="ByBlock";polyline.Plinegen=true;polyline.LinetypeScale=1;`
- [Строка 37](src/IsoGenerator.AutoCAD/SymbolLibrary.cs#L37) `int count=0;`
- [Строка 45](src/IsoGenerator.AutoCAD/SymbolLibrary.cs#L45) `var result=new Polyline();int vertex=0;`
- [Строка 46](src/IsoGenerator.AutoCAD/SymbolLibrary.cs#L46) `void Vertex(Point3d p){result.AddVertexAt(vertex++,Project(p),0,0,0);}`
- [Строка 49](src/IsoGenerator.AutoCAD/SymbolLibrary.cs#L49) `int segments=poly.Closed?poly.NumberOfVertices:poly.NumberOfVertices-1;`
- [Строка 50](src/IsoGenerator.AutoCAD/SymbolLibrary.cs#L50) `for(int segment=0;segment<segments;segment++)`
- [Строка 52](src/IsoGenerator.AutoCAD/SymbolLibrary.cs#L52) `int steps=Math.Abs(poly.GetBulgeAt(segment))<1e-10?1:24;`
- [Строка 53](src/IsoGenerator.AutoCAD/SymbolLibrary.cs#L53) `for(int step=0;step<steps;step++)Vertex(poly.GetPointAtParameter(segment+(double)step/steps));`
- [Строка 61](src/IsoGenerator.AutoCAD/SymbolLibrary.cs#L61) `int steps=curve is Circle?96:48;`
- [Строка 62](src/IsoGenerator.AutoCAD/SymbolLibrary.cs#L62) `for(int i=0;i<=steps;i++)Vertex(curve.GetPointAtParameter(curve.StartParam+(curve.EndParam-curve.StartParam)*i/steps));`
- [Строка 64](src/IsoGenerator.AutoCAD/SymbolLibrary.cs#L64) `result.Layer="0";result.ColorIndex=0;result.LineWeight=LineWeight.ByBlock;result.Linetype="ByBlock";result.Plinegen=true;result.LinetypeScale=1;`
- [Строка 67](src/IsoGenerator.AutoCAD/SymbolLibrary.cs#L67) `if(count==0)throw new InvalidDataException($"Empty symbol block {glyph.Block}");`

### TemplateCommands.cs

- [Строка 49](src/IsoGenerator.AutoCAD/TemplateCommands.cs#L49) `Polyline p => new { p.Closed, vertices = Enumerable.Range(0, p.NumberOfVertices).Select(i => new { point = Point(p.GetPoint3dAt(i)), bulge = p.GetBulgeAt(i) }).ToArray() },`
- [Строка 58](src/IsoGenerator.AutoCAD/TemplateCommands.cs#L58) `entities.Add(new { handle = e.Handle.ToString(), type = e.GetType().Name, e.Layer, e.ColorIndex, e.LineWeight, bounds, details, sourceLink = e.GetXDataForApplication("ISOGENERATOR")?.AsArray().Where(v=>v.TypeCode==1000).Se…`
- [Строка 70](src/IsoGenerator.AutoCAD/TemplateCommands.cs#L70) `var report = new { schemaVersion = 1, source = db.Filename, units = db.Insunits.ToString(),`
- [Строка 104](src/IsoGenerator.AutoCAD/TemplateCommands.cs#L104) `for (int i = 0; i < t.Rows.Count; i++)`
- [Строка 105](src/IsoGenerator.AutoCAD/TemplateCommands.cs#L105) `rows.Add(Enumerable.Range(0, t.Columns.Count).Select(j => t.Cells[i, j].TextString).ToArray());`
- [Строка 106](src/IsoGenerator.AutoCAD/TemplateCommands.cs#L106) `return new { rows, textHeights=Enumerable.Range(0,t.Rows.Count).Select(i=>Enumerable.Range(0,t.Columns.Count).Select(j=>t.Cells[i,j].TextHeight).ToArray()).ToArray() };`

### Program.cs

- [Строка 4](src/IsoGenerator.Cli/Program.cs#L4) `if(args.Length<2||args.Length>4)throw new ArgumentException("Usage: snapshot.json scene.json [symbol-map.xml] [--sheets]");`
- [Строка 6](src/IsoGenerator.Cli/Program.cs#L6) `var snapshot=JsonSerializer.Deserialize<PipelineSnapshot>(File.ReadAllText(args[0]))??throw new Exception("Missing snapshot");`
- [Строка 8](src/IsoGenerator.Cli/Program.cs#L8) `File.WriteAllText(Path.ChangeExtension(args[1],"validation.json"),JsonSerializer.Serialize(validation,new JsonSerializerOptions{WriteIndented=true}));`
- [Строка 9](src/IsoGenerator.Cli/Program.cs#L9) `var catalogFile=args.Length>=3?Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2]))!,"IsoSymbolStyles.symbols.json"):"";`
- [Строка 11](src/IsoGenerator.Cli/Program.cs#L11) `double symbolScale=10;`
- [Строка 12](src/IsoGenerator.Cli/Program.cs#L12) `if(Environment.GetEnvironmentVariable("ISO_SYMBOL_SCALE") is {} rawScale&&(!double.TryParse(rawScale,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out symbolScale)||!double.IsFin…`
- [Строка 14](src/IsoGenerator.Cli/Program.cs#L14) `var map=args.Length>=3?new IsoSymbolMap(File.ReadAllText(args[2]),catalog):null;`
- [Строка 15](src/IsoGenerator.Cli/Program.cs#L15) `int quarterTurns=int.TryParse(Environment.GetEnvironmentVariable("ISO_QUARTER_TURNS"),out var turns)?turns:0;`
- [Строка 16](src/IsoGenerator.Cli/Program.cs#L16) `if(args.Length==4)`
- [Строка 18](src/IsoGenerator.Cli/Program.cs#L18) `if(args[3]!="--sheets")throw new ArgumentException("Unknown option "+args[3]);`
- [Строка 19](src/IsoGenerator.Cli/Program.cs#L19) `var sheets=SheetSetPlanner.Build(snapshot,map,quarterTurns,Environment.GetEnvironmentVariable("ISO_EXCLUDE_FLANGE_HARDWARE")=="1",progress:progress,ignoreBreakPoints:Environment.GetEnvironmentVariable("ISO_IGNORE_BREAK_POI…`
- [Строка 22](src/IsoGenerator.Cli/Program.cs#L22) `var target=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!,Path.GetFileNameWithoutExtension(args[1])+$"-sheet-{sheet.Number:00}.json");`
- [Строка 28](src/IsoGenerator.Cli/Program.cs#L28) `var plans=SheetSetPlanner.Build(snapshot,map,quarterTurns,Environment.GetEnvironmentVariable("ISO_EXCLUDE_FLANGE_HARDWARE")=="1",progress:progress,ignoreBreakPoints:Environment.GetEnvironmentVariable("ISO_IGNORE_BREAK_POIN…`
- [Строка 29](src/IsoGenerator.Cli/Program.cs#L29) `if(plans.Count!=1)throw new SheetCapacityException("Линии требуется несколько листов. Используйте --sheets.");`
- [Строка 30](src/IsoGenerator.Cli/Program.cs#L30) `var scene=plans[0].Scene;`
- [Строка 31](src/IsoGenerator.Cli/Program.cs#L31) `File.WriteAllText(args[1],JsonSerializer.Serialize(scene,new JsonSerializerOptions{WriteIndented=true}));`
- [Строка 32](src/IsoGenerator.Cli/Program.cs#L32) `Console.WriteLine($"{scene.SourcePartCount} source objects; {scene.Strokes.Count} strokes; {scene.Dimensions.Count(d=>d.Role=="chain")} chain dimensions, {scene.Dimensions.Count(d=>d.Role=="overall")} overall dimensions, {…`
- [Строка 36](src/IsoGenerator.Cli/Program.cs#L36) `} catch(Exception ex) {Console.Error.WriteLine("Изометрия: "+(Environment.GetEnvironmentVariable("ISO_LAYOUT_TRACE")=="1"?ex.ToString():ex.Message));Environment.ExitCode=1;}`

### AirVentClearance.cs

- [Строка 24](src/IsoGenerator.Core/AirVentClearance.cs#L24) `while(queue.Count>0&&branch.Count<=12)`
- [Строка 33](src/IsoGenerator.Core/AirVentClearance.cs#L33) `if(reconnects||queue.Count>0)continue;`
- [Строка 37](src/IsoGenerator.Core/AirVentClearance.cs#L37) `if(necks.Length!=1||valves.Length!=1)continue;`
- [Строка 38](src/IsoGenerator.Core/AirVentClearance.cs#L38) `var neck=necks[0];var ends=neck.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).OrderBy(p=>p.Position.Z).ToArray();if(ends.Length!=2)continue;`
- [Строка 39](src/IsoGenerator.Core/AirVentClearance.cs#L39) `var low=ends[0].Position;var high=ends[1].Position;double rise=high.Z-low.Z;`
- [Строка 40](src/IsoGenerator.Core/AirVentClearance.cs#L40) `if(rise<=1||Math.Abs(high.X-low.X)>.01*rise||Math.Abs(high.Y-low.Y)>.01*rise)continue;`
- [Строка 41](src/IsoGenerator.Core/AirVentClearance.cs#L41) `var hostPort=host.Ports.FirstOrDefault(p=>p.Name==root.Port);if(hostPort is null||valves[0].Ports.Average(p=>p.Position.Z)<=hostPort.Position.Z)continue;`
- [Строка 43](src/IsoGenerator.Core/AirVentClearance.cs#L43) `if(!explicitVent&&(PartDiameter.Read(host) is not {} diameter||physical.Any(p=>PartDiameter.Read(p) is {} size&&size*2>diameter)||PartDiameter.Read(neck) is not {} bore||bore*2>diameter))continue;`
- [Строка 48](src/IsoGenerator.Core/AirVentClearance.cs#L48) `if(footprint.Length==0)continue;`
- [Строка 49](src/IsoGenerator.Core/AirVentClearance.cs#L49) `double left=footprint.Min(p=>p.X)-2,right=footprint.Max(p=>p.X)+2,bottom=footprint.Min(p=>p.Y),top=footprint.Max(p=>p.Y);`
- [Строка 57](src/IsoGenerator.Core/AirVentClearance.cs#L57) `if(d.ModelStart is not {} ms||d.ModelEnd is not {} me||Math.Abs(ms.Z-me.Z)>1||Math.Abs(d.End.X-d.Start.X)<.1)continue;`
- [Строка 59](src/IsoGenerator.Core/AirVentClearance.cs#L59) `var ex=d.ExtensionDirection!.Value;var line=d.DimensionLinePoint!.Value;double dot=ex.X*nx+ex.Y*ny;if(Math.Abs(dot)<1e-8)continue;`
- [Строка 64](src/IsoGenerator.Core/AirVentClearance.cs#L64) `if(y<bottom-5||y>top+2)continue;`
- [Строка 65](src/IsoGenerator.Core/AirVentClearance.cs#L65) `ceiling=Math.Max(ceiling,y+5); // Include text ink above the dimension baseline.`
- [Строка 68](src/IsoGenerator.Core/AirVentClearance.cs#L68) `double extra=ceiling+5-bottom;`
- [Строка 69](src/IsoGenerator.Core/AirVentClearance.cs#L69) `if(extra>.1)`
- [Строка 72](src/IsoGenerator.Core/AirVentClearance.cs#L72) `double length=Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Y-a.Y,2));`
- [Строка 75](src/IsoGenerator.Core/AirVentClearance.cs#L75) `if(b.Y-a.Y>.1&&extra<=b.Y-a.Y)`
- [Строка 76](src/IsoGenerator.Core/AirVentClearance.cs#L76) `throw new AirVentSpacingException(neck.Handle,extra*length/(b.Y-a.Y)+1);`

### AnnotationPlacement.cs

- [Строка 9](src/IsoGenerator.Core/AnnotationPlacement.cs#L9) `static Box TextBox(SheetPoint p,string text,bool position=false,double rotation=0)`
- [Строка 11](src/IsoGenerator.Core/AnnotationPlacement.cs#L11) `int length=TextLength(text),lines=text.Count(c=>c=='\n')+1;`
- [Строка 12](src/IsoGenerator.Core/AnnotationPlacement.cs#L12) `var local=position?new Box(-1,-.8,length*2.1+2,5):new Box(-.8,-.6-(lines-1)*4.8,length*2.1+1.5,4.6);`
- [Строка 13](src/IsoGenerator.Core/AnnotationPlacement.cs#L13) `double ratio=IsoAdvancedSettings.Current.LeaderTextHeight/3.5;`
- [Строка 22](src/IsoGenerator.Core/AnnotationPlacement.cs#L22) `double low=0,high=1,dx=b.X-a.X,dy=b.Y-a.Y;`
- [Строка 23](src/IsoGenerator.Core/AnnotationPlacement.cs#L23) `bool Clip(double p,double q){if(Math.Abs(p)<1e-10)return q>=0;double r=q/p;if(p<0)low=Math.Max(low,r);else high=Math.Min(high,r);return low<=high;}`
- [Строка 30](src/IsoGenerator.Core/AnnotationPlacement.cs#L30) `double ratio=IsoAdvancedSettings.Current.DimensionTextHeight/3.5;`
- [Строка 31](src/IsoGenerator.Core/AnnotationPlacement.cs#L31) `double halfWidth=d.Label.Length*1.05*ratio+2.5,halfHeight=3.3*ratio;`
- [Строка 60](src/IsoGenerator.Core/AnnotationPlacement.cs#L60) `double ratio=IsoAdvancedSettings.Current.DimensionTextHeight/3.5;`
- [Строка 61](src/IsoGenerator.Core/AnnotationPlacement.cs#L61) `double w=d.Label.Length*1.05*ratio+.2,h=1.77*ratio;`
- [Строка 67](src/IsoGenerator.Core/AnnotationPlacement.cs#L67) `var insulationLines=strokes.Where(s=>s.Role=="insulation").SelectMany(s=>s.Points.Zip(s.Points.Skip(1),(a,b)=>(A:a,B:b))).ToArray();`
- [Строка 68](src/IsoGenerator.Core/AnnotationPlacement.cs#L68) `var lines=strokes.Where(s=>s.Role!="insulation").SelectMany(s=>s.Points.Zip(s.Points.Skip(1),(a,b)=>(A:a,B:b))).ToList();`
- [Строка 73](src/IsoGenerator.Core/AnnotationPlacement.cs#L73) `foreach(var glyph in symbols.Where(s=>s.LocalContours is {Count:>0}))`
- [Строка 77](src/IsoGenerator.Core/AnnotationPlacement.cs#L77) `foreach(var (a,b) in contour.Zip(contour.Skip(1)))lines.Add((At(a),At(b)));`
- [Строка 79](src/IsoGenerator.Core/AnnotationPlacement.cs#L79) `foreach(var elbow in symbols.Where(s=>s.LocalContours is null&&s.Block=="Колено"&&s.Variant.Length==0))`
- [Строка 82](src/IsoGenerator.Core/AnnotationPlacement.cs#L82) `lines.Add((At(-.5,0),At(-.125,0)));`
- [Строка 83](src/IsoGenerator.Core/AnnotationPlacement.cs#L83) `var previous=At(-.125,0);`
- [Строка 84](src/IsoGenerator.Core/AnnotationPlacement.cs#L84) `for(int i=1;i<=24;i++){double angle=-Math.PI/2+i*Math.PI/48;var next=At(-.125+.125*Math.Cos(angle),.125+.125*Math.Sin(angle));lines.Add((previous,next));previous=next;}`
- [Строка 85](src/IsoGenerator.Core/AnnotationPlacement.cs#L85) `lines.Add((At(0,.125),At(0,.5)));`
- [Строка 87](src/IsoGenerator.Core/AnnotationPlacement.cs#L87) `var bodies=symbols.Where(s=>s.LocalContours is not {Count:>0}&&s.Block!="Колено").Select(s=>`
- [Строка 89](src/IsoGenerator.Core/AnnotationPlacement.cs#L89) `double x=s.Block=="Колено"?Math.Abs(s.XAxis.X)*.5+Math.Abs(s.YAxis.X)*.5:Math.Abs(s.XAxis.X)*(s.Block=="ФланецWN"?.15625:.5)+Math.Abs(s.YAxis.X)*.25;`
- [Строка 90](src/IsoGenerator.Core/AnnotationPlacement.cs#L90) `double y=s.Block=="Колено"?Math.Abs(s.XAxis.Y)*.5+Math.Abs(s.YAxis.Y)*.5:Math.Abs(s.XAxis.Y)*(s.Block=="ФланецWN"?.15625:.5)+Math.Abs(s.YAxis.Y)*.25;`
- [Строка 93](src/IsoGenerator.Core/AnnotationPlacement.cs#L93) `var points=(from u in new[]{-.027,0.0} from v in new[]{-.25,.25} select new SheetPoint(s.Origin.X+s.XAxis.X*u+s.YAxis.X*v,s.Origin.Y+s.XAxis.Y*u+s.YAxis.Y*v)).ToArray();`
- [Строка 94](src/IsoGenerator.Core/AnnotationPlacement.cs#L94) `return new Box(points.Min(p=>p.X)-1,points.Min(p=>p.Y)-1,points.Max(p=>p.X)+1,points.Max(p=>p.Y)+1);`
- [Строка 98](src/IsoGenerator.Core/AnnotationPlacement.cs#L98) `var points=(from u in new[]{-.25,.25} from v in new[]{0.0,.625} select new SheetPoint(s.Origin.X+s.XAxis.X*u+s.YAxis.X*v,s.Origin.Y+s.XAxis.Y*u+s.YAxis.Y*v)).ToArray();`
- [Строка 99](src/IsoGenerator.Core/AnnotationPlacement.cs#L99) `return new Box(points.Min(p=>p.X)-1,points.Min(p=>p.Y)-1,points.Max(p=>p.X)+1,points.Max(p=>p.Y)+1);`
- [Строка 101](src/IsoGenerator.Core/AnnotationPlacement.cs#L101) `return new Box(s.Origin.X-x-1,s.Origin.Y-y-1,s.Origin.X+x+1,s.Origin.Y+y+1);`
- [Строка 106](src/IsoGenerator.Core/AnnotationPlacement.cs#L106) `var field=layoutPoints.Length==0?(Box?)null:new Box(layoutPoints.Min(p=>p.X),layoutPoints.Min(p=>p.Y),`
- [Строка 113](src/IsoGenerator.Core/AnnotationPlacement.cs#L113) `return Math.Max(0,bounds.Right-bounds.Left-SheetComposition.Paper.Width)+`
- [Строка 114](src/IsoGenerator.Core/AnnotationPlacement.cs#L114) `Math.Max(0,bounds.Top-bounds.Bottom-SheetComposition.Paper.Height);`
- [Строка 123](src/IsoGenerator.Core/AnnotationPlacement.cs#L123) `foreach(var label in labels.OrderBy(l=>l.Role is "slope-angle" or "slope-value"?0:l.Role is "valve-tag" or "instrument-tag" or "external-line" or "support-type"?1:l.Role=="elevation"?2:l.Role is "position" or "position-gro…`
- [Строка 128](src/IsoGenerator.Core/AnnotationPlacement.cs#L128) `bool placed=false;double width=TextLength(label.Text)*2.1*(IsoAdvancedSettings.Current.LeaderTextHeight/3.5)+3;int rejectedText=0;`
- [Строка 132](src/IsoGenerator.Core/AnnotationPlacement.cs#L132) `for(int pass=0;pass<2&&!placed&&bestLeader is null;pass++)`
- [Строка 133](src/IsoGenerator.Core/AnnotationPlacement.cs#L133) `foreach(var anchorCandidate in new[]{anchor}.Concat(label.NearbyAnchors??Array.Empty<SheetPoint>()).Concat(label.Role=="elevation"?symbols.Where(g=>g.Variant=="missing").SelectMany(SymbolFootprint.Corners).Where(p=>Math.Ab…`
- [Строка 137](src/IsoGenerator.Core/AnnotationPlacement.cs#L137) `foreach(double radius in (compactLeader?new[]{10.0,15,20,24,30,36,42,48,60,72,84,96}:remoteLeader?new[]{24.0,30,36,42,48,60,72,84,96}:new[]{4.0,6,8,10,12,18,24,30,36,42,48}).Append(IsoAdvancedSettings.Current.LeaderTarget)…`
- [Строка 140](src/IsoGenerator.Core/AnnotationPlacement.cs#L140) `foreach(var dir in new[]{(1.0,1.0),(-1.0,1.0),(1.0,-1.0),(-1.0,-1.0),(1.0,.5),(-1.0,.5),(1.0,-.5),(-1.0,-.5),(.5,1.0),(-.5,1.0),(.5,-1.0),(-.5,-1.0),(1.0,0.0),(-1.0,0.0),(0.0,1.0),(0.0,-1.0)})`
- [Строка 142](src/IsoGenerator.Core/AnnotationPlacement.cs#L142) `foreach(double shelfSign in dir.Item1 == 0 || compactLeader ? new[]{-1.0, 1.0} : new[]{dir.Item1 < 0 ? -1.0 : 1.0})`
- [Строка 146](src/IsoGenerator.Core/AnnotationPlacement.cs#L146) `var left=new SheetPoint(knee.X-(shelfSign<0?width:0),knee.Y);`
- [Строка 148](src/IsoGenerator.Core/AnnotationPlacement.cs#L148) `var text=new SheetPoint(left.X+1,left.Y+1.5);var box=TextBox(text,label.Text);`
- [Строка 150](src/IsoGenerator.Core/AnnotationPlacement.cs#L150) `if(pass==0&&!remoteLeader&&dimensionSegments.Any(segment=>`
- [Строка 153](src/IsoGenerator.Core/AnnotationPlacement.cs#L153) `if(box.Left<20||box.Right>570||box.Bottom<20||box.Top>390)continue;`
- [Строка 158](src/IsoGenerator.Core/AnnotationPlacement.cs#L158) `var neighbourhood=new Box(box.Left-12,box.Bottom-12,box.Right+12,box.Top+12);`
- [Строка 159](src/IsoGenerator.Core/AnnotationPlacement.cs#L159) `double crowding=lines.Count(l=>Hits(l.A,l.B,neighbourhood))*10+occupied.Count(b=>b.Overlaps(neighbourhood))*15+bodies.Count(b=>b.Overlaps(neighbourhood))*15;`
- [Строка 166](src/IsoGenerator.Core/AnnotationPlacement.cs#L166) `double score=(compactLeader?Math.Abs(radius-IsoAdvancedSettings.Current.LeaderTarget)*.15+Math.Max(0,radius-IsoAdvancedSettings.Current.LeaderSoftLimit)*.5:radius*.15)+(knee.Y>=anchorCandidate.Y?.05:0);`
- [Строка 168](src/IsoGenerator.Core/AnnotationPlacement.cs#L168) `bool better=bestLeader is null||overflow<bestLeader.Value.Overflow-.001;`
- [Строка 169](src/IsoGenerator.Core/AnnotationPlacement.cs#L169) `if(!better&&bestLeader is {} prior&&Math.Abs(overflow-prior.Overflow)<.001)`
- [Строка 173](src/IsoGenerator.Core/AnnotationPlacement.cs#L173) `double cost=score+crossings*crossingWeight+crowding*.005;`
- [Строка 174](src/IsoGenerator.Core/AnnotationPlacement.cs#L174) `double priorCost=prior.Score+prior.Crossings*crossingWeight+prior.Crowding*.005;`
- [Строка 175](src/IsoGenerator.Core/AnnotationPlacement.cs#L175) `better=(leaderCrossings,cost).CompareTo((prior.LeaderCrossings,priorCost))<0;`
- [Строка 178](src/IsoGenerator.Core/AnnotationPlacement.cs#L178) `.CompareTo((prior.LeaderCrossings,prior.Crowding,prior.Crossings,prior.Score))<0;`
- [Строка 190](src/IsoGenerator.Core/AnnotationPlacement.cs#L190) `if(placed||remoteLeader&&bestLeader is {Crowding:0,Crossings:0,LeaderCrossings:0,Overflow:0}&&(!compactLeader||bestLeader.Value.Score<=.05))break;`
- [Строка 198](src/IsoGenerator.Core/AnnotationPlacement.cs#L198) `lines.Add((best.Anchor,best.Knee));lines.Add((best.Left,best.Right));leaderPaths.Add((best.Anchor,best.Knee));leaderPaths.Add((best.Left,best.Right));Occupy(new(best.Box.Left-3,best.Box.Bottom-3,best.Box.Right+3,best.Box.T…`
- [Строка 201](src/IsoGenerator.Core/AnnotationPlacement.cs#L201) `if(!placed&&Environment.GetEnvironmentVariable("ISO_LAYOUT_TRACE")=="1")Console.Error.WriteLine($"leader {label.Text} at {anchor}: text={rejectedText}");`
- [Строка 213](src/IsoGenerator.Core/AnnotationPlacement.cs#L213) `var first=indicator.Points[0];var second=indicator.Points[1];`
- [Строка 214](src/IsoGenerator.Core/AnnotationPlacement.cs#L214) `var center=new SheetPoint((first.X+second.X)/2,(first.Y+second.Y)/2);`
- [Строка 216](src/IsoGenerator.Core/AnnotationPlacement.cs#L216) `double span=run is null?0:Math.Abs((run.Points[^1].X-run.Points[0].X)*axis.X+(run.Points[^1].Y-run.Points[0].Y)*axis.Y);`
- [Строка 217](src/IsoGenerator.Core/AnnotationPlacement.cs#L217) `var alternatives=from offset in new[]{0d,2d,4d,6d,9d,12d,-12d,-14d,-16d,-18d}`
- [Строка 219](src/IsoGenerator.Core/AnnotationPlacement.cs#L219) `orderby (offset>=0?offset:-offset-10)+Math.Abs(slide)*span*.35`
- [Строка 226](src/IsoGenerator.Core/AnnotationPlacement.cs#L226) `double moveX=point.X-normal.X*2.8-center.X,moveY=point.Y-normal.Y*2.8-center.Y;`
- [Строка 231](src/IsoGenerator.Core/AnnotationPlacement.cs#L231) `if(bounds.Left<20||bounds.Right>570||bounds.Bottom<20||bounds.Top>390||`
- [Строка 243](src/IsoGenerator.Core/AnnotationPlacement.cs#L243) `var origin=new SheetPoint(label.Position.X-2,label.Position.Y-3);`
- [Строка 246](src/IsoGenerator.Core/AnnotationPlacement.cs#L246) `var positionDirs = label.Role is "position" or "position-group"?new[]{(1.0,1.0),(-1.0,1.0),(1.0,-1.0),(-1.0,-1.0),(1.0,0.0),(-1.0,0.0),(0.0,1.0),(0.0,-1.0),(1.0,.5),(-1.0,.5),(1.0,-.5),(-1.0,-.5),(.5,1.0),(-.5,1.0),(.5,-1.…`
- [Строка 248](src/IsoGenerator.Core/AnnotationPlacement.cs#L248) `foreach(double radius in label.Role=="slope-angle"?new[]{4.0,6,8,10,14,18,24,30,36,42,48}:label.Role=="slope-value"?new[]{4.0,6,8,10,14,18,24,30}:label.Role=="position-group"?new[]{2.5,3.5,4.5,6,8,10,12,14,18,22,26,30,35,4…`
- [Строка 252](src/IsoGenerator.Core/AnnotationPlacement.cs#L252) `var point=new SheetPoint(near.X+dir.Item1/dlen*radius-(dir.Item1<=0?label.Text.Length*2.1:0),near.Y+dir.Item2/dlen*radius);`
- [Строка 253](src/IsoGenerator.Core/AnnotationPlacement.cs#L253) `double distance=Math.Pow(point.X+label.Text.Length*1.05-near.X,2)+Math.Pow(point.Y+1.75-near.Y,2);`
- [Строка 254](src/IsoGenerator.Core/AnnotationPlacement.cs#L254) `candidates.Add((point,near,distance+.001*(Math.Pow(near.X-origin.X,2)+Math.Pow(near.Y-origin.Y,2))));`
- [Строка 261](src/IsoGenerator.Core/AnnotationPlacement.cs#L261) `if(box.Left<20||box.Right>570||box.Bottom<20||box.Top>390)continue;`
- [Строка 262](src/IsoGenerator.Core/AnnotationPlacement.cs#L262) `if(occupied.Any(b=>b.Overlaps(box))||bodies.Any(b=>b.Overlaps(box))||lines.Any(l=>Hits(l.A,l.B,box))||insulationLines.Any(l=>Hits(l.A,l.B,new Box(box.Left-1,box.Bottom-1,box.Right+1,box.Top+1))))continue;`
- [Строка 269](src/IsoGenerator.Core/AnnotationPlacement.cs#L269) `foreach(double radius in new[]{18d,22d,26d,30d})`
- [Строка 276](src/IsoGenerator.Core/AnnotationPlacement.cs#L276) `var candidate=new SheetPoint(tip.X+(dir.Item1<0?-label.Text.Length*2.1-2:2),tip.Y-1.5);`
- [Строка 278](src/IsoGenerator.Core/AnnotationPlacement.cs#L278) `if(box.Left<20||box.Right>570||box.Bottom<20||box.Top>390||`

### AutomaticView.cs

- [Строка 9](src/IsoGenerator.Core/AutomaticView.cs#L9) `Rank(source,cancellationToken)[0];`
- [Строка 14](src/IsoGenerator.Core/AutomaticView.cs#L14) `var edges=graph.Edges.Where(e=>graph.Nodes[e.A].Point.DistanceTo(graph.Nodes[e.B].Point)>1e-6).ToArray();`
- [Строка 16](src/IsoGenerator.Core/AutomaticView.cs#L16) `for(int turn=0;turn<4;turn++)`
- [Строка 20](src/IsoGenerator.Core/AutomaticView.cs#L20) `double score=0;int collapsed=0;`
- [Строка 21](src/IsoGenerator.Core/AutomaticView.cs#L21) `for(int i=0;i<edges.Length;i++)`
- [Строка 26](src/IsoGenerator.Core/AutomaticView.cs#L26) `if(length<.001)collapsed++;`
- [Строка 27](src/IsoGenerator.Core/AutomaticView.cs#L27) `score+=Math.Max(0,1-length/actual); // Prefer visible lengths for sloping runs.`
- [Строка 28](src/IsoGenerator.Core/AutomaticView.cs#L28) `for(int j=0;j<i;j++)`
- [Строка 32](src/IsoGenerator.Core/AutomaticView.cs#L32) `if(new[]{edge.A,edge.B}.Any(n=>new[]{other.A,other.B}.Any(m=>graph.Nodes[n].Point.DistanceTo(graph.Nodes[m].Point)<1)))continue;`
- [Строка 35](src/IsoGenerator.Core/AutomaticView.cs#L35) `if(Math.Abs(cross)<1e-6*Math.Max(1,length)&&Math.Abs(cross2)<1e-6*Math.Max(1,length)&&length>1e-6)`
- [Строка 39](src/IsoGenerator.Core/AutomaticView.cs#L39) `if(Math.Min(length,Math.Max(t1,t2))-Math.Max(0,Math.Min(t1,t2))>1)score+=10000;`
- [Строка 41](src/IsoGenerator.Core/AutomaticView.cs#L41) `else if(cross*cross2<0&&Cross(c,d,a)*Cross(c,d,b)<0)score+=100;`
- [Строка 46](src/IsoGenerator.Core/AutomaticView.cs#L46) `return scores.OrderBy(view=>view.Collapsed).ThenBy(view=>Math.Round(view.Score,8))`

### BomCategories.cs

- [Строка 36](src/IsoGenerator.Core/BomCategories.cs#L36) `return identity.Contains("ELBOW")?0:identity.Contains("TEE")?1:identity.Contains("REDUCER")?2:3;`

### BomPagination.cs

- [Строка 12](src/IsoGenerator.Core/BomPagination.cs#L12) `if(page.Count==0)throw new InvalidDataException("A BOM row does not fit an empty sheet.");`
- [Строка 17](src/IsoGenerator.Core/BomPagination.cs#L17) `if(page.Count>0||pages.Count==0)pages.Add(page.ToArray());`
- [Строка 24](src/IsoGenerator.Core/BomPagination.cs#L24) `var numbers=new Dictionary<int,int>();int next=1;`
- [Строка 25](src/IsoGenerator.Core/BomPagination.cs#L25) `for(int i=0;i<geometry.Count;i++){numbers.Add(geometry[i].Number,next);next+=pagesBySheet[i].Count;}`
- [Строка 26](src/IsoGenerator.Core/BomPagination.cs#L26) `for(int sheetIndex=0;sheetIndex<geometry.Count;sheetIndex++)`
- [Строка 30](src/IsoGenerator.Core/BomPagination.cs#L30) `result.Add(sheet with{Scene=sheet.Scene with{BillOfMaterials=pages[0],FittingSourceIds=fittings}});`
- [Строка 31](src/IsoGenerator.Core/BomPagination.cs#L31) `foreach(var rows in pages.Skip(1))`
- [Строка 37](src/IsoGenerator.Core/BomPagination.cs#L37) `Annotations=new[]{new Annotation("","title",new(75,385),$"{sheet.Scene.Line} — продолжение спецификации листа {numbers[sheet.Number]}",4)},`
- [Строка 38](src/IsoGenerator.Core/BomPagination.cs#L38) `BillOfMaterials=rows,SourcePartCount=0,SourceGuids=new Dictionary<string,string>(),BomOnly=true,FittingSourceIds=Array.Empty<string>()`
- [Строка 40](src/IsoGenerator.Core/BomPagination.cs#L40) `result.Add(new(0,0,empty,scene));`
- [Строка 45](src/IsoGenerator.Core/BomPagination.cs#L45) `return result.Select((s,i)=>s with{Number=i+1,Total=total,Scene=s.Scene with`
- [Строка 47](src/IsoGenerator.Core/BomPagination.cs#L47) `Annotations=s.Scene.Annotations.Select(a=>a.Role=="title"?a with{Text=s.Scene.BomOnly?a.Text+$" — лист {i+1} из {total}":s.Scene.Line+$" — лист {i+1} из {total}"}:a.Role=="continuation"?a with{Text=System.Text.RegularExpre…`

### BreakPointPreparation.cs

- [Строка 7](src/IsoGenerator.Core/BreakPointPreparation.cs#L7) `public const int BreakPointInformationType=4;`
- [Строка 16](src/IsoGenerator.Core/BreakPointPreparation.cs#L16) `if(ends.Length!=2)return false;`
- [Строка 17](src/IsoGenerator.Core/BreakPointPreparation.cs#L17) `var a=ends[0].Position;var b=ends[1].Position;`
- [Строка 19](src/IsoGenerator.Core/BreakPointPreparation.cs#L19) `if(length2<1e-8)return false;`
- [Строка 20](src/IsoGenerator.Core/BreakPointPreparation.cs#L20) `double t=Math.Clamp(((point.X-a.X)*dx+(point.Y-a.Y)*dy+(point.Z-a.Z)*dz)/length2,0,1);`
- [Строка 21](src/IsoGenerator.Core/BreakPointPreparation.cs#L21) `return point.DistanceTo(new(a.X+t*dx,a.Y+t*dy,a.Z+t*dz))<=1;`
- [Строка 27](src/IsoGenerator.Core/BreakPointPreparation.cs#L27) `if(markers.Count==0)return ignoreBreakPoints?original with{RequiredCuts=Array.Empty<Connection>()}:original;`
- [Строка 32](src/IsoGenerator.Core/BreakPointPreparation.cs#L32) `if(ignoreBreakPoints)return original with{Parts=parts,Connections=connections,IsoMarkers=markers.Where(m=>m.InformationType is 1 or 5).ToArray(),RequiredCuts=Array.Empty<Connection>()};`
- [Строка 38](src/IsoGenerator.Core/BreakPointPreparation.cs#L38) `if(candidates.Count!=1)`
- [Строка 40](src/IsoGenerator.Core/BreakPointPreparation.cs#L40) `warnings.Add($"{marker.Handle}: восстановлена привязка точки разрыва к трубе {candidates[0].Handle} по оси.");`
- [Строка 41](src/IsoGenerator.Core/BreakPointPreparation.cs#L41) `return marker with{HostHandle=candidates[0].Handle};`
- [Строка 47](src/IsoGenerator.Core/BreakPointPreparation.cs#L47) `var ends=host.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();if(ends.Length==0)throw new InvalidDataException($"{host.Handle}: не найдены порты компонента с точкой разрыва.");`
- [Строка 52](src/IsoGenerator.Core/BreakPointPreparation.cs#L52) `if(nearest.Position.DistanceTo(marker.Position)<=1)`
- [Строка 58](src/IsoGenerator.Core/BreakPointPreparation.cs#L58) `if(host.Properties.GetValueOrDefault("PnPClassName")!="Pipe"||ends.Length!=2)throw new InvalidDataException($"{marker.Handle}: точка разрыва находится внутри фитинга; перенесите её на соединение.");`
- [Строка 59](src/IsoGenerator.Core/BreakPointPreparation.cs#L59) `var a=ends[0].Position;var b=ends[1].Position;double dx=b.X-a.X,dy=b.Y-a.Y,dz=b.Z-a.Z,l2=dx*dx+dy*dy+dz*dz;`
- [Строка 61](src/IsoGenerator.Core/BreakPointPreparation.cs#L61) `if(t<=0||t>=1||at.DistanceTo(marker.Position)>1)throw new InvalidDataException($"{marker.Handle}: точка разрыва не лежит на оси своей трубы.");`
- [Строка 64](src/IsoGenerator.Core/BreakPointPreparation.cs#L64) `if(internalMarkers.Count==0)continue;`
- [Строка 65](src/IsoGenerator.Core/BreakPointPreparation.cs#L65) `var sorted=internalMarkers.OrderBy(m=>m.T).DistinctBy(m=>Math.Round(m.T*ends[0].Position.DistanceTo(ends[1].Position),3)).ToArray();`
- [Строка 66](src/IsoGenerator.Core/BreakPointPreparation.cs#L66) `var points=new[]{ends[0].Position}.Concat(sorted.Select(m=>m.At)).Append(ends[1].Position).ToArray();`
- [Строка 68](src/IsoGenerator.Core/BreakPointPreparation.cs#L68) `for(int i=0;i<points.Length-1;i++)`
- [Строка 70](src/IsoGenerator.Core/BreakPointPreparation.cs#L70) `string handle=host.Handle+"#ISO"+(i+1);var values=new Dictionary<string,string>(host.Properties,StringComparer.OrdinalIgnoreCase){["IsoOriginalHandle"]=host.Properties.GetValueOrDefault("IsoOriginalHandle",host.Handle),["L…`
- [Строка 71](src/IsoGenerator.Core/BreakPointPreparation.cs#L71) `var ports=new List<Port>{new("S1",points[i],ends[0].Direction,i==0?ends[0].EndCondition:"ISO-CONTINUATION"),new("S2",points[i+1],ends[1].Direction,i==points.Length-2?ends[1].EndCondition:"ISO-CONTINUATION")};`
- [Строка 74](src/IsoGenerator.Core/BreakPointPreparation.cs#L74) `double t=(port.Position.X-ends[0].Position.X)*(ends[1].Position.X-ends[0].Position.X)+(port.Position.Y-ends[0].Position.Y)*(ends[1].Position.Y-ends[0].Position.Y)+(port.Position.Z-ends[0].Position.Z)*(ends[1].Position.Z-en…`
- [Строка 75](src/IsoGenerator.Core/BreakPointPreparation.cs#L75) `t/=Math.Pow(ends[0].Position.DistanceTo(ends[1].Position),2);`
- [Строка 76](src/IsoGenerator.Core/BreakPointPreparation.cs#L76) `int index=Array.FindIndex(sorted,m=>t<=m.T);if(index<0)index=points.Length-2;`
- [Строка 81](src/IsoGenerator.Core/BreakPointPreparation.cs#L81) `replacement[host.Handle+":"+ends[0].Name]=fragments[0].Handle+":S1";replacement[host.Handle+":"+ends[1].Name]=fragments[^1].Handle+":S2";`
- [Строка 83](src/IsoGenerator.Core/BreakPointPreparation.cs#L83) `for(int i=0;i<fragments.Count-1;i++){var link=new Connection(fragments[i].Handle,"S2",fragments[i+1].Handle,"S1");connections.Add(link);cuts.Add(link);}`
- [Строка 86](src/IsoGenerator.Core/BreakPointPreparation.cs#L86) `{if(!replacement.TryGetValue(handle+":"+port,out var value))return(handle,port);int colon=value.LastIndexOf(':');return(value[..colon],value[(colon+1)..]);}`
- [Строка 92](src/IsoGenerator.Core/BreakPointPreparation.cs#L92) `var host=fragments.Where(p=>{var e=p.Ports.Where(q=>q.Name is "S1" or "S2").ToArray();return e.Length==2&&Math.Abs(e[0].Position.DistanceTo(marker.Position)+e[1].Position.DistanceTo(marker.Position)-e[0].Position.DistanceT…`
- [Строка 96](src/IsoGenerator.Core/BreakPointPreparation.cs#L96) `var prepared=original with{Parts=result,Warnings=warnings,Connections=connections.Select(Map).ToArray(),RequiredCuts=cuts.Select(Map).Distinct().ToArray(),IsoMarkers=markers.Where(m=>m.InformationType is 1 or 5).Select(Map…`
- [Строка 100](src/IsoGenerator.Core/BreakPointPreparation.cs#L100) `double Length(Part p){var ends=p.Ports.Where(q=>q.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();return ends[0].Position.DistanceTo(ends[1].Position);}`
- [Строка 101](src/IsoGenerator.Core/BreakPointPreparation.cs#L101) `if(Math.Abs(fragments.Sum(Length)-Length(host))>.001)throw new InvalidDataException("Нарушена длина трубы при разделении по точке разрыва: "+host.Handle);`

### ContinuationContextPlanner.cs

- [Строка 19](src/IsoGenerator.Core/ContinuationContextPlanner.cs#L19) `double Distance(SheetPoint p)=>Math.Sqrt(Math.Pow(p.X-there.X,2)+Math.Pow(p.Y-there.Y,2));`
- [Строка 22](src/IsoGenerator.Core/ContinuationContextPlanner.cs#L22) `while(queue.Count>0&&selected.Count<3)`
- [Строка 28](src/IsoGenerator.Core/ContinuationContextPlanner.cs#L28) `if(kind=="Pipe"&&selected.Count>0)continue;`
- [Строка 30](src/IsoGenerator.Core/ContinuationContextPlanner.cs#L30) `if(selected.Count==0)assembly=kind is "Valve" or "Flange";`
- [Строка 31](src/IsoGenerator.Core/ContinuationContextPlanner.cs#L31) `if(selected.Count>0&&!assembly)continue;`
- [Строка 46](src/IsoGenerator.Core/ContinuationContextPlanner.cs#L46) `var start=points[0];var end=points[^1];double dx=end.X-start.X,dy=end.Y-start.Y,length2=dx*dx+dy*dy;`
- [Строка 47](src/IsoGenerator.Core/ContinuationContextPlanner.cs#L47) `if(length2<1e-10)continue;`
- [Строка 48](src/IsoGenerator.Core/ContinuationContextPlanner.cs#L48) `double t=Math.Clamp(((there.X-start.X)*dx+(there.Y-start.Y)*dy)/length2,0,1);`
- [Строка 54](src/IsoGenerator.Core/ContinuationContextPlanner.cs#L54) `double ex=endpoint.X-at.X,ey=endpoint.Y-at.Y,length=Math.Sqrt(ex*ex+ey*ey);if(length<.001)continue;`
- [Строка 55](src/IsoGenerator.Core/ContinuationContextPlanner.cs#L55) `double fraction=Math.Min(1,IsoAdvancedSettings.Current.ContinuationPipeLength/length);var segment=new[]{at,new SheetPoint(at.X+ex*fraction,at.Y+ey*fraction)};`
- [Строка 63](src/IsoGenerator.Core/ContinuationContextPlanner.cs#L63) `if(selected.Contains(glyph.SourceId)&&Distance(glyph.Origin)<45)symbols.Add(glyph with{Origin=Move(glyph.Origin),Context=true});`

### CutPreferences.cs

- [Строка 12](src/IsoGenerator.Core/CutPreferences.cs#L12) `if(match.Success&&double.TryParse(match.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out double value)&&value>0)return value;`
- [Строка 16](src/IsoGenerator.Core/CutPreferences.cs#L16) `public static double[] ReducerSizes(Part part)=>Regex.Matches(part.Properties.GetValueOrDefault("Size","").Replace(',','.'),@"\d+(?:\.\d+)?").Select(m=>double.Parse(m.Value,CultureInfo.InvariantCulture)).Where(n=>n>0).ToAr…`
- [Строка 28](src/IsoGenerator.Core/CutPreferences.cs#L28) `double Length(Part p){var ports=p.Ports.Where(q=>q.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();return ports.Length==2?ports[0].Position.DistanceTo(ports[1].Position):0;}`
- [Строка 37](src/IsoGenerator.Core/CutPreferences.cs#L37) `if(ends.Length==3)`
- [Строка 39](src/IsoGenerator.Core/CutPreferences.cs#L39) `var pair=(from i in Enumerable.Range(0,3) from j in Enumerable.Range(i+1,2-i) let a=ends[i].Direction let b=ends[j].Direction where a.X*b.X+a.Y*b.Y+a.Z*b.Z<-.99 select(i,j)).FirstOrDefault((-1,-1));`
- [Строка 40](src/IsoGenerator.Core/CutPreferences.cs#L40) `if(pair.Item1>=0)branchPorts.Add(ends[Enumerable.Range(0,3).Single(i=>i!=pair.Item1&&i!=pair.Item2)].Name);`
- [Строка 47](src/IsoGenerator.Core/CutPreferences.cs#L47) `while(queue.Count>0)`
- [Строка 58](src/IsoGenerator.Core/CutPreferences.cs#L58) `double hostDiameter=PartDiameter.Read(host)??0;`
- [Строка 62](src/IsoGenerator.Core/CutPreferences.cs#L62) `bool pattern=branchPipes.Length==1&&physical.Count(p=>p.Properties.GetValueOrDefault("PnPClassName")=="Valve")==1&&hostDiameter>0&&physical.Any(p=>PartDiameter.Read(p) is {} size&&size*2<=hostDiameter)&&physical.All(p=>Par…`
- [Строка 69](src/IsoGenerator.Core/CutPreferences.cs#L69) `var sizes=PartDiameter.ReducerSizes(reducer);if(sizes.Length<2)continue;double small=sizes.Min(),large=sizes.Max();if(large/small<2)continue;`
- [Строка 73](src/IsoGenerator.Core/CutPreferences.cs#L73) `while(pending.Count>0)`
- [Строка 77](src/IsoGenerator.Core/CutPreferences.cs#L77) `if(part.Properties.GetValueOrDefault("PnPClassName")!="Pipe"||PartDiameter.Read(part) is not {} diameter||Math.Abs(diameter-small)>.001)break;`
- [Строка 79](src/IsoGenerator.Core/CutPreferences.cs#L79) `var run=new HashSet<string>(StringComparer.OrdinalIgnoreCase){reducer.Handle};var q=new Queue<string>();q.Enqueue(part.Handle);double length=0;`
- [Строка 80](src/IsoGenerator.Core/CutPreferences.cs#L80) `while(q.Count>0)`
- [Строка 84](src/IsoGenerator.Core/CutPreferences.cs#L84) `if(kind=="Pipe"){if(PartDiameter.Read(p) is not {} d||Math.Abs(d-small)>.001)continue;length+=Length(p);}`
- [Строка 87](src/IsoGenerator.Core/CutPreferences.cs#L87) `if(length>500)preferred.Add(Key(current.Link));break;`

### CycleLengthSolver.cs

- [Строка 6](src/IsoGenerator.Core/CycleLengthSolver.cs#L6) `public bool HasCycles=>cycles.Count>0;`
- [Строка 7](src/IsoGenerator.Core/CycleLengthSolver.cs#L7) `public int Count=>cycles.Count/2;`
- [Строка 10](src/IsoGenerator.Core/CycleLengthSolver.cs#L10) `int count=graph.Edges.Count;directions=new SheetPoint[count];variables=Enumerable.Range(0,count).Where(i=>graph.Edges[i].Kind=="Pipe").ToArray();`
- [Строка 13](src/IsoGenerator.Core/CycleLengthSolver.cs#L13) `for(int i=0;i<count;i++)`
- [Строка 19](src/IsoGenerator.Core/CycleLengthSolver.cs#L19) `if(length<.001)throw new LayoutProjectionException($"{edge.Source}: участок вырождается в выбранном ракурсе; выберите другое направление севера.");`
- [Строка 23](src/IsoGenerator.Core/CycleLengthSolver.cs#L23) `var previous=new Dictionary<int,(int Node,int Edge)>{{a,(-1,-1)}};var queue=new Queue<int>();queue.Enqueue(a);`
- [Строка 24](src/IsoGenerator.Core/CycleLengthSolver.cs#L24) `while(queue.Count>0&&!previous.ContainsKey(b)){int node=queue.Dequeue();foreach(var next in tree[node])if(previous.TryAdd(next.Node,(node,next.Edge)))queue.Enqueue(next.Node);}`
- [Строка 27](src/IsoGenerator.Core/CycleLengthSolver.cs#L27) `{var step=previous[node];double sign=graph.Sets.Find(graph.Edges[step.Edge].A)==step.Node?1:-1;x[step.Edge]-=sign*directions[step.Edge].X;y[step.Edge]-=sign*directions[step.Edge].Y;node=step.Node;}`
- [Строка 36](src/IsoGenerator.Core/CycleLengthSolver.cs#L36) `for(int row=0;row<m;row++)`
- [Строка 37](src/IsoGenerator.Core/CycleLengthSolver.cs#L37) `{for(int edge=0;edge<preferred.Length;edge++)if(!variableSet.Contains(edge))b[row]-=cycles[row][edge]*preferred[edge];for(int column=0;column<n;column++)a[row,column]=cycles[row][variables[column]]*weights[column];}`
- [Строка 38](src/IsoGenerator.Core/CycleLengthSolver.cs#L38) `var gram=new double[m,m];var transform=new double[m,m];double largest=0;`
- [Строка 39](src/IsoGenerator.Core/CycleLengthSolver.cs#L39) `for(int i=0;i<m;i++)for(int j=0;j<m;j++){for(int k=0;k<n;k++)gram[i,j]+=a[i,k]*a[j,k];largest=Math.Max(largest,Math.Abs(gram[i,j]));transform[i,j]=i==j?1:0;}`
- [Строка 40](src/IsoGenerator.Core/CycleLengthSolver.cs#L40) `var pivots=new List<int>();int rank=0;`
- [Строка 41](src/IsoGenerator.Core/CycleLengthSolver.cs#L41) `for(int column=0;column<m;column++)`
- [Строка 44](src/IsoGenerator.Core/CycleLengthSolver.cs#L44) `if(Math.Abs(gram[pivot,column])<=Math.Max(1e-12,largest*1e-12))continue;`
- [Строка 45](src/IsoGenerator.Core/CycleLengthSolver.cs#L45) `for(int j=0;j<m;j++){(gram[pivot,j],gram[rank,j])=(gram[rank,j],gram[pivot,j]);(transform[pivot,j],transform[rank,j])=(transform[rank,j],transform[pivot,j]);}`
- [Строка 46](src/IsoGenerator.Core/CycleLengthSolver.cs#L46) `double divisor=gram[rank,column];for(int j=0;j<m;j++){gram[rank,j]/=divisor;transform[rank,j]/=divisor;}`
- [Строка 47](src/IsoGenerator.Core/CycleLengthSolver.cs#L47) `for(int r=0;r<m;r++)if(r!=rank){double factor=gram[r,column];for(int j=0;j<m;j++){gram[r,j]-=factor*gram[rank,j];transform[r,j]-=factor*transform[rank,j];}}`
- [Строка 52](src/IsoGenerator.Core/CycleLengthSolver.cs#L52) `var reduced=new double[m];for(int i=0;i<m;i++)for(int j=0;j<m;j++)reduced[i]+=transform[i,j]*rhs[j];`
- [Строка 53](src/IsoGenerator.Core/CycleLengthSolver.cs#L53) `for(int i=rank;i<m;i++)if(Math.Abs(reduced[i])>1e-5)throw new InvalidDataException("Контур не удаётся замкнуть изменением длин труб: проверьте направления портов жёстких элементов.");`
- [Строка 54](src/IsoGenerator.Core/CycleLengthSolver.cs#L54) `var result=new double[m];for(int i=0;i<rank;i++)result[pivots[i]]=reduced[i];return result;`
- [Строка 58](src/IsoGenerator.Core/CycleLengthSolver.cs#L58) `for(int iteration=0;iteration<6000;iteration++)`
- [Строка 60](src/IsoGenerator.Core/CycleLengthSolver.cs#L60) `if(iteration%32==0)PlanningExecution.Check();`
- [Строка 61](src/IsoGenerator.Core/CycleLengthSolver.cs#L61) `if(iteration%128==0)PlanningExecution.Report("Замыкание контуров");`
- [Строка 62](src/IsoGenerator.Core/CycleLengthSolver.cs#L62) `var trial=new double[n];for(int i=0;i<n;i++)trial[i]=value[i]+affine[i];`
- [Строка 63](src/IsoGenerator.Core/CycleLengthSolver.cs#L63) `var rhs=new double[m];for(int r=0;r<m;r++){rhs[r]=-b[r];for(int i=0;i<n;i++)rhs[r]+=a[r,i]*trial[i];}`
- [Строка 65](src/IsoGenerator.Core/CycleLengthSolver.cs#L65) `for(int i=0;i<n;i++){double projected=trial[i];for(int r=0;r<m;r++)projected-=a[r,i]*lambda[r];affine[i]=trial[i]-projected;double candidate=projected+bounds[i];next[i]=Math.Max((lowerBounds?[variables[i]]??minimum)/weight…`
- [Строка 66](src/IsoGenerator.Core/CycleLengthSolver.cs#L66) `value=next;error=0;for(int r=0;r<m;r++){double residual=-b[r];for(int i=0;i<n;i++)residual+=a[r,i]*value[i];error=Math.Max(error,Math.Abs(residual));}`
- [Строка 67](src/IsoGenerator.Core/CycleLengthSolver.cs#L67) `if(error<1e-6)break;`
- [Строка 69](src/IsoGenerator.Core/CycleLengthSolver.cs#L69) `if(error>.01)throw new SheetCapacityException($"Не удалось совместить замкнутые контуры с минимальной длиной труб; невязка {error:F3} мм.");`
- [Строка 71](src/IsoGenerator.Core/CycleLengthSolver.cs#L71) `for(int i=0;i<n;i++)lengths[variables[i]]=value[i]*weights[i];`
- [Строка 80](src/IsoGenerator.Core/CycleLengthSolver.cs#L80) `const double tolerance=.001;`
- [Строка 88](src/IsoGenerator.Core/CycleLengthSolver.cs#L88) `if(Math.Abs(near-closed[edge])>.01&&Math.Abs(far-near)>.01)`
- [Строка 93](src/IsoGenerator.Core/CycleLengthSolver.cs#L93) `if(adjustable.Count>7)return null;`
- [Строка 96](src/IsoGenerator.Core/CycleLengthSolver.cs#L96) `for(int mask=1;mask<(1<<adjustable.Count);mask++)`
- [Строка 99](src/IsoGenerator.Core/CycleLengthSolver.cs#L99) `for(int row=0;row<cycles.Count&&valid;row++)`
- [Строка 102](src/IsoGenerator.Core/CycleLengthSolver.cs#L102) `for(int i=0;i<adjustable.Count;i++)`
- [Строка 103](src/IsoGenerator.Core/CycleLengthSolver.cs#L103) `if((mask&(1<<i))!=0)error+=deltas[i][row];`
- [Строка 107](src/IsoGenerator.Core/CycleLengthSolver.cs#L107) `for(int i=0;i<adjustable.Count;i++)`
- [Строка 108](src/IsoGenerator.Core/CycleLengthSolver.cs#L108) `if((mask&(1<<i))!=0)snapped[adjustable[i].Edge]=adjustable[i].Other;`

### DimensionChainPlanner.cs

- [Строка 20](src/IsoGenerator.Core/DimensionChainPlanner.cs#L20) `static ModelPoint Unit(ModelPoint a)=>Mul(a,1/Math.Sqrt(Dot(a,a)));`
- [Строка 25](src/IsoGenerator.Core/DimensionChainPlanner.cs#L25) `if(Math.Abs(den)<1e-10)return null;`
- [Строка 27](src/IsoGenerator.Core/DimensionChainPlanner.cs#L27) `return p.DistanceTo(q)<1?Mul(Add(p,q),.5):null;`
- [Строка 32](src/IsoGenerator.Core/DimensionChainPlanner.cs#L32) `if(dx<-.0001||(Math.Abs(dx)<.0001&&dy<0)){nx=-nx;ny=-ny;}`
- [Строка 33](src/IsoGenerator.Core/DimensionChainPlanner.cs#L33) `var ex=d.ExtensionDirection!.Value;var line=d.DimensionLinePoint!.Value;var mid=new SheetPoint((d.Start.X+d.End.X)/2,(d.Start.Y+d.End.Y)/2);`
- [Строка 35](src/IsoGenerator.Core/DimensionChainPlanner.cs#L35) `return d with{TextPosition=new(mid.X+ex.X*t+nx*2.8,mid.Y+ex.Y*t+ny*2.8)};`
- [Строка 42](src/IsoGenerator.Core/DimensionChainPlanner.cs#L42) `void EdgeBetween(int a,int b,string source,string kind){if(nodes[a].Point.DistanceTo(nodes[b].Point)<.01)sets.Join(a,b);else edges.Add(new(a,b,source,kind));}`
- [Строка 49](src/IsoGenerator.Core/DimensionChainPlanner.cs#L49) `if(ends.Length<2)continue;`
- [Строка 52](src/IsoGenerator.Core/DimensionChainPlanner.cs#L52) `var corner=Corner(ends[0],ends[1]);if(corner is null)throw new InvalidDataException($"{p.Handle}: cannot dimension elbow without intersecting port axes");`
- [Строка 63](src/IsoGenerator.Core/DimensionChainPlanner.cs#L63) `else if(kind=="Tee"&&ends.Length==3)`
- [Строка 65](src/IsoGenerator.Core/DimensionChainPlanner.cs#L65) `var pair=(from i in Enumerable.Range(0,3) from j in Enumerable.Range(i+1,2-i) where Dot(Unit(ends[i].Direction),Unit(ends[j].Direction))<-.999 select (i,j)).FirstOrDefault((-1,-1));`
- [Строка 66](src/IsoGenerator.Core/DimensionChainPlanner.cs#L66) `if(pair.Item1<0)throw new InvalidDataException($"{p.Handle}: tee has no opposite run ports");`
- [Строка 67](src/IsoGenerator.Core/DimensionChainPlanner.cs#L67) `var a=ends[pair.Item1];var b=ends[pair.Item2];var branch=ends[Enumerable.Range(0,3).Single(i=>i!=pair.Item1&&i!=pair.Item2)];`
- [Строка 75](src/IsoGenerator.Core/DimensionChainPlanner.cs#L75) `var a=ends[0];var b=ends[1];var dir=Unit(Sub(b.Position,a.Position));double length=a.Position.DistanceTo(b.Position);`
- [Строка 76](src/IsoGenerator.Core/DimensionChainPlanner.cs#L76) `var stations=new List<(double T,int Id)>{(0,Id(a)),(length,Id(b))};`
- [Строка 80](src/IsoGenerator.Core/DimensionChainPlanner.cs#L80) `if(t<-.01||t>length+.01)throw new InvalidDataException($"{p.Handle}: dynamic port outside pipe length");`
- [Строка 81](src/IsoGenerator.Core/DimensionChainPlanner.cs#L81) `var axisPoint=Add(a.Position,Mul(dir,Math.Clamp(t,0,length)));`
- [Строка 82](src/IsoGenerator.Core/DimensionChainPlanner.cs#L82) `int center=NodeAt(axisPoint,"TapAxis",p.Handle,dynamicPort.Name);stations.Add((Math.Clamp(t,0,length),center));`
- [Строка 86](src/IsoGenerator.Core/DimensionChainPlanner.cs#L86) `for(int i=1;i<sorted.Length;i++)EdgeBetween(sorted[i-1].Id,sorted[i].Id,p.Handle,kind);`
- [Строка 88](src/IsoGenerator.Core/DimensionChainPlanner.cs#L88) `else if(ends.Length>2)`
- [Строка 93](src/IsoGenerator.Core/DimensionChainPlanner.cs#L93) `else if(ends.Length==2)EdgeBetween(Id(ends[0]),Id(ends[1]),p.Handle,kind);`
- [Строка 109](src/IsoGenerator.Core/DimensionChainPlanner.cs#L109) `public static IReadOnlyList<PipeDimension> Build(PipelineSnapshot snapshot,Func<ModelPoint,SheetPoint> project,int quarterTurns=0,IReadOnlyList<SymbolGlyph>? symbols=null,IReadOnlyList<Stroke>? obstacles=null,Func<ModelPoi…`
- [Строка 114](src/IsoGenerator.Core/DimensionChainPlanner.cs#L114) `for(int i=0;i<edges.Count;i++)`
- [Строка 123](src/IsoGenerator.Core/DimensionChainPlanner.cs#L123) `for(int i=0;i<list.Count;i++)for(int j=i+1;j<list.Count;j++)`
- [Строка 127](src/IsoGenerator.Core/DimensionChainPlanner.cs#L127) `double tolerance=a.Kind is "Elbow" or "Tee"||b.Kind is "Elbow" or "Tee"?.5:.1;`
- [Строка 128](src/IsoGenerator.Core/DimensionChainPlanner.cs#L128) `if(Math.Abs(Dot(Direction(a),Direction(b)))>Math.Cos(tolerance*Math.PI/180))groups.Join(list[i],list[j]);`
- [Строка 130](src/IsoGenerator.Core/DimensionChainPlanner.cs#L130) `var rootNodes=Enumerable.Range(0,nodes.Count).GroupBy(sets.Find).ToDictionary(g=>g.Key,g=>g.ToArray());`
- [Строка 132](src/IsoGenerator.Core/DimensionChainPlanner.cs#L132) `var allProjected=Enumerable.Range(0,nodes.Count).Select(ProjectNode).ToArray();`
- [Строка 133](src/IsoGenerator.Core/DimensionChainPlanner.cs#L133) `var sceneCenter=new SheetPoint((allProjected.Min(p=>p.X)+allProjected.Max(p=>p.X))/2,(allProjected.Min(p=>p.Y)+allProjected.Max(p=>p.Y))/2);`
- [Строка 135](src/IsoGenerator.Core/DimensionChainPlanner.cs#L135) `var dimensions=new List<PipeDimension>();int chainNumber=0;`
- [Строка 137](src/IsoGenerator.Core/DimensionChainPlanner.cs#L137) `foreach(var group in Enumerable.Range(0,edges.Count).GroupBy(groups.Find))`
- [Строка 140](src/IsoGenerator.Core/DimensionChainPlanner.cs#L140) `var direction=Direction(edges[indices[0]]);`
- [Строка 142](src/IsoGenerator.Core/DimensionChainPlanner.cs#L142) `if(Math.Abs(direction.X)>1e-6?direction.X<0:Math.Abs(direction.Y)>1e-6?direction.Y<0:direction.Z<0)direction=Mul(direction,-1);`
- [Строка 144](src/IsoGenerator.Core/DimensionChainPlanner.cs#L144) `var origin=nodes[roots[0]].Point;`
- [Строка 150](src/IsoGenerator.Core/DimensionChainPlanner.cs#L150) `bool anchor=degree!=2||tags.Contains("Axis")||tags.Contains("TapAxis")||tags.Contains("ValvePort")||tags.Contains("FlangePort")||(!tags.Contains("ElbowPort")&&!tags.Contains("TeePort")&&tags.Contains("PipePort"));`
- [Строка 154](src/IsoGenerator.Core/DimensionChainPlanner.cs#L154) `if(degree==2&&tapSurface&&!tags.Contains("TapAxis"))anchor=false;`
- [Строка 161](src/IsoGenerator.Core/DimensionChainPlanner.cs#L161) `for(int i=ordered.Count-1;i>0;i--)if(Math.Abs(ordered[i].T-ordered[i-1].T)<.01)ordered.RemoveAt(i);`
- [Строка 165](src/IsoGenerator.Core/DimensionChainPlanner.cs#L165) `for(int i=1;i<ordered.Count&&ordered.Count>2;i++)`
- [Строка 170](src/IsoGenerator.Core/DimensionChainPlanner.cs#L170) `return Math.Max(ta,tb)>ordered[i-1].T+.001&&Math.Min(ta,tb)<ordered[i].T-.001;`
- [Строка 173](src/IsoGenerator.Core/DimensionChainPlanner.cs#L173) `ordered.RemoveAt(i==1?i:i-1);i=Math.Max(0,i-2);`
- [Строка 175](src/IsoGenerator.Core/DimensionChainPlanner.cs#L175) `if(ordered.Count<2)continue;`
- [Строка 177](src/IsoGenerator.Core/DimensionChainPlanner.cs#L177) `var a2=ProjectNode(ordered[0].Root);var b2=ProjectNode(ordered[^1].Root);double dx=b2.X-a2.X,dy=b2.Y-a2.Y,screenLength=Math.Sqrt(dx*dx+dy*dy);`
- [Строка 178](src/IsoGenerator.Core/DimensionChainPlanner.cs#L178) `runEnds[chainId]=new(ordered[0].Root,ordered[^1].Root,a2,b2);`
- [Строка 179](src/IsoGenerator.Core/DimensionChainPlanner.cs#L179) `if(screenLength<.1)`
- [Строка 185](src/IsoGenerator.Core/DimensionChainPlanner.cs#L185) `var mid=new SheetPoint((a2.X+b2.X)/2,(a2.Y+b2.Y)/2);`
- [Строка 188](src/IsoGenerator.Core/DimensionChainPlanner.cs#L188) `double score=-.01*((mid.X-sceneCenter.X)*sx+(mid.Y-sceneCenter.Y)*sy);`
- [Строка 202](src/IsoGenerator.Core/DimensionChainPlanner.cs#L202) `while(pending.Count>0)`
- [Строка 206](src/IsoGenerator.Core/DimensionChainPlanner.cs#L206) `if(Math.Sqrt(Math.Pow(point.X-at.X,2)+Math.Pow(point.Y-at.Y,2))>.1)`
- [Строка 208](src/IsoGenerator.Core/DimensionChainPlanner.cs#L208) `if((point.X-at.X)*sx+(point.Y-at.Y)*sy>.1)score+=tap?1000000:100;`
- [Строка 221](src/IsoGenerator.Core/DimensionChainPlanner.cs#L221) `double den=cross(b.X-a.X,b.Y-a.Y,d.X-c.X,d.Y-c.Y);if(Math.Abs(den)<1e-8)return false;`
- [Строка 224](src/IsoGenerator.Core/DimensionChainPlanner.cs#L224) `return t>.001&&t<.999&&u>.001&&u<.999;`
- [Строка 226](src/IsoGenerator.Core/DimensionChainPlanner.cs#L226) `foreach(double offset in new[]{10.0,20.0})`
- [Строка 229](src/IsoGenerator.Core/DimensionChainPlanner.cs#L229) `foreach(int i in Enumerable.Range(0,edges.Count).Where(i=>!inside.Contains(i)))`
- [Строка 230](src/IsoGenerator.Core/DimensionChainPlanner.cs#L230) `if(Cross(start,end,ProjectNode(edges[i].A),ProjectNode(edges[i].B)))score+=1000;`
- [Строка 235](src/IsoGenerator.Core/DimensionChainPlanner.cs#L235) `var axisCandidates=new[]{new ModelPoint(1,0,0),new ModelPoint(0,1,0),new ModelPoint(0,0,1)}`
- [Строка 239](src/IsoGenerator.Core/DimensionChainPlanner.cs#L239) `var extension=axisCandidates[0];`
- [Строка 242](src/IsoGenerator.Core/DimensionChainPlanner.cs#L242) `if(Math.Abs(ny)>.2)`
- [Строка 243](src/IsoGenerator.Core/DimensionChainPlanner.cs#L243) `{if(ny<0){nx=-nx;ny=-ny;}extension=new(0,1);}`
- [Строка 244](src/IsoGenerator.Core/DimensionChainPlanner.cs#L244) `else if(extension.X*nx+extension.Y*ny<0)extension=new(-extension.X,-extension.Y);`
- [Строка 253](src/IsoGenerator.Core/DimensionChainPlanner.cs#L253) `.Select(p=>(p.X-a2.X)*nx+(p.Y-a2.Y)*ny).DefaultIfEmpty(0).Max();`
- [Строка 254](src/IsoGenerator.Core/DimensionChainPlanner.cs#L254) `double firstOffset=Math.Max(0,envelope)+10;`
- [Строка 259](src/IsoGenerator.Core/DimensionChainPlanner.cs#L259) `string source=nodes[rootNodes[ordered[i].Root][0]].Source;`
- [Строка 264](src/IsoGenerator.Core/DimensionChainPlanner.cs#L264) `}).FirstOrDefault(e=>e.High>ordered[i].T+.001&&e.Low<ordered[j].T-.001);`
- [Строка 266](src/IsoGenerator.Core/DimensionChainPlanner.cs#L266) `var intervalKinds=indices.Select(k=>edges[k]).Where(edge=>{double ta=Dot(Sub(nodes[edge.A].Point,origin),direction),tb=Dot(Sub(nodes[edge.B].Point,origin),direction);return Math.Max(ta,tb)>ordered[i].T+.001&&Math.Min(ta,tb…`
- [Строка 268](src/IsoGenerator.Core/DimensionChainPlanner.cs#L268) `return new(source,start,end,ordered[j].T-ordered[i].T,role,chainId,linePoint,extension,ordered[i].Point,ordered[j].Point,sources,DisplayMillimeters:Math.Ceiling(ordered[j].T-ordered[i].T-1e-8),ReferenceOnly:reference);`
- [Строка 271](src/IsoGenerator.Core/DimensionChainPlanner.cs#L271) `if(ordered.Count>2)for(int i=1;i<ordered.Count;i++)`
- [Строка 276](src/IsoGenerator.Core/DimensionChainPlanner.cs#L276) `return Math.Max(ta,tb)>ordered[i-1].T+.001&&Math.Min(ta,tb)<ordered[i].T-.001;`
- [Строка 280](src/IsoGenerator.Core/DimensionChainPlanner.cs#L280) `if(kinds.Length>0&&kinds.All(kind=>kind is "Joint" or "Tee" or "Elbow"))continue;`
- [Строка 281](src/IsoGenerator.Core/DimensionChainPlanner.cs#L281) `dimensions.Add(Dimension(i-1,i,"chain",firstOffset));`
- [Строка 283](src/IsoGenerator.Core/DimensionChainPlanner.cs#L283) `dimensions.Add(Dimension(0,ordered.Count-1,"overall",ordered.Count>2?firstOffset+7:firstOffset));`
- [Строка 285](src/IsoGenerator.Core/DimensionChainPlanner.cs#L285) `return PackDimensionText(DimensionCorners.Align(dimensions,runEnds,new[]{new ModelPoint(1,0,0),new ModelPoint(0,1,0),new ModelPoint(0,0,1)}.Select(v=>IsometricProjection.Project(v,quarterTurns:quarterTurns)).Select(v=>{dou…`

### DimensionCongestion.cs

- [Строка 27](src/IsoGenerator.Core/DimensionCongestion.cs#L27) `return points.Zip(points.Skip(1).Append(points[0]),(a,b)=>new DimensionRouting.Segment(a,b,false)).Any(edge=>DimensionRouting.CheckCross(new(row.Start,row.End,true),edge));`
- [Строка 31](src/IsoGenerator.Core/DimensionCongestion.cs#L31) `for(int depth=0;depth<8&&frontier.Length>0;depth++)`
- [Строка 37](src/IsoGenerator.Core/DimensionCongestion.cs#L37) `double dx=stroke.Points[^1].X-stroke.Points[0].X,dy=stroke.Points[^1].Y-stroke.Points[0].Y,length=Math.Sqrt(dx*dx+dy*dy);`
- [Строка 38](src/IsoGenerator.Core/DimensionCongestion.cs#L38) `if(length>1e-6&&Math.Abs(dx*row.Normal.X+dy*row.Normal.Y)/length>.25)`
- [Строка 40](src/IsoGenerator.Core/DimensionCongestion.cs#L40) `if(Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1")`
- [Строка 50](src/IsoGenerator.Core/DimensionCongestion.cs#L50) `for(int i=0;i<rows.Length;i++)for(int j=i+1;j<rows.Length;j++)`
- [Строка 52](src/IsoGenerator.Core/DimensionCongestion.cs#L52) `var a=rows[i];var b=rows[j];if(a.Dimension.ChainId==b.Dimension.ChainId||Math.Abs(Dot(a.Axis,b.Axis))<.999)continue;`
- [Строка 55](src/IsoGenerator.Core/DimensionCongestion.cs#L55) `if(gap<7-1e-6&&overlap>3)conflicts.Add((a,b,gap));`
- [Строка 63](src/IsoGenerator.Core/DimensionCongestion.cs#L63) `while(queue.Count>0){var h=queue.Dequeue();if(to.Contains(h)){reached=h;break;}foreach(var next in adjacency[h])if(previous.TryAdd(next,h))queue.Enqueue(next);}`
- [Строка 69](src/IsoGenerator.Core/DimensionCongestion.cs#L69) `if(stroke is null)return (Handle:h,Score:0.0);`
- [Строка 70](src/IsoGenerator.Core/DimensionCongestion.cs#L70) `var a=stroke.Points[0];var b=stroke.Points[^1];double dx=b.X-a.X,dy=b.Y-a.Y,l=Math.Sqrt(dx*dx+dy*dy);`
- [Строка 71](src/IsoGenerator.Core/DimensionCongestion.cs#L71) `return (Handle:h,Score:Math.Abs(dx*conflict.A.Normal.X+dy*conflict.A.Normal.Y)/Math.Max(.001,l));`
- [Строка 72](src/IsoGenerator.Core/DimensionCongestion.cs#L72) `}).Where(c=>c.Score>.25).OrderByDescending(c=>c.Score).ToArray();`
- [Строка 73](src/IsoGenerator.Core/DimensionCongestion.cs#L73) `if(candidates.Length>0)return candidates[0].Handle;`
- [Строка 77](src/IsoGenerator.Core/DimensionCongestion.cs#L77) `var pipeStrokes = scene.Strokes.Where(s => s.Role == "pipe" && s.Points.Count >= 2).ToArray();`
- [Строка 85](src/IsoGenerator.Core/DimensionCongestion.cs#L85) `return scene.Annotations.Where(a=>a.Role=="continuation"&&a.Anchor.HasValue&&parts.ContainsKey(a.SourceId)).OrderBy(a=>Math.Pow(a.Anchor!.Value.X-point.X,2)+Math.Pow(a.Anchor.Value.Y-point.Y,2)).FirstOrDefault()?.SourceId;`
- [Строка 95](src/IsoGenerator.Core/DimensionCongestion.cs#L95) `foreach(var context in scene.Strokes.Where(st=>st.Role.StartsWith("context-",StringComparison.Ordinal)&&st.Points.Count>1))`
- [Строка 96](src/IsoGenerator.Core/DimensionCongestion.cs#L96) `foreach(var (a,b) in context.Points.Zip(context.Points.Skip(1)))`
- [Строка 98](src/IsoGenerator.Core/DimensionCongestion.cs#L98) `var middle=new SheetPoint((a.X+b.X)/2,(a.Y+b.Y)/2);string? owner=ContextOwner(context.SourceId,middle);`
- [Строка 102](src/IsoGenerator.Core/DimensionCongestion.cs#L102) `foreach (var c in scene.Strokes.Where(s => s.Role == "continuation" && s.Points.Count >= 2))`
- [Строка 112](src/IsoGenerator.Core/DimensionCongestion.cs#L112) `var p1 = ps.Points[0]; var p2 = ps.Points[^1];`
- [Строка 114](src/IsoGenerator.Core/DimensionCongestion.cs#L114) `if (l2 < 1.0) continue;`
- [Строка 117](src/IsoGenerator.Core/DimensionCongestion.cs#L117) `double pipeZ = pipePart.Ports.Count > 0 ? pipePart.Ports.Average(p => p.Position.Z) : 0;`
- [Строка 125](src/IsoGenerator.Core/DimensionCongestion.cs#L125) `if (t <= 0.05 || t >= 0.95) continue;`
- [Строка 129](src/IsoGenerator.Core/DimensionCongestion.cs#L129) `if (dist >= 6.0) continue;`
- [Строка 132](src/IsoGenerator.Core/DimensionCongestion.cs#L132) `double obsZ = obsPart.Ports.Count > 0 ? obsPart.Ports.Average(p => p.Position.Z) : 0;`
- [Строка 133](src/IsoGenerator.Core/DimensionCongestion.cs#L133) `if (!contextOwners.Contains(obsId)&&Math.Abs(pipeZ - obsZ) < 50.0) continue;`
- [Строка 135](src/IsoGenerator.Core/DimensionCongestion.cs#L135) `var normal = dist > 0.001`
- [Строка 148](src/IsoGenerator.Core/DimensionCongestion.cs#L148) `while (queue.Count > 0)`
- [Строка 168](src/IsoGenerator.Core/DimensionCongestion.cs#L168) `if (stroke is null) return (Handle: h, Score: 0.0);`
- [Строка 169](src/IsoGenerator.Core/DimensionCongestion.cs#L169) `var a = stroke.Points[0]; var b = stroke.Points[^1];`
- [Строка 171](src/IsoGenerator.Core/DimensionCongestion.cs#L171) `if (l < 0.01) return (Handle: h, Score: 0.0);`
- [Строка 173](src/IsoGenerator.Core/DimensionCongestion.cs#L173) `if (Math.Abs(dx) < 0.01 && Math.Abs(dy) > 0.1) score += 0.2; // bonus for vertical riser/drop`
- [Строка 176](src/IsoGenerator.Core/DimensionCongestion.cs#L176) `.Where(c => c.Score > 0.25)`
- [Строка 180](src/IsoGenerator.Core/DimensionCongestion.cs#L180) `if (candidates.Length > 0) return candidates[0].Handle;`

### DimensionCorners.cs

- [Строка 9](src/IsoGenerator.Core/DimensionCorners.cs#L9) `static SheetPoint Normal(PipeDimension d){double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,l=Math.Sqrt(dx*dx+dy*dy);var n=new SheetPoint(-dy/l,dx/l);return Dot(n,d.ExtensionDirection!.Value)<0?new(-n.X,-n.Y):n;}`
- [Строка 11](src/IsoGenerator.Core/DimensionCorners.cs#L11) `for(int i=0;i<ids.Length;i++)for(int j=i+1;j<ids.Length;j++)`
- [Строка 15](src/IsoGenerator.Core/DimensionCorners.cs#L15) `int shared=ea.Start==eb.Start||ea.Start==eb.End?ea.Start:ea.End==eb.Start||ea.End==eb.End?ea.End:-1;if(shared<0)continue;`
- [Строка 16](src/IsoGenerator.Core/DimensionCorners.cs#L16) `var a=Inner(ids[i]);var b=Inner(ids[j]);var na=Normal(a);var nb=Normal(b);if(Math.Abs(Dot(na,nb))>.999)continue;`
- [Строка 19](src/IsoGenerator.Core/DimensionCorners.cs#L19) `SheetPoint? chosen=null;double da=0,db=0,best=double.MaxValue;`
- [Строка 22](src/IsoGenerator.Core/DimensionCorners.cs#L22) `if(Dot(axis,a.ExtensionDirection!.Value)<.999||Dot(axis,b.ExtensionDirection!.Value)<.999)continue;`
- [Строка 23](src/IsoGenerator.Core/DimensionCorners.cs#L23) `if(locked.TryGetValue(ids[i],out var la)&&Dot(la,axis)<.999||locked.TryGetValue(ids[j],out var lb)&&Dot(lb,axis)<.999)continue;`
- [Строка 24](src/IsoGenerator.Core/DimensionCorners.cs#L24) `double ca=Dot(axis,na),cb=Dot(axis,nb);if(ca<.2||cb<.2)continue;`
- [Строка 26](src/IsoGenerator.Core/DimensionCorners.cs#L26) `if(ma>7||mb>7)continue;`
- [Строка 30](src/IsoGenerator.Core/DimensionCorners.cs#L30) `for(int k=0;k<result.Length;k++)if(result[k].ChainId==ids[i]||result[k].ChainId==ids[j])`

### DimensionRouting.cs

- [Строка 10](src/IsoGenerator.Core/DimensionRouting.cs#L10) `if(g.LocalContours is {Count:>0})return g.LocalContours.Select(contour=>new Stroke(g.SourceId,"symbol-contour",contour.Select(p=>new SheetPoint(g.Origin.X+g.XAxis.X*p.X+g.YAxis.X*p.Y,g.Origin.Y+g.XAxis.Y*p.X+g.YAxis.Y*p.Y)…`
- [Строка 11](src/IsoGenerator.Core/DimensionRouting.cs#L11) `var corners=SymbolFootprint.Corners(g).ToArray();return new[]{new Stroke(g.SourceId,"symbol-contour",corners.Append(corners[0]).ToArray())};`
- [Строка 19](src/IsoGenerator.Core/DimensionRouting.cs#L19) `var u=Sub(a.B,a.A);var v=Sub(b.B,b.A);double den=u.X*v.Y-u.Y*v.X;if(Math.Abs(den)<1e-8)return false;`
- [Строка 22](src/IsoGenerator.Core/DimensionRouting.cs#L22) `return t>1e-5&&t<1-1e-5&&s>1e-5&&s<1-1e-5;`
- [Строка 33](src/IsoGenerator.Core/DimensionRouting.cs#L33) `.Where(stroke=>stroke.Role=="pipe"&&stroke.Points.Count>=2)`
- [Строка 34](src/IsoGenerator.Core/DimensionRouting.cs#L34) `.Select(stroke=>(stroke.SourceId,new Segment(stroke.Points[0],stroke.Points[^1],false))).ToArray();`
- [Строка 42](src/IsoGenerator.Core/DimensionRouting.cs#L42) `var basis=dimensions.FirstOrDefault(row=>row.Role=="overall")??dimensions[0];`
- [Строка 52](src/IsoGenerator.Core/DimensionRouting.cs#L52) `runs.Where(run=>!sources.Contains(run.Source)&&Math.Abs(Dot(Unit(Sub(run.Axis.B,run.Axis.A)),direction))>=.999)`
- [Строка 57](src/IsoGenerator.Core/DimensionRouting.cs#L57) `if(obstacles.Length==0)return false;`
- [Строка 61](src/IsoGenerator.Core/DimensionRouting.cs#L61) `double threshold=Math.Min(35,IsoAdvancedSettings.Current.LongExtensionThreshold);`
- [Строка 68](src/IsoGenerator.Core/DimensionRouting.cs#L68) `var isoAxes = new[] { new ModelPoint(1, 0, 0), new ModelPoint(0, 1, 0), new ModelPoint(0, 0, 1) }`
- [Строка 73](src/IsoGenerator.Core/DimensionRouting.cs#L73) `.SelectMany(s=>s.Points.Zip(s.Points.Skip(1),(a,b)=>new Segment(a,b,true))).ToArray();`
- [Строка 78](src/IsoGenerator.Core/DimensionRouting.cs#L78) `var overall=rows.FirstOrDefault(d=>d.Role=="overall")??rows[0];var axis=Unit(Sub(overall.End,overall.Start));var n=new SheetPoint(-axis.Y,axis.X);`
- [Строка 79](src/IsoGenerator.Core/DimensionRouting.cs#L79) `if(Dot(n,overall.ExtensionDirection!.Value)<0)n=new(-n.X,-n.Y);`
- [Строка 83](src/IsoGenerator.Core/DimensionRouting.cs#L83) `bool vertical=Math.Abs(axis.X)<.05;`
- [Строка 85](src/IsoGenerator.Core/DimensionRouting.cs#L85) `exs.AddRange(isoAxes.Where(ex=>Math.Abs(Dot(ex,n))>.5&&Math.Abs(Dot(ex,exs[0]))<.999));`
- [Строка 89](src/IsoGenerator.Core/DimensionRouting.cs#L89) `var contextSegments=strokes.Where(s=>s.Role.StartsWith("context-",StringComparison.Ordinal)).SelectMany(s=>s.Points.Zip(s.Points.Skip(1),(a,b)=>new Segment(a,b,false))).ToArray();`
- [Строка 90](src/IsoGenerator.Core/DimensionRouting.cs#L90) `var symbolEdges=TextObstacles(Array.Empty<Stroke>(),symbols).SelectMany(s=>s.Points.Zip(s.Points.Skip(1),(a,b)=>new Segment(a,b,false))).ToArray();`
- [Строка 95](src/IsoGenerator.Core/DimensionRouting.cs#L95) `foreach(int side in new[]{1,-1})`
- [Строка 97](src/IsoGenerator.Core/DimensionRouting.cs#L97) `double oppositeEnvelope=contour.Select(p=>Dot(Sub(p,overall.Start),n)*side).Where(v=>v>=0&&v<=25).DefaultIfEmpty(0).Max();`
- [Строка 98](src/IsoGenerator.Core/DimensionRouting.cs#L98) `double first=side==1?Math.Max(MinimumPipeClearance,current):MinimumPipeClearance+oppositeEnvelope;`
- [Строка 99](src/IsoGenerator.Core/DimensionRouting.cs#L99) `foreach(var ex in exs)for(int lane=0;lane<=IsoAdvancedSettings.Current.DimensionLaneCount;lane++)`
- [Строка 104](src/IsoGenerator.Core/DimensionRouting.cs#L104) `double offset=side*(first+IsoAdvancedSettings.Current.DimensionRowStep*(lane+(hasChain&&d.Role=="overall"?1:0)));`
- [Строка 108](src/IsoGenerator.Core/DimensionRouting.cs#L108) `if(ownAxis.X<-.0001||(Math.Abs(ownAxis.X)<.0001&&ownAxis.Y<0))readableNormal=new(-readableNormal.X,-readableNormal.Y);`
- [Строка 109](src/IsoGenerator.Core/DimensionRouting.cs#L109) `placed.Add(moved with{TextPosition=new((a.X+b.X)/2+readableNormal.X*IsoAdvancedSettings.Current.TextLift,(a.Y+b.Y)/2+readableNormal.Y*IsoAdvancedSettings.Current.TextLift)});`
- [Строка 113](src/IsoGenerator.Core/DimensionRouting.cs#L113) `double corridorPenalty=0;`
- [Строка 114](src/IsoGenerator.Core/DimensionRouting.cs#L114) `foreach(var pipe in strokes.Where(st=>st.Role=="pipe"&&!source.Contains(st.SourceId)&&st.Points.Count>=2))`
- [Строка 116](src/IsoGenerator.Core/DimensionRouting.cs#L116) `var p=pipe.Points[0];var q=pipe.Points[^1];double px=q.X-p.X,py=q.Y-p.Y,pl=Math.Sqrt(px*px+py*py);`
- [Строка 117](src/IsoGenerator.Core/DimensionRouting.cs#L117) `if(pl<1||Math.Abs((px*axis.X+py*axis.Y)/pl)<.999)continue;`
- [Строка 119](src/IsoGenerator.Core/DimensionRouting.cs#L119) `if(Math.Abs(otherOffset)<5||Math.Abs(otherOffset)>60)continue;`
- [Строка 120](src/IsoGenerator.Core/DimensionRouting.cs#L120) `double overlap=Math.Min(Dot(Sub(q,overall.Start),axis)>Dot(Sub(p,overall.Start),axis)?Dot(Sub(q,overall.Start),axis):Dot(Sub(p,overall.Start),axis),Math.Sqrt(Math.Pow(overall.End.X-overall.Start.X,2)+Math.Pow(overall.End.Y…`
- [Строка 121](src/IsoGenerator.Core/DimensionRouting.cs#L121) `if(overlap<5)continue;`
- [Строка 122](src/IsoGenerator.Core/DimensionRouting.cs#L122) `double offset=Dot(Sub(placed[0].DimensionLinePoint!.Value,overall.Start),n);`
- [Строка 123](src/IsoGenerator.Core/DimensionRouting.cs#L123) `if(offset*otherOffset>0&&Math.Abs(offset)<Math.Abs(otherOffset)+3)corridorPenalty+=5000;`
- [Строка 128](src/IsoGenerator.Core/DimensionRouting.cs#L128) `OverlapsContext(line,context)?(line.Baseline?50000d:200d):`
- [Строка 129](src/IsoGenerator.Core/DimensionRouting.cs#L129) `Cross(line,context)?(line.Baseline?1000d:30d):0d));`
- [Строка 130](src/IsoGenerator.Core/DimensionRouting.cs#L130) `double cost=100000*PipeClearanceViolations(lines,pipes)+corridorPenalty+contextCost+`
- [Строка 131](src/IsoGenerator.Core/DimensionRouting.cs#L131) `DimensionTextPlacement.CollisionCount(placed,strokes,textSegments)*1000000+`
- [Строка 132](src/IsoGenerator.Core/DimensionRouting.cs#L132) `lines.Where(line=>line.Baseline).Sum(line=>symbolEdges.Count(edge=>Cross(line,edge)))*5000+`
- [Строка 133](src/IsoGenerator.Core/DimensionRouting.cs#L133) `modelCross*5000+(vertical?first+7*lane:lane*7+first*.1)+(side==-1?.5:0)+(!exs[0].Equals(ex)?1:0);`
- [Строка 148](src/IsoGenerator.Core/DimensionRouting.cs#L148) `var edges=group.SelectMany(stroke=>stroke.Points.Zip(stroke.Points.Skip(1),(a,b)=>new Segment(a,b,false))).ToArray();`
- [Строка 156](src/IsoGenerator.Core/DimensionRouting.cs#L156) `.SelectMany(stroke=>stroke.Points.Zip(stroke.Points.Skip(1),(a,b)=>new Segment(a,b,false))).ToArray();`
- [Строка 163](src/IsoGenerator.Core/DimensionRouting.cs#L163) `int violations=0;`
- [Строка 168](src/IsoGenerator.Core/DimensionRouting.cs#L168) `if(length<.01)continue;`
- [Строка 173](src/IsoGenerator.Core/DimensionRouting.cs#L173) `if(span<.01||Math.Abs(Dot(direction,axis))/span<.999)continue;`
- [Строка 175](src/IsoGenerator.Core/DimensionRouting.cs#L175) `double overlapStart=Math.Max(0,Math.Min(Dot(first,axis),Dot(last,axis)));`
- [Строка 177](src/IsoGenerator.Core/DimensionRouting.cs#L177) `if(overlapEnd-overlapStart<.1)continue;`
- [Строка 181](src/IsoGenerator.Core/DimensionRouting.cs#L181) `double gap=gapA*gapB<=0?0:Math.Min(Math.Abs(gapA),Math.Abs(gapB));`
- [Строка 182](src/IsoGenerator.Core/DimensionRouting.cs#L182) `if(gap<MinimumPipeClearance-.05)violations++;`
- [Строка 191](src/IsoGenerator.Core/DimensionRouting.cs#L191) `double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,length=Math.Sqrt(dx*dx+dy*dy);if(length<.01)return false;`
- [Строка 194](src/IsoGenerator.Core/DimensionRouting.cs#L194) `return strokes.Where(st=>st.Role=="pipe"&&!sources.Contains(st.SourceId)&&st.Points.Count>=2).Any(st=>`
- [Строка 196](src/IsoGenerator.Core/DimensionRouting.cs#L196) `var a=st.Points[0];var b=st.Points[^1];var v=Sub(b,a);double l=Math.Sqrt(Dot(v,v));if(l<1||Math.Abs(Dot(v,u)/l)<.999)return false;`
- [Строка 198](src/IsoGenerator.Core/DimensionRouting.cs#L198) `return Math.Abs(other)>=5&&Math.Abs(other)<=60&&other*offset>0&&Math.Abs(offset)<Math.Abs(other)+3&&Math.Min(length,Math.Max(first,last))-Math.Max(0,Math.Min(first,last))>5;`
- [Строка 202](src/IsoGenerator.Core/DimensionRouting.cs#L202) `double dx=a.B.X-a.A.X,dy=a.B.Y-a.A.Y,l=Math.Sqrt(dx*dx+dy*dy);if(l<.001)return false;`
- [Строка 205](src/IsoGenerator.Core/DimensionRouting.cs#L205) `if(Math.Max(h1,h2)>.8)return false;`
- [Строка 207](src/IsoGenerator.Core/DimensionRouting.cs#L207) `return Math.Min(l,Math.Max(t1,t2))-Math.Max(0,Math.Min(t1,t2))>1;`
- [Строка 213](src/IsoGenerator.Core/DimensionRouting.cs#L213) `var groups=dimensions.GroupBy(d=>d.ChainId).Select(g=>Segments(g)).ToArray();int result=0;`
- [Строка 214](src/IsoGenerator.Core/DimensionRouting.cs#L214) `for(int i=0;i<groups.Length;i++)for(int j=i+1;j<groups.Length;j++)result+=Conflicts(groups[i],groups[j]);`
- [Строка 224](src/IsoGenerator.Core/DimensionRouting.cs#L224) `.SelectMany(s=>s.Points.Zip(s.Points.Skip(1),(a,b)=>new Segment(a,b,true))).ToArray();`
- [Строка 230](src/IsoGenerator.Core/DimensionRouting.cs#L230) `if(input.Count==0)return input;`
- [Строка 233](src/IsoGenerator.Core/DimensionRouting.cs#L233) `.SelectMany(s=>s.Points.Zip(s.Points.Skip(1),(a,b)=>new Segment(a,b,true))).ToArray();`
- [Строка 234](src/IsoGenerator.Core/DimensionRouting.cs#L234) `var symbolSegments=TextObstacles(Array.Empty<Stroke>(),symbols).SelectMany(s=>s.Points.Zip(s.Points.Skip(1),(a,b)=>new Segment(a,b,false))).ToArray();`
- [Строка 237](src/IsoGenerator.Core/DimensionRouting.cs#L237) `if(CrossingCount(input)==0&&LongWitnessThroughParallelRun(input,strokes) is null&&PipeClearanceViolations(input,strokes)==0&&!crossesGeometry&&!crossesSymbols&&!input.Any(d=>InCorridor(d,strokes))&&DimensionTextPlacement.C…`
- [Строка 239](src/IsoGenerator.Core/DimensionRouting.cs#L239) `double Extent(PipeDimension[] rows){var d=rows.FirstOrDefault(x=>x.Role=="overall")??rows[0];return Dot(Sub(d.End,d.Start),Sub(d.End,d.Start));}`
- [Строка 240](src/IsoGenerator.Core/DimensionRouting.cs#L240) `var isoAxes = new[] { new ModelPoint(1, 0, 0), new ModelPoint(0, 1, 0), new ModelPoint(0, 0, 1) }`
- [Строка 245](src/IsoGenerator.Core/DimensionRouting.cs#L245) `var options=groups.ToDictionary(g=>g[0].ChainId,Choices);`
- [Строка 247](src/IsoGenerator.Core/DimensionRouting.cs#L247) `if(options[group[0].ChainId].Length==0)`
- [Строка 248](src/IsoGenerator.Core/DimensionRouting.cs#L248) `throw new SheetCapacityException("Нет места для размерной цепочки без пересечения соседней параллельной трубы.",group[0].SourceId);`
- [Строка 250](src/IsoGenerator.Core/DimensionRouting.cs#L250) `var envelope=modelPoints.Length==0?new State(new(),0):`
- [Строка 251](src/IsoGenerator.Core/DimensionRouting.cs#L251) `new State(new(),0,modelPoints.Min(point=>point.X),modelPoints.Min(point=>point.Y),`
- [Строка 259](src/IsoGenerator.Core/DimensionRouting.cs#L259) `const double tolerance=.81;`
- [Строка 261](src/IsoGenerator.Core/DimensionRouting.cs#L261) `x.Top+tolerance<y.Bottom||y.Top+tolerance<x.Bottom)return 0;`
- [Строка 275](src/IsoGenerator.Core/DimensionRouting.cs#L275) `foreach(var state in beam)foreach(var choice in options[group[0].ChainId])`
- [Строка 277](src/IsoGenerator.Core/DimensionRouting.cs#L277) `int crossings=0;`
- [Строка 282](src/IsoGenerator.Core/DimensionRouting.cs#L282) `double overflow=Math.Max(0,width-SheetComposition.Paper.Width)+Math.Max(0,height-SheetComposition.Paper.Height);`
- [Строка 283](src/IsoGenerator.Core/DimensionRouting.cs#L283) `expanded.Add((state,choice,state.Cost+choice.Cost+crossings*1000000+overflow*10000,left,bottom,right,top));`
- [Строка 287](src/IsoGenerator.Core/DimensionRouting.cs#L287) `if(best is null||beam[0].Cost<best.Cost)best=beam[0];`
- [Строка 291](src/IsoGenerator.Core/DimensionRouting.cs#L291) `if(best is not null && best.Choices.Sum(c => best.Choices.Sum(c2 => c == c2 ? 0 : CachedConflicts(c, c2))) > 0)`
- [Строка 294](src/IsoGenerator.Core/DimensionRouting.cs#L294) `var cList = options[g[0].ChainId].Where(c => c.Cost < 1000000).ToArray();`
- [Строка 295](src/IsoGenerator.Core/DimensionRouting.cs#L295) `return cList.Length > 0 ? cList : options[g[0].ChainId].Take(6).ToArray();`
- [Строка 298](src/IsoGenerator.Core/DimensionRouting.cs#L298) `var chainOrder = Enumerable.Range(0, groups.Length).OrderBy(i => cleanOptions[i].Length).ToArray();`
- [Строка 302](src/IsoGenerator.Core/DimensionRouting.cs#L302) `int backtracks = 0;`
- [Строка 317](src/IsoGenerator.Core/DimensionRouting.cs#L317) `for (int prev = 0; prev < stepIndex; prev++)`
- [Строка 319](src/IsoGenerator.Core/DimensionRouting.cs#L319) `if (CachedConflicts(currentAssignment[chainOrder[prev]]!, choice) > 0)`
- [Строка 331](src/IsoGenerator.Core/DimensionRouting.cs#L331) `Search(stepIndex + 1, currentCost + choice.Cost,next);`
- [Строка 337](src/IsoGenerator.Core/DimensionRouting.cs#L337) `Search(0, 0,envelope);`

### DimensionRowAlignment.cs

- [Строка 10](src/IsoGenerator.Core/DimensionRowAlignment.cs#L10) `var parent=Enumerable.Range(0,representatives.Length).ToArray();`
- [Строка 12](src/IsoGenerator.Core/DimensionRowAlignment.cs#L12) `for(int i=0;i<representatives.Length;i++)for(int j=i+1;j<representatives.Length;j++)`
- [Строка 15](src/IsoGenerator.Core/DimensionRowAlignment.cs#L15) `if(l<.1||m<.1||Math.Abs((dx*ex+dy*ey)/(l*m))<Math.Cos(Math.PI/360))continue;`
- [Строка 19](src/IsoGenerator.Core/DimensionRowAlignment.cs#L19) `if(ea.X*eb.X+ea.Y*eb.Y<.5)continue;`
- [Строка 23](src/IsoGenerator.Core/DimensionRowAlignment.cs#L23) `if(Math.Min(Math.Abs(Offset(b.Start)),Math.Abs(Offset(b.End)))>3)continue;`
- [Строка 25](src/IsoGenerator.Core/DimensionRowAlignment.cs#L25) `if(Math.Max(t1,t2)<-20||Math.Min(t1,t2)>l+20)continue;`
- [Строка 33](src/IsoGenerator.Core/DimensionRowAlignment.cs#L33) `foreach(var group in Enumerable.Range(0,result.Length).GroupBy(i=>result[i].ChainId))`
- [Строка 35](src/IsoGenerator.Core/DimensionRowAlignment.cs#L35) `var chain=group.Where(i=>result[i].Role=="chain").ToArray();if(chain.Length==0)continue;`
- [Строка 38](src/IsoGenerator.Core/DimensionRowAlignment.cs#L38) `var overall=result[index];double dx=overall.End.X-overall.Start.X,dy=overall.End.Y-overall.Start.Y,length=Math.Sqrt(dx*dx+dy*dy);if(length<1e-8)continue;`
- [Строка 41](src/IsoGenerator.Core/DimensionRowAlignment.cs#L41) `double side=Math.Sign(Offset(result[chain[0]]));if(side==0)continue;`
- [Строка 43](src/IsoGenerator.Core/DimensionRowAlignment.cs#L43) `var ex=overall.ExtensionDirection!.Value;double dot=ex.X*n.X+ex.Y*n.Y;if(Math.Abs(dot)<1e-8)continue;`
- [Строка 51](src/IsoGenerator.Core/DimensionRowAlignment.cs#L51) `var groups=input.GroupBy(d=>d.ChainId).ToArray();var parent=Enumerable.Range(0,groups.Length).ToArray();`
- [Строка 53](src/IsoGenerator.Core/DimensionRowAlignment.cs#L53) `bool Same(ModelPoint? a,ModelPoint? b,SheetPoint p,SheetPoint q)=>a is {} x&&b is {} y&&x.DistanceTo(y)<.001&&Math.Sqrt(Math.Pow(p.X-q.X,2)+Math.Pow(p.Y-q.Y,2))<.01;`
- [Строка 54](src/IsoGenerator.Core/DimensionRowAlignment.cs#L54) `for(int i=0;i<groups.Length;i++)for(int j=i+1;j<groups.Length;j++)`
- [Строка 57](src/IsoGenerator.Core/DimensionRowAlignment.cs#L57) `if(a.ExtensionDirection is not {} ea||b.ExtensionDirection is not {} eb||ea.X*eb.X+ea.Y*eb.Y<.999)continue;`
- [Строка 62](src/IsoGenerator.Core/DimensionRowAlignment.cs#L62) `if(Distance(aChain)*Distance(bChain)<=0)continue;`
- [Строка 71](src/IsoGenerator.Core/DimensionRowAlignment.cs#L71) `foreach(var family in groups.Select((g,i)=>(Group:g,Family:Find(i))).GroupBy(g=>g.Family).Where(g=>g.Count()>1))`
- [Строка 77](src/IsoGenerator.Core/DimensionRowAlignment.cs#L77) `for(int i=0;i<result.Length;i++)`
- [Строка 96](src/IsoGenerator.Core/DimensionRowAlignment.cs#L96) `foreach(var group in Enumerable.Range(0,input.Count).GroupBy(i=>families[input[i].ChainId]))`
- [Строка 98](src/IsoGenerator.Core/DimensionRowAlignment.cs#L98) `var indices=group.ToArray();if(indices.Select(i=>input[i].ChainId).Distinct().Count()<2)continue;`
- [Строка 99](src/IsoGenerator.Core/DimensionRowAlignment.cs#L99) `var reference=indices.Select(i=>input[i]).OrderByDescending(d=>Math.Pow(d.End.X-d.Start.X,2)+Math.Pow(d.End.Y-d.Start.Y,2)).First();`
- [Строка 103](src/IsoGenerator.Core/DimensionRowAlignment.cs#L103) `if(normal.X*refEx.X+normal.Y*refEx.Y<0)normal=new(-normal.X,-normal.Y);`
- [Строка 107](src/IsoGenerator.Core/DimensionRowAlignment.cs#L107) `double outer=Math.Max(inner+(hasChain?IsoAdvancedSettings.Current.DimensionRowStep:0),indices.Where(i=>input[i].Role=="overall").Select(i=>Offset(input[i])).DefaultIfEmpty(inner).Max());`
- [Строка 111](src/IsoGenerator.Core/DimensionRowAlignment.cs#L111) `var ex=d.ExtensionDirection!.Value;double dot=ex.X*normal.X+ex.Y*normal.Y;if(Math.Abs(dot)<1e-8)continue;`

### DimensionTextPlacement.cs

- [Строка 12](src/IsoGenerator.Core/DimensionTextPlacement.cs#L12) `double low=0,high=1,dx=bx-ax,dy=by-ay;`
- [Строка 13](src/IsoGenerator.Core/DimensionTextPlacement.cs#L13) `bool Clip(double p,double q){if(Math.Abs(p)<1e-10)return q>=0;double t=q/p;if(p<0)low=Math.Max(low,t);else high=Math.Min(high,t);return low<=high;}`
- [Строка 23](src/IsoGenerator.Core/DimensionTextPlacement.cs#L23) `if(separation>=ra+rb+.6)return false;`
- [Строка 31](src/IsoGenerator.Core/DimensionTextPlacement.cs#L31) `double ratio=IsoAdvancedSettings.Current.DimensionTextHeight/3.5;`
- [Строка 32](src/IsoGenerator.Core/DimensionTextPlacement.cs#L32) `return new(d.TextPosition!.Value,new(dx/len,dy/len),d.Label.Length*1.05*ratio+.2,1.77*ratio);`
- [Строка 36](src/IsoGenerator.Core/DimensionTextPlacement.cs#L36) `.SelectMany(stroke=>stroke.Points.Zip(stroke.Points.Skip(1),(a,b)=>(a,b))).ToArray();`
- [Строка 44](src/IsoGenerator.Core/DimensionTextPlacement.cs#L44) `for(int attempt=0;attempt<=40;attempt++)`
- [Строка 49](src/IsoGenerator.Core/DimensionTextPlacement.cs#L49) `if(blocked.Length==0)return placed;`
- [Строка 50](src/IsoGenerator.Core/DimensionTextPlacement.cs#L50) `if(attempt==40)break;`
- [Строка 55](src/IsoGenerator.Core/DimensionTextPlacement.cs#L55) `if(attempt==1)`
- [Строка 60](src/IsoGenerator.Core/DimensionTextPlacement.cs#L60) `for(int i=0;i<trial.Length;i++)if(trial[i].ChainId==chain)`
- [Строка 65](src/IsoGenerator.Core/DimensionTextPlacement.cs#L65) `trial[i]=d with{DimensionLinePoint=new(p.X-2*offset*n.X,p.Y-2*offset*n.Y)};`
- [Строка 71](src/IsoGenerator.Core/DimensionTextPlacement.cs#L71) `placed=PlaceText(rows,strokes,out blocked);if(blocked.Length==0)return placed;`
- [Строка 73](src/IsoGenerator.Core/DimensionTextPlacement.cs#L73) `for(int i=0;i<placed.Count;i++)for(int j=i+1;j<placed.Count;j++)`
- [Строка 79](src/IsoGenerator.Core/DimensionTextPlacement.cs#L79) `var offending=blocked.Select(i=>rows[i]).OrderBy(d=>d.Role=="overall"?0:1).First();`
- [Строка 84](src/IsoGenerator.Core/DimensionTextPlacement.cs#L84) `double step=(side==0?1:side)/(ex.X*normal.X+ex.Y*normal.Y);`
- [Строка 87](src/IsoGenerator.Core/DimensionTextPlacement.cs#L87) `for(int i=0;i<rows.Length;i++)if(rows[i].ChainId==offending.ChainId&&(offending.Role=="chain"||rows[i].Role==offending.Role))`
- [Строка 93](src/IsoGenerator.Core/DimensionTextPlacement.cs#L93) `throw new SheetCapacityException("Не удалось разместить размерные подписи без пересечений: "+string.Join(", ",blocked.Select(i=>rows[i].ChainId+":"+rows[i].Label)),blocked.Length>0?rows[blocked[0]].SourceId:null,blocked.Le…`
- [Строка 112](src/IsoGenerator.Core/DimensionTextPlacement.cs#L112) `foreach(var (tip,sign) in new[]{(a,1.0),(b,-1.0)})`
- [Строка 113](src/IsoGenerator.Core/DimensionTextPlacement.cs#L113) `foreach(double side in new[]{-.7,0,.7})`
- [Строка 114](src/IsoGenerator.Core/DimensionTextPlacement.cs#L114) `segments.Add((new(tip.X+nx*side,tip.Y+ny*side),new(tip.X+along.X*sign*(IsoAdvancedSettings.Current.ArrowSize+1)+nx*side,tip.Y+along.Y*sign*(IsoAdvancedSettings.Current.ArrowSize+1)+ny*side)));`
- [Строка 118](src/IsoGenerator.Core/DimensionTextPlacement.cs#L118) `for(int i=0;!measureOnly&&i<result.Length;i++)`
- [Строка 121](src/IsoGenerator.Core/DimensionTextPlacement.cs#L121) `var n=new SheetPoint(-dy/l,dx/l);if(dx<-.0001||(Math.Abs(dx)<.0001&&dy<0))n=new(-n.X,-n.Y);`
- [Строка 122](src/IsoGenerator.Core/DimensionTextPlacement.cs#L122) `var ex=d.ExtensionDirection!.Value;var p=d.DimensionLinePoint!.Value;var mid=new SheetPoint((d.Start.X+d.End.X)/2,(d.Start.Y+d.End.Y)/2);`
- [Строка 129](src/IsoGenerator.Core/DimensionTextPlacement.cs#L129) `var conflicts=Enumerable.Range(0,result.Length).Where(i=>segments.Any(s=>boxes[i].Hits(s.A,s.B))).ToHashSet();`
- [Строка 130](src/IsoGenerator.Core/DimensionTextPlacement.cs#L130) `for(int i=0;i<result.Length;i++)`
- [Строка 131](src/IsoGenerator.Core/DimensionTextPlacement.cs#L131) `for(int j=i+1;j<result.Length;j++)`
- [Строка 136](src/IsoGenerator.Core/DimensionTextPlacement.cs#L136) `if(!measureOnly&&bad.Count>0)`
- [Строка 142](src/IsoGenerator.Core/DimensionTextPlacement.cs#L142) `foreach(double slide in new[]{2.0,-2.0,3.5,-3.5,5.0,-5.0,7.0,-7.0})`
- [Строка 148](src/IsoGenerator.Core/DimensionTextPlacement.cs#L148) `for(int j=0;j<result.Length;j++)if(i!=j&&box.Overlaps(boxes[j])){overlap=true;break;}`
- [Строка 155](src/IsoGenerator.Core/DimensionTextPlacement.cs#L155) `if(Environment.GetEnvironmentVariable("ISO_LAYOUT_TRACE")=="1"&&blocked.Length>0)`
- [Строка 163](src/IsoGenerator.Core/DimensionTextPlacement.cs#L163) `for(int bj=0;bj<result.Length;bj++)`

### DrawingFileNames.cs

- [Строка 8](src/IsoGenerator.Core/DrawingFileNames.cs#L8) `string stem=new(line.Select(c=>c<32||forbidden.Contains(c)?'_':c).ToArray());`
- [Строка 11](src/IsoGenerator.Core/DrawingFileNames.cs#L11) `string first=stem.Split('.')[0].ToUpperInvariant();`
- [Строка 12](src/IsoGenerator.Core/DrawingFileNames.cs#L12) `if(first is "CON" or "PRN" or "AUX" or "NUL"||System.Text.RegularExpressions.Regex.IsMatch(first,"^(COM|LPT)[1-9]$"))stem="_"+stem;`
- [Строка 17](src/IsoGenerator.Core/DrawingFileNames.cs#L17) `if(number<1||total<1||number>total)throw new ArgumentOutOfRangeException(nameof(number));`
- [Строка 18](src/IsoGenerator.Core/DrawingFileNames.cs#L18) `return Path.Combine(folder,LineStem(line)+(total>1?$"(L{number})":"")+".dwg");`
- [Строка 20](src/IsoGenerator.Core/DrawingFileNames.cs#L20) `public static (string[] Drawings,string Pdf) OutputSet(string folder,string line,int total,int index=0)`
- [Строка 22](src/IsoGenerator.Core/DrawingFileNames.cs#L22) `if(total<1||index<0)throw new ArgumentOutOfRangeException(nameof(total));`
- [Строка 23](src/IsoGenerator.Core/DrawingFileNames.cs#L23) `string stem=LineStem(line)+(index>0?$" ({index})":"");`
- [Строка 24](src/IsoGenerator.Core/DrawingFileNames.cs#L24) `return (Enumerable.Range(1,total).Select(number=>Dwg(folder,stem,number,total)).ToArray(),Path.Combine(folder,stem+".pdf"));`
- [Строка 30](src/IsoGenerator.Core/DrawingFileNames.cs#L30) `string pattern="^"+System.Text.RegularExpressions.Regex.Escape(stem)+@"(?:\(L[1-9]\d*\))?\.dwg$";`
- [Строка 36](src/IsoGenerator.Core/DrawingFileNames.cs#L36) `for(int index=1;;index++)`

### ElevationNotation.cs

- [Строка 9](src/IsoGenerator.Core/ElevationNotation.cs#L9) `foreach(var edge in graph.Edges)foreach(int root in new[]{graph.Sets.Find(edge.A),graph.Sets.Find(edge.B)})degree[root]=degree.GetValueOrDefault(root)+1;`
- [Строка 14](src/IsoGenerator.Core/ElevationNotation.cs#L14) `var candidates=graph.Nodes.Select((n,i)=>(Node:n,Root:graph.Sets.Find(i))).Where(x=>x.Node.Tag is "Axis" or "TapAxis"||degree.GetValueOrDefault(x.Root)==1)`
- [Строка 16](src/IsoGenerator.Core/ElevationNotation.cs#L16) `.OrderBy(x=>degree.GetValueOrDefault(x.Root)==1?0:x.Node.Tag=="Axis"?1:2).ThenBy(x=>x.Node.Source,StringComparer.Ordinal);`
- [Строка 17](src/IsoGenerator.Core/ElevationNotation.cs#L17) `foreach(var level in candidates.GroupBy(x=>Math.Round(x.Node.Point.Z,0)))`
- [Строка 21](src/IsoGenerator.Core/ElevationNotation.cs#L21) `double meters=node.Point.Z/1000;`
- [Строка 22](src/IsoGenerator.Core/ElevationNotation.cs#L22) `string text="ОТМ. "+meters.ToString("+0.000;-0.000;0.000",CultureInfo.InvariantCulture)+"*";`
- [Строка 23](src/IsoGenerator.Core/ElevationNotation.cs#L23) `var alternatives=snapshot.Parts.SelectMany(p=>p.Ports.Select(port=>(Part:p,Port:port))).Where(p=>Math.Abs(p.Port.Position.Z-node.Point.Z)<.001).Select(p=>At(p.Port.Position,p.Part.Handle)).ToList();`
- [Строка 27](src/IsoGenerator.Core/ElevationNotation.cs#L27) `if(ends.Length!=2||ends.Any(p=>Math.Abs(p.Position.Z-node.Point.Z)>.001))continue;`
- [Строка 28](src/IsoGenerator.Core/ElevationNotation.cs#L28) `var a=At(ends[0].Position,part.Handle);var b=At(ends[1].Position,part.Handle);`
- [Строка 29](src/IsoGenerator.Core/ElevationNotation.cs#L29) `foreach(double t in new[]{.2,.5,.8})alternatives.Add(new(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t));`

### ExternalLineContextPlanner.cs

- [Строка 14](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L14) `if(Math.Abs(den)<1e-10)return null;`
- [Строка 33](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L33) `var ends=pipe.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();if(ends.Length!=2)continue;`
- [Строка 34](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L34) `var axis=Sub(ends[1].Position,ends[0].Position);double l2=Dot(axis,axis);if(l2<1e-9)continue;`
- [Строка 35](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L35) `double t=Math.Clamp(Dot(Sub(port.Position,ends[0].Position),axis)/l2,0,1);`
- [Строка 36](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L36) `boundaryPoint=new(ends[0].Position.X+t*axis.X,ends[0].Position.Y+t*axis.Y,ends[0].Position.Z+t*axis.Z);`
- [Строка 39](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L39) `string elevation=(boundaryPoint.Z/1000).ToString("+0.000;-0.000;0.000",CultureInfo.InvariantCulture);`
- [Строка 43](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L43) `var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int physical=0;`
- [Строка 44](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L44) `while(pending.Count>0&&physical<2)`
- [Строка 57](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L57) `double scale=map?.Catalog?.ScaleFor(part)??10;`
- [Строка 59](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L59) `{var v=IsometricProjection.Project(vector,quarterTurns:turns);double l=Math.Sqrt(v.X*v.X+v.Y*v.Y);return l<1e-8?from:new(from.X+v.X/l*length,from.Y+v.Y/l*length);}`
- [Строка 60](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L60) `if(kind=="Elbow"&&ends.Length==2&&Corner(ends[0],ends[1]) is {} corner)`
- [Строка 62](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L62) `int i=Array.FindIndex(ends,p=>p.Name==entry.Port);if(i<0)i=0;int j=1-i;`
- [Строка 63](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L63) `double leg=library is null?5:Math.Max(.1,Math.Max(library.Maximum.X-library.Minimum.X,library.Maximum.Y-library.Minimum.Y)*scale/2);`
- [Строка 66](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L66) `if(library is not null)symbols.Add(IsoSymbolPlacement.Corner(part,block!,map!.Catalog!,located[ends[0].Name],atCorner,located[ends[1].Name]) with{Context=true});`
- [Строка 71](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L71) `double length=12;`
- [Строка 73](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L73) `{var a=library.Ports.GetValueOrDefault("Port1",library.Minimum);var b=library.Ports.GetValueOrDefault("Port2",library.Maximum);length=Math.Max(.1,Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Y-a.Y,2))*scale);}`
- [Строка 74](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L74) `bool drawInline=kind!="Pipe"&&library is not null&&ends.Length==2&&ends.Any(port=>port.Name==entry.Port);`
- [Строка 82](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L82) `symbols.Add(IsoSymbolPlacement.Inline(part,block!,map!.Catalog,located[ends[0].Name],located[ends[1].Name],IsoSymbolPlacement.Transverse(Sub(ends[1].Position,ends[0].Position),turns)) with{Context=true});`
- [Строка 99](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L99) `if(length>1e-8)`
- [Строка 103](src/IsoGenerator.Core/ExternalLineContextPlanner.cs#L103) `strokes.Add(new(context.OwnerHandle,"continuation-break",new[]{At(-4,0),At(-1.5,0),At(-.7,1.5),At(.7,-1.5),At(1.5,0),At(4,0)}));`

### FlangedJointPolicy.cs

- [Строка 43](src/IsoGenerator.Core/FlangedJointPolicy.cs#L43) `if(ends.Length!=2)continue;`
- [Строка 45](src/IsoGenerator.Core/FlangedJointPolicy.cs#L45) `var ordered=ends.OrderBy(e=>parts[e.Handle].Properties.GetValueOrDefault("PnPClassName")=="Flange"?0:1).ThenBy(e=>e.Handle,StringComparer.Ordinal).ToArray();`
- [Строка 46](src/IsoGenerator.Core/FlangedJointPolicy.cs#L46) `result.Add(new(joint.Handle,ordered[0].Handle,ordered[0].Port,ordered[1].Handle,ordered[1].Port));`

### IncrementalSheetPlanner.cs

- [Строка 5](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L5) `public sealed record RememberedLayout(PipelineSnapshot Source,IReadOnlyList<PlannedSheet> Sheets,double MinimumPipeLength=10,string? AdvancedSettingsSignature=null);`
- [Строка 21](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L21) `if(Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1")`
- [Строка 42](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L42) `if(old.Length==0||old.Any(sheet=>sheet.BaseScene is null))return null;`
- [Строка 56](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L56) `while(pending.Count>0)`
- [Строка 61](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L61) `for(int i=0;i<group.Count;i++)`
- [Строка 67](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L67) `if(neighbors.Count!=1)return null;`
- [Строка 79](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L79) `var starts=(source.IsoMarkers??Array.Empty<ModelIsoMarker>()).Where(marker=>marker.InformationType==5).ToArray();`
- [Строка 80](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L80) `if(starts.Length>1||starts.Length==1&&(!owner.TryGetValue(starts[0].HostHandle,out int start)||start!=0))return null;`
- [Строка 84](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L84) `if(pages>1)`
- [Строка 90](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L90) `var leaves=Enumerable.Range(0,old.Length).Select(index=>subset(owner.Where(item=>item.Value==index)`
- [Строка 92](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L92) `if(leaves.Any(leaf=>leaf.Parts.Count==0||SheetConnectivity.Components(leaf).Count!=1))return null;`
- [Строка 101](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L101) `for(int i=0;i<oldCuts.Length;i++)`
- [Строка 104](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L104) `string mark="П"+(i+1);`
- [Строка 105](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L105) `marks[a].Add(new(cut.FirstHandle,cut.FirstPort,mark,b+1));`
- [Строка 106](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L106) `marks[b].Add(new(cut.SecondHandle,cut.SecondPort,mark,a+1));`
- [Строка 118](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L118) `for(int i=0;i<leaves.Length;i++)`
- [Строка 123](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L123) `bool neighborChanged=marks[i].Any(mark=>changed[mark.OtherSheet-1]);`
- [Строка 134](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L134) `if(Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1")`
- [Строка 135](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L135) `Console.Error.WriteLine($"Обновление листа {i+1}: модель={changed[i]}, сосед={neighborChanged}.");`
- [Строка 142](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L142) `var scene=SheetPlanner.Build(leaf,turns,map,marks[i],i+1,leaves.Length,trialContext.Strokes,trialContext.Symbols,`
- [Строка 150](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L150) `if(weld.JointType=="Tap"&&owner.GetValueOrDefault(tap,-1)==i&&weld.ModelPosition is {} axis)`
- [Строка 162](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L162) `result.Add(new(i+1,leaves.Length,leaf,updated,raw[i]));`
- [Строка 176](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L176) `var numbers=geometry.Select((page,index)=>(page.Number,Diagram:index+1))`
- [Строка 180](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L180) `int next=index+1<geometry.Length?geometry[index+1].Number:pages.Count+1;`
- [Строка 185](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L185) `match=>" / лист "+numbers[int.Parse(match.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture)])}`
- [Строка 187](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L187) `return page with{Number=index+1,Total=geometry.Length,`
- [Строка 195](src/IsoGenerator.Core/IncrementalSheetPlanner.cs#L195) `static SheetPoint Round(SheetPoint point)=>new(Math.Round(point.X,6)+0,Math.Round(point.Y,6)+0);`

### InstallationUnits.cs

- [Строка 15](src/IsoGenerator.Core/InstallationUnits.cs#L15) `for(int i=0;i<links.Length;i++){adjacency[links[i].FirstHandle].Add(i);adjacency[links[i].SecondHandle].Add(i);}`
- [Строка 17](src/IsoGenerator.Core/InstallationUnits.cs#L17) `var stack=new Stack<int>();var result=new List<InstallationUnit>();int clock=0;`
- [Строка 21](src/IsoGenerator.Core/InstallationUnits.cs#L21) `var vertices=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int count=0,edge;`
- [Строка 25](src/IsoGenerator.Core/InstallationUnits.cs#L25) `if(vertices.Count<3||count<vertices.Count)return;`
- [Строка 27](src/IsoGenerator.Core/InstallationUnits.cs#L27) `if(vertices.Count(h=>adjacency[h].Select(i=>Other(i,h)).Distinct(StringComparer.OrdinalIgnoreCase).Count()>=3)<2)return;`
- [Строка 44](src/IsoGenerator.Core/InstallationUnits.cs#L44) `foreach(string h in parts.Keys.OrderBy(h=>h,StringComparer.OrdinalIgnoreCase))if(!discovered.ContainsKey(h))Visit(h,-1);`

### InsulationJunctions.cs

- [Строка 16](src/IsoGenerator.Core/InsulationJunctions.cs#L16) `while(queue.Count>0)`
- [Строка 33](src/IsoGenerator.Core/InsulationJunctions.cs#L33) `void AddSegment(SheetPoint a,SheetPoint b){if(pieces.Count>0&&Math.Abs(pieces[^1][^1].X-a.X)<1e-8&&Math.Abs(pieces[^1][^1].Y-a.Y)<1e-8)pieces[^1].Add(b);else pieces.Add(new(){a,b});}`
- [Строка 35](src/IsoGenerator.Core/InsulationJunctions.cs#L35) `foreach(var (a,b) in stroke.Points.Zip(stroke.Points.Skip(1)))`
- [Строка 40](src/IsoGenerator.Core/InsulationJunctions.cs#L40) `double dx=cover.B.X-cover.A.X,dy=cover.B.Y-cover.A.Y,l=Math.Sqrt(dx*dx+dy*dy);if(l<1e-8)continue;`
- [Строка 44](src/IsoGenerator.Core/InsulationJunctions.cs#L44) `double low=0,high=1;`
- [Строка 45](src/IsoGenerator.Core/InsulationJunctions.cs#L45) `bool Clip(double p,double q){if(Math.Abs(p)<1e-10)return q>=0;double t=q/p;if(p<0)low=Math.Max(low,t);else high=Math.Min(high,t);return low<=high;}`
- [Строка 46](src/IsoGenerator.Core/InsulationJunctions.cs#L46) `if(Clip(-vx,x)&&Clip(vx,l-x)&&Clip(-vy,y+offset-.001)&&Clip(vy,offset-.001-y)&&high-low>1e-8)cuts.Add((low,high));`
- [Строка 48](src/IsoGenerator.Core/InsulationJunctions.cs#L48) `double cursor=0;SheetPoint At(double t)=>new(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);`
- [Строка 49](src/IsoGenerator.Core/InsulationJunctions.cs#L49) `foreach(var cut in cuts.OrderBy(c=>c.Low)){if(cut.Low>cursor+1e-8)AddSegment(At(cursor),At(cut.Low));cursor=Math.Max(cursor,cut.High);}`
- [Строка 50](src/IsoGenerator.Core/InsulationJunctions.cs#L50) `if(cursor<1-1e-8)AddSegment(At(cursor),b);`

### InsulationPlanner.cs

- [Строка 10](src/IsoGenerator.Core/InsulationPlanner.cs#L10) `double inherited=snapshot.Parts.Select(Thickness).Where(n=>n>0).Select(n=>n!.Value).DefaultIfEmpty(0).Max();`
- [Строка 16](src/IsoGenerator.Core/InsulationPlanner.cs#L16) `if(kind is not ("Pipe" or "Elbow" or "Tee")||(Thickness(part)??inherited)<=0)continue;`
- [Строка 17](src/IsoGenerator.Core/InsulationPlanner.cs#L17) `var ends=part.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();if(ends.Length<2)continue;`
- [Строка 18](src/IsoGenerator.Core/InsulationPlanner.cs#L18) `var bendGlyph=kind=="Elbow"?symbols.FirstOrDefault(g=>g.SourceId==part.Handle&&g.Variant.Length==0):null;`
- [Строка 20](src/IsoGenerator.Core/InsulationPlanner.cs#L20) `if(kind=="Pipe")paths.Add(new[]{At(ends[0].Position),At(ends[1].Position)});`
- [Строка 25](src/IsoGenerator.Core/InsulationPlanner.cs#L25) `var a=ends[0];var b=ends[1];`
- [Строка 29](src/IsoGenerator.Core/InsulationPlanner.cs#L29) `if(Math.Abs(den)<1e-10)throw new InvalidDataException($"{part.Handle}: не определён угол отвода для изоляции");`
- [Строка 36](src/IsoGenerator.Core/InsulationPlanner.cs#L36) `var pair=(from i in Enumerable.Range(0,ends.Length) from j in Enumerable.Range(i+1,ends.Length-i-1)`
- [Строка 37](src/IsoGenerator.Core/InsulationPlanner.cs#L37) `where Dot(ends[i].Direction,ends[j].Direction)<-.999 select(i,j)).FirstOrDefault((-1,-1));`
- [Строка 38](src/IsoGenerator.Core/InsulationPlanner.cs#L38) `if(pair.Item1<0)throw new InvalidDataException($"{part.Handle}: не определена ось тройника для изоляции");`
- [Строка 50](src/IsoGenerator.Core/InsulationPlanner.cs#L50) `foreach(var path in paths)axes.AddRange(path.Zip(path.Skip(1),(a,b)=>(part.Handle,a,b)));`
- [Строка 51](src/IsoGenerator.Core/InsulationPlanner.cs#L51) `foreach(var path in paths)foreach(double side in new[]{-1.0,1.0})`
- [Строка 54](src/IsoGenerator.Core/InsulationPlanner.cs#L54) `for(int i=0;i<path.Length;i++)`
- [Строка 56](src/IsoGenerator.Core/InsulationPlanner.cs#L56) `var before=path[Math.Max(0,i-1)];var at=path[i];var after=path[Math.Min(path.Length-1,i+1)];`
- [Строка 57](src/IsoGenerator.Core/InsulationPlanner.cs#L57) `var a=i==0?at:before;var b=i==0?after:at;double dx=b.X-a.X,dy=b.Y-a.Y,l=Math.Sqrt(dx*dx+dy*dy);if(l<1e-6)continue;`
- [Строка 59](src/IsoGenerator.Core/InsulationPlanner.cs#L59) `if(i>0&&i<path.Length-1)`
- [Строка 62](src/IsoGenerator.Core/InsulationPlanner.cs#L62) `if(el>1e-6&&Math.Abs(den)>1e-8)`
- [Строка 70](src/IsoGenerator.Core/InsulationPlanner.cs#L70) `if(result.Count==3)`
- [Строка 72](src/IsoGenerator.Core/InsulationPlanner.cs#L72) `var a=result[0];var c=result[1];var b=result[2];double l1=Math.Sqrt(Math.Pow(a.X-c.X,2)+Math.Pow(a.Y-c.Y,2)),l2=Math.Sqrt(Math.Pow(b.X-c.X,2)+Math.Pow(b.Y-c.Y,2));`
- [Строка 73](src/IsoGenerator.Core/InsulationPlanner.cs#L73) `var incoming=new SheetPoint(path[1].X-path[0].X,path[1].Y-path[0].Y);var outgoing=new SheetPoint(path[2].X-path[1].X,path[2].Y-path[1].Y);`
- [Строка 75](src/IsoGenerator.Core/InsulationPlanner.cs#L75) `double dot=Math.Clamp((incoming.X*outgoing.X+incoming.Y*outgoing.Y)/(il*ol),-1,1),turn=Math.Acos(dot);`
- [Строка 77](src/IsoGenerator.Core/InsulationPlanner.cs#L77) `double tangent=Math.Min(il,ol)*.25;`
- [Строка 79](src/IsoGenerator.Core/InsulationPlanner.cs#L79) `var curve=bendGlyph?.LocalContours?.Where(points=>points.Count>2).OrderByDescending(points=>points.Count).FirstOrDefault();`
- [Строка 83](src/IsoGenerator.Core/InsulationPlanner.cs#L83) `double Distance(SheetPoint p)=>Math.Sqrt(Math.Pow(p.X-path[1].X,2)+Math.Pow(p.Y-path[1].Y,2));`
- [Строка 84](src/IsoGenerator.Core/InsulationPlanner.cs#L84) `tangent=(Distance(AtCurve(curve[0]))+Distance(AtCurve(curve[^1])))/2;`
- [Строка 88](src/IsoGenerator.Core/InsulationPlanner.cs#L88) `double available=Math.Min(l1,l2)*.8;`
- [Строка 89](src/IsoGenerator.Core/InsulationPlanner.cs#L89) `double minimumTrim=Math.Min(available,1.25*Math.Tan(turn/2));`
- [Строка 90](src/IsoGenerator.Core/InsulationPlanner.cs#L90) `double trim=Math.Clamp(tangent-side*sign*Offset*Math.Tan(turn/2),minimumTrim,available);`
- [Строка 92](src/IsoGenerator.Core/InsulationPlanner.cs#L92) `double weight=Math.Cos(turn/2);`
- [Строка 94](src/IsoGenerator.Core/InsulationPlanner.cs#L94) `for(int i=1;i<32;i++)`
- [Строка 96](src/IsoGenerator.Core/InsulationPlanner.cs#L96) `double t=i/32.0,s=1-t,den=s*s+2*weight*s*t+t*t;`
- [Строка 97](src/IsoGenerator.Core/InsulationPlanner.cs#L97) `result.Add(new((s*s*u.X+2*weight*s*t*c.X+t*t*v.X)/den,(s*s*u.Y+2*weight*s*t*c.Y+t*t*v.Y)/den));`
- [Строка 101](src/IsoGenerator.Core/InsulationPlanner.cs#L101) `if(result.Count>1)strokes.Add(new(part.Handle,"insulation",result));`

### IsoBlockCatalog.cs

- [Строка 6](src/IsoGenerator.Core/IsoBlockCatalog.cs#L6) `public sealed record IsoBlockCatalog(IReadOnlyList<IsoBlockDefinition> Blocks,double SymbolScale=1,IsoStyleSettings? Style=null)`
- [Строка 9](src/IsoGenerator.Core/IsoBlockCatalog.cs#L9) `public double ScaleFor(Part part)=>double.IsFinite(SymbolScale)&&SymbolScale>0?SymbolScale/(BomCategories.Classify(part)==BomCategory.Supports?IsoAdvancedSettings.Current.SupportScaleDivisor:1):throw new InvalidDataExcepti…`

### IsoStyleSettings.cs

- [Строка 4](src/IsoGenerator.Core/IsoStyleSettings.cs#L4) `public sealed record IsoStyleSettings(double SymbolScale,double? SmallBoreDiameter=null,int? VentDrainMaxComponents=null,int? VentDrainMaxPipes=null,double PipeGapLength=6.0)`
- [Строка 13](src/IsoGenerator.Core/IsoStyleSettings.cs#L13) `if(!double.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out double scale)||!double.IsFinite(scale)||scale<=0)throw new InvalidDataException("В основном стиле IsoConfig.xml не указан допустимый масштаб Symbo…`
- [Строка 18](src/IsoGenerator.Core/IsoStyleSettings.cs#L18) `double gap=double.TryParse((string?)geometry?.Attribute("PipeGapLength"),NumberStyles.Float,CultureInfo.InvariantCulture,out double g)&&g>0?g:6.0;`

### IsoSymbolPlacement.cs

- [Строка 4](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L4) `static ModelPoint Unit(ModelPoint v){double l=Math.Sqrt(v.X*v.X+v.Y*v.Y+v.Z*v.Z);if(l<1e-9)throw new InvalidDataException("У компонента не определено направление порта.");return new(v.X/l,v.Y/l,v.Z/l);}`
- [Строка 7](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L7) `axis=Unit(axis);var seed=Math.Abs(axis.Z)<.95?new ModelPoint(0,0,1):new ModelPoint(1,0,0);`
- [Строка 17](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L17) `if(Math.Abs(Unit(axis).Z)<.99)return fallback;`
- [Строка 22](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L22) `while(frontier.Length>0)`
- [Строка 31](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L31) `if(kind=="Pipe"&&ends.Length==2&&entry.Port.Length>0)`
- [Строка 33](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L33) `var run=Unit(new(ends[1].Position.X-ends[0].Position.X,ends[1].Position.Y-ends[0].Position.Y,ends[1].Position.Z-ends[0].Position.Z));`
- [Строка 34](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L34) `if(Math.Abs(run.Z)<.95)`
- [Строка 39](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L39) `else if(inferred.Count==0&&PartDiameter.Read(part) is {} hostSize&&PartDiameter.Read(flange) is {} size&&size<hostSize)`
- [Строка 44](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L44) `if(kind=="Tee"&&ends.Length==3&&entry.Port.Length>0)`
- [Строка 48](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L48) `first.Direction.X*second.Direction.X+first.Direction.Y*second.Direction.Y+first.Direction.Z*second.Direction.Z<-.99`
- [Строка 60](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L60) `if(explicitHosts.Count>0)return ProjectHost(explicitHosts,out hostFound);`
- [Строка 63](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L63) `if(inferred.Count>0)return ProjectHost(inferred,out hostFound);`
- [Строка 67](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L67) `var first=candidates[0];`
- [Строка 68](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L68) `if(candidates.Any(v=>Math.Abs(v.X*first.X+v.Y*first.Y+v.Z*first.Z)<.99))return fallback;`
- [Строка 69](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L69) `if(first.X<-.001||(Math.Abs(first.X)<.001&&first.Y<0))first=new(-first.X,-first.Y,-first.Z);`
- [Строка 72](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L72) `found=length>1e-8;`
- [Строка 79](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L79) `var port1=definition.Ports.GetValueOrDefault("Port1",new(definition.Minimum.X,0));`
- [Строка 80](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L80) `var port2=definition.Ports.GetValueOrDefault("Port2",new(definition.Maximum.X,0));`
- [Строка 82](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L82) `double tx=to.X-from.X,ty=to.Y-from.Y,len=Math.Sqrt(tx*tx+ty*ty);if(len<1e-8)throw new InvalidDataException($"{part.Handle}: совпавшие порты компонента");`
- [Строка 83](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L83) `if(span<1e-8)`
- [Строка 86](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L86) `double width=Math.Max(.001,definition.Maximum.X-definition.Minimum.X),height=Math.Max(.001,definition.Maximum.Y-definition.Minimum.Y);`
- [Строка 88](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L88) `var mid=new SheetPoint((from.X+to.X)/2,(from.Y+to.Y)/2);`
- [Строка 102](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L102) `if(ports.Length<2)throw new InvalidDataException($"{block}: для углового символа нужны два параметра порта");`
- [Строка 103](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L103) `var p1=ports[0];var p2=ports[^1];var localCorner=new SheetPoint(p2.X,p1.Y);`
- [Строка 109](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L109) `var ports=definition.Ports.Where(p=>p.Key.StartsWith("Port",StringComparison.OrdinalIgnoreCase)).OrderBy(p=>int.Parse(p.Key[4..])).Take(targets.Count).Select(p=>p.Value).ToArray();`
- [Строка 110](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L110) `if(ports.Length<3)throw new InvalidDataException($"{block}: недостаточно параметров портов для разветвления");`
- [Строка 111](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L111) `var placed=Three(part,block,definition,ports.Take(3).ToArray(),targets.Take(3).ToArray());`
- [Строка 112](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L112) `for(int i=3;i<Math.Min(ports.Length,targets.Count);i++)`
- [Строка 115](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L115) `if(Math.Sqrt(Math.Pow(actual.X-targets[i].X,2)+Math.Pow(actual.Y-targets[i].Y,2))>.1)throw new InvalidDataException($"{part.Handle}: блок {block} не соответствует расположению портов компонента");`
- [Строка 121](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L121) `double ax=source[1].X-source[0].X,ay=source[1].Y-source[0].Y,bx=source[2].X-source[0].X,by=source[2].Y-source[0].Y,det=ax*by-ay*bx;`
- [Строка 122](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L122) `if(Math.Abs(det)<1e-9)throw new InvalidDataException($"{block}: параметры портов не задают плоскость символа");`
- [Строка 123](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L123) `var u=new SheetPoint(target[1].X-target[0].X,target[1].Y-target[0].Y);var v=new SheetPoint(target[2].X-target[0].X,target[2].Y-target[0].Y);`
- [Строка 125](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L125) `return new(part.Handle,part.Properties.GetValueOrDefault("PnPGuid",""),part.Properties.GetValueOrDefault("ContentIsoSymbolDefinition",""),block,new(target[0].X-x.X*source[0].X-y.X*source[0].Y,target[0].Y-x.Y*source[0].X-y.…`
- [Строка 136](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L136) `if(l<1e-8)throw new InvalidDataException($"{part.Handle}: не определено направление символа опоры");`
- [Строка 138](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L138) `double width=Math.Max(.001,definition.Maximum.X-definition.Minimum.X),height=Math.Max(.001,definition.Maximum.Y-definition.Minimum.Y);`
- [Строка 142](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L142) `if(IsoDefinition.Parse(part.Properties.GetValueOrDefault("ContentIsoSymbolDefinition","")).Skey=="DUCK"&&y.Y>0)y=new(-y.X,-y.Y);`
- [Строка 154](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L154) `var p1=definition.Ports.GetValueOrDefault("Port1",new(definition.Minimum.X,0));`
- [Строка 155](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L155) `var p2=definition.Ports.GetValueOrDefault("Port2",new(definition.Maximum.X,0));`
- [Строка 157](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L157) `double across=span>1e-8?definition.Corners.Max(p=>(-dy*p.X+dx*p.Y)/span)-definition.Corners.Min(p=>(-dy*p.X+dx*p.Y)/span):0;`
- [Строка 158](src/IsoGenerator.Core/IsoSymbolPlacement.cs#L158) `double thickness=(span>1e-8?span:definition.Maximum.X-definition.Minimum.X)*catalog!.ScaleFor(part);`

### IsoSymbols.cs

- [Строка 11](src/IsoGenerator.Core/IsoSymbols.cs#L11) `var pair=field.Split('=',2);if(pair.Length!=2)continue;`
- [Строка 12](src/IsoGenerator.Core/IsoSymbols.cs#L12) `string key=pair[0].Trim(),value=pair[1].Trim();`
- [Строка 31](src/IsoGenerator.Core/IsoSymbols.cs#L31) `.Where(e=>e.Item1.Length>0&&e.Item2.Length>0).ToArray();`
- [Строка 47](src/IsoGenerator.Core/IsoSymbols.cs#L47) `if(hits.Length==0)return Catalog?.Find(skey)?.Name;`
- [Строка 48](src/IsoGenerator.Core/IsoSymbols.cs#L48) `int specificity=hits[0].Pattern.Count(c=>c is '?' or '*');`
- [Строка 49](src/IsoGenerator.Core/IsoSymbols.cs#L49) `if(hits.Where(r=>r.Pattern.Count(c=>c is '?' or '*')==specificity).Select(r=>r.Block).Distinct().Count()>1)`

### MissingIsoSymbol.cs

- [Строка 8](src/IsoGenerator.Core/MissingIsoSymbol.cs#L8) `if(ports.Count==1&&BomCategories.Classify(part)!=BomCategory.Supports)`
- [Строка 13](src/IsoGenerator.Core/MissingIsoSymbol.cs#L13) `center=reach>1e-8?new(center.X+projected.X*5/reach,center.Y+projected.Y*5/reach):new(center.X,center.Y+5);`
- [Строка 17](src/IsoGenerator.Core/MissingIsoSymbol.cs#L17) `new SheetPoint[]{new(-5,-5),new(5,-5),new(5,5),new(-5,5),new(-5,-5)},`
- [Строка 18](src/IsoGenerator.Core/MissingIsoSymbol.cs#L18) `new SheetPoint[]{new(-5,-5),new(5,5)},new SheetPoint[]{new(-5,5),new(5,-5)}`
- [Строка 20](src/IsoGenerator.Core/MissingIsoSymbol.cs#L20) `return new(part.Handle,part.Properties.GetValueOrDefault("PnPGuid",""),part.Properties.GetValueOrDefault("ContentIsoSymbolDefinition",""),"SpecGenMissingIsoSymbol",center,new(1,0),new(0,1),"missing",LocalBounds:new SheetPo…`

### Model.cs

- [Строка 6](src/IsoGenerator.Core/Model.cs#L6) `public double DistanceTo(ModelPoint b) => Math.Sqrt(Math.Pow(X-b.X,2)+Math.Pow(Y-b.Y,2)+Math.Pow(Z-b.Z,2));`
- [Строка 22](src/IsoGenerator.Core/Model.cs#L22) `public static SheetPoint Project(ModelPoint point, ModelPoint origin = default, int quarterTurns = 0)`
- [Строка 25](src/IsoGenerator.Core/Model.cs#L25) `int turns = ((quarterTurns % 4) + 4) % 4;`
- [Строка 26](src/IsoGenerator.Core/Model.cs#L26) `for (int i=0;i<turns;i++) (x,y)=(-y,x);`
- [Строка 27](src/IsoGenerator.Core/Model.cs#L27) `return new(Math.Sqrt(3)/2*(x-y), .5*(x+y)+z);`
- [Строка 32](src/IsoGenerator.Core/Model.cs#L32) `public string Label => Math.Round(Millimeters, 0, MidpointRounding.AwayFromZero)`

### PipeCrossingBreaks.cs

- [Строка 11](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L11) `public const double DefaultGapLength = 6.0;`
- [Строка 22](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L22) `int quarterTurns = 0,`
- [Строка 26](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L26) `if (strokes.Count == 0 || gapLength <= 0.01) return strokes;`
- [Строка 34](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L34) `int turns = ((quarterTurns % 4) + 4) % 4;`
- [Строка 38](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L38) `if (s.Role == "pipe" && s.Points.Count == 2 && partMap.TryGetValue(s.SourceId, out var part))`
- [Строка 41](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L41) `if (ends.Length == 2)`
- [Строка 43](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L43) `var p1 = s.Points[0];`
- [Строка 44](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L44) `var p2 = s.Points[1];`
- [Строка 48](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L48) `if (len > 0.1)`
- [Строка 50](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L50) `var m1 = ends[0].Position;`
- [Строка 51](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L51) `var m2 = ends[1].Position;`
- [Строка 53](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L53) `if (vProj.X * dx + vProj.Y * dy < 0)`
- [Строка 65](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L65) `if (pipeSegments.Count == 0 || (pipeSegments.Count < 2 && (symbols is null || symbols.Count == 0) && !otherStrokes.Any(s => s.Role == "continuation"))) return strokes;`
- [Строка 67](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L67) `var repairs=Enumerable.Range(0,pipeSegments.Count).Select(_=>new List<(double Start,double End,string Pipe)>()).ToArray();`
- [Строка 69](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L69) `for (int i = 0; i < pipeSegments.Count; i++)`
- [Строка 74](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L74) `for (int k = 0; k < turns; k++) (x, y) = (-y, x);`
- [Строка 78](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L78) `for (int i = 0; i < pipeSegments.Count; i++)`
- [Строка 81](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L81) `for (int j = i + 1; j < pipeSegments.Count; j++)`
- [Строка 89](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L89) `if (segA.M1.DistanceTo(segB.M1) < 1.0 || segA.M1.DistanceTo(segB.M2) < 1.0 ||`
- [Строка 90](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L90) `segA.M2.DistanceTo(segB.M1) < 1.0 || segA.M2.DistanceTo(segB.M2) < 1.0)`
- [Строка 99](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L99) `if (Math.Abs(den) < 1e-6) continue;`
- [Строка 108](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L108) `if (ta <= 1e-4 || ta >= 1.0 - 1e-4 || tb <= 1e-4 || tb >= 1.0 - 1e-4)`
- [Строка 121](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L121) `if (pt3Da.DistanceTo(pt3Db) < 1.0) continue;`
- [Строка 129](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L129) `if (Math.Abs(dz) > 0.5)`
- [Строка 131](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L131) `if (dz < 0)`
- [Строка 162](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L162) `double deltaT = (gapLength * 0.5) / lowerLen;`
- [Строка 163](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L163) `double t0 = Math.Max(0.0, lowerT - deltaT);`
- [Строка 164](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L164) `double t1 = Math.Min(1.0, lowerT + deltaT);`
- [Строка 172](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L172) `if (symbols is not null && symbols.Count > 0)`
- [Строка 176](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L176) `if (!partMap.TryGetValue(g.SourceId, out var symPart) || symPart.Ports.Count == 0) continue;`
- [Строка 178](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L178) `symPart.Properties.GetValueOrDefault("PnPClassName", "").IndexOf("Support", StringComparison.OrdinalIgnoreCase) >= 0 ||`
- [Строка 179](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L179) `g.Definition.IndexOf("SUPPORT", StringComparison.OrdinalIgnoreCase) >= 0) continue;`
- [Строка 186](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L186) `double symRadius = corners.Length > 0 ? corners.Max(c => Math.Sqrt((c.X - g.Origin.X) * (c.X - g.Origin.X) + (c.Y - g.Origin.Y) * (c.Y - g.Origin.Y))) : 5.0;`
- [Строка 188](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L188) `for (int i = 0; i < pipeSegments.Count; i++)`
- [Строка 198](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L198) `if (t <= 1e-4 || t >= 1.0 - 1e-4) continue;`
- [Строка 204](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L204) `if (dist > Math.Max(symRadius, 4.0)) continue;`
- [Строка 212](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L212) `bool isLower = Math.Abs(dz) > 0.5 ? dz < 0 : ObserverProximity(pt3Da) < ObserverProximity(symPt3D);`
- [Строка 216](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L216) `double breakWidth = Math.Max(gapLength * 0.5, symRadius * 0.8);`
- [Строка 218](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L218) `double t0 = Math.Max(0.0, t - deltaT);`
- [Строка 219](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L219) `double t1 = Math.Min(1.0, t + deltaT);`
- [Строка 227](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L227) `foreach (var cs in strokes.Where(s => s.Role == "continuation" && s.Points.Count >= 2))`
- [Строка 229](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L229) `if (!partMap.TryGetValue(cs.SourceId, out var contPart) || contPart.Ports.Count == 0) continue;`
- [Строка 237](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L237) `for (int i = 0; i < pipeSegments.Count; i++)`
- [Строка 247](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L247) `if (t <= 1e-4 || t >= 1.0 - 1e-4) continue;`
- [Строка 253](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L253) `if (dist > 3.5) continue;`
- [Строка 261](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L261) `bool isLower = Math.Abs(dz) > 0.5 ? dz < 0 : ObserverProximity(pt3Da) < ObserverProximity(contPt3D);`
- [Строка 265](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L265) `double breakWidth = gapLength * 0.5 + 1.5;`
- [Строка 267](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L267) `double t0 = Math.Max(0.0, t - deltaT);`
- [Строка 268](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L268) `double t1 = Math.Min(1.0, t + deltaT);`
- [Строка 276](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L276) `for (int i = 0; i < pipeSegments.Count; i++)`
- [Строка 280](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L280) `if(intervals.Count>0&&symbols is not null)`
- [Строка 289](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L289) `double t=((port.Position.X-seg.M1.X)*mx+(port.Position.Y-seg.M1.Y)*my+(port.Position.Z-seg.M1.Z)*mz)/Math.Max(1e-9,l2);`
- [Строка 290](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L290) `return t>=0&&t<=1&&port.Position.DistanceTo(new(seg.M1.X+mx*t,seg.M1.Y+my*t,seg.M1.Z+mz*t))<=1;`
- [Строка 294](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L294) `double low=footprint.Min(p=>(p.X-seg.P1.X)*ux+(p.Y-seg.P1.Y)*uy)-1.5;`
- [Строка 295](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L295) `double high=footprint.Max(p=>(p.X-seg.P1.X)*ux+(p.Y-seg.P1.Y)*uy)+1.5;`
- [Строка 304](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L304) `if (intervals.Count == 0)`
- [Строка 312](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L312) `var current = intervals[0];`
- [Строка 313](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L313) `for (int k = 1; k < intervals.Count; k++)`
- [Строка 315](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L315) `if (intervals[k].Start <= current.End + 1e-6)`
- [Строка 327](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L327) `double cursor = 0.0;`
- [Строка 333](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L333) `if (brk.Start > cursor + 1e-5)`
- [Строка 336](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L336) `if (subLen >= 0.5)`
- [Строка 346](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L346) `if (cursor < 1.0 - 1e-5)`
- [Строка 348](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L348) `double subLen = (1.0 - cursor) * seg.Length2D;`
- [Строка 349](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L349) `if (subLen >= 0.5)`
- [Строка 352](src/IsoGenerator.Core/PipeCrossingBreaks.cs#L352) `var endPt = new SheetPoint(seg.P1.X + 1.0 * dax, seg.P1.Y + 1.0 * day);`

### PlanningExecution.cs

- [Строка 14](src/IsoGenerator.Core/PlanningExecution.cs#L14) `public static double MinimumPipeLength=>current.Value?.minimumPipeLength??10;`
- [Строка 15](src/IsoGenerator.Core/PlanningExecution.cs#L15) `readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();long last=-1000;int variants;`
- [Строка 18](src/IsoGenerator.Core/PlanningExecution.cs#L18) `public PlanningExecution(CancellationToken token,IProgress<PlanningProgress>? progress,TimeSpan? timeLimit,double minimumPipeLength=10)`
- [Строка 22](src/IsoGenerator.Core/PlanningExecution.cs#L22) `if(!double.IsFinite(minimumPipeLength)||minimumPipeLength<1||minimumPipeLength>100000)`
- [Строка 28](src/IsoGenerator.Core/PlanningExecution.cs#L28) `(scope.clock.Elapsed<scope.timeLimit-TimeSpan.FromSeconds(15)&&scope.variants<Math.Max(1,IsoAdvancedSettings.Current.PlanningVariants-12));`
- [Строка 35](src/IsoGenerator.Core/PlanningExecution.cs#L35) `throw new PlanningLimitException($"Расчёт компоновки остановлен: достигнут предел поиска ({scope.timeLimit.TotalSeconds:0} с или {IsoAdvancedSettings.Current.PlanningVariants} вариантов). Проверено вариантов: {scope.varian…`
- [Строка 40](src/IsoGenerator.Core/PlanningExecution.cs#L40) `if(scope.clock.ElapsedMilliseconds-scope.last<250)return;scope.last=scope.clock.ElapsedMilliseconds;`

### SchematicLayout.cs

- [Строка 11](src/IsoGenerator.Core/SchematicLayout.cs#L11) `static (long,long,long) Key(ModelPoint p)=>( (long)Math.Round(p.X*100), (long)Math.Round(p.Y*100),(long)Math.Round(p.Z*100));`
- [Строка 17](src/IsoGenerator.Core/SchematicLayout.cs#L17) `public SchematicLayout(PipelineSnapshot snapshot,int quarterTurns=0,int compactLevel=0,IReadOnlyDictionary<string,double>? pipeExtensions=null,IsoSymbolMap? symbolMap=null,double paperMargin=0,double? fixedScale=null)`
- [Строка 23](src/IsoGenerator.Core/SchematicLayout.cs#L23) `double RoundPipeLength(double length)=>Math.Ceiling((length-1e-8)/pipeGridStep)*pipeGridStep;`
- [Строка 27](src/IsoGenerator.Core/SchematicLayout.cs#L27) `if(edge.Kind=="TapOffset")return 0;`
- [Строка 28](src/IsoGenerator.Core/SchematicLayout.cs#L28) `if(edge.Kind=="Joint")return 1; // Requested visible flange-joint gap, not a symbol size.`
- [Строка 33](src/IsoGenerator.Core/SchematicLayout.cs#L33) `if(definition is null)return 10; // Explicit missing-block placeholder policy.`
- [Строка 34](src/IsoGenerator.Core/SchematicLayout.cs#L34) `var ports=definition.Ports.Where(p=>p.Key.StartsWith("Port",StringComparison.OrdinalIgnoreCase)).OrderBy(p=>int.TryParse(p.Key[4..],out int number)?number:int.MaxValue).Select(p=>p.Value).ToArray();`
- [Строка 38](src/IsoGenerator.Core/SchematicLayout.cs#L38) `if(edge.Kind=="Tee"&&modelPorts.Length==3)`
- [Строка 40](src/IsoGenerator.Core/SchematicLayout.cs#L40) `var pair=(from i in Enumerable.Range(0,3) from j in Enumerable.Range(i+1,2-i) let u=modelPorts[i].Direction let v=modelPorts[j].Direction where u.X*v.X+u.Y*v.Y+u.Z*v.Z<-.99 select (i,j)).FirstOrDefault((-1,-1));`
- [Строка 41](src/IsoGenerator.Core/SchematicLayout.cs#L41) `if(pair.Item1>=0)modelPorts=new[]{modelPorts[pair.Item1],modelPorts[pair.Item2],modelPorts[Enumerable.Range(0,3).Single(i=>i!=pair.Item1&&i!=pair.Item2)]};`
- [Строка 43](src/IsoGenerator.Core/SchematicLayout.cs#L43) `string name=graph.Nodes[edge.A].PortName.Length>0?graph.Nodes[edge.A].PortName:graph.Nodes[edge.B].PortName;`
- [Строка 46](src/IsoGenerator.Core/SchematicLayout.cs#L46) `if(index>=0&&index<ports.Length)`
- [Строка 47](src/IsoGenerator.Core/SchematicLayout.cs#L47) `{var port=ports[index];return Math.Sqrt(Math.Pow(port.X-localCenter.X,2)+Math.Pow(port.Y-localCenter.Y,2))*catalog.ScaleFor(part);}`
- [Строка 49](src/IsoGenerator.Core/SchematicLayout.cs#L49) `var a=ports.Length>0?ports[0]:new SheetPoint(definition.Minimum.X,0);`
- [Строка 50](src/IsoGenerator.Core/SchematicLayout.cs#L50) `var b=ports.Length>1?ports[1]:new SheetPoint(definition.Maximum.X,0);`
- [Строка 51](src/IsoGenerator.Core/SchematicLayout.cs#L51) `double span=Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Y-a.Y,2));`
- [Строка 52](src/IsoGenerator.Core/SchematicLayout.cs#L52) `return (span>1e-8?span:definition.Maximum.X-definition.Minimum.X)*catalog.ScaleFor(part);`
- [Строка 58](src/IsoGenerator.Core/SchematicLayout.cs#L58) `for(int i=0;i<graph.Edges.Count;i++)`
- [Строка 67](src/IsoGenerator.Core/SchematicLayout.cs#L67) `var lowerBounds=graph.Edges.Select(edge=>edge.Kind=="Pipe"?PlanningExecution.MinimumPipeLength:0).ToArray();`
- [Строка 68](src/IsoGenerator.Core/SchematicLayout.cs#L68) `var pipeReferences=graph.Edges.Where(e=>e.Kind=="Pipe").Select(e=>graph.Nodes[e.A].Point.DistanceTo(graph.Nodes[e.B].Point)).Where(l=>l>.001).OrderBy(l=>l).ToArray();`
- [Строка 69](src/IsoGenerator.Core/SchematicLayout.cs#L69) `double referencePipeLength=pipeReferences.Length>0?pipeReferences[pipeReferences.Length/2]:1;`
- [Строка 70](src/IsoGenerator.Core/SchematicLayout.cs#L70) `double PaperDemand(double length)=>compactLevel is 1 or 2 or 4 or 5?Math.Sqrt(length*referencePipeLength):length;`
- [Строка 71](src/IsoGenerator.Core/SchematicLayout.cs#L71) `double availableWidth=SheetComposition.Paper.Width-2*IsoAdvancedSettings.Current.DimensionClearance-paperMargin;`
- [Строка 76](src/IsoGenerator.Core/SchematicLayout.cs#L76) `for(int i=0;i<graph.Edges.Count;i++)`
- [Строка 80](src/IsoGenerator.Core/SchematicLayout.cs#L80) `if(!isPipe[i]){adjustments[i]=hiddenGaskets.Contains(edge.Source)?1:SymbolLength(edge);continue;}`
- [Строка 84](src/IsoGenerator.Core/SchematicLayout.cs#L84) `if(Math.Abs(dx)>Math.Abs(dy))demands[i]*=compactLevel==0?3:2;`
- [Строка 85](src/IsoGenerator.Core/SchematicLayout.cs#L85) `if(pipeExtensions is null||pipeExtensions.Count==0)continue;`
- [Строка 87](src/IsoGenerator.Core/SchematicLayout.cs#L87) `double fraction=a.DistanceTo(b)/Math.Max(.001,pipeTotals[edge.Source]);`
- [Строка 96](src/IsoGenerator.Core/SchematicLayout.cs#L96) `for(int i=0;i<graph.Edges.Count;i++)`
- [Строка 107](src/IsoGenerator.Core/SchematicLayout.cs#L107) `result[root]=new(0,0);var queue=new Queue<(int Node,int Parent)>();queue.Enqueue((root,-1));`
- [Строка 109](src/IsoGenerator.Core/SchematicLayout.cs#L109) `while(queue.Count>0)`
- [Строка 118](src/IsoGenerator.Core/SchematicLayout.cs#L118) `if(len<.001)throw new LayoutProjectionException("Участок вырождается в выбранном ракурсе; выберите другое направление севера.");`
- [Строка 123](src/IsoGenerator.Core/SchematicLayout.cs#L123) `if(Math.Sqrt(Math.Pow(existing.X-target.X,2)+Math.Pow(existing.Y-target.Y,2))>.1)`
- [Строка 132](src/IsoGenerator.Core/SchematicLayout.cs#L132) `foreach(int root in Enumerable.Range(0,graph.Nodes.Count).Select(graph.Sets.Find).Distinct())`
- [Строка 133](src/IsoGenerator.Core/SchematicLayout.cs#L133) `if(!result.ContainsKey(root)){result[root]=new(0,0);components.Add(new(){root});}`
- [Строка 135](src/IsoGenerator.Core/SchematicLayout.cs#L135) `var packed=new List<DrawingBounds>();double heightLimit=SheetComposition.Paper.Height-(compactLevel==0?55:compactLevel==1?75:95)-paperMargin;`
- [Строка 136](src/IsoGenerator.Core/SchematicLayout.cs#L136) `foreach(var component in components.OrderByDescending(c=>(c.Max(n=>result[n].X)-c.Min(n=>result[n].X)+10)*(c.Max(n=>result[n].Y)-c.Min(n=>result[n].Y)+10)))`
- [Строка 139](src/IsoGenerator.Core/SchematicLayout.cs#L139) `double width=Math.Max(5,component.Max(n=>result[n].X)-left),height=Math.Max(5,component.Max(n=>result[n].Y)-bottom);`
- [Строка 140](src/IsoGenerator.Core/SchematicLayout.cs#L140) `var xs=new[]{0.0}.Concat(packed.Select(b=>b.Right+20)).Distinct();var ys=new[]{0.0}.Concat(packed.Select(b=>b.Top+20)).Distinct();`
- [Строка 142](src/IsoGenerator.Core/SchematicLayout.cs#L142) `where !packed.Any(b=>box.Overlaps(new(b.Left-19.99,b.Bottom-19.99,b.Right+19.99,b.Top+19.99)))`
- [Строка 143](src/IsoGenerator.Core/SchematicLayout.cs#L143) `let w=Math.Max(box.Right,packed.Select(b=>b.Right).DefaultIfEmpty(0).Max())`
- [Строка 144](src/IsoGenerator.Core/SchematicLayout.cs#L144) `let h=Math.Max(box.Top,packed.Select(b=>b.Top).DefaultIfEmpty(0).Max())`
- [Строка 146](src/IsoGenerator.Core/SchematicLayout.cs#L146) `var selected=choices[0];`
- [Строка 152](src/IsoGenerator.Core/SchematicLayout.cs#L152) `double maxHeight=SheetComposition.Paper.Height-(compactLevel==3?20:compactLevel==0?55:compactLevel==1?75:95)-paperMargin;`
- [Строка 153](src/IsoGenerator.Core/SchematicLayout.cs#L153) `bool Fits(Dictionary<int,SheetPoint> p)=>p.Count>0&&p.Values.Max(v=>v.X)-p.Values.Min(v=>v.X)<=availableWidth&&p.Values.Max(v=>v.Y)-p.Values.Min(v=>v.Y)<=maxHeight;`
- [Строка 154](src/IsoGenerator.Core/SchematicLayout.cs#L154) `var minimum=Arrange(0);`
- [Строка 156](src/IsoGenerator.Core/SchematicLayout.cs#L156) `double low=0,high=.2;`
- [Строка 159](src/IsoGenerator.Core/SchematicLayout.cs#L159) `if(fixedScale is null&&compactLevel!=3)for(int i=0;i<18;i++){PlanningExecution.Check();double mid=(low+high)/2;if(Fits(Arrange(mid)))low=mid;else high=mid;}`
- [Строка 160](src/IsoGenerator.Core/SchematicLayout.cs#L160) `if(compactLevel>=2)low*=compactLevel switch{2=>.8,4=>.6,5=>.5,_=>0};`
- [Строка 163](src/IsoGenerator.Core/SchematicLayout.cs#L163) `var arranged=compactLevel==3?minimum:Arrange(low);`
- [Строка 164](src/IsoGenerator.Core/SchematicLayout.cs#L164) `var pipeEdges=Enumerable.Range(0,graph.Edges.Count).Where(i=>graph.Edges[i].Kind=="Pipe").ToArray();`
- [Строка 167](src/IsoGenerator.Core/SchematicLayout.cs#L167) `var edge=graph.Edges[i];var a=p[graph.Sets.Find(edge.A)];var b=p[graph.Sets.Find(edge.B)];return Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));`
- [Строка 177](src/IsoGenerator.Core/SchematicLayout.cs#L177) `double den=Cross(v.X-u.X,v.Y-u.Y,z.X-w.X,z.Y-w.Y);if(Math.Abs(den)<1e-8)return false;`
- [Строка 179](src/IsoGenerator.Core/SchematicLayout.cs#L179) `return t>.001&&t<.999&&s>.001&&s<.999;`
- [Строка 181](src/IsoGenerator.Core/SchematicLayout.cs#L181) `var edgePairs=(from i in Enumerable.Range(0,graph.Edges.Count) from j in Enumerable.Range(i+1,graph.Edges.Count-i-1) where !Crosses(i,j,arranged) select (i,j)).ToArray();`
- [Строка 184](src/IsoGenerator.Core/SchematicLayout.cs#L184) `for(int iteration=0;iteration<(fixedScale is null&&compactLevel==0&&!cycleSolver.HasCycles?10:0);iteration++)`
- [Строка 191](src/IsoGenerator.Core/SchematicLayout.cs#L191) `foreach(var option in options.Take(32))`
- [Строка 204](src/IsoGenerator.Core/SchematicLayout.cs#L204) `return Math.Abs(length-Math.Round(length/pipeGridStep)*pipeGridStep)>.01;`
- [Строка 207](src/IsoGenerator.Core/SchematicLayout.cs#L207) `shift=new(70-positions.Values.Min(p=>p.X),55-positions.Values.Min(p=>p.Y));`
- [Строка 213](src/IsoGenerator.Core/SchematicLayout.cs#L213) `return e.Kind=="Pipe"&&!hiddenGaskets.Contains(e.Source)&&Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2))*low<lowerBounds[item.index]-.01;`
- [Строка 215](src/IsoGenerator.Core/SchematicLayout.cs#L215) `for(int i=0;i<graph.Nodes.Count;i++)`
- [Строка 225](src/IsoGenerator.Core/SchematicLayout.cs#L225) `if(matches.Count>0)`
- [Строка 227](src/IsoGenerator.Core/SchematicLayout.cs#L227) `var values=matches.Select(n=>positions[graph.Sets.Find(n)]).ToArray();var first=values[0];`
- [Строка 228](src/IsoGenerator.Core/SchematicLayout.cs#L228) `if(values.Any(p=>Math.Sqrt(Math.Pow(p.X-first.X,2)+Math.Pow(p.Y-first.Y,2))>.1))throw new InvalidDataException("Coincident unconnected model points need source-specific layout mapping");`
- [Строка 239](src/IsoGenerator.Core/SchematicLayout.cs#L239) `if(length2<1e-12)continue;`
- [Строка 241](src/IsoGenerator.Core/SchematicLayout.cs#L241) `if(t<-.0001||t>1.0001)continue;`
- [Строка 244](src/IsoGenerator.Core/SchematicLayout.cs#L244) `best=distance;t=Math.Clamp(t,0,1);var pa=positions[graph.Sets.Find(edge.A)];var pb=positions[graph.Sets.Find(edge.B)];`
- [Строка 247](src/IsoGenerator.Core/SchematicLayout.cs#L247) `if(best>.1||result is null)throw new InvalidDataException($"No schematic mapping for point {point}");`

### SheetComposition.cs

- [Строка 7](src/IsoGenerator.Core/SheetComposition.cs#L7) `public SheetPoint Center=>new((Left+Right)/2,(Bottom+Top)/2);`
- [Строка 9](src/IsoGenerator.Core/SheetComposition.cs#L9) `public bool Overlaps(DrawingBounds b)=>Left<b.Right-1e-6&&Right>b.Left+1e-6&&Bottom<b.Top-1e-6&&Top>b.Bottom+1e-6;`
- [Строка 18](src/IsoGenerator.Core/SheetComposition.cs#L18) `public static IReadOnlyList<DrawingBounds> Reserved(double bomRows)=>new[]{new DrawingBounds(398,Math.Max(60,399-IsoAdvancedSettings.Current.BomRowHeight*bomRows-6),590,420),new DrawingBounds(398,0,590,60)};`
- [Строка 19](src/IsoGenerator.Core/SheetComposition.cs#L19) `static DrawingBounds Box(IEnumerable<SheetPoint> points,double pad=0)`
- [Строка 27](src/IsoGenerator.Core/SheetComposition.cs#L27) `foreach(var (a,b) in stroke.Points.Zip(stroke.Points.Skip(1)))pieces.Add(Box(new[]{a,b},.3));`
- [Строка 28](src/IsoGenerator.Core/SheetComposition.cs#L28) `foreach(var symbol in scene.Symbols??Array.Empty<SymbolGlyph>())pieces.Add(Box(SymbolFootprint.Corners(symbol),.3));`
- [Строка 33](src/IsoGenerator.Core/SheetComposition.cs#L33) `double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,l=Math.Sqrt(dx*dx+dy*dy);if(l<.001)continue;`
- [Строка 37](src/IsoGenerator.Core/SheetComposition.cs#L37) `pieces.Add(Box(new[]{a,b},.8));`
- [Строка 38](src/IsoGenerator.Core/SheetComposition.cs#L38) `pieces.Add(Box(new[]{d.Start,new SheetPoint(a.X+ex.X,a.Y+ex.Y)},.2));`
- [Строка 39](src/IsoGenerator.Core/SheetComposition.cs#L39) `pieces.Add(Box(new[]{d.End,new SheetPoint(b.X+ex.X,b.Y+ex.Y)},.2));`
- [Строка 42](src/IsoGenerator.Core/SheetComposition.cs#L42) `foreach(var weld in scene.Welds??Array.Empty<WeldMarker>())pieces.Add(new(weld.Position.X-.9,weld.Position.Y-.9,weld.Position.X+.9,weld.Position.Y+.9));`
- [Строка 51](src/IsoGenerator.Core/SheetComposition.cs#L51) `var pieces=Pieces(scene);if(pieces.Count==0)return scene;`
- [Строка 53](src/IsoGenerator.Core/SheetComposition.cs#L53) `if(minX>maxX||minY>maxY)throw new SheetCapacityException($"Чертёж с размерами и выносками {bounds.Width:F1} x {bounds.Height:F1} мм не помещается в рабочую область A2 {Paper.Width:F1} x {Paper.Height:F1} мм.",paperWidthOve…`
- [Строка 57](src/IsoGenerator.Core/SheetComposition.cs#L57) `var ys=new[]{Math.Clamp(desiredY,minY,maxY),minY,maxY}.Concat(forbidden.SelectMany(f=>new[]{f.Bottom-.01,f.Top+.01}).Where(y=>y>=minY&&y<=maxY)).Distinct().ToArray();`
- [Строка 66](src/IsoGenerator.Core/SheetComposition.cs#L66) `if(f.Left>start)intervals.Add((start,Math.Min(maxX,f.Left-.01)));`
- [Строка 67](src/IsoGenerator.Core/SheetComposition.cs#L67) `start=Math.Max(start,f.Right+.01);if(start>maxX)break;`
- [Строка 72](src/IsoGenerator.Core/SheetComposition.cs#L72) `double x=Math.Clamp(desiredX,interval.Left,interval.Right),score=Math.Pow(x-desiredX,2)+Math.Pow(y-desiredY,2);`

### SheetConnectivity.cs

- [Строка 17](src/IsoGenerator.Core/SheetConnectivity.cs#L17) `if(ends.Length!=1)throw new InvalidDataException($"{instrument.Handle}: не удалось определить узел для выноски несоединённого КИП. Проверьте подключение в Plant.");`
- [Строка 18](src/IsoGenerator.Core/SheetConnectivity.cs#L18) `var at=ends[0];`
- [Строка 22](src/IsoGenerator.Core/SheetConnectivity.cs#L22) `where port.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)&&!occupied.Contains(host.Handle+":"+port.Name)&&port.Position.DistanceTo(at.Position)<=.01`
- [Строка 25](src/IsoGenerator.Core/SheetConnectivity.cs#L25) `where length>1e-10&&(u.X*v.X+u.Y*v.Y+u.Z*v.Z)/length<=-.99`
- [Строка 27](src/IsoGenerator.Core/SheetConnectivity.cs#L27) `if(candidates.Length!=1)throw new InvalidDataException($"{instrument.Handle}: для выноски КИП найдено {candidates.Length} однозначных подключений. Проверьте соединение в Plant; отдельный пустой лист не создаётся.");`
- [Строка 28](src/IsoGenerator.Core/SheetConnectivity.cs#L28) `var owner=candidates[0];`
- [Строка 42](src/IsoGenerator.Core/SheetConnectivity.cs#L42) `if(links.Any(c=>c.FirstHandle==support.Handle||c.SecondHandle==support.Handle)||support.Ports.Count==0)continue;`
- [Строка 43](src/IsoGenerator.Core/SheetConnectivity.cs#L43) `var cp=support.Ports[0].Position;`
- [Строка 47](src/IsoGenerator.Core/SheetConnectivity.cs#L47) `var ends=pipe.Ports.Where(p=>p.Name is "S1" or "S2").ToArray();if(ends.Length!=2)continue;`
- [Строка 48](src/IsoGenerator.Core/SheetConnectivity.cs#L48) `var a=ends[0].Position;var b=ends[1].Position;double dx=b.X-a.X,dy=b.Y-a.Y,dz=b.Z-a.Z,l2=dx*dx+dy*dy+dz*dz;`
- [Строка 49](src/IsoGenerator.Core/SheetConnectivity.cs#L49) `if(l2<1e-8)continue;double t=((cp.X-a.X)*dx+(cp.Y-a.Y)*dy+(cp.Z-a.Z)*dz)/l2;`
- [Строка 50](src/IsoGenerator.Core/SheetConnectivity.cs#L50) `if(t<0||t>1||cp.DistanceTo(new(a.X+t*dx,a.Y+t*dy,a.Z+t*dz))>.01)continue;`
- [Строка 51](src/IsoGenerator.Core/SheetConnectivity.cs#L51) `hosts.Add((pipe,ends[0],ends[1]));`
- [Строка 53](src/IsoGenerator.Core/SheetConnectivity.cs#L53) `if(hosts.Count!=1)throw new InvalidDataException($"{support.Handle}: опора без связи с трубой; не удалось однозначно определить трубу по CP.");`
- [Строка 54](src/IsoGenerator.Core/SheetConnectivity.cs#L54) `var host=hosts[0];string name="ISO-SUPPORT-"+support.Handle;`
- [Строка 55](src/IsoGenerator.Core/SheetConnectivity.cs#L55) `var port=host.Part.Ports.FirstOrDefault(p=>p.Position.DistanceTo(cp)<.001);`
- [Строка 57](src/IsoGenerator.Core/SheetConnectivity.cs#L57) `links.Add(new(host.Part.Handle,port.Name,support.Handle,support.Ports[0].Name));`
- [Строка 68](src/IsoGenerator.Core/SheetConnectivity.cs#L68) `while(remaining.Count>0){string root=remaining.First();remaining.Remove(root);var group=new HashSet<string>(StringComparer.OrdinalIgnoreCase){root};var q=new Queue<string>();q.Enqueue(root);while(q.Count>0)foreach(var next…`

### SheetPlanner.cs

- [Строка 5](src/IsoGenerator.Core/SheetPlanner.cs#L5) `public sealed record Annotation(string SourceId, string Role, SheetPoint Position, string Text, double Height = 3.5, SheetPoint? Anchor = null, SheetPoint? LeaderKnee = null, IReadOnlyList<SheetPoint>? NearbyAnchors = null…`
- [Строка 10](src/IsoGenerator.Core/SheetPlanner.cs#L10) `public string Label => Math.Ceiling(Millimeters-1e-8).ToString("0.#",CultureInfo.InvariantCulture)+(ReferenceOnly?"*":"");`
- [Строка 14](src/IsoGenerator.Core/SheetPlanner.cs#L14) `public string QuantityText => Unit.Trim()=="м" ? (Quantity>0?Math.Max(.1,Math.Ceiling(Quantity*10-1e-9)/10):0).ToString("0.0",CultureInfo.InvariantCulture) : Quantity.ToString("0.###",CultureInfo.InvariantCulture);`
- [Строка 18](src/IsoGenerator.Core/SheetPlanner.cs#L18) `IReadOnlyList<PipeDimension> Dimensions, IReadOnlyList<BomRow> BillOfMaterials, IReadOnlyList<string> Warnings, int SourcePartCount, IReadOnlyList<SymbolGlyph>? Symbols = null, string SourceDrawing = "", IReadOnlyDictionar…`
- [Строка 26](src/IsoGenerator.Core/SheetPlanner.cs#L26) `if(snapshot.SchemaVersion!=1) errors.Add("Unsupported snapshot schema");`
- [Строка 27](src/IsoGenerator.Core/SheetPlanner.cs#L27) `if(snapshot.Parts.Count==0) errors.Add("Empty snapshot");`
- [Строка 28](src/IsoGenerator.Core/SheetPlanner.cs#L28) `var duplicates=snapshot.Parts.GroupBy(p=>p.Handle,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1);`
- [Строка 30](src/IsoGenerator.Core/SheetPlanner.cs#L30) `if(errors.Count>0) return new(errors,warnings,0);`
- [Строка 38](src/IsoGenerator.Core/SheetPlanner.cs#L38) `if(p.Ports.GroupBy(x=>x.Name).Any(g=>g.Count()>1)) errors.Add($"{p.Handle}: duplicate port names");`
- [Строка 46](src/IsoGenerator.Core/SheetPlanner.cs#L46) `if(gap>1) errors.Add($"Connected ports {c.FirstHandle}:{c.FirstPort}/{c.SecondHandle}:{c.SecondPort} differ by {gap:F3} mm");`
- [Строка 49](src/IsoGenerator.Core/SheetPlanner.cs#L49) `var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var componentOf=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase); int components=0;`
- [Строка 53](src/IsoGenerator.Core/SheetPlanner.cs#L53) `while(queue.Count>0) foreach(var n in adjacency[queue.Dequeue()]) if(visited.Add(n)){componentOf[n]=components;queue.Enqueue(n);}`
- [Строка 55](src/IsoGenerator.Core/SheetPlanner.cs#L55) `if(components>1) warnings.Add($"Line contains {components} disconnected components; no artificial links were created");`
- [Строка 56](src/IsoGenerator.Core/SheetPlanner.cs#L56) `if(components>1)`
- [Строка 60](src/IsoGenerator.Core/SheetPlanner.cs#L60) `for(int i=0;i<free.Length;i++)for(int j=i+1;j<free.Length;j++)`
- [Строка 63](src/IsoGenerator.Core/SheetPlanner.cs#L63) `double gap=a.Port.Position.DistanceTo(b.Port.Position);if(gap>1)continue;`
- [Строка 65](src/IsoGenerator.Core/SheetPlanner.cs#L65) `if(lengths<1e-10||(u.X*v.X+u.Y*v.Y+u.Z*v.Z)/lengths>-.99)continue;`
- [Строка 66](src/IsoGenerator.Core/SheetPlanner.cs#L66) `warnings.Add(FormattableString.Invariant($"Нет соединения: {a.Part.Handle}:{a.Port.Name} / {b.Part.Handle}:{b.Port.Name}; расстояние {gap:0.####} мм. Проверьте соединение в модели Plant."));`
- [Строка 82](src/IsoGenerator.Core/SheetPlanner.cs#L82) `static ModelPoint Mid(ModelPoint a,ModelPoint b)=>Mul(Add(a,b),.5);`
- [Строка 87](src/IsoGenerator.Core/SheetPlanner.cs#L87) `if(Math.Abs(den)<1e-9)return null;`
- [Строка 90](src/IsoGenerator.Core/SheetPlanner.cs#L90) `return p.DistanceTo(q)<1?Mid(p,q):null;`
- [Строка 92](src/IsoGenerator.Core/SheetPlanner.cs#L92) `public static SheetScene Build(PipelineSnapshot snapshot,int quarterTurns=0, IsoSymbolMap? symbolMap=null, IReadOnlyList<SheetContinuation>? continuations=null, int sheetNumber=1, int sheetCount=1, IReadOnlyList<Stroke>? c…`
- [Строка 95](src/IsoGenerator.Core/SheetPlanner.cs#L95) `if(validation.Errors.Count>0)throw new InvalidDataException(string.Join("\n",validation.Errors));`
- [Строка 98](src/IsoGenerator.Core/SheetPlanner.cs#L98) `if(physical.Length==0)throw new InvalidDataException("No physical parts");`
- [Строка 100](src/IsoGenerator.Core/SheetPlanner.cs#L100) `if(lineNames.Length!=1)throw new InvalidDataException("Prototype supports one line per sheet");`
- [Строка 102](src/IsoGenerator.Core/SheetPlanner.cs#L102) `if(ports.Length==0)throw new InvalidDataException("No geometry");`
- [Строка 107](src/IsoGenerator.Core/SheetPlanner.cs#L107) `if(layout.NonGridPipes.Count>0)`
- [Строка 108](src/IsoGenerator.Core/SheetPlanner.cs#L108) `warnings.Add("Для замыкания контура отступили от шага 10 мм на листе: "+string.Join(", ",layout.NonGridPipes)+".");`
- [Строка 109](src/IsoGenerator.Core/SheetPlanner.cs#L109) `if(layout.ClosedLoops>0)warnings.Add($"Совместно замкнуто контуров: {layout.ClosedLoops}; размеры элементов и реальные координаты сохранены.");`
- [Строка 114](src/IsoGenerator.Core/SheetPlanner.cs#L114) `var first=group.First(); bool pipe=Kind(first)=="Pipe"; double qty=0;`
- [Строка 117](src/IsoGenerator.Core/SheetPlanner.cs#L117) `if(pipe){var e=Ends(p);if(e.Length!=2)throw new InvalidDataException($"{p.Handle}: pipe requires S1/S2");qty+=e[0].Position.DistanceTo(e[1].Position)/1000;}`
- [Строка 118](src/IsoGenerator.Core/SheetPlanner.cs#L118) `else if(Get(p,"IsoQuantity").Length>0){if(!double.TryParse(Get(p,"IsoQuantity"),NumberStyles.Float,CultureInfo.InvariantCulture,out var amount)||!double.IsFinite(amount)||amount<=0)throw new InvalidDataException($"{p.Handl…`
- [Строка 121](src/IsoGenerator.Core/SheetPlanner.cs#L121) `int position=rows.Count+1;`
- [Строка 123](src/IsoGenerator.Core/SheetPlanner.cs#L123) `if(BomCategories.Classify(first)==BomCategory.Fasteners&&Get(first,"Length").Length>0)description+=" (L="+Get(first,"Length")+" мм)";`
- [Строка 124](src/IsoGenerator.Core/SheetPlanner.cs#L124) `rows.Add(new(position,Kind(first),description,Get(first,"DesignStd"),qty,pipe?"м":Get(first,"IsoQuantityUnit").Length>0?Get(first,"IsoQuantityUnit"):"шт.",group.Select(p=>p.Handle).ToArray(),PartIdentifier.Read(first),BomC…`
- [Строка 133](src/IsoGenerator.Core/SheetPlanner.cs#L133) `if(ends.Length==0)throw new InvalidDataException($"{p.Handle}: у КИП нет точки подключения");`
- [Строка 134](src/IsoGenerator.Core/SheetPlanner.cs#L134) `var anchor=Project(ends[0].Position);string tag=PartIdentifier.Read(p);`
- [Строка 135](src/IsoGenerator.Core/SheetPlanner.cs#L135) `if(tag.Length==0){tag="КИП: ИД не задан";warnings.Add($"{p.Handle}: у КИП не задан Tag; обозначение прибора не выдумывалось.");}`
- [Строка 139](src/IsoGenerator.Core/SheetPlanner.cs#L139) `if(symbolMap is not null&&kind!="Pipe"&&ends.Length>0)`
- [Строка 149](src/IsoGenerator.Core/SheetPlanner.cs#L149) `if(reach>5.01)strokes.Add(new(p.Handle,"missing-connection",new[]{point,new SheetPoint(placeholderCenter.X+dx*5/reach,placeholderCenter.Y+dy*5/reach)}));`
- [Строка 151](src/IsoGenerator.Core/SheetPlanner.cs#L151) `if(positions.ContainsKey(p.Handle))labels.Add(new(p.Handle,"position",new(placeholderCenter.X+7,placeholderCenter.Y+3),positions[p.Handle].ToString(CultureInfo.InvariantCulture),Anchor:placeholderCenter,NearbyAnchors:ends.…`
- [Строка 153](src/IsoGenerator.Core/SheetPlanner.cs#L153) `{string type=Get(p,"underSupportType").Trim();if(type.Any(char.IsLetterOrDigit)){var anchor=Project(ends[0].Position);labels.Add(new(p.Handle,"support-type",anchor,type,Anchor:anchor));}}`
- [Строка 154](src/IsoGenerator.Core/SheetPlanner.cs#L154) `warnings.Add($"{p.Handle}: {Get(p,"ContentIsoSymbolDefinition")}: "+(block is null?"не найдено сопоставление SKEY":"в библиотеке проекта отсутствует блок "+block)+"; установлен резервный символ 10×10 мм с крестом.");`
- [Строка 158](src/IsoGenerator.Core/SheetPlanner.cs#L158) `if(ends.Length==1)`
- [Строка 169](src/IsoGenerator.Core/SheetPlanner.cs#L169) `var axis=hostEnds.Length==2?(Kind(host!)=="Elbow"?hostEnds.OrderBy(e=>Math.Abs(e.Direction.Z)).First().Direction:Sub(hostEnds[1].Position,hostEnds[0].Position)):ends[0].Direction;`
- [Строка 170](src/IsoGenerator.Core/SheetPlanner.cs#L170) `terminal=IsoSymbolPlacement.Attachment(p,mapped,symbolMap.Catalog,Project(ends[0].Position),axis,quarterTurns);`
- [Строка 173](src/IsoGenerator.Core/SheetPlanner.cs#L173) `{var anchor=Project(ends[0].Position);labels.Add(new(p.Handle,"support-type",anchor,supportType,Anchor:anchor));}`
- [Строка 178](src/IsoGenerator.Core/SheetPlanner.cs#L178) `?IsoSymbolPlacement.FlangeTransverse(snapshot,p,ends[0].Direction,quarterTurns):null;`
- [Строка 179](src/IsoGenerator.Core/SheetPlanner.cs#L179) `terminal=IsoSymbolPlacement.Terminal(p,mapped,symbolMap.Catalog,Project(ends[0].Position),quarterTurns,transverse);`
- [Строка 182](src/IsoGenerator.Core/SheetPlanner.cs#L182) `var origin=terminal.Origin;if(positions.ContainsKey(p.Handle))labels.Add(new(p.Handle,"position",new(origin.X+2,origin.Y+3),positions[p.Handle].ToString(CultureInfo.InvariantCulture),Anchor:origin));`
- [Строка 185](src/IsoGenerator.Core/SheetPlanner.cs#L185) `if(ends.Length<2)throw new InvalidDataException($"{p.Handle}: у компонента нет читаемых портов");`
- [Строка 186](src/IsoGenerator.Core/SheetPlanner.cs#L186) `if(kind=="Tee"&&ends.Length==3)`
- [Строка 188](src/IsoGenerator.Core/SheetPlanner.cs#L188) `var run=(from i in Enumerable.Range(0,3) from j in Enumerable.Range(i+1,2-i) where Dot(ends[i].Direction,ends[j].Direction)<-.99 select (i,j)).FirstOrDefault((-1,-1));`
- [Строка 189](src/IsoGenerator.Core/SheetPlanner.cs#L189) `if(run.Item1>=0)ends=new[]{ends[run.Item1],ends[run.Item2],ends[Enumerable.Range(0,3).Single(k=>k!=run.Item1&&k!=run.Item2)]};`
- [Строка 191](src/IsoGenerator.Core/SheetPlanner.cs#L191) `var a=ends[0].Position;var b=ends[1].Position;`
- [Строка 195](src/IsoGenerator.Core/SheetPlanner.cs#L195) `var corner=Corner(ends[0],ends[1]);`
- [Строка 202](src/IsoGenerator.Core/SheetPlanner.cs#L202) `foreach(var end in ends.Skip(2))strokes.Add(new(p.Handle,"branch",new[]{Project(center),Project(end.Position)}));`
- [Строка 212](src/IsoGenerator.Core/SheetPlanner.cs#L212) `else if(ends.Length>=3)fitted=IsoSymbolPlacement.Junction(p,block,blockCatalog,ends.Select(e=>Project(e.Position)).ToArray());`
- [Строка 216](src/IsoGenerator.Core/SheetPlanner.cs#L216) `if(kind=="Flange"&&ends[1].EndCondition=="FL")(start,finish)=(finish,start);`
- [Строка 231](src/IsoGenerator.Core/SheetPlanner.cs#L231) `foreach(var (port,point,sign) in new[]{(ends[0],pa,-1.0),(ends[1],pb,1.0)})`
- [Строка 239](src/IsoGenerator.Core/SheetPlanner.cs#L239) `var unit=Mul(axis,1/Math.Sqrt(Dot(axis,axis)));var stem=p.OperatorDirection??new ModelPoint(0,0,1);var normal=Sub(stem,Mul(unit,Dot(stem,unit)));`
- [Строка 240](src/IsoGenerator.Core/SheetPlanner.cs#L240) `if(Dot(normal,normal)<1e-10)normal=Math.Abs(unit.Z)<.95?new(0,0,1):new(1,0,0);`
- [Строка 242](src/IsoGenerator.Core/SheetPlanner.cs#L242) `var operatorAcross=hostFound?(plane.X*across.X+plane.Y*across.Y<0?new SheetPoint(-across.X,-across.Y):across):new SheetPoint(plane.X/length,plane.Y/length);`
- [Строка 252](src/IsoGenerator.Core/SheetPlanner.cs#L252) `if(double.TryParse(Get(p,"Length"),NumberStyles.Float,CultureInfo.InvariantCulture,out double declared)&&Math.Abs(declared-length)>.1)`
- [Строка 256](src/IsoGenerator.Core/SheetPlanner.cs#L256) `if(positions.ContainsKey(p.Handle))labels.Add(new(p.Handle,"position",new(c.X+2,c.Y+3),positions[p.Handle].ToString(CultureInfo.InvariantCulture),Anchor:c,NearbyAnchors:kind=="Pipe"?new[]{.1,.2,.35,.65,.8,.9}.Select(t=>{va…`
- [Строка 261](src/IsoGenerator.Core/SheetPlanner.cs#L261) `string tag=PartIdentifier.Read(valve);if(tag.Length==0)continue;`
- [Строка 262](src/IsoGenerator.Core/SheetPlanner.cs#L262) `var ends=Ends(valve);if(ends.Length<2)continue;`
- [Строка 263](src/IsoGenerator.Core/SheetPlanner.cs#L263) `var anchor=Project(Mid(ends[0].Position,ends[1].Position));`
- [Строка 273](src/IsoGenerator.Core/SheetPlanner.cs#L273) `var flanges=physical.Where(p=>neighbors.Contains(p.Handle)&&positions.ContainsKey(p.Handle)&&(Kind(p) is "Flange" or "BlindFlange"||IsoDefinition.Parse(Get(p,"ContentIsoSymbolDefinition")).Type.StartsWith("FLANGE",StringCo…`
- [Строка 277](src/IsoGenerator.Core/SheetPlanner.cs#L277) `if(text.Length==0)continue;`
- [Строка 280](src/IsoGenerator.Core/SheetPlanner.cs#L280) `if(points.Length==0)continue;`
- [Строка 282](src/IsoGenerator.Core/SheetPlanner.cs#L282) `var flangeGlyph=symbols.FirstOrDefault(g=>g.SourceId==owner.Handle&&g.Variant.Length==0);`
- [Строка 283](src/IsoGenerator.Core/SheetPlanner.cs#L283) `if(flangeGlyph is not null){var bounds=SymbolFootprint.Corners(flangeGlyph);anchor=new((bounds.Min(p=>p.X)+bounds.Max(p=>p.X))/2,(bounds.Min(p=>p.Y)+bounds.Max(p=>p.Y))/2);}`
- [Строка 286](src/IsoGenerator.Core/SheetPlanner.cs#L286) `labels.Add(new(owner.Handle,"position-group",new(anchor.X+2,anchor.Y+3),text,Anchor:anchor,NearbyAnchors:points));`
- [Строка 301](src/IsoGenerator.Core/SheetPlanner.cs#L301) `var contextPaths=(contextStrokes??Array.Empty<Stroke>()).Where(st=>st.Role.StartsWith("context-",StringComparison.Ordinal)&&st.Points.Count>1).Select(st=>st.Points.ToArray()).ToArray();`
- [Строка 303](src/IsoGenerator.Core/SheetPlanner.cs#L303) `for(int step=0;step<contextPaths.Length;step++)`
- [Строка 305](src/IsoGenerator.Core/SheetPlanner.cs#L305) `int next=Array.FindIndex(contextPaths,path=>!usedContext.Contains(Array.IndexOf(contextPaths,path))&&(Math.Sqrt(Math.Pow(path[0].X-breakPoint.X,2)+Math.Pow(path[0].Y-breakPoint.Y,2))<.01||Math.Sqrt(Math.Pow(path[^1].X-brea…`
- [Строка 306](src/IsoGenerator.Core/SheetPlanner.cs#L306) `if(next<0)break;usedContext.Add(next);var path=contextPaths[next];`
- [Строка 307](src/IsoGenerator.Core/SheetPlanner.cs#L307) `bool begins=Math.Sqrt(Math.Pow(path[0].X-breakPoint.X,2)+Math.Pow(path[0].Y-breakPoint.Y,2))<.01;`
- [Строка 308](src/IsoGenerator.Core/SheetPlanner.cs#L308) `breakPoint=begins?path[^1]:path[0];`
- [Строка 312](src/IsoGenerator.Core/SheetPlanner.cs#L312) `if(directionLength<1e-8)throw new InvalidDataException("Не определено направление знака разрыва: "+part.Handle);`
- [Строка 316](src/IsoGenerator.Core/SheetPlanner.cs#L316) `strokes.Add(new(part.Handle,"continuation-break",new[]{BreakAt(-4,0),BreakAt(-1.5,0),BreakAt(-.7,1.5),BreakAt(.7,-1.5),BreakAt(1.5,0),BreakAt(4,0)}));`
- [Строка 326](src/IsoGenerator.Core/SheetPlanner.cs#L326) `if(marker.InformationType!=1)continue;`
- [Строка 330](src/IsoGenerator.Core/SheetPlanner.cs#L330) `if(marker.Direction is not {} direction||ends.Length!=2)`
- [Строка 335](src/IsoGenerator.Core/SheetPlanner.cs#L335) `var a=layout.Project(ends[0].Position,host.Handle);var b=layout.Project(ends[1].Position,host.Handle);`
- [Строка 336](src/IsoGenerator.Core/SheetPlanner.cs#L336) `var d=new ModelPoint(ends[1].Position.X-ends[0].Position.X,ends[1].Position.Y-ends[0].Position.Y,ends[1].Position.Z-ends[0].Position.Z);`
- [Строка 338](src/IsoGenerator.Core/SheetPlanner.cs#L338) `if(!double.IsFinite(sign)||Math.Abs(sign)<1e-8)`
- [Строка 343](src/IsoGenerator.Core/SheetPlanner.cs#L343) `double length=Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Y-a.Y,2));if(length<1e-6)continue;`
- [Строка 345](src/IsoGenerator.Core/SheetPlanner.cs#L345) `double t=Math.Clamp(ends[0].Position.DistanceTo(marker.Position)/Math.Max(1e-8,ends[0].Position.DistanceTo(ends[1].Position)),.15,.85);`
- [Строка 347](src/IsoGenerator.Core/SheetPlanner.cs#L347) `double size=Math.Min(4,length/5);`
- [Строка 348](src/IsoGenerator.Core/SheetPlanner.cs#L348) `strokes.Add(new(host.Handle,"flow-shaft",new[]{new SheetPoint(at.X-ux*size*2,at.Y-uy*size*2),new SheetPoint(at.X-ux*size,at.Y-uy*size)}));`
- [Строка 349](src/IsoGenerator.Core/SheetPlanner.cs#L349) `strokes.Add(new(host.Handle,"flow-arrow",new[]{new SheetPoint(at.X-ux*size-uy*size*.45,at.Y-uy*size+ux*size*.45),at,new SheetPoint(at.X-ux*size+uy*size*.45,at.Y-uy*size-ux*size*.45)}));`
- [Строка 354](src/IsoGenerator.Core/SheetPlanner.cs#L354) `if(DimensionRouting.CrossingCount(dimensions)>0)`
- [Строка 358](src/IsoGenerator.Core/SheetPlanner.cs#L358) `if(DimensionRouting.CrossingCount(dimensions)>0)warnings.Add("Пересечения размерных линий оформляются штатными разрывами DIMBREAK при выпуске DWG.");`
- [Строка 375](src/IsoGenerator.Core/SheetPlanner.cs#L375) `labels.Add(new("","title",new(75,385),lineNames[0]+$" — лист {sheetNumber} из {sheetCount}",4));`
- [Строка 376](src/IsoGenerator.Core/SheetPlanner.cs#L376) `labels.Add(new("","status",new(75,375),"КОНТРОЛЬНЫЙ ЛИСТ — НЕ ДЛЯ МОНТАЖА",3));`
- [Строка 378](src/IsoGenerator.Core/SheetPlanner.cs#L378) `if(Environment.GetEnvironmentVariable("ISO_PIPE_GAP_LENGTH") is {} rawGap&&double.TryParse(rawGap,NumberStyles.Float,CultureInfo.InvariantCulture,out double envGap)&&envGap>0)`
- [Строка 381](src/IsoGenerator.Core/SheetPlanner.cs#L381) `return new(lineNames[0],strokes,labels,dimensions,rows,warnings,snapshot.Parts.Count,symbols,snapshot.SourceDrawing,snapshot.Parts.ToDictionary(p=>p.Handle,p=>Get(p,"PnPGuid")),welds.Markers,compactLevel,pipeExtensions,qua…`

### SheetRunContinuity.cs

- [Строка 12](src/IsoGenerator.Core/SheetRunContinuity.cs#L12) `.Where(p=>p.Ends.Length==2&&p.Ends[0].Position.DistanceTo(p.Ends[1].Position)>1).ToArray();`
- [Строка 16](src/IsoGenerator.Core/SheetRunContinuity.cs#L16) `var a=axis.Ends[0].Position;var b=axis.Ends[1].Position;double length=a.DistanceTo(b);`
- [Строка 19](src/IsoGenerator.Core/SheetRunContinuity.cs#L19) `bool OnLine(ModelPoint p){double t=Station(p);return p.DistanceTo(new(a.X+ux*t,a.Y+uy*t,a.Z+uz*t))<=1;}`
- [Строка 21](src/IsoGenerator.Core/SheetRunContinuity.cs#L21) `.OrderBy(p=>(Station(p.Ends[0].Position)+Station(p.Ends[1].Position))/2).Select(p=>p.Part.Handle).ToArray();`
- [Строка 22](src/IsoGenerator.Core/SheetRunContinuity.cs#L22) `if(run.Length<3)continue;`
- [Строка 32](src/IsoGenerator.Core/SheetRunContinuity.cs#L32) `for(int i=first+1;first>=0&&i<last;i++)if(!included.Contains(run[i]))return true;`

### SheetSetIntegrity.cs

- [Строка 27](src/IsoGenerator.Core/SheetSetIntegrity.cs#L27) `if(split<=0||!int.TryParse(annotation.Text.AsSpan(split+separator.Length),out int target)||`
- [Строка 35](src/IsoGenerator.Core/SheetSetIntegrity.cs#L35) `if(endpoints.Count!=2||endpoints[0].Sheet!=endpoints[1].Target||endpoints[1].Sheet!=endpoints[0].Target)`
- [Строка 60](src/IsoGenerator.Core/SheetSetIntegrity.cs#L60) `if(!sheet.Scene.BomOnly&&SheetConnectivity.Components(sheet.Snapshot).Count!=1)throw new InvalidDataException("На листе "+sheet.Number+" обнаружены несвязанные фрагменты трубопровода.");`
- [Строка 71](src/IsoGenerator.Core/SheetSetIntegrity.cs#L71) `if(SheetComposition.Pieces(sheet.Scene).Any(bounds=>bounds.Left<paper.Left-.01||bounds.Right>paper.Right+.01||`
- [Строка 72](src/IsoGenerator.Core/SheetSetIntegrity.cs#L72) `bounds.Bottom<paper.Bottom-.01||bounds.Top>paper.Top+.01))`
- [Строка 76](src/IsoGenerator.Core/SheetSetIntegrity.cs#L76) `if(blocked.Length>0)`
- [Строка 80](src/IsoGenerator.Core/SheetSetIntegrity.cs#L80) `string.Join(", ",sizes.Select(size=>$"{size.Label} ({size.SourceId})"))+".",sizes[0].SourceId);`

### SheetSetPlanner.cs

- [Строка 3](src/IsoGenerator.Core/SheetSetPlanner.cs#L3) `public class SheetCapacityException(string message,string? sourceId=null,ModelPoint? modelStart=null,ModelPoint? modelEnd=null,double paperWidthOverflow=0,double paperHeightOverflow=0) : IOException(message) {public double…`
- [Строка 9](src/IsoGenerator.Core/SheetSetPlanner.cs#L9) `public static IReadOnlyList<PlannedSheet> Build(PipelineSnapshot source,IsoSymbolMap? symbolMap=null,int quarterTurns=0,bool excludeFlangeHardware=false,CancellationToken cancellationToken=default,IProgress<PlanningProgres…`
- [Строка 28](src/IsoGenerator.Core/SheetSetPlanner.cs#L28) `if(supportWarnings.Count>0)source=source with{Parts=parts,Warnings=source.Warnings.Concat(supportWarnings).ToArray()};`
- [Строка 30](src/IsoGenerator.Core/SheetSetPlanner.cs#L30) `if(missingProperties.Length>0)throw new InvalidDataException($"Не прочитаны свойства Plant 3D у {missingProperties.Length} компонентов (например, {string.Join(", ",missingProperties.Take(5))}). Откройте DWG из соответствую…`
- [Строка 36](src/IsoGenerator.Core/SheetSetPlanner.cs#L36) `if(validation.Errors.Count>0)throw new InvalidDataException(string.Join("\n",validation.Errors));`
- [Строка 39](src/IsoGenerator.Core/SheetSetPlanner.cs#L39) `var densityOrder=new[]{1,2,4,5,0,3};`
- [Строка 50](src/IsoGenerator.Core/SheetSetPlanner.cs#L50) `for(int depth=0;depth<=10;depth++)`
- [Строка 53](src/IsoGenerator.Core/SheetSetPlanner.cs#L53) `.OrderBy(h=>{var ports=parts[h].Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();return ports.Length==2?ports[0].Position.DistanceTo(ports[1].Position):double.MaxValue;}).ThenBy(h=>h,Stri…`
- [Строка 54](src/IsoGenerator.Core/SheetSetPlanner.cs#L54) `if(pipes.Length>0)return pipes[0];`
- [Строка 85](src/IsoGenerator.Core/SheetSetPlanner.cs#L85) `if(ends.Length!=2)return default;`
- [Строка 86](src/IsoGenerator.Core/SheetSetPlanner.cs#L86) `return IsometricProjection.Project(ends[1].Position,ends[0].Position,quarterTurns);`
- [Строка 96](src/IsoGenerator.Core/SheetSetPlanner.cs#L96) `return (Handle:handle,Angle:Math.Abs(candidate.X*axis.Y-candidate.Y*axis.X)/Math.Max(.001,length*size));`
- [Строка 98](src/IsoGenerator.Core/SheetSetPlanner.cs#L98) `.Where(candidate=>candidate.Angle>.3)`
- [Строка 100](src/IsoGenerator.Core/SheetSetPlanner.cs#L100) `.Select(candidate=>candidate.Handle).Take(4).Append(crossingPipe).ToArray();`
- [Строка 107](src/IsoGenerator.Core/SheetSetPlanner.cs#L107) `if(SheetConnectivity.Components(snapshot).Count>1)throw new SheetCapacityException("На листе допускается только один связный фрагмент трубопровода.");`
- [Строка 112](src/IsoGenerator.Core/SheetSetPlanner.cs#L112) `foreach(int level in quick?densityOrder.Take(2):densityOrder)`
- [Строка 116](src/IsoGenerator.Core/SheetSetPlanner.cs#L116) `var extension=requiredVentExtensions.TryGetValue(key,out var required)?new Dictionary<string,double>(required,StringComparer.OrdinalIgnoreCase):new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);double paperMa…`
- [Строка 118](src/IsoGenerator.Core/SheetSetPlanner.cs#L118) `for(int iteration=0;iteration<(quick?3:12);iteration++)`
- [Строка 125](src/IsoGenerator.Core/SheetSetPlanner.cs#L125) `if(Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1")Console.Error.WriteLine($"VENT parts={snapshot.Parts.Count} level={level} neck={ex.Pipe} extra={ex.Extra:F1}");`
- [Строка 128](src/IsoGenerator.Core/SheetSetPlanner.cs#L128) `catch(SheetCapacityException ex) when(ex.PaperWidthOverflow>0||ex.PaperHeightOverflow>0)`
- [Строка 130](src/IsoGenerator.Core/SheetSetPlanner.cs#L130) `paperMargin+=Math.Ceiling(Math.Max(ex.PaperWidthOverflow,ex.PaperHeightOverflow))+1;failure=ex;continue;`
- [Строка 132](src/IsoGenerator.Core/SheetSetPlanner.cs#L132) `catch(SheetCapacityException ex) when(ex.SourceId is null&&(paperMargin>0||extension.Count>0))`
- [Строка 135](src/IsoGenerator.Core/SheetSetPlanner.cs#L135) `paperMargin+=7;failure=ex;continue;`
- [Строка 139](src/IsoGenerator.Core/SheetSetPlanner.cs#L139) `if((Environment.GetEnvironmentVariable("ISO_LAYOUT_TRACE")=="1"||Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1"))Console.Error.WriteLine($"parts={snapshot.Parts.Count}, density={level}, extension={iteration},…`
- [Строка 141](src/IsoGenerator.Core/SheetSetPlanner.cs#L141) `?TransverseRepairs(snapshot,ex.SourceId,collision.LowerPipe).FirstOrDefault(handle=>congestionRetries.GetValueOrDefault(handle)<2)`
- [Строка 150](src/IsoGenerator.Core/SheetSetPlanner.cs#L150) `var a=graph.Nodes[e.A].Point;var b=graph.Nodes[e.B].Point;var mid=new ModelPoint((a.X+b.X)/2,(a.Y+b.Y)/2,(a.Z+b.Z)/2);`
- [Строка 151](src/IsoGenerator.Core/SheetSetPlanner.cs#L151) `double t=((mid.X-start.X)*dx+(mid.Y-start.Y)*dy+(mid.Z-start.Z)*dz)/Math.Max(l2,1e-12);`
- [Строка 152](src/IsoGenerator.Core/SheetSetPlanner.cs#L152) `return t>=-.001&&t<=1.001&&mid.DistanceTo(new(start.X+dx*t,start.Y+dy*t,start.Z+dz*t))<=1;`
- [Строка 157](src/IsoGenerator.Core/SheetSetPlanner.cs#L157) `if(localRetries>=4){failure=ex;break;}`
- [Строка 158](src/IsoGenerator.Core/SheetSetPlanner.cs#L158) `congestionRetries[extensionKey]=localRetries+1;`
- [Строка 159](src/IsoGenerator.Core/SheetSetPlanner.cs#L159) `extension[extensionKey]=extension.GetValueOrDefault(extensionKey)+10;failure=ex;continue;`
- [Строка 167](src/IsoGenerator.Core/SheetSetPlanner.cs#L167) `if(Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1")`
- [Строка 170](src/IsoGenerator.Core/SheetSetPlanner.cs#L170) `if(Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1")Console.Error.WriteLine($"CONGESTION parts={snapshot.Parts.Count} level={level} iteration={iteration} pipe={pipe}");`
- [Строка 174](src/IsoGenerator.Core/SheetSetPlanner.cs#L174) `if(retries>=3)break;`
- [Строка 175](src/IsoGenerator.Core/SheetSetPlanner.cs#L175) `congestionRetries[pipe]=retries+1;`
- [Строка 177](src/IsoGenerator.Core/SheetSetPlanner.cs#L177) `var runs=centered.Strokes.Where(stroke=>stroke.Role=="pipe"&&stroke.Points.Count>=2)`
- [Строка 181](src/IsoGenerator.Core/SheetSetPlanner.cs#L181) `var a=stroke.Points[0];var b=stroke.Points[^1];`
- [Строка 182](src/IsoGenerator.Core/SheetSetPlanner.cs#L182) `return Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Y-a.Y,2));`
- [Строка 184](src/IsoGenerator.Core/SheetSetPlanner.cs#L184) `double median=runs.Select(run=>run.Length).OrderBy(length=>length).ElementAtOrDefault(runs.Length/2);`
- [Строка 185](src/IsoGenerator.Core/SheetSetPlanner.cs#L185) `var donor=runs.Where(run=>run.Handle!=pipe&&extension.GetValueOrDefault(run.Handle)<=0&&run.Length>=Math.Max(4*adjustment,2*median))`
- [Строка 192](src/IsoGenerator.Core/SheetSetPlanner.cs#L192) `catch(SheetCapacityException ex){failure=ex;if((Environment.GetEnvironmentVariable("ISO_LAYOUT_TRACE")=="1"||Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1"))Console.Error.WriteLine($"parts={snapshot.Parts.Cou…`
- [Строка 202](src/IsoGenerator.Core/SheetSetPlanner.cs#L202) `required[failure.Pipe]=Math.Max(required.GetValueOrDefault(failure.Pipe),(current.PipeExtensions?.GetValueOrDefault(failure.Pipe)??0)+failure.Extra);`
- [Строка 207](src/IsoGenerator.Core/SheetSetPlanner.cs#L207) `int first=Array.IndexOf(densityOrder,Plan(snapshot).LayoutLevel)+1;`
- [Строка 223](src/IsoGenerator.Core/SheetSetPlanner.cs#L223) `if(SheetComposition.Pieces(scene).Count==0)return 0;`
- [Строка 234](src/IsoGenerator.Core/SheetSetPlanner.cs#L234) `Math.Pow(segment.B.X-segment.A.X,2)+Math.Pow(segment.B.Y-segment.A.Y,2)>longExtension*longExtension);`
- [Строка 242](src/IsoGenerator.Core/SheetSetPlanner.cs#L242) `Math.Abs(d.End.Y-d.Start.Y)>5*Math.Abs(d.End.X-d.Start.X)&&`
- [Строка 245](src/IsoGenerator.Core/SheetSetPlanner.cs#L245) `.Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();`
- [Строка 250](src/IsoGenerator.Core/SheetSetPlanner.cs#L250) `.SelectMany(stroke=>stroke.Points.Zip(stroke.Points.Skip(1),(a,b)=>new DimensionRouting.Segment(a,b,false))).ToArray();`
- [Строка 251](src/IsoGenerator.Core/SheetSetPlanner.cs#L251) `int crossings=0;`
- [Строка 252](src/IsoGenerator.Core/SheetSetPlanner.cs#L252) `for(int i=0;i<strokes.Length;i++)`
- [Строка 253](src/IsoGenerator.Core/SheetSetPlanner.cs#L253) `for(int j=0;j<i;j++)`
- [Строка 259](src/IsoGenerator.Core/SheetSetPlanner.cs#L259) `return Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));`
- [Строка 261](src/IsoGenerator.Core/SheetSetPlanner.cs#L261) `return (crossings,reaches.Count(length=>length>30.01),`
- [Строка 262](src/IsoGenerator.Core/SheetSetPlanner.cs#L262) `(int)Math.Round(reaches.Sum(length=>Math.Max(0,length-20))/5));`
- [Строка 267](src/IsoGenerator.Core/SheetSetPlanner.cs#L267) `if(pipes.Count>0&&PlanningExecution.CanTryOptionalVariant())`
- [Строка 278](src/IsoGenerator.Core/SheetSetPlanner.cs#L278) `foreach(double extra in new[]{20d,30d,40d,50d,60d,70d})`
- [Строка 287](src/IsoGenerator.Core/SheetSetPlanner.cs#L287) `(best.LayoutLevel,best.PaperMargin-20),`
- [Строка 288](src/IsoGenerator.Core/SheetSetPlanner.cs#L288) `(best.LayoutLevel,best.PaperMargin-40),`
- [Строка 289](src/IsoGenerator.Core/SheetSetPlanner.cs#L289) `(1,best.PaperMargin),`
- [Строка 290](src/IsoGenerator.Core/SheetSetPlanner.cs#L290) `(0,best.PaperMargin)})`
- [Строка 296](src/IsoGenerator.Core/SheetSetPlanner.cs#L296) `var drawingBounds=SheetComposition.Pieces(best).Count>0?SheetComposition.Bounds(best):default;`
- [Строка 297](src/IsoGenerator.Core/SheetSetPlanner.cs#L297) `if(fixedScale is >0&&drawingBounds.Width>SheetComposition.Paper.Width*.8&&`
- [Строка 298](src/IsoGenerator.Core/SheetSetPlanner.cs#L298) `drawingBounds.Height<SheetComposition.Paper.Height*.8)`
- [Строка 303](src/IsoGenerator.Core/SheetSetPlanner.cs#L303) `var runs=growthBase.Strokes.Where(stroke=>stroke.Role=="pipe"&&stroke.Points.Count>=2)`
- [Строка 306](src/IsoGenerator.Core/SheetSetPlanner.cs#L306) `double x=group.Sum(stroke=>Math.Abs(stroke.Points[^1].X-stroke.Points[0].X));`
- [Строка 307](src/IsoGenerator.Core/SheetSetPlanner.cs#L307) `double y=group.Sum(stroke=>Math.Abs(stroke.Points[^1].Y-stroke.Points[0].Y));`
- [Строка 310](src/IsoGenerator.Core/SheetSetPlanner.cs#L310) `var donor=runs.Where(run=>run.X>run.Y*1.3&&run.X>=45)`
- [Строка 312](src/IsoGenerator.Core/SheetSetPlanner.cs#L312) `foreach(var run in runs.Where(run=>run.Y>run.X*2.5&&run.Y>=10)`
- [Строка 313](src/IsoGenerator.Core/SheetSetPlanner.cs#L313) `.OrderByDescending(run=>run.Y).Take(2))`
- [Строка 314](src/IsoGenerator.Core/SheetSetPlanner.cs#L314) `foreach(double extra in new[]{30d,60d,90d})`
- [Строка 321](src/IsoGenerator.Core/SheetSetPlanner.cs#L321) `if(extra==60&&donor.Pipe is not null&&PlanningExecution.CanTryOptionalVariant())`
- [Строка 325](src/IsoGenerator.Core/SheetSetPlanner.cs#L325) `[donor.Pipe]=extensions.GetValueOrDefault(donor.Pipe)-20`
- [Строка 332](src/IsoGenerator.Core/SheetSetPlanner.cs#L332) `bool freeField=drawingBounds.Width<SheetComposition.Paper.Width*.9&&drawingBounds.Height<SheetComposition.Paper.Height*.9;`
- [Строка 333](src/IsoGenerator.Core/SheetSetPlanner.cs#L333) `if(best.LayoutLevel==3&&freeField)`
- [Строка 336](src/IsoGenerator.Core/SheetSetPlanner.cs#L336) `var longRuns=best.Strokes.Where(stroke=>stroke.Role=="pipe"&&stroke.Points.Count>=2)`
- [Строка 339](src/IsoGenerator.Core/SheetSetPlanner.cs#L339) `Math.Sqrt(Math.Pow(stroke.Points[^1].X-stroke.Points[0].X,2)+`
- [Строка 340](src/IsoGenerator.Core/SheetSetPlanner.cs#L340) `Math.Pow(stroke.Points[^1].Y-stroke.Points[0].Y,2)))))`
- [Строка 341](src/IsoGenerator.Core/SheetSetPlanner.cs#L341) `.OrderByDescending(run=>run.Length).Take(2).ToArray();`
- [Строка 343](src/IsoGenerator.Core/SheetSetPlanner.cs#L343) `foreach(double extra in new[]{20d,40d,60d})`
- [Строка 352](src/IsoGenerator.Core/SheetSetPlanner.cs#L352) `if(fixedScale is >0&&freeField)`
- [Строка 355](src/IsoGenerator.Core/SheetSetPlanner.cs#L355) `foreach(double factor in new[]{1.15,1.3,1.45})`
- [Строка 370](src/IsoGenerator.Core/SheetSetPlanner.cs#L370) `if(localPipe is not null&&Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1")`
- [Строка 376](src/IsoGenerator.Core/SheetSetPlanner.cs#L376) `if(Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1"&&localPipe is not null)`
- [Строка 380](src/IsoGenerator.Core/SheetSetPlanner.cs#L380) `if(priority<0||priority==0&&(leaderPriority<0||`
- [Строка 381](src/IsoGenerator.Core/SheetSetPlanner.cs#L381) `leaderPriority==0&&localPipe is null&&Footprint(candidate)>Footprint(best)*1.02))`
- [Строка 387](src/IsoGenerator.Core/SheetSetPlanner.cs#L387) `if(localPipe is not null&&Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1")`
- [Строка 399](src/IsoGenerator.Core/SheetSetPlanner.cs#L399) `bool HasInteriorGap(IEnumerable<string> handles)=>mandatory.Count==0&&runContinuity.HasInteriorGap(handles);`
- [Строка 428](src/IsoGenerator.Core/SheetSetPlanner.cs#L428) `return Finish(BomPagination.Apply(new[]{new PlannedSheet(1,1,source,expanded,raw)}));`
- [Строка 444](src/IsoGenerator.Core/SheetSetPlanner.cs#L444) `for(int i=0;i<2;i++)`
- [Строка 446](src/IsoGenerator.Core/SheetSetPlanner.cs#L446) `var marks=new List<SheetContinuation>();int mark=0;`
- [Строка 451](src/IsoGenerator.Core/SheetSetPlanner.cs#L451) `marks.Add(new(handle,port,"П"+(++mark),i==0?2:1));`
- [Строка 454](src/IsoGenerator.Core/SheetSetPlanner.cs#L454) `SheetComposition.Center(SheetPlanner.Build(pair[i],instrumentCalloutsOnly:instrumentCalloutsOnly,quarterTurns:quarterTurns,excludeFlangeHardware:excludeFlangeHardware,symbolMap:symbolMap,continuations:marks,sheetNumber:i+1…`
- [Строка 465](src/IsoGenerator.Core/SheetSetPlanner.cs#L465) `if(components.Count>1){foreach(var component in components)Split(Subset(component));return;}`
- [Строка 474](src/IsoGenerator.Core/SheetSetPlanner.cs#L474) `foreach(var p in snapshot.Parts.Where(p=>p.Properties.GetValueOrDefault("PnPClassName")=="Valve")){distances[p.Handle]=0;queue.Enqueue(p.Handle);}`
- [Строка 475](src/IsoGenerator.Core/SheetSetPlanner.cs#L475) `while(queue.Count>0){var h=queue.Dequeue();foreach(var n in adjacency[h])if(distances[n]>distances[h]+1){distances[n]=distances[h]+1;queue.Enqueue(n);}}`
- [Строка 488](src/IsoGenerator.Core/SheetSetPlanner.cs#L488) `while(queue.Count>0){var h=queue.Dequeue();foreach(var n in adjacency[h])`
- [Строка 496](src/IsoGenerator.Core/SheetSetPlanner.cs#L496) `int na=Physical(a),nb=Physical(b);if(na==0||nb==0)continue;`
- [Строка 499](src/IsoGenerator.Core/SheetSetPlanner.cs#L499) `int PreferredRank(HashSet<string> a)=>source.Connections.Any(c=>cutPolicy.Preferred.Contains(CutPreferences.Key(c))&&a.Contains(c.FirstHandle)!=a.Contains(c.SecondHandle))?0:1;`
- [Строка 506](src/IsoGenerator.Core/SheetSetPlanner.cs#L506) `for(int first=0;first<cuttable.Length;first++)for(int second=first+1;second<cuttable.Length;second++)`
- [Строка 512](src/IsoGenerator.Core/SheetSetPlanner.cs#L512) `while(pending.Count>0){string current=pending.Dequeue();foreach(var next in adjacency[current])if(!Blocked(current,next)&&a.Add(next))pending.Enqueue(next);}`
- [Строка 514](src/IsoGenerator.Core/SheetSetPlanner.cs#L514) `var b=handles.Except(a,StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);int na=Physical(a),nb=Physical(b);if(na==0||nb==0)continue;`
- [Строка 529](src/IsoGenerator.Core/SheetSetPlanner.cs#L529) `foreach(var c in ordered.Take(6))`
- [Строка 532](src/IsoGenerator.Core/SheetSetPlanner.cs#L532) `if((Environment.GetEnvironmentVariable("ISO_LAYOUT_TRACE")=="1"||Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1"))`
- [Строка 536](src/IsoGenerator.Core/SheetSetPlanner.cs#L536) `if(ordered.Length==0)throw new SheetCapacityException("Не удалось разделить линию по соединениям труб. Требуется ручное указание границы листов.");`
- [Строка 538](src/IsoGenerator.Core/SheetSetPlanner.cs#L538) `var balanced=ordered.Where(c=>c.Balance<=Physical(handles)*.6).ToArray();`
- [Строка 544](src/IsoGenerator.Core/SheetSetPlanner.cs#L544) `var options=pool.Concat(ordered).Take(12)`
- [Строка 565](src/IsoGenerator.Core/SheetSetPlanner.cs#L565) `if(explicitCuts.Count>0)`
- [Строка 571](src/IsoGenerator.Core/SheetSetPlanner.cs#L571) `while(remaining.Count>0){string root=remaining.First();var group=new HashSet<string>(StringComparer.OrdinalIgnoreCase){root};remaining.Remove(root);var q=new Queue<string>();q.Enqueue(root);while(q.Count>0)foreach(var next…`
- [Строка 572](src/IsoGenerator.Core/SheetSetPlanner.cs#L572) `if(groups.Count>1)`
- [Строка 581](src/IsoGenerator.Core/SheetSetPlanner.cs#L581) `var startMarkers=(source.IsoMarkers??Array.Empty<ModelIsoMarker>()).Where(m=>m.InformationType==5).ToArray();`
- [Строка 582](src/IsoGenerator.Core/SheetSetPlanner.cs#L582) `if(startMarkers.Length>1)throw new InvalidDataException("На линии несколько точек начала изометрии. Оставьте одну точку начала.");`
- [Строка 596](src/IsoGenerator.Core/SheetSetPlanner.cs#L596) `int start=Array.FindIndex(neighbors,adjacent=>adjacent.Count==1);`
- [Строка 597](src/IsoGenerator.Core/SheetSetPlanner.cs#L597) `if(startMarkers.Length==1)`
- [Строка 599](src/IsoGenerator.Core/SheetSetPlanner.cs#L599) `string handle=startMarkers[0].HostHandle;`
- [Строка 602](src/IsoGenerator.Core/SheetSetPlanner.cs#L602) `if(start<0)throw new InvalidDataException("Не найден элемент точки начала изометрии: "+handle);`
- [Строка 607](src/IsoGenerator.Core/SheetSetPlanner.cs#L607) `foreach(int root in new[]{Math.Max(0,start)}.Concat(Enumerable.Range(0,leaves.Count)))`
- [Строка 627](src/IsoGenerator.Core/SheetSetPlanner.cs#L627) `if(neighbors[first].Count<=1)return 0;`
- [Строка 630](src/IsoGenerator.Core/SheetSetPlanner.cs#L630) `queue.Enqueue((first,0));`
- [Строка 631](src/IsoGenerator.Core/SheetSetPlanner.cs#L631) `int depth=0;`
- [Строка 636](src/IsoGenerator.Core/SheetSetPlanner.cs#L636) `if(visited.Add(next))queue.Enqueue((next,depth+1));`
- [Строка 645](src/IsoGenerator.Core/SheetSetPlanner.cs#L645) `if((source.RequiredCuts??Array.Empty<Connection>()).Any(c=>included.Contains(c.FirstHandle)&&included.Contains(c.SecondHandle))||SheetConnectivity.Components(snapshot).Count!=1)return false;`
- [Строка 648](src/IsoGenerator.Core/SheetSetPlanner.cs#L648) `foreach(int level in densityOrder.Take(2))`
- [Строка 673](src/IsoGenerator.Core/SheetSetPlanner.cs#L673) `var neighbours=(from a in Enumerable.Range(0,leaves.Count) from b in Enumerable.Range(a+1,leaves.Count-a-1) orderby leaves[a].Parts.Count+leaves[b].Parts.Count select(A:a,B:b)).ToArray();`
- [Строка 701](src/IsoGenerator.Core/SheetSetPlanner.cs#L701) `int mark=0;var boundaries=new HashSet<string>(StringComparer.OrdinalIgnoreCase);`
- [Строка 708](src/IsoGenerator.Core/SheetSetPlanner.cs#L708) `continuations[a].Add(new(c.FirstHandle,c.FirstPort,label,b+1));`
- [Строка 709](src/IsoGenerator.Core/SheetSetPlanner.cs#L709) `continuations[b].Add(new(c.SecondHandle,c.SecondPort,label,a+1));`
- [Строка 716](src/IsoGenerator.Core/SheetSetPlanner.cs#L716) `for(int i=0;i<leaves.Count;i++)`
- [Строка 721](src/IsoGenerator.Core/SheetSetPlanner.cs#L721) `try{scene=SheetPlanner.Build(snapshot,instrumentCalloutsOnly:instrumentCalloutsOnly,quarterTurns:quarterTurns,excludeFlangeHardware:excludeFlangeHardware,symbolMap:symbolMap,continuations:continuations[i],sheetNumber:i+1,s…`
- [Строка 743](src/IsoGenerator.Core/SheetSetPlanner.cs#L743) `result.Add(new(i+1,leaves.Count,snapshot,scene,baseScenes[i]));`
- [Строка 755](src/IsoGenerator.Core/SheetSetPlanner.cs#L755) `for(int i=0;i<result.Count&&PlanningExecution.CanTryOptionalVariant();i++)`
- [Строка 764](src/IsoGenerator.Core/SheetSetPlanner.cs#L764) `var trial=SheetPlanner.Build(snapshot,quarterTurns,symbolMap,continuations[i],i+1,leaves.Count,`

### SlopeNotation.cs

- [Строка 10](src/IsoGenerator.Core/SlopeNotation.cs#L10) `var ports=part.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();if(ports.Length!=2)continue;`
- [Строка 11](src/IsoGenerator.Core/SlopeNotation.cs#L11) `var a=ports[0].Position;var b=ports[1].Position;`
- [Строка 13](src/IsoGenerator.Core/SlopeNotation.cs#L13) `if(horizontal>1&&Math.Abs(dz)>.001&&Math.Abs(dz)/horizontal<=.1)`
- [Строка 15](src/IsoGenerator.Core/SlopeNotation.cs#L15) `var high=dz<0?At(a):At(b);var low=dz<0?At(b):At(a);`
- [Строка 16](src/IsoGenerator.Core/SlopeNotation.cs#L16) `double ux=low.X-high.X,uy=low.Y-high.Y,span=Math.Sqrt(ux*ux+uy*uy);if(span<.01)continue;ux/=span;uy/=span;`
- [Строка 17](src/IsoGenerator.Core/SlopeNotation.cs#L17) `double nx=-uy,ny=ux;if(ny<0){nx=-nx;ny=-ny;}`
- [Строка 18](src/IsoGenerator.Core/SlopeNotation.cs#L18) `var center=new SheetPoint((high.X+low.X)/2+nx*6,(high.Y+low.Y)/2+ny*6);`
- [Строка 19](src/IsoGenerator.Core/SlopeNotation.cs#L19) `double half=Math.Min(4,span/4);`
- [Строка 21](src/IsoGenerator.Core/SlopeNotation.cs#L21) `var upperTail=new SheetPoint(tail.X+nx*1.8,tail.Y+ny*1.8);`
- [Строка 23](src/IsoGenerator.Core/SlopeNotation.cs#L23) `double rotation=Math.Atan2(uy,ux);if(rotation>Math.PI/2)rotation-=Math.PI;if(rotation<=-Math.PI/2)rotation+=Math.PI;`
- [Строка 24](src/IsoGenerator.Core/SlopeNotation.cs#L24) `labels.Add(new(part.Handle,"slope-value",new(center.X+nx*2.8,center.Y+ny*2.8),"1:"+(horizontal/Math.Abs(dz)).ToString("0.######",CultureInfo.GetCultureInfo("ru-RU")),Rotation:rotation));`
- [Строка 27](src/IsoGenerator.Core/SlopeNotation.cs#L27) `if(length<.001||Math.Max(Math.Abs(dx),Math.Max(Math.Abs(dy),Math.Abs(dz)))/length>Math.Cos(.01*Math.PI/180))continue;`
- [Строка 29](src/IsoGenerator.Core/SlopeNotation.cs#L29) `var reference=Math.Abs(dz)>.001?new ModelPoint(dx,dy,0):Math.Abs(dx)>=Math.Abs(dy)?new ModelPoint(dx,0,0):new ModelPoint(0,dy,0);`
- [Строка 31](src/IsoGenerator.Core/SlopeNotation.cs#L31) `double angle=Math.Atan2(Math.Abs(dz)>.001?Math.Abs(dz):Math.Min(Math.Abs(dx),Math.Abs(dy)),refLength)*180/Math.PI;`
- [Строка 34](src/IsoGenerator.Core/SlopeNotation.cs#L34) `double scale=Math.Sqrt(Math.Pow(pb.X-pa.X,2)+Math.Pow(pb.Y-pa.Y,2))/projectedLength;`
- [Строка 35](src/IsoGenerator.Core/SlopeNotation.cs#L35) `const double fraction=1;`
- [Строка 39](src/IsoGenerator.Core/SlopeNotation.cs#L39) `if(Math.Abs((corner.X-start.X)*(tip.Y-start.Y)-(corner.Y-start.Y)*(tip.X-start.X))<.1)continue;`
- [Строка 43](src/IsoGenerator.Core/SlopeNotation.cs#L43) `double square=Math.Min(2.5,Math.Min(la,lb)*.2);`
- [Строка 50](src/IsoGenerator.Core/SlopeNotation.cs#L50) `double alpha=angle*Math.PI/180;`
- [Строка 51](src/IsoGenerator.Core/SlopeNotation.cs#L51) `double radius=Math.Min(10,Math.Min(la,lb)*.5);`
- [Строка 57](src/IsoGenerator.Core/SlopeNotation.cs#L57) `var arc=Enumerable.Range(0,33).Select(i=>Arc(alpha*i/32)).ToArray();`
- [Строка 62](src/IsoGenerator.Core/SlopeNotation.cs#L62) `const double arrowLength=2.5;double half=arrowLength*Math.Tan(10*Math.PI/180);`
- [Строка 67](src/IsoGenerator.Core/SlopeNotation.cs#L67) `Arrow(arc[0],arc[1]);Arrow(arc[^1],arc[^2]);`
- [Строка 69](src/IsoGenerator.Core/SlopeNotation.cs#L69) `int hatchCount=Math.Max(1,(int)Math.Floor(hatchSpan/5));`
- [Строка 70](src/IsoGenerator.Core/SlopeNotation.cs#L70) `for(int i=1;i<=hatchCount;i++)`
- [Строка 72](src/IsoGenerator.Core/SlopeNotation.cs#L72) `double t=(double)i/(hatchCount+1);`
- [Строка 77](src/IsoGenerator.Core/SlopeNotation.cs#L77) `var labelAnchor=Arc(alpha/2);`
- [Строка 78](src/IsoGenerator.Core/SlopeNotation.cs#L78) `labels.Add(new(part.Handle,"slope-angle",new(labelAnchor.X+2,labelAnchor.Y+3),angle.ToString("0.#",CultureInfo.InvariantCulture)+"°"));`

### SymbolFootprint.cs

- [Строка 7](src/IsoGenerator.Core/SymbolFootprint.cs#L7) `(double x0,double x1,double y0,double y1)=g.Variant.StartsWith("end-",StringComparison.Ordinal)?(-.027,0,-.25,.25):`
- [Строка 8](src/IsoGenerator.Core/SymbolFootprint.cs#L8) `g.Variant=="operator"?(-.25,.25,0,.625):g.Block=="ФланецWN"?(-.15625,.15625,-.25,.25):`
- [Строка 9](src/IsoGenerator.Core/SymbolFootprint.cs#L9) `g.Block=="Колено"?(-.5,0,0,.5):g.Block=="Тройник"?(-.5,.5,0,.5):(-.5,.5,-.25,.25);`

### TapDimensionReferences.cs

- [Строка 13](src/IsoGenerator.Core/TapDimensionReferences.cs#L13) `var ends=pipe.Ports.Where(p=>p.Name.StartsWith("S",StringComparison.OrdinalIgnoreCase)).ToArray();if(ends.Length!=2)continue;`
- [Строка 14](src/IsoGenerator.Core/TapDimensionReferences.cs#L14) `var a=ends[0].Position;var b=ends[1].Position;double dx=b.X-a.X,dy=b.Y-a.Y,dz=b.Z-a.Z,l2=dx*dx+dy*dy+dz*dz;if(l2<1e-10)continue;`
- [Строка 18](src/IsoGenerator.Core/TapDimensionReferences.cs#L18) `if(t<0||t>1)continue;var axis=new ModelPoint(a.X+t*dx,a.Y+t*dy,a.Z+t*dz);`

### WeldPlanner.cs

- [Строка 21](src/IsoGenerator.Core/WeldPlanner.cs#L21) `if(ea.Length==0||eb.Length==0){warnings.Add($"{source}: missing per-port end condition at {ah}:{ap}/{bh}:{bp}; weld not guessed");return;}`
- [Строка 25](src/IsoGenerator.Core/WeldPlanner.cs#L25) `if(pa.Position.DistanceTo(pb.Position)>1){warnings.Add($"{source}: welded ports are not coincident; weld not drawn");return;}`
- [Строка 28](src/IsoGenerator.Core/WeldPlanner.cs#L28) `var pos=new ModelPoint((pa.Position.X+pb.Position.X)/2,(pa.Position.Y+pb.Position.Y)/2,(pa.Position.Z+pb.Position.Z)/2);`
- [Строка 34](src/IsoGenerator.Core/WeldPlanner.cs#L34) `if(ends.Length==2)`
- [Строка 36](src/IsoGenerator.Core/WeldPlanner.cs#L36) `var u=ends[0].Position;var v=ends[1].Position;`
- [Строка 38](src/IsoGenerator.Core/WeldPlanner.cs#L38) `if(l2>1e-10){double t=((port.Position.X-u.X)*dx+(port.Position.Y-u.Y)*dy+(port.Position.Z-u.Z)*dz)/l2;pos=new(u.X+t*dx,u.Y+t*dy,u.Z+t*dz);}`
- [Строка 54](src/IsoGenerator.Core/WeldPlanner.cs#L54) `if(ends.Length==2)Candidate(joint.Handle,ends[0].Handle,ends[0].Port,ends[1].Handle,ends[1].Port);`

### OutputSetPublisher.cs

- [Строка 8](src/IsoGenerator.Publishing/OutputSetPublisher.cs#L8) `if(staged.Count==0||staged.Count!=outputs.Count)throw new ArgumentException("Неверный состав выпуска.");`
- [Строка 11](src/IsoGenerator.Publishing/OutputSetPublisher.cs#L11) `if(obsolete.Length>0&&!overwrite)throw new IOException("Retiring old sheets requires approved replacement.");`
- [Строка 21](src/IsoGenerator.Publishing/OutputSetPublisher.cs#L21) `for(int i=0;i<staged.Count;i++)`
- [Строка 48](src/IsoGenerator.Publishing/OutputSetPublisher.cs#L48) `if(overwrite)for(int i=0;i<targets.Length;i++)if(File.Exists(targets[i]))`
- [Строка 49](src/IsoGenerator.Publishing/OutputSetPublisher.cs#L49) `{string backup=Path.Combine(backupDirectory,i.ToString("000")+"-"+Path.GetFileName(targets[i]));File.Copy(targets[i],backup,false);backups[i]=backup;}`
- [Строка 51](src/IsoGenerator.Publishing/OutputSetPublisher.cs#L51) `{string backup=Path.Combine(backupDirectory,"retired-"+retiredBackups.Count.ToString("000")+"-"+Path.GetFileName(old));File.Copy(old,backup,false);retiredBackups[old]=backup;}`
- [Строка 52](src/IsoGenerator.Publishing/OutputSetPublisher.cs#L52) `for(int i=0;i<targets.Length;i++)`
- [Строка 89](src/IsoGenerator.Publishing/OutputSetPublisher.cs#L89) `if(errors.Count>1)throw new AggregateException("Ошибка публикации. Резервные копии: "+backupDirectory,errors);`
- [Строка 126](src/IsoGenerator.Publishing/OutputSetPublisher.cs#L126) `streams.Add(new FileStream(Path.Combine(directory,".specgen-iso-publish.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose));`

### PdfSetPublisher.cs

- [Строка 8](src/IsoGenerator.Publishing/PdfSetPublisher.cs#L8) `if(sheets.Count==0)throw new InvalidDataException("Нет листов для PDF.");`
- [Строка 14](src/IsoGenerator.Publishing/PdfSetPublisher.cs#L14) `if(source.PageCount!=1)throw new InvalidDataException("Ожидался один лист в промежуточном PDF: "+sheet);`
- [Строка 15](src/IsoGenerator.Publishing/PdfSetPublisher.cs#L15) `document.AddPage(source.Pages[0]);`
