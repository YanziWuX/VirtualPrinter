using System;
using System.Diagnostics;
using System.ServiceProcess;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VirtualPrinter.Service.Services;
using VirtualPrinter.Service.Utils;

namespace VirtualPrinter.Service
{
    public class MainService : ServiceBase
    {
        private PipeListener _pipeListener;
        private ResultPipeListener _resultPipeListener;
        private JobProcessor _jobProcessor;
        private ConfigManager _config;
        private CancellationTokenSource _cts;
        private TempFileManager _tempFileManager;
        private FileSystemWatcher _fileWatcher;
        private DateTime _lastWriteTime;
        private bool _jobInProgress;
        private readonly object _debounceLock = new object();
        private readonly object _jobGateLock = new object();

        public MainService()
        {
            ServiceName = "VirtualPrinterService";
            CanStop = true;
            CanShutdown = true;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            _cts = new CancellationTokenSource();
            _config = new ConfigManager();
            _jobProcessor = new JobProcessor(_config);
            _pipeListener = new PipeListener(_jobProcessor);
            _resultPipeListener = new ResultPipeListener(OnJobResult);

            Task.Run(() => _pipeListener.Start(_cts.Token), _cts.Token);
            Task.Run(() => _resultPipeListener.Start(_cts.Token), _cts.Token);

            string tempDir = Path.Combine(Path.GetTempPath(), "VirtualPrinter");
            Directory.CreateDirectory(tempDir);

            _tempFileManager = new TempFileManager(TimeSpan.FromMinutes(15));

            StartFileWatcher();

            EventLog.WriteEntry("VirtualPrinter Service started successfully", EventLogEntryType.Information);
        }

        protected override void OnStop()
        {
            _cts?.Cancel();
            _pipeListener?.Stop();
            _resultPipeListener?.Stop();
            _tempFileManager?.Dispose();
            EventLog.WriteEntry("VirtualPrinter Service stopped", EventLogEntryType.Information);
        }

        protected override void OnShutdown()
        {
            _cts?.Cancel();
            _pipeListener?.Stop();
            _resultPipeListener?.Stop();
            _tempFileManager?.Dispose();
            StopFileWatcher();
        }

        private void StartFileWatcher()
        {
            string watchDir = @"C:\Temp\VPPrint";
            string watchFile = "output.prn";
            Directory.CreateDirectory(watchDir);

            _fileWatcher = new FileSystemWatcher(watchDir, watchFile)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _fileWatcher.Changed += OnOutputFileChanged;
            EventLog.WriteEntry($"FileWatcher started: {watchDir}\\{watchFile}", EventLogEntryType.Information);
        }

        private void StopFileWatcher()
        {
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }
        }

        private void OnJobResult(Models.JobResult result)
        {
            _jobProcessor.OnJobResult(result);
            lock (_jobGateLock)
            {
                _jobInProgress = false;
                EventLog.WriteEntry($"Job gate cleared for JobId={result.JobId}", EventLogEntryType.Information);
            }
        }

        private void OnOutputFileChanged(object sender, FileSystemEventArgs e)
        {
            lock (_debounceLock)
            {
                var now = DateTime.Now;
                if ((now - _lastWriteTime).TotalMilliseconds < 1500)
                {
                    _lastWriteTime = now;
                    return;
                }
                _lastWriteTime = now;
            }

            lock (_jobGateLock)
            {
                if (_jobInProgress)
                {
                    EventLog.WriteEntry("FileWatcher: job in progress, ignoring event", EventLogEntryType.Information);
                    _lastWriteTime = DateTime.Now;
                    return;
                }
                _jobInProgress = true;
            }

            try
            {
                _fileWatcher.EnableRaisingEvents = false;
                try
                {
                    System.Threading.Thread.Sleep(800);

                    // Pass the output.prn path directly — dialog will copy it on load
                    var job = new Models.PrintJob
                    {
                        JobId = (int)(DateTime.Now.Ticks % int.MaxValue),
                        TempFile = e.FullPath,
                        DocumentName = "Print Job",
                        PrinterName = "VirtualPrinter",
                        ReceivedAt = DateTime.Now
                    };

                    _jobProcessor.Enqueue(job);
                    EventLog.WriteEntry($"FileWatcher: job queued from {e.FullPath}, JobId={job.JobId}", EventLogEntryType.Information);
                }
                finally
                {
                    _fileWatcher.EnableRaisingEvents = true;
                }
            }
            catch (Exception ex)
            {
                lock (_jobGateLock)
                {
                    _jobInProgress = false;
                }
                EventLog.WriteEntry($"FileWatcher error: {ex.Message}", EventLogEntryType.Warning);
            }
        }
    }
}
