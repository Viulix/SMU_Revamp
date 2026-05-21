using Avalonia;
using Avalonia.Controls;
using SMU_Revamp.ViewModels;

namespace SMU_Revamp.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            DataContext = new SettingsViewModel();
        }

        private void ResetButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel vm)
            {
                vm.ResetSettings();
            }
        }

        private void ApplyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel vm)
            {
                vm.ApplySettings();
            }
        }

        private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }
    }
}
