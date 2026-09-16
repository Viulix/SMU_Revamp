using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SMU_Revamp.Models;
using SMU_Revamp.Services;
using SMU_Revamp.Interfaces;
using System;
using System.Threading.Tasks;

namespace SMU_Revamp.ViewModels
{
    /// <summary>
    /// ViewModel for the settings window that binds to service configuration values.
    /// </summary>
    public class SettingsViewModel : ViewModelBase
    {
        private readonly IProberService _proberService;
        private readonly SwitchMatrixService _switchMatrixService;
        private readonly ConfigurationService _configService;

        private bool _proberQuietMode = false;
        private string _proberResource = "GPIB0::22::INSTR";
        private int _proberTimeoutMs = 20000;
        private string _switchMatrixResource = "GPIB0::23::INSTR";
        private int _switchMatrixTimeoutMs = 5000;
        private string _smuResource = "GPIB0::17::INSTR";
        private int _smuTimeoutMs = 300000;
        private string _applyStatusMessage = string.Empty;

        private string _profile = string.Empty;
        private string _sampleName = string.Empty;
        private bool _showAlignmentWarning = true;

        // Database Configuration
        private string _dbAddress = "134.245.242.39";
        private string _dbUser = "root";
        private string _dbPassword = string.Empty;
        private string _dbName = "smu_measurements";
        private bool _saveToDatabase = false;

        public bool ProberQuietMode
        {
            get => _proberQuietMode;
            set => SetProperty(ref _proberQuietMode, value);
        }

        public string ProberResource
        {
            get => _proberResource;
            set => SetProperty(ref _proberResource, value ?? "GPIB0::22::INSTR");
        }

        public int ProberTimeoutMs
        {
            get => _proberTimeoutMs;
            set => SetProperty(ref _proberTimeoutMs, value);
        }

        public string SwitchMatrixResource
        {
            get => _switchMatrixResource;
            set => SetProperty(ref _switchMatrixResource, value ?? "GPIB0::23::INSTR");
        }

        public int SwitchMatrixTimeoutMs
        {
            get => _switchMatrixTimeoutMs;
            set => SetProperty(ref _switchMatrixTimeoutMs, value);
        }

        public string SMUResource
        {
            get => _smuResource;
            set => SetProperty(ref _smuResource, value ?? "GPIB0::17::INSTR");
        }

        public int SMUTimeoutMs
        {
            get => _smuTimeoutMs;
            set => SetProperty(ref _smuTimeoutMs, value);
        }

        public string Profile
        {
            get => _profile;
            set => SetProperty(ref _profile, value ?? string.Empty);
        }

        public string SampleName
        {
            get => _sampleName;
            set => SetProperty(ref _sampleName, value ?? string.Empty);
        }

        public string ApplyStatusMessage
        {
            get => _applyStatusMessage;
            set => SetProperty(ref _applyStatusMessage, value);
        }

        public bool ShowAlignmentWarning
        {
            get => _showAlignmentWarning;
            set => SetProperty(ref _showAlignmentWarning, value);
        }

        private bool _simulationMode = false;
        /// <summary>
        /// Runs all instrument connections in software simulation (no hardware).
        /// </summary>
        public bool SimulationMode
        {
            get => _simulationMode;
            set
            {
                if (SetProperty(ref _simulationMode, value))
                {
                    // Apply immediately so ConnectAsync picks it up without waiting
                    // for the next settings save.
                    E5263_SMU.Instance.SetSimulationMode(value);
                }
            }
        }

        public string DbAddress
        {
            get => _dbAddress;
            set => SetProperty(ref _dbAddress, value ?? string.Empty);
        }

        public string DbUser
        {
            get => _dbUser;
            set => SetProperty(ref _dbUser, value ?? string.Empty);
        }

        public string DbPassword
        {
            get => _dbPassword;
            set => SetProperty(ref _dbPassword, value ?? string.Empty);
        }

        public string DbName
        {
            get => _dbName;
            set => SetProperty(ref _dbName, value ?? string.Empty);
        }

        public bool SaveToDatabase
        {
            get => _saveToDatabase;
            set => SetProperty(ref _saveToDatabase, value);
        }

        private bool _autoSyncDatabase = true;
        public bool AutoSyncDatabase
        {
            get => _autoSyncDatabase;
            set => SetProperty(ref _autoSyncDatabase, value);
        }

        private System.DateTime? _lastDatabaseSyncTimestamp;
        public System.DateTime? LastDatabaseSyncTimestamp
        {
            get => _lastDatabaseSyncTimestamp;
            set => SetProperty(ref _lastDatabaseSyncTimestamp, value);
        }

