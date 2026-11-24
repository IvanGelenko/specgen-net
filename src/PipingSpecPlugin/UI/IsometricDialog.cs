using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Text.Json;
using IsoGenerator.Core;
using IsoGenerator.Publishing;

namespace PipingSpecPlugin.UI;
internal sealed record IsometricLineChoice(string Key,string Diameter,int Count);
internal sealed record IsoDocumentDetails(string Valves="",string Supports="",string TitlePath="",string ContentsPath="");
internal sealed class IsometricDialog : Form
{
    readonly IReadOnlyList<IsometricLineChoice> lines;
    readonly string drawingPath;
    readonly CheckBox recalculateLine=new();
    readonly HashSet<string> checkedLines=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,bool> rememberedChoices=new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> savedLayouts=new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> publishedLines=new(StringComparer.OrdinalIgnoreCase);
    bool rebuildingRows,documentsInitialized;
    string loadedLine;
    public bool RememberLayout(string line)=>rememberedChoices.GetValueOrDefault(line);
    public IReadOnlyList<string> SelectedLines=>lines.Where(line=>checkedLines.Contains(line.Key)).Select(line=>line.Key).ToArray();
    public bool RecalculateLine=>recalculateLine.Checked;
    readonly TextBox filter=new(),folder=new();
    readonly DataGridView list=new();
    const int GenerateColumn=0,RememberColumn=5;
    readonly Button generate=new();
    readonly Label details=new();
    readonly CheckBox overrideNorth=new();
    readonly CheckBox excludeHardware=new(),ignoreBreaks=new();
    bool instrumentCalloutsOnly=true;
    readonly TextBox template=new();
    public IsoDocumentDetails Documents {get;private set;}
    public IsometricTitleDetails TitleDetails {get;private set;}
    static readonly string assets=Path.Combine(Path.GetDirectoryName(typeof(IsometricDialog).Assembly.Location)!,"assets");
    public static string DefaultContentsTemplate=>Path.Combine(assets,"iso-contents-template.docx");
    static string TitleExample=>Path.Combine(assets,"iso-title-template.docx");
    const decimal DefaultMinimumPipeLength=10;
    const decimal MinimumPipeLengthLimit=1;
    const decimal MaximumPipeLengthLimit=100000;
    readonly NumericUpDown scale=new();
    public IsoAdvancedSettings AdvancedSettings {get;private set;}
    public double MinimumPipeLength {get;private set;}
    public bool InstrumentCalloutsOnly=>instrumentCalloutsOnly;
    public bool IgnoreBreakPoints=>ignoreBreaks.Checked;
    public string TemplatePath=>template.Text.Trim();
    public double SymbolScale=>(double)scale.Value;
    public bool ExcludeFlangeHardware=>excludeHardware.Checked;
    readonly ComboBox north=new();
    public bool AutomaticView=>!overrideNorth.Checked;
    public int QuarterTurns=>overrideNorth.Checked?new[]{0,3,1,2}[north.SelectedIndex]:0;
    public string OutputDirectory=>folder.Text.Trim();
    public IsometricDialog(IReadOnlyList<IsometricLineChoice> choices,string drawing,string outputDirectory,string templatePath="",double symbolScale=10,double lastMinimumPipeLength=10,IsoAdvancedSettings advancedSettings=null,IsoDocumentDetails documents=null,IReadOnlyCollection<string> selectedLines=null)
    {
        Documents=documents??new IsoDocumentDetails();
        if(File.Exists(Documents.TitlePath))
            try{TitleDetails=IsometricDocumentTemplates.ReadTitle(Documents.TitlePath);}catch(Exception){}
        documentsInitialized=Documents!=new IsoDocumentDetails();
        AdvancedSettings=advancedSettings??new IsoAdvancedSettings();
        MinimumPipeLength=NormalizePipeLength(lastMinimumPipeLength);
        lines=choices;drawingPath=drawing;
        foreach(var choice in choices)
        {
            if(IsoGenerator.AutoCAD.RememberedIsoOutput.Exists(drawing,choice.Key))savedLayouts.Add(choice.Key);
            if(IsoGenerator.AutoCAD.RememberedIsoOutput.WasPublished(drawing,choice.Key))publishedLines.Add(choice.Key);
            rememberedChoices[choice.Key]=IsoGenerator.AutoCAD.RememberedIsoOutput.PreferRememberLayout(drawing,choice.Key);
        }
        if(selectedLines is not null)
        {
            checkedLines.UnionWith(selectedLines);
            checkedLines.IntersectWith(choices.Select(choice=>choice.Key));
        }
        else if(choices.Count==1)checkedLines.Add(choices[0].Key);
        Text="SpecGen — Изометрии";Font=new Font("Segoe UI",9.5f);ClientSize=new Size(760,780);MinimumSize=new Size(776,815);
        StartPosition=FormStartPosition.CenterParent;MinimizeBox=false;MaximizeBox=false;ShowInTaskbar=false;
        Controls.Add(new Label{Text="Отметьте линии для изометрии",Left=18,Top=15,Width=700,Height=27,Font=new Font(Font.FontFamily,13,FontStyle.Bold)});
        Controls.Add(new Label{Text="Текущий чертёж: "+Path.GetFileName(drawing),Left=18,Top=48,Width=710,Height=22,AutoEllipsis=true});
        filter.SetBounds(18,80,724,27);filter.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;filter.PlaceholderText="Поиск по номеру линии";filter.TextChanged+=(s,e)=>Fill();Controls.Add(filter);
        list.SetBounds(18,119,724,246);list.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;
        list.AllowUserToAddRows=false;list.AllowUserToDeleteRows=false;list.AllowUserToResizeRows=false;
        list.RowHeadersVisible=false;list.MultiSelect=false;list.EditMode=DataGridViewEditMode.EditOnEnter;
        list.BackgroundColor=SystemColors.Window;
        list.Columns.Add(new DataGridViewCheckBoxColumn{HeaderText="Выпуск",Width=70});
        list.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Номер линии",ReadOnly=true,AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,MinimumWidth=180});
        list.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Ду",ReadOnly=true,Width=64});
        list.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Деталей",ReadOnly=true,Width=76});
        list.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Компоновка",ReadOnly=true,Width=112});
        list.Columns.Add(new DataGridViewCheckBoxColumn{HeaderText="Запомнить",Width=108});
        foreach(DataGridViewColumn column in list.Columns)column.SortMode=DataGridViewColumnSortMode.NotSortable;
        list.CurrentCellDirtyStateChanged+=(_,_)=>{if(list.IsCurrentCellDirty)list.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        list.CellValueChanged+=(_,eventArgs)=>LineOptionChanged(eventArgs);
        Controls.Add(list);
        details.SetBounds(18,380,724,30);details.Anchor=AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;details.ForeColor=Color.DimGray;Controls.Add(details);
        Controls.Add(new Label{Text="Шаблон DWG / DWT",Left=18,Top=419,Width=135,Height=24,Anchor=AnchorStyles.Bottom|AnchorStyles.Left});
        template.SetBounds(155,417,471,28);template.Anchor=AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;template.Text=templatePath;template.TextChanged+=(s,e)=>UpdateSelection();Controls.Add(template);
        var chooseTemplate=new Button{Text="Обзор…",Left=638,Top=415,Width=104,Height=30,Anchor=AnchorStyles.Bottom|AnchorStyles.Right};
        chooseTemplate.Click+=(s,e)=>{using var picker=new OpenFileDialog{Title="Выберите локальную копию шаблона изометрии А2",Filter="Шаблон AutoCAD (*.dwg;*.dwt)|*.dwg;*.dwt",CheckFileExists=true};if(File.Exists(TemplatePath))picker.FileName=TemplatePath;if(picker.ShowDialog(this)==DialogResult.OK)template.Text=picker.FileName;};Controls.Add(chooseTemplate);
        Controls.Add(new Label{Text="Масштаб элементов",Left=18,Top=455,Width=145,Height=24,Anchor=AnchorStyles.Bottom|AnchorStyles.Left});
        scale.SetBounds(170,453,120,28);scale.DecimalPlaces=4;scale.Minimum=.0001m;scale.Maximum=100000;scale.Increment=1;scale.Value=double.IsFinite(symbolScale)&&symbolScale>0&&symbolScale<=100000?(decimal)symbolScale:10;scale.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;Controls.Add(scale);
        ignoreBreaks.Text="Игнорировать точки разрыва на модели";ignoreBreaks.SetBounds(18,569,724,27);ignoreBreaks.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;Controls.Add(ignoreBreaks);
        overrideNorth.Text="Ракурс вручную (иначе — автоподбор для линии)";overrideNorth.SetBounds(18,497,425,27);overrideNorth.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;Controls.Add(overrideNorth);
        north.SetBounds(450,497,292,28);north.DropDownStyle=ComboBoxStyle.DropDownList;north.Items.AddRange(new object[]{"↖ Левый верхний","↗ Правый верхний","↙ Левый нижний","↘ Правый нижний"});north.SelectedIndex=0;north.Enabled=false;north.Anchor=AnchorStyles.Bottom|AnchorStyles.Right;Controls.Add(north);
        overrideNorth.CheckedChanged+=(s,e)=>north.Enabled=overrideNorth.Checked;
        excludeHardware.Text="Не включать фланцы, прокладки и крепёж в спецификацию";excludeHardware.Checked=true;excludeHardware.SetBounds(18,533,724,27);excludeHardware.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;Controls.Add(excludeHardware);
        recalculateLine.Text="Пересчитать выбранные линии";
        recalculateLine.SetBounds(18,628,240,25);
        var documentsButton=new Button{Text="Реквизиты документов…",Left=274,Top=626,Width=234,Height=29,
            Anchor=AnchorStyles.Bottom|AnchorStyles.Right};
        documentsButton.Click+=(_,_)=>EditDocuments();
        Controls.Add(documentsButton);
        var advanced=new Button{Text="Глубокие настройки…",Left=520,Top=626,Width=222,Height=29,
            Anchor=AnchorStyles.Bottom|AnchorStyles.Right};
        advanced.Click+=(_,_)=>EditAdvancedSettings();
        Controls.Add(advanced);
        recalculateLine.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
        Controls.Add(recalculateLine);
        Controls.Add(new Label{Text="Папка выпуска (DWG, PDF, DOCX)",Left=18,Top=659,Width=240,Height=22,Anchor=AnchorStyles.Bottom|AnchorStyles.Left});
        folder.SetBounds(18,682,608,27);folder.Text=outputDirectory;folder.Anchor=AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;folder.TextChanged+=(s,e)=>UpdateSelection();Controls.Add(folder);
        var browse=new Button{Text="Обзор…",Left=638,Top=680,Width=104,Height=30,Anchor=AnchorStyles.Bottom|AnchorStyles.Right};
        browse.Click+=(s,e)=>{using var picker=new FolderBrowserDialog{Description="Папка для изометрии",UseDescriptionForTitle=true,SelectedPath=Directory.Exists(OutputDirectory)?OutputDirectory:Path.GetDirectoryName(drawing)};if(picker.ShowDialog(this)==DialogResult.OK)folder.Text=picker.SelectedPath;};Controls.Add(browse);
        generate.SetBounds(482,722,152,34);generate.Text="Создать изометрии";generate.Anchor=AnchorStyles.Bottom|AnchorStyles.Right;generate.Click+=(s,e)=>Accept();Controls.Add(generate);
        var cancel=new Button{Text="Отмена",Left=646,Top=722,Width=96,Height=34,DialogResult=DialogResult.Cancel,Anchor=AnchorStyles.Bottom|AnchorStyles.Right};Controls.Add(cancel);
        AcceptButton=generate;CancelButton=cancel;Fill();
    }
    void EditAdvancedSettings()
    {
        var draft=JsonSerializer.Deserialize<IsoAdvancedSettings>(JsonSerializer.Serialize(AdvancedSettings));
        using var window=new Form{Text="Глубокие настройки изометрии",StartPosition=FormStartPosition.CenterParent,
            ClientSize=new Size(650,580),MinimumSize=new Size(550,460),ShowInTaskbar=false};
        var pipeSettings=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(8),WrapContents=false};
        var pipeLengthLabel=new Label{Text="Минимальная длина трубы на листе, мм",AutoSize=true,Anchor=AnchorStyles.Left};
        var pipeLength=new NumericUpDown{Width=100,DecimalPlaces=1,Minimum=MinimumPipeLengthLimit,
            Maximum=MaximumPipeLengthLimit,Value=(decimal)MinimumPipeLength,AccessibleName=pipeLengthLabel.Text};
        pipeSettings.Controls.AddRange(new Control[]{pipeLengthLabel,pipeLength});
        var instrumentCallouts=new CheckBox{Text="КИП: только выноска с ИД, без символа и позиции в спецификации",
            Checked=instrumentCalloutsOnly,Dock=DockStyle.Top,Height=30,Padding=new Padding(8,0,0,0)};
        var grid=new PropertyGrid{Dock=DockStyle.Fill,SelectedObject=draft,ToolbarVisible=false};
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=42,FlowDirection=FlowDirection.RightToLeft};
        var save=new Button{Text="Сохранить",AutoSize=true};
        var cancel=new Button{Text="Отмена",AutoSize=true,DialogResult=DialogResult.Cancel};
        var reset=new Button{Text="По умолчанию",AutoSize=true};
        reset.Click+=(_,_)=>{
            draft=new IsoAdvancedSettings();
            grid.SelectedObject=draft;
            pipeLength.Value=DefaultMinimumPipeLength;
            instrumentCallouts.Checked=true;
        };
        save.Click+=(_,_)=>{
            try
            {
                draft.Validate();
                AdvancedSettings=draft;
                instrumentCalloutsOnly=instrumentCallouts.Checked;
                MinimumPipeLength=(double)pipeLength.Value;
                window.DialogResult=DialogResult.OK;
            }
            catch(System.ComponentModel.DataAnnotations.ValidationException error)
            {MessageBox.Show(window,error.Message,window.Text,MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        };
        buttons.Controls.AddRange(new Control[]{save,cancel,reset});
        window.Controls.Add(grid);window.Controls.Add(buttons);window.Controls.Add(instrumentCallouts);window.Controls.Add(pipeSettings);
        window.CancelButton=cancel;
        window.ShowDialog(this);
    }

    static double NormalizePipeLength(double value)=>
        value>=(double)MinimumPipeLengthLimit&&value<=(double)MaximumPipeLengthLimit?value:(double)DefaultMinimumPipeLength;
    void EditDocuments()
    {
        using var window=new Form{Text="Реквизиты документов",Font=Font,StartPosition=FormStartPosition.CenterParent,
            ClientSize=new Size(760,415),FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false,ShowInTaskbar=false};
        TextBox AddField(string caption,string value,int top,bool readOnly=false,int height=26,int width=534)
        {
            var input=new TextBox{Left=205,Top=top,Width=width,Height=height,Text=value,ReadOnly=readOnly,AccessibleName=caption,
                Multiline=height>30,ScrollBars=height>30?ScrollBars.Vertical:ScrollBars.None};
            window.Controls.Add(new Label{Text=caption,Left=18,Top=top+3,AutoSize=true});
            window.Controls.Add(input);
            return input;
        }
        Button AddButton(string caption,int left,int top,int width,Action action)
        {
            var button=new Button{Text=caption,Left=left,Top=top,Width=width,Height=30};
            button.Click+=(_,_)=>action();
            window.Controls.Add(button);
            return button;
        }
        var valves=AddField("Документ по арматуре",Documents.Valves,18);
        var supports=AddField("Документ по опорам",Documents.Supports,54);
        var title=AddField("Заполненный титул DOCX",Documents.TitlePath,92,true,width:422);
        var contents=AddField("Шаблон содержания DOCX",Documents.ContentsPath,128,true,width:422);
        contents.PlaceholderText="Встроенный шаблон";
        var number=AddField("Номер документа",TitleDetails?.DocumentNumber??"",169,true);
        var objectName=AddField("Объект",TitleDetails?.ObjectName??"",205,true);
        var unit=AddField("Установка",TitleDetails?.UnitName??"",241,true,56);
        IsometricTitleDetails parsed=TitleDetails;
        IsoDocumentDetails Capture()=>new(valves.Text.Trim(),supports.Text.Trim(),title.Text,contents.Text);

        void WithErrorMessage(Action action)
        {
            try{action();}catch(Exception ex){MessageBox.Show(window,ex.Message,window.Text,MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        }
        void LoadTitle(string path)
        {
            var data=IsometricDocumentTemplates.ReadTitle(path);
            _=DrawingFileNames.DocumentStem(data.DocumentNumber);
            parsed=data;
            title.Text=path;
            number.Text=data.DocumentNumber;
            objectName.Text=data.ObjectName;
            unit.Text=data.UnitName;
        }
        void ChooseTemplate(TextBox target,bool isTitle)
        {
            using var chooser=new OpenFileDialog{Title=isTitle?"Выберите заполненный титульный лист":"Выберите шаблон содержания",
                Filter="Документы Word (*.docx)|*.docx",CheckFileExists=true};
            if(File.Exists(target.Text))chooser.FileName=target.Text;
            if(chooser.ShowDialog(window)!=DialogResult.OK)return;
            if(isTitle)WithErrorMessage(()=>LoadTitle(chooser.FileName));else target.Text=chooser.FileName;
        }
        AddButton("Обзор…",635,90,104,()=>ChooseTemplate(title,true));
        AddButton("Обзор…",635,126,104,()=>ChooseTemplate(contents,false));
        AddButton("Встроенное содержание",18,311,181,()=>contents.Clear());
        AddButton("Сохранить образец титула…",205,311,206,()=>
        {
            using var chooser=new SaveFileDialog{Filter="Документ Word (*.docx)|*.docx",FileName="Титульный лист.docx"};
            if(chooser.ShowDialog(window)==DialogResult.OK)
                WithErrorMessage(()=>File.Copy(TitleExample,chooser.FileName,overwrite:true));
        });
        AddButton("Сохранить образец содержания…",422,311,251,()=>
        {
            using var chooser=new SaveFileDialog{Filter="Документ Word (*.docx)|*.docx",FileName="Содержание.docx"};
            if(chooser.ShowDialog(window)==DialogResult.OK)
                WithErrorMessage(()=>File.Copy(DefaultContentsTemplate,chooser.FileName,overwrite:true));
        });
        AddButton("Загрузить реквизиты…",18,364,176,()=>
        {
            using var chooser=new OpenFileDialog{Filter="Реквизиты изометрий (*.iso-profile.json)|*.iso-profile.json",CheckFileExists=true};
            if(chooser.ShowDialog(window)!=DialogResult.OK)return;
            WithErrorMessage(()=>
            {
                var profile=IsoDocumentProfileFile.Load(chooser.FileName);
                LoadTitle(profile.TitlePath);
                valves.Text=profile.Valves;
                supports.Text=profile.Supports;
                contents.Text=profile.ContentsPath;
            });
        });
        AddButton("Сохранить реквизиты…",205,364,181,()=>
        {
            if(parsed is null){MessageBox.Show(window,"Выберите заполненный титульный лист.",window.Text);return;}
            using var chooser=new SaveFileDialog{Filter="Реквизиты изометрий (*.iso-profile.json)|*.iso-profile.json",
                FileName=DrawingFileNames.DocumentStem(parsed.DocumentNumber)+".iso-profile.json"};
            if(chooser.ShowDialog(window)==DialogResult.OK)WithErrorMessage(()=>IsoDocumentProfileFile.Save(chooser.FileName,Capture()));
        });
        var save=AddButton("Сохранить",522,364,103,()=>WithErrorMessage(()=>
        {
            if(File.Exists(title.Text))LoadTitle(title.Text);
            window.DialogResult=DialogResult.OK;
        }));
        var cancel=new Button{Text="Отмена",Left=635,Top=364,Width=104,Height=30,DialogResult=DialogResult.Cancel};
        window.Controls.Add(cancel);
        window.AcceptButton=save;window.CancelButton=cancel;
        if(window.ShowDialog(this)!=DialogResult.OK)return;
        Documents=Capture();
        TitleDetails=parsed;
        documentsInitialized=true;
        UpdateSelection();
    }

    void Fill()
    {
        rebuildingRows=true;
        try
        {
            list.Rows.Clear();
            string query=filter.Text.Trim();
            foreach(var line in lines.Where(line=>line.Key.Contains(query,StringComparison.CurrentCultureIgnoreCase)))
            {
                string status=savedLayouts.Contains(line.Key)?"Сохранена":publishedLines.Contains(line.Key)?"Выпущена":"—";
                int index=list.Rows.Add(checkedLines.Contains(line.Key),line.Key,line.Diameter??"",line.Count,status,RememberLayout(line.Key));
                list.Rows[index].Tag=line.Key;
            }
        }
        finally{rebuildingRows=false;}
        UpdateSelection();
    }

    void LineOptionChanged(DataGridViewCellEventArgs args)
    {
        if(rebuildingRows||args.RowIndex<0||args.ColumnIndex<0||list.Rows[args.RowIndex].Tag is not string line)return;
        bool enabled=list.Rows[args.RowIndex].Cells[args.ColumnIndex].Value is true;
        if(args.ColumnIndex==RememberColumn)rememberedChoices[line]=enabled;
        if(args.ColumnIndex==GenerateColumn)
        {
            if(enabled)checkedLines.Add(line);else checkedLines.Remove(line);
        }
        UpdateSelection();
    }

    void UpdateSelection()
    {
        if(checkedLines.Count==1&&checkedLines.Single() is {} line&&
           !StringComparer.OrdinalIgnoreCase.Equals(loadedLine,line))
        {
            loadedLine=line;
            recalculateLine.Checked=false;
            if(savedLayouts.Contains(line)&&IsoGenerator.AutoCAD.RememberedIsoOutput.Defaults(drawingPath,line) is {} remembered)
            {
                if(!documentsInitialized)
                {
                    Documents=Documents with{Valves=remembered.Valves,Supports=remembered.Supports};
                    documentsInitialized=Documents!=new IsoDocumentDetails();
                }
                MinimumPipeLength=NormalizePipeLength(remembered.MinimumPipeLength);
                overrideNorth.Checked=!remembered.AutomaticView;
                north.SelectedIndex=Math.Max(0,Array.IndexOf(new[]{0,3,1,2},remembered.QuarterTurns));
                excludeHardware.Checked=remembered.ExcludeHardware;
                ignoreBreaks.Checked=remembered.IgnoreBreaks;
                instrumentCalloutsOnly=remembered.InstrumentsOnly;
                if(double.IsFinite(remembered.SymbolScale)&&remembered.SymbolScale>=(double)scale.Minimum&&
                   remembered.SymbolScale<=(double)scale.Maximum)scale.Value=(decimal)remembered.SymbolScale;
            }
        }
        recalculateLine.Enabled=checkedLines.Any(line=>savedLayouts.Contains(line)&&RememberLayout(line));
        if(!recalculateLine.Enabled)recalculateLine.Checked=false;
        generate.Enabled=checkedLines.Count>0&&!string.IsNullOrWhiteSpace(OutputDirectory)&&!string.IsNullOrWhiteSpace(TemplatePath);
        string pdf=TitleDetails is null?"":"  PDF: "+TitleDetails.DocumentNumber;
        details.Text=$"Отмечено линий: {checkedLines.Count} из {lines.Count}."+pdf;
    }
    void Accept()
    {
        if(!generate.Enabled)return;
        if(TitleDetails is null)
        {
            EditDocuments();
            if(TitleDetails is null)return;
        }
        try
        {
            TitleDetails=IsometricDocumentTemplates.ReadTitle(Documents.TitlePath);
            _=DrawingFileNames.DocumentStem(TitleDetails.DocumentNumber);
            if(!string.IsNullOrWhiteSpace(Documents.ContentsPath)&&!File.Exists(Documents.ContentsPath))
                throw new FileNotFoundException("Не найден выбранный шаблон содержания.",Documents.ContentsPath);
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
        try{if(!Path.IsPathRooted(OutputDirectory))throw new IOException("Выберите полный путь к папке.");Path.GetFullPath(OutputDirectory);if(!Path.IsPathRooted(TemplatePath))throw new IOException("Укажите полный путь к локальной копии шаблона.");Path.GetFullPath(TemplatePath);if(!File.Exists(TemplatePath))throw new FileNotFoundException("Не найден выбранный шаблон.",TemplatePath);}
        catch(Exception ex){MessageBox.Show(this,ex.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
        DialogResult=DialogResult.OK;Close();
    }
}
