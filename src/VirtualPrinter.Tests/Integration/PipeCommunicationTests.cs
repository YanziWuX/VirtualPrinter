using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using VirtualPrinter.Service.Models;
using System.Web.Script.Serialization;

namespace VirtualPrinter.Tests.Integration
{
    public class PipeCommunicationTests : IDisposable
    {
        private readonly string _pipeName = $"VirtualPrinter_Test_{Guid.NewGuid():N}";

        [Fact]
        public async Task SendAndReceiveJob_ValidJson_Success()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var json = $"{{\"Action\":\"NewJob\",\"JobId\":1,\"TempFile\":\"C:\\\\test.tmp\",\"DocumentName\":\"doc.ps\",\"PrinterName\":\"VP\"}}";

            var server = Task.Run(async () =>
            {
                using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1);
                await pipe.WaitForConnectionAsync(cts.Token);
                using var reader = new StreamReader(pipe, Encoding.Unicode);
                return await reader.ReadToEndAsync();
            });

            var client = Task.Run(async () =>
            {
                using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                await pipe.ConnectAsync(2000, cts.Token);
                byte[] data = Encoding.Unicode.GetBytes(json);
                await pipe.WriteAsync(data, 0, data.Length);
            });

            await Task.WhenAll(server, client);
            var received = await server;

            Assert.NotNull(received);
            var parser = new JavaScriptSerializer();
            var dict = parser.Deserialize<System.Collections.Generic.Dictionary<string, object>>(received);
            Assert.Equal("NewJob", dict["Action"]);
            Assert.Equal("1", dict["JobId"].ToString());
        }

        [Fact]
        public async Task SendResult_ValidJson_Success()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var resultJson = "{\"JobId\":42,\"OutputPath\":\"C:\\\\out.pdf\",\"Success\":true,\"Error\":null}";

            var server = Task.Run(async () =>
            {
                using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1);
                await pipe.WaitForConnectionAsync(cts.Token);
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                return await reader.ReadToEndAsync();
            });

            var client = Task.Run(async () =>
            {
                using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                await pipe.ConnectAsync(2000, cts.Token);
                byte[] data = Encoding.UTF8.GetBytes(resultJson);
                await pipe.WriteAsync(data, 0, data.Length);
            });

            await Task.WhenAll(server, client);
            var received = await server;

            var serializer = new JavaScriptSerializer();
            var result = serializer.Deserialize<JobResult>(received);
            Assert.Equal(42, result.JobId);
            Assert.True(result.Success);
            Assert.Equal("C:\\out.pdf", result.OutputPath);
        }

        [Fact]
        public async Task SendResult_Failure_Success()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var resultJson = "{\"JobId\":7,\"OutputPath\":null,\"Success\":false,\"Error\":\"Conversion failed\"}";

            var server = Task.Run(async () =>
            {
                using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1);
                await pipe.WaitForConnectionAsync(cts.Token);
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                return await reader.ReadToEndAsync();
            });

            var client = Task.Run(async () =>
            {
                using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                await pipe.ConnectAsync(2000, cts.Token);
                byte[] data = Encoding.UTF8.GetBytes(resultJson);
                await pipe.WriteAsync(data, 0, data.Length);
            });

            await Task.WhenAll(server, client);
            var received = await server;

            var serializer = new JavaScriptSerializer();
            var result = serializer.Deserialize<JobResult>(received);
            Assert.Equal(7, result.JobId);
            Assert.False(result.Success);
            Assert.Equal("Conversion failed", result.Error);
        }

        [Fact]
        public void ClientTimeout_NoServer_Throws()
        {
            using var pipe = new NamedPipeClientStream(".", _pipeName + "_nonexistent", PipeDirection.Out);
            Assert.Throws<TimeoutException>(() => pipe.Connect(200));
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
