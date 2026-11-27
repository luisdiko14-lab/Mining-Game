#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName "Mining Game"
#define PublishDir "..\artifacts\publish\win-x64"

[Setup]
AppId={{A83D10A9-54D5-4C81-9EC4-48382FB13247}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Mining Game
DefaultDirName={autopf}\Mining Game
DefaultGroupName=Mining Game
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
OutputDir=..\artifacts\installer
OutputBaseFilename=BlockCraft-{#MyAppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\generated\BlockCraft.ico
UninstallDisplayIcon={app}\Mining_Game.exe
SetupLogging=yes
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Mining Game"; Filename: "{app}\Mining_Game.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Mining Game"; Filename: "{app}\Mining_Game.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Mining_Game.exe"; Description: "Launch Mining Game"; Flags: postinstall nowait skipifsilent
