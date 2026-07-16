; VirtualPrinter Inno Setup Installer
#define MyAppName "VirtualPrinter"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "VirtualPrinter"
#define MyAppURL "https://virtualprinter.local"

[Setup]
AppId={{F3A2B1C4-D5E6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=..\dist
OutputBaseFilename=VirtualPrinter_Setup
Compression=lzma2/ultra
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=6.1.7601
DisableProgramGroupPage=yes
DisableReadyPage=no
SetupIconFile=assets\logo.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Core binaries
Source: "..\dist\bin\VPPostScriptMon.dll"; DestDir: "{sys}"; Flags: regserver restartreplace uninsrestartdelete
Source: "..\dist\bin\EnvChecker.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\bin\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "VPPostScriptMon.dll"

; Canon Generic Plus PS3 driver (default V3 PostScript driver)
Source: "..\drivers\CanonGenericPlusPS3\*"; DestDir: "{app}\drivers\Canon"; Flags: ignoreversion recursesubdirs

; Ghostscript runtime
Source: "..\lib\gs\*"; DestDir: "{app}\gs"; Flags: ignoreversion recursesubdirs

; VC++ Redist (embedded for Win10/11)
Source: "..\lib\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall; Check: IsWin10OrLater

[Icons]
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{group}\VirtualPrinter 管理工具"; Filename: "{app}\VirtualPrinterManager.exe"
Name: "{commondesktop}\VirtualPrinter 管理工具"; Filename: "{app}\VirtualPrinterManager.exe"

[Run]
; Install VC++ Redist (Win10/11 embedded)
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "正在安装 VC++ Redist..."; Check: IsWin10OrLater and NeedsVCRedist

; Install .NET Framework 4.8 (Win7 only, download from CDN)
Filename: "{tmp}\ndp48-x86-x64-allos-enu.exe"; Parameters: "/q /norestart /chainingpackage VirtualPrinter"; StatusMsg: "正在安装 .NET Framework 4.8..."; Check: IsWin7OrLater and not IsWin10OrLater and NeedsDotNet48

; Install Canon Generic Plus PS3 driver (V3 PostScript driver)
Filename: "pnputil"; Parameters: "/add-driver ""{app}\drivers\Canon\CNS30MA64.INF"""; Flags: runhidden; StatusMsg: "正在安装打印机驱动..."

; Create port and printer
Filename: "rundll32"; Parameters: "printui.dll,PrintUIEntry /if /b ""YanziWu PDF-IMG Printer"" /f ""{app}\drivers\Canon\CNS30MA64.INF"" /r ""VP_Port"" /m ""Canon Generic Plus PS3"""; Flags: runhidden; StatusMsg: "正在创建打印机..."

; Install and start Windows Service
Filename: "sc"; Parameters: "create VirtualPrinterService binPath=""{app}\VirtualPrinterService.exe"" start=auto displayName=""VirtualPrinter Service"""; Flags: runhidden; StatusMsg: "正在安装后台服务..."
Filename: "sc"; Parameters: "description VirtualPrinterService ""PostScript to PDF/Image conversion service for VirtualPrinter"""; Flags: runhidden
Filename: "sc"; Parameters: "start VirtualPrinterService"; Flags: runhidden; StatusMsg: "正在启动服务..."

[UninstallRun]
Filename: "sc"; Parameters: "stop VirtualPrinterService"; Flags: runhidden
Filename: "sc"; Parameters: "delete VirtualPrinterService"; Flags: runhidden
Filename: "rundll32"; Parameters: "printui.dll,PrintUIEntry /dl /n ""YanziWu PDF-IMG Printer"""; Flags: runhidden
Filename: "rundll32"; Parameters: "printui.dll,PrintUIEntry /dr /n ""VP_Port"""; Flags: runhidden
Filename: "rundll32"; Parameters: "printui.dll,PrintUIEntry /dd /m ""Canon Generic Plus PS3"""; Flags: runhidden

[Code]
// Environment detection functions
function NeedsVCRedist: Boolean;
var
  major: Cardinal;
begin
  Result := True;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Major', major) then
  begin
    if major >= 14 then
      Result := False;
  end;
end;

function NeedsDotNet48: Boolean;
var
  release: Cardinal;
begin
  Result := True;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', release) then
  begin
    if release >= 528040 then  // .NET 4.8
      Result := False;
  end;
end;

function _GetWindowsVersion: Cardinal;
var
  major, minor: Cardinal;
begin
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\Windows NT\CurrentVersion', 'CurrentMajorVersionNumber', major) and
     RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\Windows NT\CurrentVersion', 'CurrentMinorVersionNumber', minor) then
  begin
    Result := (major shl 8) or minor;
  end
  else
  begin
    Result := 0;
  end;
end;

function IsWin7OrLater: Boolean;
begin
  Result := _GetWindowsVersion >= $0601;
end;

function IsWin10OrLater: Boolean;
begin
  Result := _GetWindowsVersion >= $0A00;
end;

// Custom setup page: Environment check
var
  CheckPage: TOutputProgressWizardPage;
  CheckFailed: Boolean;

procedure CheckEnvironment;
var
  arch: string;
  osVer: string;
begin
  CheckPage.SetProgress(0, 10);
  CheckPage.SetText('正在检测系统环境...', '');

  // Check OS version
  if IsWin10OrLater then
    osVer := 'Windows 10/11'
  else if IsWin7OrLater then
    osVer := 'Windows 7 SP1'
  else
  begin
    MsgBox('不支持的 Windows 版本。需要 Windows 7 SP1 或更高版本。', mbError, MB_OK);
    CheckFailed := True;
    Exit;
  end;
  CheckPage.SetProgress(1, 10);

  // Check architecture
  if not Is64BitInstallMode then
  begin
    MsgBox('仅支持 64 位 (x64) 系统。', mbError, MB_OK);
    CheckFailed := True;
    Exit;
  end;
  CheckPage.SetProgress(2, 10);

  // Check admin
  if not IsAdminLoggedOn then
  begin
    MsgBox('安装需要管理员权限，请以管理员身份运行。', mbError, MB_OK);
    CheckFailed := True;
    Exit;
  end;
  CheckPage.SetProgress(3, 10);

  // Check .NET Framework
  if NeedsDotNet48 and not IsWin10OrLater then
  begin
    CheckPage.SetText('需要安装 .NET Framework 4.8 (将从微软 CDN 下载)...', '');
    // Installer will handle this in [Run] section
  end;
  CheckPage.SetProgress(5, 10);

  // Check VC++ Redist
  if NeedsVCRedist then
  begin
    if IsWin10OrLater then
      CheckPage.SetText('VC++ Redist 已内嵌，正在准备安装...', '')
    else
      CheckPage.SetText('VC++ Redist 需要从 CDN 下载...', '');
  end;
  CheckPage.SetProgress(7, 10);
end;

procedure InitializeWizard;
begin
  CheckPage := CreateOutputProgressPage('系统环境检测', '正在检测您的系统环境...');
  CheckFailed := False;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpReady then
  begin
    CheckPage.Show;
    try
      CheckEnvironment;
    finally
      CheckPage.Hide;
    end;

    if CheckFailed then
      WizardForm.Close;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Verify printer was installed
  end;
end;
