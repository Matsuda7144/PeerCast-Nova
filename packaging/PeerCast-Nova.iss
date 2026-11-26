#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish-self-contained"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif

[Setup]
AppId={{A7C9E4B2-4F81-4E3A-9C2E-6D84F1E2A701}}
AppName=PeerCast Nova
AppVersion={#AppVersion}
AppVerName=PeerCast Nova {#AppVersion}
AppPublisher=PeerCast Nova Contributors
AppPublisherURL=https://github.com/Matsuda7144/PeerCast-Nova
AppSupportURL=https://github.com/Matsuda7144/PeerCast-Nova/releases
AppUpdatesURL=https://github.com/Matsuda7144/PeerCast-Nova/releases
DefaultDirName={autopf}\PeerCast Nova
DefaultGroupName=PeerCast Nova
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=PeerCast-Nova-{#AppVersion}-win-x64-setup
SetupIconFile=..\src\Nova.App\Assets\Nova.ico
UninstallDisplayIcon={app}\Nova.App.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\PeerCast Nova"; Filename: "{app}\Nova.App.exe"
Name: "{group}\{cm:UninstallProgram,PeerCast Nova}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\PeerCast Nova"; Filename: "{app}\Nova.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Nova.App.exe"; Description: "{cm:LaunchProgram,PeerCast Nova}"; Flags: nowait postinstall skipifsilent
