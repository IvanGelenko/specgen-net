using System.Security.Cryptography;
using System.Text.Json;

namespace IsoGenerator.Publishing;

public sealed class OutputOwnershipConflictException(string file)
    :IOException($"Файл «{Path.GetFileName(file)}» принадлежит другому комплекту изометрий.");

public static partial class OutputSetPublisher
{
    const string ManifestSuffix=".specgen-iso.json";
    static readonly StringComparer FileNames=StringComparer.OrdinalIgnoreCase;
    sealed record Manifest(int Version,string Pdf,string? ReleaseKey,IReadOnlyDictionary<string,string> Files);

    public static string ManifestPath(string pdf)=>pdf+ManifestSuffix;

    public static bool IsCurrent(string pdf,string releaseKey,IReadOnlyCollection<string> outputs)
    {
        string? directory=Path.GetDirectoryName(Path.GetFullPath(pdf));
        return directory is not null&&ReadManifest(ManifestPath(pdf)) is {} manifest&&
            manifest.ReleaseKey==releaseKey&&FileNames.Equals(manifest.Pdf,Path.GetFileName(pdf))&&
            manifest.Files.Keys.ToHashSet(FileNames).SetEquals(outputs.Select(path=>Path.GetFileName(path)!))&&
            manifest.Files.All(file=>HasHash(Path.Combine(directory,file.Key),file.Value));
    }

    public static bool IsOwnedOutput(string pdf,string output)
    {
        string folder=Path.GetDirectoryName(Path.GetFullPath(pdf))!;
        return FileNames.Equals(folder,Path.GetDirectoryName(Path.GetFullPath(output)))&&
            ReadManifest(ManifestPath(pdf)) is {} manifest&&FileNames.Equals(manifest.Pdf,Path.GetFileName(pdf))&&
            manifest.Files.Any(file=>FileNames.Equals(file.Key,Path.GetFileName(output))&&HasHash(output,file.Value));
    }

    public static void PublishDocument(string pdf,IReadOnlyList<string> staged,IReadOnlyList<string> outputs,
        bool overwrite,string backupDirectory,string? releaseKey=null,IReadOnlyDictionary<string,string>? retainedOutputs=null,string? expectedPreviousPdfHash=null)
    {
        string folder=Path.GetDirectoryName(Path.GetFullPath(pdf))!;
        string pdfName=Path.GetFileName(pdf);
        string manifestPath=ManifestPath(Path.GetFullPath(pdf));
        var targets=outputs.Concat(retainedOutputs?.Keys??Array.Empty<string>()).Select(Path.GetFullPath).ToArray();
        if(staged.Count!=outputs.Count||!targets.Any(path=>FileNames.Equals(path,Path.GetFullPath(pdf)))||
           targets.Any(path=>!FileNames.Equals(Path.GetDirectoryName(path),folder)||FileNames.Equals(path,manifestPath)))
            throw new ArgumentException("Файлы документа должны находиться в одной выходной папке.");

        using var locks=new PublicationLocks(new[]{manifestPath});
        if(expectedPreviousPdfHash is not null&&!HasHash(pdf,expectedPreviousPdfHash))
            throw new InvalidDataException("Существующий комплект изменился во время добавления линии. Откройте генератор повторно.");
        var expected=targets.Select(Path.GetFileName).ToHashSet(FileNames);
        var ownedByOthers=new HashSet<string>(FileNames);
        Manifest? previous=null;
        foreach(string path in Directory.EnumerateFiles(folder,"*"+ManifestSuffix))
        {
            var other=ReadManifest(path);
            if(other is null)continue;
            if(FileNames.Equals(path,manifestPath))
            {
                if(FileNames.Equals(other.Pdf,pdfName))previous=other;
                continue;
            }
            if(FileNames.Equals(path,ManifestPath(Path.Combine(folder,other.Pdf))))
                ownedByOthers.UnionWith(other.Files.Keys);
        }
        if(expected.FirstOrDefault(name=>name is not null&&ownedByOthers.Contains(name)&&File.Exists(Path.Combine(folder,name))) is {} collision)
            throw new OutputOwnershipConflictException(collision);

        var obsolete=overwrite&&previous is not null?previous.Files
            .Where(file=>!expected.Contains(file.Key)&&!ownedByOthers.Contains(file.Key)&&
                HasHash(Path.Combine(folder,file.Key),file.Value))
            .Select(file=>Path.Combine(folder,file.Key)).ToArray():Array.Empty<string>();
        var files=new Dictionary<string,string>(FileNames);
        for(int i=0;i<outputs.Count;i++)files.Add(Path.GetFileName(outputs[i]),HashFile(staged[i]));
        if(retainedOutputs is not null)foreach(var (path,hash) in retainedOutputs)files.Add(Path.GetFileName(path),hash);

        string manifestStage=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(staged[0]))!,Guid.NewGuid().ToString("N")+".manifest");
        try
        {
            File.WriteAllText(manifestStage,JsonSerializer.Serialize(new Manifest(1,pdfName,releaseKey,files)));
            PublishCore(staged.Append(manifestStage).ToArray(),outputs.Append(manifestPath).ToArray(),
                overwrite,backupDirectory,obsolete,retainedOutputs,alreadyLocked:true);
        }
        finally{try{File.Delete(manifestStage);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }

    static Manifest? ReadManifest(string path)
    {
        try
        {
            if(!File.Exists(path))return null;
            var manifest=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path));
            if(manifest is not {Version:1,Files.Count:>0}||!IsFileName(manifest.Pdf)||
               !manifest.Files.Keys.Any(name=>FileNames.Equals(name,manifest.Pdf))||
               manifest.Files.Any(file=>!IsFileName(file.Key)||file.Value is not {Length:64}))
                return null;
            return manifest;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){return null;}
    }

    static bool IsFileName(string name)=>!string.IsNullOrWhiteSpace(name)&&name is not ("." or "..")&&
        name.IndexOfAny(new[]{'/','\\',':'})<0&&Path.GetFileName(name)==name;

    static bool HasHash(string path,string hash)
    {
        try{return File.Exists(path)&&StringComparer.Ordinal.Equals(HashFile(path),hash);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){return false;}
    }

    static string HashFile(string path)
    {
        using var source=File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(source));
    }
}

