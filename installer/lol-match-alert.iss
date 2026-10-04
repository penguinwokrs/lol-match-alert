; SPDX-License-Identifier: GPL-3.0-only
; Copyright (C) 2026 penguinwokrs
;
; Built by .github/workflows/release.yml after `dotnet publish` has put the exe in dist\app.

#define AppName "lol-match-alert"
#define AppExe "lol-match-alert.exe"
#define AppVersion GetEnv("LMA_VERSION")
#if AppVersion == ""
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{5B7E2C1A-9D43-4F0E-8A61-3C2D7E9B4F10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=penguinwokrs
AppSupportURL=https://github.com/penguinwokrs/lol-match-alert
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename={#AppName}-{#AppVersion}-setup
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
LicenseFile=..\LICENSE
; Per user, never elevated: the autostart entry goes in HKCU, which must be the hive of the person
; installing, not of an administrator whose credentials were borrowed.
PrivilegesRequired=lowest
; The tray holds this mutex. Setup and uninstall ask for it to be closed rather than killing it,
; because a tray killed mid alert cannot put the keyboard's lighting back.
AppMutex={#AppName}

[Tasks]
Name: "autostart"; Description: "Start when Windows starts"; GroupDescription: "Additional tasks:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional tasks:"; Flags: unchecked

[Files]
; Self-contained single file: there is no runtime to install.
Source: "..\dist\app\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
; GPL-3.0 wants the licence to travel with the binary.
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value the tray's own "Start with Windows" toggle writes.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
  ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; The tray's toggle may have written the Run value without the autostart task; never leave it
; pointing at a deleted exe.
Filename: "reg.exe"; Parameters: "delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v {#AppName} /f"; \
  Flags: runhidden; RunOnceId: "RemoveAutostart"

[UninstallDelete]
; %APPDATA%\lol-match-alert (settings, device profiles) is kept so a reinstall keeps them.
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{localappdata}\{#AppName}"
