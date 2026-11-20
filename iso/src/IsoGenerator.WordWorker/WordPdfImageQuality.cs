using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace IsoGenerator.WordWorker;

// Word may resample small PNGs even with OptimizeForImageQuality enabled.
// Restore an original image only when its dimensions and decoded pixels identify it.
internal static class WordPdfImageQuality
{
    const string ImageSubtype = "/Image";

    public static void TryRestore(string docx, string pdf)
    {
        try { Restore(docx, pdf); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or ExternalException or PdfReaderException)
        {
            // The original Word PDF is already valid and stays available.
            Console.Error.WriteLine("Сохранён штатный PDF Word. Не удалось восстановить исходные изображения: " + error.Message);
        }
    }

    public static void Restore(string docx, string pdf)
    {
        using var package = ZipFile.OpenRead(docx);
        var pictures = new List<Bitmap>();
        string improved = pdf + ".images";
        try
        {
            foreach (var part in package.Entries.Where(entry => entry.FullName.StartsWith("word/media/", StringComparison.Ordinal) &&
                entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = part.Open();
                using var image = Image.FromStream(stream);
                pictures.Add(new Bitmap(image));
            }
            if (pictures.Count == 0) return;
            bool changed = false;
            using (var document = PdfReader.Open(pdf, PdfDocumentOpenMode.Modify))
            {
                var processed = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
                foreach (var page in document.Pages)
                    Visit(page.Elements.GetDictionary("/Resources"));
                if (changed) document.Save(improved);

                void Visit(PdfDictionary? resources)
                {
                    var xobjects = resources?.Elements.GetDictionary("/XObject");
                    if (xobjects is null) return;
                    foreach (string name in xobjects.Elements.Keys.ToArray())
                    {
                        var image = xobjects.Elements.GetDictionary(name);
                        if (image is null || !processed.Add(image)) continue;
                        if (image.Elements.GetName("/Subtype") == "/Form") Visit(image.Elements.GetDictionary("/Resources"));
                        else if (image.Elements.GetName("/Subtype") == ImageSubtype &&
                            FindOriginal(image, pictures) is { } source)
                        {
                            RestoreImage(document, image, source);
                            changed = true;
                        }
                    }
                }
            }
            if (changed) File.Move(improved, pdf, overwrite: true);
        }
        finally
        {
            foreach (var picture in pictures) picture.Dispose();
            if (File.Exists(improved))
            {
                try { File.Delete(improved); } catch { }
            }
        }
    }

    static Bitmap? FindOriginal(PdfDictionary image, IReadOnlyList<Bitmap> originals)
    {
        if (image.Elements.GetName("/ColorSpace") != "/DeviceRGB" || image.Elements.GetInteger("/BitsPerComponent") != 8 ||
           image.Elements.ContainsKey("/Decode") || image.Elements.ContainsKey("/Mask")) return null;
        int width = image.Elements.GetInteger("/Width"), height = image.Elements.GetInteger("/Height");
        if (width <= 0 || height <= 0) return null;
        var candidates = originals.Where(source => source.Width > width && source.Height > height &&
            Math.Abs((double)source.Width * height / source.Height / width - 1) < .06).ToArray();
        if (candidates.Length == 0 || image.Stream?.UnfilteredValue is not { } rgb || rgb.LongLength != (long)width * height * 3) return null;
        var mask = image.Elements.GetDictionary("/SMask")?.Stream?.UnfilteredValue;
        if (mask is not null && mask.Length != rgb.Length / 3) return null;
        var matches = candidates.Select(source => (Source: source, Score: Difference(source, rgb, mask, width, height)))
            .OrderBy(match => match.Score).Take(2).ToArray();
        if (matches[0].Score < .45 && (matches.Length == 1 || matches[1].Score - matches[0].Score > .12)) return matches[0].Source;
        // Very thin pen strokes can lose most of their ink at Word's sampling resolution.
        // Verify their low-frequency shape independently of ink density.
        var shape = candidates.Select(source => (Source: source, Score: ShapeSimilarity(source, rgb, mask, width, height)))
            .OrderByDescending(match => match.Score).Take(2).ToArray();
        return shape[0].Score > .96 && (shape.Length == 1 || shape[0].Score - shape[1].Score > .04) ? shape[0].Source : null;
    }

    static double Difference(Bitmap source, byte[] rgb, byte[]? mask, int width, int height)
    {
        var sample = Pixels(source, width, height);
        long distance = 0, ink = 0;
        for (int i = 0; i < rgb.Length / 3; i++)
            for (int channel = 0; channel < 3; channel++)
            {
                int pdfInk = (255 - rgb[i * 3 + channel]) * (mask?[i] ?? 255);
                int originalInk = (255 - sample[i * 4 + 2 - channel]) * sample[i * 4 + 3];
                distance += Math.Abs(pdfInk - originalInk);
                ink += Math.Max(pdfInk, originalInk);
            }
        return ink == 0 ? 1 : (double)distance / ink;
    }

    static double ShapeSimilarity(Bitmap source, byte[] rgb, byte[]? mask, int width, int height)
    {
        var sample = Pixels(source, width, height);
        var pdf = new double[width * height];
        var original = new double[pdf.Length];
        for (int i = 0; i < pdf.Length; i++)
        {
            pdf[i] = (765 - rgb[i * 3] - rgb[i * 3 + 1] - rgb[i * 3 + 2]) * (mask?[i] ?? 255);
            original[i] = (765 - sample[i * 4] - sample[i * 4 + 1] - sample[i * 4 + 2]) * sample[i * 4 + 3];
        }
        double aa = 0, bb = 0, ab = 0;
        for (int y = 3; y + 3 < height; y++)
            for (int x = 3; x + 3 < width; x++)
            {
                double a = 0, b = 0;
                for (int dy = -3; dy <= 3; dy++)
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        int index = (y + dy) * width + x + dx;
                        int weight = (4 - Math.Abs(dx)) * (4 - Math.Abs(dy));
                        a += pdf[index] * weight;
                        b += original[index] * weight;
                    }
                aa += a * a; bb += b * b; ab += a * b;
            }
        return aa > 0 && bb > 0 ? ab / Math.Sqrt(aa * bb) : 0;
    }

