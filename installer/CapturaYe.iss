#ifndef MyAppVersion
  #error 请由 scripts/release.ps1 传入版本号
#endif
#ifndef PackageDir
  #error 请由 scripts/release.ps1 传入程序目录
#endif
#ifndef ReleaseDir
  #error 请由 scripts/release.ps1 传入输出目录
#endif

[Setup]
AppId={code:GetAppId}
AppName=Captura Ye 中文版
AppVersion={#MyAppVersion}
AppVerName=Captura Ye 中文版 v{#MyAppVersion}
AppPublisher=yakoye
AppPublisherURL=https://github.com/yakoye/Captura_Ye
AppSupportURL=https://github.com/yakoye/Captura_Ye/issues
AppUpdatesURL=https://github.com/yakoye/Captura_Ye/releases
DefaultDirName={localappdata}\Programs\CapturaYe
DefaultGroupName=Captura Ye 中文版
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
DisableWelcomePage=no
WizardStyle=modern
OutputDir={#ReleaseDir}
OutputBaseFilename=CapturaYe-v{#MyAppVersion}-Setup
Compression=lzma2
SolidCompression=yes
SetupIconFile=..\src\Captura\Images\Captura.ico
UninstallDisplayIcon={app}\captura.exe
VersionInfoVersion={#MyAppVersion}
VersionInfoDescription=Captura Ye 中文版安装程序
ShowLanguageDialog=yes
UsePreviousLanguage=no
CloseApplications=no
MinVersion=10.0

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PackageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Captura Ye 中文版"; Filename: "{app}\captura.exe"; Check: not IsVerification
Name: "{userdesktop}\Captura Ye 中文版"; Filename: "{app}\captura.exe"; Tasks: desktopicon; Check: not IsVerification

[Run]
Filename: "{app}\captura.exe"; Description: "{cm:LaunchProgram,Captura Ye 中文版}"; Flags: nowait postinstall skipifsilent

[Code]
function IsVerification(): Boolean;
begin
  Result := ExpandConstant('{param:TESTMODE|0}') = '1';
end;

function GetAppId(Param: String): String;
begin
  Result := '{82BA6AC1-AFB9-4D42-86E5-44BF7850AEB8}';
  if IsVerification then Result := Result + '-Verification';
end;

function InitializeSetup(): Boolean;
var
  FrameworkRelease: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', FrameworkRelease);
  if Result then Result := FrameworkRelease >= 461808;
  if not Result then
    MsgBox('请先安装 .NET Framework 4.7.2 或更高版本，再运行本安装程序。', mbError, MB_OK);
end;
