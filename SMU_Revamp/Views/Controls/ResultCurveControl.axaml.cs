using Avalonia.Controls;
using Avalonia.Interactivity;
using SMU_Revamp.ViewModels;

namespace SMU_Revamp.Views.Controls;

public partial class ResultCurveControl : UserControl
{
    public ResultCurveControl()
    {
        InitializeComponent();
    }

    private void EnlargeResultPlotButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.SelectedResultContact != null)
        {
            string title = vm.SelectedResultContact.DisplayName ?? "I/V Curve";
            if (vm.SelectedResultCell != null && vm.SelectedResultSubCell != null)
            {
                title = $"Cell: {vm.SelectedResultCell.Id} | Sub: {vm.SelectedResultSubCell.Id} | {title}";
            }
            
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow != null)
            {
                var enlargedWindow = new EnlargedResultPlotWindow(title, vm.SelectedResultContact.CurveData);
                enlargedWindow.Show(parentWindow);
            }
        }
    }
}
