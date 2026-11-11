namespace IsoGenerator.Core;

public static class BomPagination
{
    public static IReadOnlyList<IReadOnlyList<BomRow>> Pages(IReadOnlyList<BomRow> rows)
    {
        var pages=new List<IReadOnlyList<BomRow>>();var page=new List<BomRow>();
        foreach(var row in rows)
        {
            if(!BomCategories.Fits(page.Append(row).ToArray()))
            {
                if(page.Count==0)throw new InvalidDataException("A BOM row does not fit an empty sheet.");
                pages.Add(page.ToArray());page.Clear();
            }
            page.Add(row);
        }
        if(page.Count>0||pages.Count==0)pages.Add(page.ToArray());
        return pages;
    }
    public static IReadOnlyList<PlannedSheet> Apply(IReadOnlyList<PlannedSheet> geometry)
    {
        var result=new List<PlannedSheet>();var continuation=new List<PlannedSheet>();
        foreach(var sheet in geometry)
        {
            var pages=Pages(sheet.Scene.BillOfMaterials);
            var fittings=sheet.Scene.BillOfMaterials.Where(r=>r.Category==BomCategory.Fittings).SelectMany(r=>r.Handles).ToArray();
            result.Add(sheet with{Scene=sheet.Scene with{BillOfMaterials=pages[0],FittingSourceIds=fittings}});
            foreach(var rows in pages.Skip(1))
            {
                var empty=sheet.Snapshot with{Parts=Array.Empty<Part>(),Connections=Array.Empty<Connection>(),Accessories=Array.Empty<Part>()};
                var scene=sheet.Scene with
                {
                    Strokes=Array.Empty<Stroke>(),Symbols=Array.Empty<SymbolGlyph>(),Dimensions=Array.Empty<PipeDimension>(),Welds=Array.Empty<WeldMarker>(),
                    Annotations=new[]{new Annotation("","title",new(75,385),$"{sheet.Scene.Line} — продолжение спецификации листа {sheet.Number}",4)},
                    BillOfMaterials=rows,SourcePartCount=0,SourceGuids=new Dictionary<string,string>(),BomOnly=true,FittingSourceIds=Array.Empty<string>()
                };
                continuation.Add(new(0,0,empty,scene));
            }
        }
        // Append BOM-only sheets so diagram continuation references keep their sheet numbers.
        result.AddRange(continuation);int total=result.Count;
        return result.Select((s,i)=>s with{Number=i+1,Total=total,Scene=s.Scene with
        {
            Annotations=s.Scene.Annotations.Select(a=>a.Role=="title"?a with{Text=s.Scene.BomOnly?a.Text+$" — лист {i+1} из {total}":s.Scene.Line+$" — лист {i+1} из {total}"}:a).ToArray()
        }}).ToArray();
    }
}
