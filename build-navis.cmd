@echo off
setlocal enableextensions enabledelayedexpansion

rem === NavisAudit (Navisworks piping audit) builder ===
rem  Reads src\NavisAudit\VERSION, then:
rem  1) dotnet build for Navisworks 2024, 2025, 2026 (one DLL per release:
rem     the Navisworks API is versioned per release)
rem  2) dotnet test (the audit rules are shared with the Plant 3D plug-in)
rem  3) stage NavisAudit.bundle (DLLs + OpenXml + icons + stamped manifest)
rem  4) iscc -> dist_navis\NavisAudit-Setup.exe
rem  A release is built only where its Navisworks is installed; set
rem  NAVIS_YEARS=2025 2026 to choose.

cd /d "%~dp0"

set /p APPVERSION=<src\NavisAudit\VERSION
if "%APPVERSION%"=="" (
    echo src\NavisAudit\VERSION is missing.
    exit /b 1
)
if "%NAVIS_YEARS%"=="" set NAVIS_YEARS=2024 2025 2026
echo Building NavisAudit %APPVERSION% for Navisworks %NAVIS_YEARS%

set STAGE=bundle_navis_staged
rmdir /s /q %STAGE% 2>nul
mkdir %STAGE%\Contents

set BUILT=
for %%Y in (%NAVIS_YEARS%) do (
    if exist "C:\Program Files\Autodesk\Navisworks Manage %%Y\Autodesk.Navisworks.Api.dll" (
        set NAVISDIR=C:\Program Files\Autodesk\Navisworks Manage %%Y
    ) else (
        set NAVISDIR=C:\Program Files\Autodesk\Navisworks Simulate %%Y
    )
    if not exist "!NAVISDIR!\Autodesk.Navisworks.Api.dll" (
        echo Navisworks %%Y is not installed - skipped.
    ) else (
        echo.
        echo === dotnet build: Navisworks %%Y ===
        set OUT=%CD%\src\NavisAudit\bin\Release-%%Y-v%APPVERSION%
        rmdir /s /q "!OUT!" 2>nul
        dotnet build src\NavisAudit\NavisAudit.csproj -c Release -p:NavisYear=%%Y "-p:NavisDir=!NAVISDIR!" "-p:OutDir=!OUT!\\" -p:Version=%APPVERSION%
        if errorlevel 1 (
            echo dotnet build failed for Navisworks %%Y.
            exit /b 1
        )
        mkdir %STAGE%\Contents\%%Y\Images
        copy /y "!OUT!\NavisAudit.dll" %STAGE%\Contents\%%Y\ >nul
        copy /y "!OUT!\DocumentFormat.OpenXml.dll" %STAGE%\Contents\%%Y\ >nul
        copy /y "!OUT!\Images\*.png" %STAGE%\Contents\%%Y\Images\ >nul
        if not exist %STAGE%\Contents\%%Y\DocumentFormat.OpenXml.dll (
            echo DocumentFormat.OpenXml.dll missing for %%Y - the Word report would fail.
            exit /b 1
        )
        set BUILT=!BUILT! %%Y
    )
)
if "%BUILT%"=="" (
    echo No Navisworks release found to build against.
    exit /b 1
)

echo.
echo === dotnet test ===
dotnet test test\PipingSpecPlugin.Tests\PipingSpecPlugin.Tests.csproj
if errorlevel 1 (
    echo Tests failed.
    exit /b 1
)

powershell -NoProfile -Command ^
    "(Get-Content -Raw -Encoding UTF8 src\NavisAudit\bundle\PackageContents.xml) -replace '__APPVERSION__', '%APPVERSION%' | Set-Content -Encoding UTF8 %STAGE%\PackageContents.xml"
if errorlevel 1 (
    echo Failed to stamp PackageContents.xml.
    exit /b 1
)

echo.
echo === Inno Setup ===
set ISCC="C:\Program Files\Inno Setup 7\ISCC.exe"
if not exist %ISCC% set ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not exist %ISCC% (
    echo Inno Setup not found ^(checked Inno Setup 7 and 6^).
    exit /b 1
)
if exist dist_navis\NavisAudit-Setup.exe del /q dist_navis\NavisAudit-Setup.exe
%ISCC% /DMyAppVersion=%APPVERSION% installer-navis.iss
if errorlevel 1 (
    echo Inno Setup failed.
    exit /b 1
)

echo.
echo ============================================================
echo  Done. NavisAudit %APPVERSION% for Navisworks%BUILT%
echo  Installer: dist_navis\NavisAudit-Setup.exe
echo  Bundle:    %STAGE%\
echo ============================================================
endlocal
