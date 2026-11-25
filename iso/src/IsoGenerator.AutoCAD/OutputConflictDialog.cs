using System.Windows.Forms;
namespace IsoGenerator.AutoCAD;
internal static class OutputConflictDialog
{
    public static DialogResult Choose(string folder,IWin32Window? owner=null,bool allowAppend=false,bool rebuildArchived=false)
    {
        using var form=new Form{Text="Файлы изометрии уже существуют",ClientSize=new System.Drawing.Size(allowAppend?650:475,170),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterScreen,MinimizeBox=false,MaximizeBox=false};
        string detail=allowAppend&&rebuildArchived
            ?"При добавлении линий прежние листы будут пересчитаны из сохранённых данных модели."
            :"Выберите способ сохранения комплекта.";
        form.Controls.Add(new Label{Text="В папке уже есть файлы этого выпуска.\n"+detail+"\n"+folder,
            Left=18,Top=18,Width=allowAppend?610:438,Height=85,AutoEllipsis=true});
        var replace=new Button{Text="Заменить",Left=18,Top=120,Width=125,Height=32,DialogResult=DialogResult.Yes};
        var indexed=new Button{Text="Добавить индекс",Left=155,Top=120,Width=165,Height=32,DialogResult=DialogResult.No};
        var cancel=new Button{Text="Отмена",Left=allowAppend?507:332,Top=120,Width=125,Height=32,DialogResult=DialogResult.Cancel};
        form.Controls.AddRange(new Control[]{replace,indexed,cancel});
        if(allowAppend)
            form.Controls.Add(new Button{Text="Добавить линии",Left=332,Top=120,Width=165,Height=32,DialogResult=DialogResult.Retry});form.AcceptButton=indexed;form.CancelButton=cancel;
        return owner is null?form.ShowDialog():form.ShowDialog(owner);
    }
}
