; Setup for the Sonokinesis app. Built by build/package.sh (Inno Setup 6):
;   ISCC.exe /DAppVersion=0.3.0 installer\Sonokinesis.iss
; Installs for the current user only (no administrator rights), so the app can update itself.
; The app runs this setup with /SILENT to install an update over itself, and it starts again after.

#ifndef AppVersion
  #error Pass the version: /DAppVersion=x.y.z
#endif
#define AppSource "..\dist\Sonokinesis-" + AppVersion

[Setup]
AppId={{961B0676-C948-4F6E-8A1E-A8DEC07AE1B7}
AppName=Sonokinesis
AppVersion={#AppVersion}
AppVerName=Sonokinesis {#AppVersion}
AppPublisher=The Void
AppPublisherURL=https://github.com/TheFormOfVoid/sonokinesis
AppSupportURL=https://github.com/TheFormOfVoid/sonokinesis/issues
AppUpdatesURL=https://github.com/TheFormOfVoid/sonokinesis/releases
DefaultDirName={localappdata}\Programs\Sonokinesis
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=Sonokinesis-Setup-{#AppVersion}
SetupIconFile=..\build\sonokinesis.ico
UninstallDisplayIcon={app}\sonokinesis.ico
UninstallDisplayName=Sonokinesis
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Put a shortcut on the desktop"

[InstallDelete]
; The engine and its runtime are replaced as a whole on updates, so no old file stays behind.
Type: filesandordirs; Name: "{app}\data_SonokinesisEditor_windows_x86_64"
Type: filesandordirs; Name: "{app}\loader"
Type: filesandordirs; Name: "{app}\library"
Type: filesandordirs; Name: "{app}\docs"

[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "..\build\sonokinesis.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Sonokinesis"; Filename: "{app}\Sonokinesis.exe"; IconFilename: "{app}\sonokinesis.ico"; Comment: "Play and make custom levels for Just Shapes & Beats"
Name: "{autodesktop}\Sonokinesis"; Filename: "{app}\Sonokinesis.exe"; IconFilename: "{app}\sonokinesis.ico"; Tasks: desktopicon

[Run]
; Also runs after a silent update, so the app comes back by itself.
Filename: "{app}\Sonokinesis.exe"; Description: "Start Sonokinesis"; Flags: nowait postinstall

[UninstallDelete]
; The AI tool, if it was downloaded from inside the app.
Type: filesandordirs; Name: "{app}\tools"

[Messages]
FinishedLabel=Sonokinesis is installed. Open it, press Play, and it sets up Just Shapes & Beats for you.
