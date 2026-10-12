; Bing 壁纸 — Windows 安装包（Inno Setup 6.3+）
;
; 生成安装包：
;   ISCC.exe /DAppVersion=1.0.1 /DAppVersionNumeric=1.0.1.0 /DSourceDir=dist /DOutputDir=dist installer\bing-wallpaper.iss
; 不带参数直接编译时，默认读 ..\dist、写 ..\dist。
; 推荐入口是 tools\release.ps1，由它计算版本号并调用 ISCC。

#if VER < EncodeVer(6, 3, 0)
  #error 需要 Inno Setup 6.3 或更高版本（ArchitecturesAllowed=x64compatible 自 6.3 起支持）
#endif

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef AppVersionNumeric
  #define AppVersionNumeric "0.0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\dist"
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "Bing 壁纸"
#define AppExeName "BingWallpaper.exe"
#define ProjectUrl "https://github.com/skorpiox89/bing-wallpaper"

[Setup]
; AppId 一经发布不要改。改掉会被系统当成另一个程序，无法覆盖升级。
AppId={{3F1E7A24-8C5B-4D92-A6E0-5B7C2F9D41A8}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=skorpiox89
AppPublisherURL={#ProjectUrl}
AppSupportURL={#ProjectUrl}/issues
AppUpdatesURL={#ProjectUrl}/releases
; 按用户安装：PrivilegesRequired=lowest 时 {autopf} = %LOCALAPPDATA%\Programs
DefaultDirName={autopf}\BingWallpaper
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
LicenseFile=..\LICENSE
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=BingWallpaper-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\BingWallpaper\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
VersionInfoVersion={#AppVersionNumeric}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} 安装程序
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 托盘程序没有主窗口，Restart Manager 找不到它；由下面的 [Code] 直接结束进程
CloseApplications=no
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "chinese"; MessagesFile: "languages\ChineseSimplified.isl"

[Files]
Source: "{#SourceDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "立即运行 {#AppName}"; Flags: nowait postinstall skipifsilent

[Registry]
; 开机自启由程序自己在首次运行时写入；这里只在卸载时把那条 Run 值删掉
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "BingWallpaper"; ValueType: none; Flags: uninsdeletevalue

[UninstallDelete]
; 图片缓存 / 索引 / 日志一起删掉
Type: filesandordirs; Name: "{localappdata}\BingWallpaper"

[Code]
const
  AppImageName = '{#AppExeName}';

procedure KillRunningApp();
var
  ResultCode: Integer;
begin
  { 结束正在运行的托盘程序（没在跑时 taskkill 返回非 0，属正常，不阻塞流程） }
  if Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ' + AppImageName, '',
          SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Log('taskkill ' + AppImageName + ' 退出码: ' + IntToStr(ResultCode));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  { 安装正式开始前：覆盖安装时先结束旧版本 }
  KillRunningApp();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    KillRunningApp();
end;
