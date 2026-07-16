using System;
using Xunit;
using VirtualPrinter.Service.Models;

namespace VirtualPrinter.Tests.Models
{
    public class PrintJobTests
    {
        [Fact]
        public void FromJson_ValidJson_ReturnsPrintJob()
        {
            string json = "{\"Action\":\"NewJob\",\"JobId\":42,\"TempFile\":\"C:\\\\Temp\\\\VirtualPrinter\\\\42.tmp\",\"DocumentName\":\"test.ps\",\"PrinterName\":\"VP\"}";
            var job = PrintJob.FromJson(json);

            Assert.NotNull(job);
            Assert.Equal(42, job.JobId);
            Assert.Equal("C:\\Temp\\VirtualPrinter\\42.tmp", job.TempFile);
            Assert.Equal("test.ps", job.DocumentName);
            Assert.Equal("VP", job.PrinterName);
        }

        [Fact]
        public void FromJson_MissingFields_ReturnsNull()
        {
            string json = "{\"Action\":\"NewJob\"}";
            var job = PrintJob.FromJson(json);
            Assert.Null(job);
        }

        [Fact]
        public void FromJson_InvalidJson_ReturnsNull()
        {
            var job = PrintJob.FromJson("not json");
            Assert.Null(job);
        }

        [Fact]
        public void FromJson_NullInput_ReturnsNull()
        {
            var job = PrintJob.FromJson(null);
            Assert.Null(job);
        }

        [Fact]
        public void PrintJob_DefaultIsProcessedFalse()
        {
            var job = new PrintJob();
            Assert.False(job.IsProcessed);
        }

        [Fact]
        public void PrintJob_ReceivedAtSetOnCreation()
        {
            var before = DateTime.Now.AddSeconds(-1);
            var job = new PrintJob();
            var after = DateTime.Now.AddSeconds(1);
            Assert.InRange(job.ReceivedAt, before, after);
        }
    }
}
