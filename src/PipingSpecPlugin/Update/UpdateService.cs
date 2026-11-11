using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace PipingSpecPlugin.Update
{
    // Automatic checks stay quiet, but their failure is distinct from "up to date".
    public static class UpdateService
    {
        const string UserAgent="PipingSpecPlugin-Updater";
        static readonly HttpClient Http=CreateClient(TimeSpan.FromSeconds(15));
        static readonly HttpClient Download=CreateClient(Timeout.InfiniteTimeSpan);
        public static string LogFilePath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PipingSpecPlugin","update.log");
        static HttpClient CreateClient(TimeSpan timeout)
        {
            var handler=new HttpClientHandler{DefaultProxyCredentials=CredentialCache.DefaultCredentials};
            var client=new HttpClient(handler){Timeout=timeout};
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            return client;
        }
        public static string CurrentVersion()
        {
            try{var v=Assembly.GetExecutingAssembly().GetName().Version;return v==null?"0.0.0":$"{v.Major}.{v.Minor}.{v.Build}";}
            catch{return "0.0.0";}
        }
        public static Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken=default)=>CheckAsync(Http,CurrentVersion(),cancellationToken);
        internal static async Task<UpdateInfo> CheckAsync(HttpClient client,string current,CancellationToken cancellationToken=default)
        {
            try
            {
                using var request=new HttpRequestMessage(HttpMethod.Get,UpdateChecker.LatestReleaseApi);
                request.Headers.UserAgent.ParseAdd(UserAgent);request.Headers.Accept.ParseAdd("application/vnd.github+json");
                request.Headers.CacheControl=new CacheControlHeaderValue{NoCache=true,MaxAge=TimeSpan.Zero};
                using var response=await client.SendAsync(request,cancellationToken).ConfigureAwait(false);
                if(!response.IsSuccessStatusCode)
                {
                    string reason=(int)response.StatusCode switch
                    {
                        403 or 429=>"GitHub ограничил доступ к проверке обновлений (возможен лимит запросов). Повторите позже.",
                        407=>"Прокси-сервер требует авторизацию. Проверьте настройки сети Windows.",
                        404=>"На GitHub пока нет опубликованного релиза с обновлением.",
                        _=>$"GitHub вернул HTTP {(int)response.StatusCode}."
                    };
                    Log(reason);return UpdateInfo.Failed(current,reason);
                }
                var result=UpdateChecker.Evaluate(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false),current);
                Log(result.CheckSucceeded?$"Проверка: установлена {current}, опубликована {result.LatestVersion}.":result.CheckError);
                return result;
            }
            catch(OperationCanceledException){const string error="Проверка обновлений отменена или превышено время ожидания GitHub.";Log(error);return UpdateInfo.Failed(current,error);}
            catch(Exception ex){string error="Не удалось подключиться к GitHub: "+ex.Message;Log(error);return UpdateInfo.Failed(current,error);}
        }
        public static async Task<bool> DownloadAndLaunchInstallerAsync(UpdateInfo info,IProgress<int> progress=null,CancellationToken cancellationToken=default)
        {
            string path=await DownloadInstallerAsync(info,Download,Path.Combine(Path.GetTempPath(),"SpecGen","updates"),progress,cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo{FileName=path,UseShellExecute=true});
            if(process==null)throw new IOException("Установщик загружен, но Windows не смогла его запустить. Файл: "+path);
            Log("Установщик запущен: "+info.LatestVersion);return true;
        }
        // Separate download from execution: tests and release verification never launch an installer.
        internal static async Task<string> DownloadInstallerAsync(UpdateInfo info,HttpClient client,string destinationRoot,IProgress<int> progress=null,CancellationToken cancellationToken=default)
        {
            if(info==null||!Version.TryParse(info.LatestVersion,out var version)||!Uri.TryCreate(info.InstallerUrl,UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps)
                throw new IOException("В релизе отсутствует корректная ссылка HTTPS на установщик.");
            string folder=Path.Combine(destinationRoot,version.ToString(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            string target=Path.Combine(folder,UpdateChecker.InstallerAssetName),partial=target+".part";
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromMinutes(10));var token=timeout.Token;
            try
            {
                using(var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))
                {
                    if(!response.IsSuccessStatusCode)throw new IOException($"Не удалось скачать установщик: HTTP {(int)response.StatusCode}.");
                    long? total=response.Content.Headers.ContentLength??info.InstallerSize;long copied=0;
                    await using var source=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                    await using var destination=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true);
                    var buffer=new byte[65536];int read;
                    while((read=await source.ReadAsync(buffer.AsMemory(),token).ConfigureAwait(false))>0)
                    {
                        await destination.WriteAsync(buffer.AsMemory(0,read),token).ConfigureAwait(false);copied+=read;
                        progress?.Report(total>0?(int)Math.Min(99,copied*100/total.Value):-1);
                    }
                    await destination.FlushAsync(token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                using(var file=File.OpenRead(partial))
                {
                    if(file.ReadByte()!='M'||file.ReadByte()!='Z')throw new IOException("Вместо установщика получен другой файл (возможно, страница блокировки прокси).");
                    file.Position=0;
                    if(!UpdateChecker.VerifyDownload(file,info.InstallerSha256,info.InstallerSize))throw new IOException("Размер или SHA-256 установщика не совпадает с опубликованным файлом. Установщик не запущен.");
                }
                File.Move(partial,target);progress?.Report(100);Log("Загружен и проверен установщик: "+info.LatestVersion);return target;
            }
            catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested)
            {TryDelete(partial);const string error="Превышено время загрузки установщика (10 минут).";Log(error);throw new IOException(error);}
            catch(Exception ex){TryDelete(partial);Log("Загрузка не завершена: "+ex.Message);throw;}
        }
        static void TryDelete(string path){try{if(File.Exists(path))File.Delete(path);}catch{}}
        static void Log(string message)
        {try{Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath));File.AppendAllText(LogFilePath,DateTimeOffset.UtcNow.ToString("O")+" "+message+Environment.NewLine);}catch{}}
    }
}
