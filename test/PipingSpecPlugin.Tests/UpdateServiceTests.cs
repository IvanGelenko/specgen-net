using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PipingSpecPlugin.Update;
using Xunit;

namespace PipingSpecPlugin.Tests;
public sealed class UpdateServiceTests
{
 sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
 {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(response(request));}}
 sealed class Capture:IProgress<int>{public int Last;public void Report(int value)=>Last=value;}
 static HttpClient Client(Func<HttpRequestMessage,HttpResponseMessage> response)=>new(new Handler(response));
 static string Root()=>Path.Combine(Path.GetTempPath(),"SpecGenUpdaterTests",Guid.NewGuid().ToString("N"));
 static void Cleanup(string path)
 {
  string prefix=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"SpecGenUpdaterTests"))+Path.DirectorySeparatorChar;
  if(!Path.GetFullPath(path).StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unsafe test cleanup");
  if(Directory.Exists(path))Directory.Delete(path,true);
 }
 static readonly byte[] Exe=Encoding.ASCII.GetBytes("MZ test executable bytes - not executed");
 static UpdateInfo Info()=>new(){LatestVersion="1.3.35",InstallerUrl="https://github.com/IvanGelenko/specgen-net/releases/download/v1.3.35/PipingSpecPlugin-Setup.exe",InstallerSize=Exe.Length,InstallerSha256=Convert.ToHexString(SHA256.HashData(Exe)).ToLowerInvariant()};
 [Fact]public async Task Check_FindsNewPublishedVersion_AndBypassesStaleCaches()
 {
  using var client=Client(request=>{Assert.True(request.Headers.CacheControl.NoCache);Assert.NotEmpty(request.Headers.UserAgent);return new(HttpStatusCode.OK){Content=new StringContent("{\"tag_name\":\"v1.3.35\",\"assets\":[]}")};});
  var result=await UpdateService.CheckAsync(client,"1.3.34");Assert.True(result.CheckSucceeded);Assert.True(result.UpdateAvailable);Assert.Equal("1.3.35",result.LatestVersion);
 }
 [Theory][InlineData(403)][InlineData(407)][InlineData(429)][InlineData(500)]public async Task Check_HttpErrors_AreNotReportedAsUpToDate(int status)
 {using var client=Client(_=>new((HttpStatusCode)status));var result=await UpdateService.CheckAsync(client,"1.3.34");Assert.False(result.CheckSucceeded);Assert.False(result.UpdateAvailable);Assert.NotEmpty(result.CheckError);Assert.Null(result.LatestVersion);}
 [Fact]public async Task Check_MalformedResponse_IsADiagnosticFailure()
 {using var client=Client(_=>new(HttpStatusCode.OK){Content=new StringContent("<html>Proxy error</html>")});var result=await UpdateService.CheckAsync(client,"1.3.34");Assert.False(result.CheckSucceeded);Assert.NotEmpty(result.CheckError);}
 [Fact]public async Task Download_VerifiesHash_ReportsProgress_AndUsesDistinctFiles()
 {
  string root=Root();try{
   using var client=Client(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(Exe)});var progress=new Capture();
   var a=await UpdateService.DownloadInstallerAsync(Info(),client,root,progress);var b=await UpdateService.DownloadInstallerAsync(Info(),client,root);
   Assert.NotEqual(a,b);Assert.Equal(Exe,File.ReadAllBytes(a));Assert.Equal(100,progress.Last);Assert.Empty(Directory.GetFiles(root,"*.part",SearchOption.AllDirectories));
  }finally{Cleanup(root);}
 }
 [Theory][InlineData(true)][InlineData(false)]public async Task Download_RejectsCorruptPayload_AndDeletesPartial(bool wrongHash)
 {
  string root=Root();try{
   var info=Info();byte[] payload=wrongHash?Exe:Encoding.UTF8.GetBytes("<html>Proxy denied</html>");if(wrongHash)info.InstallerSha256=new string('0',64);
   using var client=Client(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(payload)});
   await Assert.ThrowsAsync<IOException>(()=>UpdateService.DownloadInstallerAsync(info,client,root));
   Assert.Empty(Directory.GetFiles(root,"*.part",SearchOption.AllDirectories));Assert.Empty(Directory.GetFiles(root,"*.exe",SearchOption.AllDirectories));
  }finally{Cleanup(root);}
 }
 sealed class StalledStream:Stream
 {
  public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
  public override void Flush(){}public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
  public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){await Task.Delay(Timeout.Infinite,token);return 0;}
 }
 [Fact]public async Task Download_CancellationCoversBody_AfterHeadersHaveArrived()
 {
  string root=Root();try{
   using var client=Client(_=>new(HttpStatusCode.OK){Content=new StreamContent(new StalledStream())});using var cts=new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
   await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>UpdateService.DownloadInstallerAsync(Info(),client,root,cancellationToken:cts.Token));
   Assert.Empty(Directory.GetFiles(root,"*.part",SearchOption.AllDirectories));
  }finally{Cleanup(root);}
 }
}
