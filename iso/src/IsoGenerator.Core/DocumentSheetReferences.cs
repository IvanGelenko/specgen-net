namespace IsoGenerator.Core;

public sealed record LocalSheetLink(string Mark,int Target);
public sealed record ExternalSheetLink(string Line,string OwnerPort,string Handle,string Port);

public static class DocumentSheetReferences
{
    internal static LocalSheetLink? ReadLocal(Annotation annotation)
    {
        if(annotation.LocalLink is {} link)return link;
        const string separator=" / лист ";
        int split=annotation.Text.LastIndexOf(separator,StringComparison.Ordinal);
        return split>0&&int.TryParse(annotation.Text.AsSpan(split+separator.Length),out int target)
            ?new(annotation.Text[..split],target):null;
    }

    internal static Annotation RenumberLocal(Annotation annotation,IReadOnlyDictionary<int,int> numbers)
    {
        if(ReadLocal(annotation) is not {} link)return annotation;
        int target=numbers[link.Target];
        return annotation with{Text=$"{link.Mark} / лист {target}",LocalLink=link with{Target=target}};
    }

    public static SheetScene Apply(PlannedSheet sheet,string line,int offset,
        IReadOnlyDictionary<string,(int Offset,IReadOnlyList<PlannedSheet> Sheets)> document,
        bool wrapPageNumber=false,bool fitToPaper=false)
    {
        string pageSeparator=wrapPageNumber?"\nлист ":" лист ";
        string LocalName(string target,int number,int count)=>
            target+(count>1||document.Count>1?$"(L{number})":"");

        string ReferenceName(string name)
        {
            if(!fitToPaper)return name;
            int middle=name.Length/2;
            int suffix=name.IndexOf('(');
            int split=Enumerable.Range(0,suffix>=0?suffix:name.Length)
                .Where(i=>name[i]=='-').OrderBy(i=>Math.Abs(i-middle)).FirstOrDefault(-1);
            return split>=0?name.Insert(split+1,"\n"):name;
        }

        Annotation Update(Annotation label)
        {
            if(label.Role=="continuation"&&ReadLocal(label) is {} localLink)
                return label with{Text=$"{localLink.Mark} / см. {ReferenceName(LocalName(line,localLink.Target,sheet.Total))}{pageSeparator}{offset+localLink.Target}"};
            if(label.Role!="external-line")return label;

            int breakAt=label.Text.IndexOf('\n');
            string first=breakAt>=0?label.Text[..breakAt]:label.Text;
            string? targetLine=label.ExternalLink?.Line??(first.StartsWith("см. ",StringComparison.Ordinal)?first[4..]:null);
            if(targetLine is null||!document.TryGetValue(targetLine,out var other))return label;

            var candidates=other.Sheets.Where(item=>!item.Scene.BomOnly).ToArray();
            if(candidates.Length>1)
            {
                var contexts=(sheet.Snapshot.ExternalLineContexts??Array.Empty<ExternalLineContext>())
                    .Where(ctx=>StringComparer.OrdinalIgnoreCase.Equals(ctx.OwnerHandle,label.SourceId)&&
                        StringComparer.OrdinalIgnoreCase.Equals(ctx.Line,targetLine)&&
                        (label.ExternalLink is not {} link||
                         StringComparer.OrdinalIgnoreCase.Equals(link.OwnerPort,ctx.OwnerPort)&&
                         StringComparer.OrdinalIgnoreCase.Equals(link.Handle,ctx.EntryHandle)&&
                         StringComparer.OrdinalIgnoreCase.Equals(link.Port,ctx.EntryPort))).ToArray();
                candidates=candidates.Where(item=>contexts.Any(ctx=>item.Snapshot.Parts.Any(part=>MatchesEntry(part,ctx)))).ToArray();
            }
            if(candidates.Length!=1)return label;
            int local=candidates[0].Number;
            return label with{Text=$"см. {ReferenceName(LocalName(targetLine,local,other.Sheets.Count))}{pageSeparator}{other.Offset+local}"+
                (breakAt>=0?label.Text[breakAt..]:"")};
        }

        var labels=sheet.Scene.Annotations.Select(Update).ToArray();
        if(labels.SequenceEqual(sheet.Scene.Annotations))return sheet.Scene;
        var strokes=sheet.Scene.Strokes.Where(stroke=>stroke.Role is not ("elevation-leader" or "elevation-shelf" or "position-leader")).ToList();
        var fixedGeometry=fitToPaper?SheetComposition.TryBounds(sheet.Scene with
        {
            Strokes=strokes,Annotations=Array.Empty<Annotation>()
        }):null;
        var placed=AnnotationPlacement.Place(labels,strokes,sheet.Scene.Symbols??Array.Empty<SymbolGlyph>(),sheet.Scene.Dimensions,
            fixedGeometry:fixedGeometry,reuseBendPositions:true);
        return sheet.Scene with{Annotations=placed,Strokes=strokes};
    }

    static bool MatchesEntry(Part part,ExternalLineContext context)
    {
        if(StringComparer.OrdinalIgnoreCase.Equals(part.Handle,context.EntryHandle))return true;
        if(!StringComparer.OrdinalIgnoreCase.Equals(part.Properties.GetValueOrDefault("IsoOriginalHandle"),context.EntryHandle))return false;
        var location=context.Parts.FirstOrDefault(p=>StringComparer.OrdinalIgnoreCase.Equals(p.Handle,context.EntryHandle))?
            .Ports.FirstOrDefault(p=>StringComparer.OrdinalIgnoreCase.Equals(p.Name,context.EntryPort))?.Position;
        return location is {} point&&part.Ports.Any(port=>port.Position.DistanceTo(point)<1);
    }
}

