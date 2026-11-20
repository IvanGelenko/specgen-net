using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace IsoGenerator.Publishing;

public sealed record IsometricTitleDetails(string ObjectName,string UnitName,string DocumentNumber);
public sealed record IsometricReportDetails(string TitleTemplate,string ContentsTemplate,IsometricTitleDetails Title)
{
    public string StampDate {get;init;}=DateTimeOffset.Now.ToString("MM.yy",CultureInfo.InvariantCulture);
}

public static partial class IsometricDocumentTemplates
{
    static readonly XNamespace Word="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    const string Number="{{ISO_NUMBER}}", Object="{{ISO_OBJECT}}", Unit="{{ISO_UNIT}}";
    const string Sheet="{{ISO_SHEET}}", Drawing="{{ISO_DRAWING}}";
    const string ContentsSheets="{{ISO_CONTENTS_SHEETS}}", Total="{{ISO_TOTAL_SHEETS}}", Final="{{ISO_FINAL_SHEET}}";

    public static IsometricTitleDetails ReadTitle(string template)
    {
        using var package=ZipFile.OpenRead(template);
        using var data=(package.GetEntry("word/document.xml")??throw new InvalidDataException("В титульном листе отсутствует текст DOCX.")).Open();
        var document=XDocument.Load(data);
        var body=document.Root?.Element(Word+"body")??throw new InvalidDataException("Не найдено содержимое титульного листа.");

        var tags=new[]{Object,Unit,Number};
        var tagged=tags.Select(tag=>
        {
            string key=tag.Trim('{','}');
            var control=body.Descendants(Word+"sdt").SingleOrDefault(item=>
                (string?)item.Element(Word+"sdtPr")?.Element(Word+"tag")?.Attribute(Word+"val")==key);
            var content=control?.Element(Word+"sdtContent");
            if(content is null||control!.Element(Word+"sdtPr")!.Element(Word+"showingPlcHdr") is not null)return "";
            var paragraphs=Paragraphs(content).ToArray();
            return (paragraphs.Length==0?string.Concat(content.Descendants(Word+"t").Select(node=>node.Value)):
                string.Join(" ",paragraphs.Select(Text))).Trim();
        }).ToArray();

        if(tagged.All(value=>value.Length>0))return new(tagged[0],tagged[1],tagged[2]);
        if(!body.Descendants(Word+"sdtPr").Any(props=>
            tags.Any(tag=>(string?)props.Element(Word+"tag")?.Attribute(Word+"val")==tag.Trim('{','}'))))
        {
            // The supplied untagged title has the object and unit immediately above
            // the document type, and the number below the isometry title.
            var paragraphs=body.Elements(Word+"p").Select(Text).Select(value=>value.Trim()).Where(value=>value.Length>0).ToArray();
            int type=Array.FindIndex(paragraphs,value=>value.Equals("РАБОЧАЯ ДОКУМЕНТАЦИЯ",StringComparison.OrdinalIgnoreCase)||
                value.Equals("ÐÀÁÎ×Àß ÄÎÊÓÌÅÍÒÀÖÈß",StringComparison.Ordinal));
            int title=Array.FindIndex(paragraphs,value=>value.Equals("Изометрические чертежи",StringComparison.OrdinalIgnoreCase));
            if(type>=2&&title>type&&title+1<paragraphs.Length)
                return new(paragraphs[type-2],paragraphs[type-1],paragraphs[title+1]);
        }
        throw new InvalidDataException("Не удалось прочитать объект, установку и номер документа. Заполните три поля в подготовленном шаблоне титульного листа.");
    }

    public static void PrepareTitle(string template,string file,bool trimTrailingParagraphs=false)
    {
        using var output=new FileStream(file,FileMode.CreateNew);
        Fill(template,output,new(),Array.Empty<string>(),(documents,_) =>
        {
            var document=documents[MainPart];
            TrimBlankTitleSection(document);
            if(!trimTrailingParagraphs)return;
            var body=document.Root!.Element(Word+"body")!;
            foreach(var paragraph in body.Elements().Reverse().SkipWhile(e=>e.Name==Word+"sectPr").ToArray())
            {
                if(paragraph.Name!=Word+"p"||paragraph.Element(Word+"pPr")?.Elements().Any(e=>
                    e.Name==Word+"sectPr"||e.Name==Word+"numPr"||e.Name==Word+"pBdr")==true||
                    paragraph.Elements().Any(e=>e.Name!=Word+"pPr"&&(e.Name!=Word+"r"||e.Elements().Any(child=>
                        child.Name!=Word+"rPr"&&(child.Name!=Word+"t"||!string.IsNullOrWhiteSpace(child.Value))))))break;
                paragraph.Remove();
            }

            // Word can attach the final section break to a nonempty paragraph (for example, the year).
            // Keep that paragraph and the preceding section's page setup.
            var finalSection=body.Element(Word+"sectPr");
            var precedingSection=finalSection?.ElementsBeforeSelf().LastOrDefault()?
                .Element(Word+"pPr")?.Element(Word+"sectPr");
            if(precedingSection is not null)
            {
                finalSection!.ReplaceWith(new XElement(precedingSection));
                precedingSection.Remove();
            }
        });
    }

