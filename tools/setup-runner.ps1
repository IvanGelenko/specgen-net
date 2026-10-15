<#
.SYNOPSIS
  One-shot installer for the GitHub Actions self-hosted runner used to build
  and release PipingSpecPlugin on a machine that has AutoCAD Plant 3D 2025 +
  Inno Setup 6.

.DESCRIPTION
  Downloads the runner into C:\actions-runner (GitHub's recommended path on
  Windows), registers it against the repo with the default self-hosted +
  windows labels, and installs it as an auto-starting Windows service.

  The registration TOKEN is short-lived (~1 hour) and single-use. Get a fresh
  one from:  GitHub repo -> Settings -> Actions -> Runners -> New runner
  (the "Configure" step shows  ./config.cmd --url ... --token <TOKEN>).

.PARAMETER Token
  The registration token from the New runner page.

.PARAMETER RunnerVersion
  Runner package version (default matches the New runner page at setup time).

.EXAMPLE
  # From an ELEVATED PowerShell (service install needs admin):
  ./tools/setup-runner.ps1 -Token ALUUB3BANKBYNA6PLUERTZLKDQYAS
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Token,

    [string] $RepoUrl = "https://github.com/IvanGelenko/specgen-net",
    [string] $RunnerVersion = "2.334.0",
    [string] $InstallDir = "C:\actions-runner",
    [string] $RunnerName = $env:COMPUTERNAME
)

$ErrorActionPreference = "Stop"

Write-Host "=== GitHub Actions runner setup ===" -ForegroundColor Cyan
Write-Host "Repo:    $RepoUrl"
Write-Host "Version: $RunnerVersion"
Write-Host "Dir:     $InstallDir"
Write-Host "Name:    $RunnerName"
Write-Host ""

# --- prerequisites (build needs these; fail early with a clear message) ----
$checks = @(
    @{ Name = "AutoCAD 2025 DLLs"; Path = "C:\Program Files\Autodesk\AutoCAD 2025\AcCoreMgd.dll" },
    @{ Name = "Inno Setup 6";      Path = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" }
)
foreach ($c in $checks) {
    if (-not (Test-Path $c.Path)) {
        throw "Missing prerequisite: $($c.Name) (expected at $($c.Path)). The release build will fail without it."
    }
    Write-Host "OK: $($c.Name)" -ForegroundColor Green
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK not found on PATH."
}
Write-Host "OK: dotnet SDK $(dotnet --version)" -ForegroundColor Green
Write-Host ""

# --- download + extract ----------------------------------------------------
if (Test-Path (Join-Path $InstallDir "config.cmd")) {
    Write-Host "Runner already extracted in $InstallDir - skipping download." -ForegroundColor Yellow
} else {
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    $zip = Join-Path $InstallDir "actions-runner-win-x64-$RunnerVersion.zip"
    $url = "https://github.com/actions/runner/releases/download/v$RunnerVersion/actions-runner-win-x64-$RunnerVersion.zip"
    Write-Host "Downloading $url ..."
    Invoke-WebRequest -Uri $url -OutFile $zip
    Write-Host "Extracting ..."
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $InstallDir)
    Remove-Item $zip -Force
    Write-Host "OK: extracted to $InstallDir" -ForegroundColor Green
}
Write-Host ""

# --- configure + install service -------------------------------------------
# --unattended      : no prompts
# --replace         : re-register cleanly if a runner with this name exists
# --runasservice    : install + auto-start as a Windows service
# default labels (self-hosted, Windows, X64) are what release.yml expects.
Push-Location $InstallDir
try {
    Write-Host "Configuring runner as a service ..."
    & "$InstallDir\config.cmd" --unattended `
        --url $RepoUrl `
        --token $Token `
        --name $RunnerName `
        --replace `
        --runasservice
    if ($LASTEXITCODE -ne 0) { throw "config.cmd failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Cyan
Write-Host "Runner '$RunnerName' is registered and running as a service."
Write-Host "Verify under: $RepoUrl/settings/actions/runners (status should be Idle)."
Write-Host "Release it later (if needed) with:  $InstallDir\config.cmd remove --token RUNNER_REMOVAL_TOKEN"
