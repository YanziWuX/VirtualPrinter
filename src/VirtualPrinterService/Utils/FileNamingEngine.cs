using System;
using System.IO;
using System.Text.RegularExpressions;

namespace VirtualPrinter.Service.Utils
{
    public static class FileNamingEngine
    {
        public static string GenerateFileName(string rule, string documentName, int jobId, string format)
        {
            string name = string.IsNullOrEmpty(documentName)
                ? "Untitled"
                : Path.GetFileNameWithoutExtension(documentName);

            string safeName = SanitizeFileName(name);
            string now = DateTime.Now.ToString("yyyyMMdd");
            string time = DateTime.Now.ToString("HHmmss");
            string dateTime = now + "_" + time;

            var result = rule
                .Replace("{DocumentName}", safeName)
                .Replace("{Date}", now)
                .Replace("{Time}", time)
                .Replace("{DateTime}", dateTime)
                .Replace("{JobId}", jobId.ToString())
                .Replace("{Format}", format);

            return SanitizeFileName(result);
        }

        private static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string sanitized = Regex.Replace(name, "[" + Regex.Escape(new string(invalid)) + "]", "_");
            sanitized = sanitized.TrimEnd('.');
            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = "output";
            return sanitized;
        }
    }
}
