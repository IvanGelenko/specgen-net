using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace PipingSpecPlugin.UI;

internal static class IsoDocumentProfileFile
{
    sealed record Profile(int Version,string Valves,string Supports,byte[] Title,byte[] Contents);

    public static void Save(string path,IsoDocumentDetails documents)
    {
        var profile=new Profile(1,documents.Valves,documents.Supports,File.ReadAllBytes(documents.TitlePath),
            string.IsNullOrWhiteSpace(documents.ContentsPath)?null:File.ReadAllBytes(documents.ContentsPath));
        string pending=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllBytes(pending,JsonSerializer.SerializeToUtf8Bytes(profile,new JsonSerializerOptions{WriteIndented=true}));
            File.Move(pending,path,overwrite:true);
        }
        finally{File.Delete(pending);}
    }

    public static IsoDocumentDetails Load(string path)
    {
        using var file=File.OpenRead(path);
        var profile=JsonSerializer.Deserialize<Profile>(file);
        if(profile is not {Version:1,Title.Length:>0})throw new InvalidDataException("Неподдерживаемый файл реквизитов изометрий.");
        string title=StoreAttachment(profile.Title,"Templates",".docx"),contents=profile.Contents is {Length:>0}?StoreAttachment(profile.Contents,"Templates",".docx"):"";
        return new(profile.Valves??"",profile.Supports??"",title,contents);
    }

    static string StoreAttachment(byte[] content,string group,string extension)
    {
        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IsoGenerator",group);
        Directory.CreateDirectory(folder);
        var hash=SHA256.HashData(content);
        string path=Path.Combine(folder,Convert.ToHexString(hash)+extension);
        if(File.Exists(path)&&SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(hash))return path;
        string pending=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllBytes(pending,content);
            File.Move(pending,path,overwrite:true);
        }
        finally{File.Delete(pending);}
        return path;
    }
}
