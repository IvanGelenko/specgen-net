using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;

namespace PipingSpecPlugin.UI;
internal sealed record IsometricLineChoice(string Key,string Diameter,int Count);
internal sealed class IsometricDialog : Form
{
    readonly IReadOnlyList<IsometricLineChoice> lines;
    readonly TextBox filter=new(),folder=new();
    readonly ListView list=new();
    readonly Button generate=new();
    readonly Label details=new();
    readonly CheckBox overrideNorth=new();
    readonly CheckBox excludeHardware=new(),ignoreBreaks=new();
    readonly TextBox template=new();
    readonly NumericUpDown scale=new();
    public bool IgnoreBreakPoints=>ignoreBreaks.Checked;
    public string TemplatePath=>template.Text.Trim();
    public double SymbolScale=>(double)scale.Value;
    public bool ExcludeFlangeHardware=>excludeHardware.Checked;
    readonly ComboBox north=new();
    public int QuarterTurns=>overrideNorth.Checked?new[]{0,3,1,2}[north.SelectedIndex]:0;
    public string SelectedLine=>list.SelectedItems.Count==1?(string)list.SelectedItems[0].Tag:null;
    public string OutputDirectory=>folder.Text.Trim();
    public IsometricDialog(IReadOnlyList<IsometricLineChoice> choices,string drawing,string outputDirectory,string templatePath="",double symbolScale=10)
    {
        lines=choices;Text="SpecGen — Изометрии";Font=new Font("Segoe UI",9.5f);ClientSize=new Size(760,744);MinimumSize=new Size(700,779);
        StartPosition=FormStartPosition.CenterParent;MinimizeBox=false;MaximizeBox=false;ShowInTaskbar=false;
        Controls.Add(new Label{Text="Выберите линию для изометрии",Left=18,Top=15,Width=700,Height=27,Font=new Font(Font.FontFamily,13,FontStyle.Bold)});
        Controls.Add(new Label{Text="Текущий чертёж: "+Path.GetFileName(drawing),Left=18,Top=48,Width=710,Height=22,AutoEllipsis=true});
        filter.SetBounds(18,80,724,27);filter.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;filter.PlaceholderText="Поиск по номеру линии";filter.TextChanged+=(s,e)=>Fill();Controls.Add(filter);
        list.SetBounds(18,119,724,253);list.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;
        list.View=View.Details;list.FullRowSelect=true;list.HideSelection=false;list.MultiSelect=false;
        list.Columns.Add("Номер линии",512);list.Columns.Add("Ду",80);list.Columns.Add("Деталей",100);
        list.SelectedIndexChanged+=(s,e)=>UpdateSelection();list.DoubleClick+=(s,e)=>Accept();Controls.Add(list);
        details.SetBounds(18,380,724,30);details.Anchor=AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;details.ForeColor=Color.DimGray;Controls.Add(details);
        Controls.Add(new Label{Text="Шаблон DWG / DWT",Left=18,Top=419,Width=135,Height=24,Anchor=AnchorStyles.Bottom|AnchorStyles.Left});
        template.SetBounds(155,417,471,28);template.Anchor=AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;template.Text=templatePath;template.TextChanged+=(s,e)=>UpdateSelection();Controls.Add(template);
        var chooseTemplate=new Button{Text="Обзор…",Left=638,Top=415,Width=104,Height=30,Anchor=AnchorStyles.Bottom|AnchorStyles.Right};
        chooseTemplate.Click+=(s,e)=>{using var picker=new OpenFileDialog{Title="Выберите локальную копию шаблона изометрии А2",Filter="Шаблон AutoCAD (*.dwg;*.dwt)|*.dwg;*.dwt",CheckFileExists=true};if(File.Exists(TemplatePath))picker.FileName=TemplatePath;if(picker.ShowDialog(this)==DialogResult.OK)template.Text=picker.FileName;};Controls.Add(chooseTemplate);
        Controls.Add(new Label{Text="Масштаб элементов",Left=18,Top=455,Width=145,Height=24,Anchor=AnchorStyles.Bottom|AnchorStyles.Left});
        scale.SetBounds(170,453,120,28);scale.DecimalPlaces=4;scale.Minimum=.0001m;scale.Maximum=100000;scale.Increment=1;scale.Value=double.IsFinite(symbolScale)&&symbolScale>0&&symbolScale<=100000?(decimal)symbolScale:10;scale.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;Controls.Add(scale);
        Controls.Add(new Label{Text="Единый для всех элементов и диаметров",Left=304,Top=457,Width=420,Height=24,Anchor=AnchorStyles.Bottom|AnchorStyles.Left});
        ignoreBreaks.Text="Игнорировать точки разрыва на модели";ignoreBreaks.SetBounds(18,569,724,27);ignoreBreaks.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;Controls.Add(ignoreBreaks);
        overrideNorth.Text="Переопределить направление стрелки на север";overrideNorth.SetBounds(18,497,425,27);overrideNorth.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;Controls.Add(overrideNorth);
        north.SetBounds(450,497,292,28);north.DropDownStyle=ComboBoxStyle.DropDownList;north.Items.AddRange(new object[]{"↖ Левый верхний","↗ Правый верхний","↙ Левый нижний","↘ Правый нижний"});north.SelectedIndex=0;north.Enabled=false;north.Anchor=AnchorStyles.Bottom|AnchorStyles.Right;Controls.Add(north);
        overrideNorth.CheckedChanged+=(s,e)=>north.Enabled=overrideNorth.Checked;
        excludeHardware.Text="Не включать фланцы, прокладки и крепёж в спецификацию";excludeHardware.SetBounds(18,533,724,27);excludeHardware.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;Controls.Add(excludeHardware);
        Controls.Add(new Label{Text="Папка для DWG и PDF",Left=18,Top=608,Width=240,Height=22,Anchor=AnchorStyles.Bottom|AnchorStyles.Left});
        folder.SetBounds(18,633,608,27);folder.Text=outputDirectory;folder.Anchor=AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;folder.TextChanged+=(s,e)=>UpdateSelection();Controls.Add(folder);
        var browse=new Button{Text="Обзор…",Left=638,Top=631,Width=104,Height=30,Anchor=AnchorStyles.Bottom|AnchorStyles.Right};
        browse.Click+=(s,e)=>{using var picker=new FolderBrowserDialog{Description="Папка для изометрии",UseDescriptionForTitle=true,SelectedPath=Directory.Exists(OutputDirectory)?OutputDirectory:Path.GetDirectoryName(drawing)};if(picker.ShowDialog(this)==DialogResult.OK)folder.Text=picker.SelectedPath;};Controls.Add(browse);
        generate.SetBounds(482,686,152,34);generate.Text="Создать изометрию";generate.Anchor=AnchorStyles.Bottom|AnchorStyles.Right;generate.Click+=(s,e)=>Accept();Controls.Add(generate);
        var cancel=new Button{Text="Отмена",Left=646,Top=686,Width=96,Height=34,DialogResult=DialogResult.Cancel,Anchor=AnchorStyles.Bottom|AnchorStyles.Right};Controls.Add(cancel);
        AcceptButton=generate;CancelButton=cancel;Fill();
    }
    void Fill()
    {
        string selected=SelectedLine;list.BeginUpdate();list.Items.Clear();
        foreach(var line in lines.Where(l=>l.Key.IndexOf(filter.Text.Trim(),StringComparison.CurrentCultureIgnoreCase)>=0))
        {
            var item=new ListViewItem(line.Key){Tag=line.Key};item.SubItems.Add(line.Diameter??"");item.SubItems.Add(line.Count.ToString());list.Items.Add(item);
            if(line.Key==selected)item.Selected=true;
        }
        if(list.Items.Count==1)list.Items[0].Selected=true;
        list.EndUpdate();UpdateSelection();
    }
    void UpdateSelection()
    {
        generate.Enabled=SelectedLine!=null&&!string.IsNullOrWhiteSpace(OutputDirectory)&&!string.IsNullOrWhiteSpace(TemplatePath);
        details.Text=SelectedLine==null?$"Найдено линий: {list.Items.Count}. Выберите одну линию.":$"{SelectedLine}. Один общий PDF; DWG по листам. Разбиение — только при нехватке места.";
    }
    void Accept()
    {
        if(!generate.Enabled)return;
        try{if(!Path.IsPathRooted(OutputDirectory))throw new IOException("Выберите полный путь к папке.");Path.GetFullPath(OutputDirectory);if(!Path.IsPathRooted(TemplatePath))throw new IOException("Укажите полный путь к локальной копии шаблона.");Path.GetFullPath(TemplatePath);if(!File.Exists(TemplatePath))throw new FileNotFoundException("Не найден выбранный шаблон.",TemplatePath);}
        catch(Exception ex){MessageBox.Show(this,ex.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
        DialogResult=DialogResult.OK;Close();
    }
}
