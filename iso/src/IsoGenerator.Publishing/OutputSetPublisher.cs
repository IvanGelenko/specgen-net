namespace IsoGenerator.Publishing;
public static class OutputSetPublisher
{
    // Native rendering finishes in the job directory before any published file changes.
    public static void Publish(IReadOnlyList<string> staged,IReadOnlyList<string> outputs,bool overwrite,string backupDirectory,IReadOnlyList<string>? obsoleteOutputs=null)
    {
        if(staged.Count==0||staged.Count!=outputs.Count)throw new ArgumentException("Неверный состав выпуска.");
        var targets=outputs.Select(Path.GetFullPath).ToArray();
        var obsolete=(obsoleteOutputs??Array.Empty<string>()).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(obsolete.Length>0&&!overwrite)throw new IOException("Retiring old sheets requires approved replacement.");
        if(obsolete.Intersect(targets,StringComparer.OrdinalIgnoreCase).Any())throw new IOException("Current and retired outputs overlap.");
        using var publicationLocks=new PublicationLocks(targets.Concat(obsolete));
        if(targets.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=targets.Length)throw new IOException("Повторяющиеся имена выходных файлов.");
        for(int i=0;i<staged.Count;i++)
        {
            if(!File.Exists(staged[i]))throw new FileNotFoundException("Не подготовлен файл выпуска.",staged[i]);
            if(string.Equals(Path.GetFullPath(staged[i]),targets[i],StringComparison.OrdinalIgnoreCase))throw new IOException("Промежуточный и итоговый файл совпадают.");
            if(!overwrite&&File.Exists(targets[i]))throw new IOException("Файл уже существует: "+targets[i]);
        }
        Directory.CreateDirectory(backupDirectory);
        var retired=new List<string>();var retiredBackups=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var backups=new Dictionary<int,string>();var completed=new List<int>();var temporary=new List<string>();
        try
        {
            if(overwrite)for(int i=0;i<targets.Length;i++)if(File.Exists(targets[i]))
            {string backup=Path.Combine(backupDirectory,i.ToString("000")+"-"+Path.GetFileName(targets[i]));File.Copy(targets[i],backup,false);backups[i]=backup;}
            foreach(string old in obsolete.Where(File.Exists))
            {string backup=Path.Combine(backupDirectory,"retired-"+retiredBackups.Count.ToString("000")+"-"+Path.GetFileName(old));File.Copy(old,backup,false);retiredBackups[old]=backup;}
            for(int i=0;i<targets.Length;i++)
            {
                string pending=Path.Combine(Path.GetDirectoryName(targets[i])!,"."+Path.GetFileName(targets[i])+"."+Guid.NewGuid().ToString("N")+".tmp");
                temporary.Add(pending);File.Copy(staged[i],pending,false);File.Move(pending,targets[i],overwrite);completed.Add(i);
            }
            foreach(string old in retiredBackups.Keys){File.Delete(old);retired.Add(old);}
        }
        catch(Exception failure)
        {
            var errors=new List<Exception>{failure};
            foreach(string old in retired.AsEnumerable().Reverse())
            {try{File.Copy(retiredBackups[old],old,true);}catch(Exception restore){errors.Add(restore);}}
            foreach(int index in completed.AsEnumerable().Reverse())
            {
                try{if(backups.TryGetValue(index,out var backup))File.Copy(backup,targets[index],true);else File.Delete(targets[index]);}
                catch(Exception restore){errors.Add(restore);}
            }
            if(errors.Count>1)throw new AggregateException("Ошибка публикации. Резервные копии: "+backupDirectory,errors);
            throw;
        }
        finally
        {
            foreach(string file in temporary)try{if(File.Exists(file))File.Delete(file);}catch(IOException){}
        }
    }
    sealed class PublicationLocks : IDisposable
    {
        readonly List<FileStream> streams=new();
        public PublicationLocks(IEnumerable<string> targets)
        {
            try
            {
                foreach(string directory in targets.Select(p=>Path.GetDirectoryName(p)!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase))
                    streams.Add(new FileStream(Path.Combine(directory,".specgen-iso-publish.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose));
            }
            catch{Dispose();throw;}
        }
        public void Dispose(){foreach(var stream in streams.AsEnumerable().Reverse())stream.Dispose();}
    }

}