        private string _syncStatusMessage = string.Empty;
        public string SyncStatusMessage
        {
            get => _syncStatusMessage;
            set => SetProperty(ref _syncStatusMessage, value);
        }

        private bool _isSyncingDatabase = false;
        public bool IsSyncingDatabase
        {
            get => _isSyncingDatabase;
            set => SetProperty(ref _isSyncingDatabase, value);
        }

        private double _syncProgressPercentage = 0;
        public double SyncProgressPercentage
        {
            get => _syncProgressPercentage;
            set => SetProperty(ref _syncProgressPercentage, value);
        }

        private bool _isSyncProgressIndeterminate = true;
        public bool IsSyncProgressIndeterminate
        {
            get => _isSyncProgressIndeterminate;
            set => SetProperty(ref _isSyncProgressIndeterminate, value);
        }

        // Local Storage Cleanup Configuration
        private bool _autoCleanupSyncedLocalFiles = false;
        public bool AutoCleanupSyncedLocalFiles
        {
            get => _autoCleanupSyncedLocalFiles;
            set => SetProperty(ref _autoCleanupSyncedLocalFiles, value);
        }

        private int _cleanupRetentionDays = 30;
        public int CleanupRetentionDays
        {
            get => _cleanupRetentionDays;
            set => SetProperty(ref _cleanupRetentionDays, Math.Max(1, value));
        }

        private bool _cleanupOnlyWafermaps = true;
        public bool CleanupOnlyWafermaps
        {
            get => _cleanupOnlyWafermaps;
            set => SetProperty(ref _cleanupOnlyWafermaps, value);
        }

        private bool _isCleaningUpLocalFiles = false;
        public bool IsCleaningUpLocalFiles
        {
            get => _isCleaningUpLocalFiles;
            set => SetProperty(ref _isCleaningUpLocalFiles, value);
        }

        private string _cleanupStatusMessage = string.Empty;
        public string CleanupStatusMessage
        {
            get => _cleanupStatusMessage;
            set => SetProperty(ref _cleanupStatusMessage, value);
        }

        private bool _isCleanupConfirmationVisible = false;
        public bool IsCleanupConfirmationVisible
        {
            get => _isCleanupConfirmationVisible;
            set => SetProperty(ref _isCleanupConfirmationVisible, value);
        }

        private string _cleanupConfirmationMessage = string.Empty;
        public string CleanupConfirmationMessage
        {
            get => _cleanupConfirmationMessage;
            set => SetProperty(ref _cleanupConfirmationMessage, value);
        }

        public IRelayCommand CancelCleanupCommand { get; }
        public IAsyncRelayCommand ConfirmCleanupCommand { get; }

        public SettingsViewModel()
        {
            // Get singleton instances
            _proberService = ProberService.Instance;
            _switchMatrixService = SwitchMatrixService.Instance;
            _configService = ConfigurationService.Instance;

            // Initialize bindings from service state
            ProberQuietMode = _proberService.QuietMode;
            SwitchMatrixTimeoutMs = _switchMatrixService.GetTimeout();
            SMUTimeoutMs = E5263_SMU.Instance.GetTimeout();

            // Load from config
            var config = _configService.GetConfig();
            ProberResource = config.ProberResource;
            ProberTimeoutMs = config.ProberTimeoutMs;
            SwitchMatrixResource = config.SwitchMatrixResource;
            SMUResource = config.SMUResource;
            Profile = string.Empty;
            SampleName = string.Empty;
            ShowAlignmentWarning = config.ShowAlignmentWarning;
            SimulationMode = config.SimulationMode;

            DbAddress = config.DbAddress;
            DbUser = config.DbUser;
            DbPassword = config.DbPassword;
            DbName = config.DbName;
            SaveToDatabase = config.SaveToDatabase;
            AutoSyncDatabase = config.AutoSyncDatabase;
            LastDatabaseSyncTimestamp = config.LastDatabaseSyncTimestamp;
            UpdateSyncStatusSummary();

            AutoCleanupSyncedLocalFiles = config.AutoCleanupSyncedLocalFiles;
            CleanupRetentionDays = config.CleanupRetentionDays > 0 ? config.CleanupRetentionDays : 30;
            CleanupOnlyWafermaps = config.CleanupOnlyWafermaps;

            CancelCleanupCommand = new RelayCommand(CancelCleanup);
            ConfirmCleanupCommand = new AsyncRelayCommand(ConfirmCleanupAsync);

            // Idempotent: repeated LoadSettings calls (settings reopened) must not
            // stack duplicate handlers on the static sync service.
            DatabaseSyncService.Instance.SyncCompleted -= OnSyncCompleted;
            DatabaseSyncService.Instance.SyncCompleted += OnSyncCompleted;
            DatabaseSyncService.Instance.SyncProgressChanged -= OnSyncProgressChanged;
            DatabaseSyncService.Instance.SyncProgressChanged += OnSyncProgressChanged;
            DatabaseSyncService.Instance.SyncStateChanged -= OnSyncStateChanged;
            DatabaseSyncService.Instance.SyncStateChanged += OnSyncStateChanged;
        }

