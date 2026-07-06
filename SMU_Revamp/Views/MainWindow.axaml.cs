using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SMU_Revamp.ViewModels;
using SMU_Revamp.Services;

namespace SMU_Revamp.Views;

public partial class MainWindow : Window
{
    private Avalonia.Controls.Notifications.WindowNotificationManager? _notificationManager;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);

        _notificationManager = new Avalonia.Controls.Notifications.WindowNotificationManager(this)
        {
            Position = Avalonia.Controls.Notifications.NotificationPosition.BottomRight,
            MaxItems = 3
        };

        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            vm.NotificationRequested += ShowNotification;
        }
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            vm.NotificationRequested -= ShowNotification;
        }
        base.OnUnloaded(e);
    }

    private void ShowNotification(string title, string message, string? filePath)
    {
        if (_notificationManager == null) return;

        _notificationManager.Show(new Avalonia.Controls.Notifications.Notification(
            title,
            message,
            Avalonia.Controls.Notifications.NotificationType.Success,
            System.TimeSpan.FromSeconds(6),
            onClick: () =>
            {
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    OpenExplorerForFile(filePath);
                }
            }
        ));
    }

    private void OpenExplorerForFile(string filePath)
    {
        try
        {
            if (System.IO.File.Exists(filePath))
            {
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                }
                else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                {
                    System.Diagnostics.Process.Start("open", $"-R \"{filePath}\"");
                }
                else
                {
                    // Linux: open containing folder
                    var folder = System.IO.Path.GetDirectoryName(filePath);
                    if (folder != null)
                    {
                        System.Diagnostics.Process.Start("xdg-open", $"\"{folder}\"");
                    }
                }
            }
            else
            {
                // Fallback to folder
                var folder = System.IO.Path.GetDirectoryName(filePath);
                if (folder != null && System.IO.Directory.Exists(folder))
                {
                    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"");
                    }
                    else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                    {
                        System.Diagnostics.Process.Start("open", $"\"{folder}\"");
                    }
                    else
                    {
                        System.Diagnostics.Process.Start("xdg-open", $"\"{folder}\"");
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open explorer: {ex.Message}");
        }
    }

    private async void DebugButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var debugWindow = new DebugWindow
        {
            DataContext = this.DataContext
        };
        await debugWindow.ShowDialog(this);
    }

    private async void SettingsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow();
        await settingsWindow.ShowDialog(this);
    }

    private async void DefaultsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var defaultsWindow = new DefaultsWindow();
        await defaultsWindow.ShowDialog(this);
    }

    private async void ExportCsvButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Plot Data to CSV",
                DefaultExtension = "csv",
                SuggestedFileName = "measurement_data.csv",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("CSV Files (*.csv)") { Patterns = new[] { "*.csv" } }
                }
            });

            if (file != null)
            {
                var path = file.Path.LocalPath;
                await vm.SaveCurvePointsToCsvAsync(path);
            }
        }
    }

    private async void ImportFileButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Measurement File",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("CSV/TSV/TXT Files (*.csv;*.tsv;*.txt)") { Patterns = new[] { "*.csv", "*.tsv", "*.txt" } },
                    new FilePickerFileType("All Files (*.*)") { Patterns = new[] { "*" } }
                }
            });

            if (result != null && result.Count > 0)
            {
                var path = result[0].Path.LocalPath;
                await vm.ImportCurvePointsFromFileAsync(path);
            }
        }
    }

    private async void LoadFromDatabaseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            var dbViewModel = new ViewModels.DatabaseLoadViewModel();
            dbViewModel.RequestLoadMeasurement = async (id) => 
            {
                await vm.LoadMeasurementFromDatabaseAsync(id);
            };

            var dbWindow = new DatabaseLoadWindow(dbViewModel);
            await dbWindow.ShowDialog(this);
        }
    }

    private async void UploadToDatabaseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            await vm.UploadCurrentMeasurementToDatabaseAsync();
        }
    }

    private async void LoadResultFolderButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Wafer Scan Folder"
            });

            if (result != null && result.Count > 0)
            {
                var path = result[0].Path.LocalPath;
                await vm.LoadScanFolderAsync(path);
            }
        }
    }

    private void MeasurementScrollViewer_ScrollChanged(object? sender, Avalonia.Controls.ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            var indicator = this.FindControl<Border>("MeasurementScrollIndicator");
            if (indicator != null)
            {
                // Show indicator if there is still content to scroll down to
                bool canScrollDown = sv.Extent.Height > sv.Viewport.Height && 
                                     sv.Offset.Y < (sv.Extent.Height - sv.Viewport.Height) - 1.0;
                indicator.Opacity = canScrollDown ? 1.0 : 0.0;
            }
        }
    }

    private async void OpenAdvancedPlotSettingsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var settingsWindow = new AdvancedPlotSettingsWindow
        {
            DataContext = this.DataContext
        };
        await settingsWindow.ShowDialog(this);
    }

    private async void ManageParameterLink_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control control && control.DataContext is Models.MeasurementParameter parameter)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedPlan != null)
            {
                var dialog = new ParameterLinkDialog
                {
                    DataContext = new ParameterLinkViewModel(parameter, vm.SelectedPlan.Parameters)
                };

                var result = await dialog.ShowDialog<bool>(this);
                
                if (result)
                {
                    var linkVm = (ParameterLinkViewModel)dialog.DataContext;
                    
                    if (linkVm.SelectedTargetParameter != null)
                    {
                        parameter.LinkedParameter = linkVm.SelectedTargetParameter;
                        parameter.LinkedMultiplier = linkVm.Multiplier;
                        parameter.IsLinked = true;
                    }
                    else
                    {
                        parameter.LinkedParameter = null;
                        parameter.IsLinked = false;
                    }

                    // Save the link in config
                    var config = ConfigurationService.Instance.GetConfig();
                    if (!config.ParameterLinks.ContainsKey(vm.SelectedPlan.Name))
                    {
                        config.ParameterLinks[vm.SelectedPlan.Name] = new System.Collections.Generic.Dictionary<string, Models.ParameterLinkConfig>();
                    }

                    if (parameter.IsLinked && parameter.LinkedParameter != null)
                    {
                        config.ParameterLinks[vm.SelectedPlan.Name][parameter.Name] = new Models.ParameterLinkConfig
                        {
                            LinkedParameterName = parameter.LinkedParameter.Name,
                            Multiplier = parameter.LinkedMultiplier,
                            IsActive = true
                        };
                    }
                    else
                    {
                        config.ParameterLinks[vm.SelectedPlan.Name].Remove(parameter.Name);
                    }

                    await ConfigurationService.Instance.SaveAsync(config);
                }
            }
        }
    }

    private async void ToggleParameterLink_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control control && control.DataContext is Models.MeasurementParameter parameter)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedPlan != null)
            {
                var config = ConfigurationService.Instance.GetConfig();
                if (config.ParameterLinks != null && 
                    config.ParameterLinks.TryGetValue(vm.SelectedPlan.Name, out var planLinks) &&
                    planLinks.TryGetValue(parameter.Name, out var linkConfig))
                {
                    linkConfig.IsActive = !linkConfig.IsActive;

                    if (linkConfig.IsActive)
                    {
                        var targetParam = vm.SelectedPlan.Parameters.FirstOrDefault(p => p.Name == linkConfig.LinkedParameterName);
                        if (targetParam != null)
                        {
                            parameter.LinkedParameter = targetParam;
                            parameter.LinkedMultiplier = linkConfig.Multiplier;
                            parameter.IsLinked = true;
                        }
                    }
                    else
                    {
                        parameter.LinkedParameter = null;
                        parameter.IsLinked = false;
                    }

                    await ConfigurationService.Instance.SaveAsync(config);
                }
            }
        }
    }
}