using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using IsoGenerator.Publishing;

namespace IsoGenerator.WordWorker;

public static class Program
{
    public sealed record Pages(string CoverPdf, string ContentsPdf, string TitleDocx, string ContentsDocx, int ContentsPageCount);
    sealed record Request(IsometricReportDetails Details, string Number, string[] Titles);

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
        }
        catch { }

        if (args.Length == 0)
        {
            Console.WriteLine("Служба экспорта Word/PDF для SpecGen.");
            Console.WriteLine("Использование:");
            Console.WriteLine("  IsoWordWorker front-matter <request.json> <result.json>");
            Console.WriteLine("  IsoWordWorker restore-signatures <source.docx> <target.pdf>");
            return 0;
        }

        if (args.Length >= 3 && string.Equals(args[0], "restore-signatures", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteSignatureRestoration(args[1], args[2]);
        }

        string requestPath;
        string resultPath;
        if (string.Equals(args[0], "front-matter", StringComparison.OrdinalIgnoreCase) && args.Length >= 3)
        {
            requestPath = args[1];
            resultPath = args[2];
        }
        else if (args.Length >= 2)
        {
            requestPath = args[0];
            resultPath = args[1];
        }
        else
        {
            Console.Error.WriteLine("Неверные аргументы командной строки.");
            return 2;
        }

        string work = Path.GetDirectoryName(Path.GetFullPath(requestPath))!;
        string logPath = Path.Combine(work, "report.log");
        using var journal = new Journal(logPath);

        try
        {
            ExecuteFrontMatter(requestPath, resultPath, work, journal);
            return 0;
        }
        catch (Exception ex)
        {
            journal.Error("Сбой рабочего процесса выпуска Word/PDF", ex);
            Console.Error.WriteLine($"[IsoWordWorker] Ошибка: {ex.Message}");
            return 1;
        }
    }

    static int ExecuteSignatureRestoration(string docx, string pdf)
    {
        try
        {
            if (!File.Exists(docx))
            {
                Console.Error.WriteLine($"Файл DOCX не найден: {docx}");
                return 1;
            }
            if (!File.Exists(pdf))
            {
                Console.Error.WriteLine($"Файл PDF не найден: {pdf}");
                return 1;
            }

            WordPdfImageQuality.Restore(docx, pdf);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Аварийное завершение восстановления подписей: {ex}");
            return 1;
        }
    }

    static void ExecuteFrontMatter(string requestPath, string resultPath, string work, Journal journal)
    {
        journal.StepStart("Инициализация задания", requestPath);
        if (!File.Exists(requestPath))
            throw new FileNotFoundException("Файл задания Word не найден.", requestPath);

        var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(requestPath))
            ?? throw new InvalidDataException("Пустое задание Word.");
        journal.StepDone("Инициализация задания", $"Документ: {request.Number}, листов в содержании: {request.Titles.Length}");

        var pages = RenderFrontMatter(request.Details, request.Number, request.Titles, work, journal, CancellationToken.None);

        journal.StepStart("Сохранение результата", resultPath);
        File.WriteAllText(resultPath, JsonSerializer.Serialize(pages));
        journal.StepDone("Сохранение результата", $"Файл {resultPath} записан успешно");
        journal.StepDone("Выпуск титульного листа и содержания", "Все шаги завершены успешно");
    }

    static Pages RenderFrontMatter(IsometricReportDetails details, string number, IReadOnlyList<string> titles,
        string job, Journal journal, CancellationToken token)
    {
        journal.StepStart("Запуск приложения Microsoft Word (COM)");
        var wordType = Type.GetTypeFromProgID("Word.Application")
            ?? throw new InvalidOperationException("Для выпуска титульного листа и содержания установите Microsoft Word.");
        dynamic word = Activator.CreateInstance(wordType)!;
        dynamic? options = null;
        bool? updateLinksAtOpen = null;

        try
        {
            word.Visible = false;
            word.DisplayAlerts = 0;
            word.AutomationSecurity = 3;
            options = word.Options;
            updateLinksAtOpen = (bool)options.UpdateLinksAtOpen;
            options.UpdateLinksAtOpen = false;
            journal.StepDone("Запуск приложения Microsoft Word (COM)", "Приложение Word готово к работе");

            // --- ШАГ 1: Титульный лист DOCX ---
            string coverWord = Path.Combine(job, "title.docx");
            string coverPdf = Path.Combine(job, "title.pdf");

            journal.StepStart("Подготовка титульного листа DOCX", coverWord);
            IsometricDocumentTemplates.PrepareTitle(details.TitleTemplate, coverWord);
            journal.StepDone("Подготовка титульного листа DOCX", $"Файл создан: {coverWord}");

            // --- ШАГ 2: Экспорт титульного листа в PDF через Word ---
            journal.StepStart("Экспорт титульного листа в PDF через Word", coverPdf);
            ExportPdf(word, coverWord, coverPdf, token);
            journal.StepDone("Экспорт титульного листа в PDF через Word", $"Размер: {new FileInfo(coverPdf).Length} байт");

            int coverCount = PdfSetPublisher.PageCount(coverPdf);
            if (coverCount > 1)
            {
                journal.StepStart("Повторный экспорт титула без пустых конечных абзацев");
                string trimmedWord = Path.Combine(job, "title-trimmed.docx");
                string trimmedPdf = Path.Combine(job, "title-trimmed.pdf");
                IsometricDocumentTemplates.PrepareTitle(details.TitleTemplate, trimmedWord, trimTrailingParagraphs: true);
                ExportPdf(word, trimmedWord, trimmedPdf, token);
                if (PdfSetPublisher.PageCount(trimmedPdf) == 1)
                {
                    File.Move(trimmedWord, coverWord, overwrite: true);
                    File.Move(trimmedPdf, coverPdf, overwrite: true);
                    coverCount = 1;
                }
                journal.StepDone("Повторный экспорт титула без пустых конечных абзацев", $"Страниц: {coverCount}");
            }
            if (coverCount != 1)
                throw new InvalidDataException($"Титульный лист должен занимать ровно одну страницу (фактически: {coverCount}). Проверьте шаблон Word.");

            // --- ШАГ 3: Восстановление качества подписей титульного листа (отдельный процесс) ---
            journal.StepStart("Восстановление качества подписей титульного листа", "Запуск в изолированном процессе");
            RunIsolatedSignatureRestoration(coverWord, coverPdf, journal, token);
            journal.StepDone("Восстановление качества подписей титульного листа", "Шаг завершён");

            // --- ШАГ 4: Проверка количества страниц титульного листа ---
            journal.StepStart("Проверка количества страниц титульного листа", coverPdf);
            coverCount = PdfSetPublisher.PageCount(coverPdf);
            if (coverCount != 1)
                throw new InvalidDataException($"После обработки подписей титульный лист содержит {coverCount} страниц вместо одной.");
            journal.StepDone("Проверка количества страниц титульного листа", $"Страниц: {coverCount}");

            // --- ШАГ 5: Цикл подготовки и стабилизации содержания ---
            int contentsPages = 1;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                token.ThrowIfCancellationRequested();
                string contentsWord = Path.Combine(job, $"contents-{attempt}.docx");
                string contentsPdf = Path.ChangeExtension(contentsWord, ".pdf");

                journal.StepStart($"Подготовка содержания DOCX (попытка {attempt}/3)", $"ожидаемое число страниц: {contentsPages}");
                IsometricDocumentTemplates.FillContents(contentsWord, details, number, titles, contentsPages);
                journal.StepDone($"Подготовка содержания DOCX (попытка {attempt}/3)", contentsWord);

                journal.StepStart($"Экспорт содержания в PDF через Word (попытка {attempt}/3)", contentsPdf);
                ExportPdf(word, contentsWord, contentsPdf, token);
                journal.StepDone($"Экспорт содержания в PDF через Word (попытка {attempt}/3)", contentsPdf);

                journal.StepStart($"Проверка количества страниц содержания (попытка {attempt}/3)", contentsPdf);
                int actual = PdfSetPublisher.PageCount(contentsPdf) - 1;
                if (actual < 1)
                    throw new InvalidDataException("Содержание и лист регистрации должны занимать отдельные страницы.");
                journal.StepDone($"Проверка количества страниц содержания (попытка {attempt}/3)", $"фактически: {actual}, ожидалось: {contentsPages}");

                if (actual == contentsPages)
                {
                    journal.Info($"Нумерация листов стабилизирована на попытке {attempt} (листов содержания: {actual})");

                    // --- Восстановление качества подписей содержания (отдельный процесс) ---
                    journal.StepStart("Восстановление качества подписей содержания", "Запуск в изолированном процессе");
                    RunIsolatedSignatureRestoration(contentsWord, contentsPdf, journal, token);
                    journal.StepDone("Восстановление качества подписей содержания", "Шаг завершён");

                    return new Pages(coverPdf, contentsPdf, coverWord, contentsWord, actual);
                }

                journal.Info($"Число страниц содержания изменилось: было {contentsPages}, стало {actual}. Повторный выпуск.");
                contentsPages = actual;
            }

            throw new InvalidDataException("Не удалось стабилизировать нумерацию листов в содержании за 3 попытки.");
        }
        finally
        {
            journal.StepStart("Завершение работы Microsoft Word и освобождение ресурсов");
            try
            {
                if (options is not null)
                {
                    try
                    {
                        if (updateLinksAtOpen.HasValue)
                            options.UpdateLinksAtOpen = updateLinksAtOpen.Value;
                    }
                    finally
                    {
                        Marshal.FinalReleaseComObject(options);
                    }
                }
            }
            finally
            {
                try { word.Quit(0); }
                finally { Marshal.FinalReleaseComObject(word); }
            }
            journal.StepDone("Завершение работы Microsoft Word и освобождение ресурсов");
        }
    }

    static void ExportPdf(dynamic word, string source, string destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        dynamic documents = word.Documents;
        try
        {
            dynamic document = documents.Open(source, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: false, NoEncodingDialog: true);
            try
            {
                document.Repaginate();
                try
                {
                    document.ExportAsFixedFormat2(destination, 17, OptimizeForImageQuality: true);
                }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    // Older Word installations expose only the original export method.
                    document.ExportAsFixedFormat(destination, 17);
                }
                catch (COMException error) when (error.ErrorCode == unchecked((int)0x80020003))
                {
                    document.ExportAsFixedFormat(destination, 17);
                }
            }
            finally
            {
                try { document.Close(0); }
                finally { Marshal.FinalReleaseComObject(document); }
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(documents);
        }
    }

    static void RunIsolatedSignatureRestoration(string docx, string pdf, Journal journal, CancellationToken token)
    {
        string? workerPath = Environment.ProcessPath;
        ProcessStartInfo start;
        if (!string.IsNullOrEmpty(workerPath) && Path.GetExtension(workerPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            start = new ProcessStartInfo(workerPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add("restore-signatures");
            start.ArgumentList.Add(docx);
            start.ArgumentList.Add(pdf);
        }
        else
        {
            string dllPath = typeof(Program).Assembly.Location;
            start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add(dllPath);
            start.ArgumentList.Add("restore-signatures");
            start.ArgumentList.Add(docx);
            start.ArgumentList.Add(pdf);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                journal.Warning("Восстановление качества подписей", $"Не удалось запустить отдельный процесс для {Path.GetFileName(pdf)}. Сохранён обычный PDF Word.");
                CleanTemp(pdf);
                return;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);

            string stderr = "";
            try
            {
                var stderrTask = process.StandardError.ReadToEndAsync();
                process.WaitForExitAsync(linked.Token).GetAwaiter().GetResult();
                stderr = stderrTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                journal.Warning("Восстановление качества подписей", $"Превышено время ожидания процесса ({Path.GetFileName(pdf)}). Сохранён обычный PDF Word.");
                CleanTemp(pdf);
                return;
            }

            if (process.ExitCode == 0)
            {
                journal.StepDone("Восстановление качества подписей", $"Качество подписей успешно восстановлено для {Path.GetFileName(pdf)}");
            }
            else
            {
                journal.Warning("Восстановление качества подписей", $"Процесс завершился с кодом {process.ExitCode}: {stderr.Trim()}. Сохранён обычный PDF Word.");
                CleanTemp(pdf);
            }
        }
        catch (Exception ex)
        {
            journal.Warning("Восстановление качества подписей", $"Ошибка при выполнении процесса ({Path.GetFileName(pdf)}): {ex.Message}. Сохранён обычный PDF Word.");
            CleanTemp(pdf);
        }

        static void CleanTemp(string pdf)
        {
            try
            {
                string temp = pdf + ".images";
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch { }
        }
    }
}
