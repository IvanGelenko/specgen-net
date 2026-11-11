namespace IsoGenerator.Core;

// Reject a corrupt sheet set before any native drawing or published file is changed.
internal static class SheetSetIntegrity
{
    public static void Validate(PipelineSnapshot source,IReadOnlyList<PlannedSheet> sheets,bool excludeFlangeHardware)
    {
        var originals=source.Parts.Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assigned=sheets.SelectMany(s=>s.Snapshot.Parts).Select(p=>p.Handle).ToArray();
        if(assigned.Length!=originals.Count||assigned.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=assigned.Length||!originals.SetEquals(assigned))
            throw new InvalidDataException("Sheet partition lost or duplicated source objects.");
        var owner=sheets.SelectMany((s,i)=>s.Snapshot.Parts.Select(p=>(p.Handle,Sheet:i))).ToDictionary(x=>x.Handle,x=>x.Sheet,StringComparer.OrdinalIgnoreCase);
        var supports=source.Parts.Where(p=>BomCategories.Classify(p)==BomCategory.Supports).Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var c in source.Connections)
            if((supports.Contains(c.FirstHandle)||supports.Contains(c.SecondHandle))&&owner.TryGetValue(c.FirstHandle,out int a)&&owner.TryGetValue(c.SecondHandle,out int b)&&a!=b)
                throw new InvalidDataException("A support was separated from its connected pipe.");
        if(!excludeFlangeHardware)
        {
            var expected=(source.Accessories??Array.Empty<Part>()).Select(p=>p.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actual=sheets.SelectMany(s=>s.Scene.BillOfMaterials).SelectMany(r=>r.Handles).Where(expected.Contains).ToArray();
            if(actual.Length!=expected.Count||actual.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=actual.Length||!expected.SetEquals(actual))
                throw new InvalidDataException("Sheet partition lost or duplicated connector accessories.");
        }
        var welds=sheets.SelectMany(s=>s.Scene.Welds??Array.Empty<WeldMarker>()).Select(w=>string.Join("|",new[]{w.FirstHandle+":"+w.FirstPort,w.SecondHandle+":"+w.SecondPort}.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))).ToArray();
        if(welds.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=welds.Length)
            throw new InvalidDataException("A boundary weld was assigned to more than one sheet.");
        foreach(var sheet in sheets)
        {
            if(!sheet.Scene.BomOnly&&SheetConnectivity.Components(sheet.Snapshot).Count!=1)throw new InvalidDataException("На листе "+sheet.Number+" обнаружены несвязанные фрагменты трубопровода.");
            var points=sheet.Scene.Strokes.SelectMany(s=>s.Points).Concat(sheet.Scene.Annotations.Select(a=>a.Position)).Concat(sheet.Scene.Dimensions.SelectMany(d=>new[]{d.Start,d.End,d.DimensionLinePoint??default,d.TextPosition??default}));
            if(points.Any(p=>!double.IsFinite(p.X)||!double.IsFinite(p.Y)))throw new InvalidDataException("Non-finite drawing coordinates in sheet "+sheet.Number);
        }
    }
}
