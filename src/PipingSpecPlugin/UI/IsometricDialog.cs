using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Text.Json;
using IsoGenerator.Core;
using IsoGenerator.Publishing;
using PipingSpecPlugin.Settings;

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
    const int GenerateColumn=0,ViewColumn=5,RememberColumn=6;
    const string AutomaticViewLabel="Авто";
    static readonly int[] ViewTurns={0,3,1,2};
    static readonly string[] ViewLabels={"↖ Верх слева","↗ Верх справа","↙ Низ слева","↘ Низ справа"};
    readonly Dictionary<string,int> lineViews=new(StringComparer.OrdinalIgnoreCase);
    readonly ComboBox viewMode=new(),north=new();
    readonly Button generate=new();
    readonly Label details=new(),listSummary=new(),titleStatus=new();
    readonly Button selectVisible=new(),clearSelection=new(),editDocuments=new();
    readonly TabControl tabs=new();
    readonly TabPage releasePage=new("Выпуск"),settingsPage=new("Настройки");
    readonly ToolTip tips=new();
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
    IsoViewMode ViewMode=>(IsoViewMode)viewMode.SelectedIndex;
    int CommonQuarterTurns=>ViewTurns[north.SelectedIndex];
    public bool AutomaticView=>ViewMode==IsoViewMode.Automatic;
    public int QuarterTurns=>AutomaticView?0:CommonQuarterTurns;
    public int? ManualQuarterTurns(string line)=>ViewMode==IsoViewMode.PerLine?lineViews.GetValueOrDefault(line,CommonQuarterTurns):null;
    public IsoViewPreferences ViewPreferences=>new(ViewMode,CommonQuarterTurns,new(lineViews,StringComparer.OrdinalIgnoreCase));
    public string OutputDirectory=>folder.Text.Trim();
    public IsometricDialog(IReadOnlyList<IsometricLineChoice> choices,string drawing,string outputDirectory,string templatePath="",double symbolScale=10,double lastMinimumPipeLength=10,IsoAdvancedSettings advancedSettings=null,IsoDocumentDetails documents=null,IReadOnlyCollection<string> selectedLines=null,IsoViewPreferences viewPreferences=null)
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
        if(viewPreferences is null&&checkedLines.Count==1&&savedLayouts.Contains(checkedLines.Single())&&
           IsoGenerator.AutoCAD.RememberedIsoOutput.Defaults(drawing,checkedLines.Single()) is {AutomaticView:false} legacy)
            viewPreferences=new(IsoViewMode.Common,legacy.QuarterTurns,new());
        if(viewPreferences?.LineQuarterTurns is {} savedViews)
            foreach(var (line,turns) in savedViews)
                if(turns is >=0 and <=3)lineViews[line]=turns;
        Text="SpecGen · Генератор изометрий";
        Font=new Font("Segoe UI",10f);
        ClientSize=new Size(1110,690);
        MinimumSize=new Size(930,570);
        AutoScaleMode=AutoScaleMode.Dpi;
        StartPosition=FormStartPosition.CenterParent;
        MinimizeBox=false;
        ShowInTaskbar=false;
        BackColor=Canvas;
        ForeColor=Ink;

        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(24,18,24,12)};
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        var header=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,RowCount=2,Margin=new Padding(0,0,0,14)};
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var heading=Heading("Генератор изометрий",22);
        var drawingLabel=Note($"Чертёж · {Path.GetFileName(drawing)}");
        drawingLabel.AutoSize=false;
        drawingLabel.AutoEllipsis=true;
        drawingLabel.Dock=DockStyle.Fill;
        drawingLabel.Height=24;
        tips.SetToolTip(drawingLabel,drawing);
        var help=ActionButton("Справка  F1",()=>ShowHelp(CurrentHelpTopic()));
        header.Controls.Add(heading,0,0);
        header.Controls.Add(help,1,0);
        header.Controls.Add(drawingLabel,0,1);
        layout.Controls.Add(header,0,0);

        tabs.Dock=DockStyle.Fill;
        tabs.AccessibleName="Разделы генератора";
        tabs.Margin=new Padding(0);
        tabs.TabPages.AddRange(new[]{releasePage,settingsPage});
        layout.Controls.Add(tabs,0,1);
        BuildReleasePage();
        BuildSettingsPage();

        var footer=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=3,Margin=new Padding(0,12,0,0)};
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        details.Dock=DockStyle.Fill;
        details.TextAlign=ContentAlignment.MiddleLeft;
        details.AutoEllipsis=true;
        details.AccessibleName="Состояние готовности к выпуску";
        details.AccessibleRole=AccessibleRole.StatusBar;
        footer.Controls.Add(details,0,0);
        var cancel=ActionButton("Закрыть",null);
        cancel.DialogResult=DialogResult.Cancel;
        footer.Controls.Add(cancel,1,0);
        ConfigureButton(generate,"Создать изометрии",primary:true);
        generate.Click+=(_,_)=>Accept();
        footer.Controls.Add(generate,2,0);
        layout.Controls.Add(footer,0,2);

        AcceptButton=generate;
        CancelButton=cancel;
        Shown+=(_,_)=>filter.Focus();
        Fill();

        void BuildReleasePage()
        {
            var main=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(14),BackColor=Canvas};
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,270));
            releasePage.Controls.Add(main);

            var selection=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Margin=new Padding(0,0,12,0),BackColor=Surface,Padding=new Padding(14)};
            selection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            selection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            selection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            selection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            selection.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            selection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            selection.Controls.Add(Note("Поиск по номеру линии  ·  Ctrl+F"),0,1);
            filter.Dock=DockStyle.Top;
            filter.Margin=new Padding(0);
            filter.AccessibleName="Поиск по номеру линии";
            filter.TextChanged+=(_,_)=>Fill();
            filter.KeyDown+=(_,args)=>
            {
                if(args.KeyCode==Keys.Enter){list.Focus();args.SuppressKeyPress=true;}
            };
            selection.Controls.Add(filter,0,2);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Margin=new Padding(0,4,0,5)};
            ConfigureButton(selectVisible,"Отметить найденные");
            selectVisible.AccessibleDescription="Отмечает строки, которые видны с текущим фильтром.";
            selectVisible.Click+=(_,_)=>SetSelection(visibleOnly:true);
            ConfigureButton(clearSelection,"Снять отметки");
            clearSelection.AccessibleDescription="Снимает отметки со всех линий, включая скрытые фильтром.";
            clearSelection.Click+=(_,_)=>SetSelection(visibleOnly:false);
            actions.Controls.AddRange(new Control[]{selectVisible,clearSelection});
            selection.Controls.Add(actions,0,3);

            var views=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Margin=new Padding(0,0,0,6)};
            var linesHeading=Heading("Линии чертежа");
            linesHeading.Margin=new Padding(0,0,15,0);
            linesHeading.Anchor=AnchorStyles.Left;
            views.Controls.Add(linesHeading);
            views.Controls.Add(new Label{Text="Ракурс",AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(0,0,8,0)});
            viewMode.Width=166;viewMode.DropDownStyle=ComboBoxStyle.DropDownList;viewMode.AccessibleName="Режим выбора ракурса";
            viewMode.Items.AddRange(new object[]{"Автоматически","Один для всех линий","Для каждой линии"});
            viewMode.SelectedIndex=viewPreferences is not null&&Enum.IsDefined(viewPreferences.Mode)?(int)viewPreferences.Mode:0;
            north.Width=134;north.DropDownStyle=ComboBoxStyle.DropDownList;north.AccessibleName="Общий ракурс";
            north.Items.AddRange(ViewLabels);
            north.SelectedIndex=Math.Max(0,Array.IndexOf(ViewTurns,viewPreferences?.CommonQuarterTurns??0));
            views.Controls.Add(viewMode);
            views.Controls.Add(north);
            selection.Controls.Add(views,0,0);

            list.Dock=DockStyle.Fill;
            list.Margin=new Padding(0);
            list.AccessibleName="Линии Plant 3D";
            list.AccessibleDescription="Пробел переключает флажок в активной ячейке. Стрелки перемещают фокус по таблице.";
            list.AllowUserToAddRows=false;list.AllowUserToDeleteRows=false;list.AllowUserToResizeRows=false;
            list.RowHeadersVisible=false;list.MultiSelect=false;list.EditMode=DataGridViewEditMode.EditOnEnter;
            list.BackgroundColor=Surface;list.BorderStyle=BorderStyle.None;list.GridColor=Divider;
            list.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;
            list.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;
            list.EnableHeadersVisualStyles=SystemInformation.HighContrast;
            list.ColumnHeadersHeight=33;list.RowTemplate.Height=30;
            list.ColumnHeadersDefaultCellStyle.BackColor=Canvas;
            list.ColumnHeadersDefaultCellStyle.ForeColor=Ink;
            list.DefaultCellStyle.BackColor=Surface;
            list.DefaultCellStyle.ForeColor=Ink;
            list.DefaultCellStyle.SelectionBackColor=SystemInformation.HighContrast?SystemColors.Highlight:Color.FromArgb(220,234,249);
            list.DefaultCellStyle.SelectionForeColor=SystemInformation.HighContrast?SystemColors.HighlightText:Ink;
            list.Columns.Add(new DataGridViewCheckBoxColumn{HeaderText="Выпуск",Width=62,ToolTipText="Отметьте линии, которые нужно выпустить."});
            list.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Номер линии",ReadOnly=true,AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,MinimumWidth=134});
            list.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Ду",ReadOnly=true,Width=52});
            list.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Деталей",ReadOnly=true,Width=65});
            list.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Состояние",ReadOnly=true,Width=100,ToolTipText="Сохранённая компоновка или ранее выпущенная линия."});
            var lineView=new DataGridViewComboBoxColumn{HeaderText="Ракурс",Width=132,FlatStyle=FlatStyle.Flat,
                ToolTipText="В режиме «Для каждой линии» выберите направление в этой колонке."};
            lineView.Items.Add(AutomaticViewLabel);
            lineView.Items.AddRange(ViewLabels);
            list.Columns.Add(lineView);
            list.Columns.Add(new DataGridViewCheckBoxColumn{HeaderText="Запомнить",Width=88,ToolTipText="Сохранить компоновку и повторно использовать её при выпуске."});
            foreach(DataGridViewColumn column in list.Columns)column.SortMode=DataGridViewColumnSortMode.NotSortable;
            list.SizeChanged+=(_,_)=>UpdateListColumns();
            list.CurrentCellDirtyStateChanged+=(_,_)=>{if(list.IsCurrentCellDirty)list.CommitEdit(DataGridViewDataErrorContexts.Commit);};
            list.EditingControlShowing+=(_,args)=>
            {
                if(list.CurrentCell?.ColumnIndex==ViewColumn&&args.Control is DataGridViewComboBoxEditingControl editor)
                    editor.Items.Remove(AutomaticViewLabel);
            };
            list.CellValueChanged+=(_,args)=>LineOptionChanged(args);
            selection.Controls.Add(list,0,4);
            viewMode.SelectedIndexChanged+=(_,_)=>UpdateViews();
            north.SelectedIndexChanged+=(_,_)=>UpdateViews();
            UpdateViews();
            listSummary.AutoSize=true;listSummary.ForeColor=Muted;listSummary.Margin=new Padding(0,9,0,0);
            selection.Controls.Add(listSummary,0,5);
            main.Controls.Add(selection,0,0);

            var output=SectionPanel();
            output.Dock=DockStyle.Fill;
            output.Controls.Add(Heading("Параметры выпуска"));
            output.Controls.Add(Note("Титульный лист"));
            titleStatus.AutoSize=true;titleStatus.MaximumSize=new Size(224,0);titleStatus.Margin=new Padding(0,0,0,4);
            titleStatus.AccessibleName="Состояние титульного листа";
            output.Controls.Add(titleStatus);
            ConfigureButton(editDocuments,"Реквизиты документов…");
            editDocuments.Click+=(_,_)=>EditDocuments();
            editDocuments.Margin=new Padding(0,0,0,11);
            tips.SetToolTip(editDocuments,"Выбрать титул, настроить содержание и указать документы по арматуре и опорам.");
            output.Controls.Add(editDocuments);
            output.Controls.Add(Note("Папка выпуска"));
            folder.Width=224;folder.Text=outputDirectory;folder.AccessibleName="Папка выпуска";folder.Margin=new Padding(0,0,0,4);
            folder.TextChanged+=(_,_)=>UpdateSelection();
            output.Controls.Add(folder);
            output.Controls.Add(ActionButton("Выбрать папку…",()=>
            {
                using var picker=new FolderBrowserDialog{Description="Папка выпуска изометрий",UseDescriptionForTitle=true,
                    SelectedPath=Directory.Exists(OutputDirectory)?OutputDirectory:Path.GetDirectoryName(drawing)};
                if(picker.ShowDialog(this)==DialogResult.OK)folder.Text=picker.SelectedPath;
            }));
            output.Controls.Add(Note("Результат · PDF, DWG, DOCX",224));
            output.Controls.Add(HelpLink("Как подготовить выпуск","Выпуск"));
            main.Controls.Add(output,1,0);
        }

        void BuildSettingsPage()
        {
            var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=Canvas};
            var settings=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,
                ColumnCount=2,RowCount=1,Padding=new Padding(14),BackColor=Canvas};
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            settingsPage.Controls.Add(scroll);
            scroll.Controls.Add(settings);
            var sheetOptions=SectionPanel();
            sheetOptions.Dock=DockStyle.Fill;
            sheetOptions.AutoScroll=false;
            sheetOptions.AutoSize=true;
            sheetOptions.Margin=new Padding(0,0,12,0);
            settings.Controls.Add(sheetOptions,0,0);
            var layoutOptions=SectionPanel();
            layoutOptions.Dock=DockStyle.Fill;
            layoutOptions.AutoScroll=false;
            layoutOptions.AutoSize=true;
            settings.Controls.Add(layoutOptions,1,0);

            sheetOptions.Controls.Add(Heading("Оформление листа"));
            sheetOptions.Controls.Add(Note("Шаблон листа А2 (DWG или DWT)"));
            template.Width=320;template.Text=templatePath;template.AccessibleName="Шаблон листа А2";
            template.TextChanged+=(_,_)=>UpdateSelection();
            sheetOptions.Controls.Add(template);
            sheetOptions.Controls.Add(ActionButton("Выбрать шаблон…",()=>
            {
                using var picker=new OpenFileDialog{Title="Выберите локальную копию шаблона изометрии А2",
                    Filter="Шаблон AutoCAD (*.dwg;*.dwt)|*.dwg;*.dwt",CheckFileExists=true};
                if(File.Exists(TemplatePath))picker.FileName=TemplatePath;
                if(picker.ShowDialog(this)==DialogResult.OK)template.Text=picker.FileName;
            }));

            sheetOptions.Controls.Add(Note("Масштаб символов"));
            scale.Width=130;scale.DecimalPlaces=4;scale.Minimum=.0001m;scale.Maximum=100000;scale.Increment=1;
            scale.Value=double.IsFinite(symbolScale)&&symbolScale>=(double)scale.Minimum&&symbolScale<=100000?(decimal)symbolScale:10;
            scale.AccessibleName="Масштаб символов";
            sheetOptions.Controls.Add(scale);
            sheetOptions.Controls.Add(Note("Меняет размер условных обозначений на листе.",320));

            ConfigureCheck(excludeHardware,"Исключить фланцевые детали при арматуре");
            excludeHardware.Checked=true;
            sheetOptions.Controls.Add(excludeHardware);
            sheetOptions.Controls.Add(Note("Из спецификации исключаются фланцы, заглушки, прокладки и крепёж при арматуре. Самостоятельные фланцевые пары учитываются.",320));

            layoutOptions.Controls.Add(Heading("Компоновка"));
            layoutOptions.Controls.Add(Note("Ракурс задаётся на вкладке «Выпуск».",320));
            ConfigureCheck(ignoreBreaks,"Игнорировать точки разрыва на модели");
            layoutOptions.Controls.Add(ignoreBreaks);
            layoutOptions.Controls.Add(Note("Разрешает генератору самому выбрать границы листов.",320));
            ConfigureCheck(recalculateLine,"Пересчитать выбранные линии");
            layoutOptions.Controls.Add(recalculateLine);
            layoutOptions.Controls.Add(Note("Доступно для отмеченных линий с сохранённой компоновкой и флажком «Запомнить».",320));
            var advanced=ActionButton("Расширенные настройки…",EditAdvancedSettings);
            advanced.Margin=new Padding(0,12,0,4);
            layoutOptions.Controls.Add(advanced);
            layoutOptions.Controls.Add(HelpLink("Что меняют параметры","Компоновка"));
        }
    }

    protected override bool ProcessCmdKey(ref Message message,Keys keyData)
    {
        switch(keyData)
        {
            case Keys.F1:
                ShowHelp(CurrentHelpTopic());
                return true;
            case Keys.Control|Keys.F:
                tabs.SelectedTab=releasePage;
                filter.Focus();
                filter.SelectAll();
                return true;
            default:
                return base.ProcessCmdKey(ref message,keyData);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)tips.Dispose();
        base.Dispose(disposing);
    }

    static Color Canvas=>SystemInformation.HighContrast?SystemColors.Control:Color.FromArgb(241,245,250);
    static Color Surface=>SystemColors.Window;
    static Color Ink=>SystemColors.WindowText;
    static Color Muted=>SystemInformation.HighContrast?SystemColors.WindowText:Color.FromArgb(71,87,107);
    static Color Divider=>SystemInformation.HighContrast?SystemColors.WindowText:Color.FromArgb(218,226,237);
    static Color Accent=>SystemInformation.HighContrast?SystemColors.Highlight:Color.FromArgb(25,84,153);

    Label Heading(string text,float size=14)=>new(){Text=text,AutoSize=true,ForeColor=Ink,
        Font=new Font(Font.FontFamily,size,FontStyle.Bold),Margin=new Padding(0,0,0,6)};
    static Label Note(string text,int width=0)=>new(){Text=text,AutoSize=true,ForeColor=Muted,
        MaximumSize=new Size(width,0),Margin=new Padding(0,2,0,5)};
    static FlowLayoutPanel SectionPanel()=>new(){FlowDirection=FlowDirection.TopDown,WrapContents=false,
        AutoScroll=true,Padding=new Padding(14),Margin=new Padding(0),BackColor=Surface};
    static void ConfigureCheck(CheckBox box,string text)
    {
        box.Text=text;box.AutoSize=true;box.Margin=new Padding(0,6,8,2);
    }
    static void ConfigureButton(Button button,string text,bool primary=false)
    {
        button.Text=text;button.AutoSize=true;button.MinimumSize=new Size(0,34);
        button.Padding=new Padding(8,0,8,0);
        if(SystemInformation.HighContrast)return;
        button.FlatStyle=FlatStyle.Flat;
        button.FlatAppearance.BorderColor=primary?Accent:Divider;
        button.FlatAppearance.MouseOverBackColor=primary?Color.FromArgb(20,72,132):Canvas;
        button.BackColor=primary?Accent:Surface;
        button.ForeColor=primary?Color.White:Ink;
        if(primary)
        {
            void UpdateColors()
            {
                button.BackColor=button.Enabled?Accent:Canvas;
                button.ForeColor=button.Enabled?Color.White:Muted;
                button.FlatAppearance.BorderColor=button.Enabled?Accent:Divider;
            }
            button.EnabledChanged+=(_,_)=>UpdateColors();
            UpdateColors();
        }
    }
    static Button ActionButton(string text,Action action)
    {
        var button=new Button();
        ConfigureButton(button,text);
        if(action is not null)button.Click+=(_,_)=>action();
        return button;
    }
    LinkLabel HelpLink(string text,string topic)
    {
        var link=new LinkLabel{Text=text,AutoSize=true,LinkColor=Accent,Margin=new Padding(0,9,0,3)};
        link.LinkClicked+=(_,_)=>ShowHelp(topic);
        return link;
    }
    void ShowHelp(string topic)=>IsometricHelp.Show(this,topic);
    string CurrentHelpTopic()=>tabs.SelectedTab==settingsPage?"Компоновка":list.ContainsFocus||filter.ContainsFocus?"Линии":"Выпуск";

    void SetSelection(bool visibleOnly)
    {
        if(visibleOnly)checkedLines.UnionWith(list.Rows.Cast<DataGridViewRow>().Select(row=>(string)row.Tag));
        else checkedLines.Clear();
        Fill();
    }

    void UpdateListColumns()
    {
        bool showDetails=list.ClientSize.Width>=LogicalToDeviceUnits(690);
        bool showStatus=list.ClientSize.Width>=LogicalToDeviceUnits(610);
        if(list.CurrentCell is {} cell&&((cell.ColumnIndex is 2 or 3&&!showDetails)||(cell.ColumnIndex==4&&!showStatus)))
            list.CurrentCell=list.Rows[cell.RowIndex].Cells[1];
        list.Columns[2].Visible=showDetails;
        list.Columns[3].Visible=showDetails;
        list.Columns[4].Visible=showStatus;
    }

    string ViewLabel(string line)=>AutomaticView?AutomaticViewLabel:
        ViewLabels[Array.IndexOf(ViewTurns,ManualQuarterTurns(line)??CommonQuarterTurns)];

    void UpdateViews()
    {
        north.Visible=ViewMode==IsoViewMode.Common;
        var column=(DataGridViewComboBoxColumn)list.Columns[ViewColumn];
        rebuildingRows=true;
        try
        {
            list.EndEdit();
            // Keep the cell's domain stable. WinForms can format a row while its mode changes.
            foreach(DataGridViewRow row in list.Rows)
                row.Cells[ViewColumn].Value=ViewLabel((string)row.Tag);
            column.ReadOnly=ViewMode!=IsoViewMode.PerLine;
            column.DisplayStyle=column.ReadOnly?DataGridViewComboBoxDisplayStyle.Nothing:DataGridViewComboBoxDisplayStyle.DropDownButton;
        }
        finally{rebuildingRows=false;}
    }

    void EditAdvancedSettings()
    {
        var draft=JsonSerializer.Deserialize<IsoAdvancedSettings>(JsonSerializer.Serialize(AdvancedSettings));
        using var window=new Form{Text="Расширенные настройки изометрии",StartPosition=FormStartPosition.CenterParent,
            ClientSize=new Size(780,605),MinimumSize=new Size(640,460),ShowInTaskbar=false,
            Font=Font,AutoScaleMode=AutoScaleMode.Dpi};
        var pipeSettings=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(8),WrapContents=false};
        var pipeLengthLabel=new Label{Text="Минимальная длина трубы на листе, мм",AutoSize=true,Anchor=AnchorStyles.Left};
        var pipeLength=new NumericUpDown{Width=100,DecimalPlaces=1,Minimum=MinimumPipeLengthLimit,
            Maximum=MaximumPipeLengthLimit,Value=(decimal)MinimumPipeLength,AccessibleName=pipeLengthLabel.Text};
        using var localTips=new ToolTip();
        localTips.SetToolTip(pipeLength,"Увеличивает короткие участки на бумаге. Размерные подписи берутся из модели.");
        window.HelpRequested+=(_,args)=>{IsometricHelp.Show(window,"Компоновка");args.Handled=true;};
        pipeSettings.Controls.AddRange(new Control[]{pipeLengthLabel,pipeLength});
        var instrumentCallouts=new CheckBox{Text="КИП: только выноска с ИД, без символа и позиции в спецификации",
            Checked=instrumentCalloutsOnly,Dock=DockStyle.Top,Height=30,Padding=new Padding(8,0,0,0)};
        var grid=new PropertyGrid{Dock=DockStyle.Fill,SelectedObject=draft,ToolbarVisible=false,HelpVisible=true,
            PropertySort=PropertySort.Categorized,AccessibleName="Параметры компоновки. Описание выбранного параметра находится под таблицей."};
        var gridHint=new Label{Text="Выберите параметр, чтобы прочитать его описание под таблицей.",
            Dock=DockStyle.Top,Height=28,Padding=new Padding(8,0,0,0),ForeColor=Muted};
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=42,FlowDirection=FlowDirection.RightToLeft};
        var save=new Button{Text="Сохранить",AutoSize=true};
        var cancel=new Button{Text="Отмена",AutoSize=true,DialogResult=DialogResult.Cancel};
        var reset=new Button{Text="По умолчанию",AutoSize=true};
        var help=new Button{Text="Справка",AutoSize=true};
        help.Click+=(_,_)=>IsometricHelp.Show(window,"Компоновка");
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
        buttons.Controls.AddRange(new Control[]{save,cancel,reset,help});
        window.Controls.Add(grid);window.Controls.Add(buttons);window.Controls.Add(gridHint);
        window.Controls.Add(instrumentCallouts);window.Controls.Add(pipeSettings);
        window.CancelButton=cancel;
        window.ShowDialog(this);
    }

    static double NormalizePipeLength(double value)=>
        value>=(double)MinimumPipeLengthLimit&&value<=(double)MaximumPipeLengthLimit?value:(double)DefaultMinimumPipeLength;
    void EditDocuments()
    {
        using var window=new Form{Text="Реквизиты документов",Font=Font,StartPosition=FormStartPosition.CenterParent,
            ClientSize=new Size(830,469),FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false,
            ShowInTaskbar=false,AutoScaleMode=AutoScaleMode.Dpi};
        window.HelpRequested+=(_,args)=>{IsometricHelp.Show(window,"Документы");args.Handled=true;};
        window.Controls.Add(new Label{Text="Для выпуска выберите заполненный титул. Остальные реквизиты можно добавить при необходимости.",
            Left=18,Top=16,Width=790,Height=30,ForeColor=Muted});
        TextBox AddField(string caption,string value,int top,bool readOnly=false,int height=26,int width=566)
        {
            top+=38;
            var input=new TextBox{Left=246,Top=top,Width=width,Height=height,Text=value,ReadOnly=readOnly,AccessibleName=caption,
                Multiline=height>30,ScrollBars=height>30?ScrollBars.Vertical:ScrollBars.None};
            window.Controls.Add(new Label{Text=caption,Left=18,Top=top+3,AutoSize=true});
            window.Controls.Add(input);
            return input;
        }
        Button AddButton(string caption,int left,int top,int width,Action action)
        {
            var button=new Button{Text=caption,Left=left,Top=top+38,Width=width,Height=32};
            button.Click+=(_,_)=>action();
            window.Controls.Add(button);
            return button;
        }
        var title=AddField("Титульный лист DOCX",Documents.TitlePath,18,true,width:452);
        var contents=AddField("Шаблон содержания DOCX",Documents.ContentsPath,54,true,width:452);
        var valves=AddField("Документ по арматуре",Documents.Valves,92);
        var supports=AddField("Документ по опорам",Documents.Supports,128);
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
        var chooseTitle=AddButton("Выбрать…",708,16,104,()=>ChooseTemplate(title,true));
        chooseTitle.AccessibleName="Выбрать титульный лист DOCX";
        AddButton("Выбрать…",708,52,104,()=>ChooseTemplate(contents,false)).AccessibleName="Выбрать шаблон содержания DOCX";
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
        AddButton("Справка",468,364,103,()=>IsometricHelp.Show(window,"Документы"));
        var save=AddButton("Сохранить",594,364,103,()=>WithErrorMessage(()=>
        {
            if(File.Exists(title.Text))LoadTitle(title.Text);
            window.DialogResult=DialogResult.OK;
        }));
        var cancel=new Button{Text="Отмена",Left=708,Top=402,Width=104,Height=32,DialogResult=DialogResult.Cancel};
        window.Controls.Add(cancel);
        window.AcceptButton=save;window.CancelButton=cancel;
        window.Shown+=(_,_)=>chooseTitle.Focus();
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
                int index=list.Rows.Add(checkedLines.Contains(line.Key),line.Key,line.Diameter??"",line.Count,status,ViewLabel(line.Key),RememberLayout(line.Key));
                var row=list.Rows[index];
                row.Tag=line.Key;
                string diameter=string.IsNullOrWhiteSpace(line.Diameter)?"—":line.Diameter;
                row.Cells[1].ToolTipText=$"{line.Key}\nДу {diameter} · Деталей {line.Count} · Состояние {status}";
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
        if(args.ColumnIndex==ViewColumn&&ViewMode==IsoViewMode.PerLine&&
           Array.IndexOf(ViewLabels,list.Rows[args.RowIndex].Cells[ViewColumn].Value as string) is int viewIndex&&viewIndex>=0)
            lineViews[line]=ViewTurns[viewIndex];
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
                excludeHardware.Checked=remembered.ExcludeHardware;
                ignoreBreaks.Checked=remembered.IgnoreBreaks;
                instrumentCalloutsOnly=remembered.InstrumentsOnly;
                if(double.IsFinite(remembered.SymbolScale)&&remembered.SymbolScale>=(double)scale.Minimum&&
                   remembered.SymbolScale<=(double)scale.Maximum)scale.Value=(decimal)remembered.SymbolScale;
            }
        }
        recalculateLine.Enabled=checkedLines.Any(line=>savedLayouts.Contains(line)&&RememberLayout(line));
        if(!recalculateLine.Enabled)recalculateLine.Checked=false;
        int visible=list.Rows.Count;
        int selectedVisible=list.Rows.Cast<DataGridViewRow>().Count(row=>checkedLines.Contains((string)row.Tag));
        int hidden=checkedLines.Count-selectedVisible;
        string count=$"Отмечено {checkedLines.Count} из {lines.Count}";
        listSummary.Text=(visible==0?"Линии не найдены. Уточните запрос.":$"Показано {visible} из {lines.Count}.")+
            (hidden>0?$" Фильтр скрыл отмеченные линии: {hidden}.":"");
        selectVisible.Enabled=visible>selectedVisible;
        clearSelection.Enabled=checkedLines.Count>0;
        titleStatus.Text=TitleDetails?.DocumentNumber??"Требуется заполненный DOCX";
        titleStatus.ForeColor=TitleDetails is null?Muted:Ink;
        editDocuments.Text=TitleDetails is null?"Выбрать титульный лист…":"Реквизиты документов…";
        string step=checkedLines.Count==0?"Отметьте линии":
            TitleDetails is null?"Выберите титульный лист":
            !Path.IsPathFullyQualified(OutputDirectory)?"Укажите полный путь к папке выпуска":
            !Path.IsPathFullyQualified(TemplatePath)?"Укажите шаблон A2 во вкладке «Настройки»":"Готово к выпуску";
        generate.Enabled=step=="Готово к выпуску";
        details.Text=$"{count}  ·  {step}";
        details.ForeColor=generate.Enabled?Ink:Muted;
    }
    void Accept()
    {
        if(!generate.Enabled)return;
        try
        {
            TitleDetails=IsometricDocumentTemplates.ReadTitle(Documents.TitlePath);
            _=DrawingFileNames.DocumentStem(TitleDetails.DocumentNumber);
            if(!string.IsNullOrWhiteSpace(Documents.ContentsPath)&&!File.Exists(Documents.ContentsPath))
                throw new FileNotFoundException("Не найден выбранный шаблон содержания.",Documents.ContentsPath);
        }
        catch(Exception ex)
        {
            tabs.SelectedTab=releasePage;
            MessageBox.Show(this,ex.Message,"Реквизиты документов",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            return;
        }
        try{if(!Path.IsPathRooted(OutputDirectory))throw new IOException("Выберите полный путь к папке.");Path.GetFullPath(OutputDirectory);if(!Path.IsPathRooted(TemplatePath))throw new IOException("Укажите полный путь к локальной копии шаблона.");Path.GetFullPath(TemplatePath);if(!File.Exists(TemplatePath))throw new FileNotFoundException("Не найден выбранный шаблон.",TemplatePath);}
        catch(Exception ex)
        {
            tabs.SelectedTab=settingsPage;
            template.Focus();
            MessageBox.Show(this,ex.Message,"Параметры выпуска",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            return;
        }
        DialogResult=DialogResult.OK;Close();
    }
}
