using System.IO.Compression;
using System.Xml.Linq;

namespace IsoGenerator.Publishing;

public static partial class IsometricDocumentTemplates
{
    static readonly XNamespace Relationships="http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    static InvalidDataException ContentsFormatError()=>
        new("Не удалось распознать шаблон содержания. Используйте исходную таблицу «Лист / Наименование / Примечание» со строкой образца и штампами или сохраните образец из окна «Реквизиты документов».");

    static XElement PrepareLegacyContents(XElement body,Dictionary<string,XDocument> documents,ZipArchive package)
    {
        var table=body.Elements(Word+"tbl").FirstOrDefault(candidate=>
            candidate.Elements(Word+"tr").FirstOrDefault()?.Elements(Word+"tc")
                .Select(cell=>CellText(cell)).SequenceEqual(new[]{"Лист","Наименование","Примечание"},StringComparer.OrdinalIgnoreCase)==true)
            ??throw ContentsFormatError();
        var rows=table.Elements(Word+"tr").ToArray();
        if(rows.Length<3)throw ContentsFormatError();
        var heading=rows[0].Element(Word+"trPr");
        if(heading is null){heading=new XElement(Word+"trPr");rows[0].AddFirst(heading);}
        if(heading.Element(Word+"tblHeader") is null)heading.Add(new XElement(Word+"tblHeader"));
        SetCellText(Cell(rows,1,0),ContentsSheets);
        SetCellText(Cell(rows,2,0),Sheet);
        var drawingCell=Cell(rows,2,1);
        SetCellText(drawingCell,Drawing);
        drawingCell.Element(Word+"tcPr")?.Element(Word+"tcFitText")?.Remove();
        rows.Skip(3).Remove();

        var firstSection=body.Elements(Word+"p").Select(p=>p.Element(Word+"pPr")?.Element(Word+"sectPr"))
            .FirstOrDefault(section=>section is not null)??throw ContentsFormatError();
        var finalSection=body.Element(Word+"sectPr")??throw ContentsFormatError();
        var relations=ReadPart(package.GetEntry("word/_rels/document.xml.rels")??throw ContentsFormatError());
        string? PartPath(XElement? reference)
        {
            var id=(string?)reference?.Attribute(Relationships+"id");
            var target=(string?)relations.Root!.Elements().FirstOrDefault(e=>(string?)e.Attribute("Id")==id)?.Attribute("Target");
            return target is null?null:target.StartsWith('/')?target.TrimStart('/'):"word/"+target;
        }
        XDocument? FindPart(XElement? reference)=>PartPath(reference) is string path&&documents.TryGetValue(path,out var value)
            ?value:null;
        XDocument Part(XElement? reference)=>FindPart(reference)??throw ContentsFormatError();
        static XElement? PartRef(XElement section,string kind,string type)=>
            section.Elements(Word+kind+"Reference").FirstOrDefault(e=>(string?)e.Attribute(Word+"type")==type);

        static XElement[] StampRows(XDocument? part)=>
            part?.Root?.Element(Word+"tbl")?.Elements(Word+"tr").ToArray()??Array.Empty<XElement>();
        static bool HasCellLabel(XElement[] rows,int row,int column,string label)=>
            rows.ElementAtOrDefault(row)?.Elements(Word+"tc").ElementAtOrDefault(column) is {} cell&&
            CellText(cell).Equals(label,StringComparison.OrdinalIgnoreCase);
        var defaultFooter=FindPart(PartRef(firstSection,"footer","default"));
        var firstRows=StampRows(FindPart(PartRef(firstSection,"footer","first")));
        bool fullOnFirst=HasCellLabel(firstRows,5,7,"Листов");
        var fullRows=fullOnFirst?firstRows:StampRows(defaultFooter);
        var register=Part(PartRef(finalSection,"footer","default"));
        var registerRows=StampRows(register);
        if(!HasCellLabel(fullRows,5,6,"Лист")||!HasCellLabel(fullRows,5,7,"Листов")||
           !HasCellLabel(registerRows,0,7,"Лист"))throw ContentsFormatError();

        SetCellText(Cell(fullRows,0,6),Number);
        SetCellText(Cell(fullRows,2,6),Object);
        SetCellText(Cell(fullRows,5,4),Unit);
        SetCellText(Cell(fullRows,6,7),Total);
        var sheetCell=Cell(fullRows,6,6);
        if(!sheetCell.Descendants(Word+"fldSimple").Any()&&!sheetCell.Descendants(Word+"instrText").Any())
        {
            SetCellText(sheetCell,"2");
            var run=sheetCell.Descendants(Word+"t").First().Parent!;
            run.ReplaceWith(new XElement(Word+"fldSimple",new XAttribute(Word+"instr"," PAGE "),new XElement(run)));
        }
        SetCellText(Cell(registerRows,0,6),Number);

        // The supplied unmarked template has spare empty parts for even pages.
        // Reuse them for the small stamp without changing the registration page.
        var smallFooterRef=PartRef(firstSection,"footer","even");
        var smallHeaderRef=PartRef(firstSection,"header","even");
        var registrationHeaderRef=PartRef(finalSection,"header","default");
        if(fullOnFirst)
        {
            foreach(var footer in new[]{defaultFooter,FindPart(smallFooterRef)}.Distinct())
            {
                var smallRows=StampRows(footer);
                if(HasCellLabel(smallRows,0,7,"Лист"))SetCellText(Cell(smallRows,0,6),Number);
            }
        }
        else if(CanCopyToSpare(smallFooterRef,PartRef(finalSection,"footer","default"))&&
           CanCopyToSpare(smallHeaderRef,registrationHeaderRef)&&
           PartRef(firstSection,"footer","first") is not null&&PartRef(firstSection,"header","first") is not null)
        {
            documents[PartPath(smallFooterRef)!]=new XDocument(register);
            var smallHeader=new XDocument(Part(registrationHeaderRef));
            foreach(var paragraph in Paragraphs(smallHeader.Root!).Where(p=>Text(p).Trim()=="ЛИСТ РЕГИСТРАЦИИ ИЗМЕНЕНИЙ"))
                foreach(var text in Runs(paragraph))text.Value="";
            documents[PartPath(smallHeaderRef)!]=smallHeader;
            foreach(string kind in new[]{"header","footer"})
            {
                var defaultRef=PartRef(firstSection,kind,"default")??throw ContentsFormatError();
                var firstRef=PartRef(firstSection,kind,"first")!;
                firstRef.SetAttributeValue(Relationships+"id",(string?)defaultRef.Attribute(Relationships+"id"));
                defaultRef.SetAttributeValue(Relationships+"id",(string?)PartRef(firstSection,kind,"even")!.Attribute(Relationships+"id"));
            }
            if(firstSection.Element(Word+"titlePg") is null)
            {
                var at=firstSection.Elements().FirstOrDefault(e=>e.Name.LocalName is "textDirection" or "bidi" or "rtlGutter" or "docGrid" or "printerSettings" or "sectPrChange");
                if(at is null)firstSection.Add(new XElement(Word+"titlePg"));else at.AddBeforeSelf(new XElement(Word+"titlePg"));
            }
        }
        SetCellText(Cell(registerRows,1,7),Final,removePageField:true);
        return rows[2];

        bool CanCopyToSpare(XElement? spare,XElement? template)
        {
            if(PartPath(spare) is not string path||PartPath(template) is not string master||
               !documents.TryGetValue(path,out var destination)||destination.Root!.Elements().Any(e=>
                   e.DescendantsAndSelf().Any(node=>node.Name==Word+"tbl"||node.Name==Word+"drawing"||
                       node.Name==Word+"pict"||node.Name==Word+"t"&&!string.IsNullOrWhiteSpace(node.Value))))
                return false;
            return package.GetEntry("word/_rels/"+Path.GetFileName(master)+".rels") is null;
        }
    }

