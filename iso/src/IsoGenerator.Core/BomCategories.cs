namespace IsoGenerator.Core;
public enum BomCategory { Pipes, Fittings, Supports, Valves, Instruments, Flanges, Gaskets, Fasteners, Miscellaneous }
public static class BomCategories
{
    public const double ItemHeight=14, HeadingHeight=7, TextHeight=3;
    public static string Title(BomCategory category)=>category switch
    {
        BomCategory.Pipes=>"Трубы", BomCategory.Fittings=>"Фитинги", BomCategory.Supports=>"Опоры трубопровода",
        BomCategory.Valves=>"Трубопроводная арматура", BomCategory.Instruments=>"КИП", BomCategory.Flanges=>"Фланцы, заглушки",
        BomCategory.Gaskets=>"Прокладки", BomCategory.Fasteners=>"Крепёж", _=>"Разное"
    };
    public static BomCategory Classify(Part part)
    {
        string kind=part.Properties.GetValueOrDefault("PnPClassName","").ToUpperInvariant();
        string type=IsoDefinition.Parse(part.Properties.GetValueOrDefault("ContentIsoSymbolDefinition","")).Type;
        string identity=kind+" "+type;
        bool Has(params string[] values)=>values.Any(identity.Contains);
        if(kind=="PIPE"||type=="PIPE"||kind is "PIPEBEND" or "PIPE-BEND" or "BEND"||type=="PIPE-BEND")return BomCategory.Pipes;
        if(Has("ELBOW","TEE","REDUCER","CROSS","COUPLING","OLET","FITTING","BEND"))return BomCategory.Fittings;
        if(Has("SUPPORT","HANGER"))return BomCategory.Supports;
        bool genericInstrument=Has("INSTRUMENT")&&!new[]{"SENSOR","GAUGE","THERMOMETER"}.Any(kind.Contains);
        string deviceDescription=string.Join(" ",new[]{"PartFamilyLongDesc","PartSizeLongDesc","ShortDescription"}.Select(k=>part.Properties.GetValueOrDefault(k,"").ToUpperInvariant()));
        if(genericInstrument&&new[]{"FLOWMETER","FLOW-METER","FLOW METER","РАСХОДОМЕР","КЛАПАН"}.Any(deviceDescription.Contains))return BomCategory.Valves;
        if(Has("VALVE","STRAINER","STEAMTRAP","STEAM-TRAP","FLOWMETER","FLOW-METER","FLOW METER","РАСХОДОМЕР","КЛАПАН"))return BomCategory.Valves;
        if(Has("INSTRUMENT","GAUGE","SENSOR","THERMOMETER","PRESSURETRANSMITTER","TEMPERATURE","МАНОМЕТР","ДАТЧИК","ТЕРМОМЕТР"))return BomCategory.Instruments;
        if(Has("FLANGE","CAP","PLUG","BLIND"))return BomCategory.Flanges;
        if(Has("GASKET","SEAL"))return BomCategory.Gaskets;
        if(Has("BOLT","STUD","NUT","WASHER","FASTENER"))return BomCategory.Fasteners;
        return BomCategory.Miscellaneous;
    }
    public static int FittingOrder(Part part)
    {
        string identity=part.Properties.GetValueOrDefault("PnPClassName","").ToUpperInvariant()+" "+IsoDefinition.Parse(part.Properties.GetValueOrDefault("ContentIsoSymbolDefinition","")).Type;
        return identity.Contains("ELBOW")?0:identity.Contains("TEE")?1:identity.Contains("REDUCER")?2:3;
    }
    public static double BodyHeight(IReadOnlyList<BomRow> rows)=>rows.Count*ItemHeight+rows.Select(r=>r.Category).Distinct().Count()*HeadingHeight;
    public static bool Fits(IReadOnlyList<BomRow> rows)=>BodyHeight(rows)<=330;
}
