#define AppName "AirMic"
#define AppVersion "0.1.0-mvp"
#define Publisher "Md Adib Azam"
#define AppExe "AirMic.exe"
#define PublishDir "..\artifacts\windows\publish"
#define DriverDir "..\artifacts\windows\driver"

#if !FileExists(PublishDir + "\" + AppExe)
  #error "Publish the Windows app before compiling the installer."
#endif
#if !FileExists(DriverDir + "\AirMicVirtualAudio.inf")
  #error "A signed AirMic virtual-audio driver is required. The installer must not ship without it."
#endif

[Setup]
AppId={{B636EA80-6A95-40DC-9CEB-8CF08200639F}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={autopf}\AirMic
DefaultGroupName=AirMic
OutputDir=output
OutputBaseFilename=AirMic-Setup-x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\{#AppExe}
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#DriverDir}\*"; DestDir: "{app}\driver"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "install-components.ps1"; DestDir: "{app}\installer"; Flags: ignoreversion

[Icons]
Name: "{group}\AirMic"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\AirMic"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\installer\install-components.ps1"" -Action Install -InstallDirectory ""{app}"" -DriverInf ""{app}\driver\AirMicVirtualAudio.inf"""; StatusMsg: "Installing the signed AirMic virtual microphone and Private-network firewall rules…"; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExe}"; Description: "Open AirMic"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\installer\install-components.ps1"" -Action Uninstall -InstallDirectory ""{app}"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveAirMicComponents"

[Code]
procedure InitializeWizard;
var
  Page: TOutputMsgWizardPage;
begin
  Page := CreateOutputMsgPage(wpWelcome, 'Audio component and permissions',
    'AirMic installs a signed virtual audio device.',
    'Windows administrator permission is required to install AirMic Virtual Microphone. ' +
    'The installer also creates inbound TCP 51243 and UDP 51244 rules for Private networks only. ' +
    'No cloud service, account, or background Windows service is installed.');
end;
