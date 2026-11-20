namespace IsoGenerator.Core;

public sealed record DocumentDrawing(PlannedSheet Sheet,SheetScene Scene,int PageNumber);

public static class DocumentDrawingPlan
{
    public static IReadOnlyDictionary<string,IReadOnlyList<DocumentDrawing>> Build(
        IReadOnlyList<(string Line,IReadOnlyList<PlannedSheet> Sheets)> lines,int frontPages)
    {
        if(frontPages<1)throw new ArgumentOutOfRangeException(nameof(frontPages));
        var addresses=new Dictionary<string,(int Offset,IReadOnlyList<PlannedSheet> Sheets)>(StringComparer.OrdinalIgnoreCase);
        int offset=frontPages;
        foreach(var (line,sheets) in lines)
        {
            addresses.Add(line,(offset,sheets));
            offset+=sheets.Count;
        }

        var result=new Dictionary<string,IReadOnlyList<DocumentDrawing>>(StringComparer.OrdinalIgnoreCase);
        foreach(var (line,sheets) in lines)
        {
            int firstOffset=addresses[line].Offset;
            result.Add(line,sheets.Select(sheet=>
            {
                // Validate fitting the real reference text before launching any native renderers.
                try
                {
                    try{return Fit(wrapPageNumber:false);}
                    catch(SheetCapacityException)
                    {
                        try{return Fit(wrapPageNumber:true);}
                        catch(SheetCapacityException){return Fit(wrapPageNumber:true,fitToPaper:true);}
                    }
                }
                catch(SheetCapacityException ex)
                {
                    throw new SheetCapacityException($"Линия {line}, лист {firstOffset+sheet.Number}: {ex.Message}",
                        ex.SourceId,ex.ModelStart,ex.ModelEnd,ex.PaperWidthOverflow,ex.PaperHeightOverflow,ex.Conflict);
                }

                DocumentDrawing Fit(bool wrapPageNumber,bool fitToPaper=false)
                {
                    var scene=DocumentSheetReferences.Apply(sheet,line,firstOffset,addresses,wrapPageNumber,fitToPaper);
                    _ = SheetComposition.Center(scene);
                    return new(sheet,scene,firstOffset+sheet.Number);
                }
            }).ToArray());
        }
        return result;
    }
}

