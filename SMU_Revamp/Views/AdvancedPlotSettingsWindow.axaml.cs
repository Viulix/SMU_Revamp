using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SMU_Revamp.Views;

public partial class AdvancedPlotSettingsWindow : Window
{
    public AdvancedPlotSettingsWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
