using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace VirtualPrinter.SaveDialog.Services
{
    public static class ServiceClient
    {
        public static async Task SendResultAsync(int jobId, string outputPath, bool success, string error)
        {
            try
            {
                string json = $"{{\"Action\":\"JobResult\",\"JobId\":{jobId}," +
                    $"\"OutputPath\":\"{JsonEscape(outputPath ?? "")}\",\"Success\":{success.ToString().ToLower()}," +
                    $"\"Error\":\"{JsonEscape(error ?? "")}\"}}";

                using (var pipe = new NamedPipeClientStream(".", "VirtualPrinterResult",
                    PipeDirection.Out, PipeOptions.Asynchronous))
                {
                    await pipe.ConnectAsync(300);
                    byte[] data = Encoding.UTF8.GetBytes(json);

                    var write = pipe.WriteAsync(data, 0, data.Length);
                    var timeout = Task.Delay(500);
                    await Task.WhenAny(write, timeout);
                }
            }
            catch
            {
                // Service may not be listening for results
            }
        }

        private static string JsonEscape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }
    }
}
