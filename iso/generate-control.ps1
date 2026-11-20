param([string]$Snapshot="$PSScriptRoot\output\pipeline.json",[string]$Name='control-sheet')
$ErrorActionPreference='Stop'
if($Name -notmatch '^[a-zA-Z0-9_-]+$'){throw 'Name must be a simple filename without extension'}
$root=$PSScriptRoot; $output=Join-Path $root 'output'
$dwg=Join-Path $output "$Name.dwg"; $pdf=Join-Path $output "$Name.pdf"
if((Test-Path $dwg)-or(Test-Path $pdf)){throw 'Output already exists. Choose a new -Name.'}
$scene=Join-Path $output "$Name.scene.json"
& dotnet run --project "$root\src\IsoGenerator.Cli" -c Release -- $Snapshot $scene "$root\assets\IsoSkeyAcadBlockMap.xml"
if($LASTEXITCODE -ne 0){throw 'Scene generation failed'}
& dotnet build "$root\src\IsoGenerator.AutoCAD" -c Release --nologo
if($LASTEXITCODE -ne 0){throw 'Build failed'}
$working=Join-Path $output "$Name.working.dwg"
Copy-Item -LiteralPath "$root\iso_template.dwg" -Destination $working -Force
$dll="$root\src\IsoGenerator.AutoCAD\bin\Release\net8.0-windows\IsoGenerator.dll"
$script=Join-Path $output "$Name.scr"
@('SECURELOAD','0','_NETLOAD',$dll,'ISORENDER','ISOPDF','_QUIT','_Y')|Set-Content -LiteralPath $script -Encoding ascii
$oldLibrary=$env:ISO_SYMBOL_LIBRARY;$oldScene=$env:ISO_SCENE_INPUT;$oldDwg=$env:ISO_DWG_OUTPUT;$oldPdf=$env:ISO_PDF_OUTPUT
try {
    $env:ISO_SYMBOL_LIBRARY="$root\assets\IsoSymbolStyles.dwg";$env:ISO_SCENE_INPUT=$scene;$env:ISO_DWG_OUTPUT=$dwg;$env:ISO_PDF_OUTPUT=$pdf
    $log=& 'C:\Program Files\Autodesk\AutoCAD 2026\accoreconsole.exe' /i $working /s $script /l en-US 2>&1
    $log|Set-Content -LiteralPath (Join-Path $output "$Name.console.log") -Encoding utf8
    if($LASTEXITCODE -ne 0 -or !(Test-Path $dwg) -or !(Test-Path $pdf)){throw 'DWG/PDF generation failed. See console log.'}
    & python "$root\audit-pdf.py" $pdf
    if($LASTEXITCODE -ne 0){throw "Layout audit failed. Output is diagnostic only; see PDF audit JSON."}
    Write-Output $dwg;Write-Output $pdf
} finally {$env:ISO_SYMBOL_LIBRARY=$oldLibrary;$env:ISO_SCENE_INPUT=$oldScene;$env:ISO_DWG_OUTPUT=$oldDwg;$env:ISO_PDF_OUTPUT=$oldPdf}


