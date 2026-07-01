using Avalonia.Controls;
using Avalonia.Interactivity;
using SMU_Revamp.ViewModels;

namespace SMU_Revamp.Views
{
    public partial class ParameterLinkDialog : Window
    {
        public ParameterLinkDialog()
        {
            InitializeComponent();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private void RemoveLinkButton_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is ParameterLinkViewModel vm)
            {
                vm.SelectedTargetParameter = null;
            }
            Close(true);
        }

        private void SaveButton_Click(object? sender, RoutedEventArgs e)
        {
            Close(true);
        }
    }
}
