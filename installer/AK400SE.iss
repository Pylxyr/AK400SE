; Inno Setup 6 script for AK400SE.
; Build (from the repo root, after "dotnet publish" into artifacts\publish and scripts\get-pawnio.ps1):
;   set DCL_VERSION=1.2.3
;   ISCC.exe installer\AK400SE.iss
; The version comes from the DCL_VERSION environment variable (the csproj <Version>, see the CI workflows).

#define AppName "AK400SE"
#define AppExe "AK400SE.exe"
#define AppPublisher "Pylxyr"
#define AppUrl "https://github.com/Pylxyr/AK400SE"
; The installer ID. It never changes, so every version (and the old "DeepCool Lite" 0.1.x) is recognised as the same product.
#define AppGuid "99F4FFF9-D367-4302-8080-91AFCA0D9FEE"

; The official PawnIO installer that is bundled, unmodified. scripts\get-pawnio.ps1 downloads exactly this release from
; github.com/namazso/PawnIO.Setup and refuses to continue if its SHA-256 differs. Setup checks it again before running it.
#define PawnIoVersion "2.2.0"
#define PawnIoSha256 "1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032"

#define AppVersion GetEnv("DCL_VERSION")
#if AppVersion == ""
  #error Set the DCL_VERSION environment variable first, for example 0.2.0
#endif
; "0.2.0-rc1" -> "0.2.0" for the numeric file version
#define DashPos Pos("-", AppVersion)
#if DashPos > 0
  #define NumericVersion Copy(AppVersion, 1, DashPos - 1)
#else
  #define NumericVersion AppVersion
#endif

