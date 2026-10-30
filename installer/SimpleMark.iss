; Inno Setup script for Simple Mark.
; Build: ISCC /DAppVersion=1.2.3 installer\SimpleMark.iss  (expects build.cmd output in bin\)

#ifndef AppVersion
  #error Define AppVersion, e.g. ISCC /DAppVersion=1.2.3
#endif

#define AppName "Simple Mark"
#define AppExe "SimpleMark.exe"
#define ProgId "SimpleMark.md"
#define RepoUrl "https://github.com/rodrigopero/simple-mark"
; Quotes are doubled because the value is written inside a quoted ValueData: "<app>\SimpleMark.exe" "%1"
#define OpenCommand '""{app}\' + AppExe + '"" ""%1""'

[Setup]
; AppId identifies the app for upgrades and uninstall: never change it.
AppId={{FEC72340-6AE5-495D-A313-90522933F7E0}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Simple Mark contributors
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
; Per-user install: no administrator rights needed.
PrivilegesRequired=lowest
ChangesAssociations=yes
CloseApplications=yes
MinVersion=10.0
LicenseFile=..\LICENSE
SetupIconFile=..\src\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputBaseFilename=SimpleMark-{#AppVersion}-setup
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
english.SetDefaultApp=Make Simple Mark the default app for .md files
spanish.SetDefaultApp=Hacer de Simple Mark la aplicación predeterminada para archivos .md

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\bin\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; File type handled by the app.
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: ""; ValueData: "Markdown document"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: "{#OpenCommand}"

; Offer the app in "Open with" for .md and .markdown without taking over the current default.
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: "{#OpenCommand}"

; Register in Settings > Default apps so the user can pick Simple Mark for .md files.
Root: HKA; Subkey: "Software\SimpleMark"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\SimpleMark\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppName}"
Root: HKA; Subkey: "Software\SimpleMark\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Minimal, secure Markdown viewer"
Root: HKA; Subkey: "Software\SimpleMark\Capabilities\FileAssociations"; ValueType: string; ValueName: ".md"; ValueData: "{#ProgId}"
Root: HKA; Subkey: "Software\SimpleMark\Capabilities\FileAssociations"; ValueType: string; ValueName: ".markdown"; ValueData: "{#ProgId}"
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#AppName}"; ValueData: "Software\SimpleMark\Capabilities"; Flags: uninsdeletevalue

[Run]
; Windows does not let apps set themselves as default; this opens the Settings page to confirm it.
Filename: "ms-settings:defaultapps?registeredAppUser=Simple%20Mark"; Description: "{cm:SetDefaultApp}"; Flags: shellexec postinstall skipifsilent nowait
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: postinstall skipifsilent nowait unchecked
