using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IsoGenerator.Core;

namespace IsoGenerator.AutoCAD;

public static class RememberedIsoOutput
{
    internal sealed record Page(string Drawing,string Pdf,string DrawingHash,string PdfHash,string? NumberingKey=null);
    internal sealed record Entry(string LayoutKey,string RenderKey,int QuarterTurns,RememberedLayout Layout,
        Page[] Pages,string PublishedPdf);

    static string CacheFile(string drawing,string line)
    {
        string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.GetFullPath(drawing).ToUpperInvariant()+"\n"+line.ToUpperInvariant())));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IsoGenerator","Remembered",key+".json");
    }

    public static bool Exists(string drawing,string line)=>File.Exists(CacheFile(drawing,line));
    static string PublishedMarker(string drawing,string line)=>Path.ChangeExtension(CacheFile(drawing,line),".published");
    public static bool WasPublished(string drawing,string line)=>File.Exists(PublishedMarker(drawing,line))||Exists(drawing,line);

    public static bool PreferRememberLayout(string drawing,string line)
    {
        try
        {
            string marker=PublishedMarker(drawing,line);
            if(File.Exists(marker)&&bool.TryParse(File.ReadAllText(marker),out bool remember))return remember;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
        return Exists(drawing,line);
    }

    internal static void MarkPublished(string drawing,string line,bool remember)
    {
        string path=PublishedMarker(drawing,line);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,remember.ToString());
    }

    public sealed record Preferences(string Valves,string Supports,string Stamp,bool AutomaticView,int QuarterTurns,
        bool ExcludeHardware,bool IgnoreBreaks,bool InstrumentsOnly,double SymbolScale,double MinimumPipeLength);

    public static Preferences? Defaults(string drawing,string line)
    {
        if(Read(drawing,line) is not {} entry)return null;
        try
        {
            using var render=JsonDocument.Parse(entry.RenderKey);
            using var layout=JsonDocument.Parse(entry.LayoutKey);
            string ReadValue(string key)=>render.RootElement.TryGetProperty(key,out var value)?value.GetString()??"":"";
            var settings=layout.RootElement;
            return new(ReadValue("valvesDocument"),ReadValue("supportsDocument"),ReadValue("stampDocument"),
                settings.GetProperty("automaticView").GetBoolean(),settings.GetProperty("quarterTurns").GetInt32(),
                settings.GetProperty("excludeFlangeHardware").GetBoolean(),settings.GetProperty("ignoreBreakPoints").GetBoolean(),
                settings.GetProperty("instrumentCalloutsOnly").GetBoolean(),settings.GetProperty("symbolScale").GetDouble(),
                settings.TryGetProperty("minimumPipeLength",out var length)?length.GetDouble():10);
        }
        catch(Exception ex) when(ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException){return null;}
    }

    internal static Entry? Read(string drawing,string line)
    {
        try
        {
            string path=CacheFile(drawing,line);
            if(!File.Exists(path))return null;
            var entry=JsonSerializer.Deserialize<Entry>(File.ReadAllText(path));
            if(entry?.Layout?.Source?.Parts is null||entry.Layout.Source.Connections is null||
               entry.Layout.Sheets is not {Count:>0}||entry.Pages?.Length!=entry.Layout.Sheets.Count||
               string.IsNullOrWhiteSpace(entry.PublishedPdf)||entry.QuarterTurns is <0 or >3)return null;
            for(int i=0;i<entry.Pages.Length;i++)
            {
                var sheet=entry.Layout.Sheets[i];
                var page=entry.Pages[i];
                if(sheet?.Scene is null||sheet.Snapshot?.Parts is null||sheet.Number!=i+1||
                   sheet.Total!=entry.Pages.Length||page is null||string.IsNullOrWhiteSpace(page.Drawing)||
                   string.IsNullOrWhiteSpace(page.Pdf)||string.IsNullOrWhiteSpace(page.DrawingHash)||
                   string.IsNullOrWhiteSpace(page.PdfHash))return null;
            }
            return entry;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){return null;}
    }

    internal static void Save(string drawing,string line,Entry entry)
    {
        string path=CacheFile(drawing,line);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllText(temporary,JsonSerializer.Serialize(entry));
            File.Move(temporary,path,true);
        }
        finally
        {
            try{File.Delete(temporary);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
        }
    }

    internal static string HashFile(string path)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static bool Matches(string path,string hash)
    {
        try{return File.Exists(path)&&StringComparer.Ordinal.Equals(HashFile(path),hash);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){return false;}
    }
}
