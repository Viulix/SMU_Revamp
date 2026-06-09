using Avalonia.Controls;
using Avalonia.Platform.Storage;

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
}