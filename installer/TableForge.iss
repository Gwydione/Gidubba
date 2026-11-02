; Gidubba (formerly TableForge) per-user installer (Inno Setup 6).
;
; Build it with:   .\publish.ps1 -Installer      (publishes first, then compiles this script)
; or by hand:      ISCC.exe /DAppVersion=1.5.0 installer\TableForge.iss   (after publish.ps1)
;
; Contract:
;   - Installs the self-contained publish\win-x64 folder to %LOCALAPPDATA%\Programs\TableForge, for the current Windows
;     user only, with no administrator prompt. No .NET install is needed; the WebView2 Runtime is NOT a prerequisite
;     (Gidubba starts without it; only dddice dice need it).
;   - AppId below never changes: a newer installer recognises an installed TableForge or Gidubba and replaces it in place.
;     1.2.0 renamed the product only: the install folder, TableForge.exe and the data folder keep their names.
;   - The data lives in %LOCALAPPDATA%\TableForge (database, settings, dddice account, WebView2 profile, migration backups).
;     This script never installs anything there, and neither upgrading nor uninstalling touches it: uninstall removes only
;     what was installed below (program files, Start Menu shortcut, uninstaller).
;   - The one thing an upgrade deletes is the Start Menu shortcut that 1.1.0 and earlier created as "TableForge", and only
;     when it still points at this installation's TableForge.exe ([InstallDelete] and IsOldTableForgeShortcut below).

#ifndef AppVersion
  #error Pass the version: ISCC.exe /DAppVersion=<TableForge.csproj Version> installer\TableForge.iss
#endif

[Setup]
AppId={{89A2A71F-CF3F-4C91-BDCB-125354F776EB}
AppName=Gidubba
AppVersion={#AppVersion}
AppVerName=Gidubba {#AppVersion}
AppPublisher=RPG Frequencies
AppPublisherURL=https://github.com/Gwydione/Gidubba
AppSupportURL=https://github.com/Gwydione/Gidubba/issues
AppUpdatesURL=https://github.com/Gwydione/Gidubba/releases
AppCopyright=Copyright (c) 2026 RPG Frequencies
VersionInfoCompany=RPG Frequencies
VersionInfoCopyright=Copyright (c) 2026 RPG Frequencies
VersionInfoProductName=Gidubba
SetupIconFile=TableForge\Assets\Gidubba.ico
VersionInfoVersion=1.5.0
VersionInfoProductTextVersion={#AppVersion}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
DefaultDirName={localappdata}\Programs\TableForge
DisableDirPage=yes
DefaultGroupName=Gidubba
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayName=Gidubba
UninstallDisplayIcon={app}\TableForge.exe
CloseApplications=yes
RestartApplications=no
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
SourceDir=..
OutputDir=publish\release
OutputBaseFilename=Gidubba-{#AppVersion}-Setup

[InstallDelete]
; The pre-1.2.0 shortcut, now replaced by the Gidubba one below. A single file, deleted only by IsOldTableForgeShortcut's
; check; never a folder, a wildcard or anything in the data folder.
Type: files; Name: "{autoprograms}\TableForge.lnk"; Check: IsOldTableForgeShortcut

[Files]
; The published app, exactly as publish.ps1 produced and verified it (never debug symbols). It already contains
; LICENSE.txt, THIRD-PARTY-NOTICES.txt and PRIVACY.txt beside TableForge.exe.
Source: "publish\win-x64\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Gidubba"; Filename: "{app}\TableForge.exe"; IconFilename: "{app}\TableForge.exe"

[Run]
Filename: "{app}\TableForge.exe"; Description: "Start Gidubba"; Flags: nowait postinstall skipifsilent

; Deliberately no [UninstallDelete]: nothing outside the installed files is ever removed.

[Code]
// True only for the shortcut an earlier installer made: "TableForge.lnk" in this user's Start Menu whose target is this
// installation's TableForge.exe. A shortcut that points anywhere else, or that cannot be read, is left alone.
function IsOldTableForgeShortcut: Boolean;
var
  Shell, Link: Variant;
  ShortcutPath: String;
begin
  Result := False;
  ShortcutPath := ExpandConstant('{autoprograms}\TableForge.lnk');
  if not FileExists(ShortcutPath) then Exit;
  try
    Shell := CreateOleObject('WScript.Shell');
    Link := Shell.CreateShortcut(ShortcutPath);
    Result := CompareText(Link.TargetPath, ExpandConstant('{app}\TableForge.exe')) = 0;
  except
    Result := False;
  end;
  Log(Format('Old TableForge shortcut %s: delete = %d', [ShortcutPath, Ord(Result)]));
end;
