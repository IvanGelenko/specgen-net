using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests;

public class SpecTemplateAccuracyTests
{
    [Fact] public void MalformedReservedRowCannotSilentlyLosePositionOrOverwriteOutput()
    {
        var directory=Path.Combine(Path.GetTempPath(),"spec-template-check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var template=Path.Combine(directory,"template.docx");
            var output=Path.Combine(directory,"output.docx");
            TableCell Cell(string text)=>new(new Paragraph(new Run(new Text(text))));
            using(var doc=WordprocessingDocument.Create(template,WordprocessingDocumentType.Document))
            {
                var part=doc.AddMainDocumentPart();
                part.Document=new Document(new Body(new Table(
                    new TableRow(Enumerable.Range(0,9).Select(i=>Cell("column"))),
                    new TableRow(Cell(SpecConstants.MaterialsSectionHeader)),
                    new TableRow(Cell("bad"),Cell("row")))));
                part.Document.Save();
            }
            File.WriteAllText(output,"previous document");
            Assert.Throws<InvalidOperationException>(()=>SpecTemplateWriter.Write(template,output,
                new[]{new SpecRow{Name="Pipe",Unit="м",Qty=1}}));
            Assert.Equal("previous document",File.ReadAllText(output));
            Assert.Equal(2,Directory.GetFiles(directory).Length);
        }
        finally{Directory.Delete(directory,true);}
    }

    [Theory] [InlineData(double.NaN,"шт.")] [InlineData(double.PositiveInfinity,"м")]
    [InlineData(1.5,"шт.")] [InlineData(.01,"м")] [InlineData(0,"м")]
    public void WriterRejectsQuantitiesItCannotRepresentExactly(double quantity,string unit)
    {
        Assert.Throws<InvalidOperationException>(()=>SpecTemplateWriter.Write("unused.docx","unused-output.docx",
            new[]{new SpecRow{Name="invalid",Qty=quantity,Unit=unit}}));
    }
}
