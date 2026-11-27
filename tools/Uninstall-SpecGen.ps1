[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Desktop') {
    # Load the inbox modules even if a roaming module path is offline.
    $originalModulePath = $env:PSModulePath
    try {
        $env:PSModulePath = "$PSHOME\Modules"
        Import-Module Microsoft.PowerShell.Management
        Import-Module Microsoft.PowerShell.Utility
    }
    finally {
        $env:PSModulePath = $originalModulePath
    }
}

$bundle = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\PipingSpecPlugin.bundle'
$manifest = Join-Path $bundle 'PackageContents.xml'
$disabledManifest = "$manifest.disabled"
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{B5E20C42-5C4D-4F1B-9B7C-1D5E2B0A8C05}_is1'
$acadRoot = 'HKCU:\Software\Autodesk\AutoCAD'
$modulePath = 'Contents\Windows\PipingSpecPlugin.dll'
# AutoCAD may append a dot to the bundle directory in its cached LOADER path.
$loaderPaths = (Join-Path $bundle $modulePath), (Join-Path "$bundle." $modulePath)
$autoloadKeys = @(if (Test-Path -LiteralPath $acadRoot) {
    foreach ($series in Get-ChildItem -LiteralPath $acadRoot) {
        foreach ($product in Get-ChildItem -LiteralPath $series.PSPath) {
            $application = Join-Path $product.PSPath 'Applications\PipingSpecPlugin'
            if ((Test-Path -LiteralPath $application) -and
                ($loaderPaths -contains (Get-ItemProperty -LiteralPath $application).LOADER)) {
                $application
            }
        }
    }
})
$hasBundle = Test-Path -LiteralPath $bundle
$hasRegistration = Test-Path -LiteralPath $uninstallKey
if (!$hasBundle -and !$hasRegistration -and !$autoloadKeys) {
    Write-Output 'SpecGen is already uninstalled.'
    return
}

if ($hasBundle) {
    $bundleItem = Get-Item -LiteralPath $bundle -Force
    if (!$bundleItem.PSIsContainer -or ($bundleItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Unexpected bundle directory: $bundle"
    }
}

$uninstaller = $null
if ($hasRegistration) {
    $registration = Get-ItemProperty -LiteralPath $uninstallKey
    if ($registration.InstallLocation -and $registration.InstallLocation.TrimEnd('\') -ne $bundle) {
        throw "SpecGen is registered in a different directory: $($registration.InstallLocation)"
    }
    $command = $registration.UninstallString
    if ($command -match '^"([^"]+)"$') {
        $command = $Matches[1]
    }
    if ($command -and ((Split-Path -Parent $command) -ne $bundle -or
        (Split-Path -Leaf $command) -notmatch '^unins[0-9]{3}\.exe$')) {
        throw "Unexpected SpecGen uninstaller path: $command"
    }
    if ($command -and (Test-Path -LiteralPath $command -PathType Leaf)) {
        $uninstaller = $command
    }
}

if (!$PSCmdlet.ShouldProcess($bundle, 'Uninstall SpecGen and remove its autoload registrations')) {
    return
}

$bundlePrefix = $bundle + '\'
$loadedBy = foreach ($process in Get-Process -Name acad, accoreconsole -ErrorAction SilentlyContinue) {
    foreach ($module in $process.Modules) {
        if ($module.FileName.StartsWith($bundlePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            $process.Id
            break
        }
    }
}
if ($loadedBy) {
    if (Test-Path -LiteralPath $manifest -PathType Leaf) {
        Move-Item -LiteralPath $manifest -Destination $disabledManifest -Force
        Write-Output 'SpecGen autoload has been disabled.'
    }
    throw "SpecGen is still loaded in AutoCAD (PID $($loadedBy -join ', ')). Close those sessions and rerun this script."
}

if ($uninstaller) {
    $process = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "The SpecGen uninstaller exited with code $($process.ExitCode)."
    }
}

if (Test-Path -LiteralPath $bundle) {
    $resolvedBundle = (Resolve-Path -LiteralPath $bundle).ProviderPath
    if ($resolvedBundle -ne $bundle -or
        (Get-ChildItem -LiteralPath $resolvedBundle -Recurse -Force -Attributes ReparsePoint | Select-Object -First 1)) {
        throw "Unexpected link in the SpecGen bundle: $bundle"
    }
    Remove-Item -LiteralPath $resolvedBundle -Recurse -Force
}
if (Test-Path -LiteralPath $uninstallKey) {
    Remove-Item -LiteralPath $uninstallKey -Force
}
foreach ($application in $autoloadKeys) {
    if (Test-Path -LiteralPath $application) {
        Remove-Item -LiteralPath $application -Recurse -Force
    }
}
if ((Test-Path -LiteralPath $bundle) -or (Test-Path -LiteralPath $uninstallKey)) {
    throw 'SpecGen uninstall verification failed.'
}
Write-Output 'SpecGen has been uninstalled.'
