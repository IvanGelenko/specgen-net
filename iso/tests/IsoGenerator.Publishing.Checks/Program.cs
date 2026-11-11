using IsoGenerator.Publishing;
using PdfSharp.Pdf.IO;
var pages=System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(args[0]))!;
try
{
    PdfSetPublisher.Merge(pages,args[1],"SpecGen merge check");
    using(var merged=PdfReader.Open(args[1],PdfDocumentOpenMode.Import))if(merged.PageCount!=pages.Length)throw new Exception("Incorrect PDF page count");
    bool prevented=false;try{PdfSetPublisher.Merge(pages,args[1],"Do not overwrite");}catch(IOException){prevented=true;}
    if(!prevented)throw new Exception("Existing PDF was overwritten");
    string publication=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!,"publication-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(publication);
    string stagedOne=Path.Combine(publication,"staged-one"),stagedTwo=Path.Combine(publication,"staged-two"),targetOne=Path.Combine(publication,"one.dwg"),targetTwo=Path.Combine(publication,"two.pdf");
    File.WriteAllText(stagedOne,"new-one");File.WriteAllText(stagedTwo,"new-two");File.WriteAllText(targetOne,"old-one");File.WriteAllText(targetTwo,"old-two");
    bool noOverwrite=false;try{OutputSetPublisher.Publish(new[]{stagedOne,stagedTwo},new[]{targetOne,targetTwo},false,Path.Combine(publication,"no-overwrite"));}catch(IOException){noOverwrite=true;}
    if(!noOverwrite||File.ReadAllText(targetOne)!="old-one")throw new Exception("Unapproved replacement was allowed");
    bool rolledBack=false;
    using(var locked=new FileStream(stagedTwo,FileMode.Open,FileAccess.Read,FileShare.None))
    {
        try{OutputSetPublisher.Publish(new[]{stagedOne,stagedTwo},new[]{targetOne,targetTwo},true,Path.Combine(publication,"rollback"));}catch(IOException){rolledBack=true;}
    }
    if(!rolledBack||File.ReadAllText(targetOne)!="old-one"||File.ReadAllText(targetTwo)!="old-two")throw new Exception("Failed publication did not restore the old set");
    OutputSetPublisher.Publish(new[]{stagedOne,stagedTwo},new[]{targetOne,targetTwo},true,Path.Combine(publication,"replacement"));
    if(File.ReadAllText(targetOne)!="new-one"||File.ReadAllText(targetTwo)!="new-two")throw new Exception("Replacement publication failed");
    string retiredOne=Path.Combine(publication,"old(L7).dwg"),retiredTwo=Path.Combine(publication,"old(L8).dwg");
    File.WriteAllText(retiredOne,"old-seven");File.WriteAllText(retiredTwo,"old-eight");
    bool retiredRollback=false;
    using(var locked=new FileStream(retiredTwo,FileMode.Open,FileAccess.Read,FileShare.Read))
    {
        try{OutputSetPublisher.Publish(new[]{stagedOne,stagedTwo},new[]{targetOne,targetTwo},true,Path.Combine(publication,"retirement-rollback"),new[]{retiredOne,retiredTwo});}catch(IOException){retiredRollback=true;}
    }
    if(!retiredRollback||File.ReadAllText(retiredOne)!="old-seven"||File.ReadAllText(retiredTwo)!="old-eight"||File.ReadAllText(targetOne)!="new-one")throw new Exception("Retirement failure did not restore complete previous set");
    OutputSetPublisher.Publish(new[]{stagedOne,stagedTwo},new[]{targetOne,targetTwo},true,Path.Combine(publication,"retirement"),new[]{retiredOne,retiredTwo});
    if(File.Exists(retiredOne)||File.Exists(retiredTwo)||!File.Exists(Path.Combine(publication,"retirement","retired-000-old(L7).dwg")))throw new Exception("Obsolete drawings were not retired with backups");
    bool publicationLocked=false;
    using(var locked=new FileStream(Path.Combine(publication,".specgen-iso-publish.lock"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose))
    {
        try{OutputSetPublisher.Publish(new[]{stagedOne,stagedTwo},new[]{targetOne,targetTwo},true,Path.Combine(publication,"concurrent"));}catch(IOException){publicationLocked=true;}
    }
    if(!publicationLocked)throw new Exception("Concurrent publication was allowed");
    Console.WriteLine("Extra output checks passed: obsolete-sheet retirement, retirement rollback, concurrent publication lock.");
    Console.WriteLine("Output set checks passed: no accidental overwrite, rollback, complete replacement.");
    Console.WriteLine($"PDF checks passed: {pages.Length} ordered pages, existing files preserved.");
}
catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
