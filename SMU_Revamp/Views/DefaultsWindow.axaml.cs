using Avalonia;
using Avalonia.Controls;
using SMU_Revamp.ViewModels;

namespace SMU_Revamp.Views
{
    public partial class DefaultsWindow : Window
    {
        public DefaultsWindow()
        {
            InitializeComponent();
            DataContext = new DefaultsViewModel();
        }

        private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }
    }
}
