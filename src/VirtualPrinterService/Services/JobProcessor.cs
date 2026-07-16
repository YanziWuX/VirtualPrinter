using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VirtualPrinter.Service.Models;
using VirtualPrinter.Service.Utils;

namespace VirtualPrinter.Service.Services
{
    public class JobProcessor
    {
        private readonly ConcurrentQueue<PrintJob> _queue;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JobResult>> _pendingJobs;
        private readonly ConfigManager _config;
        private CancellationTokenSource _cts;
        private Task _processingTask;
        private readonly DateTime _startupTime;
        private static readonly TimeSpan StartupQuietPeriod = TimeSpan.FromSeconds(15);

        public Action<PrintJob> OnJobCompleted { get; set; }

        public JobProcessor(ConfigManager config)
        {
            _config = config;
            _queue = new ConcurrentQueue<PrintJob>();
            _pendingJobs = new ConcurrentDictionary<int, TaskCompletionSource<JobResult>>();
            _cts = new CancellationTokenSource();
            _processingTask = Task.Run(() => ProcessLoop(_cts.Token));
            _startupTime = DateTime.UtcNow;
        }

        public void Enqueue(string json)
        {
            var job = PrintJob.FromJson(json);
            if (job != null)
            {
                Enqueue(job);
            }
        }

        public void Enqueue(PrintJob job)
        {
            if (job != null)
            {
                _queue.Enqueue(job);
                System.Diagnostics.EventLog.WriteEntry(
                    "VirtualPrinterService",
                    $"Job queued: {job.DocumentName} (JobId: {job.JobId})",
                    System.Diagnostics.EventLogEntryType.Information);
            }
        }

        public void OnJobResult(JobResult result)
        {
            if (_pendingJobs.TryRemove(result.JobId, out var tcs))
            {
                tcs.TrySetResult(result);
            }
        }

        private async Task ProcessLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (_queue.TryDequeue(out var job))
                {
                    var jobCopy = job;
                    try
                    {
                        _ = Task.Run(() => ProcessJob(jobCopy, token), token);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.EventLog.WriteEntry(
                            "VirtualPrinterService",
                            $"Job processing failed: {jobCopy.DocumentName}, Error: {ex.Message}",
                            System.Diagnostics.EventLogEntryType.Error);
                    }
                }
                else
                {
                    await Task.Delay(500, token);
                }
            }
        }

        private async Task ProcessJob(PrintJob job, CancellationToken token)
        {
            if (!File.Exists(job.TempFile))
            {
                System.Diagnostics.EventLog.WriteEntry(
                    "VirtualPrinterService",
                    $"Temp file not found: {job.TempFile}",
                    System.Diagnostics.EventLogEntryType.Error);
                OnJobCompleted?.Invoke(job);
                return;
            }

            // Quiet period after startup: spooler may be re-processing old jobs
            // Silently discard them without showing a save dialog
            if (DateTime.UtcNow - _startupTime < StartupQuietPeriod)
            {
                System.Diagnostics.EventLog.WriteEntry(
                    "VirtualPrinterService",
                    $"Startup quiet period: silently discarding re-processed job {job.JobId} ({job.DocumentName})",
                    System.Diagnostics.EventLogEntryType.Information);
                CleanupTempFile(job.TempFile);
                return;
            }

            var tcs = new TaskCompletionSource<JobResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pendingJobs.TryAdd(job.JobId, tcs))
            {
                System.Diagnostics.EventLog.WriteEntry(
                    "VirtualPrinterService",
                    $"Job {job.JobId}: duplicate JobId detected, overwriting pending TCS",
                    System.Diagnostics.EventLogEntryType.Warning);
                _pendingJobs[job.JobId] = tcs;
            }

            bool launched = LaunchSaveDialog(job);

            if (!launched)
            {
                _pendingJobs.TryRemove(job.JobId, out _);
                tcs.TrySetResult(new JobResult
                {
                    JobId = job.JobId,
                    Success = false,
                    Error = "Failed to launch save dialog"
                });
                job.IsProcessed = true;
                OnJobCompleted?.Invoke(job);
                // Temp file cleanup handled by dialog; if orphaned, TempFileManager will get it
                return;
            }

            var timeout = TimeSpan.FromMinutes(12);
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout, token));

            _pendingJobs.TryRemove(job.JobId, out _);

            if (completed == tcs.Task)
            {
                var result = await tcs.Task;
                System.Diagnostics.EventLog.WriteEntry(
                    "VirtualPrinterService",
                    $"Job {job.JobId} completed: success={result.Success}, output={result.OutputPath}",
                    result.Success
                        ? System.Diagnostics.EventLogEntryType.Information
                        : System.Diagnostics.EventLogEntryType.Warning);
            }
            else
            {
                System.Diagnostics.EventLog.WriteEntry(
                    "VirtualPrinterService",
                    $"Job {job.JobId} timed out waiting for save dialog response",
                    System.Diagnostics.EventLogEntryType.Warning);
            }

            job.IsProcessed = true;
            OnJobCompleted?.Invoke(job);
            // PS file cleanup is handled by the save dialog on close/print.
            // .pending file no longer exists (renamed to .ps by dialog).
            // Orphaned files are cleaned by TempFileManager.
        }

        private static void CleanupTempFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        private bool LaunchSaveDialog(PrintJob job)
        {
            string exePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "VirtualPrinterSaveDialog.exe");

            if (!File.Exists(exePath))
            {
                System.Diagnostics.EventLog.WriteEntry(
                    "VirtualPrinterService",
                    $"Save dialog not found: {exePath}",
                    System.Diagnostics.EventLogEntryType.Error);
                return false;
            }

            string args = $"\"{job.TempFile}\" \"{job.DocumentName}\" {job.JobId}";

            bool launched = false;
            try
            {
                launched = SessionLauncher.LaunchInActiveSession(exePath, args);
            }
            catch (Exception ex)
            {
                System.Diagnostics.EventLog.WriteEntry(
                    "VirtualPrinterService",
                    $"SessionLauncher threw: {ex.Message}, trying fallback",
                    System.Diagnostics.EventLogEntryType.Warning);
            }

            if (launched)
            {
                System.Diagnostics.EventLog.WriteEntry(
                    "VirtualPrinterService",
                    $"Save dialog launched via SessionLauncher: JobId={job.JobId}",
                    System.Diagnostics.EventLogEntryType.Information);
                return true;
            }

            System.Diagnostics.EventLog.WriteEntry(
                "VirtualPrinterService",
                $"SessionLauncher failed, trying fallback Process.Start for JobId={job.JobId}",
                System.Diagnostics.EventLogEntryType.Warning);

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exePath, args)
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                };
                var proc = Process.Start(psi);

                if (proc != null && !proc.HasExited)
                {
                    System.Diagnostics.EventLog.WriteEntry(
                        "VirtualPrinterService",
                        $"Save dialog launched via fallback: JobId={job.JobId}, PID={proc.Id}",
                        System.Diagnostics.EventLogEntryType.Information);
                    proc.Dispose();
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.EventLog.WriteEntry(
                    "VirtualPrinterService",
                    $"Fallback launch failed: {ex.Message}",
                    System.Diagnostics.EventLogEntryType.Error);
            }

            System.Diagnostics.EventLog.WriteEntry(
                "VirtualPrinterService",
                $"All launch methods failed for JobId={job.JobId}",
                System.Diagnostics.EventLogEntryType.Error);
            return false;
        }
    }
}
