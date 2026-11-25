using System.Security.Cryptography;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
namespace IsoGenerator.Publishing;
public static class PdfSetPublisher
{
    const string SourceSheetKey="/SpecGenSourceSheet";

    public static void Merge(IReadOnlyList<string> sheets,string output,string title)=>
        Save(sheets,output,title,null,null);

    public static int PageCount(string file)
    {
        using var document=PdfReader.Open(file,PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    public static void MergeReport(string cover,string contents,IReadOnlyList<string> drawings,string output,string title)=>
        Save(drawings,output,title,cover,contents);

    public static int MergeWithRetainedPages(IReadOnlyList<string> drawings,string output,string title,
        string? cover,string? contents,string previousPdf,IReadOnlySet<int> retainedDrawings)=>
        Save(drawings,output,title,cover,contents,previousPdf,retainedDrawings);

    static int Save(IReadOnlyList<string> sheets,string output,string title,string? cover,string? contents,
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
        using var previous=previousPdf is null?null:PdfReader.Open(previousPdf,PdfDocumentOpenMode.Import);
        var previousSheets=previous?.Pages.Cast<PdfPage>()
            .GroupBy(page=>page.Elements.GetString(SourceSheetKey),StringComparer.Ordinal)
            .Where(group=>group.Key.Length!=0&&group.Count()==1)
            .ToDictionary(group=>group.Key,group=>group.Single(),StringComparer.Ordinal);
        int retained=0;
        for(int i=0;i<sheets.Count;i++)
        {
            using var input=File.OpenRead(sheets[i]);
            string sourceHash=Convert.ToHexString(SHA256.HashData(input));
            input.Position=0;
            using var source=PdfReader.Open(input,PdfDocumentOpenMode.Import);
            if(source.PageCount!=1)throw new InvalidDataException("Ожидался один лист в промежуточном PDF: "+sheets[i]);
            var page=source.Pages[0];
            if(retainedDrawings?.Contains(i)==true&&previousSheets is not null&&
                previousSheets.Remove(sourceHash,out var previousSheet)&&MatchesDrawing(previousSheet,page))
            {
                page=previousSheet;
                retained++;
            }
            document.AddPage(page).Elements.SetString(SourceSheetKey,sourceHash);
        }
        if(contentsDocument is not null)document.AddPage(contentsDocument.Pages[contentsDocument.PageCount-1]);
        // CreateNew preserves the no-overwrite contract even if a file appears during generation.
        using var destination=new FileStream(output,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
        document.Save(destination,false);
        return retained;
    }

    // Drawing commands can stay identical while resources change what they render.
    static bool MatchesDrawing(PdfPage previous,PdfPage source)=>
        previous.Rotate==source.Rotate&&previous.MediaBoxReadOnly==source.MediaBoxReadOnly&&
        previous.CropBoxReadOnly==source.CropBoxReadOnly&&
        previous.BleedBoxReadOnly==source.BleedBoxReadOnly&&
        previous.TrimBoxReadOnly==source.TrimBoxReadOnly&&previous.ArtBoxReadOnly==source.ArtBoxReadOnly&&
        previous.Contents.CreateSingleContent().Stream.Value.AsSpan()
            .SequenceEqual(source.Contents.CreateSingleContent().Stream.Value)&&
        MatchesPdfObjects(previous.Elements["/Resources"],source.Elements["/Resources"])&&
        HasPublishedPageSettings(previous);

    static bool HasPublishedPageSettings(PdfPage page)
    {
        // PDFsharp's page import omits /UserUnit and /Group. Its writer adds this RGB group.
        var groupItem=page.Elements["/Group"];
        if(groupItem is PdfReference reference)groupItem=reference.Value;
        return page.Elements["/UserUnit"] is null&&groupItem is PdfDictionary group&&group.Stream is null&&
            group.Elements.Count==2&&group.Elements["/CS"] is PdfName { Value:"/DeviceRGB" }&&
            group.Elements["/S"] is PdfName { Value:"/Transparency" };
    }

    static bool MatchesPdfObjects(PdfItem? previous,PdfItem? source)
    {
        var pending=new Stack<(PdfItem? Previous,PdfItem? Source)>();
        var visited=new HashSet<(PdfItem Previous,PdfItem Source)>();
        pending.Push((previous,source));
        while(pending.TryPop(out var pair))
        {
            var (left,right)=pair;
            if(left is PdfReference leftReference)left=leftReference.Value;
            if(right is PdfReference rightReference)right=rightReference.Value;
            if(left is null||right is null)
            {
                if(pair.Previous is not null||pair.Source is not null)return false;
                continue;
            }
            if(!visited.Add((left,right)))continue;

            if(left is PdfDictionary leftDictionary&&right is PdfDictionary rightDictionary)
            {
                var leftStream=leftDictionary.Stream;
                var rightStream=rightDictionary.Stream;
                if(leftStream is null?rightStream is not null:
                    rightStream is null||!leftStream.Value.AsSpan().SequenceEqual(rightStream.Value))return false;
                if(leftDictionary.Elements.Count!=rightDictionary.Elements.Count)return false;
                foreach(string key in leftDictionary.Elements.Keys)
                    pending.Push((leftDictionary.Elements[key],rightDictionary.Elements[key]));
                continue;
            }
            if(left is PdfArray leftArray&&right is PdfArray rightArray)
            {
                if(leftArray.Elements.Count!=rightArray.Elements.Count)return false;
                for(int i=0;i<leftArray.Elements.Count;i++)
                    pending.Push((leftArray.Elements[i],rightArray.Elements[i]));
                continue;
            }
            if(!MatchesPdfValue(left,right))return false;
        }
        return true;
    }

    static bool MatchesPdfValue(PdfItem left,PdfItem right)=>(left,right) switch
    {
        (PdfBoolean a,PdfBoolean b)=>a.Value==b.Value,
        (PdfBooleanObject a,PdfBooleanObject b)=>a.Value==b.Value,
        (PdfInteger a,PdfInteger b)=>a.Value==b.Value,
        (PdfIntegerObject a,PdfIntegerObject b)=>a.Value==b.Value,
        (PdfLongInteger a,PdfLongInteger b)=>a.Value==b.Value,
        (PdfLongIntegerObject a,PdfLongIntegerObject b)=>a.Value==b.Value,
        (PdfReal a,PdfReal b)=>a.Value==b.Value,
        (PdfRealObject a,PdfRealObject b)=>a.Value==b.Value,
        // PDFsharp can write an integral real value without its decimal point.
        (PdfReal a,PdfInteger b)=>a.Value==b.Value,
        (PdfInteger a,PdfReal b)=>a.Value==b.Value,
        (PdfName a,PdfName b)=>a.Value==b.Value,
        (PdfNameObject a,PdfNameObject b)=>a.Value==b.Value,
        (PdfString a,PdfString b)=>a.Value==b.Value&&a.Encoding==b.Encoding,
        (PdfStringObject a,PdfStringObject b)=>a.Value==b.Value&&a.Encoding==b.Encoding,
        (PdfLiteral a,PdfLiteral b)=>a.Value==b.Value,
        (PdfNull,PdfNull)=>true,
        (PdfNullObject,PdfNullObject)=>true,
        _=>false
    };
}
