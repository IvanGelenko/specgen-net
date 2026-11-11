using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
using IsoGenerator.AutoCAD;
internal static class Program
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Pump(){for(int i=0;i<10;i++){Application.DoEvents();Thread.Sleep(5);}}
    [STAThread] static int Main(string[] args)
    {
        try
        {
            ApplicationConfiguration.Initialize();Directory.CreateDirectory(args[0]);
            var view=new IsoProgressWindow();
            var source=(CancellationTokenSource)typeof(IsoProgressWindow).GetField("cancellation",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)!;
            var type=typeof(IsoProgressWindow).GetNestedType("ProgressForm",BindingFlags.NonPublic)!;
            using var form=(Form)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{"150-KSN001-LS016C-HN",source},null)!;
            typeof(IsoProgressWindow).GetField("form",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(view,form);
            form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);form.Show();Pump();
            var bar=form.Controls.OfType<ProgressBar>().Single();var cancel=form.Controls.OfType<Button>().Single(b=>b.Text=="Отмена");
            Check(bar.Style==ProgressBarStyle.Marquee,"Unknown duration must not display a fabricated percentage");
            Task.Run(()=>view.Report("Этап 3 из 6 — расчёт компоновки","Проверено вариантов: 42")).GetAwaiter().GetResult();Pump();
            Check(form.Controls.OfType<TextBox>().Single().Text.Contains("42"),"Background progress must reach the UI thread");
            Save(form,Path.Combine(args[0],"planning.png"));
            view.Report("Этап 4 из 6 — выпуск листов","Создаётся лист 3 из 6. Готово: 2",2,6);Pump();
            Check(bar.Style==ProgressBarStyle.Continuous&&bar.Value==2&&bar.Maximum==6,"Sheet progress must represent completed pages");Save(form,Path.Combine(args[0],"rendering.png"));
            cancel.PerformClick();Pump();Check(view.Token.IsCancellationRequested&&!cancel.Enabled&&!form.IsDisposed,"Cancellation keeps the window alive until work has stopped");
            view.Finish("Создание отменено","Итоговые файлы не изменены.");Pump();Check(cancel.Enabled&&cancel.Text=="Закрыть","Cancelled state must allow closing");Save(form,Path.Combine(args[0],"cancelled.png"));
            var second=new IsoProgressWindow();
            var secondSource=(CancellationTokenSource)typeof(IsoProgressWindow).GetField("cancellation",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(second)!;
            using var saving=(Form)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{"150-KSN001-LS016C-HN",secondSource},null)!;
            typeof(IsoProgressWindow).GetField("form",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(second,saving);
            saving.StartPosition=FormStartPosition.Manual;saving.Location=new Point(-32000,-32000);saving.Show();Pump();
            var publishing=Task.Run(async()=>await second.BeginPublication());
            while(!publishing.IsCompleted)Pump();publishing.GetAwaiter().GetResult();
            saving.Close();Pump();Check(!saving.IsDisposed&&!second.Token.IsCancellationRequested,"Final publication must not be interrupted by closing the window");
            second.Finish("Изометрия создана","Листов: 6. DWG и общий PDF сохранены.",true,args[0]);Pump();
            Check(saving.Controls.OfType<ProgressBar>().Single().Value==100,"100 percent is only shown after successful publication");Save(saving,Path.Combine(args[0],"completed.png"));
            Console.WriteLine("Progress UI checks passed: unknown duration, background reporting, real page progress, cancellation, completion.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    static void Save(Form form,string path){using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(path);}
}
