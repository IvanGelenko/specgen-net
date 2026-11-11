namespace IsoGenerator.Core;
public static class DrawingFileNames
{
    public static string LineStem(string line)
    {
        // Windows file-name rules must be deterministic even in cross-platform core tests.
        const string forbidden="<>:\"/\\|?*";
        string stem=new(line.Select(c=>c<32||forbidden.Contains(c)?'_':c).ToArray());
        stem=stem.Trim().TrimEnd('.',' ');
        if(string.IsNullOrWhiteSpace(stem))throw new InvalidDataException("Не заполнен номер линии для имени файла.");
        string first=stem.Split('.')[0].ToUpperInvariant();
        if(first is "CON" or "PRN" or "AUX" or "NUL"||System.Text.RegularExpressions.Regex.IsMatch(first,"^(COM|LPT)[1-9]$"))stem="_"+stem;
        return stem;
    }
    public static string Dwg(string folder,string line,int number,int total)
    {
        if(number<1||total<1||number>total)throw new ArgumentOutOfRangeException(nameof(number));
        return Path.Combine(folder,LineStem(line)+(total>1?$"(L{number})":"")+".dwg");
    }
    public static (string[] Drawings,string Pdf) OutputSet(string folder,string line,int total,int index=0)
    {
        if(total<1||index<0)throw new ArgumentOutOfRangeException(nameof(total));
        string stem=LineStem(line)+(index>0?$" ({index})":"");
        return (Enumerable.Range(1,total).Select(number=>Dwg(folder,stem,number,total)).ToArray(),Path.Combine(folder,stem+".pdf"));
    }
    public static string[] ObsoleteDrawings(string pdf,IReadOnlyCollection<string> currentDrawings)
    {
        string folder=Path.GetDirectoryName(Path.GetFullPath(pdf))!,stem=Path.GetFileNameWithoutExtension(pdf);
        var current=currentDrawings.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string pattern="^"+System.Text.RegularExpressions.Regex.Escape(stem)+@"(?:\(L[1-9]\d*\))?\.dwg$";
        return Directory.EnumerateFiles(folder).Where(p=>!current.Contains(Path.GetFullPath(p))&&System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p),pattern,System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant)).ToArray();
    }
    public static (string[] Drawings,string Pdf) IndexedOutputSet(string folder,string line,int total,Func<string,bool>? exists=null)
    {
        exists??=File.Exists;
        for(int index=1;;index++)
        {
            var result=OutputSet(folder,line,total,index);
            if(!result.Drawings.Append(result.Pdf).Any(exists))return result;
        }
    }
}
