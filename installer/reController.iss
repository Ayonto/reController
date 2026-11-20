; Inno Setup 6 script for reController.
; Build:  iscc /DAppVersion=1.2.3 installer\reController.iss   (after `dotnet publish -p:PublishProfile=win-x64`)

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "reController"
#define AppExe "reController.exe"

[Setup]
AppId={{8C6F1E2A-4B7D-4F3A-9E21-5A0D3C7B9F64}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=reController contributors
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=Output
OutputBaseFilename=reController-Setup-{#AppVersion}
SetupIconFile=..\src\ReController.App\Assets\reController.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\win-x64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Remove the "Start with Windows" entry (created by the app itself) on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "reController"; ValueType: none; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "StopReController"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorCode: Integer;
begin
  if (CurStep = ssPostInstall) and (not WizardSilent) then
  begin
    if not RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ViGEmBus') then
    begin
      if MsgBox('reController needs the free ViGEmBus driver to create a virtual controller.' + #13#10#13#10 +
                'Open the ViGEmBus download page now?', mbConfirmation, MB_YESNO) = IDYES then
        ShellExec('open', 'https://github.com/nefarius/ViGEmBus/releases/latest', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
    if not RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\HidHide') then
    begin
      if MsgBox('Recommended: HidHide hides your physical controller from games so they do not receive input twice.' + #13#10#13#10 +
                'Open the HidHide download page now?', mbConfirmation, MB_YESNO) = IDYES then
        ShellExec('open', 'https://github.com/nefarius/HidHide/releases/latest', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
  end;
end;
