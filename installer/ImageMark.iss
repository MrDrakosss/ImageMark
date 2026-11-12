; ImageMark telepítő – fordítás: ISCC.exe installer\ImageMark.iss
; Előfeltétel: dotnet publish -c Release -r win-x64 --self-contained -o publish

; A verzió, a kiadó és a copyright a publikált exe-ből jön (ImageMark.csproj), itt nem kell átírni.

#define MyAppName "ImageMark"
#define MyAppExeName "ImageMark.exe"
#define MyAppExePath "..\publish\" + MyAppExeName
#define MyAppVersion GetStringFileInfo(MyAppExePath, "ProductVersion")
#define MyAppPublisher GetStringFileInfo(MyAppExePath, "CompanyName")
#define MyAppCopyright GetStringFileInfo(MyAppExePath, "LegalCopyright")

[Setup]
AppId={{8B1F64D2-3A7C-4E45-9C0B-2ED1F5A0A7B1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright={#MyAppCopyright}
VersionInfoVersion={#GetVersionNumbersString(MyAppExePath)}
VersionInfoProductTextVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\dist
OutputBaseFilename=ImageMark-Setup-{#MyAppVersion}
SetupIconFile=..\Assets\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Rendszergazdai jog nélkül, felhasználói profilba telepít; kérésre gépszintű is lehet.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "hungarian"; MessagesFile: "compiler:Languages\Hungarian.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
