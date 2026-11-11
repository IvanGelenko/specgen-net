using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
namespace IsoGenerator.AutoCAD;

// A dedicated UI thread keeps capture feedback visible without pumping AutoCAD messages.
internal sealed class IsoProgressWindow
{
    static IsoProgressWindow? previousWindow;
    readonly CancellationTokenSource cancellation=new();
    ProgressForm form=null!;
    public CancellationToken Token=>cancellation.Token;
    public static IsoProgressWindow Start(string line,IntPtr owner)
    {
        var session=new IsoProgressWindow();var ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            try{session.form=new ProgressForm(line,session.cancellation);session.form.Show(new Owner(owner));ready.TrySetResult(true);System.Windows.Forms.Application.Run(session.form);}
            catch(Exception ex){ready.TrySetException(ex);}
        }){IsBackground=true,Name="SpecGen iso progress"};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();ready.Task.GetAwaiter().GetResult();
        var old=Interlocked.Exchange(ref previousWindow,session);old?.Post(()=>old.form.CloseIfFinished());return session;
    }
    public void Report(string stage,string detail,int completed=0,int total=0)=>Post(()=>form.Report(stage,detail,completed,total));
    public void Finish(string title,string detail,bool success=false,string? folder=null)=>Post(()=>form.Finish(title,detail,success,folder));
    void Post(Action action){try{if(!form.IsDisposed)form.BeginInvoke(action);}catch(InvalidOperationException){}}
    Task<T> Invoke<T>(Func<T> action)
    {
        var done=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try{form.BeginInvoke(new Action(()=>{try{done.SetResult(action());}catch(Exception ex){done.SetException(ex);}}));}catch(Exception ex){done.SetException(ex);}return done.Task;
    }
    public Task<DialogResult> ChooseOutput(string folder)=>Invoke(()=>OutputConflictDialog.Choose(folder,form));
    public Task<bool> BeginPublication()=>Invoke(()=>{Token.ThrowIfCancellationRequested();form.Publishing();return true;});
    sealed class Owner(IntPtr handle):IWin32Window {public IntPtr Handle=>handle;}
    sealed class ProgressForm:Form
    {
        readonly Label stage=new(),elapsed=new();readonly TextBox detail=new(){ReadOnly=true,Multiline=true,BorderStyle=BorderStyle.None,BackColor=SystemColors.Control,ScrollBars=ScrollBars.Vertical};readonly ProgressBar bar=new();readonly Button cancel=new(),open=new();
        readonly System.Windows.Forms.Timer timer=new(){Interval=500};readonly Stopwatch clock=Stopwatch.StartNew();readonly CancellationTokenSource cancellation;
        bool running=true,canCancel=true;string? folder;
        public ProgressForm(string line,CancellationTokenSource cancellation)
        {
            this.cancellation=cancellation;Text="Создание изометрии";ClientSize=new Size(560,275);Font=new Font("Segoe UI",10);
            FormBorderStyle=FormBorderStyle.FixedDialog;StartPosition=FormStartPosition.CenterScreen;MaximizeBox=false;MinimizeBox=true;ShowInTaskbar=true;
            Controls.Add(new Label{Text="Линия: "+line,Left=20,Top=18,Width=520,Height=28,AutoEllipsis=true});
            stage.SetBounds(20,55,520,28);stage.Font=new Font(Font,FontStyle.Bold);detail.SetBounds(20,90,520,55);
            bar.SetBounds(20,150,520,22);bar.Style=ProgressBarStyle.Marquee;bar.MarqueeAnimationSpeed=30;
            elapsed.Text="Прошло: 00:00:00";elapsed.SetBounds(20,185,350,24);cancel.SetBounds(410,225,130,32);cancel.Text="Отмена";cancel.Click+=(_,_)=>{if(running)RequestCancel();else Close();};
            open.SetBounds(255,225,145,32);open.Text="Открыть папку";open.Visible=false;open.Click+=(_,_)=>{if(folder is not null)try{Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,ex.Message,"Не удалось открыть папку");}};
            Controls.AddRange(new Control[]{stage,detail,bar,elapsed,cancel,open});timer.Tick+=(_,_)=>elapsed.Text="Прошло: "+clock.Elapsed.ToString(@"hh\:mm\:ss");timer.Start();
            FormClosing+=(_,e)=>{if(running){e.Cancel=true;RequestCancel();}};
            FormClosed+=(_,_)=>{timer.Dispose();cancellation.Dispose();};Report("Этап 1 из 6 — чтение модели","Подготовка данных трубопровода…",0,0);
        }
        public void CloseIfFinished(){if(!running)Close();}
        void RequestCancel(){if(!canCancel)return;canCancel=false;cancel.Enabled=false;cancellation.Cancel();detail.Text="Отмена… Завершаем текущий шаг.";}
        public void Report(string title,string text,int completed,int total)
        {
            if(!canCancel&&running)return;stage.Text=title;detail.Text=text;
            bar.Style=total>0?ProgressBarStyle.Continuous:ProgressBarStyle.Marquee;
            if(total>0){bar.Maximum=total;bar.Value=Math.Clamp(completed,0,total);}
        }
        public void Publishing(){canCancel=false;cancel.Enabled=false;stage.Text="Этап 6 из 6 — сохранение комплекта";detail.Text="Сохраняем DWG и общий PDF…";bar.Style=ProgressBarStyle.Marquee;}
        public void Finish(string title,string text,bool success,string? outputFolder)
        {
            running=false;canCancel=false;clock.Stop();timer.Stop();stage.Text=title;detail.Text=text;bar.Style=ProgressBarStyle.Continuous;bar.Maximum=100;bar.Value=success?100:0;
            cancel.Text="Закрыть";cancel.Enabled=true;folder=outputFolder;open.Visible=folder is not null;
        }
    }
}
