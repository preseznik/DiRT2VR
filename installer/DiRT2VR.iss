#ifndef Stage
  #error Stage must point to the staged package
#endif
#ifndef PackageVersion
  #define PackageVersion "0.1.0-alpha.2"
#endif
[Setup]
AppId={{7BB25362-6E23-44BD-AE38-E64B2EC717D7}
AppName=DiRT2VR
AppVersion={#PackageVersion}
AppPublisher=Bohloney
AppPublisherURL=https://github.com/preseznik/DiRT2VR
DefaultDirName={code:DefaultGameDir}
AppendDefaultDirName=no
DisableProgramGroupPage=yes
DirExistsWarning=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallFilesDir={app}\DiRT2VR\uninstall
OutputDir={#Stage}\..
OutputBaseFilename=DiRT2VR-{#PackageVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
AppMutex=Global\DiRT2VR.Session
SetupLogging=yes
SetupIconFile=..\launcher\assets\DiRT2VR.ico
UninstallDisplayIcon={app}\DiRT2VR.exe

[Files]
Source: "{#Stage}\DiRT2VR.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\Start-DiRT2VR.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\DiRT2VR\*"; DestDir: "{app}\DiRT2VR"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Icons]
Name: "{autoprograms}\DiRT2VR"; Filename: "{app}\DiRT2VR.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\DiRT2VR"; Filename: "{app}\DiRT2VR.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\DiRT2VR.exe"; Description: "Open DiRT2VR"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
var
  SuggestedGame: String;

function DefaultGameDir(Param: String): String;
begin
  if SuggestedGame <> '' then Result := SuggestedGame
  else Result := ExpandConstant('{commonpf32}\Steam\steamapps\common\Dirt 2');
end;

procedure InitializeWizard();
var
  ResultCode: Integer;
  I: Integer;
  ExplicitDirectory: Boolean;
  Discovered: AnsiString;
begin
  ExplicitDirectory := False;
  for I := 1 to ParamCount do
    if CompareText(Copy(ParamStr(I), 1, 5), '/DIR=') = 0 then ExplicitDirectory := True;
  WizardForm.SelectDirLabel.Caption := 'Select the existing DiRT 2 folder containing dirt2.exe and dirt2_game.exe. No game files are included.';
  ExtractTemporaryFile('DiRT2VR.exe');
  if Exec(ExpandConstant('{tmp}\DiRT2VR.exe'), '--discover "' + ExpandConstant('{tmp}\game-path.txt') + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
    if LoadStringFromFile(ExpandConstant('{tmp}\game-path.txt'), Discovered) then
    begin
      SuggestedGame := UTF8Decode(Discovered);
      if not ExplicitDirectory and not FileExists(WizardDirValue() + '\dirt2_game.exe') then
        WizardForm.DirEdit.Text := SuggestedGame;
    end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if CheckForMutexes('Global\DiRT2VR.Session') then
  begin
    Result := 'Close the DiRT2VR session before installing.';
    exit;
  end;
  if not Exec(ExpandConstant('{tmp}\DiRT2VR.exe'), '--check-install --quiet --game "' + WizardDirValue() + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
  begin
    Result := 'Game-folder validation failed. No package files were installed.';
    exit;
  end;
  if not Exec(ExpandConstant('{tmp}\DiRT2VR.exe'), '--recover --quiet --game "' + WizardDirValue() + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    Result := 'Restore original files with the existing launcher before upgrading.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
    if not Exec(ExpandConstant('{app}\DiRT2VR.exe'), '--setup --quiet', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
      RaiseException('The package was copied but proxy setup failed. Open DiRT2VR to resolve the reported problem; the original game files were not patched by setup.');
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  if CheckForMutexes('Global\DiRT2VR.Session') then
  begin
    MsgBox('Close DiRT 2 and let DiRT2VR restore its files before uninstalling.', mbError, MB_OK);
    exit;
  end;
  if not Exec(ExpandConstant('{app}\DiRT2VR.exe'), '--remove-proxy --quiet', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
  begin
    MsgBox('Recovery or proxy removal failed. Backups and installation files have been preserved. Resolve recovery under the Windows account that played VR, then retry.', mbError, MB_OK);
    exit;
  end;
  Result := True;
end;
