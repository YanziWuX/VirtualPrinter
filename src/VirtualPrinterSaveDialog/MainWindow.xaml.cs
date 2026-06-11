using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VirtualPrinter.GhostLib;
using VirtualPrinter.SaveDialog.Services;

namespace VirtualPrinter.SaveDialog
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string _inputPsFile;
        private string _documentName;
        private int _jobId;
        private bool _resultSent;

        private int _jpegQuality = 85;
        private string _pdfVersion = "1.7";
        private bool _embedFonts = true;

        public string DocumentName { get; set; }
        public string PrintTime { get; set; }

        public List<FormatItem> Formats { get; }
        public FormatItem SelectedFormat { get; set; }
        private string _prevFormatValue;

        public List<string> ColorModes { get; }
        public string SelectedColorMode { get; set; }

        public int Resolution { get; set; } = 300;

        public bool PdfMergePages { get; set; } = true;
        public bool ImagePerPage { get; set; } = true;
        public bool MultiPageTiff { get; set; }
        public bool MergeImagePages { get; set; }

        public bool EnableWatermark { get; set; }
        public string WatermarkText { get; set; } = "Confidential";
        public string WatermarkPosition { get; set; } = "Center";
        public int WatermarkOpacity { get; set; } = 30;
        public List<string> WatermarkPositions { get; } =
            new List<string> { "Center", "TopLeft", "TopRight", "BottomLeft", "BottomRight", "Tile" };

        public string SaveFolder { get; set; }
        public string FileName { get; set; }
        public string FileExtension
        {
            get
            {
                var val = SelectedFormat?.Value ?? "pdf";
                return val == "jpeg" ? "jpg" : val;
            }
        }

        public bool RememberSettings { get; set; } = true;
        public bool OpenFolderAfterPrint { get; set; } = true;
        public bool ShowSuccessMessage { get; set; } = true;
        private string _psFileForCleanup;
        private DispatcherTimer _autoCloseTimer;

        public MainWindow()
        {
            Formats = new List<FormatItem>
            {
                new FormatItem("PDF", "pdf"),
                new FormatItem("PNG", "png"),
                new FormatItem("JPEG", "jpeg"),
                new FormatItem("BMP", "bmp"),
                new FormatItem("TIFF", "tiff"),
            };

            ColorModes = new List<string> { "RGB", "Grayscale", "CMYK", "BlackWhite" };

            SelectedFormat = Formats[0];
            _prevFormatValue = "pdf";
            SelectedColorMode = "RGB";

            string mgrDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VirtualPrinter");
            SaveFolder = LoadManagerSaveFolder(mgrDir) ??
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            PrintTime = $"打印于: {DateTime.Now:yyyy-MM-dd HH:mm}";

            InitializeComponent();
            DataContext = this;
            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        private static string LoadManagerSaveFolder(string mgrDir)
        {
            try
            {
                string path = Path.Combine(mgrDir, "settings.json");
                if (File.Exists(path))
                {
                    var dict = JsonParseFlat(File.ReadAllText(path));
                    if (dict.TryGetValue("SaveFolder", out var sf) && sf != null)
                        return sf.ToString();
                }
            }
            catch { }
            return null;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length >= 2)
                _inputPsFile = args[1];
            if (args.Length >= 3)
            {
                _documentName = args[2];
                DocumentName = _documentName;
                string baseName = Path.GetFileNameWithoutExtension(_documentName);
                FileName = $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}";
                OnPropertyChanged(nameof(FileName));
            }
            if (args.Length >= 4)
                int.TryParse(args[3], out _jobId);

            LoadSettings();
            ShowSuccessMsgBox.IsChecked = ShowSuccessMessage;
            UpdateMultiPageForFormat();

            // Auto-copy output.prn to jobs dir as .pending file
            if (!string.IsNullOrEmpty(_inputPsFile) && File.Exists(_inputPsFile))
            {
                try
                {
                    string jobsDir = Path.Combine(
                        Path.GetDirectoryName(_inputPsFile), "jobs");
                    Directory.CreateDirectory(jobsDir);
                    string pendingFile = Path.Combine(
                        jobsDir, $"job_{DateTime.Now.Ticks}.pending");
                    File.Copy(_inputPsFile, pendingFile, true);
                    _inputPsFile = pendingFile;
                    _psFileForCleanup = pendingFile;
                }
                catch { }
            }

            // Auto-close after 10 minutes if user doesn't act
            _autoCloseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(10)
            };
            _autoCloseTimer.Tick += (tSender, tArgs) =>
            {
                _autoCloseTimer.Stop();
                if (_jobId > 0 && !_resultSent)
                {
                    _ = ServiceClient.SendResultAsync(_jobId, null, false, "Timed out");
                    _resultSent = true;
                }
                CleanupPsFile();
                Close();
            };
            _autoCloseTimer.Start();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            _autoCloseTimer?.Stop();
            if (_jobId > 0 && !_resultSent)
            {
                _ = ServiceClient.SendResultAsync(_jobId, null, false, "Cancelled");
                _resultSent = true;
            }
            CleanupPsFile();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            _autoCloseTimer?.Stop();
            if (_jobId > 0 && !_resultSent)
                _ = ServiceClient.SendResultAsync(_jobId, null, false, "Cancelled");
            CleanupPsFile();
            Close();
        }

        private void CleanupPsFile()
        {
            if (_psFileForCleanup != null && File.Exists(_psFileForCleanup))
            {
                try { File.Delete(_psFileForCleanup); } catch { }
                _psFileForCleanup = null;
            }
        }

        private void OnFormatChanged(object sender, SelectionChangedEventArgs e)
        {
            string newVal = SelectedFormat?.Value;
            OnPropertyChanged(nameof(FileExtension));

            UpdateMultiPageForFormat();
            string baseName = Path.GetFileNameWithoutExtension(_documentName ?? "print");
            FileName = $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}";
            OnPropertyChanged(nameof(FileName));
            _prevFormatValue = newVal;
        }

        private void UpdateMultiPageForFormat()
        {
            string fmt = SelectedFormat?.Value;
            if (fmt == "pdf")
            {
                MultiPageGroup.Visibility = Visibility.Visible;
                RadioPdfMerge.Visibility = Visibility.Visible;
                ImagePageOptions.Visibility = Visibility.Collapsed;
                RadioPdfMerge.IsChecked = true;
                PdfMergePages = true;
                ImagePerPage = false;
                MultiPageTiff = false;
                MergeImagePages = false;
            }
            else
            {
                MultiPageGroup.Visibility = Visibility.Visible;
                RadioPdfMerge.Visibility = Visibility.Collapsed;
                ImagePageOptions.Visibility = Visibility.Visible;
                RadioImagePerPage.IsChecked = true;
                ImagePerPage = true;
                MultiPageTiff = false;
                MergeImagePages = false;
            }
        }

        private void OnPdfMergeChecked(object sender, RoutedEventArgs e)
        {
            PdfMergePages = true;
            ImagePerPage = false;
            MultiPageTiff = false;
            MergeImagePages = false;
        }

        private void OnImagePerPageChecked(object sender, RoutedEventArgs e)
        {
            PdfMergePages = false;
            ImagePerPage = true;
            MultiPageTiff = false;
            MergeImagePages = false;
        }

        private void OnImageMergeChecked(object sender, RoutedEventArgs e)
        {
            PdfMergePages = false;
            ImagePerPage = false;
            string fmt = SelectedFormat?.Value;
            if (fmt == "tiff")
            {
                MultiPageTiff = true;
                MergeImagePages = false;
            }
            else
            {
                MultiPageTiff = false;
                MergeImagePages = true;
            }
        }

        private void LoadSettings()
        {
            string configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VirtualPrinter", "save_dialog_settings.json");
            if (!File.Exists(configPath))
                return;
            try
            {
                string json = File.ReadAllText(configPath);
                var dict = JsonParseFlat(json);

                if (dict.TryGetValue("SaveFolder", out var sf) && sf != null)
                { SaveFolder = sf.ToString(); OnPropertyChanged(nameof(SaveFolder)); }
                if (dict.TryGetValue("OpenFolderAfterPrint", out var of) && of != null)
                { OpenFolderAfterPrint = bool.Parse(of.ToString()); OnPropertyChanged(nameof(OpenFolderAfterPrint)); }
                if (dict.TryGetValue("ShowSuccessMessage", out var ss) && ss != null)
                { ShowSuccessMessage = bool.Parse(ss.ToString()); }
                if (dict.TryGetValue("WatermarkText", out var wt) && wt != null)
                    WatermarkText = wt.ToString();
                if (dict.TryGetValue("WatermarkPosition", out var wp) && wp != null)
                    WatermarkPosition = wp.ToString();
                if (dict.TryGetValue("WatermarkOpacity", out var wo) && wo != null)
                { WatermarkOpacity = int.Parse(wo.ToString()); }

                if (dict.TryGetValue("Format", out var fmt) && fmt != null)
                {
                    var match = Formats.FirstOrDefault(f => f.Value == fmt.ToString());
                    if (match != null)
                    {
                        SelectedFormat = match;
                        _prevFormatValue = fmt.ToString();
                        OnPropertyChanged(nameof(SelectedFormat));
                        OnPropertyChanged(nameof(FileExtension));
                    }
                }
                if (dict.TryGetValue("ColorMode", out var cm) && cm != null)
                { SelectedColorMode = cm.ToString(); OnPropertyChanged(nameof(SelectedColorMode)); }
                if (dict.TryGetValue("Resolution", out var res) && res != null)
                { Resolution = int.Parse(res.ToString()); OnPropertyChanged(nameof(Resolution)); }

                bool merge = dict.TryGetValue("MergePages", out var mp) && mp != null && bool.Parse(mp.ToString());
                UpdateMultiPageForFormat();
                if (merge)
                    RadioImageMerge.IsChecked = true;
            }
            catch
            {
                // If JSON is corrupt (e.g. old format), ignore and use defaults
            }
        }

        private void SaveSettings()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "VirtualPrinter");
                Directory.CreateDirectory(dir);

                bool showSuccess = ShowSuccessMsgBox.IsChecked == true;
                bool mergePages = RadioImageMerge.IsChecked == true;

                var parts = new List<string>
                {
                    $"\"SaveFolder\":\"{JsonEscape(SaveFolder)}\"",
                    $"\"Format\":\"{JsonEscape(SelectedFormat?.Value ?? "pdf")}\"",
                    $"\"ColorMode\":\"{JsonEscape(SelectedColorMode ?? "RGB")}\"",
                    $"\"Resolution\":{Resolution}",
                    $"\"OpenFolderAfterPrint\":{BoolStr(OpenFolderAfterPrint)}",
                    $"\"ShowSuccessMessage\":{BoolStr(showSuccess)}",
                    $"\"WatermarkText\":\"{JsonEscape(WatermarkText)}\"",
                    $"\"WatermarkPosition\":\"{JsonEscape(WatermarkPosition)}\"",
                    $"\"WatermarkOpacity\":{WatermarkOpacity}",
                    $"\"MergePages\":{BoolStr(mergePages)}",
                };

                string json = "{" + string.Join(",", parts) + "}";
                File.WriteAllText(Path.Combine(dir, "save_dialog_settings.json"), json);
            }
            catch { }
        }

        private static string JsonEscape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }

        private static string BoolStr(bool b) => b ? "true" : "false";

        private static Dictionary<string, object> JsonParseFlat(string json)
        {
            var result = new Dictionary<string, object>();
            int i = 0;
            while (i < json.Length)
            {
                // skip whitespace and separators
                if (json[i] == '{' || json[i] == '}' || json[i] == ',' || json[i] == ' ' || json[i] == '\r' || json[i] == '\n' || json[i] == '\t')
                { i++; continue; }

                // expect key string
                if (json[i] == '"')
                {
                    i++;
                    var key = new System.Text.StringBuilder();
                    while (i < json.Length && json[i] != '"')
                    {
                        if (json[i] == '\\') { i++; if (i < json.Length) key.Append(json[i]); }
                        else key.Append(json[i]);
                        i++;
                    }
                    i++; // skip closing quote
                    string k = key.ToString();

                    // skip :
                    while (i < json.Length && (json[i] == ':' || json[i] == ' ')) i++;

                    // parse value
                    if (i < json.Length && json[i] == '"')
                    {
                        i++;
                        var val = new System.Text.StringBuilder();
                        while (i < json.Length && json[i] != '"')
                        {
                            if (json[i] == '\\') { i++; if (i < json.Length) val.Append(json[i]); }
                            else val.Append(json[i]);
                            i++;
                        }
                        i++;
                        result[k] = val.ToString();
                    }
                    else if (i < json.Length && (json[i] == 't' || json[i] == 'f'))
                    {
                        if (json.Substring(i).StartsWith("true")) { result[k] = "true"; i += 4; }
                        else { result[k] = "false"; i += 5; }
                    }
                    else
                    {
                        var num = new System.Text.StringBuilder();
                        while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '.' || json[i] == '-'))
                        { num.Append(json[i]); i++; }
                        result[k] = num.ToString();
                    }
                }
                else i++;
            }
            return result;
        }

        private void OnBrowseFolder(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                SelectedPath = SaveFolder,
                Description = "选择保存输出文件的文件夹"
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SaveFolder = dialog.SelectedPath;
                OnPropertyChanged(nameof(SaveFolder));
            }
        }

        private void OnAdvancedSettings(object sender, RoutedEventArgs e)
        {
            var win = new AdvancedSettingsWindow
            {
                Owner = this,
                JpegQuality = _jpegQuality,
                PdfVersion = _pdfVersion,
                EmbedFonts = _embedFonts
            };
            if (win.ShowDialog() == true)
            {
                _jpegQuality = win.JpegQuality;
                _pdfVersion = win.PdfVersion;
                _embedFonts = win.EmbedFonts;
            }
        }

        private async void OnPrint(object sender, RoutedEventArgs e)
        {
            _autoCloseTimer?.Stop();
            SaveSettings();

            string fmt = SelectedFormat?.Value ?? "pdf";
            string ext = fmt == "jpeg" ? "jpg" : fmt;
            string outputPath = Path.Combine(SaveFolder, FileName + "." + ext);

            if (string.IsNullOrEmpty(_inputPsFile) || !File.Exists(_inputPsFile))
            {
                MessageBox.Show("打印数据文件不存在，请重新打印", "VirtualPrinter",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                await ServiceClient.SendResultAsync(_jobId, null, false, "File not found");
                _resultSent = true;
                return;
            }

            var progress = new ProgressWindow { Owner = this };
            progress.Show();

            string psFile = null;
            bool closeAfterPrint = false;
            try
            {
                // Step 1: create PS file from .pending
                progress.SetStatus("正在创建 PS 文件...");
                string pendingPath = _inputPsFile;
                string pendingDir = Path.GetDirectoryName(pendingPath);
                string pendingName = Path.GetFileNameWithoutExtension(pendingPath);
                psFile = Path.Combine(pendingDir, pendingName + ".ps");
                File.Move(pendingPath, psFile);
                _psFileForCleanup = psFile;

                // Step 2: GS conversion
                progress.SetStatus($"正在转换为 {fmt.ToUpper()}...");
                var opts = new GSConvertOptions
                {
                    Format = ParseFormat(fmt),
                    ColorMode = ParseColorMode(SelectedColorMode ?? "RGB"),
                    Resolution = Resolution,
                    JpegQuality = _jpegQuality,
                    EmbedFonts = _embedFonts,
                    PdfVersion = _pdfVersion,
                    MultiPageTiff = MultiPageTiff,
                    MergeImagePages = MergeImagePages,
                    EnableWatermark = EnableWatermark
                };

                GSConvertResult gsResult = null;
                await System.Threading.Tasks.Task.Run(() =>
                {
                    var converter = new GSConvert();
                    gsResult = converter.Convert(psFile, outputPath, opts);
                });

                if (gsResult.Success && EnableWatermark && !string.IsNullOrEmpty(WatermarkText))
                {
                    progress.SetStatus("正在添加水印...");
                    await System.Threading.Tasks.Task.Run(() =>
                        WatermarkEngine.Apply(outputPath, WatermarkText, WatermarkPosition, WatermarkOpacity));
                }

                progress.Close();

                if (gsResult.Success)
                {
                    bool showSuccess = ShowSuccessMsgBox.IsChecked == true;

                    if (OpenFolderAfterPrint)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start("explorer.exe",
                                $"/select,\"{outputPath}\"");
                        }
                        catch { }
                    }

                    if (showSuccess)
                    {
                        MessageBox.Show($"打印成功！\n文件已保存到:\n{outputPath}", "VirtualPrinter",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    _ = ServiceClient.SendResultAsync(_jobId, outputPath, true, null);
                    _resultSent = true;
                    closeAfterPrint = true;
                }
                else
                {
                    MessageBox.Show($"打印失败:\n{gsResult.ErrorMessage}",
                        "VirtualPrinter", MessageBoxButton.OK, MessageBoxImage.Error);
                    _ = ServiceClient.SendResultAsync(_jobId, null, false, gsResult.ErrorMessage);
                    _resultSent = true;
                    closeAfterPrint = true;
                }
            }
            catch (Exception ex)
            {
                progress.Close();
                MessageBox.Show($"打印失败:\n{ex.Message}",
                    "VirtualPrinter", MessageBoxButton.OK, MessageBoxImage.Error);
                _ = ServiceClient.SendResultAsync(_jobId, null, false, ex.Message);
                _resultSent = true;
                closeAfterPrint = true;
            }
            finally
            {
                // Delete PS file after conversion (success or failure)
                if (psFile != null && File.Exists(psFile))
                {
                    try { File.Delete(psFile); } catch { }
                    if (_psFileForCleanup == psFile) _psFileForCleanup = null;
                }

                if (closeAfterPrint)
                {
                    // 1) Graceful WPF close
                    try
                    {
                        if (Dispatcher.CheckAccess())
                            Close();
                        else
                            Dispatcher.Invoke(Close);
                    }
                    catch { }

                    // 2) Explicit shutdown (ShutdownMode=OnExplicitShutdown)
                    try { Application.Current.Shutdown(); } catch { }

                    // 3) Nuclear option — guarantee process terminates on Win10
                    Environment.Exit(0);
                }
            }
        }

        private OutputFormat ParseFormat(string fmt)
        {
            switch (fmt.ToLower())
            {
                case "pdf": return OutputFormat.PDF;
                case "png": return OutputFormat.PNG;
                case "jpeg": return OutputFormat.JPEG;
                case "bmp": return OutputFormat.BMP;
                case "tiff": return OutputFormat.TIFF;
                default: return OutputFormat.PDF;
            }
        }

        private ColorMode ParseColorMode(string mode)
        {
            switch (mode.ToLower())
            {
                case "rgb": return ColorMode.RGB;
                case "grayscale": return ColorMode.Grayscale;
                case "cmyk": return ColorMode.CMYK;
                case "blackwhite": return ColorMode.BlackWhite;
                default: return ColorMode.RGB;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class FormatItem
    {
        public string Name { get; }
        public string Value { get; }
        public FormatItem(string name, string value) { Name = name; Value = value; }
    }
}
