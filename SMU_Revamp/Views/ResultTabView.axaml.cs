using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SMU_Revamp.ViewModels;

namespace SMU_Revamp.Views;

public partial class ResultTabView : UserControl
{
    public ResultTabView()
    {
        InitializeComponent();
    }

    private async void LoadResultFolderButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
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

    private async void LoadResultFromDatabaseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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

}
