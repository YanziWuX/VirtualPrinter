using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace VirtualPrinter.GhostLib
{
    public enum OutputFormat
    {
        PDF,
        PNG,
        JPEG,
        BMP,
        TIFF
    }

    public enum ColorMode
    {
        RGB,
        Grayscale,
        CMYK,
        BlackWhite
    }

    public class GSConvertResult
    {
        public bool Success { get; set; }
        public string OutputPath { get; set; }
        public string ErrorMessage { get; set; }
        public int ExitCode { get; set; }
    }

    public class GSConvertOptions
    {
        public OutputFormat Format { get; set; } = OutputFormat.PDF;
        public ColorMode ColorMode { get; set; } = ColorMode.RGB;
        public int Resolution { get; set; } = 300;
        public int JpegQuality { get; set; } = 85;
        public bool EmbedFonts { get; set; } = true;
        public string PdfVersion { get; set; } = "1.7";
        public bool MultiPageTiff { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 120;
        public string GsPath { get; set; }

        // Watermark - applied externally via overlay
        public bool EnableWatermark { get; set; } = false;

        // Image merge: stitch all pages into one tall image (PNG/JPEG/BMP)
        public bool MergeImagePages { get; set; } = false;
    }

    public class GSConvert
    {
        private static void StitchImages(List<string> pageFiles, string outputPath, OutputFormat fmt)
        {
            var images = pageFiles.Select(f => Image.FromFile(f)).ToList();
            int totalH = images.Sum(img => img.Height);
            int w = images.Max(img => img.Width);

            using (var merged = new Bitmap(w, totalH))
            using (var g = System.Drawing.Graphics.FromImage(merged))
            {
                g.Clear(Color.White);
                int y = 0;
                foreach (var img in images)
                {
                    g.DrawImage(img, (w - img.Width) / 2, y, img.Width, img.Height);
                    y += img.Height;
                    img.Dispose();
                }

                ImageCodecInfo codec = null;
                EncoderParameters eps = null;

                if (fmt == OutputFormat.JPEG)
                {
                    codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    eps = new EncoderParameters(1);
                    eps.Param[0] = new EncoderParameter(Encoder.Quality, 85L);
                }

                if (codec != null)
                    merged.Save(outputPath, codec, eps);
                else
                    merged.Save(outputPath);
            }
        }

        private string FindGSExecutable()
        {
            string[] searchPaths =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "gs", "gs10.05.1", "bin"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "gs", "gs10.05.1", "bin"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "gs", "gs10.04.0", "bin"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "gs", "gs10.04.0", "bin"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gs"),
                @"C:\Program Files\gs\gs10.05.1\bin",
                @"C:\Program Files\gs\gs10.04.0\bin",
                @"C:\Program Files (x86)\gs\gs10.04.0\bin",
            };

            foreach (var dir in searchPaths)
            {
                string exe = Path.Combine(dir, "gswin64c.exe");
                if (File.Exists(exe)) return exe;
            }
            return "gswin64c.exe";
        }

        private string GetDeviceName(OutputFormat fmt, ColorMode color)
        {
            if (fmt == OutputFormat.PDF) return "pdfwrite";

            if (fmt == OutputFormat.PNG)
            {
                switch (color)
                {
                    case ColorMode.Grayscale: return "pnggray";
                    case ColorMode.BlackWhite: return "pngmono";
                    default: return "png16m";
                }
            }

            if (fmt == OutputFormat.JPEG)
            {
                return color == ColorMode.Grayscale ? "jpeggray" : "jpeg";
            }

            if (fmt == OutputFormat.BMP)
            {
                switch (color)
                {
                    case ColorMode.Grayscale: return "bmpgray";
                    case ColorMode.BlackWhite: return "bmpmono";
                    default: return "bmp16m";
                }
            }

            if (fmt == OutputFormat.TIFF)
            {
                switch (color)
                {
                    case ColorMode.Grayscale: return "tiffgray";
                    case ColorMode.BlackWhite: return "tiffg4";
                    case ColorMode.CMYK: return "tiff32nc";
                    default: return "tiff24nc";
                }
            }

            return "pdfwrite";
        }

        private string BuildOutputPattern(string outputPath, OutputFormat fmt, bool multiPage)
        {
            if (fmt == OutputFormat.PDF || (fmt == OutputFormat.TIFF && multiPage))
            {
                return $"\"{outputPath}\"";
            }

            string dir = Path.GetDirectoryName(outputPath);
            string name = Path.GetFileNameWithoutExtension(outputPath);
            string ext = Path.GetExtension(outputPath);
            return $"\"{Path.Combine(dir, name + "_%d" + ext)}\"";
        }

        private string BuildArguments(GSConvertOptions opts, string inputPs, string outputPath)
        {
            string device = GetDeviceName(opts.Format, opts.ColorMode);
            string outputPat = BuildOutputPattern(outputPath, opts.Format, opts.MultiPageTiff);

            var args = $"-dNOPAUSE -dBATCH -dQUIET " +
                $"-sDEVICE={device} " +
                $"-r{opts.Resolution} " +
                $"-dTextAlphaBits=4 -dGraphicsAlphaBits=4 " +
                $"-sOutputFile={outputPat} " +
                $"\"{inputPs}\"";

            if (opts.Format == OutputFormat.PDF)
            {
                args = $"-dNOPAUSE -dBATCH -dQUIET " +
                    $"-sDEVICE=pdfwrite " +
                    $"-dCompatibilityLevel={opts.PdfVersion} " +
                    (opts.EmbedFonts ? "-dEmbedAllFonts=true -dSubsetFonts=true " : "") +
                    $"-dPDFSETTINGS=/prepress " +
                    $"-sOutputFile=\"{outputPath}\" " +
                    $"\"{inputPs}\"";
            }

            if (opts.Format == OutputFormat.JPEG)
            {
                args += $" -dJPEGQ={opts.JpegQuality}";
            }

            if (opts.Format == OutputFormat.TIFF && opts.ColorMode == ColorMode.BlackWhite)
            {
                args = args.Replace("-dTextAlphaBits=4 -dGraphicsAlphaBits=4", "");
            }

            return args;
        }

        public GSConvertResult Convert(string inputPs, string outputPath, GSConvertOptions opts)
        {
            var result = new GSConvertResult
            {
                OutputPath = outputPath
            };

            try
            {
                if (!File.Exists(inputPs))
                {
                    result.ErrorMessage = $"Input file not found: {inputPs}";
                    return result;
                }

                string gsExe = opts.GsPath ?? FindGSExecutable();
                string args = BuildArguments(opts, inputPs, outputPath);

                var psi = new ProcessStartInfo(gsExe, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (var process = new Process { StartInfo = psi })
                using (var stderrStream = new StringWriter())
                {
                    process.Start();

                    // Read stderr on background thread to prevent pipe buffer deadlock
                    var stderrTask = Task.Run(() =>
                    {
                        char[] buf = new char[4096];
                        int read;
                        while ((read = process.StandardError.Read(buf, 0, buf.Length)) > 0)
                            stderrStream.Write(buf, 0, read);
                    });

                    bool exited = process.WaitForExit(opts.TimeoutSeconds * 1000);
                    if (!exited)
                    {
                        process.Kill();
                        stderrTask.Wait(1000);
                        result.ErrorMessage = "Ghostscript timed out after " + opts.TimeoutSeconds + "s";
                        return result;
                    }

                    stderrTask.Wait(5000);
                    string stderr = stderrStream.ToString();

                    result.ExitCode = process.ExitCode;
                    bool outputOk = false;
                    if (process.ExitCode == 0)
                    {
                        if (opts.Format == OutputFormat.PDF || (opts.Format == OutputFormat.TIFF && opts.MultiPageTiff))
                        {
                            outputOk = File.Exists(outputPath);
                        }
                        else
                        {
                            string dir = Path.GetDirectoryName(outputPath) ?? ".";
                            string name = Path.GetFileNameWithoutExtension(outputPath);
                            string ext = Path.GetExtension(outputPath);
                            outputOk = Directory.GetFiles(dir, $"{name}_*{ext}").Length > 0;
                            if (outputOk && opts.MergeImagePages)
                            {
                                // StitchImages will produce the final outputPath
                                outputOk = true;
                            }
                        }
                    }
                    if (outputOk)
                    {
                        result.Success = true;

                        if (opts.MergeImagePages && opts.Format != OutputFormat.PDF &&
                            !(opts.Format == OutputFormat.TIFF && opts.MultiPageTiff))
                        {
                            string dir = Path.GetDirectoryName(outputPath) ?? ".";
                            string name = Path.GetFileNameWithoutExtension(outputPath);
                            string ext = Path.GetExtension(outputPath);
                            var pages = Directory.GetFiles(dir, $"{name}_*{ext}")
                                .OrderBy(f =>
                                {
                                    var m = System.Text.RegularExpressions.Regex.Match(
                                        Path.GetFileNameWithoutExtension(f), @"_(\d+)$");
                                    return m.Success ? int.Parse(m.Groups[1].Value) : 0;
                                })
                                .ToList();

                            if (pages.Count > 0)
                            {
                                StitchImages(pages, outputPath, opts.Format);
                                foreach (var p in pages)
                                {
                                    try { File.Delete(p); } catch { }
                                }
                            }
                        }
                    }
                    else
                    {
                        result.ErrorMessage = $"Ghostscript exit code: {process.ExitCode}\n{stderr}";
                    }
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"GS conversion failed: {ex.Message}";
            }

            return result;
        }
    }
}
