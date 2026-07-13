using Avalonia.Controls;
using SMU_Revamp.ViewModels;

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

    private async void SettingsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow
        {
            DataContext = this.DataContext
        };
        await settingsWindow.ShowDialog(this);
    }
}