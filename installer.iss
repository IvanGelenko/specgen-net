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
#define SourceStage       "bundle_staged"

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
; Optional resource icons (already embedded in the DLL, but keep on
; disk for future per-locale customisation).
Source: "{#SourceStage}\Contents\Resources\icon16.png";             DestDir: "{app}\Contents\Resources"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourceStage}\Contents\Resources\icon32.png";             DestDir: "{app}\Contents\Resources"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
; No Start Menu shortcut — this is a Plant 3D plug-in, not a stand-alone app.

[Code]
function InitializeUninstall(): Boolean;
begin
  Result := True;
  // Leave the user's spec output folder (%APPDATA%\PipingSpecPlugin)
  // alone on uninstall — that's where settings.json / last-run.log live
  // and the user may want to keep them.
end;
