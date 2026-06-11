using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualPrinter.Service.Services
{
    public class PipeListener
    {
        private readonly JobProcessor _processor;
        private readonly CancellationTokenSource _stopCts = new CancellationTokenSource();
        private Task _listenTask;

        public PipeListener(JobProcessor processor)
        {
            _processor = processor;
        }

        public void Start(CancellationToken token)
        {
            token.Register(() => _stopCts.Cancel());
            _listenTask = Task.Run(() => ListenLoop(_stopCts.Token), _stopCts.Token);
        }

        public void Stop()
        {
            _stopCts.Cancel();
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(
                        "VirtualPrinter",
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances);

                    await pipe.WaitForConnectionAsync(token);

                    // Accept next connection immediately on a new loop iteration
                    _ = Task.Run(() => ProcessConnectionAsync(pipe, token), token);

                    // Don't dispose pipe here - it's now owned by ProcessConnectionAsync
                    pipe = null;
                }
                catch (OperationCanceledException)
                {
                    pipe?.Dispose();
                    break;
                }
                catch (IOException)
                {
                    pipe?.Dispose();
                }
                catch (Exception ex)
                {
                    pipe?.Dispose();
                    System.Diagnostics.EventLog.WriteEntry(
                        "VirtualPrinterService",
                        $"Pipe listener error: {ex.Message}",
                        System.Diagnostics.EventLogEntryType.Warning);
                }
            }
        }

        private async Task ProcessConnectionAsync(NamedPipeServerStream pipe, CancellationToken token)
        {
            try
            {
                using (pipe)
                using (var reader = new StreamReader(pipe, Encoding.Unicode))
                {
                    string json = await reader.ReadToEndAsync();
                    if (!string.IsNullOrEmpty(json))
                    {
                        _processor.Enqueue(json);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (Exception ex)
            {
                System.Diagnostics.EventLog.WriteEntry(
                    "VirtualPrinterService",
                    $"Pipe processing error: {ex.Message}",
                    System.Diagnostics.EventLogEntryType.Warning);
            }
        }
    }
}
