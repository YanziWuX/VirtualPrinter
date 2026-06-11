namespace VirtualPrinter.Service.Models
{
    public class Settings
    {
        public string DefaultFormat { get; set; } = "pdf";
        public int Resolution { get; set; } = 300;
        public string ColorMode { get; set; } = "rgb";
        public string SaveFolder { get; set; } = "";
        public string FileNamingRule { get; set; } = "{DocumentName}_{DateTime}";
        public int JpegQuality { get; set; } = 85;
        public bool OpenFolderAfterPrint { get; set; } = true;
        public bool PngTransparency { get; set; } = false;
        public string WatermarkText { get; set; } = "Confidential";
        public int MaxHistoryCount { get; set; } = 100;
    }
}
