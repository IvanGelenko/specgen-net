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
    string marked=Path.Combine(publication,"marked.pdf"), pageFile=Path.Combine(publication,"page.pdf");
    using(var pdf=new PdfSharp.Pdf.PdfDocument())
    {
        for(int i=0;i<3;i++)
            pdf.AddPage().Annotations.Add(new PdfSharp.Pdf.Annotations.PdfTextAnnotation { Contents=$"marker-{i}" });
        pdf.Save(marked);
    }
    using(var pdf=new PdfSharp.Pdf.PdfDocument()){pdf.AddPage();pdf.Save(pageFile);}
    string partial=Path.Combine(publication,"partial.pdf");
    PdfSetPublisher.MergeWithRetainedPages(new[]{pageFile,pageFile,pageFile},partial,"Partial",null,null,marked,new HashSet<int>{0,2});
    using(var pdf=PdfReader.Open(partial,PdfDocumentOpenMode.Import))
    {
        if(pdf.PageCount!=3||pdf.Pages[0].Annotations.Count!=1||pdf.Pages[1].Annotations.Count!=0||pdf.Pages[2].Annotations.Count!=1||
           pdf.Pages[0].Annotations[0].Contents!="marker-0"||pdf.Pages[2].Annotations[0].Contents!="marker-2")
            throw new Exception("Partial publication lost annotations or retained the replaced page.");
    }
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
    OutputSetPublisher.Publish(new[]{stagedOne,stagedTwo},new[]{targetOne,targetTwo},true,Path.Combine(publication,"rollback"));
    if(File.ReadAllText(targetOne)!="new-one"||File.ReadAllText(targetTwo)!="new-two")throw new Exception("Replacement publication failed");
    string retained=Path.Combine(publication,"retained.dwg");
    File.WriteAllText(retained,"approved-sheet");
    byte[] retainedBefore=File.ReadAllBytes(retained);
    var retainedWriteTime=File.GetLastWriteTimeUtc(retained);
    string retainedHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(retainedBefore));
    var retainedOutputs=new Dictionary<string,string>{{retained,retainedHash}};
    using(var readOnly=new FileStream(retained,FileMode.Open,FileAccess.Read,FileShare.Read))
        OutputSetPublisher.Publish(new[]{stagedTwo},new[]{targetTwo},true,Path.Combine(publication,"single-sheet-update"),
            retainedOutputs:retainedOutputs);
    if(!File.ReadAllBytes(retained).SequenceEqual(retainedBefore)||File.GetLastWriteTimeUtc(retained)!=retainedWriteTime)
        throw new Exception("Partial publication modified an unchanged sheet.");
    File.WriteAllText(retained,"different sheet from a concurrent publication");
    File.WriteAllText(stagedTwo,"must not publish");
    bool rejectedChangedRetained=false;
    try
    {
        OutputSetPublisher.Publish(new[]{stagedTwo},new[]{targetTwo},true,Path.Combine(publication,"retained-changed"),
            retainedOutputs:retainedOutputs);
    }
    catch(IOException){rejectedChangedRetained=true;}
    if(!rejectedChangedRetained||File.ReadAllText(targetTwo)!="new-two")
        throw new Exception("Publication accepted a retained sheet from another revision.");
    File.WriteAllText(stagedTwo,"new-two");
    string retiredOne=Path.Combine(publication,"old(L7).dwg"),retiredTwo=Path.Combine(publication,"old(L8).dwg");
    File.WriteAllText(retiredOne,"old-seven");File.WriteAllText(retiredTwo,"old-eight");
    bool retiredRollback=false;
    using(var locked=new FileStream(retiredTwo,FileMode.Open,FileAccess.Read,FileShare.Read))
    {
        try{OutputSetPublisher.Publish(new[]{stagedOne,stagedTwo},new[]{targetOne,targetTwo},true,Path.Combine(publication,"retirement-rollback"),new[]{retiredOne,retiredTwo});}catch(IOException){retiredRollback=true;}
    }
    if(!retiredRollback||File.ReadAllText(retiredOne)!="old-seven"||File.ReadAllText(retiredTwo)!="old-eight"||File.ReadAllText(targetOne)!="new-one")throw new Exception("Retirement failure did not restore complete previous set");
    OutputSetPublisher.Publish(new[]{stagedOne,stagedTwo},new[]{targetOne,targetTwo},true,Path.Combine(publication,"retirement"),new[]{retiredOne,retiredTwo});
    if(File.Exists(retiredOne)||File.Exists(retiredTwo)||!Directory.EnumerateFiles(Path.Combine(publication,"retirement"),"retired-000-old(L7).dwg",SearchOption.AllDirectories).Any())throw new Exception("Obsolete drawings were not retired with backups");
    bool publicationLocked=false;
    using(var locked=new FileStream(Path.Combine(publication,".specgen-iso-publish.lock"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose))
    {
        try{OutputSetPublisher.Publish(new[]{stagedOne,stagedTwo},new[]{targetOne,targetTwo},true,Path.Combine(publication,"concurrent"));}catch(IOException){publicationLocked=true;}
    }
    if(!publicationLocked)throw new Exception("Concurrent publication was allowed");
    string aPdf=Path.Combine(publication,"A.pdf"),bPdf=Path.Combine(publication,"B.pdf"),common=Path.Combine(publication,"LINE(L1).dwg");
    string stale=Path.Combine(publication,"LINE(L2).dwg"),foreign=Path.Combine(publication,"LINE(L3).dwg");
    File.WriteAllText(foreign,"untracked");
    OutputSetPublisher.PublishDocument(aPdf,new[]{stagedOne,stagedOne,stagedTwo},new[]{common,stale,aPdf},false,publication,"first");
    if(!OutputSetPublisher.IsCurrent(aPdf,"first",new[]{common,stale,aPdf}))throw new Exception("Manifest did not identify current release.");
    bool protectedOwnership=false;
    try{OutputSetPublisher.PublishDocument(bPdf,new[]{stagedOne,stagedTwo},new[]{common,bPdf},true,publication);}
    catch(OutputOwnershipConflictException){protectedOwnership=true;}
    if(!protectedOwnership||File.Exists(bPdf)||!File.Exists(common))throw new Exception("A different document overwrote a managed drawing.");
    OutputSetPublisher.PublishDocument(aPdf,new[]{stagedTwo,stagedOne},new[]{common,aPdf},true,publication,"second");
    if(File.Exists(stale)||!File.Exists(foreign)||!OutputSetPublisher.IsCurrent(aPdf,"second",new[]{common,aPdf}))
        throw new Exception("Replacing a document did not retire only its previous unchanged drawings.");
    File.AppendAllText(common,"edited");
    if(OutputSetPublisher.IsCurrent(aPdf,"second",new[]{common,aPdf}))throw new Exception("An edited output was mistaken for a current release.");
    if(!OutputSetPublisher.IsOwnedOutput(aPdf,aPdf)||OutputSetPublisher.IsOwnedOutput(aPdf,bPdf))
        throw new Exception("Append must only use files owned by the selected document.");
    bool detectedConcurrentAppend=false;
    try
    {
        OutputSetPublisher.PublishDocument(aPdf,new[]{stagedOne},new[]{aPdf},true,publication,
            expectedPreviousPdfHash:new string('0',64));
    }
    catch(InvalidDataException){detectedConcurrentAppend=true;}
    if(!detectedConcurrentAppend||!OutputSetPublisher.IsOwnedOutput(aPdf,aPdf)||File.ReadAllText(common)!="new-twoedited")
        throw new Exception("Concurrent append should be rejected without changing the published set.");
    Console.WriteLine("Document ownership, retirement and fingerprint checks passed.");
    Console.WriteLine("Extra output checks passed: obsolete-sheet retirement, retirement rollback, concurrent publication lock.");
    Console.WriteLine("Output set checks passed: no accidental overwrite, rollback, complete replacement.");
    Console.WriteLine($"PDF checks passed: {pages.Length} ordered pages, existing files preserved.");
}
catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