    static void TrimBlankTitleSection(XDocument document)
    {
        // The supplied Word template ends with an empty section which prints a blank page.
        var body=document.Root!.Element(Word+"body")!;
        var trailingSection=body.Elements(Word+"p").LastOrDefault(p=>p.Element(Word+"pPr")?.Element(Word+"sectPr") is not null);
        if(trailingSection is null||HasContent(trailingSection)||trailingSection.ElementsAfterSelf().Any(element=>
            element.Name!=Word+"sectPr"&&(element.Name!=Word+"p"||HasContent(element))))return;
        var section=new XElement(trailingSection.Element(Word+"pPr")!.Element(Word+"sectPr")!);
        trailingSection.ElementsAfterSelf().Remove();
        trailingSection.Remove();
        body.Add(section);

        static bool HasContent(XElement element)=>element.Descendants().Any(node=>
            node.Name==Word+"drawing"||node.Name==Word+"pict"||node.Name==Word+"object"||
            node.Name==Word+"t"&&!string.IsNullOrWhiteSpace(node.Value));
    }

    public static void ValidateContents(IsometricReportDetails details,string number)
    {
        using var probe=new MemoryStream();
        FillContents(probe,details,number,new[]{"Изометрические чертежи"},1);
    }

    public static void FillContents(string file,IsometricReportDetails details,string number,
        IReadOnlyList<string> drawingTitles,int contentsPageCount)
    {
        using var output=new FileStream(file,FileMode.CreateNew);
        FillContents(output,details,number,drawingTitles,contentsPageCount);
    }

    static void FillContents(Stream output,IsometricReportDetails details,string number,
        IReadOnlyList<string> drawingTitles,int contentsPageCount)
    {
        var fields=Fields(details,number);
        int final=contentsPageCount+drawingTitles.Count+2;
        fields.Add(ContentsSheets,contentsPageCount==1?"2":$"2–{contentsPageCount+1}");
        fields.Add(Total,final.ToString(CultureInfo.InvariantCulture));
        fields.Add(Final,final.ToString(CultureInfo.InvariantCulture));
        var required=fields.Keys.ToArray();
        // Earlier editable samples contained empty slots for the in-app executor editor.
        foreach(string role in new[]{"DEVELOPER","CHECKED","HEAD","CONTROL","APPROVED"})
            fields.Add("{{ISO_NAME_"+role+"}}","");
        fields.Add("{{ISO_DATE}}","");

        Fill(details.ContentsTemplate,output,fields,required,(documents,input)=>
        {
            var body=documents[MainPart].Root!.Element(Word+"body")!;
            var marked=body.Descendants(Word+"tr").FirstOrDefault(row=>
                Paragraphs(row).Any(p=>Text(p).Contains(Drawing,StringComparison.Ordinal)));
            var row=marked??PrepareLegacyContents(body,documents,input);
            ApplyStampDate(documents.Values,details.StampDate);
            var firstSection=body.Elements(Word+"p").Select(p=>p.Element(Word+"pPr")?.Element(Word+"sectPr"))
                .FirstOrDefault(section=>section is not null)??throw ContentsFormatError();
            var pageNumbers=firstSection.Element(Word+"pgNumType");
            if(pageNumbers is null)
            {
                pageNumbers=new XElement(Word+"pgNumType");
                var following=firstSection.Elements().FirstOrDefault(e=>e.Name.LocalName is "cols" or "formProt" or
                    "vAlign" or "noEndnote" or "titlePg" or "textDirection" or "bidi" or "rtlGutter" or "docGrid" or "printerSettings" or "sectPrChange");
                if(following is null)firstSection.Add(pageNumbers);else following.AddBeforeSelf(pageNumbers);
            }
            pageNumbers.SetAttributeValue(Word+"start",2);
            if(!Paragraphs(row).Any(p=>Text(p).Contains(Sheet,StringComparison.Ordinal)))
                throw ContentsFormatError();
            for(int i=0;i<drawingTitles.Count;i++)
            {
                var item=new XElement(row);
                var rowValues=new Dictionary<string,string>
                {
                    [Sheet]=(contentsPageCount+i+2).ToString(CultureInfo.InvariantCulture),
                    [Drawing]=KeepPipelineNameTogether(drawingTitles[i])
                };
                Replace(item,rowValues,new HashSet<string>(StringComparer.Ordinal));
                foreach(var text in item.Descendants(Word+"t").Where(t=>t.Value.Contains('\n')).ToArray())
                {
                    var lines=text.Value.Split('\n');
                    text.Value=lines[0];
                    XNode last=text;
                    foreach(string line in lines.Skip(1))
                    {
                        var next=new XElement(Word+"t",new XAttribute(XNamespace.Xml+"space","preserve"),line);
                        last.AddAfterSelf(new XElement(Word+"br"),next);
                        last=next;
                    }
                }
                row.AddBeforeSelf(item);
            }
            row.Remove();
        });
    }