    static void ApplyStampDate(IEnumerable<XDocument> parts,string date)
    {
        foreach(var table in parts.Select(part=>part.Root?.Element(Word+"tbl")).OfType<XElement>())
        {
            var rows=table.Elements(Word+"tr").ToArray();
            if(rows.Length<11||rows[4].Elements(Word+"tc").ElementAtOrDefault(5) is not {} heading||
               !CellText(heading).Equals("Дата",StringComparison.OrdinalIgnoreCase))continue;
            foreach(var row in rows.Skip(5))
            {
                var cells=row.Elements(Word+"tc").ToArray();
                if(cells.Length>3&&CellText(cells[0]).TrimEnd('.').ToLowerInvariant() is
                   ("разраб" or "проверил" or "нач. отд" or "н. контр" or "утвердил"))
                    SetCellText(cells[3],date);
            }
        }
    }

    static XElement Cell(XElement[] rows,int row,int column)=>
        rows.ElementAtOrDefault(row)?.Elements(Word+"tc").ElementAtOrDefault(column)??throw ContentsFormatError();

    static string CellText(XElement cell)=>string.Join(" ",Paragraphs(cell).Select(Text)).Trim();

    static void SetCellText(XElement cell,string value,bool removePageField=false)
    {
        if(removePageField)
        {
            foreach(var field in cell.Descendants(Word+"fldSimple").ToArray())field.ReplaceWith(field.Nodes());
            cell.Descendants().Where(n=>n.Name==Word+"fldChar"||n.Name==Word+"instrText").Remove();
        }
        var text=cell.Descendants(Word+"t").ToArray();
        if(text.Length==0)
        {
            var paragraph=cell.Element(Word+"p")??throw ContentsFormatError();
            paragraph.Add(new XElement(Word+"r",new XElement(Word+"t",value)));
            return;
        }
        text[0].Value=value;
        foreach(var run in text.Skip(1))run.Value="";
    }
}

