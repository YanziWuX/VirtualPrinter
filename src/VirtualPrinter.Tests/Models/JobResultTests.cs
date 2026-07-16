using Xunit;
using VirtualPrinter.Service.Models;

namespace VirtualPrinter.Tests.Models
{
    public class JobResultTests
    {
        [Fact]
        public void JobResult_DefaultValues()
        {
            var r = new JobResult();
            Assert.Equal(0, r.JobId);
            Assert.Null(r.OutputPath);
            Assert.False(r.Success);
            Assert.Null(r.Error);
        }

        [Fact]
        public void JobResult_SetProperties()
        {
            var r = new JobResult
            {
                JobId = 1,
                OutputPath = "C:\\out.pdf",
                Success = true,
                Error = null
            };
            Assert.Equal(1, r.JobId);
            Assert.Equal("C:\\out.pdf", r.OutputPath);
            Assert.True(r.Success);
            Assert.Null(r.Error);
        }

        [Fact]
        public void JobResult_Failure()
        {
            var r = new JobResult
            {
                JobId = 2,
                Success = false,
                Error = "GS conversion failed"
            };
            Assert.False(r.Success);
            Assert.Equal("GS conversion failed", r.Error);
        }
    }
}
