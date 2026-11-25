using PipingSpecPlugin.UI;
using PipingSpecPlugin.Settings;
using IsoGenerator.Core;
using System.Drawing.Imaging;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string image=Path.GetFullPath(args[0]);
        string root=Path.GetFullPath(args.Length>1?args[1]:".");
        string template=Path.Combine(root,"iso","iso_template.dwg");
        string title=Path.Combine(root,"iso","assets","iso-title-template.docx");
        var choices=new[]
        {
            new IsometricLineChoice("020-WS3/5.2-001-CS016B-__","25",96),
            new IsometricLineChoice("020-WS3/5.2-002-CS016B-__","25",54),
            new IsometricLineChoice("020-WV3/5.1-001-CS016B-__","25",218),
            new IsometricLineChoice("020-WV3/5.1-002-CS016B-__","25",111),
            new IsometricLineChoice("050-N001-LS016B-NN","50",18),
            new IsometricLineChoice("150-FL007-LS016B-HN","150",59),
        };
        using var window=new IsometricDialog(choices,Path.Combine(root,"Технологические трубопроводы.dwg"),
            Path.Combine(root,"Изометрии"),template,7.5,documents:new IsoDocumentDetails(TitlePath:title),
            selectedLines:new[]{choices[0].Key,choices[1].Key});
        window.StartPosition=FormStartPosition.Manual;
        window.Location=new Point(-32000,-32000);
        window.Show();
        Application.DoEvents();
        if(window.SymbolScale!=7.5||window.TemplatePath!=template||window.TitleDetails is null)
            throw new Exception("Saved template, scale or title was not loaded.");
        var search=Find<TextBox>(window,c=>c.AccessibleName=="Поиск по номеру линии");
        var results=Find<DataGridView>(window,_=>true);
        results.DataError+=(_,args)=>throw new InvalidOperationException("The view grid failed to format or commit a cell.",args.Exception);
        var tabs=Find<TabControl>(window,_=>true);
        var generate=Find<Button>(window,c=>c.Text=="Создать изометрии");
        if(!generate.Enabled)throw new Exception("A complete configuration cannot be released.");
        search.Text="WV3";
        if(results.Rows.Count!=2||window.SelectedLines.Count!=2)
            throw new Exception("Filtering affected the checked lines.");
        Find<Button>(window,c=>c.Text=="Отметить найденные").PerformClick();
        if(window.SelectedLines.Count!=4)throw new Exception("Select visible did not keep hidden lines checked.");
        Find<Button>(window,c=>c.Text=="Снять отметки").PerformClick();
        if(window.SelectedLines.Count!=0||generate.Enabled)throw new Exception("Clear selection left an enabled release action.");
        search.Text="WS3";
        Find<Button>(window,c=>c.Text=="Отметить найденные").PerformClick();
        search.Clear();
        Capture(window,image);
        var mode=Find<ComboBox>(window,c=>c.AccessibleName=="Режим выбора ракурса");
        var common=Find<ComboBox>(window,c=>c.AccessibleName=="Общий ракурс");
        var view=results.Columns.Cast<DataGridViewColumn>().Single(c=>c.HeaderText=="Ракурс");
        if(!window.AutomaticView||!view.ReadOnly||results.Rows[0].Cells[view.Index].Value as string!="Авто")
            throw new Exception("Automatic view is not displayed.");
        mode.SelectedIndex=1;
        common.SelectedIndex=1;
        if(window.AutomaticView||window.QuarterTurns!=3||!view.ReadOnly||
           results.Rows.Cast<DataGridViewRow>().Any(row=>!Equals(row.Cells[view.Index].Value,common.SelectedItem)))
            throw new Exception("Common view is not applied to every row.");
        mode.SelectedIndex=2;
        results.CurrentCell=results.Rows[0].Cells[view.Index];
        results.BeginEdit(true);
        var editor=(ComboBox)results.EditingControl!;
        if(editor.Items.Count!=4||editor.Items.Contains("Авто"))
            throw new Exception("The manual editor must offer exactly four directions.");
        editor.SelectedIndex=2;
        if(!results.EndEdit()||window.ManualQuarterTurns(choices[0].Key)!=1||window.ManualQuarterTurns(choices[1].Key)!=3)
            throw new Exception("The line combo did not commit its own view.");
        search.Text=choices[1].Key;
        results.Rows[0].Cells[view.Index].Value=common.Items[3];
        search.Clear();
        mode.SelectedIndex=0;
        results.Refresh();
        mode.SelectedIndex=2;
        results.Refresh();
        if(window.ManualQuarterTurns(choices[0].Key)!=1||window.ManualQuarterTurns(choices[1].Key)!=2)
            throw new Exception("Filtering or mode switches lost individual views.");
        foreach(var (choice,expected) in new[]{(choices[0],1),(choices[1],2)})
        {
            var pipe=new Part("Test.dwg","1","AcPpDb3dPipe",choice.Key,
                new[]{new Port("S1",new(0,0,0),new(-1,0,0)),new Port("S2",new(1000,0,0),new(1,0,0))},
                new Dictionary<string,string>{{"PnPClassName","Pipe"}});
            var snapshot=new PipelineSnapshot(1,"Test.dwg",new[]{pipe},Array.Empty<Connection>(),Array.Empty<string>());
            var options=new LineLayoutOptions(window.AutomaticView,window.ManualQuarterTurns(choice.Key)!.Value,
                false,false,true,10,new IsoAdvancedSettings());
            var plan=LineViewPlanner.Plan(snapshot,new IsoSymbolMap("<Map/>"),options);
            if(plan.QuarterTurns!=expected||plan.Sheets.Any(sheet=>sheet.Scene.QuarterTurns!=expected))
                throw new Exception("The planner did not apply the individual orientation to its sheets.");
        }
        Capture(window,WithSuffix(image,"-views"));
        var profile=System.Text.Json.JsonSerializer.Deserialize<IsoViewPreferences>(
            System.Text.Json.JsonSerializer.Serialize(window.ViewPreferences))!;
        using(var reopened=new IsometricDialog(choices,Path.Combine(root,"Технологические трубопроводы.dwg"),
            Path.Combine(root,"Изометрии"),template,viewPreferences:profile))
        {
            if(reopened.AutomaticView||reopened.ManualQuarterTurns(choices[0].Key)!=1||
               reopened.ManualQuarterTurns(choices[1].Key)!=2||reopened.QuarterTurns!=3)
                throw new Exception("The saved drawing view settings did not round trip.");
        }
        tabs.SelectedIndex=1;
        Capture(window,WithSuffix(image,"-settings"));
        window.ClientSize=new Size(914,531);
        tabs.SelectedIndex=0;
        Capture(window,WithSuffix(image,"-compact"));
        if(results.Columns[2].Visible||results.Columns[3].Visible||results.Columns[1].Width<215)
            throw new Exception("Compact layout has insufficient room for line names.");
        tabs.SelectedIndex=1;
        Capture(window,WithSuffix(image,"-settings-compact"));

        InspectDialog(window,c=>c.Text.StartsWith("Справка  F1"),"Справка · Генератор изометрий",help=>
        {
            var article=Find<RichTextBox>(help,_=>true);
            if(!article.Text.StartsWith("Компоновка"))throw new Exception("Settings did not open contextual help.");
            Capture(help,WithSuffix(image,"-help"));
            var field=Find<TextBox>(help,c=>c.AccessibleName=="Поиск в справке");
            field.Text="PSISOMETRIC";
            if(!article.Text.Contains("PSISOMETRIC"))throw new Exception("Command could not be found in help.");
            field.Text="неттакойтемы";
            if(!article.Text.Contains("ничего не найдено"))throw new Exception("Help did not render its empty state.");
        });
        InspectDialog(window,c=>c.Text=="Расширенные настройки…","Расширенные настройки изометрии",advanced=>
        {
            var grid=Find<PropertyGrid>(advanced,_=>true);
            var properties=System.ComponentModel.TypeDescriptor.GetProperties(grid.SelectedObject).Cast<System.ComponentModel.PropertyDescriptor>();
            if(!grid.HelpVisible||properties.Any(property=>string.IsNullOrWhiteSpace(property.Description)))
                throw new Exception("An advanced setting does not have an explanation.");
            Capture(advanced,WithSuffix(image,"-advanced"));
        });

        using var firstRun=new IsometricDialog(choices,Path.Combine(root,"Новый чертёж.dwg"),Path.Combine(root,"Изометрии"),template,
            selectedLines:new[]{choices[0].Key});
        firstRun.StartPosition=FormStartPosition.Manual;
        firstRun.Location=window.Location;
        firstRun.Show();
        if(Find<Button>(firstRun,c=>c.Text=="Создать изометрии").Enabled||
           !Find<Label>(firstRun,c=>c.AccessibleName=="Состояние готовности к выпуску").Text.Contains("Выберите титульный лист"))
            throw new Exception("First run does not explain the required title.");
        Capture(firstRun,WithSuffix(image,"-first-run"));
        InspectDialog(firstRun,c=>c.Text=="Выбрать титульный лист…","Реквизиты документов",docs=>
        {
            if(!Find<Button>(docs,c=>c.AccessibleName=="Выбрать титульный лист DOCX").Enabled)
                throw new Exception("A new user cannot pick the required title.");
            Capture(docs,WithSuffix(image,"-documents"));
        });
        Console.WriteLine("Release workflow, filtering, three view modes, settings round trip and help checks passed.");
    }

    static T Find<T>(Control root,Func<T,bool> match) where T:Control=>
        Descendants(root).OfType<T>().Single(match);

    static void InspectDialog(Form owner,Func<Button,bool> command,string title,Action<Form> inspect)
    {
        Exception? failure=null;
        using var timer=new System.Windows.Forms.Timer{Interval=120};
        timer.Tick+=(_,_)=>
        {
            timer.Stop();
            var modal=Application.OpenForms.Cast<Form>().LastOrDefault(form=>form!=owner&&form.Modal);
            if(modal is null)return;
            try
            {
                if(modal.Text!=title)throw new Exception($"Expected {title}, opened {modal.Text}.");
                inspect(modal);
            }
            catch(Exception error){failure=error;}
            finally{modal.Close();}
        };
        timer.Start();
        Find<Button>(owner,command).PerformClick();
        if(timer.Enabled||failure is not null)throw new Exception($"Could not verify {title}.",failure);
    }

    static IEnumerable<Control> Descendants(Control root)
    {
        foreach(Control control in root.Controls)
        {
            yield return control;
            foreach(var child in Descendants(control))yield return child;
        }
    }

    static string WithSuffix(string path,string suffix)=>Path.Combine(Path.GetDirectoryName(path)!,
        Path.GetFileNameWithoutExtension(path)+suffix+Path.GetExtension(path));

    static void Capture(Form window,string path)
    {
        window.PerformLayout();
        Application.DoEvents();
        using var bitmap=new Bitmap(window.Width,window.Height);
        window.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
        bitmap.Save(path,ImageFormat.Png);
    }
}
