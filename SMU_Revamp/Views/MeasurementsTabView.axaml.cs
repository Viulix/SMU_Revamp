using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SMU_Revamp.ViewModels;
using SMU_Revamp.Services;

namespace SMU_Revamp.Views;

public partial class MeasurementsTabView : UserControl
{
    public MeasurementsTabView()
    {
        InitializeComponent();
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
}
