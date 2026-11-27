; Standalone launcher for the SpecGen cleanup script.
[Setup]
AppName=Удаление SpecGen
AppVersion={#MyAppVersion}
CreateAppDir=no
Uninstallable=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableStartupPrompt=yes
DisableWelcomePage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
CloseApplications=no
RestartApplications=no
OutputDir=dist_installer
OutputBaseFilename=SpecGen-Uninstall

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "tools\Uninstall-SpecGen.ps1"; Flags: dontcopy

[Code]
function PreviewRequested(): Boolean;
var I: Integer;
begin
  Result := True;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/WHATIF') = 0 then Exit;
  Result := False;
end;

function InitializeSetup(): Boolean;
begin
  Result := PreviewRequested();
  if not Result then
    Result := SuppressibleMsgBox(
      'Удалить SpecGen и его записи автозагрузки из текущего профиля Windows?',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDYES) = IDYES;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExitCode, I: Integer;
  Output: TExecOutput;
  Arguments, Details, MessageText: String;
  Preview: Boolean;
begin
  if CurStep <> ssInstall then Exit;

  ExtractTemporaryFile('Uninstall-SpecGen.ps1');
  Preview := PreviewRequested();
  Arguments := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{tmp}\Uninstall-SpecGen.ps1') + '"';
  if Preview then Arguments := Arguments + ' -WhatIf';
  if not ExecAndCaptureOutput(
    ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Arguments, '', SW_HIDE, ewWaitUntilTerminated, ExitCode, Output) then
    RaiseException('Не удалось запустить удаление SpecGen. ' + SysErrorMessage(ExitCode));

  MessageText := 'SpecGen удалён.';
  for I := 0 to GetArrayLength(Output.StdOut)-1 do
  begin
    Log(Output.StdOut[I]);
    if Output.StdOut[I] = 'SpecGen is already uninstalled.' then
      MessageText := 'SpecGen уже удалён.';
  end;

  if ExitCode <> 0 then
  begin
    Details := '';
    for I := 0 to GetArrayLength(Output.StdErr)-1 do
      Details := Details + #13#10 + Output.StdErr[I];
    RaiseException('Не удалось завершить удаление SpecGen.' + Details);
  end;

  if Preview then MessageText := 'Проверка завершена. Удаление не запускалось.';
  SuppressibleMsgBox(MessageText, mbInformation, MB_OK, IDOK);
end;
