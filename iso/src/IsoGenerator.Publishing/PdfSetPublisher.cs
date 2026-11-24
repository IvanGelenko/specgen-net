using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
namespace IsoGenerator.Publishing;
public static class PdfSetPublisher
{
    public static void Merge(IReadOnlyList<string> sheets,string output,string title)=>
        Save(sheets,output,title,null,null);

    public static int PageCount(string file)
    {
        using var document=PdfReader.Open(file,PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    public static void MergeReport(string cover,string contents,IReadOnlyList<string> drawings,string output,string title)=>
        Save(drawings,output,title,cover,contents);

    public static void MergeWithRetainedPages(IReadOnlyList<string> drawings,string output,string title,
        string? cover,string? contents,string previousPdf,IReadOnlySet<int> retainedDrawings)=>
        Save(drawings,output,title,cover,contents,previousPdf,retainedDrawings);

    static void Save(IReadOnlyList<string> sheets,string output,string title,string? cover,string? contents,
        string? previousPdf=null,IReadOnlySet<int>? retainedDrawings=null)
    {
        if(sheets.Count==0)throw new InvalidDataException("Нет листов для PDF.");
        if(File.Exists(output))throw new IOException("PDF уже существует: "+output);
        using var document=new PdfDocument();document.Info.Title=title;document.Info.Creator="SpecGen";
        using var contentsDocument=contents is null?null:PdfReader.Open(contents,PdfDocumentOpenMode.Import);
        if(cover is not null)
        {
            using var titleDocument=PdfReader.Open(cover,PdfDocumentOpenMode.Import);
            if(titleDocument.PageCount!=1)throw new InvalidDataException("Титульный лист должен занимать одну страницу.");
            document.AddPage(titleDocument.Pages[0]);
        }
        if(contentsDocument is not null)
        {
            if(contentsDocument.PageCount<2)throw new InvalidDataException("Шаблон должен содержать страницы содержания и лист регистрации изменений.");
            for(int i=0;i<contentsDocument.PageCount-1;i++)document.AddPage(contentsDocument.Pages[i]);
        }
        int frontPages=document.PageCount;
        using var previous=previousPdf is null?null:PdfReader.Open(previousPdf,PdfDocumentOpenMode.Import);
        if(previous is not null&&previous.PageCount!=frontPages+sheets.Count+(contentsDocument is null?0:1))
            throw new InvalidDataException("����� ������� ������������� PDF ����������. ��������� ������ ���������.");
        for(int i=0;i<sheets.Count;i++)
        {
            if(previous is not null&&retainedDrawings?.Contains(i)==true)
            {
                document.AddPage(previous.Pages[frontPages+i]);
                continue;
            }
            var sheet=sheets[i];
            using var source=PdfReader.Open(sheet,PdfDocumentOpenMode.Import);
            if(source.PageCount!=1)throw new InvalidDataException("Ожидался один лист в промежуточном PDF: "+sheet);
            document.AddPage(source.Pages[0]);
        }
        if(contentsDocument is not null)document.AddPage(contentsDocument.Pages[contentsDocument.PageCount-1]);
        // CreateNew preserves the no-overwrite contract even if a file appears during generation.
        using var destination=new FileStream(output,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
        document.Save(destination,false);
    }
}
