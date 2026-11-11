; Inno Setup installer for the PipingSpecPlugin (.NET) — Plant 3D 2025 and 2026.
; Builds a single Setup.exe that drops the plug-in into the per-user
; Autodesk\ApplicationPlugins folder. No admin rights required, and the
; plug-in auto-loads on every AutoCAD start through the bundle's
; PackageContents.xml — no NETLOAD needed.

#define MyAppName         "PipingSpecPlugin"
#define MyAppDisplayName  "Генератор спецификации Plant 3D"
; MyAppVersion injected by build.cmd: `iscc /DMyAppVersion=X.Y.Z`
#ifndef MyAppVersion
  #error MyAppVersion not defined. Run build via build.cmd, not iscc directly.
#endif
#define MyAppPublisher    "In-house"
#define BundleFolder      "PipingSpecPlugin.bundle"
#ifndef SourceStage
#define SourceStage       "bundle_staged"
#endif

[Setup]
AppId={{B5E20C42-5C4D-4F1B-9B7C-1D5E2B0A8C05}
AppName={#MyAppDisplayName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={userappdata}\Autodesk\ApplicationPlugins\{#BundleFolder}
DefaultGroupName={#MyAppDisplayName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=PipingSpecPlugin-Setup
OutputDir=dist_installer
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
UninstallDisplayName={#MyAppDisplayName}
; Always the fixed per-user bundle folder (the dir page is disabled too),
; never a directory remembered from an older install. NOTE: this does NOT
; wipe the folder — an update overwrites the files listed in [Files] (same
; AppId, so it replaces the previous install). There is deliberately no
; [InstallDelete] for Contents\: if AutoCAD still holds the DLL, a wipe could
; delete DocumentFormat.OpenXml.dll and then fail on the locked plug-in DLL,
; leaving a bundle that loads but cannot write Word files.
UsePreviousAppDir=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
; Bundle manifest (version stamped by build.cmd) sits at the bundle root,
; everything else under Contents\Windows.
Source: "{#SourceStage}\PackageContents.xml";                       DestDir: "{app}";                    Flags: ignoreversion
Source: "{#SourceStage}\Contents\Windows\PipingSpecPlugin.dll";     DestDir: "{app}\Contents\Windows";   Flags: ignoreversion
; DocumentFormat.OpenXml is a NuGet runtime dependency the build copies
; alongside our DLL. Without it the plug-in fails to load AutoCAD-side
; with a FileNotFoundException on first WRITESPEC.
Source: "{#SourceStage}\Contents\Windows\DocumentFormat.OpenXml.dll"; DestDir: "{app}\Contents\Windows"; Flags: ignoreversion
; The isometric module is a mandatory part of the SpecGen installation.
Source: "{#SourceStage}\Contents\Windows\IsoGenerator.dll"; DestDir: "{app}\Contents\Windows"; Flags: ignoreversion
Source: "{#SourceStage}\Contents\Windows\IsoGenerator.Core.dll"; DestDir: "{app}\Contents\Windows"; Flags: ignoreversion
Source: "{#SourceStage}\Contents\Windows\iso_template.dwg"; DestDir: "{app}\Contents\Windows"; Flags: ignoreversion
Source: "{#SourceStage}\Contents\Windows\assets\IsoSymbolStyles.dwg"; DestDir: "{app}\Contents\Windows\assets"; Flags: ignoreversion
Source: "{#SourceStage}\Contents\Windows\assets\IsoSkeyAcadBlockMap.xml"; DestDir: "{app}\Contents\Windows\assets"; Flags: ignoreversion
Source: "{#SourceStage}\Contents\Windows\IsoGenerator.Publishing.dll"; DestDir: "{app}\Contents\Windows"; Flags: ignoreversion
Source: "{#SourceStage}\Contents\Windows\PdfSharp*.dll"; DestDir: "{app}\Contents\Windows"; Flags: ignoreversion
Source: "{#SourceStage}\Contents\Windows\Microsoft.Extensions.DependencyInjection.Abstractions.dll"; DestDir: "{app}\Contents\Windows"; Flags: ignoreversion
Source: "{#SourceStage}\Contents\Windows\Microsoft.Extensions.Logging.Abstractions.dll"; DestDir: "{app}\Contents\Windows"; Flags: ignoreversion
Source: "{#SourceStage}\Contents\Windows\System.Security.Cryptography.Pkcs.dll"; DestDir: "{app}\Contents\Windows"; Flags: ignoreversion
Source: "{#SourceStage}\Contents\Windows\assets\PDFsharp-LICENSE.txt"; DestDir: "{app}\Contents\Windows\assets"; Flags: ignoreversion
; Optional resource icons (already embedded in the DLL, but keep on
; disk for future per-locale customisation).
Source: "{#SourceStage}\Contents\Resources\icon16.png";             DestDir: "{app}\Contents\Resources"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourceStage}\Contents\Resources\icon32.png";             DestDir: "{app}\Contents\Resources"; Flags: ignoreversion skipifsourcedoesntexist

Source: "{#SourceStage}\Contents\Windows\dependency-manifest.txt"; DestDir: "{app}\Contents\Windows"; Flags: ignoreversion

[Icons]
; No Start Menu shortcut — this is a Plant 3D plug-in, not a stand-alone app.

[Code]
var MissingHostFiles: String;
#include SourceStage + "\Contents\Windows\host-dependencies.iss"
function InitializeUninstall(): Boolean;
begin
  Result := True;
  // Leave the user's spec output folder (%APPDATA%\PipingSpecPlugin)
  // alone on uninstall — that's where settings.json / last-run.log live
  // and the user may want to keep them.
end;



function HasRuntime8(const Framework, RequiredFile: String): Boolean;
var Found: TFindRec; Root, InstallRoot: String;
begin
  Result := False;
  if not RegQueryStringValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64', 'InstallLocation', InstallRoot) then InstallRoot := ExpandConstant('{commonpf64}\dotnet');
  Root := AddBackslash(InstallRoot) + 'shared\' + Framework + '\';
  if FindFirst(Root + '8.*', Found) then
  begin
    try
      repeat
        if (Found.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0) and
           FileExists(Root + Found.Name + '\' + RequiredFile) then
        begin Result := True; Break; end;
      until not FindNext(Found);
    finally FindClose(Found); end;
  end;
end;

function HasPlantVersion(const Series: String): Boolean;
var Keys: TArrayOfString; I: Integer; Root, Location, Product: String;
begin
  Result := False;
  Root := 'SOFTWARE\Autodesk\AutoCAD\' + Series;
  if RegGetSubkeyNames(HKLM64, Root, Keys) then
    for I := 0 to GetArrayLength(Keys)-1 do
    begin
      if RegQueryStringValue(HKLM64, Root + '\' + Keys[I], 'AcadLocation', Location) and
         RegQueryStringValue(HKLM64, Root + '\' + Keys[I], 'ProductName', Product) then
        if (Pos('plant 3d', Lowercase(Product)) > 0) and
           HostFilesPresent(Location) then
        begin Result := True; Exit; end;
    end;
end;

function CheckPrerequisites(): String;
begin
  Result := '';
  if not (HasPlantVersion('R25.0') or HasPlantVersion('R25.1')) then
    Result := Result + 'Не найдена полная установка AutoCAD Plant 3D 2025 или 2026 с Core Console. Восстановите установку Plant 3D. Отсутствующие файлы: ' + MissingHostFiles + #13#10;
  if not (HasRuntime8('Microsoft.NETCore.App', 'System.Private.CoreLib.dll') and
          HasRuntime8('Microsoft.WindowsDesktop.App', 'System.Windows.Forms.dll')) then
    Result := Result + 'Необходим Microsoft .NET 8 Desktop Runtime x64. Установите его с сайта Microsoft.' + #13#10;
  if not (FileExists(ExpandConstant('{fonts}\GOST_B.TTF')) or
          FileExists(ExpandConstant('{localappdata}\Microsoft\Windows\Fonts\GOST_B.TTF'))) then
    Result := Result + 'Не найден шрифт GOST type B (GOST_B.TTF), необходимый для оформления чертежей. Установите шрифт перед установкой SpecGen.' + #13#10;
end;

function InitializeSetup(): Boolean;
var Missing: String;
begin
  Missing := CheckPrerequisites();
  Result := Missing = '';
  if not Result then MsgBox('Установка SpecGen не может быть продолжена:' + #13#10#13#10 + Missing, mbError, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := CheckPrerequisites();
end;

procedure VerifyPayload(const Root: String);
var Manifest: TArrayOfString; I, Separator: Integer; Relative, Expected, Target: String;
begin
  if not LoadStringsFromFile(AddBackslash(Root) + 'dependency-manifest.txt', Manifest) then
    RaiseException('Не удалось прочитать список зависимостей SpecGen. Повторите установку.');
  for I := 0 to GetArrayLength(Manifest)-1 do
  begin
    Separator := Pos('|', Manifest[I]);
    if Separator > 1 then
    begin
      Relative := Copy(Manifest[I], 1, Separator-1);
      Expected := Copy(Manifest[I], Separator+1, MaxInt);
      Target := AddBackslash(Root) + Relative;
      if not FileExists(Target) then RaiseException('Не установлен обязательный файл: ' + Relative);
      if CompareText(GetSHA256OfFile(Target), Expected) <> 0 then
        RaiseException('Проверка установленного файла не пройдена: ' + Relative + '. Повторите установку с закрытым Plant 3D.');
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then VerifyPayload(ExpandConstant('{app}\Contents\Windows'));
end;
