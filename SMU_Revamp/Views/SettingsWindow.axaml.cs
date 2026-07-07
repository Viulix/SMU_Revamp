using Avalonia;
using Avalonia.Controls;
using SMU_Revamp.ViewModels;
using System.Threading.Tasks;

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

        private async void ApplyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel vm)
            {
                await vm.ApplySettingsAsync();
                await Task.Delay(500);
                Close();
            }
        }

        private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }

        private async void TestDbConnectionButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel vm)
            {
                await vm.TestDbConnectionAsync();
            }
        }
    }
}
