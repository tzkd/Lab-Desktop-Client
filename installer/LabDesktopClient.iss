#ifndef AppVersion
  #error AppVersion must be supplied by scripts/release.ps1
#endif

#define AppName "Lab Desktop Client"
#define AppPublisher "tzkd"
#define AppExeName "LabDesktopClient.exe"
#define ProjectRoot AddBackslash(SourcePath) + ".."

[Setup]
AppId={{9C0C7AE4-4618-4FD8-8247-61E817070215}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}.0
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/tzkd/Lab-Desktop-Client
AppSupportURL=https://github.com/tzkd/Lab-Desktop-Client/issues
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
OutputDir={#ProjectRoot}\artifacts\installer
OutputBaseFilename=LabDesktopClient-{#AppVersion}-win-x64-setup
LicenseFile={#ProjectRoot}\LICENSE
SetupIconFile={#ProjectRoot}\src\LabDesktop.Client.App\Assets\LabConnect.ico
UninstallDisplayIcon={app}\{#AppExeName}
CloseApplications=force
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Files]
Source: "{#ProjectRoot}\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ProjectRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProjectRoot}\docs\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\实验室远程桌面"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\实验室远程桌面"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "启动实验室远程桌面"; Flags: nowait postinstall skipifsilent
