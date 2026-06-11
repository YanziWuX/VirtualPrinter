using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Reflection;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.RegularExpressions;
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
        private const string DRIVER_NAME = "Microsoft PS Class Driver";
        private const string PORT_NAME = @"C:\Temp\VPPrint\output.prn";
        private const string SERVICE_NAME = "VirtualPrinterService";
        private const string OUTPUT_DIR = @"C:\Temp\VPPrint";
        private const string JOBS_DIR = @"C:\Temp\VPPrint\jobs";

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
                AdminStatus.Text = "✓ 管理员模式";
                AdminStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
            }
            else
            {
                AdminStatus.Text = "○ 非管理员模式 - 部分功能受限";
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

                // Step 2: Add local port
                Log("Step 2: 添加本地端口...");
                status("正在添加打印端口...");
                bool portOk = await Task.Run(() =>
                {
                    var port = RunPowerShellGetOutput($"Get-PrinterPort -Name '{PORT_NAME}' -ErrorAction SilentlyContinue");
                    if (!string.IsNullOrEmpty(port)) return true;
                    var result = RunPowerShell($"Add-PrinterPort -Name '{PORT_NAME}' -ErrorAction Stop");
                    return result;
                });
                if (!portOk) throw new Exception("端口创建失败");
                Log("  端口已创建");

                // Step 3: Install service
                Log("Step 3: 安装 Windows 服务...");
                status("正在安装 Windows 服务...");
                svcOk = await Task.Run(() => InstallService(exeDir, status));
                Log($"  服务: svcOk={svcOk}");
                if (!svcOk) throw new Exception("Windows 服务安装失败");

                // Step 4: Create printer
                Log("Step 4: 创建打印机...");
                status("正在创建打印机...");
                bool printerOk = await Task.Run(() =>
                {
                    var driver = RunPowerShellGetOutput($"Get-PrinterDriver -Name '{DRIVER_NAME}' -ErrorAction SilentlyContinue");
                    if (string.IsNullOrEmpty(driver))
                    {
                        Log("  PS Class Driver 未安装，尝试安装...");
                        RunPowerShell($"rundll32 printui.dll,PrintUIEntry /ia /m \"{DRIVER_NAME}\"");
                        System.Threading.Thread.Sleep(5000);
                    }
                    var existing = RunPowerShellGetOutput($"Get-Printer -Name '{PRINTER_NAME}' -ErrorAction SilentlyContinue");
                    if (!string.IsNullOrEmpty(existing))
                        RunPowerShell($"Remove-Printer -Name '{PRINTER_NAME}' -ErrorAction SilentlyContinue");

                    var result = RunPowerShell(
                        $"Add-Printer -Name '{PRINTER_NAME}' -DriverName '{DRIVER_NAME}' -PortName '{PORT_NAME}' -ErrorAction Stop");
                    return result;
                });
                if (!printerOk) throw new Exception("打印机创建失败");

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
                    PrinterStatus.Text = $"● {PRINTER_NAME} 已安装";
                    PrinterStatus.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    PrinterStatus.Text = $"○ {PRINTER_NAME} 未安装";
                    PrinterStatus.Foreground = System.Windows.Media.Brushes.Gray;
                }
                PortInfo.Text = $"端口: {PORT_NAME} (文件端口)";
                DriverInfo.Text = IsPrinterDriverInstalled(DRIVER_NAME)
                    ? $"驱动: {DRIVER_NAME}"
                    : "驱动: 未安装";
                try
                {
                    var sc = new ServiceController(SERVICE_NAME);
                    ServiceInfo.Text = sc.Status == ServiceControllerStatus.Running
                        ? $"服务: {SERVICE_NAME} ● 运行中"
                        : $"服务: {SERVICE_NAME} ○ 已停止";
                }
                catch
                {
                    ServiceInfo.Text = $"服务: {SERVICE_NAME} ○ 未安装";
                }
                CheckDotNet.Text = CheckDotNetInstalled();
                CheckVC.Text = CheckVCRedist();
                CheckSpooler.Text = CheckSpoolerRunning()
                    ? "✓ Print Spooler: 运行中" : "✗ Print Spooler: 未运行";
                CheckPS.Text = CheckPSDriverInstalled()
                    ? "✓ PS Class Driver: 已安装" : "○ PS Class Driver: 未安装";
                CheckGS.Text = CheckGhostscriptExists()
                    ? "✓ Ghostscript: 就绪" : "○ Ghostscript: 未安装";
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
                        if (release >= 528040) return "✓ .NET Framework 4.8: 已安装";
                        if (release >= 461808) return "✓ .NET Framework 4.7.2: 已安装";
                        return $"⚠ .NET Framework: 版本 {release} (需要 4.7.2+)";
                    }
                }
            }
            catch { }
            return "⚠ .NET Framework: 无法检测";
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
                        if (major >= 14) return "✓ VC++ Redist 2015-2022: 已安装";
                    }
                }
            }
            catch { }
            return "○ VC++ Redist: 未安装";
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
                var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_PrinterDriver WHERE Name LIKE '%{DRIVER_NAME}%'");
                foreach (var obj in searcher.Get()) return true;
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
                var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_PrinterDriver WHERE Name LIKE '%{modelName}%'");
                foreach (var obj in searcher.Get()) return true;
            }
            catch { }
            return false;
        }

        private static bool IsPrinterInstalled()
        {
            try
            {
                var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_Printer WHERE Name = '{PRINTER_NAME}'");
                foreach (var obj in searcher.Get()) return true;
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
                var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -Command \"{command}\"")
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

        private static string RunPowerShellGetOutput(string command)
        {
            try
            {
                var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -Command \"{command}\"")
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
