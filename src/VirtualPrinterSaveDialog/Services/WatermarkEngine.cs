using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace VirtualPrinter.SaveDialog.Services
{
    public static class WatermarkEngine
    {
        public static void Apply(string filePath, string text, string position, int opacity)
        {
            if (string.IsNullOrEmpty(text) || !File.Exists(filePath))
                return;

            string ext = Path.GetExtension(filePath).ToLower();

            switch (ext)
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".bmp":
                case ".tif":
                case ".tiff":
                    ApplyImageWatermark(filePath, text, position, opacity);
                    break;
                case ".pdf":
                    ApplyPdfWatermark(filePath, text, position, opacity);
                    break;
            }
        }

    private static void ApplyImageWatermark(string filePath, string text, string position, int opacity)
    {
        string ext = Path.GetExtension(filePath).ToLower();

        byte[] imageBytes = File.ReadAllBytes(filePath);
        using (var ms = new MemoryStream(imageBytes))
        using (var image = Image.FromStream(ms))
        using (var bitmap = new Bitmap(image.Width, image.Height))
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, 0, 0, image.Width, image.Height);

            int alpha = opacity * 255 / 100;
            float fontSize = Math.Max(12f, Math.Min(image.Width, image.Height) / 25f);
            fontSize = Math.Min(fontSize, 72f);

            using (var font = new Font("Arial", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(Color.FromArgb(alpha, 128, 128, 128)))
            using (var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            {
                var bounds = GetPositionBounds(position, image.Width, image.Height);
                g.DrawString(text, font, brush, bounds, sf);
            }

            bitmap.Save(filePath, GetImageFormat(ext));
        }
    }

        private static void ApplyPdfWatermark(string filePath, string text, string position, int opacity)
        {
            string gsExe = FindGsExecutable();
            if (gsExe == null) return;

            string dir = Path.GetDirectoryName(filePath);
            string name = Path.GetFileNameWithoutExtension(filePath);
            string tempFile = Path.Combine(dir, $"_{name}_wm.pdf");

            float alpha = opacity / 100f;
            string pos = GetGsPosition(position);

            string escape = text.Replace("(", "\\(").Replace(")", "\\)");

            string psCode = $"<</EndPage {{2 eq {{pop false}} {{gsave /Helvetica-Bold 36 selectfont {alpha:F2} setgray " +
                $"{pos} moveto ({escape}) show grestore true}} ifelse}}>> setpagedevice";

            string args = $"-dNOPAUSE -dBATCH -dQUIET -dNOSAFER " +
                $"-sDEVICE=pdfwrite " +
                $"-sOutputFile=\"{tempFile}\" " +
                $"-c \"{psCode}\" " +
                $"-f \"{filePath}\"";

            var psi = new ProcessStartInfo(gsExe, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using (var proc = new Process { StartInfo = psi })
            {
                proc.Start();
                if (!proc.WaitForExit(30000))
                {
                    proc.Kill();
                    return;
                }

                if (proc.ExitCode == 0 && File.Exists(tempFile))
                {
                    try { File.Delete(filePath); } catch { }
                    File.Move(tempFile, filePath);
                }
            }

            try { File.Delete(tempFile); } catch { }
        }

        private static string GetGsPosition(string position)
        {
            switch (position)
            {
                case "TopLeft": return "36 720";
                case "TopRight": return "504 720";
                case "BottomLeft": return "36 36";
                case "BottomRight": return "504 36";
                case "Tile": return "0 0";
                default: return "270 396";
            }
        }

        private static string FindGsExecutable()
        {
            string[] searchPaths =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "gs", "gs10.05.1", "bin"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "gs", "gs10.05.1", "bin"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "gs", "gs10.04.0", "bin"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "gs", "gs10.04.0", "bin"),
                @"C:\Program Files\gs\gs10.05.1\bin",
                @"C:\Program Files\gs\gs10.04.0\bin",
                @"C:\Program Files (x86)\gs\gs10.05.1\bin",
                @"C:\Program Files (x86)\gs\gs10.04.0\bin",
            };

            foreach (var dir in searchPaths)
            {
                string exe = Path.Combine(dir, "gswin64c.exe");
                if (File.Exists(exe)) return exe;
            }
            return null;
        }

        private static RectangleF GetPositionBounds(string position, int width, int height)
        {
            float margin = 20;

            switch (position)
            {
                case "TopLeft":
                    return new RectangleF(margin, margin, width * 0.35f, height * 0.1f);
                case "TopRight":
                    return new RectangleF(width - width * 0.35f - margin, margin,
                        width * 0.35f, height * 0.1f);
                case "BottomLeft":
                    return new RectangleF(margin, height - height * 0.1f - margin,
                        width * 0.35f, height * 0.1f);
                case "BottomRight":
                    return new RectangleF(width - width * 0.35f - margin,
                        height - height * 0.1f - margin, width * 0.35f, height * 0.1f);
                case "Tile":
                    return new RectangleF(0, 0, width, height);
                case "Center":
                default:
                    return new RectangleF(width * 0.25f, height * 0.4f,
                        width * 0.5f, height * 0.2f);
            }
        }

        private static ImageFormat GetImageFormat(string ext)
        {
            switch (ext)
            {
                case ".png": return ImageFormat.Png;
                case ".jpg":
                case ".jpeg": return ImageFormat.Jpeg;
                case ".bmp": return ImageFormat.Bmp;
                case ".tif":
                case ".tiff": return ImageFormat.Tiff;
                default: return ImageFormat.Png;
            }
        }
    }
}
