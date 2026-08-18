using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SMU_Revamp.Models;

namespace SMU_Revamp.Services
{
    public class DatabaseSyncResult
    {
        public bool Success { get; set; }
        public int UploadedCount { get; set; }
        public int SkippedCount { get; set; }
        public string Message { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
    }

    public sealed class DatabaseSyncService
    {
        private static readonly Lazy<DatabaseSyncService> _instance = new(() => new DatabaseSyncService());
        public static DatabaseSyncService Instance => _instance.Value;

        private readonly SemaphoreSlim _syncLock = new(1, 1);
        private Timer? _backgroundTimer;
        private bool _isTimerStarted = false;

        public bool IsSyncing { get; private set; }

        public event Action<DatabaseSyncResult>? SyncCompleted;

        private DatabaseSyncService() { }

        public void StartBackgroundTimer()
        {
            if (_isTimerStarted) return;
            _isTimerStarted = true;

            // Delayed startup sync: wait 3 seconds after app launch
            _ = Task.Run(async () =>
            {
                await Task.Delay(3000);
                var config = ConfigurationService.Instance.GetConfig();
                if (config.AutoSyncDatabase && !string.IsNullOrWhiteSpace(config.DbAddress))
                {
                    await SyncNowAsync();
                }
            });

            // Recurring timer: check every 30 minutes if daily sync is due
            _backgroundTimer = new Timer(async _ =>
            {
                var config = ConfigurationService.Instance.GetConfig();
                if (!config.AutoSyncDatabase || string.IsNullOrWhiteSpace(config.DbAddress)) return;

                var lastSync = config.LastDatabaseSyncTimestamp ?? DateTime.MinValue;
                if ((DateTime.Now - lastSync).TotalHours >= 23.0)
                {
                    await SyncNowAsync();
                }
            }, null, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
        }

        public async Task<DatabaseSyncResult> SyncNowAsync(IProgress<string>? progress = null)
        {
            if (!await _syncLock.WaitAsync(100))
            {
                return new DatabaseSyncResult
                {
                    Success = false,
                    Message = "A database synchronization is already in progress."
                };
            }

            IsSyncing = true;
            progress?.Report("Checking database connection...");

            try
            {
                var config = ConfigurationService.Instance.GetConfig();
                if (string.IsNullOrWhiteSpace(config.DbAddress))
                {
                    var noAddrResult = new DatabaseSyncResult
                    {
                        Success = false,
                        Message = "Database address is not configured."
                    };
                    SyncCompleted?.Invoke(noAddrResult);
                    return noAddrResult;
                }

                // 1. Connection Health Check
                bool isConnected = await DatabaseService.Instance.TestConnectionAsync(
                    config.DbAddress, config.DbUser, config.DbPassword, config.DbName);

                if (!isConnected)
                {
                    var failResult = new DatabaseSyncResult
                    {
                        Success = false,
                        Message = $"Database offline ({config.DbAddress}). Sync deferred."
                    };
                    SyncCompleted?.Invoke(failResult);
                    return failResult;
                }

                progress?.Report("Querying existing database measurements...");
                var existingKeys = await DatabaseService.Instance.GetExistingMeasurementKeysAsync();

                progress?.Report("Scanning local measurement folders...");
                var localFiles = DiscoverLocalMeasurementFiles();

                int uploadedCount = 0;
                int skippedCount = 0;

                int total = localFiles.Count;
                int current = 0;

                foreach (var fileInfo in localFiles)
                {
                    current++;
                    string compositeKey = DatabaseService.BuildCompositeKey(
                        fileInfo.ProfileName, fileInfo.FolderName, fileInfo.FileName);

                    if (existingKeys.Contains(compositeKey))
                    {
                        skippedCount++;
                        continue;
                    }

                    progress?.Report($"Uploading ({current}/{total}): {fileInfo.FileName}...");

                    try
                    {
                        var parsedData = ParseLocalCsvFile(fileInfo.FullPath, fileInfo.SampleName);
                        if (parsedData != null)
                        {
                            await DatabaseService.Instance.SaveMeasurementRawAsync(
                                fileInfo.ProfileName,
                                parsedData.PlanName,
                                parsedData.SampleName,
                                parsedData.Timestamp,
                                fileInfo.FolderName,
                                fileInfo.FileName,
                                parsedData.Parameters,
                                parsedData.Points
                            );

                            existingKeys.Add(compositeKey);
                            uploadedCount++;
                        }
                        else
                        {
                            skippedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DatabaseSyncService] Failed to upload {fileInfo.FileName}: {ex.Message}");
                    }
                }

                config.LastDatabaseSyncTimestamp = DateTime.Now;
                await ConfigurationService.Instance.SaveAsync(config);

                var successResult = new DatabaseSyncResult
                {
                    Success = true,
                    UploadedCount = uploadedCount,
                    SkippedCount = skippedCount,
                    Message = uploadedCount > 0 
                        ? $"Database sync completed: {uploadedCount} new measurement(s) uploaded ({skippedCount} already in database)."
                        : $"Database sync completed: All {skippedCount} local measurements are up to date."
                };

                progress?.Report(successResult.Message);
                SyncCompleted?.Invoke(successResult);
                return successResult;
            }
            catch (Exception ex)
            {
                var errorResult = new DatabaseSyncResult
                {
                    Success = false,
                    Message = $"Database sync error: {ex.Message}",
                    Exception = ex
                };
                progress?.Report(errorResult.Message);
                SyncCompleted?.Invoke(errorResult);
                return errorResult;
            }
            finally
            {
                IsSyncing = false;
                _syncLock.Release();
            }
        }

        private class LocalMeasurementFileInfo
        {
            public string FullPath { get; set; } = string.Empty;
            public string ProfileName { get; set; } = string.Empty;
            public string SampleName { get; set; } = string.Empty;
            public string FolderName { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
        }

        private List<LocalMeasurementFileInfo> DiscoverLocalMeasurementFiles()
        {
            var results = new List<LocalMeasurementFileInfo>();
            var seenFullPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] basePaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                AppDomain.CurrentDomain.BaseDirectory
            };

            foreach (var basePath in basePaths)
            {
                try
                {
                    string rootSmuDir = Path.Combine(basePath, "SMU_Measurements");
                    if (!Directory.Exists(rootSmuDir)) continue;

                    var profileDirs = Directory.GetDirectories(rootSmuDir);
                    foreach (var profileDir in profileDirs)
                    {
                        string profileName = Path.GetFileName(profileDir);
                        if (string.IsNullOrWhiteSpace(profileName)) continue;

                        // 1. Check Wafermaps: SMU_Measurements/<Profile>/Wafermaps/<DeviceName>/<FolderName>/*.csv
                        string wafermapsRoot = Path.Combine(profileDir, "Wafermaps");
                        if (Directory.Exists(wafermapsRoot))
                        {
                            var deviceDirs = Directory.GetDirectories(wafermapsRoot);
                            foreach (var devDir in deviceDirs)
                            {
                                string devName = Path.GetFileName(devDir);
                                var scanDirs = Directory.GetDirectories(devDir);
                                foreach (var scanDir in scanDirs)
                                {
                                    string folderName = Path.GetFileName(scanDir);
                                    var csvFiles = Directory.GetFiles(scanDir, "*.csv");
                                    foreach (var file in csvFiles)
                                    {
                                        if (seenFullPaths.Add(file))
                                        {
                                            results.Add(new LocalMeasurementFileInfo
                                            {
                                                FullPath = file,
                                                ProfileName = profileName,
                                                SampleName = devName,
                                                FolderName = folderName,
                                                FileName = Path.GetFileName(file)
                                            });
                                        }
                                    }
                                }
                            }
                        }

                        // 2. Check Standard Measurement Folders: SMU_Measurements/<Profile>/<FolderName>/*.csv
                        var directSubDirs = Directory.GetDirectories(profileDir);
                        foreach (var subDir in directSubDirs)
                        {
                            string folderName = Path.GetFileName(subDir);
                            if (folderName.Equals("Wafermaps", StringComparison.OrdinalIgnoreCase)) continue;

                            var csvFiles = Directory.GetFiles(subDir, "*.csv");
                            foreach (var file in csvFiles)
                            {
                                if (seenFullPaths.Add(file))
                                {
                                    // Extract device name from folder or filename if available
                                    string sampleName = folderName;
                                    int underscoreIdx = folderName.LastIndexOf('_');
                                    if (underscoreIdx > 0 && folderName.Length - underscoreIdx == 9) // e.g. "DeviceA_20260818"
                                    {
                                        sampleName = folderName.Substring(0, underscoreIdx);
                                    }

                                    results.Add(new LocalMeasurementFileInfo
                                    {
                                        FullPath = file,
                                        ProfileName = profileName,
                                        SampleName = string.IsNullOrWhiteSpace(sampleName) ? "Empty Device" : sampleName,
                                        FolderName = folderName,
                                        FileName = Path.GetFileName(file)
                                    });
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DatabaseSyncService] Error scanning path {basePath}: {ex.Message}");
                }
            }

            return results;
        }

        private class ParsedCsvData
        {
            public string PlanName { get; set; } = "Measurement";
            public string SampleName { get; set; } = "Empty Device";
            public DateTime Timestamp { get; set; } = DateTime.Now;
            public Dictionary<string, string> Parameters { get; set; } = new();
            public List<CurvePoint> Points { get; set; } = new();
        }

        private ParsedCsvData? ParseLocalCsvFile(string filePath, string fallbackSampleName)
        {
            try
            {
                if (!File.Exists(filePath)) return null;

                var lines = File.ReadAllLines(filePath);
                if (lines.Length == 0) return null;

                var data = new ParsedCsvData
                {
                    SampleName = string.IsNullOrWhiteSpace(fallbackSampleName) ? "Empty Device" : fallbackSampleName,
                    Timestamp = File.GetLastWriteTime(filePath)
                };

                // Extract timestamp from filename if available (e.g. *_20260818_143000.csv)
                var filename = Path.GetFileNameWithoutExtension(filePath);
                var matchTimestamp = Regex.Match(filename, @"_(\d{8}_\d{6})$");
                if (matchTimestamp.Success)
                {
                    if (DateTime.TryParseExact(matchTimestamp.Groups[1].Value, "yyyyMMdd_HHmmss",
                        System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDt))
                    {
                        data.Timestamp = parsedDt;
                    }
                }

                bool isDataSection = false;
                bool isFirstDataHeader = true;

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("sep=")) continue;

                    if (trimmed.StartsWith("#"))
                    {
                        var parts = trimmed.Substring(1).Trim().Split('\t');
                        if (parts.Length < 2)
                        {
                            parts = trimmed.Substring(1).Trim().Split(new[] { ':', '=' }, 2);
                        }

                        if (parts.Length >= 2)
                        {
                            string key = parts[0].Trim();
                            string val = parts[1].Trim();

                            if (key.Equals("Plan", StringComparison.OrdinalIgnoreCase) || key.Equals("PlanName", StringComparison.OrdinalIgnoreCase))
                            {
                                data.PlanName = val;
                            }
                            else if (key.Equals("Sample", StringComparison.OrdinalIgnoreCase) || key.Equals("SampleName", StringComparison.OrdinalIgnoreCase) || key.Equals("Device", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!string.IsNullOrWhiteSpace(val)) data.SampleName = val;
                            }
                            else
                            {
                                data.Parameters[key] = val;
                            }
                        }
                        continue;
                    }

                    // Once we reach non-# lines, it's the data header or rows
                    if (!isDataSection)
                    {
                        isDataSection = true;
                    }

                    if (isFirstDataHeader)
                    {
                        isFirstDataHeader = false;
                        // Skip header row if it contains column names like Voltage, Current
                        if (!double.TryParse(trimmed.Split(new[] { '\t', ',' })[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
                        {
                            continue;
                        }
                    }

                    var pointParts = trimmed.Contains('\t') ? trimmed.Split('\t') : trimmed.Split(',');
                    if (pointParts.Length >= 2)
                    {
                        if (double.TryParse(pointParts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double x) &&
                            double.TryParse(pointParts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double y))
                        {
                            data.Points.Add(new CurvePoint(x, y));
                        }
                    }
                }

                return data;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseSyncService] Error parsing CSV {filePath}: {ex.Message}");
                return null;
            }
        }
    }
}
