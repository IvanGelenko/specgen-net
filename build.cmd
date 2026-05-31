@echo off
setlocal enableextensions enabledelayedexpansion

rem === PipingSpecPlugin (.NET) builder ===
rem  Reads version from VERSION file, then:
rem  1) dotnet build -c Release  -> bin\Release-vX.Y.Z\
rem  2) stage bundle (DLL + OpenXml + icons + version-stamped manifest)
rem  3) iscc -> dist_installer\PipingSpecPlugin-Setup.exe

cd /d "%~dp0"

if not exist VERSION (
    echo VERSION file is missing.
    exit /b 1
)
set /p APPVERSION=<VERSION
echo Building version %APPVERSION%

echo.
echo === [1/3] dotnet build ===
set BUILDOUT=src\PipingSpecPlugin\bin\Release-v%APPVERSION%
rmdir /s /q "%BUILDOUT%" 2>nul

rem Pass only -p:Version. AssemblyVersion / FileVersion are generated from
rem it by the csproj (GenerateVersionInfo target); passing them explicitly
rem here would duplicate the attributes and fail the build.
dotnet build src\PipingSpecPlugin\PipingSpecPlugin.csproj -c Release "-p:OutDir=%CD%\%BUILDOUT%\\" -p:Version=%APPVERSION%
if errorlevel 1 (
    echo dotnet build failed.
    exit /b 1
)

if not exist "%BUILDOUT%\PipingSpecPlugin.dll" (
    echo Build succeeded but PipingSpecPlugin.dll is missing from %BUILDOUT%.
    exit /b 1
)

echo.
echo === [2/3] Stage bundle ===
set STAGE=bundle_staged
rmdir /s /q %STAGE% 2>nul
mkdir %STAGE%\Contents\Windows
mkdir %STAGE%\Contents\Resources

rem Stamp AppVersion into a working copy of the manifest. The source
rem PackageContents.xml keeps the __APPVERSION__ placeholder so VERSION
rem remains the single source of truth for bumping releases.
powershell -NoProfile -Command ^
    "(Get-Content -Raw -Encoding UTF8 bundle\PackageContents.xml) -replace '__APPVERSION__', '%APPVERSION%' | Set-Content -Encoding UTF8 %STAGE%\PackageContents.xml"
if errorlevel 1 (
    echo Failed to stamp AppVersion into PackageContents.xml.
    exit /b 1
)

copy /y "%BUILDOUT%\PipingSpecPlugin.dll" %STAGE%\Contents\Windows\PipingSpecPlugin.dll >nul
if errorlevel 1 (
    echo Failed to stage PipingSpecPlugin.dll.
    exit /b 1
)
if exist "%BUILDOUT%\DocumentFormat.OpenXml.dll" (
    copy /y "%BUILDOUT%\DocumentFormat.OpenXml.dll" %STAGE%\Contents\Windows\DocumentFormat.OpenXml.dll >nul
) else (
    echo WARNING: DocumentFormat.OpenXml.dll missing from build output.
    echo The installer will be built without it; WRITESPEC will fail at runtime.
)
if exist src\PipingSpecPlugin\Resources\icon16.png (
    copy /y src\PipingSpecPlugin\Resources\icon16.png %STAGE%\Contents\Resources\icon16.png >nul
)
if exist src\PipingSpecPlugin\Resources\icon32.png (
    copy /y src\PipingSpecPlugin\Resources\icon32.png %STAGE%\Contents\Resources\icon32.png >nul
)

echo.
echo === [3/3] Inno Setup ===
set ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not exist %ISCC% (
    echo Inno Setup 6 not found at %ISCC%
    echo Install it from https://jrsoftware.org/isinfo.php
    exit /b 1
)
rmdir /s /q dist_installer 2>nul
%ISCC% /DMyAppVersion=%APPVERSION% installer.iss
if errorlevel 1 (
    echo Inno Setup failed.
    exit /b 1
)

echo.
echo ============================================================
echo  Done. Version %APPVERSION%
echo  Installer: dist_installer\PipingSpecPlugin-Setup.exe
echo  Bundle:    %STAGE%\
echo ============================================================
endlocal