    static void RestoreImage(PdfDocument document, PdfDictionary image, Bitmap source)
    {
        var pixels = Pixels(source, source.Width, source.Height);
        var rgb = new byte[pixels.Length / 4 * 3];
        var alpha = new byte[pixels.Length / 4];
        for (int i = 0; i < alpha.Length; i++)
        {
            rgb[i * 3] = pixels[i * 4 + 2]; rgb[i * 3 + 1] = pixels[i * 4 + 1]; rgb[i * 3 + 2] = pixels[i * 4]; alpha[i] = pixels[i * 4 + 3];
        }
        Write(image, source.Width, source.Height, rgb);
        if (alpha.All(value => value == 255))
        {
            image.Elements.Remove("/SMask");
            return;
        }
        var mask = new PdfDictionary(document);
        mask.Elements.SetName("/Type", "/XObject");
        mask.Elements.SetName("/Subtype", ImageSubtype);
        mask.Elements.SetName("/ColorSpace", "/DeviceGray");
        mask.Elements.SetInteger("/BitsPerComponent", 8);
        Write(mask, source.Width, source.Height, alpha);
        document.Internals.AddObject(mask);
        image.Elements["/SMask"] = mask.ReferenceNotNull;
    }

    static void Write(PdfDictionary image, int width, int height, byte[] bytes)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(bytes);
        var data = compressed.ToArray();
        image.Elements.Remove("/DecodeParms");
        image.Elements.SetName("/Filter", "/FlateDecode");
        image.Elements.SetInteger("/Width", width);
        image.Elements.SetInteger("/Height", height);
        image.Elements.SetInteger("/Length", data.Length);
        image.Elements.SetBoolean("/Interpolate", true);
        if (image.Stream is null) image.CreateStream(data); else image.Stream.Value = data;
    }

    static byte[] Pixels(Bitmap source, int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        }
        var bounds = new Rectangle(0, 0, width, height);
        var bits = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var data = new byte[width * height * 4];
            for (int y = 0; y < height; y++) Marshal.Copy(bits.Scan0 + y * bits.Stride, data, y * width * 4, width * 4);
            return data;
        }
        finally { bitmap.UnlockBits(bits); }
    }
}
