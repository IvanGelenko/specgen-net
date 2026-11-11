using System.Xml.Linq;
using System.Text.RegularExpressions;
namespace IsoGenerator.Core;
public sealed record IsoDefinition(string Type,string Skey)
{
    public static IsoDefinition Parse(string text)
    {
        var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var field in text.Split(','))
        {
            var pair=field.Split('=',2);if(pair.Length!=2)continue;
            string key=pair[0].Trim(),value=pair[1].Trim();
            if(values.TryGetValue(key,out var old)&&old!=value)throw new InvalidDataException($"Conflicting ISO definition {key}");
            values[key]=value;
        }
        return new(values.GetValueOrDefault("TYPE","").ToUpperInvariant(),values.GetValueOrDefault("SKEY","").ToUpperInvariant());
    }
}
public sealed class IsoSymbolMap
{
    readonly (string Pattern,string Block)[] rules;
    readonly XElement root;
    public IsoBlockCatalog? Catalog {get;}
    public IsoSymbolMap(string xml,IsoBlockCatalog? catalog=null)
    {
        Catalog=catalog;
        // Components only. EndTypeMap and spindle maps are separate semantic categories.
        root=XDocument.Parse(xml).Root!;
        rules=root.Elements("SkeyMap")
            .Select(e=>((string?)e.Attribute("SKEY")??"",(string?)e.Attribute("AcadBlock")??""))
            .Where(e=>e.Item1.Length>0&&e.Item2.Length>0).ToArray();
    }
    static bool Matches(string value,string pattern)=>Regex.IsMatch(value,"^"+Regex.Escape(pattern).Replace("\\?",".").Replace("\\*",".*")+"$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    public string? ResolveEnd(string end)=>root.Elements("EndTypeMap").Where(e=>(string?)e.Attribute("OnFitting")=="true"&&Matches(end,(string?)e.Attribute("SKEY")??""))
        .Select(e=>(string?)e.Attribute("AcadBlock")).FirstOrDefault();
    public string? ResolveSpindle(string skey)
    {
        var key=root.Elements("ValveToSpindleSkeyMap").Where(e=>Matches(skey,(string?)e.Attribute("ValveSKEY")??""))
            .Select(e=>(string?)e.Attribute("SpindleSKEY")).FirstOrDefault();
        return key is null?null:Resolve(key);
    }
    public string? Resolve(string skey)
    {
        if(string.IsNullOrWhiteSpace(skey))return null;
        var hits=rules.Where(r=>Regex.IsMatch(skey,"^"+Regex.Escape(r.Pattern).Replace("\\?",".").Replace("\\*",".*")+"$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))
            .OrderBy(r=>r.Pattern.Count(c=>c is '?' or '*')).ToArray();
        if(hits.Length==0)return Catalog?.Find(skey)?.Name;
        int specificity=hits[0].Pattern.Count(c=>c is '?' or '*');
        if(hits.Where(r=>r.Pattern.Count(c=>c is '?' or '*')==specificity).Select(r=>r.Block).Distinct().Count()>1)
            throw new InvalidDataException($"Ambiguous ISO symbol mapping for {skey}");
        return hits[0].Block;
    }
}
public sealed record SymbolGlyph(string SourceId,string SourceGuid,string Definition,string Block,
    SheetPoint Origin,SheetPoint XAxis,SheetPoint YAxis,string Variant = "",bool Context = false,IReadOnlyList<SheetPoint>? LocalBounds=null,IReadOnlyList<IReadOnlyList<SheetPoint>>? LocalContours=null);
