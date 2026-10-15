; Inno Setup installer for NavisAudit — the piping audit for Navisworks
; Manage / Simulate 2024, 2025 and 2026. Drops the bundle into the per-user
; Autodesk\ApplicationPlugins folder (no admin rights); Navisworks loads it
; on start through PackageContents.xml. Built by build-navis.cmd.

#define MyAppDisplayName  "Аудит трубопроводов для Navisworks"
#ifndef MyAppVersion
  #error MyAppVersion not defined. Run build via build-navis.cmd, not iscc directly.
#endif
#define MyAppPublisher    "In-house"
#define BundleFolder      "NavisAudit.bundle"
#define SourceStage       "bundle_navis_staged"

[Setup]
AppId={{124FBC25-EEB7-43F6-84EF-03AE37631853}
AppName={#MyAppDisplayName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={userappdata}\Autodesk\ApplicationPlugins\{#BundleFolder}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=NavisAudit-Setup
; Not dist_installer: build.cmd (Plant 3D) empties that folder.
OutputDir=dist_navis
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#MyAppDisplayName}
UsePreviousAppDir=no
; Navisworks holds the DLL while it runs.
CloseApplications=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "{#SourceStage}\PackageContents.xml"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceStage}\Contents\*"; DestDir: "{app}\Contents"; Flags: ignoreversion recursesubdirs createallsubdirs

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
  // Settings (%APPDATA%\NavisAudit\settings.ini) are kept on uninstall.
end;
