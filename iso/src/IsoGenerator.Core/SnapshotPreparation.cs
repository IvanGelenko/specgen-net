namespace IsoGenerator.Core;

internal static class SnapshotPreparation
{
    public static PipelineSnapshot ForSheetPlanning(PipelineSnapshot source,bool ignoreBreakPoints,bool instrumentCalloutsOnly,PlanningExecution execution)
    {
        var supportWarnings=new List<string>();
        var parts=source.Parts.Select(part=>
        {
            if(part.Kind!="AcPpDb3dSupport"||!string.IsNullOrWhiteSpace(part.Properties.GetValueOrDefault("PnPClassName")))return part;
            var properties=new Dictionary<string,string>(part.Properties,StringComparer.OrdinalIgnoreCase)
            {
                ["PnPClassName"]="Support"
            };
            if(string.IsNullOrWhiteSpace(properties.GetValueOrDefault("PartSizeLongDesc"))&&
               string.IsNullOrWhiteSpace(properties.GetValueOrDefault("PartFamilyLongDesc")))
                properties["PartSizeLongDesc"]="Опора (каталожные данные отсутствуют)";
            supportWarnings.Add($"{part.Handle}: у опоры отсутствует класс из базы Plant. Использованы тип объекта и доступные свойства.");
            return part with{Properties=properties};
        }).ToArray();
        if(supportWarnings.Count>0)source=source with{Parts=parts,Warnings=source.Warnings.Concat(supportWarnings).ToArray()};
        var missingProperties=source.Parts.Where(p=>p.Kind.StartsWith("AcPpDb3d",StringComparison.Ordinal)&&!p.IsConnector&&string.IsNullOrWhiteSpace(p.Properties.GetValueOrDefault("PnPClassName"))).Select(p=>p.Handle).ToArray();
        if(missingProperties.Length>0)throw new InvalidDataException($"Не прочитаны свойства Plant 3D у {missingProperties.Length} компонентов (например, {string.Join(", ",missingProperties.Take(5))}). Откройте DWG из соответствующего проекта в Диспетчере проектов Plant 3D и повторите захват. Разделение на листы по неполным данным невозможно.");
        source=SheetConnectivity.AttachSupports(BreakPointPreparation.Apply(source,ignoreBreakPoints));
        if(instrumentCalloutsOnly)source=SheetConnectivity.AttachInstrumentCallouts(source,execution);
        var requiredConnectionKeys=source.Connections.Select(CutPreferences.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if((source.RequiredCuts??Array.Empty<Connection>()).Any(c=>!requiredConnectionKeys.Contains(CutPreferences.Key(c))))throw new InvalidDataException("Обязательная точка разрыва не соответствует соединению линии.");
        return source;
    }
}