    static string KeepPipelineNameTogether(string title)
    {
        const string separator=" (монтажная): ";
        int start=title.IndexOf(separator,StringComparison.Ordinal);
        if(start<0)return title;
        start+=separator.Length;
        return title[..(start-1)]+"\n"+title[start..];
    }

    static Dictionary<string,string> Fields(IsometricReportDetails details,string number)=>
        new(){{Number,number},{Object,details.Title.ObjectName},{Unit,details.Title.UnitName}};

    const string MainPart="word/document.xml";

    static void Fill(string template,Stream output,Dictionary<string,string> values,IReadOnlyList<string> required,
        Action<Dictionary<string,XDocument>,ZipArchive> prepare)
    {
        using var input=ZipFile.OpenRead(template);
        var documents=input.Entries.Where(entry=>entry.FullName==MainPart||
            (entry.FullName.StartsWith("word/footer",StringComparison.Ordinal)||entry.FullName.StartsWith("word/header",StringComparison.Ordinal))&&
            entry.FullName.EndsWith(".xml",StringComparison.Ordinal))
            .ToDictionary(entry=>entry.FullName,ReadPart,StringComparer.Ordinal);
        if(!documents.ContainsKey(MainPart))throw new InvalidDataException("В шаблоне отсутствует текст DOCX.");
        prepare(documents,input);

        using var result=new ZipArchive(output,ZipArchiveMode.Create,leaveOpen:true);
        var replaced=new HashSet<string>(StringComparer.Ordinal);
        foreach(var entry in input.Entries)
        {
            var target=result.CreateEntry(entry.FullName,CompressionLevel.Optimal);
            using var destination=target.Open();
            if(documents.TryGetValue(entry.FullName,out var document))
            {
                Replace(document.Root!,values,replaced);
                document.Save(destination,SaveOptions.DisableFormatting);
            }
            else
            {
                using var source=entry.Open();
                source.CopyTo(destination);
            }
        }
        var missing=required.Where(key=>!replaced.Contains(key)).ToArray();
        if(missing.Length>0)throw new InvalidDataException($"В шаблоне «{Path.GetFileName(template)}» отсутствуют поля: "+string.Join(", ",missing));
    }

    static XDocument ReadPart(ZipArchiveEntry entry)
    {
        using var source=entry.Open();
        return XDocument.Load(source,LoadOptions.PreserveWhitespace);
    }

    static IEnumerable<XElement> Paragraphs(XElement source)=>source.DescendantsAndSelf(Word+"p");

    static XElement[] Runs(XElement paragraph)=>paragraph.Descendants(Word+"t")
        .Where(text=>text.Ancestors(Word+"p").First()==paragraph).ToArray();

    static string Text(XElement paragraph)=>string.Concat(Runs(paragraph).Select(node=>node.Value));

    static void Replace(XElement root,Dictionary<string,string> values,HashSet<string> replaced)
    {
        foreach(var paragraph in Paragraphs(root))
        {
            var runs=Runs(paragraph);
            string text=string.Concat(runs.Select(node=>node.Value));
            // Process positions from right to left so replacement values stay literal and
            // existing formatting outside each token stays attached to the original runs.
            var matches=new List<(int Start,string Token,string Value)>();
            foreach(var (token,value) in values)
            {
                for(int start=text.IndexOf(token,StringComparison.Ordinal);start>=0;
                    start=text.IndexOf(token,start+token.Length,StringComparison.Ordinal))
                    matches.Add((start,token,value));
            }
            foreach(var (start,token,value) in matches.OrderByDescending(match=>match.Start))
            {
                int offset=0,end=start+token.Length;
                foreach(var run in runs)
                {
                    string original=run.Value;
                    int next=offset+original.Length;
                    if(offset<end&&next>start)
                    {
                        int from=Math.Max(0,start-offset),to=Math.Min(original.Length,end-offset);
                        string replacement=start>=offset?value:"";
                        run.Value=original[..from]+replacement+original[to..];
                        run.SetAttributeValue(XNamespace.Xml+"space","preserve");
                    }
                    offset=next;
                    if(offset>=end)break;
                }
                replaced.Add(token);
            }
        }
    }
}