[Setup]
AppId={{{#AppGuid}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
DefaultDirName={autopf}\{#AppName}
; Fixed location: the start-up task runs the app with administrator rights, so it must live in Program Files, where
; normal users cannot replace it.
DisableDirPage=yes
; Versions up to 0.1.x were installed as "DeepCool Lite" (same AppId, so Windows sees an upgrade). Always use the new folder;
; [InstallDelete] below removes the old one.
UsePreviousAppDir=no
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
LicenseFile=..\LICENSE
SetupIconFile=..\src\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=AK400SE-setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
CloseApplications=no
; Makes Setup tell Explorer to refresh its icons at the end, so an upgraded icon is not hidden by the shell's icon cache.
ChangesAssociations=yes
VersionInfoVersion={#NumericVersion}.0
VersionInfoProductVersion={#NumericVersion}.0
VersionInfoProductTextVersion={#AppVersion}
VersionInfoDescription={#AppName} setup
VersionInfoCopyright=Copyright (c) 2026 {#AppPublisher}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%n{#AppName} is free software and comes with no warranty: you use it at your own risk. The MIT License on the next page has the details.%n%nClick Next to continue.

[Tasks]
Name: "startup"; Description: "Start {#AppName} when I sign in to Windows"; GroupDescription: "Start-up:"

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
; Only unpacked when the user agrees on the PawnIO page (see [Code]).
Source: "..\artifacts\pawnio\PawnIO_setup.exe"; DestDir: "{tmp}"; Flags: dontcopy

[InstallDelete]
; One-time clean-up of the old product name (installs of 0.1.x): its folder (with the old exe and uninstaller) and Start menu shortcut.
Type: filesandordirs; Name: "{autopf}\DeepCool Lite"
Type: files; Name: "{autoprograms}\DeepCool Lite.lnk"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; Removes the start-up task. PawnIO is deliberately left installed: other programs may use it.
Filename: "{app}\{#AppExe}"; Parameters: "--task remove"; Flags: runhidden; RunOnceId: "RemoveStartupTask"

[Code]
const
  PawnIoSha256 = '{#PawnIoSha256}';
  PawnIoUninstallKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO';

var
  PawnPage: TInputOptionWizardPage;
  PawnIoRestart: Boolean;
  PawnIoNote: String;

function PawnIoInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM64, PawnIoUninstallKey) or FileExists(ExpandConstant('{commonpf64}\PawnIO\PawnIOLib.dll'));
end;

procedure InitializeWizard();
begin
  PawnPage := CreateInputOptionPage(wpLicense,
    'PawnIO driver', 'Needed to show the CPU temperature',
    'To read the CPU temperature, {#AppName} uses PawnIO, a separate driver made by namazso (not by this project). ' +
    'This setup contains the unmodified official PawnIO {#PawnIoVersion} installer and checks its SHA-256 fingerprint before it runs it. ' +
    'It is installed in its normal, signed edition. Uninstalling {#AppName} leaves it in place, because other programs may use it.' + #13#10 + #13#10 +
    'Without it the cooler can still show CPU usage, but not the temperature.',
    False, False);
  PawnPage.Add('I agree: install the PawnIO driver now');
  PawnPage.Values[0] := False;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  // nothing to ask when PawnIO is already on this PC
  Result := (PawnPage <> nil) and (PageID = PawnPage.ID) and PawnIoInstalled();
end;

procedure UninstallPrevious();
var
  Cmd: String;
  Code: Integer;
begin
  // A previous version (any version, including the 0.1.x "DeepCool Lite") is uninstalled first, so no old files, shortcuts or tasks stay behind.
  // Its settings (in the user's profile) and PawnIO are left alone.
  Cmd := '';
  if not RegQueryStringValue(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{' + '{#AppGuid}' + '}_is1', 'UninstallString', Cmd) then
    Exit;
  Cmd := RemoveQuotes(Cmd);
  if (Cmd <> '') and FileExists(Cmd) then
    Exec(Cmd, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

procedure RemoveOldName();
var
  Code: Integer;
begin
  // 0.1.x ran as the task "DeepCoolLite" from deepcool-lite.exe. End and delete the task first so it cannot restart the old exe.
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/End /TN DeepCoolLite', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN DeepCoolLite /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM deepcool-lite.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

procedure StopApp();
var
  Code: Integer;
begin
  // end the task first so Task Scheduler does not restart the app, then make sure no copy is left running
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/End /TN AK400SE', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  RemoveOldName();
  StopApp();
  UninstallPrevious();
  Result := '';
end;

procedure InstallPawnIo();
var
  Setup, Actual: String;
  Code: Integer;
begin
  ExtractTemporaryFile('PawnIO_setup.exe');
  Setup := ExpandConstant('{tmp}\PawnIO_setup.exe');
  try
    Actual := GetSHA256OfFile(Setup);
  except
    Actual := '';
  end;
  if CompareText(Actual, PawnIoSha256) <> 0 then
  begin
    PawnIoNote := 'The PawnIO installer inside this setup did not match its expected SHA-256 fingerprint, so it was NOT run. ' +
                  'The CPU temperature will not be available. Please download {#AppName} again.';
    Exit;
  end;
  // Only these switches. Never "-unrestricted": that edition is a test-signed driver that needs changes to Windows boot settings.
  if not Exec(Setup, '-install -silent', '', SW_HIDE, ewWaitUntilTerminated, Code) then
    PawnIoNote := 'The PawnIO installer could not be started. The CPU temperature will not be available.'
  else if Code = 3010 then
    PawnIoRestart := True            // ERROR_SUCCESS_REBOOT_REQUIRED
  else if Code <> 0 then
    PawnIoNote := 'The PawnIO installer reported error ' + IntToStr(Code) + '. The CPU temperature will not be available.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
  Switch: String;
begin
  if CurStep <> ssPostInstall then Exit;

  if (PawnPage <> nil) and PawnPage.Values[0] and (not PawnIoInstalled()) then
    InstallPawnIo();

  // One-time elevated step: the start-up task that lets the app run with administrator rights without a prompt.
  if WizardIsTaskSelected('startup') then Switch := 'on' else Switch := 'off';
  if (not Exec(ExpandConstant('{app}\{#AppExe}'), '--task ' + Switch, '', SW_HIDE, ewWaitUntilTerminated, Code)) or (Code <> 0) then
    SuppressibleMsgBox('Setup could not create the start-up task, so {#AppName} will ask for permission each time it starts.',
      mbInformation, MB_OK, IDOK);

  if PawnIoNote <> '' then
    SuppressibleMsgBox(PawnIoNote, mbInformation, MB_OK, IDOK);
end;

function NeedRestart(): Boolean;
begin
  Result := PawnIoRestart;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    StopApp()
  else if CurUninstallStep = usDone then
    SuppressibleMsgBox('The PawnIO driver was left installed, because other programs may use it. ' +
      'To remove it, uninstall PawnIO under Settings > Apps.', mbInformation, MB_OK, IDOK);
end;
