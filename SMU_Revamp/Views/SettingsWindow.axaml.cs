using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SMU_Revamp.Services;
using SMU_Revamp.Interfaces;

namespace SMU_Revamp.Views;

public partial class SettingsWindow : Window
{
    private readonly IDeviceDebugService _debugService;

    public SettingsWindow()
    {
        InitializeComponent();
        _debugService = DeviceDebugService.Instance;
        this.Opened += (s, e) =>
        {
            if (DataContext is ViewModels.MainWindowViewModel vm)
            {
                vm.Settings.UpdateDriveStorageInfo();
                vm.Settings.DbConnectionTestMessage = string.Empty;
            }
        };
    }

    private bool IsExperimentBusy(out string reason)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            if (vm.IsScanningWafer)
            {
                reason = "Operation blocked: A wafer scan is currently running.";
                return true;
            }
            if (vm.IsQueueRunning)
            {
                reason = "Operation blocked: An experiment queue is currently running.";
                return true;
            }
            if (vm.IsMeasuring)
            {
                reason = "Operation blocked: A measurement is currently running.";
                return true;
            }
        }
        reason = string.Empty;
        return false;
    }

    private async void TestProberConnection_Click(object? sender, RoutedEventArgs e)
    {
        if (IsExperimentBusy(out var reason))
        {
            ProberOutputTextBox.Text = reason;
            return;
        }
        ProberOutputTextBox.Text = "Testing Prober connection...";
        var result = await _debugService.TestProberConnectionAsync();
        ProberOutputTextBox.Text = result;
    }



    private async void TestSwitchConnection_Click(object? sender, RoutedEventArgs e)
    {
        if (IsExperimentBusy(out var reason))
        {
            SwitchOutputTextBox.Text = reason;
            return;
        }
        SwitchOutputTextBox.Text = "Testing Switch Matrix connection...";
        var result = await _debugService.TestSwitchMatrixConnectionAsync();
        SwitchOutputTextBox.Text = result;
    }


    private async void CreateSwitchConnection_Click(object? sender, RoutedEventArgs e)
    {
        if (IsExperimentBusy(out var reason))
        {
            SwitchOutputTextBox.Text = reason;
            return;
        }
        var x = ConnectionXTextBox.Text ?? string.Empty;
        var y = ConnectionYTextBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(x) || string.IsNullOrWhiteSpace(y))
        {
            SwitchOutputTextBox.Text = "Error: Please specify endpoints X and Y to create a connection.";
            return;
        }

        SwitchOutputTextBox.Text = $"Creating connection between {x} and {y}...";
        var result = await _debugService.CreateSwitchMatrixConnectionAsync(x, y);
        SwitchOutputTextBox.Text = result;
    }

    private async void DisconnectSwitchConnection_Click(object? sender, RoutedEventArgs e)
    {
        if (IsExperimentBusy(out var reason))
        {
            SwitchOutputTextBox.Text = reason;
            return;
        }
        var x = ConnectionXTextBox.Text ?? string.Empty;
        var y = ConnectionYTextBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(x) || string.IsNullOrWhiteSpace(y))
        {
            SwitchOutputTextBox.Text = "Error: Please specify endpoints X and Y to disconnect.";
            return;
        }

        SwitchOutputTextBox.Text = $"Removing connection between {x} and {y}...";
        var result = await _debugService.RemoveSwitchMatrixConnectionAsync(x, y);
        SwitchOutputTextBox.Text = result;
    }

    private async void ClearAllSwitchConnections_Click(object? sender, RoutedEventArgs e)
    {
        if (IsExperimentBusy(out var reason))
        {
            SwitchOutputTextBox.Text = reason;
            return;
        }
        SwitchOutputTextBox.Text = "Clearing all Switch Matrix connections...";
        var result = await _debugService.ClearAllSwitchMatrixConnectionsAsync();
        SwitchOutputTextBox.Text = result;
    }


    private async void TestSMUConnection_Click(object? sender, RoutedEventArgs e)
    {
        if (IsExperimentBusy(out var reason))
        {
            SmuOutputTextBox.Text = reason;
            return;
        }
        SmuOutputTextBox.Text = "Testing SMU connection...";
        var result = await _debugService.TestSMUConnectionAsync();
        SmuOutputTextBox.Text = result;
    }

    private async void QuerySMUIdentity_Click(object? sender, RoutedEventArgs e)
    {
        if (IsExperimentBusy(out var reason))
        {
            SmuOutputTextBox.Text = reason;
            return;
        }
        SmuOutputTextBox.Text = "Querying SMU identity...";
        var result = await _debugService.QuerySMUIdentityAsync();
        SmuOutputTextBox.Text = result;
    }

    private async void ForceSmuVoltage_Click(object? sender, RoutedEventArgs e)
    {
        if (IsExperimentBusy(out var reason))
        {
            SmuOutputTextBox.Text = reason;
            return;
        }
        var channel = SmuChannelTextBox.Text ?? string.Empty;
        var voltStr = (SmuVoltageTextBox.Text ?? string.Empty).Replace(',', '.');
        var compStr = (SmuComplianceTextBox.Text ?? string.Empty).Replace(',', '.');
        var durStr = (SmuDurationTextBox.Text ?? string.Empty).Replace(',', '.');

        if (string.IsNullOrWhiteSpace(channel))
        {
            SmuOutputTextBox.Text = "Error: Please specify a channel.";
            return;
        }

        if (!double.TryParse(voltStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double voltage))
        {
            SmuOutputTextBox.Text = "Error: Voltage must be a valid number.";
            return;
        }

        if (!double.TryParse(compStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double compliance))
        {
            SmuOutputTextBox.Text = "Error: Compliance must be a valid number.";
            return;
        }

        if (!double.TryParse(durStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double duration) || duration <= 0)
        {
            SmuOutputTextBox.Text = "Error: Duration must be a positive number.";
            return;
        }

        SmuOutputTextBox.Text = $"Connecting and forcing {voltage:F3} V on channel {channel} for {duration:F1} seconds...";
        var result = await _debugService.ForceSMUDCVoltageAsync(channel, voltage, compliance, duration);
        SmuOutputTextBox.Text = result;
    }

    private void ResetButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            vm.Settings.ResetSettings();
        }
    }

    private async void ApplyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            await vm.Settings.ApplySettingsAsync();
            await Task.Delay(500);
            Close();
        }
    }


    private async void TestDbConnectionButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            await vm.Settings.TestDbConnectionAsync();
        }
    }

    private async void SyncDbNowButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            await vm.Settings.SyncDatabaseNowAsync();
        }
    }

    private async void PlayProgressDemo_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            await vm.StartDemoProgressAsync();
        }
    }

    private void ToggleIndeterminateDemo_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            vm.ToggleIndeterminateDemoProgress();
        }
    }

    private async void CleanUpLocalStorageNowButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            await vm.Settings.RequestCleanUpLocalFilesNowAsync();
        }
    }

    private static string? GetSafePath(IStorageItem? item)
    {
        if (item == null) return null;

        try
        {
            var localPath = item.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                if (localPath.Length == 2 && char.IsLetter(localPath[0]) && localPath[1] == ':')
                {
                    localPath += "\\";
                }
                return localPath;
            }
        }
        catch { }

        try
        {
            var uri = item.Path;
            if (uri != null)
            {
                string raw;
                if (uri.IsAbsoluteUri)
                {
                    raw = uri.IsFile ? uri.LocalPath : uri.ToString();
                }
                else
                {
                    raw = Uri.UnescapeDataString(uri.OriginalString);
                }

                if (!string.IsNullOrWhiteSpace(raw))
                {
                    if (raw.Length == 2 && char.IsLetter(raw[0]) && raw[1] == ':')
                    {
                        raw += "\\";
                    }
                    return raw;
                }
            }
        }
        catch { }

        return null;
    }

    private async void BrowseMeasurementsDirectory_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is ViewModels.MainWindowViewModel vm)
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                IStorageFolder? startLocation = null;
                try
                {
                    var curPath = !string.IsNullOrWhiteSpace(vm.Settings.MeasurementsDirectory)
                        ? vm.Settings.MeasurementsDirectory
                        : vm.Settings.DefaultMeasurementsDirectory;
                    if (!string.IsNullOrWhiteSpace(curPath) && System.IO.Directory.Exists(curPath))
                    {
                        startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(curPath);
                    }
                }
                catch { }

                var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Measurements / Sync Directory",
                    SuggestedStartLocation = startLocation,
                    AllowMultiple = false
                });

                if (result != null && result.Count > 0)
                {
                    var selectedPath = GetSafePath(result[0]);
                    if (!string.IsNullOrWhiteSpace(selectedPath))
                    {
                        var root = System.IO.Path.GetPathRoot(selectedPath);
                        if (!string.IsNullOrWhiteSpace(root) && 
                            string.Equals(selectedPath.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                        {
                            selectedPath = System.IO.Path.Combine(root, "SMU_Measurements");
                        }
                        vm.Settings.MeasurementsDirectory = selectedPath;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsWindow] Error browsing measurements directory: {ex.Message}");
        }
    }

    private async void BrowseLogsDirectory_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is ViewModels.MainWindowViewModel vm)
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                IStorageFolder? startLocation = null;
                try
                {
                    var curPath = !string.IsNullOrWhiteSpace(vm.Settings.LogsDirectory)
                        ? vm.Settings.LogsDirectory
                        : vm.Settings.DefaultLogsDirectory;
                    if (!string.IsNullOrWhiteSpace(curPath) && System.IO.Directory.Exists(curPath))
                    {
                        startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(curPath);
                    }
                }
                catch { }

                var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Application Logs Directory",
                    SuggestedStartLocation = startLocation,
                    AllowMultiple = false
                });

                if (result != null && result.Count > 0)
                {
                    var selectedPath = GetSafePath(result[0]);
                    if (!string.IsNullOrWhiteSpace(selectedPath))
                    {
                        var root = System.IO.Path.GetPathRoot(selectedPath);
                        if (!string.IsNullOrWhiteSpace(root) && 
                            string.Equals(selectedPath.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                        {
                            selectedPath = System.IO.Path.Combine(root, "SMU_Revamp_logs");
                        }
                        vm.Settings.LogsDirectory = selectedPath;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsWindow] Error browsing logs directory: {ex.Message}");
        }
    }
}
