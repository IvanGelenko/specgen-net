using System.Globalization;
using System.Xml.Linq;
namespace IsoGenerator.Core;
public sealed record IsoStyleSettings(double SymbolScale,double? SmallBoreDiameter=null,int? VentDrainMaxComponents=null,int? VentDrainMaxPipes=null)
{
    // One project-library scale for every component, regardless of diameter or theme.
    public double ScaleFor(Part part)=>SymbolScale;
    public static IsoStyleSettings Parse(string xml)
    {
        var root=XDocument.Parse(xml).Root??throw new InvalidDataException("Пустой IsoConfig.xml.");
        var theme=root.Element("Themes")?.Elements("Theme").FirstOrDefault(e=>(string?)e.Attribute("Name")=="Default");
        string? raw=(string?)theme?.Element("Symbols")?.Attribute("Scale");
        if(!double.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out double scale)||!double.IsFinite(scale)||scale<=0)throw new InvalidDataException("В основном стиле IsoConfig.xml не указан допустимый масштаб Symbols.Scale.");
        var small=root.Descendants("FieldFilter").FirstOrDefault(e=>(string?)e.Attribute("Name")=="SmallBorePiping");
        double? diameter=double.TryParse((string?)small?.Attribute("Fields"),NumberStyles.Float,CultureInfo.InvariantCulture,out double size)?size:null;
        var branch=root.Descendants("BranchPatternTheme").FirstOrDefault(e=>(string?)e.Attribute("Name")=="Vent/Drain Piping");
        return new(scale,diameter,(int?)branch?.Attribute("MaxComponentCount"),(int?)branch?.Attribute("MaxPipeCount"));
    }
}
