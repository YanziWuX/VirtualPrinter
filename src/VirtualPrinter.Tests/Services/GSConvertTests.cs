using Xunit;
using VirtualPrinter.GhostLib;
using System.IO;
using System;

namespace VirtualPrinter.Tests.Services
{
    public class GSConvertTests
    {
        [Fact]
        public void Convert_InputFileNotFound_ReturnsError()
        {
            var converter = new GSConvert();
            var opts = new GSConvertOptions();
            var result = converter.Convert("nonexistent.ps", "out.pdf", opts);

            Assert.False(result.Success);
            Assert.Contains("not found", result.ErrorMessage);
        }

        [Fact]
        public void GSConvertOptions_DefaultFormatIsPDF()
        {
            var opts = new GSConvertOptions();
            Assert.Equal(OutputFormat.PDF, opts.Format);
        }

        [Fact]
        public void GSConvertOptions_DefaultResolution300()
        {
            var opts = new GSConvertOptions();
            Assert.Equal(300, opts.Resolution);
        }

        [Fact]
        public void GSConvertOptions_DefaultColorModeRGB()
        {
            var opts = new GSConvertOptions();
            Assert.Equal(ColorMode.RGB, opts.ColorMode);
        }

        [Fact]
        public void GSConvertOptions_DefaultTimeout120()
        {
            var opts = new GSConvertOptions();
            Assert.Equal(120, opts.TimeoutSeconds);
        }

        [Fact]
        public void GSConvertResult_DefaultValues()
        {
            var r = new GSConvertResult();
            Assert.False(r.Success);
            Assert.Null(r.OutputPath);
            Assert.Null(r.ErrorMessage);
            Assert.Equal(0, r.ExitCode);
        }
    }
}
