using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
namespace IsoGenerator.Publishing;
public static class PdfSetPublisher
{
    public static void Merge(IReadOnlyList<string> sheets,string output,string title)
    {
        if(sheets.Count==0)throw new InvalidDataException("Нет листов для PDF.");
        if(File.Exists(output))throw new IOException("PDF уже существует: "+output);
        using var document=new PdfDocument();document.Info.Title=title;document.Info.Creator="SpecGen";
        foreach(var sheet in sheets)
        {
            using var source=PdfReader.Open(sheet,PdfDocumentOpenMode.Import);
            if(source.PageCount!=1)throw new InvalidDataException("Ожидался один лист в промежуточном PDF: "+sheet);
            document.AddPage(source.Pages[0]);
        }
        // CreateNew preserves the no-overwrite contract even if a file appears during generation.
        using var destination=new FileStream(output,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
        document.Save(destination,false);
    }
}
