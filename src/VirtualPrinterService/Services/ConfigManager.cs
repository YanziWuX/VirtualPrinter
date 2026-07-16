using System;
using System.IO;
using System.Web.Script.Serialization;
using VirtualPrinter.Service.Models;

namespace VirtualPrinter.Service.Services
{
    public class ConfigManager
    {
        private readonly string _configPath;
        private Settings _settings;

        public ConfigManager()
        {
            _configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VirtualPrinter", "settings.json");
            Load();
        }

        public Settings Settings => _settings;

        public void Load()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    var serializer = new JavaScriptSerializer();
                    _settings = serializer.Deserialize<Settings>(json);
                }
                else
                {
                    _settings = new Settings();
                    Save();
                }
            }
            catch
            {
                _settings = new Settings();
            }
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                Directory.CreateDirectory(dir);

                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(_settings);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.EventLog.WriteEntry(
                    "VirtualPrinterService",
                    $"ConfigManager.Save failed: {ex.Message}",
                    System.Diagnostics.EventLogEntryType.Warning);
            }
        }

        public string GetDefaultOutputDir()
        {
            string dir = _settings.SaveFolder;
            if (string.IsNullOrEmpty(dir))
                dir = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments), "VirtualPrinter");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
