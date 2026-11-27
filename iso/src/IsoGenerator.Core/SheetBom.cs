using System.Text.RegularExpressions;

namespace IsoGenerator.Core;

internal static class SheetBom
{
    static string BomDescription(Part part)
    {
        var description=Get(part,"PartSizeLongDesc");
        return string.IsNullOrWhiteSpace(description)?Get(part,"PartFamilyLongDesc"):description;
    }
    const RegexOptions CatalogTextOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    const string BendNumber = @"\d+(?:[.,]\d+)?";
    static readonly Regex BendRadius = new(@"(?<![\p{L}\d])(?:[РR]|радиус(?:\s+(?:гиба|изгиба))?)\s*=\s*("+BendNumber+@")\b(?:\s*мм\b)?",CatalogTextOptions);
    static readonly Regex BendAngle = new(@"(?<![\p{L}\d])(?:У|угол(?:\s+(?:гиба|изгиба))?)\s*=\s*("+BendNumber+@")\b(?:\s*(?:°|град\b\.?))?",CatalogTextOptions);
    static readonly Regex BendLength = new(@"(?<![\p{L}\d])длина(?:\s+трубы)?\s*=\s*("+BendNumber+@")\b(?:\s*мм\b)?",CatalogTextOptions);
    static readonly Regex BendTails = new(@"[,;]\s*[ДD][12]\s*=\s*"+BendNumber+@"(?:\s*мм\b)?(?=\s*[,;]|\s*$)",CatalogTextOptions);
    static readonly Regex PipeName = new(@"(?<!\p{L})труба(?!\p{L})",CatalogTextOptions);
    static readonly Regex BomStandard = new(
        @"(?<![\p{L}\p{N}])(?:ГОСТ(?:\s*Р)?|ОСТ)\s*\d+(?:\s*[-‐‑–—.]\s*\d+)*(?![\p{L}\p{N}]|\s*[-‐‑–—.]\s*\d)",
        CatalogTextOptions);
    static string FormatBendBomDescription(string description)
    {
        description=BendRadius.Replace(description,"Р=$1");
        description=BendAngle.Replace(description,"α=$1°");
        description=BendLength.Replace(description,"L=$1");
        return BendTails.Replace(description,"");
    }
    static string ExtractPipeStandard(string description,out string pipeStandard)
    {
        // The slash separates the pipe standard from the material grade and its standard.
        pipeStandard="";
        var pipe=PipeName.Match(description);
        if(!pipe.Success)return description;
        var standard=BomStandard.Match(description,pipe.Index+pipe.Length);
        if(!standard.Success)return description;
        var afterStandard=description.AsSpan(standard.Index+standard.Length).TrimStart();
        if(afterStandard.IsEmpty||afterStandard[0]!='/')return description;
        pipeStandard=standard.Value;
        return description[..standard.Index]+afterStandard[1..].TrimStart().ToString();
    }
    internal static string RemoveRepeatedStandards(string description,string designation)
    {
        static string Key(string value)=>string.Concat(value.Where(c=>!char.IsWhiteSpace(c)))
            .Replace('‐','-').Replace('‑','-').Replace('–','-').Replace('—','-').ToUpperInvariant();
        var standards=BomStandard.Matches(designation).Select(match=>Key(match.Value)).ToHashSet();
        if(standards.Count==0)return description;
        var duplicates=BomStandard.Matches(description).Where(match=>standards.Contains(Key(match.Value))).ToArray();
        foreach(var match in duplicates.Reverse())
        {
            int start=match.Index,end=match.Index+match.Length;
            if(start>0&&"-‐‑–—".Contains(description[start-1]))start--;
            if(end<description.Length&&description[end]=='/')end++;
            description=description[..start]+description[end..];
        }
        return duplicates.Length==0?description:System.Text.RegularExpressions.Regex.Replace(description,@"[ 	]{2,}"," ").Trim();
    }
    static string Get(Part part,string name)=>part.Properties.GetValueOrDefault(name,"");

    internal static (IReadOnlyList<BomRow> Rows,IReadOnlyDictionary<string,int> Positions) Build(
        IEnumerable<Part> parts,IReadOnlySet<string>? excluded,bool excludeFlangeHardware,bool instrumentCalloutsOnly)
    {
        var rows=new List<BomRow>();var positions=new Dictionary<string,int>();
        foreach(var group in parts
            .Where(p=>BomQuantities.Includes(p,excluded,instrumentCalloutsOnly))
            .GroupBy(p=>string.Join("\u001f",p.CatalogClass,BomDescription(p),Get(p,"DesignStd"),Get(p,"Size"),Get(p,"WallThickness"),Get(p,"Spec"),Get(p,"Material"),BomCategories.Classify(p)==BomCategory.Fasteners?Get(p,"Length"):"",Get(p,"BoltSize"),BomQuantities.Unit(p,excludeFlangeHardware),PartIdentifier.Read(p))).OrderBy(g=>BomCategories.Classify(g.First())).ThenBy(g=>PartDiameter.Read(g.First())??double.MaxValue).ThenBy(g=>BomCategories.FittingOrder(g.First())).ThenBy(g=>g.Key,StringComparer.Ordinal))
        {
            var first=group.First();
            double qty=group.Sum(BomQuantities.Amount);
            int position=rows.Count+1;
            string standard=Get(first,"DesignStd"),identifier=PartIdentifier.Read(first);
            var description=BomDescription(first);
            string pipeStandard="";
            if(first.CatalogClass=="PipeBend")
                description=FormatBendBomDescription(description);
            if(first.Component==ComponentKind.Pipe||first.CatalogClass=="PipeBend")
                description=ExtractPipeStandard(description,out pipeStandard);
            if(pipeStandard.Length>0)
            {
                standard=pipeStandard;
                if(identifier.Length>0&&!BomStandard.Matches(identifier).Any(match=>match.Value.Equals(pipeStandard,StringComparison.OrdinalIgnoreCase)))
                    identifier+=" / "+pipeStandard;
            }
            else description=RemoveRepeatedStandards(description,string.IsNullOrWhiteSpace(identifier)?standard:identifier);
            if(BomCategories.Classify(first)==BomCategory.Fasteners&&Get(first,"Length").Length>0)description+=" (L="+Get(first,"Length")+" мм)";
            var category=BomCategories.Classify(first);
            string unit=BomQuantities.Unit(first,excludeFlangeHardware);
            rows.Add(new(position,first.CatalogClass,description,standard,qty,unit,group.Select(p=>p.Handle).ToArray(),identifier,category));
            foreach(var p in group)positions[p.Handle]=position;
        }
        return (rows,positions);
    }
}
