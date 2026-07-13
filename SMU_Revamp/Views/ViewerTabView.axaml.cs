using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SMU_Revamp.ViewModels;

namespace SMU_Revamp.Views;

public partial class ViewerTabView : UserControl
{
    public ViewerTabView()
    {
        InitializeComponent();
    }

    private async void ExportCsvButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
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
        if (DataContext is MainWindowViewModel vm)
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
        if (DataContext is MainWindowViewModel vm)
        {
            var dbViewModel = new DatabaseLoadViewModel();
            dbViewModel.RequestLoadMeasurement = async (id) => 
            {
                await vm.LoadMeasurementFromDatabaseAsync(id);
            };
            dbViewModel.RequestLoadWafermap = async (measurements) => 
            {
                await vm.LoadWafermapFromDatabaseAsync(measurements);
            };

            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow != null)
            {
                var dbWindow = new DatabaseLoadWindow(dbViewModel);
                await dbWindow.ShowDialog(parentWindow);
            }
        }
    }

    private async void UploadToDatabaseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.UploadCurrentMeasurementToDatabaseAsync();
        }
    }

    private async void OpenAdvancedPlotSettingsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        if (parentWindow != null)
        {
            var settingsWindow = new AdvancedPlotSettingsWindow
            {
                DataContext = this.DataContext
            };
            await settingsWindow.ShowDialog(parentWindow);
        }
    }
}
