param([string]$Drawing = "$PSScriptRoot\iso_template.dwg")
$ErrorActionPreference = 'Stop'
$console = 'C:\Program Files\Autodesk\AutoCAD 2026\accoreconsole.exe'
$project = "$PSScriptRoot\src\IsoGenerator.AutoCAD\IsoGenerator.AutoCAD.csproj"
$dll = "$PSScriptRoot\src\IsoGenerator.AutoCAD\bin\Release\net8.0-windows\IsoGenerator.dll"
$source = (Resolve-Path -LiteralPath $Drawing).Path
$output = Join-Path $PSScriptRoot 'output'
New-Item -ItemType Directory -Force $output | Out-Null
& dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$name = [IO.Path]::GetFileNameWithoutExtension($source)
$copy = Join-Path $output "$name.inspect.dwg"
if ($source -eq $copy) { throw 'Choose the original source, not the inspection copy' }
Copy-Item -LiteralPath $source -Destination $copy -Force
$report = Join-Path $output "$name.json"
$script = Join-Path $output 'inspect.scr'
# Changes only the disposable console session; the source DWG is never opened for writing.
@("SECURELOAD", "0", "_NETLOAD", $dll, "ISOINSPECT", "_QUIT", "_Y") | Set-Content -LiteralPath $script -Encoding ascii
$previous = $env:ISO_INSPECT_OUTPUT
try {
    $env:ISO_INSPECT_OUTPUT = $report
    $started = [DateTime]::UtcNow
    & $console /i $copy /s $script /l en-US | Out-Null
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $report) -or (Get-Item -LiteralPath $report).LastWriteTimeUtc -lt $started) { throw 'Inspection failed; fresh report was not produced' }
    Write-Output $report
} finally { $env:ISO_INSPECT_OUTPUT = $previous }
