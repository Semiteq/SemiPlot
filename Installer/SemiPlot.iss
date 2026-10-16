; SemiPlot Inno Setup installer script
; Build with: iscc.exe /DAppVersion=1.2.3 Installer\SemiPlot.iss
; Run `dotnet publish SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj -c Release -p:Version=1.2.3` first.

#ifndef AppVersion
  #error AppVersion must be defined. Pass it as: iscc.exe /DAppVersion=1.2.3 SemiPlot.iss
#endif

#define AppName      "SemiPlot"
#define AppPublisher "Inc Semiteq"
#define AppExeName   "SemiPlot.UI.exe"
#define AppId        "{{D1A2B63D-5F0B-4A3E-9437-885417FCE2BC}"

; Paths relative to the location of this .iss file (Installer/)
#define SrcBinDir    "..\SemiPlot\Artifacts\publish\SemiPlot.UI\release_win-x64"
#define SrcCfgDir    "..\ConfigFiles"
#define AppIconFile  "..\SemiPlot\SemiPlot.UI\logo.ico"

; Site paths: docs/architecture/overview.md#deployment
#define ConfigRoot   "C:\DISTR\Config\SemiPlot"
#define LogRoot      "C:\DISTR\Logs\SemiPlot"
#define LaunchArgs   '--config-dir ""' + ConfigRoot + '"" --log-file ""' + LogRoot + '\semiplot.log"" --logging-level information'

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}

OutputDir=Output
OutputBaseFilename=SemiPlot-Setup

Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

WizardStyle=modern
SetupIconFile={#AppIconFile}
UninstallDisplayIcon={app}\{#AppExeName}

; Program Files and C:\DISTR both need admin rights
PrivilegesRequired=admin

CloseApplications=yes
CloseApplicationsFilter=*{#AppExeName}*

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Dirs]
; The operator, not the installing admin, writes both: the settings window and the log sink
Name: "{#ConfigRoot}"; Permissions: users-modify; Flags: uninsneveruninstall
Name: "{#LogRoot}"; Permissions: users-modify; Flags: uninsneveruninstall

[Files]
Source: "{#SrcBinDir}\*"; Excludes: "*.pdb"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; An upgrade keeps the operator's configuration, the archive password included
Source: "{#SrcCfgDir}\*"; DestDir: "{#ConfigRoot}"; Flags: onlyifdoesntexist uninsneveruninstall recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Parameters: "{#LaunchArgs}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Parameters: "{#LaunchArgs}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Parameters: "{#LaunchArgs}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser
