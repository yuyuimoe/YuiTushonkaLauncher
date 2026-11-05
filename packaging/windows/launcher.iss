; YuiTushonkaLauncher installer (Inno Setup 6).
;
; Per-user install: no admin, no UAC, and the app lives under %LOCALAPPDATA%. That matches what the launcher does — it
; reads a folder the player already has (the SPT install) instead of owning a program directory.
;
; Built by scripts/publish.sh (or the release workflow) with:
;   ISCC.exe /DMyAppVersion=1.2.3 /DMySourceDir=dist\win-x64-fdd /DMyOutputDir=dist packaging\windows\launcher.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#ifndef MySourceDir
  #define MySourceDir "..\..\dist\win-x64-fdd"
#endif
#ifndef MyOutputDir
  #define MyOutputDir "..\..\dist"
#endif

#define MyAppName "YuiTushonkaLauncher"
#define MyAppPublisher "SP-Tushonka"
#define MyAppExeName "YuiTushonkaLauncher.exe"
#define MyAppId "{{7C1F2E42-8E1B-4B5E-9E77-2E7A5A0C3B11}"
#define MyProgId "YuiTushonkaLauncher.sptpack"
#define MyMimeName "application/x-sptpack"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#MyOutputDir}
OutputBaseFilename={#MyAppName}-{#MyAppVersion}-win-x64-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
; The launcher keeps a database and logs under %LOCALAPPDATA%; the uninstaller leaves them alone on purpose.
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "associate"; Description: "Open .sptpack files with {#MyAppName}"; GroupDescription: "File association:"

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; A per-user association: HKCU, no admin, and it disappears with the user's profile if the launcher is never used again.
Root: HKCU; Subkey: "Software\Classes\{#MyMimeName}"; ValueType: string; ValueName: ""; ValueData: "SPT mod pack"; Flags: uninsdeletekey; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\{#MyMimeName}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\{#MyMimeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\.sptpack"; ValueType: string; ValueName: ""; ValueData: "{#MyMimeName}"; Flags: uninsdeletevalue; Tasks: associate

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
{ The SPT install is the player's own folder and the launcher asks for it on first run, so the installer never touches
  it. It only warns when .NET 10 is missing, because SPT itself needs it and the launcher will not start without it. }
function InitializeSetup(): Boolean;
var
  DotnetFound: Boolean;
begin
  DotnetFound := FileExists(ExpandConstant('{commonpf}\dotnet\dotnet.exe'))
    or FileExists(ExpandConstant('{localappdata}\Microsoft\dotnet\dotnet.exe'));
  Result := True;
  if not DotnetFound then
  begin
    if MsgBox('The .NET 10 desktop runtime was not found. SPT requires it too, so the launcher will not start until it is installed.'
      + #13#10 + #13#10 + 'Continue anyway?', mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
