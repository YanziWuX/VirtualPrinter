using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Reflection;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace VirtualPrinter.Manager
{
    public partial class MainWindow : Window
    {
        private static readonly string LOG_PATH = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VirtualPrinter", "install.log");

        private const string PRINTER_NAME = "YanziWu PDF-IMG Printer";
        private const string DRIVER_NAME = "Canon Generic Plus PS3";
        private const string PORT_NAME = "VP_Port";
        private const string SERVICE_NAME = "VirtualPrinterService";
        private static readonly string OUTPUT_DIR = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VirtualPrinter");
        private static readonly string JOBS_DIR = Path.Combine(OUTPUT_DIR, "jobs");
        private const string CANON_INF = @"drivers\Canon\CNS30MA64.INF";

        private const string APP_VERSION = "1.0.0";
        private static readonly string TEST_BATCH = VersionInfo.TestBatch;
        private static readonly string VERSION_FILE = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VirtualPrinter", "version.txt");

        private const int PORT_VERIFY_RETRIES = 6;
        private const int PORT_VERIFY_POLL_INTERVAL_MS = 1000;

        public MainWindow()
        {
            if (!IsAdmin())
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = Assembly.GetExecutingAssembly().Location,
                        Verb = "runas",
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
                catch
                {
                    MessageBox.Show("需要管理员权限才能运行。请以管理员身份重新启动此程序。", "权限不足",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                Environment.Exit(0);
            }
            InitializeComponent();
            string installedBatch = ReadInstalledVersion();
            if (!string.IsNullOrEmpty(installedBatch))
            {
                bool match = installedBatch == $"{APP_VERSION}-{TEST_BATCH}";
                VersionLabel.Text = match
                    ? $"v{APP_VERSION} — 已是最新"
                    : $"v{APP_VERSION} (待升级)";
                BatchLabel.Text = match
                    ? $"批次: {TEST_BATCH}"
                    : $"当前: {installedBatch}  →  新版: {APP_VERSION}-{TEST_BATCH}";
            }
            else
            {
                VersionLabel.Text = $"v{APP_VERSION}";
                BatchLabel.Text = $"批次: {TEST_BATCH}";
            }
            AboutVersion.Text = $"版本 {APP_VERSION} (测试批次 {TEST_BATCH})";
            UpdateAdminStatus();
            NavManage.IsChecked = true;
            LoadDefaultSettings();
            Loaded += (s, e) => RefreshStatus();
        }

        private static bool IsAdmin()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private void UpdateAdminStatus()
        {
            if (IsAdmin())
            {
                AdminStatus.Text = "\u2713 管理员模式";
                AdminStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
            }
            else
            {
                AdminStatus.Text = "\u25CB 非管理员模式 - 部分功能受限";
                AdminStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
            }
        }

        private void OnNavChanged(object sender, RoutedEventArgs e)
        {
            if (PageManage == null || PageSettings == null || PageAbout == null) return;
            PageManage.Visibility = NavManage.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageSettings.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageAbout.Visibility = NavAbout.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadDefaultSettings()
        {
            DefaultSaveDir.Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            try
            {
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "VirtualPrinter", "settings.json");
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var dict = SimpleJsonParse(json);
                    if (dict.TryGetValue("SaveFolder", out var sf))
                        DefaultSaveDir.Text = sf ?? "";
                    if (dict.TryGetValue("WatermarkText", out var wt))
                        WatermarkDefaultText.Text = wt ?? "Confidential";
                }
            }
            catch { }
        }

        private void OnBrowseDefaultDir(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                SelectedPath = DefaultSaveDir.Text,
                Description = "选择默认保存目录"
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                DefaultSaveDir.Text = dialog.SelectedPath;
        }

        private void OnInstall(object sender, RoutedEventArgs e) => InstallAsync();

        private static Dictionary<string, string> SimpleJsonParse(string json)
        {
            var dict = new Dictionary<string, string>();
            var matches = Regex.Matches(json, "\"([^\"]+)\"\\s*:\\s*\"?([^,\"}\\]]+)\"?");
            foreach (Match m in matches)
            {
                if (m.Groups.Count >= 3)
                    dict[m.Groups[1].Value] = m.Groups[2].Value.TrimEnd('"');
            }
            return dict;
        }

        private async void InstallAsync()
        {
            try { if (File.Exists(LOG_PATH)) File.Delete(LOG_PATH); } catch { }
            Log("=== VirtualPrinter 安装开始 ===");

            var progress = new ProgressWindow(this);
            progress.Show();

            bool svcOk = false;
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var status = new Action<string>(s => { progress.SetStatus(s); Log($"  [UI] {s}"); });

            try
            {
                // Step 0: Environment pre-flight checks
                Log("Step 0: 环境健康检查...");
                status("正在检查系统环境...");
                await Task.Run(() => PerformPreflightChecks());
                Log("  环境检查通过");

                // Clean up old printer if exists
                Log("清理上一次安装残留...");
                try
                {
                    await Task.Run(() => RunPowerShell(
                        $"Get-Printer -Name '{PRINTER_NAME}' -ErrorAction SilentlyContinue | Remove-Printer -ErrorAction SilentlyContinue"));
                }
                catch { }

                // Step 1: Create output directories
                Log("Step 1: 创建输出目录...");
                status("正在创建输出目录...");
                await Task.Run(() => RunPowerShell($"New-Item -ItemType Directory -Path '{OUTPUT_DIR}' -Force | Out-Null"));
                await Task.Run(() => RunPowerShell($"New-Item -ItemType Directory -Path '{JOBS_DIR}' -Force | Out-Null"));
                Log("  目录已创建");

                // Step 2: Add local port with verification
                Log("Step 2: 添加本地端口...");
                status("正在添加打印端口...");
                bool portOk = await Task.Run(() => InstallAndVerifyPort());
                if (!portOk) throw new Exception("端口创建失败");
                Log("  端口已创建");

                // Step 3: Install service
                Log("Step 3: 安装 Windows 服务...");
                status("正在安装 Windows 服务...");
                svcOk = await Task.Run(() => InstallService(exeDir, status));
                Log($"  服务: svcOk={svcOk}");
                if (!svcOk) throw new Exception("Windows 服务安装失败");

                // Step 4: Create printer with driver readiness verification
                Log("Step 4: 创建打印机...");
                status("正在创建打印机...");
                bool printerOk = await Task.Run(() => InstallPrinter());
                if (!printerOk) throw new Exception("打印机创建失败");

                WriteInstalledVersion();
                progress.Close();
                Log("=== 安装成功 ===");
                MessageBox.Show("安装成功！", "VirtualPrinter",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshStatus();
            }
            catch (Exception ex)
            {
                Log($"=== 安装失败: {ex.Message} ===");
                progress.SetStatus("安装失败，正在回滚...");
                await Task.Run(() => Rollback(svcOk));
                progress.Close();
                MessageBox.Show($"{ex.Message}，已回滚已安装的组件。", "安装失败",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                RefreshStatus();
            }
        }

        private void PerformPreflightChecks()
        {
            if (!CheckSpoolerRunning())
            {
                Log("  [错误] Print Spooler 服务未运行");
                throw new Exception("Print Spooler 服务未运行，请先启动后台打印服务");
            }
            Log("  Print Spooler: 运行中");

            string arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
            Log($"  系统架构: {arch}");
            if (!string.Equals(arch, "AMD64", StringComparison.OrdinalIgnoreCase))
            {
                Log("  [警告] 非 x64 架构，VirtualPrinter 仅支持 x64");
            }
        }

        private bool InstallAndVerifyPort()
        {
            string existingPort = RunPowerShellGetOutput(
                $"Get-PrinterPort -Name '{PORT_NAME}' -ErrorAction SilentlyContinue");
            if (!string.IsNullOrEmpty(existingPort))
            {
                Log("  VP_Port 已存在，跳过创建");
                return true;
            }

            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string monDll = Path.Combine(exeDir, "VPPostScriptMon.dll");
            string sysMonDll = Path.Combine(Environment.SystemDirectory, "VPPostScriptMon.dll");

            // Copy VPPostScriptMon.dll to System32 if needed
            if (!File.Exists(sysMonDll) || File.GetLastWriteTime(monDll) > File.GetLastWriteTime(sysMonDll))
            {
                Log("  部署 VPPostScriptMon.dll 到 System32...");
                try
                {
                    File.Copy(monDll, sysMonDll, overwrite: true);
                }
                catch (Exception ex)
                {
                    Log($"  复制 VPPostScriptMon.dll 失败: {ex.Message}");
                    return false;
                }
            }

            // Register port monitor and create VP_Port
            Log("  正在注册端口监视器并创建 VP_Port...");
            var (exitCode, errorMsg) = RunPowerShellWithError(@"
$monKey = 'HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\VirtualPrinter Port Monitor'
$portsKey = $monKey + '\Ports'
if (-not (Test-Path $monKey)) { New-Item -Path $monKey -Force | Out-Null }
Set-ItemProperty -Path $monKey -Name 'Driver' -Value 'VPPostScriptMon.dll' -Type String -Force
if (-not (Test-Path $portsKey)) { New-Item -Path $portsKey -Force | Out-Null }
New-ItemProperty -Path $portsKey -Name 'VP_Port' -Value '' -PropertyType String -Force | Out-Null
Restart-Service -Name Spooler -Force
");
            if (exitCode != 0)
            {
                Log($"  VP_Port 创建失败: {errorMsg}");
                return false;
            }

            for (int i = 0; i < PORT_VERIFY_RETRIES; i++)
            {
                string verify = RunPowerShellGetOutput(
                    $"Get-PrinterPort -Name '{PORT_NAME}' -ErrorAction SilentlyContinue");
                if (!string.IsNullOrEmpty(verify))
                {
                    Log("  VP_Port 创建成功并已验证");
                    return true;
                }
                Log($"  等待 VP_Port 注册... (尝试 {i + 1}/{PORT_VERIFY_RETRIES})");
                Thread.Sleep(PORT_VERIFY_POLL_INTERVAL_MS);
            }

            Log("  VP_Port 创建后验证超时");
            return false;
        }

        private bool InstallPrinter()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string canonInfPath = Path.Combine(exeDir, CANON_INF);

            if (!File.Exists(canonInfPath))
            {
                Log($"  Canon INF 未找到: {canonInfPath}");
                return false;
            }

            var existing = RunPowerShellGetOutput(
                $"Get-Printer -Name '{PRINTER_NAME}' -ErrorAction SilentlyContinue");
            if (!string.IsNullOrEmpty(existing))
            {
                RunPowerShell(
                    $"Remove-Printer -Name '{PRINTER_NAME}' -ErrorAction SilentlyContinue");
                Log("  已移除同名旧打印机");
            }

            // Extract CAB so Windows can find driver files during INF-based install
            string canonDir = Path.GetDirectoryName(canonInfPath);
            string cabPath = Path.Combine(canonDir, "gpps3.cab");
            Log($"  CAB 路径: {cabPath} (存在={File.Exists(cabPath)})");
            if (File.Exists(cabPath))
            {
                Log("  开始解压 gpps3.cab...");
                var (cabExit, cabErr) = RunPowerShellWithError(
                    $"expand \"{cabPath}\" -F:* \"{canonDir}\"; if (-not $?) {{ exit 1 }}");
                Log($"  解压结果: exit={cabExit}, err={cabErr}");
                if (cabExit == 0) Log("  CAB 解压完成");
            }
            else
            {
                Log("  CAB 不存在，跳过解压");
                Log($"  列出 {canonDir} 目录内容:");
                if (Directory.Exists(canonDir))
                {
                    foreach (var f in Directory.GetFiles(canonDir))
                        Log($"    {f}");
                }
            }

            Log("  使用 printui.dll 安装驱动并创建打印机...");
            var (exitCode, errorMsg) = RunPowerShellWithError(@"
$ProgressPreference='SilentlyContinue'
$inf = '" + canonInfPath.Replace("'", "''") + @"'
$printerName = '" + PRINTER_NAME.Replace("'", "''") + @"'
$portName = '" + PORT_NAME.Replace("'", "''") + @"'
$driverName = '" + DRIVER_NAME.Replace("'", "''") + @"'

Start-Sleep -Seconds 2

$p = Start-Process -FilePath 'rundll32' -ArgumentList ""printui.dll,PrintUIEntry /if /b \""$printerName\"" /f \""$inf\"" /r \""$portName\"" /m \""$driverName\"""" -Wait -PassThru -NoNewWindow
if ($p.ExitCode -ne 0) {
    Write-Output ""printui exit code: $($p.ExitCode)""
    exit 1
}
");
            if (exitCode == 0)
            {
                Log("  驱动安装+打印机创建成功");
                return true;
            }

            Log($"  printui.dll 方案失败 (exit={exitCode}): {errorMsg}");
            Log("  尝试 pnputil + Add-Printer 备用方案...");

            RunPowerShell($"pnputil /add-driver \"{canonInfPath}\"");
            var (addExit, addErr) = RunPowerShellWithError(@"
$ProgressPreference='SilentlyContinue'
$ErrorActionPreference = 'Stop'
try {
    Add-PrinterDriver -Name '" + DRIVER_NAME.Replace("'", "''") + @"' -ErrorAction Stop
    Add-Printer -Name '" + PRINTER_NAME.Replace("'", "''") + @"' -DriverName '" + DRIVER_NAME.Replace("'", "''") + @"' -PortName '" + PORT_NAME.Replace("'", "''") + @"' -ErrorAction Stop
}
catch {
    Write-Output $_.Exception.Message
    exit 1
}
");
            if (addExit == 0)
            {
                Log("  Add-Printer 备用方案成功");
                return true;
            }
            Log($"  Add-Printer 备用方案失败 (exit={addExit}): {addErr}");

            return false;
        }

        private void Rollback(bool svc)
        {
            if (svc) RollbackService();
            RunPowerShell($"Get-Printer -Name '{PRINTER_NAME}' -ErrorAction SilentlyContinue | Remove-Printer -ErrorAction SilentlyContinue");
        }

        private void RollbackService()
        {
            RunPowerShell("sc.exe stop " + SERVICE_NAME + " 2>$null");
            System.Threading.Thread.Sleep(1000);
            RunPowerShell("sc.exe delete " + SERVICE_NAME + " 2>$null");
            System.Threading.Thread.Sleep(500);
        }

        private bool InstallService(string exeDir, Action<string> status)
        {
            try
            {
                string serviceExe = Path.Combine(exeDir, "VirtualPrinterService.exe");
                if (!File.Exists(serviceExe))
                {
                    Log($"  VirtualPrinterService.exe 未找到: {serviceExe}");
                    return false;
                }

                if (ServiceExists(SERVICE_NAME))
                {
                    RunPowerShell($"sc.exe stop {SERVICE_NAME} 2>$null");
                    System.Threading.Thread.Sleep(1000);
                    RunPowerShell($"sc.exe delete {SERVICE_NAME} 2>$null");
                    System.Threading.Thread.Sleep(1000);
                }

                if (!RunPowerShell($"sc.exe create {SERVICE_NAME} binPath=\"{serviceExe}\" start=auto"))
                    return false;
                System.Threading.Thread.Sleep(1000);

                if (!RunPowerShell($"sc.exe start {SERVICE_NAME}"))
                    return false;
                System.Threading.Thread.Sleep(2000);

                return IsServiceRunning(SERVICE_NAME);
            }
            catch (Exception ex) { Log($"  InstallService 异常: {ex.Message}"); return false; }
        }

        private static bool IsServiceRunning(string name)
        {
            try { var sc = new ServiceController(name); return sc.Status == ServiceControllerStatus.Running; }
            catch { return false; }
        }

        private static bool ServiceExists(string name)
        {
            try { var sc = new ServiceController(name); var _ = sc.Status; return true; }
            catch { return false; }
        }

        private void OnUninstall(object sender, RoutedEventArgs e)
        {
            try
            {
                RunPowerShell($"sc.exe stop {SERVICE_NAME} 2>$null");
                System.Threading.Thread.Sleep(1000);
                RunPowerShell($"sc.exe delete {SERVICE_NAME} 2>$null");
                RunPowerShell($"Get-Printer -Name '{PRINTER_NAME}' -ErrorAction SilentlyContinue | Remove-Printer -ErrorAction SilentlyContinue");

                if (DeletePortCheck.IsChecked == true)
                {
                    RunPowerShell($"Get-PrinterPort -Name '{PORT_NAME}' -ErrorAction SilentlyContinue | Remove-PrinterPort -ErrorAction SilentlyContinue");
                }
                if (DeleteDriverCheck.IsChecked == true)
                {
                    RunPowerShell($"rundll32 printui.dll,PrintUIEntry /dd /q /m \"{DRIVER_NAME}\" 2>$null");
                }

                MessageBox.Show("卸载成功！", "VirtualPrinter",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"卸载失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnRefresh(object sender, RoutedEventArgs e) => RefreshStatus();

        private void RefreshStatus()
        {
            try
            {
                if (IsPrinterInstalled())
                {
                    PrinterStatus.Text = $"\u25CF {PRINTER_NAME} 已安装";
                    PrinterStatus.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    PrinterStatus.Text = $"\u25CB {PRINTER_NAME} 未安装";
                    PrinterStatus.Foreground = System.Windows.Media.Brushes.Gray;
                }
                PortInfo.Text = $"端口: {PORT_NAME} (VirtualPrinter 监视器端口)";
                DriverInfo.Text = IsPrinterDriverInstalled(DRIVER_NAME)
                    ? $"驱动: {DRIVER_NAME}"
                    : "驱动: 未安装";
                try
                {
                    var sc = new ServiceController(SERVICE_NAME);
                    ServiceInfo.Text = sc.Status == ServiceControllerStatus.Running
                        ? $"服务: {SERVICE_NAME} \u25CF 运行中"
                        : $"服务: {SERVICE_NAME} \u25CB 已停止";
                }
                catch
                {
                    ServiceInfo.Text = $"服务: {SERVICE_NAME} \u25CB 未安装";
                }
                CheckDotNet.Text = CheckDotNetInstalled();
                CheckVC.Text = CheckVCRedist();
                CheckSpooler.Text = CheckSpoolerRunning()
                    ? "\u2713 Print Spooler: 运行中" : "\u2717 Print Spooler: 未运行";
                CheckPS.Text = CheckPSDriverInstalled()
                    ? "\u2713 Canon PS3 驱动: 已安装" : "\u25CB Canon PS3 驱动: 未安装";
                CheckGS.Text = CheckGhostscriptExists()
                    ? "\u2713 Ghostscript: 就绪" : "\u25CB Ghostscript: 未安装";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Refresh failed: {ex.Message}");
            }
        }

        private string CheckDotNetInstalled()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (key != null)
                    {
                        var release = (int)(key.GetValue("Release") ?? 0);
                        if (release >= 528040) return "\u2713 .NET Framework 4.8: 已安装";
                        if (release >= 461808) return "\u2713 .NET Framework 4.7.2: 已安装";
                        return $"\u26A0 .NET Framework: 版本 {release} (需要 4.7.2+)";
                    }
                }
            }
            catch { }
            return "\u26A0 .NET Framework: 无法检测";
        }

        private string CheckVCRedist()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"))
                {
                    if (key != null)
                    {
                        var major = (int)(key.GetValue("Major") ?? 0);
                        if (major >= 14) return "\u2713 VC++ Redist 2015-2022: 已安装";
                    }
                }
            }
            catch { }
            return "\u25CB VC++ Redist: 未安装";
        }

        private bool CheckSpoolerRunning()
        {
            try { var sc = new ServiceController("Spooler"); return sc.Status == ServiceControllerStatus.Running; }
            catch { return false; }
        }

        private bool CheckPSDriverInstalled()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_PrinterDriver WHERE Name LIKE '%{DRIVER_NAME}%'"))
                {
                    foreach (var obj in searcher.Get()) return true;
                }
            }
            catch { }
            return false;
        }

        private bool CheckGhostscriptExists()
        {
            string[] paths =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "gs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "gs"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gs")
            };
            foreach (var dir in paths)
            {
                if (Directory.Exists(dir) &&
                    Directory.GetFiles(dir, "gswin64c.exe", SearchOption.AllDirectories).Length > 0)
                    return true;
            }
            return false;
        }

        private static bool IsPrinterDriverInstalled(string modelName)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_PrinterDriver WHERE Name LIKE '%{modelName}%'"))
                {
                    foreach (var obj in searcher.Get()) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool IsPrinterInstalled()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_Printer WHERE Name = '{PRINTER_NAME}'"))
                {
                    foreach (var obj in searcher.Get()) return true;
                }
            }
            catch { }
            return false;
        }

        private static void Log(string msg)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}";
            Debug.WriteLine(line);
            try
            {
                var dir = Path.GetDirectoryName(LOG_PATH);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(LOG_PATH, line + Environment.NewLine);
            }
            catch { }
        }

        private static bool RunPowerShell(string command)
        {
            try
            {
                var bytes = Encoding.Unicode.GetBytes(command);
                string encoded = Convert.ToBase64String(bytes);
                var psi = new ProcessStartInfo("powershell.exe",
                    $"-NoProfile -NonInteractive -EncodedCommand {encoded}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    bool ok = p?.WaitForExit(120000) == true && p.ExitCode == 0;
                    Log($"  PS: {command} -> exit={p?.ExitCode ?? -1}");
                    return ok;
                }
            }
            catch (Exception ex) { Log($"  PS 异常: {command} -> {ex.Message}"); return false; }
        }

        private static (int ExitCode, string ErrorOutput) RunPowerShellWithError(string command)
        {
            try
            {
                string wrapped = $"try {{ {command} }} catch {{ $e = $_; Write-Output '__PSError__'; Write-Output $e.Exception.Message; exit 1 }}";
                var bytes = Encoding.Unicode.GetBytes(wrapped);
                string encoded = Convert.ToBase64String(bytes);
                var psi = new ProcessStartInfo("powershell.exe",
                    $"-NoProfile -NonInteractive -EncodedCommand {encoded}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return (-1, "Process failed to start");
                    var outputBuilder = new StringBuilder();
                    var errorBuilder = new StringBuilder();
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit(120000);

                    string output = outputBuilder.ToString().Trim();
                    string errors = errorBuilder.ToString().Trim();
                    string mergedError = string.IsNullOrEmpty(errors) ? ExtractErrorFromOutput(output) : errors;

                    Log($"  PS: {command} -> exit={p.ExitCode}");
                    if (!string.IsNullOrEmpty(mergedError))
                        Log($"  PS 错误: {mergedError}");

                    return (p.ExitCode, mergedError);
                }
            }
            catch (Exception ex)
            {
                Log($"  PS 异常: {command} -> {ex.Message}");
                return (-1, ex.Message);
            }
        }

        private static string ExtractErrorFromOutput(string output)
        {
            if (string.IsNullOrEmpty(output)) return "";
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var errorLines = new List<string>();
            bool inError = false;
            foreach (var line in lines)
            {
                if (line.Contains("__PSError__"))
                {
                    inError = true;
                    continue;
                }
                if (inError)
                    errorLines.Add(line);
            }
            return string.Join("; ", errorLines);
        }

        private static string ReadInstalledVersion()
        {
            try
            {
                if (File.Exists(VERSION_FILE))
                    return File.ReadAllText(VERSION_FILE).Trim();
            }
            catch { }
            return null;
        }

        private static void WriteInstalledVersion()
        {
            try
            {
                string dir = Path.GetDirectoryName(VERSION_FILE);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(VERSION_FILE, $"{APP_VERSION}-{TEST_BATCH}");
            }
            catch { }
        }

        private static string RunPowerShellGetOutput(string command)
        {
            try
            {
                var bytes = Encoding.Unicode.GetBytes(command);
                string encoded = Convert.ToBase64String(bytes);
                var psi = new ProcessStartInfo("powershell.exe",
                    $"-NoProfile -NonInteractive -EncodedCommand {encoded}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return "";
                    string stdout = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(30000);
                    return stdout.Trim();
                }
            }
            catch { return ""; }
        }
    }
}
