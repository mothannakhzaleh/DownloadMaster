; DownloadMaster - Windows installer (Inno Setup 6.x).
;
; Per-user install: no administrator rights, installs to %LOCALAPPDATA%\Programs\DownloadMaster.
; Built by .github/workflows/release.yml from the published app folder:
;
;   ISCC.exe /DAppVersion=2.0.0 /DNumericVersion=2.0.0 /DPublishDir=C:\path\to\out\publish ^
;            /OC:\path\to\dist /FDownloadMaster-v2.0.0-win-x64-setup packaging\windows\DownloadMaster.iss
;
; Local build: run scripts\publish.bat first, then compile this script (defaults below use ship\win-x64
; and write the installer to ship\installer\).

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
; Windows file version: digits only (2.0.0-nightly.5 -> 2.0.0).
#ifndef NumericVersion
  #define NumericVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir AddBackslash(SourcePath) + "..\..\ship\win-x64"
#endif

#define AppName "DownloadMaster"
#define AppExeName "DownloadMaster.exe"
#define CliExeName "downloadmaster-cli.exe"
#define AppUrl "https://github.com/mothannakhzaleh/DownloadMaster"
; The command-line tool is optional: the PATH task only appears when it was published.
#define HaveCli FileExists(AddBackslash(PublishDir) + CliExeName)

[Setup]
; Never change AppId: it identifies the installation for upgrades and uninstall.
AppId={{64B15D6F-A9FE-44E8-B95E-DC749D7A19CD}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=mothannakh
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=Copyright (c) 2026 mothannakh
VersionInfoVersion={#NumericVersion}
VersionInfoProductVersion={#NumericVersion}
VersionInfoProductTextVersion={#AppVersion}
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\..\src\DownloadMaster\Assets\app.ico
OutputDir=..\..\ship\installer
OutputBaseFilename=DownloadMaster-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0
; "x64compatible" needs Inno Setup 6.3+; older compilers only know "x64".
#if VER < EncodeVer(6, 3, 0, 0)
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
; Close a running DownloadMaster (it lives in the tray) before files are replaced.
CloseApplications=yes
RestartApplications=no
; The PATH task edits the user environment; tell Explorer and new terminals about it.
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
#if HaveCli
Name: "addtopath"; Description: "Add the command-line tool (downloadmaster-cli) to PATH"; GroupDescription: "Command line:"; Flags: unchecked
#endif

[InstallDelete]
; An installed copy must never run in portable mode.
Type: files; Name: "{app}\DownloadMaster.portable"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "\DownloadMaster.portable";Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; The app's own "Start with Windows" setting writes this value; don't leave it behind after uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "DownloadMaster"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  UserEnvKey = 'Environment';

function SamePath(const A, B: string): Boolean;
begin
  Result := CompareText(RemoveBackslashUnlessRoot(Trim(A)), RemoveBackslashUnlessRoot(Trim(B))) = 0;
end;

{ True when Dir is one of the ';'-separated entries of Paths. }
function PathListContains(const Paths, Dir: string): Boolean;
var
  Rest, Item: string;
  P: Integer;
begin
  Result := False;
  Rest := Paths + ';';
  while Rest <> '' do
  begin
    P := Pos(';', Rest);
    Item := Copy(Rest, 1, P - 1);
    Delete(Rest, 1, P);
    if (Item <> '') and SamePath(Item, Dir) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

procedure AddToUserPath(const Dir: string);
var
  Paths: string;
begin
  if not RegQueryStringValue(HKCU, UserEnvKey, 'Path', Paths) then
    Paths := '';
  if PathListContains(Paths, Dir) then
    Exit;
  if (Paths <> '') and (Copy(Paths, Length(Paths), 1) <> ';') then
    Paths := Paths + ';';
  RegWriteExpandStringValue(HKCU, UserEnvKey, 'Path', Paths + Dir);
end;

procedure RemoveFromUserPath(const Dir: string);
var
  Paths, Rest, Item, Kept: string;
  P: Integer;
  Found: Boolean;
begin
  if not RegQueryStringValue(HKCU, UserEnvKey, 'Path', Paths) then
    Exit;
  Kept := '';
  Found := False;
  Rest := Paths + ';';
  while Rest <> '' do
  begin
    P := Pos(';', Rest);
    Item := Copy(Rest, 1, P - 1);
    Delete(Rest, 1, P);
    if Item <> '' then
    begin
      if SamePath(Item, Dir) then
        Found := True
      else
      begin
        if Kept <> '' then
          Kept := Kept + ';';
        Kept := Kept + Item;
      end;
    end;
  end;
  if Found then
    RegWriteExpandStringValue(HKCU, UserEnvKey, 'Path', Kept);
end;

#if HaveCli
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('addtopath') then
    AddToUserPath(ExpandConstant('{app}'));
end;
#endif

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  { Runs whether or not the task was chosen: removing an absent entry is a no-op. }
  if CurUninstallStep = usPostUninstall then
    RemoveFromUserPath(ExpandConstant('{app}'));
end;
