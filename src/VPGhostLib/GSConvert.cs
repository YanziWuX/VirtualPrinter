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

        // Rasterize PS to images first, then convert to PDF (better fidelity for complex PS)
        public bool RasterizeToPdf { get; set; } = false;
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

        private bool OutputExists(string outputPath, OutputFormat fmt, bool multiPage)
        {
            if (fmt == OutputFormat.PDF || (fmt == OutputFormat.TIFF && multiPage))
                return File.Exists(outputPath);

            string dir = Path.GetDirectoryName(outputPath) ?? ".";
            string name = Path.GetFileNameWithoutExtension(outputPath);
            string ext = Path.GetExtension(outputPath);
            return Directory.GetFiles(dir, $"{name}_*{ext}").Length > 0;
        }

        private GSConvertResult ConvertDirect(string inputPs, string outputPath, GSConvertOptions opts)
        {
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
                    return new GSConvertResult { ErrorMessage = "Ghostscript timed out after " + opts.TimeoutSeconds + "s" };
                }

                stderrTask.Wait(5000);
                string stderr = stderrStream.ToString();

                bool hasOutput = OutputExists(outputPath, opts.Format, opts.MultiPageTiff);
                bool exitOk = process.ExitCode == 0;
                string error = process.ExitCode != 0 ? $"Ghostscript exit code: {process.ExitCode}\n{stderr}" : null;

                if (!exitOk && hasOutput && !string.IsNullOrEmpty(stderr) && stderr.Contains("Unrecoverable error"))
                {
                    error = $"Ghostscript exit code: {process.ExitCode}\n{stderr}";
                }

                return new GSConvertResult
                {
                    ExitCode = process.ExitCode,
                    Success = (exitOk || hasOutput) && hasOutput,
                    ErrorMessage = (exitOk && hasOutput) ? null : error
                };
            }
        }

        private GSConvertResult ConvertRasterizeToPdf(string inputPs, string outputPath, GSConvertOptions opts)
        {
            string gsExe = opts.GsPath ?? FindGSExecutable();
            string tmpDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "VirtualPrinter", "tmp",
                "VP_Raster_" + Path.GetFileNameWithoutExtension(inputPs));
            Directory.CreateDirectory(tmpDir);

            try
            {
                // Step 1: PS → PNG per page
                string pngPattern = $"\"{Path.Combine(tmpDir, "page_%d.png")}\"";
                string rasterArgs = $"-dNOPAUSE -dBATCH -dQUIET " +
                    $"-sDEVICE=png16m " +
                    $"-r{opts.Resolution} " +
                    $"-dTextAlphaBits=4 -dGraphicsAlphaBits=4 " +
                    $"-sOutputFile={pngPattern} " +
                    $"\"{inputPs}\"";

                var psi1 = new ProcessStartInfo(gsExe, rasterArgs)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (var p1 = new Process { StartInfo = psi1 })
                using (var err1 = new StringWriter())
                {
                    p1.Start();
                    Task.Run(() =>
                    {
                        char[] buf = new char[4096];
                        int read;
                        while ((read = p1.StandardError.Read(buf, 0, buf.Length)) > 0)
                            err1.Write(buf, 0, read);
                    });
                    if (!p1.WaitForExit(opts.TimeoutSeconds * 1000))
                    {
                        p1.Kill();
                        return new GSConvertResult { ErrorMessage = "Rasterize timed out" };
                    }
                    if (p1.ExitCode != 0)
                    {
                        return new GSConvertResult { ErrorMessage = $"Rasterize failed: {err1}" };
                    }
                }

                var pageFiles = Directory.GetFiles(tmpDir, "page_*.png")
                    .OrderBy(f =>
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(
                            Path.GetFileNameWithoutExtension(f), @"_(\d+)$");
                        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
                    })
                    .ToList();

                if (pageFiles.Count == 0)
                    return new GSConvertResult { ErrorMessage = "No pages rasterized" };

                var validPages = pageFiles.Where(f => new FileInfo(f).Length > 0).ToList();
                if (validPages.Count == 0)
                    return new GSConvertResult { ErrorMessage = "Rasterized pages are empty" };
                pageFiles = validPages;

                // Log page info for debugging
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "VirtualPrinter");
                Directory.CreateDirectory(logDir);
                using (var log = File.CreateText(Path.Combine(logDir, "raster_debug.txt")))
                {
                    foreach (var pf in pageFiles)
                    {
                        var fi = new FileInfo(pf);
                        try
                        {
                            using (var img = Image.FromFile(pf))
                                log.WriteLine("{0}: {1}x{2} ({3} bytes)", fi.Name, img.Width, img.Height, fi.Length);
                        }
                        catch
                        {
                            log.WriteLine("{0}: FAILED TO LOAD ({1} bytes)", fi.Name, fi.Length);
                        }
                    }
                }

                // Step 2: PNG pages → PDF (C# native, no Ghostscript)
                bool pdfOk = CreatePdfFromImages(pageFiles, outputPath, opts.JpegQuality);

                if (!pdfOk)
                    return new GSConvertResult { ErrorMessage = "Failed to create PDF from rasterized pages" };

                return new GSConvertResult
                {
                    ExitCode = 0,
                    Success = File.Exists(outputPath),
                    OutputPath = outputPath
                };
            }
            finally
            {
                try { Directory.Delete(tmpDir, true); } catch { }
            }
        }

        private bool CreatePdfFromImages(List<string> imageFiles, string outputPath, int jpegQuality)
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "VirtualPrinter");

            try
            {
                // Debug: save first image as JPEG to verify encoding
                if (imageFiles.Count > 0)
                {
                    using (var testImg = Image.FromFile(imageFiles[0]))
                    {
                        testImg.Save(Path.Combine(logDir, "debug_first_page.jpg"), ImageFormat.Jpeg);
                    }
                }

                // Collect all page info first (no seeking trick needed)
                var pageInfos = new List<Tuple<int, int, int, int, int, byte[]>>();
                int objNum = 3;
                var pageRefs = new List<int>();

                foreach (var imgFile in imageFiles)
                {
                    using (var img = Image.FromFile(imgFile))
                    {
                        int w = img.Width;
                        int h = img.Height;

                        byte[] jpeg;
                        using (var ms = new MemoryStream())
                        {
                            img.Save(ms, ImageFormat.Jpeg);
                            jpeg = ms.ToArray();
                        }

                        File.AppendAllText(Path.Combine(logDir, "raster_debug.txt"),
                            string.Format("  JPEG: {0}x{1} -> {2} bytes\n", w, h, jpeg.Length));

                        int pObj = objNum++;
                        int cObj = objNum++;
                        int iObj = objNum++;
                        pageRefs.Add(pObj);
                        pageInfos.Add(Tuple.Create(pObj, cObj, iObj, w, h, jpeg));
                    }
                }

                // Write PDF sequentially — no seek-back overwrite
                using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                using (var writer = new BinaryWriter(fs))
                {
                    var enc = System.Text.Encoding.ASCII;
                    writer.Write(enc.GetBytes("%PDF-1.4\n"));

                    var xrefOffsets = new List<long>();

                    // Object 1: Catalog
                    xrefOffsets.Add(writer.BaseStream.Position);
                    writer.Write(enc.GetBytes("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"));

                    // Object 2: Pages (with correct kids — written before page objects)
                    xrefOffsets.Add(writer.BaseStream.Position);
                    string kids = string.Join(" ", pageRefs.Select(r => string.Format("{0} 0 R", r)));
                    writer.Write(enc.GetBytes(string.Format(
                        "2 0 obj\n<< /Type /Pages /Kids [{0}] /Count {1} >>\nendobj\n",
                        kids, pageInfos.Count)));

                    // Page objects, content streams, and image XObjects
                    foreach (var info in pageInfos)
                    {
                        int pObj = info.Item1, cObj = info.Item2, iObj = info.Item3;
                        int w = info.Item4, h = info.Item5;
                        byte[] jpeg = info.Item6;

                        string contentStream = string.Format("q {0} 0 0 {1} 0 0 cm /Im0 Do Q\n", w, h);
                        byte[] contentBytes = enc.GetBytes(contentStream);

                        xrefOffsets.Add(writer.BaseStream.Position);
                        writer.Write(enc.GetBytes(string.Format(
                            "{0} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {1} {2}] /Contents {3} 0 R /Resources << /XObject << /Im0 {4} 0 R >> >> >>\nendobj\n",
                            pObj, w, h, cObj, iObj)));

                        xrefOffsets.Add(writer.BaseStream.Position);
                        writer.Write(enc.GetBytes(string.Format(
                            "{0} 0 obj\n<< /Length {1} >>\nstream\n{2}endstream\nendobj\n",
                            cObj, contentBytes.Length, contentStream)));

                        xrefOffsets.Add(writer.BaseStream.Position);
                        writer.Write(enc.GetBytes(string.Format(
                            "{0} 0 obj\n<< /Type /XObject /Subtype /Image /Width {1} /Height {2} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {3} >>\nstream\n",
                            iObj, w, h, jpeg.Length)));
                        writer.Write(jpeg);
                        writer.Write(enc.GetBytes("\nendstream\nendobj\n"));
                    }

                    // xref and trailer
                    int maxObj = 2 + pageInfos.Count * 3;
                    long xrefOffset = writer.BaseStream.Position;
                    writer.Write(enc.GetBytes(string.Format("xref\n0 {0}\n0000000000 65535 f \n", maxObj + 1)));
                    foreach (long off in xrefOffsets)
                        writer.Write(enc.GetBytes(string.Format("{0:0000000000} 00000 n \n", off)));

                    writer.Write(enc.GetBytes(string.Format(
                        "trailer\n<< /Size {0} /Root 1 0 R >>\nstartxref\n{1}\n%%EOF\n",
                        maxObj + 1, xrefOffset)));
                }

                return true;
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(logDir, "raster_debug.txt"), "  ERROR: " + ex.Message + "\n"); }
                catch { }
                return false;
            }
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

                if (opts.Format == OutputFormat.PDF && opts.RasterizeToPdf)
                {
                    return ConvertRasterizeToPdf(inputPs, outputPath, opts);
                }

                result = ConvertDirect(inputPs, outputPath, opts);

                if (result.Success && opts.MergeImagePages && opts.Format != OutputFormat.PDF &&
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
            catch (Exception ex)
            {
                result.ErrorMessage = $"GS conversion failed: {ex.Message}";
            }

            return result;
        }
    }
}