        private void OnSyncProgressChanged(DatabaseSyncProgress progress)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                SyncStatusMessage = progress.StatusText;
                SyncProgressPercentage = progress.Percentage;
                IsSyncProgressIndeterminate = progress.IsIndeterminate;
            });
        }

        private void OnSyncStateChanged(bool isSyncing)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsSyncingDatabase = isSyncing;
                if (!isSyncing)
                {
                    IsSyncProgressIndeterminate = true;
                    SyncProgressPercentage = 0;
                }
            });
        }

        private void OnSyncCompleted(DatabaseSyncResult result)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var config = _configService.GetConfig();
                LastDatabaseSyncTimestamp = config.LastDatabaseSyncTimestamp;
                if (!IsSyncingDatabase)
                {
                    SyncStatusMessage = result.Success ? result.Message : $"Sync failed: {result.Message}";
                }
            });
        }

        private void UpdateSyncStatusSummary()
        {
            if (LastDatabaseSyncTimestamp.HasValue)
            {
                SyncStatusMessage = $"Last synced: {LastDatabaseSyncTimestamp.Value:yyyy-MM-dd HH:mm:ss}";
            }
            else
            {
                SyncStatusMessage = "No synchronization recorded yet.";
            }
        }

        /// <summary>
        /// Applies settings changes back to the services.
        /// </summary>
        public async Task ApplySettingsAsync()
        {
            _proberService.QuietMode = ProberQuietMode;
            _proberService.ResourceString = ProberResource;
            _switchMatrixService.ResourceString = SwitchMatrixResource;
            _switchMatrixService.SetTimeout(SwitchMatrixTimeoutMs);
            E5263_SMU.Instance.ResourceString = SMUResource;
            E5263_SMU.Instance.SetTimeout(SMUTimeoutMs);

            // Save to persistent configuration
            var config = _configService.GetConfig();
            config.ProberQuietMode = ProberQuietMode;
            config.ProberResource = ProberResource;
            config.ProberTimeoutMs = ProberTimeoutMs;
            config.SwitchMatrixResource = SwitchMatrixResource;
            config.SwitchMatrixTimeoutMs = SwitchMatrixTimeoutMs;
            config.SMUResource = SMUResource;
            config.SMUTimeoutMs = SMUTimeoutMs;
            config.ShowAlignmentWarning = ShowAlignmentWarning;
            config.SimulationMode = SimulationMode;

            config.DbAddress = DbAddress;
            config.DbUser = DbUser;
            config.DbPassword = DbPassword;
            config.DbName = DbName;
            config.SaveToDatabase = SaveToDatabase;
            config.AutoSyncDatabase = AutoSyncDatabase;

            config.AutoCleanupSyncedLocalFiles = AutoCleanupSyncedLocalFiles;
            config.CleanupRetentionDays = CleanupRetentionDays;
            config.CleanupOnlyWafermaps = CleanupOnlyWafermaps;

            await _configService.SaveAsync(config);
            ApplyStatusMessage = "Settings saved.";
        }

        /// <summary>
        /// Resets settings to service current values.
        /// </summary>
        public void ResetSettings()
        {
            ProberQuietMode = _proberService.QuietMode;
            SwitchMatrixTimeoutMs = _switchMatrixService.GetTimeout();
            SMUTimeoutMs = E5263_SMU.Instance.GetTimeout();
            var config = _configService.GetConfig();
            ProberResource = config.ProberResource;
            ProberTimeoutMs = config.ProberTimeoutMs;
            SwitchMatrixResource = config.SwitchMatrixResource;
            SMUResource = config.SMUResource;
            Profile = string.Empty;
            SampleName = string.Empty;
            ShowAlignmentWarning = config.ShowAlignmentWarning;
            SimulationMode = config.SimulationMode;

            DbAddress = config.DbAddress;
            DbUser = config.DbUser;
            DbPassword = config.DbPassword;
            DbName = config.DbName;
            SaveToDatabase = config.SaveToDatabase;
            AutoSyncDatabase = config.AutoSyncDatabase;
            LastDatabaseSyncTimestamp = config.LastDatabaseSyncTimestamp;
            UpdateSyncStatusSummary();

            AutoCleanupSyncedLocalFiles = config.AutoCleanupSyncedLocalFiles;
            CleanupRetentionDays = config.CleanupRetentionDays > 0 ? config.CleanupRetentionDays : 30;
            CleanupOnlyWafermaps = config.CleanupOnlyWafermaps;
            CleanupStatusMessage = string.Empty;
            IsCleanupConfirmationVisible = false;
        }

        public async Task TestDbConnectionAsync()
        {
            ApplyStatusMessage = "Testing connection...";
            var result = await DatabaseService.Instance.TestConnectionDetailedAsync(DbAddress, DbUser, DbPassword, DbName);
            if (result.Success)
            {
                ApplyStatusMessage = "Database connection successful. Tables initialized.";
            }
            else if (result.Status == DatabaseConnectionStatus.AccessDenied)
            {
                ApplyStatusMessage = $"Access Denied: {result.Message}";
            }
            else
            {
                ApplyStatusMessage = $"Connection failed ({result.Status}): {result.Message}";
            }
        }

        public async Task SyncDatabaseNowAsync()
        {
            if (IsSyncingDatabase) return;

            // Automatically apply current settings from text fields before syncing
            await ApplySettingsAsync();

            IsSyncingDatabase = true;
            IsSyncProgressIndeterminate = true;
            SyncProgressPercentage = 0;
            SyncStatusMessage = "Starting synchronization...";

            var progress = new System.Progress<string>(msg =>
            {
                if (IsSyncingDatabase)
                {
                    SyncStatusMessage = msg;
                }
            });

            var result = await DatabaseSyncService.Instance.SyncNowAsync(progress);
            IsSyncingDatabase = false;
            IsSyncProgressIndeterminate = true;
            
            var config = _configService.GetConfig();
            LastDatabaseSyncTimestamp = config.LastDatabaseSyncTimestamp;

            SyncStatusMessage = result.Success ? result.Message : $"Sync failed: {result.Message}";
            ApplyStatusMessage = result.Success ? result.Message : $"Sync error: {result.Message}";
        }

        public async Task RequestCleanUpLocalFilesNowAsync()
        {
            if (IsCleaningUpLocalFiles || IsSyncingDatabase) return;

            IsCleaningUpLocalFiles = true;
            CleanupStatusMessage = "Checking database connection and scanning local files...";

            var preview = await DatabaseSyncService.Instance.PreviewLocalCleanupAsync(
                CleanupRetentionDays, CleanupOnlyWafermaps);

            if (!preview.Success)
            {
                CleanupStatusMessage = $"Cleanup check failed: {preview.Message}";
                IsCleaningUpLocalFiles = false;
                return;
            }

            if (preview.EligibleCount == 0)
            {
                CleanupStatusMessage = $"No local files older than {CleanupRetentionDays} day(s) were found that are already synced to the database.";
                IsCleaningUpLocalFiles = false;
                return;
            }

            string scopeDesc = CleanupOnlyWafermaps ? "wafermap" : "measurement";
            CleanupConfirmationMessage = $"Found {preview.EligibleCount} local {scopeDesc} file(s) older than {CleanupRetentionDays} day(s) that are already saved in the database ({DatabaseSyncService.FormatBytes(preview.TotalBytes)}).\n\nDo you want to permanently delete these local files to free up disk space?";
            IsCleanupConfirmationVisible = true;
        }

        private void CancelCleanup()
        {
            IsCleanupConfirmationVisible = false;
            IsCleaningUpLocalFiles = false;
            CleanupStatusMessage = "Cleanup cancelled by user.";
        }

        private async Task ConfirmCleanupAsync()
        {
            IsCleanupConfirmationVisible = false;
            CleanupStatusMessage = "Deleting verified synchronized local files...";

            var progress = new System.Progress<string>(msg =>
            {
                CleanupStatusMessage = msg;
            });

            var result = await DatabaseSyncService.Instance.CleanupSyncedLocalFilesAsync(
                CleanupRetentionDays, CleanupOnlyWafermaps, progress);

            CleanupStatusMessage = result.Message;
            IsCleaningUpLocalFiles = false;
        }
    }
}

