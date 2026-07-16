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

            string tempDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VirtualPrinter");
            Directory.CreateDirectory(tempDir);
            string jobsDir = Path.Combine(tempDir, "jobs");
            Directory.CreateDirectory(jobsDir);

            // Clean stale .tmp files from previous session before accepting new jobs
            CleanupStaleTempFiles(tempDir, jobsDir);

            _config = new ConfigManager();
            _jobProcessor = new JobProcessor(_config);
            _jobProcessor.OnJobCompleted = OnJobCompleted;
            _pipeListener = new PipeListener(_jobProcessor);
            _resultPipeListener = new ResultPipeListener(OnJobResult);

            Task.Run(() => _pipeListener.Start(_cts.Token), _cts.Token);
            Task.Run(() => _resultPipeListener.Start(_cts.Token), _cts.Token);

            _tempFileManager = new TempFileManager(TimeSpan.FromMinutes(15));
            _tempFileManager.Cleanup();

            EventLog.WriteEntry("VirtualPrinter Service started, listening on \\\\.\\pipe\\VirtualPrinter", EventLogEntryType.Information);
        }

        private static void CleanupStaleTempFiles(string tempDir, string jobsDir)
        {
            try
            {
                foreach (var dir in new[] { tempDir, jobsDir })
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir, "*.tmp"))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
            }
            catch { }
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
        }

        private void OnJobResult(Models.JobResult result)
        {
            _jobProcessor.OnJobResult(result);
            EventLog.WriteEntry($"Job result: JobId={result.JobId}, Success={result.Success}", EventLogEntryType.Information);
        }

        private void OnJobCompleted(Models.PrintJob job)
        {
            EventLog.WriteEntry($"Job completed: JobId={job.JobId}", EventLogEntryType.Information);
        }
    }
}
