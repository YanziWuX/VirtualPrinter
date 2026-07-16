using Xunit;
using VirtualPrinter.Service.Models;

namespace VirtualPrinter.Tests.Models
{
    public class SettingsTests
    {
        [Fact]
        public void Settings_DefaultValues()
        {
            var s = new Settings();
            Assert.Equal("pdf", s.DefaultFormat);
            Assert.Equal(300, s.Resolution);
            Assert.Equal("rgb", s.ColorMode);
            Assert.Equal("{DocumentName}_{DateTime}", s.FileNamingRule);
            Assert.Equal(85, s.JpegQuality);
            Assert.True(s.OpenFolderAfterPrint);
            Assert.Equal("Confidential", s.WatermarkText);
            Assert.Equal(100, s.MaxHistoryCount);
        }

        [Fact]
        public void Settings_SaveFolderDefaultEmpty()
        {
            var s = new Settings();
            Assert.Equal("", s.SaveFolder);
        }
    }
}
