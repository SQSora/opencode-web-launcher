; OpenCode Launcher - C# WPF (Inno Setup 6)
;
; User build (default, no admin):
;   iscc /DMyAppVersion=1.0.0 installer\OpenCodeLauncherCSharp.iss
;
; Machine build (Program Files, admin):
;   iscc /DMyAppVersion=1.0.0 /DMACHINE_BUILD=1 installer\OpenCodeLauncherCSharp.iss
;
; The build output must exist in ..\build\csharp before compiling.

#define MyAppName "OpenCode Launcher"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "OpenCode Launcher"
#define MyAppExeName "OpenCodeLauncher.exe"

#ifdef MACHINE_BUILD
  #ifndef MyVariant
    #define MyVariant "system"
  #endif
  #define MyPrivileges "admin"
  #define MyDefaultDir "{autopf}\OpenCode Launcher"
  #define MyPrograms "{commonprograms}"
  #define MyDesktop "{commondesktop}"
  #define MyStartup "{commonstartup}"
#else
  #ifndef MyVariant
    #define MyVariant "user"
  #endif
  #define MyPrivileges "lowest"
  #define MyDefaultDir "{localappdata}\Programs\OpenCode Launcher"
  #define MyPrograms "{userprograms}"
  #define MyDesktop "{userdesktop}"
  #define MyStartup "{userstartup}"
#endif

[Setup]
AppId={{8E4B2F6A-3C1D-4A7B-9F2E-6D5C1A8B3E70}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={#MyDefaultDir}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired={#MyPrivileges}
OutputDir=..\dist
OutputBaseFilename=OpenCodeLauncher-{#MyAppVersion}-{#MyVariant}
SetupIconFile=..\app\assets\opencode.ico
UninstallDisplayIcon={app}\app\assets\opencode.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
AppMutex=Local\OpenCodeLauncher_7F3A9C21

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startupicon"; Description: "Start OpenCode Launcher at login"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\build\csharp\OpenCodeLauncher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\csharp\app\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\build\csharp\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\csharp\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\csharp\VERSION"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\csharp\BUILD.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{#MyPrograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\app\assets\opencode.ico"
Name: "{#MyDesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\app\assets\opencode.ico"
Name: "{#MyStartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\app\assets\opencode.ico"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Description: "Start {#MyAppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

; Do not add [UninstallDelete] for {app}: user config/log/WebView data lives under
; %APPDATA% and %LOCALAPPDATA%, and Inno already removes the files it installed.
