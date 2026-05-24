using System.Collections.Generic;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace PipingSpecPlugin.Output
{
    /// <summary>
    /// Writes a minimal .docx with a heading and a two-column table of
    /// (category, count). The real spec generator will replace this with
    /// template-based output (heading styles, per-spec sections, etc.);
    /// this version exists to prove DocumentFormat.OpenXml loads and runs
    /// inside the AutoCAD process and to give the next iterations a real
    /// file to compare against the Python edition's output.
    /// </summary>
    public static class SpecWordWriter
    {
        public static void Write(string outputPath,
                                 string projectName,
                                 IEnumerable<KeyValuePair<string, int>> rows)
        {
            using var doc = WordprocessingDocument.Create(outputPath,
                WordprocessingDocumentType.Document);
            var main = doc.AddMainDocumentPart();
            main.Document = new Document();
            var body = main.Document.AppendChild(new Body());

            body.AppendChild(NewParagraph($"Piping specification — {projectName}", bold: true));

            var table = new Table(
                new TableProperties(new TableBorders(
                    new TopBorder    { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new BottomBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new LeftBorder   { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new RightBorder  { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new InsideHorizontalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new InsideVerticalBorder   { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 })));

            table.AppendChild(HeaderRow("Category", "Count"));
            foreach (var kv in rows)
                table.AppendChild(DataRow(kv.Key, kv.Value.ToString()));

            body.AppendChild(table);
            main.Document.Save();
        }

        static Paragraph NewParagraph(string text, bool bold = false)
        {
            var run = new Run();
            if (bold) run.AppendChild(new RunProperties(new Bold()));
            run.AppendChild(new Text(text));
            return new Paragraph(run);
        }

        static TableRow HeaderRow(params string[] cells)
        {
            var row = new TableRow();
            foreach (var c in cells) row.AppendChild(Cell(c, bold: true));
            return row;
        }

        static TableRow DataRow(params string[] cells)
        {
            var row = new TableRow();
            foreach (var c in cells) row.AppendChild(Cell(c, bold: false));
            return row;
        }

        static TableCell Cell(string text, bool bold) => new TableCell(NewParagraph(text, bold));
    }
}
