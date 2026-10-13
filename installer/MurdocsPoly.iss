; Inno Setup script for Murdoc's Poly. Build: publish to dist\app first, then ISCC.exe installer\MurdocsPoly.iss
#define AppName "Murdoc's Poly"
#define AppVersion "0.2.1"

[Setup]
AppId={{B3C1D7E4-52A9-4F0B-8E63-7A19C2D4F5B8}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=murdoc169
DefaultDirName={autopf}\Murdocs Poly
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=MurdocsPoly-Setup-{#AppVersion}
UninstallDisplayIcon={app}\MurdocsPoly.exe
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,smoke.txt"
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\MurdocsPoly.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\MurdocsPoly.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MurdocsPoly.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
