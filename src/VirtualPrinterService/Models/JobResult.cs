namespace VirtualPrinter.Service.Models
{
    public class JobResult
    {
        public int JobId { get; set; }
        public string OutputPath { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
    }
}
