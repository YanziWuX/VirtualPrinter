using System;
using System.IO;
using System.Timers;

namespace VirtualPrinter.Service.Utils
{
    public class TempFileManager : IDisposable
    {
        private readonly string[] _scanDirs;
        private readonly Timer _cleanupTimer;
        private readonly TimeSpan _maxAge;

        public TempFileManager(TimeSpan maxAge)
        {
            _maxAge = maxAge;
            string tempDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VirtualPrinter");
            string jobsDir = Path.Combine(tempDir, "jobs");
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(jobsDir);
            _scanDirs = new[] { tempDir, jobsDir };

            _cleanupTimer = new Timer(TimeSpan.FromMinutes(5).TotalMilliseconds);
            _cleanupTimer.Elapsed += (s, e) => Cleanup();
            _cleanupTimer.Start();
        }

        public void Cleanup()
        {
            try
            {
                foreach (var dir in _scanDirs)
                {
                    if (!Directory.Exists(dir)) continue;

                    foreach (var file in Directory.GetFiles(dir, "*.ps"))
                    {
                        var info = new FileInfo(file);
                        if (DateTime.Now - info.LastWriteTime > _maxAge)
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }

                    foreach (var file in Directory.GetFiles(dir, "*.pending"))
                    {
                        var info = new FileInfo(file);
                        if (DateTime.Now - info.LastWriteTime > _maxAge)
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }

                    foreach (var file in Directory.GetFiles(dir, "*.tmp"))
                    {
                        var info = new FileInfo(file);
                        if (DateTime.Now - info.LastWriteTime > _maxAge)
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }
    }
}
