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
        var connectionKeys=source.Connections.Select(CutPreferences.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var cut in source.RequiredCuts??Array.Empty<Connection>())
        {
            if(!connectionKeys.Contains(CutPreferences.Key(cut))||!owner.TryGetValue(cut.FirstHandle,out int first)||!owner.TryGetValue(cut.SecondHandle,out int second))
                throw new InvalidDataException("Не найдено соединение для обязательной точки разрыва: "+CutPreferences.Key(cut));
            if(first==second)throw new InvalidDataException("Не соблюдена обязательная точка разрыва: "+CutPreferences.Key(cut));
        }
        var transitions=new Dictionary<string,List<(int Sheet,int Target)>>(StringComparer.Ordinal);
        var diagramNumbers=sheets.Where(sheet=>!sheet.Scene.BomOnly).Select(sheet=>sheet.Number).ToHashSet();
        foreach(var sheet in sheets)
        foreach(var annotation in sheet.Scene.Annotations.Where(annotation=>annotation.Role=="continuation"))
        {
            if(DocumentSheetReferences.ReadLocal(annotation) is not {} link||
               link.Target==sheet.Number||!diagramNumbers.Contains(link.Target))
                throw new InvalidDataException($"Лист {sheet.Number}: неверная ссылка перехода «{annotation.Text}».");
            string mark=link.Mark;
            if(!transitions.TryGetValue(mark,out var endpoints))transitions[mark]=endpoints=new();
            endpoints.Add((sheet.Number,link.Target));
        }
        foreach(var (mark,endpoints) in transitions)
            if(endpoints.Count!=2||endpoints[0].Sheet!=endpoints[1].Target||endpoints[1].Sheet!=endpoints[0].Target)
                throw new InvalidDataException($"Переход {mark}: отсутствует взаимная ссылка между листами.");
        int expectedTransitions=source.Connections
            .Where(c=>owner.TryGetValue(c.FirstHandle,out int first)&&owner.TryGetValue(c.SecondHandle,out int second)&&first!=second)
            .Select(CutPreferences.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if(transitions.Count!=expectedTransitions)
            throw new InvalidDataException($"Неверное число переходов между листами: {transitions.Count} вместо {expectedTransitions}.");
        foreach(var instrument in source.Parts.Where(p=>p.Properties.ContainsKey("IsoCalloutHost")))
            if(!owner.TryGetValue(instrument.Properties["IsoCalloutHost"],out int hostSheet)||owner[instrument.Handle]!=hostSheet)throw new InvalidDataException("Выноска КИП отделена от своего узла: "+instrument.Handle);
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
            points=points.Concat((sheet.Scene.Symbols??Array.Empty<SymbolGlyph>()).SelectMany(SymbolFootprint.Corners))
                .Concat((sheet.Scene.Welds??Array.Empty<WeldMarker>()).Select(weld=>weld.Position))
                .Concat(sheet.Scene.Annotations.SelectMany(label=>new[]{label.Anchor??default,label.LeaderKnee??default}));
            if(points.Any(p=>!double.IsFinite(p.X)||!double.IsFinite(p.Y)))throw new InvalidDataException("Некорректные координаты на листе "+sheet.Number);
            if(sheet.Scene.BomOnly)continue;
            if(sheet.Scene.Dimensions.Any(size=>size.TextPosition is null||size.DimensionLinePoint is null||
                size.ExtensionDirection is null||size.Start==size.End))
                throw new InvalidDataException($"Лист {sheet.Number}: размерная линия содержит неполные координаты.");
            var paper=SheetComposition.Paper;
            if(SheetComposition.Pieces(sheet.Scene).Any(bounds=>bounds.Left<paper.Left-.01||bounds.Right>paper.Right+.01||
                bounds.Bottom<paper.Bottom-.01||bounds.Top>paper.Top+.01))
                throw new SheetCapacityException($"Лист {sheet.Number}: элементы вышли за рабочее поле.");
            var blocked=DimensionTextPlacement.FinalCollisions(sheet.Scene.Dimensions,
                DimensionRouting.TextObstacles(sheet.Scene.Strokes,sheet.Scene.Symbols??Array.Empty<SymbolGlyph>()));
            if(blocked.Length>0)
            {
                var sizes=blocked.Select(index=>sheet.Scene.Dimensions[index]).ToArray();
                throw new SheetCapacityException($"Лист {sheet.Number}: перекрыты размерные числа "+
                    string.Join(", ",sizes.Select(size=>$"{size.Label} ({size.SourceId})"))+".",sizes[0].SourceId);
            }
        }
    }
}
