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
    public record DatabaseSyncProgress(
        string StatusText,
        int Current,
        int Total,
        double Percentage,
        bool IsIndeterminate
    );

    public class DatabaseSyncResult
    {
        public bool Success { get; set; }
        public bool IsAccessDenied { get; set; }
        public DatabaseConnectionStatus Status { get; set; } = DatabaseConnectionStatus.Connected;
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
        public event Action<DatabaseSyncProgress>? SyncProgressChanged;
        public event Action<bool>? SyncStateChanged;

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

        private void ReportProgress(string text, int current, int total, bool isIndeterminate, IProgress<string>? progress)
        {
            double percentage = total > 0 ? Math.Clamp((double)current / total * 100.0, 0.0, 100.0) : 0.0;
            var p = new DatabaseSyncProgress(text, current, total, percentage, isIndeterminate);
            SyncProgressChanged?.Invoke(p);
            progress?.Report(text);
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
            SyncStateChanged?.Invoke(true);
            ReportProgress("Checking database connection...", 0, 0, true, progress);

            try
            {
                var config = ConfigurationService.Instance.GetConfig();
                if (string.IsNullOrWhiteSpace(config.DbAddress))
                {
                    var noAddrResult = new DatabaseSyncResult
                    {
                        Success = false,
                        Status = DatabaseConnectionStatus.ConfigurationMissing,
                        Message = "Database address is not configured."
                    };
                    ReportProgress(noAddrResult.Message, 0, 0, false, progress);
                    SyncCompleted?.Invoke(noAddrResult);
                    return noAddrResult;
                }

                // 1. Connection Health Check
                var connResult = await DatabaseService.Instance.TestConnectionDetailedAsync(
                    config.DbAddress, config.DbUser, config.DbPassword, config.DbName);

                if (!connResult.Success)
                {
                    bool isAccessDenied = connResult.Status == DatabaseConnectionStatus.AccessDenied;
                    string message = connResult.Status switch
                    {
                        DatabaseConnectionStatus.AccessDenied => $"Database Access Denied ({config.DbAddress}): {connResult.Message}",
                        DatabaseConnectionStatus.ConfigurationMissing => $"Database configuration missing: {connResult.Message}",
                        DatabaseConnectionStatus.Offline => $"Database server offline or unreachable ({config.DbAddress}): {connResult.Message}",
                        _ => $"Database error ({config.DbAddress}): {connResult.Message}"
                    };

                    var failResult = new DatabaseSyncResult
                    {
                        Success = false,
                        Status = connResult.Status,
                        IsAccessDenied = isAccessDenied,
                        Message = message
                    };
                    ReportProgress(message, 0, 0, false, progress);
                    SyncCompleted?.Invoke(failResult);
                    return failResult;
                }

                ReportProgress("Querying existing database measurements...", 0, 0, true, progress);
                var existingKeys = await DatabaseService.Instance.GetExistingMeasurementKeysAsync();

                ReportProgress("Scanning local measurement folders...", 0, 0, true, progress);
                var localFiles = DiscoverLocalMeasurementFiles();

                int uploadedCount = 0;
                int skippedCount = 0;

                int total = localFiles.Count;
                int current = 0;

                if (total == 0)
                {
                    ReportProgress("No local measurement files found to sync.", 0, 0, false, progress);
                }

                foreach (var fileInfo in localFiles)
                {
                    current++;
                    string compositeKey = DatabaseService.BuildCompositeKey(
                        fileInfo.ProfileName, fileInfo.FolderName, fileInfo.FileName);

                    if (existingKeys.Contains(compositeKey))
                    {
                        skippedCount++;
                        ReportProgress($"Checked ({current}/{total}): {fileInfo.FileName} (already synced)", current, total, false, progress);
                        continue;
                    }

                    ReportProgress($"Uploading ({current}/{total}): {fileInfo.FileName}...", current, total, false, progress);

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
                    Status = DatabaseConnectionStatus.Connected,
                    UploadedCount = uploadedCount,
                    SkippedCount = skippedCount,
                    Message = uploadedCount > 0 
                        ? $"Database sync completed: {uploadedCount} new measurement(s) uploaded ({skippedCount} already in database)."
                        : $"Database sync completed: All {skippedCount} local measurements are up to date."
                };

                ReportProgress(successResult.Message, total, total, false, progress);
                SyncCompleted?.Invoke(successResult);
                return successResult;
            }
            catch (Exception ex)
            {
                var errorResult = new DatabaseSyncResult
                {
                    Success = false,
                    Status = DatabaseConnectionStatus.Error,
                    Message = $"Database sync error: {ex.Message}",
                    Exception = ex
                };
                ReportProgress(errorResult.Message, 0, 0, false, progress);
                SyncCompleted?.Invoke(errorResult);
                return errorResult;
            }
            finally
            {
                IsSyncing = false;
                SyncStateChanged?.Invoke(false);
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

            var basePaths = new List<string>();

            void TryAddBasePath(string? path)
            {
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && !basePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    basePaths.Add(path);
                }
            }

            try { TryAddBasePath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)); } catch { }
            try { TryAddBasePath(AppDomain.CurrentDomain.BaseDirectory); } catch { }
            try { TryAddBasePath(AppContext.BaseDirectory); } catch { }
            try { TryAddBasePath(Directory.GetCurrentDirectory()); } catch { }

            try
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrWhiteSpace(userProfile) && Directory.Exists(userProfile))
                {
                    TryAddBasePath(Path.Combine(userProfile, "Documents"));
                    TryAddBasePath(Path.Combine(userProfile, "OneDrive", "Documents"));
                    TryAddBasePath(userProfile);
                }
            }
            catch { }

            try { TryAddBasePath(Environment.GetFolderPath(Environment.SpecialFolder.Desktop)); } catch { }

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

                        string[] allCsvFiles;
                        try
                        {
                            allCsvFiles = Directory.GetFiles(profileDir, "*.csv", SearchOption.AllDirectories);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[DatabaseSyncService] Failed to get files in {profileDir}: {ex.Message}");
                            continue;
                        }

                        foreach (var file in allCsvFiles)
                        {
                            if (!seenFullPaths.Add(file)) continue;

                            string relPath = Path.GetRelativePath(profileDir, file);
                            string[] segments = relPath.Split(
                                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, 
                                StringSplitOptions.RemoveEmptyEntries);

                            string sampleName = "Empty Device";
                            string folderName = "General";

                            if (segments.Length <= 1)
                            {
                                // SMU_Measurements/<Profile>/file.csv
                                folderName = "General";
                                sampleName = "Empty Device";
                            }
                            else if (segments.Length == 2)
                            {
                                // SMU_Measurements/<Profile>/<FolderName>/file.csv
                                if (segments[0].Equals("Wafermaps", StringComparison.OrdinalIgnoreCase))
                                {
                                    folderName = "Wafermaps";
                                    sampleName = "Empty Device";
                                }
                                else
                                {
                                    folderName = segments[0];
                                    sampleName = ExtractSampleName(folderName);
                                }
                            }
                            else if (segments.Length == 3)
                            {
                                if (segments[0].Equals("Wafermaps", StringComparison.OrdinalIgnoreCase))
                                {
                                    // SMU_Measurements/<Profile>/Wafermaps/<ScanOrDevice>/file.csv
                                    if (segments[1].StartsWith("Scan_", StringComparison.OrdinalIgnoreCase))
                                    {
                                        sampleName = "Empty Device";
                                        folderName = segments[1];
                                    }
                                    else
                                    {
                                        sampleName = segments[1];
                                        folderName = segments[1];
                                    }
                                }
                                else
                                {
                                    // SMU_Measurements/<Profile>/<DeviceName>/<FolderName>/file.csv
                                    sampleName = segments[0];
                                    folderName = segments[1];
                                }
                            }
                            else // segments.Length >= 4
                            {
                                if (segments[0].Equals("Wafermaps", StringComparison.OrdinalIgnoreCase))
                                {
                                    // SMU_Measurements/<Profile>/Wafermaps/<DeviceName>/<FolderName>/.../file.csv
                                    sampleName = segments[1];
                                    folderName = segments[2];
                                }
                                else
                                {
                                    sampleName = segments[0];
                                    folderName = segments[1];
                                }
                            }

                            if (string.IsNullOrWhiteSpace(sampleName))
                            {
                                sampleName = "Empty Device";
                            }

                            results.Add(new LocalMeasurementFileInfo
                            {
                                FullPath = file,
                                ProfileName = profileName,
                                SampleName = sampleName,
                                FolderName = folderName,
                                FileName = Path.GetFileName(file)
                            });
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

        private static string ExtractSampleName(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName)) return "Empty Device";
            int underscoreIdx = folderName.LastIndexOf('_');
            if (underscoreIdx > 0 && folderName.Length - underscoreIdx == 9) // e.g. "DeviceA_20260818"
            {
                return folderName.Substring(0, underscoreIdx);
            }
            return folderName;
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
