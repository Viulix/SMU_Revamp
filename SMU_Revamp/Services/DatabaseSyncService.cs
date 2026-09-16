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
        public int DeletedLocalCount { get; set; }
        public long FreedBytes { get; set; }
        public string Message { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
    }

    public class LocalCleanupPreview
    {
        public bool Success { get; set; }
        public int EligibleCount { get; set; }
        public long TotalBytes { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class LocalCleanupResult
    {
        public bool Success { get; set; }
        public int DeletedCount { get; set; }
        public long FreedBytes { get; set; }
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

                int deletedLocalCount = 0;
                long freedBytes = 0;

                if (config.AutoCleanupSyncedLocalFiles && config.CleanupRetentionDays > 0)
                {
                    ReportProgress("Cleaning up old synchronized local files...", total, total, true, progress);
                    var cleanupRes = await CleanupSyncedLocalFilesInternalAsync(
                        config.CleanupRetentionDays,
                        config.CleanupOnlyWafermaps,
                        existingKeys,
                        progress);

                    deletedLocalCount = cleanupRes.DeletedCount;
                    freedBytes = cleanupRes.FreedBytes;
                }

                config.LastDatabaseSyncTimestamp = DateTime.Now;
                await ConfigurationService.Instance.SaveAsync(config);

                string syncSummaryMsg;
                if (uploadedCount > 0)
                {
                    syncSummaryMsg = $"Database sync completed: {uploadedCount} new measurement(s) uploaded ({skippedCount} already in database).";
                }
                else
                {
                    syncSummaryMsg = $"Database sync completed: All {skippedCount} local measurements are up to date.";
                }

                if (deletedLocalCount > 0)
                {
                    syncSummaryMsg += $" Cleaned up {deletedLocalCount} old local file(s) ({FormatBytes(freedBytes)} freed).";
                }

                var successResult = new DatabaseSyncResult
                {
                    Success = true,
                    Status = DatabaseConnectionStatus.Connected,
                    UploadedCount = uploadedCount,
                    SkippedCount = skippedCount,
                    DeletedLocalCount = deletedLocalCount,
                    FreedBytes = freedBytes,
                    Message = syncSummaryMsg
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

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        public async Task<LocalCleanupPreview> PreviewLocalCleanupAsync(int retentionDays, bool onlyWafermaps)
        {
            try
            {
                var config = ConfigurationService.Instance.GetConfig();
                if (string.IsNullOrWhiteSpace(config.DbAddress))
                {
                    return new LocalCleanupPreview
                    {
                        Success = false,
                        Message = "Database address is not configured."
                    };
                }

                var connResult = await DatabaseService.Instance.TestConnectionDetailedAsync(
                    config.DbAddress, config.DbUser, config.DbPassword, config.DbName);

                if (!connResult.Success)
                {
                    return new LocalCleanupPreview
                    {
                        Success = false,
                        Message = $"Database connection failed: {connResult.Message}"
                    };
                }

                var existingKeys = await DatabaseService.Instance.GetExistingMeasurementKeysAsync();
                var localFiles = DiscoverLocalMeasurementFiles();

                int eligibleCount = 0;
                long totalBytes = 0;
                var now = DateTime.Now;

                foreach (var file in localFiles)
                {
                    if (onlyWafermaps && !file.IsWafermap) continue;
                    if ((now - file.Timestamp).TotalDays < retentionDays) continue;

                    string compositeKey = DatabaseService.BuildCompositeKey(
                        file.ProfileName, file.FolderName, file.FileName);

                    if (existingKeys.Contains(compositeKey))
                    {
                        eligibleCount++;
                        totalBytes += file.FileSizeBytes;
                    }
                }

                return new LocalCleanupPreview
                {
                    Success = true,
                    EligibleCount = eligibleCount,
                    TotalBytes = totalBytes,
                    Message = eligibleCount > 0
                        ? $"Found {eligibleCount} file(s) ({FormatBytes(totalBytes)}) ready for cleanup."
                        : $"No files older than {retentionDays} day(s) eligible for cleanup."
                };
            }
            catch (Exception ex)
            {
                return new LocalCleanupPreview
                {
                    Success = false,
                    Message = $"Error previewing cleanup: {ex.Message}"
                };
            }
        }

        public async Task<LocalCleanupResult> CleanupSyncedLocalFilesAsync(
            int retentionDays, 
            bool onlyWafermaps, 
            IProgress<string>? progress = null)
        {
            try
            {
                var config = ConfigurationService.Instance.GetConfig();
                if (string.IsNullOrWhiteSpace(config.DbAddress))
                {
                    return new LocalCleanupResult
                    {
                        Success = false,
                        Message = "Database address is not configured."
                    };
                }

                var connResult = await DatabaseService.Instance.TestConnectionDetailedAsync(
                    config.DbAddress, config.DbUser, config.DbPassword, config.DbName);

                if (!connResult.Success)
                {
                    return new LocalCleanupResult
                    {
                        Success = false,
                        Message = $"Database connection failed: {connResult.Message}"
                    };
                }

                var existingKeys = await DatabaseService.Instance.GetExistingMeasurementKeysAsync();
                return await CleanupSyncedLocalFilesInternalAsync(retentionDays, onlyWafermaps, existingKeys, progress);
            }
            catch (Exception ex)
            {
                return new LocalCleanupResult
                {
                    Success = false,
                    Message = $"Cleanup failed: {ex.Message}",
                    Exception = ex
                };
            }
        }

        private async Task<LocalCleanupResult> CleanupSyncedLocalFilesInternalAsync(
            int retentionDays, 
            bool onlyWafermaps, 
            HashSet<string> existingKeys, 
            IProgress<string>? progress = null)
        {
            int deletedCount = 0;
            long freedBytes = 0;
            var affectedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var now = DateTime.Now;

            var localFiles = DiscoverLocalMeasurementFiles();

            var eligibleFiles = new List<LocalMeasurementFileInfo>();
            foreach (var file in localFiles)
            {
                if (onlyWafermaps && !file.IsWafermap) continue;
                if ((now - file.Timestamp).TotalDays < retentionDays) continue;

                string compositeKey = DatabaseService.BuildCompositeKey(
                    file.ProfileName, file.FolderName, file.FileName);

                // ABSOLUTE SAFETY CHECK: Must exist in existingKeys from MySQL!
                if (existingKeys.Contains(compositeKey))
                {
                    eligibleFiles.Add(file);
                }
            }

            int totalEligible = eligibleFiles.Count;
            int current = 0;

            foreach (var file in eligibleFiles)
            {
                current++;
                try
                {
                    if (File.Exists(file.FullPath))
                    {
                        long size = 0;
                        try { size = new FileInfo(file.FullPath).Length; } catch { }

                        var dir = Path.GetDirectoryName(file.FullPath);
                        if (!string.IsNullOrWhiteSpace(dir))
                        {
                            affectedDirs.Add(dir);
                        }

                        File.Delete(file.FullPath);
                        deletedCount++;
                        freedBytes += size;
                        progress?.Report($"Cleaned up ({current}/{totalEligible}): {file.FileName}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DatabaseSyncService] Failed to delete {file.FullPath}: {ex.Message}");
                }
            }

            // Prune empty directories (deepest first)
            foreach (var dir in affectedDirs.OrderByDescending(d => d.Length))
            {
                var stopDir = FindCleanupStopDirectory(dir);
                PruneEmptyDirectories(dir, stopDir);
            }

            string msg = deletedCount > 0
                ? $"Cleaned up {deletedCount} synchronized local file(s) ({FormatBytes(freedBytes)} freed)."
                : $"No synchronized files older than {retentionDays} day(s) were found to delete.";

            return new LocalCleanupResult
            {
                Success = true,
                DeletedCount = deletedCount,
                FreedBytes = freedBytes,
                Message = msg
            };
        }

        private static string FindCleanupStopDirectory(string dir)
        {
            var current = new DirectoryInfo(dir);
            while (current != null)
            {
                if (string.Equals(current.Name, "Wafermaps", StringComparison.OrdinalIgnoreCase))
                {
                    return current.FullName;
                }
                if (string.Equals(current.Parent?.Name, "SMU_Measurements", StringComparison.OrdinalIgnoreCase))
                {
                    return current.FullName; // Profile directory
                }
                current = current.Parent;
            }
            return dir;
        }

        private static void PruneEmptyDirectories(string dirPath, string stopAtDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath)) return;

                string normalizedDirPath = Path.GetFullPath(dirPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string normalizedStopDir = Path.GetFullPath(stopAtDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (string.Equals(normalizedDirPath, normalizedStopDir, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // If not empty, do not delete
                if (Directory.EnumerateFileSystemEntries(dirPath).Any())
                {
                    return;
                }

                Directory.Delete(dirPath, false);

                var parent = Path.GetDirectoryName(dirPath);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    PruneEmptyDirectories(parent, stopAtDir);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseSyncService] Failed to prune empty directory {dirPath}: {ex.Message}");
            }
        }

        private class LocalMeasurementFileInfo
        {
            public string FullPath { get; set; } = string.Empty;
            public string ProfileName { get; set; } = string.Empty;
            public string SampleName { get; set; } = string.Empty;
            public string FolderName { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public bool IsWafermap { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
            public long FileSizeBytes { get; set; }
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

                            bool isWafermap = (segments.Length > 0 && segments[0].Equals("Wafermaps", StringComparison.OrdinalIgnoreCase)) ||
                                              file.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }).Any(s => s.Equals("Wafermaps", StringComparison.OrdinalIgnoreCase));

                            DateTime fileTimestamp = File.GetLastWriteTime(file);
                            var filenameNoExt = Path.GetFileNameWithoutExtension(file);
                            var matchTimestamp = Regex.Match(filenameNoExt, @"_(\d{8}_\d{6})$");
                            if (matchTimestamp.Success)
                            {
                                if (DateTime.TryParseExact(matchTimestamp.Groups[1].Value, "yyyyMMdd_HHmmss",
                                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDt))
                                {
                                    fileTimestamp = parsedDt;
                                }
                            }

                            long fileSize = 0;
                            try { fileSize = new FileInfo(file).Length; } catch { }

                            results.Add(new LocalMeasurementFileInfo
                            {
                                FullPath = file,
                                ProfileName = profileName,
                                SampleName = sampleName,
                                FolderName = folderName,
                                FileName = Path.GetFileName(file),
                                IsWafermap = isWafermap,
                                Timestamp = fileTimestamp,
                                FileSizeBytes = fileSize
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
