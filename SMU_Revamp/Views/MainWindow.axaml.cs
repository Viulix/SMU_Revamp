using Avalonia.Controls;

namespace SMU_Revamp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void DebugButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var debugWindow = new DebugWindow();
        await debugWindow.ShowDialog(this);
    }

    private async void SettingsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow();
        await settingsWindow.ShowDialog(this);
    }
}