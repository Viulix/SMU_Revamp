using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SMU_Revamp.Models;

namespace SMU_Revamp.Services
{
    /// <summary>
    /// Service for loading and saving application configuration.
    /// </summary>
    public class ConfigurationService
    {
        private static readonly Lazy<ConfigurationService> _instance =
            new(() => new ConfigurationService());

        public static ConfigurationService Instance => _instance.Value;

        private readonly string _configPath;
        private AppConfig _config = new();

        private ConfigurationService()
        {
            try
            {
                var appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SMU_Revamp"
                );

                if (!Directory.Exists(appDataDir))
                {
                    Directory.CreateDirectory(appDataDir);
                }

                _configPath = Path.Combine(appDataDir, "config.json");
            }
            catch
            {
                // Fallback to temp directory if AppData is not accessible
                _configPath = Path.Combine(Path.GetTempPath(), "SMU_Revamp_config.json");
            }
        }

        /// <summary>
        /// Loads configuration from disk.
        /// </summary>
        public async Task LoadAsync()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = await File.ReadAllTextAsync(_configPath);
                    _config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                }
                else
                {
                    _config = new AppConfig();
                }
            }
            catch
            {
                _config = new AppConfig();
            }
        }

        /// <summary>
        /// Saves configuration to disk.
        /// </summary>
        public async Task SaveAsync(AppConfig config)
        {
            try
            {
                _config = config;
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(config, options);
                await File.WriteAllTextAsync(_configPath, json);
            }
            catch
            {
                // Silently fail if save doesn't work
            }
        }

        /// <summary>
        /// Gets the current configuration.
        /// </summary>
        public AppConfig GetConfig() => _config;
    }
}
