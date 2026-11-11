param([Parameter(Mandatory)][string]$BuildOutput,[Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference='Stop'
# Validate every required file before changing the staging directory.
$files=@('IsoGenerator.dll','IsoGenerator.Core.dll','IsoGenerator.Publishing.dll','iso_template.dwg','assets\IsoSymbolStyles.dwg','assets\IsoSkeyAcadBlockMap.xml','assets\PDFsharp-LICENSE.txt')
$files+=@('Microsoft.Extensions.DependencyInjection.Abstractions.dll','Microsoft.Extensions.Logging.Abstractions.dll','System.Security.Cryptography.Pkcs.dll')
$files+=@(Get-ChildItem -LiteralPath $BuildOutput -Filter 'PdfSharp*.dll'|Where-Object Name -ne 'PdfSharp.License.dll'|ForEach-Object Name)
if(!($files -contains 'PdfSharp.dll')){throw 'PDFsharp runtime is missing'}
foreach($file in $files){if(!(Test-Path -LiteralPath (Join-Path $BuildOutput $file))){throw "Missing isometric module file: $file"}}
foreach($file in $files){
    $target=Join-Path $Destination $file
    New-Item -ItemType Directory -Force (Split-Path -Parent $target)|Out-Null
    Copy-Item -LiteralPath (Join-Path $BuildOutput $file) -Destination $target -Force
}

# Hash every mandatory managed dependency and resource, so the installer verifies the
# complete installed payload without an SDK or source tree on the client computer.
$manifestFiles=@('PipingSpecPlugin.dll','DocumentFormat.OpenXml.dll')+$files
$manifest=@(foreach($relative in ($manifestFiles|Sort-Object -Unique)){
    $path=Join-Path $Destination $relative
    if(!(Test-Path -LiteralPath $path)){throw "Missing mandatory payload: $relative"}
    $relative+'|'+(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
})
$manifest|Set-Content -LiteralPath (Join-Path $Destination 'dependency-manifest.txt') -Encoding utf8

# Generate installer checks for the native API files referenced by the projects.
$nativeFiles=@('acad.exe','accoreconsole.exe')
foreach($project in @('..\src\PipingSpecPlugin\PipingSpecPlugin.csproj','..\iso\src\IsoGenerator.AutoCAD\IsoGenerator.AutoCAD.csproj')){
    [xml]$xml=Get-Content -LiteralPath (Join-Path $PSScriptRoot $project) -Raw
    foreach($hint in $xml.SelectNodes('//Reference/HintPath')){
        $relative=([string]$hint.InnerText).Replace('$(AcadInstallDir)\','').Replace('$(Plnt3DDir)\','PLNT3D\')
        if($relative.Contains('$(')){throw "Unresolved host reference: $relative"}
        $nativeFiles+=$relative
    }
}
$nativeFiles=@($nativeFiles|Sort-Object -Unique)
$code=@('function HostFilesPresent(const Location: String): Boolean;','begin','  Result := True;',"  MissingHostFiles := '';" )
foreach($relative in $nativeFiles){$escaped=$relative.Replace("'","''");$code+="  if not FileExists(AddBackslash(Location) + '$escaped') then begin Result := False; MissingHostFiles := MissingHostFiles + '$escaped; '; end;"}
$code+='end;'
$code|Set-Content -LiteralPath (Join-Path $Destination 'host-dependencies.iss') -Encoding utf8
