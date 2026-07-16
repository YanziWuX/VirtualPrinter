using System;

namespace VirtualPrinter.Service.Models
{
    public class PrintJob
    {
        public int JobId { get; set; }
        public string TempFile { get; set; }
        public string DocumentName { get; set; }
        public string PrinterName { get; set; }
        public DateTime Timestamp { get; set; }
        public DateTime ReceivedAt { get; set; } = DateTime.Now;
        public bool IsProcessed { get; set; }

        public static PrintJob FromJson(string json)
        {
            try
            {
                var parser = new System.Web.Script.Serialization.JavaScriptSerializer();
                var dict = parser.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json);

                return new PrintJob
                {
                    JobId = int.Parse(dict["JobId"]?.ToString() ?? "0"),
                    TempFile = dict["TempFile"]?.ToString() ?? "",
                    DocumentName = dict["DocumentName"]?.ToString() ?? "Untitled",
                    PrinterName = dict["PrinterName"]?.ToString() ?? "VirtualPrinter",
                    ReceivedAt = DateTime.Now
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
