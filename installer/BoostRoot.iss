; BoostRoot installer (Inno Setup 6). Built by .github/workflows/release.yml:
;   iscc /DAppVersion=26.11.1 /DSourceDir=..\publish\app installer\BoostRoot.iss
#ifndef AppVersion
  #define AppVersion "26.11.1"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\app"
#endif

[Setup]
AppId={{6F1E9B7A-3C1D-4D8E-9B2A-B00570000001}
AppName=BoostRoot
AppVersion={#AppVersion}
AppVerName=BoostRoot {#AppVersion}
AppPublisher=bl4z3root
AppPublisherURL=https://github.com/bl4z3rootstudio/BoostRoot
AppSupportURL=https://github.com/bl4z3rootstudio/BoostRoot/issues
AppUpdatesURL=https://github.com/bl4z3rootstudio/BoostRoot/releases
DefaultDirName={autopf}\BoostRoot
DefaultGroupName=BoostRoot
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\publish
OutputBaseFilename=BoostRoot-Setup-{#AppVersion}
SetupIconFile=..\src\BoostRoot.App\Assets\boostroot.ico
UninstallDisplayIcon={app}\BoostRoot.exe
WizardStyle=modern dynamic
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE
VersionInfoVersion={#AppVersion}.0

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
turkish.DesktopIcon=Masaüstüne kısayol oluştur
english.DesktopIcon=Create a desktop shortcut
turkish.RevertAsk=BoostRoot'un yaptığı tüm değişiklikler (oyun ayarları, Windows ayarları) geri alınsın mı?%n%nÖnerilen: Evet
english.RevertAsk=Undo every change BoostRoot made (game settings, Windows settings)?%n%nRecommended: Yes
turkish.LaunchNow=BoostRoot'u başlat
english.LaunchNow=Launch BoostRoot

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\BoostRoot"; Filename: "{app}\BoostRoot.exe"
Name: "{autodesktop}\BoostRoot"; Filename: "{app}\BoostRoot.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\BoostRoot.exe"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\BoostRoot\tools"
Type: filesandordirs; Name: "{localappdata}\BoostRoot\covers"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
begin
  if CurUninstallStep = usUninstall then
    if UninstallSilent or (MsgBox(CustomMessage('RevertAsk'), mbConfirmation, MB_YESNO) = IDYES) then
      Exec(ExpandConstant('{app}\BoostRoot.exe'), '--revert-all', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;
