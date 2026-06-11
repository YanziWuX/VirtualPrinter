using Xunit;
using VirtualPrinter.Service.Utils;

namespace VirtualPrinter.Tests.Utils
{
    public class FileNamingEngineTests
    {
        [Fact]
        public void GenerateFileName_BasicRule()
        {
            string result = FileNamingEngine.GenerateFileName("{DocumentName}_{DateTime}.{Format}", "test.ps", 1, "pdf");
            Assert.Contains("test", result);
            Assert.EndsWith("pdf", result);
        }

        [Fact]
        public void GenerateFileName_OnlyDocumentName()
        {
            string result = FileNamingEngine.GenerateFileName("{DocumentName}", "my-doc.ps", 42, "png");
            Assert.Equal("my-doc", result);
        }

        [Fact]
        public void GenerateFileName_WithJobId()
        {
            string result = FileNamingEngine.GenerateFileName("job_{JobId}", "doc", 99, "jpg");
            Assert.Equal("job_99", result);
        }

        [Fact]
        public void GenerateFileName_NullDocumentName_UsesUntitled()
        {
            string result = FileNamingEngine.GenerateFileName("{DocumentName}", null, 1, "pdf");
            Assert.Equal("Untitled", result);
        }

        [Fact]
        public void GenerateFileName_EmptyDocumentName_UsesUntitled()
        {
            string result = FileNamingEngine.GenerateFileName("{DocumentName}", "", 1, "pdf");
            Assert.Equal("Untitled", result);
        }

        [Fact]
        public void GenerateFileName_SanitizesInvalidChars()
        {
            string result = FileNamingEngine.GenerateFileName("{DocumentName}", "file:test?", 1, "pdf");
            Assert.DoesNotContain(":", result);
            Assert.DoesNotContain("?", result);
        }

        [Fact]
        public void GenerateFileName_TemplateWithNoPlaceholders()
        {
            string result = FileNamingEngine.GenerateFileName("output", "doc", 1, "pdf");
            Assert.Equal("output", result);
        }

        [Fact]
        public void GenerateFileName_AllPlaceholders()
        {
            string result = FileNamingEngine.GenerateFileName("{DocumentName}_{Date}_{Time}_{JobId}", "doc", 42, "tiff");
            Assert.StartsWith("doc_", result);
            Assert.Contains("42", result);
        }
    }
}
