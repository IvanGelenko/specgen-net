param([Parameter(Mandatory)][string]$EngineDirectory,[Parameter(Mandatory)][string]$ScenesGlob,[Parameter(Mandatory)][string]$OutputDirectory,[Parameter(Mandatory)][string]$BuildOutput,[Parameter(Mandatory)][string]$TemplatePath,[Parameter(Mandatory)][string]$SymbolLibrary)
$ErrorActionPreference='Stop'
$scenes=@(Get-ChildItem -Path $ScenesGlob | Sort-Object Name | ForEach-Object FullName)
if(!$scenes.Count){throw 'No input scenes'}
New-Item -ItemType Directory -Force $OutputDirectory|Out-Null
$pages=@(for($i=1;$i -le $scenes.Count;$i++){Join-Path $OutputDirectory ('sheet-'+$i.ToString('00')+'\sheet.pdf')})
$pagesFile=Join-Path $OutputDirectory 'pages.json';ConvertTo-Json -InputObject @($pages)|Set-Content -LiteralPath $pagesFile -Encoding utf8
$finalPdf=Join-Path $OutputDirectory 'combined.pdf'
for($i=0;$i -lt $scenes.Count;$i++){
 $work=Split-Path -Parent $pages[$i];New-Item -ItemType Directory -Force $work|Out-Null
 $working=Join-Path $work 'working.dwg';Copy-Item -LiteralPath $TemplatePath -Destination $working
 $commands=@('SECURELOAD','0','_NETLOAD',('"'+(Join-Path $BuildOutput 'IsoGenerator.dll')+'"'),'ISORENDER','ISOPDF','ISOINSPECT')
 if($i -eq $scenes.Count-1){$commands+='ISOMERGEPDF'};$commands+=@('_QUIT','_Y')
 $script=Join-Path $work 'render.scr';$commands|Set-Content -LiteralPath $script -Encoding utf8BOM
 $env:ISO_SCENE_INPUT=$scenes[$i];$env:ISO_DWG_OUTPUT=Join-Path $work 'output.dwg';$env:ISO_PDF_OUTPUT=$pages[$i];$env:ISO_INSPECT_OUTPUT=Join-Path $work 'report.json';$env:ISO_SYMBOL_LIBRARY=$SymbolLibrary;$env:ISO_RENDER_TRACE=Join-Path $work 'render.log';$env:ISO_SHEET_NUMBER=[string]($i+1);$env:ISO_SHEET_COUNT=[string]$scenes.Count;$env:ISO_PDF_PAGES_INPUT=$pagesFile;$env:ISO_MERGED_PDF_OUTPUT=$finalPdf
 $p=Start-Process (Join-Path $EngineDirectory 'accoreconsole.exe') -ArgumentList @('/i',('"'+$working+'"'),'/s',('"'+$script+'"')) -WindowStyle Hidden -RedirectStandardOutput (Join-Path $work 'console.log') -RedirectStandardError (Join-Path $work 'error.log') -PassThru
 if(!$p.WaitForExit(60000)){$p.Kill();throw 'Native render timed out'}
 if($p.ExitCode -ne 0 -or !(Test-Path $env:ISO_DWG_OUTPUT) -or !(Test-Path $pages[$i]) -or !(Test-Path $env:ISO_INSPECT_OUTPUT)){throw "Native validation failed on sheet $($i+1): $work"}
 Write-Output "Native sheet $($i+1)/$($scenes.Count): $EngineDirectory"
}
if(!(Test-Path $finalPdf)){throw 'Combined PDF missing'}
Write-Output "COMPLETE $finalPdf"
