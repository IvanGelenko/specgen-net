using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PipingSpecPlugin.Spec;
using Xunit;
namespace PipingSpecPlugin.Tests
{
 public class RegressionTests
 {
  [Theory]
  [InlineData("150-KSN011-LS25CH-NH", "011")]
  [InlineData("150-KSN001-LS016C-HN", "001")]
  [InlineData("150-KSN000-LS25CH-NH", "000")]
  [InlineData("150-KSN11-LS25CH-NH", null)]
  [InlineData("150-KSN1011-LS25CH-NH", null)]
  [InlineData("150-P22002/2-LS025C-HN", null)]
  public void NumberMapping(string designation, string expected)
  {
   var line = new LineGroupInfo { RowId=1, Id="1" };
   line.AllProperties["Номер"]="previous";
   var plan=LineSyncService.BuildPlan(new List<VtRowData>{new VtRowData{NN="1",Designation=designation}},new List<LineGroupInfo>{line});
   var changes=plan.Actions.Single().Changes;
   Assert.Equal(expected,changes.SingleOrDefault(c=>c.PropertyName=="Номер")?.NewValue);
   Assert.Equal(designation,changes.Single(c=>c.PropertyName=="Tag").NewValue);
  }
  [Theory]
  [InlineData("", "150-KSN001-LS016C-HN")]
  [InlineData("150-KSN001-LS016C-HN (Врезка 2; 5)", "150-KSN001-LS016C-HN")]
  [InlineData("150-KSN001-LS016C-HN", null)]
  public void TagIsWrittenWithoutVtRemark(string currentTag, string expectedNewTag)
  {
   var line = new LineGroupInfo { RowId=1, Id="1" };
   line.AllProperties["Tag"]=currentTag;
   var plan=LineSyncService.BuildPlan(new List<VtRowData>{new VtRowData{NN="1",Designation="150-KSN001-LS016C-HN (Врезка 2; 5)"}},new List<LineGroupInfo>{line});
   var changes=plan.Actions.SelectMany(a=>a.Changes).ToList();
   Assert.Equal(expectedNewTag,changes.SingleOrDefault(c=>c.PropertyName=="Tag")?.NewValue);
   Assert.Equal("001",changes.SingleOrDefault(c=>c.PropertyName=="Номер")?.NewValue);
   Assert.DoesNotContain(changes,c=>(c.NewValue??"").Contains("Врезка"));
  }
  [Fact] public void UserFormatParsesOtherFields()
  {
   var p=LineDesignationParser.Parse("150-KSN011-LS25CH-NH");
   Assert.NotNull(p); Assert.Equal("011",p.LineNumber); Assert.Equal("25",p.PressureCode); Assert.Equal("CH",p.FlangeCode);
  }
  [Theory] [InlineData("begin")] [InlineData("write")] [InlineData("accept")] [InlineData("commit")]
  public void TransactionFailureStopsAndReports(string fail)
  {
   var calls=new List<string>();
   void Step(string name) {calls.Add(name);if(name==fail)throw new Exception(name);}
   var errors=SyncTransaction.Run(()=>Step("begin"),()=>Step("write"),()=>Step("accept"),()=>Step("commit"),()=>Step("rollback"));
   Assert.NotEmpty(errors);
   var expected=new[]{"begin","write","accept","commit"}.Take(Array.IndexOf(new[]{"begin","write","accept","commit"},fail)+1).ToList();
   if(fail!="begin")expected.Add("rollback");
   Assert.Equal(expected,calls);
  }
  [Fact] public void RollbackFailureReported()
  {
   var errors=SyncTransaction.Run(()=>{},()=>throw new Exception("write"),()=>{},()=>{},()=>throw new Exception("rollback"));
   Assert.Equal(2,errors.Count);
  }
  [Fact] public void TransactionSuccess()
  {
   var calls=new List<string>();
   Assert.Empty(SyncTransaction.Run(()=>calls.Add("begin"),()=>calls.Add("write"),()=>calls.Add("accept"),()=>calls.Add("commit"),()=>calls.Add("rollback")));
   Assert.Equal(new[]{"begin","write","accept","commit"},calls);
  }
  [Theory] [InlineData(0, false)] [InlineData(1, false)] [InlineData(8, false)]
  [InlineData(0, true)] [InlineData(1, true)] [InlineData(8, true)]
  public void MaterialsStayBeforeFollowingSection(int reserved, bool overwrite)
  {
   var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
   try {
    var template=Path.Combine(dir,"template.docx");var output=Path.Combine(dir,"out.docx");
    using(var d=WordprocessingDocument.Create(template,WordprocessingDocumentType.Document)){
     var m=d.AddMainDocumentPart();m.Document=new Document(new Body());var table=new Table();m.Document.Body.Append(table);
     TableRow Data()=>new TableRow(Enumerable.Range(0,9).Select(_=>new TableCell(new Paragraph(new Run(new Text("sample"))))));
     table.Append(Data());table.Append(new TableRow(new TableCell(new Paragraph(new Run(new Text("Изделия и материалы"))))));
     for(int i=0;i<reserved;i++)table.Append(Data());
     table.Append(new TableRow(new TableCell(new Paragraph(new Run(new Text("Следующий раздел"))))));m.Document.Save();
    }
    var originalTemplate=File.ReadAllBytes(template);
    if(overwrite)File.WriteAllText(output,"old result");
    SpecTemplateWriter.Write(template,output,new List<SpecRow>{new SpecRow{Category="Pipe",Name="TEST-PIPE",Unit="м",Qty=1}});
    Assert.Equal(originalTemplate,File.ReadAllBytes(template));
    using var doc=WordprocessingDocument.Open(output,false);
    var rows=doc.MainDocumentPart.Document.Body.Descendants<TableRow>().ToList();
    Assert.True(rows.FindIndex(x=>x.InnerText.Contains("TEST-PIPE"))<rows.FindIndex(x=>x.InnerText.Contains("Следующий раздел")));
   } finally {Directory.Delete(dir,true);}
  }
  [Fact] public void InvalidTemplatePreservesExistingOutput()
  {
   var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
   try{
    var template=Path.Combine(dir,"bad.docx");var output=Path.Combine(dir,"out.docx");
    File.WriteAllText(template,"not a docx");File.WriteAllText(output,"old result");
    Assert.ThrowsAny<Exception>(()=>SpecTemplateWriter.Write(template,output,new List<SpecRow>()));
    Assert.Equal("old result",File.ReadAllText(output));Assert.Equal(2,Directory.GetFiles(dir).Length);
    Assert.Throws<InvalidOperationException>(()=>SpecTemplateWriter.Write(template,template,new List<SpecRow>()));
    Assert.Equal("not a docx",File.ReadAllText(template));
   }finally{Directory.Delete(dir,true);}
  }
 }
}
