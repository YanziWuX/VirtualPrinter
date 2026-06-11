using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using VirtualPrinter.Service.Models;

namespace VirtualPrinter.Service.Services
{
    public class ResultPipeListener
    {
        private readonly Action<JobResult> _onResult;
        private readonly CancellationTokenSource _stopCts = new CancellationTokenSource();
        private Task _listenTask;

        public ResultPipeListener(Action<JobResult> onResult)
        {
            _onResult = onResult;
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
                        "VirtualPrinterResult",
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances);

                    await pipe.WaitForConnectionAsync(token);

                    // Accept next connection immediately on a new loop iteration
                    _ = Task.Run(() => ProcessConnectionAsync(pipe, token), token);

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
                        $"Result pipe listener error: {ex.Message}",
                        System.Diagnostics.EventLogEntryType.Warning);
                }
            }
        }

        private async Task ProcessConnectionAsync(NamedPipeServerStream pipe, CancellationToken token)
        {
            try
            {
                using (pipe)
                using (var reader = new StreamReader(pipe, Encoding.UTF8))
                {
                    string json = await reader.ReadToEndAsync();
                    if (!string.IsNullOrEmpty(json))
                    {
                        var serializer = new JavaScriptSerializer();
                        var result = serializer.Deserialize<JobResult>(json);
                        _onResult?.Invoke(result);
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
                    $"Result pipe processing error: {ex.Message}",
                    System.Diagnostics.EventLogEntryType.Warning);
            }
        }
    }
}
