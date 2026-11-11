using System.Net;using System.Security.Cryptography;using PipingSpecPlugin.Update;
var info=await UpdateService.CheckAsync();
if(!info.CheckSucceeded)throw new Exception(info.CheckError);
if(!info.UpdateAvailable||info.LatestVersion!="1.3.35"||string.IsNullOrEmpty(info.InstallerUrl))throw new Exception("Published update not detected");
using var http=new HttpClient(new HttpClientHandler{DefaultProxyCredentials=CredentialCache.DefaultCredentials}){Timeout=Timeout.InfiniteTimeSpan};http.DefaultRequestHeaders.UserAgent.ParseAdd("SpecGen-Update-Probe");
var path=await UpdateService.DownloadInstallerAsync(info,http,args[0]);
var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
if(!string.Equals(hash,args[1],StringComparison.OrdinalIgnoreCase))throw new Exception("Downloaded release differs from the verified installer");
Console.WriteLine($"UPDATE VERIFIED: {info.CurrentVersion} -> {info.LatestVersion}; {new FileInfo(path).Length} bytes; SHA256 {hash}; installer not launched.");
