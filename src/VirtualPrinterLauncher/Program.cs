using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Principal;
using System.Windows;

namespace VirtualPrinter.Launcher
{
    public static class Program
    {
        private static readonly string TargetDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VirtualPrinter");

        [STAThread]
        public static void Main()
        {
            if (!IsAdmin())
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Assembly.GetExecutingAssembly().Location,
                    Verb = "runas",
                    UseShellExecute = true
                };
                try { Process.Start(psi); } catch { }
                return;
            }

            KillManagerProcesses();

            try
            {
                Directory.CreateDirectory(TargetDir);
                using var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("bundle.zip");
                if (stream == null)
                {
                    RunManager();
                    return;
                }
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                foreach (var entry in archive.Entries)
                {
                    var dest = Path.GetFullPath(Path.Combine(TargetDir, entry.FullName));
                    if (!dest.StartsWith(TargetDir, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (entry.Name == "")
                    {
                        Directory.CreateDirectory(dest);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    try
                    {
                        entry.ExtractToFile(dest, overwrite: true);
                    }
                    catch (IOException) when (entry.Name == "VirtualPrinterManager.exe")
                    {
                        // File might still be locked; rename old file first
                        string backup = dest + ".old";
                        try
                        {
                            if (File.Exists(backup)) File.Delete(backup);
                            File.Move(dest, backup);
                            entry.ExtractToFile(dest, overwrite: true);
                        }
                        catch { }
                    }
                }
            }
            catch
            {
            }

            RunManager();
        }

        private static void KillManagerProcesses()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("VirtualPrinterManager"))
                {
                    if (!p.HasExited)
                    {
                        p.Kill();
                        p.WaitForExit(5000);
                    }
                }
            }
            catch { }
        }

        private static void RunManager()
        {
            var mgr = Path.Combine(TargetDir, "VirtualPrinterManager.exe");
            if (File.Exists(mgr))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = mgr,
                    UseShellExecute = false
                });
            }
        }

        private static bool IsAdmin()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
