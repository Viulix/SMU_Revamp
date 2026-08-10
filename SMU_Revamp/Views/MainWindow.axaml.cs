using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using SMU_Revamp.ViewModels;

namespace SMU_Revamp.Views;

public partial class MainWindow : Window
{
    private WindowNotificationManager? _topNotificationManager;
    private WindowNotificationManager? _cornerNotificationManager;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);

        _topNotificationManager = new WindowNotificationManager(this)
        {
            Position = NotificationPosition.TopCenter,
            MaxItems = 3
        };

        _cornerNotificationManager = new WindowNotificationManager(this)
        {
            Position = NotificationPosition.BottomRight,
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

    private void ShowNotification(string title, string message, string? filePath, NotificationType? explicitType)
    {
        NotificationType type;
        if (explicitType.HasValue)
        {
            type = explicitType.Value;
        }
        else if (title.Contains("Error", System.StringComparison.OrdinalIgnoreCase) || 
                 title.Contains("Fehler", System.StringComparison.OrdinalIgnoreCase) || 
                 title.Contains("Invalid", System.StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Error", System.StringComparison.OrdinalIgnoreCase))
        {
            type = NotificationType.Error;
        }
        else if (title.Contains("Warning", System.StringComparison.OrdinalIgnoreCase) || 
                 title.Contains("Warnung", System.StringComparison.OrdinalIgnoreCase) || 
                 title.Contains("Skipped", System.StringComparison.OrdinalIgnoreCase))
        {
            type = NotificationType.Warning;
        }
        else
        {
            type = NotificationType.Success;
        }

        // Errors & Warnings -> TopCenter (zentral oberhalb des Screens)
        // Success / Info (e.g. "Datei gespeichert", "Preset Loaded") -> BottomRight (in der Ecke)
        var manager = (type == NotificationType.Error || type == NotificationType.Warning)
            ? _topNotificationManager ?? _cornerNotificationManager
            : _cornerNotificationManager ?? _topNotificationManager;

        if (manager == null) return;

        int displayDurationSeconds = type switch
        {
            NotificationType.Error => 8,
            NotificationType.Warning => 6,
            _ => 4
        };

        manager.Show(new Notification(
            title,
            message,
            type,
            System.TimeSpan.FromSeconds(displayDurationSeconds),
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

    private async void SettingsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow
        {
            DataContext = this.DataContext
        };
        await settingsWindow.ShowDialog(this);
    }
}