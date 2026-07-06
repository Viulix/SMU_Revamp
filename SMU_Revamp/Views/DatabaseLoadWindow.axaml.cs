using Avalonia.Controls;
using SMU_Revamp.ViewModels;

namespace SMU_Revamp.Views
{
    public partial class DatabaseLoadWindow : Window
    {
        public DatabaseLoadWindow()
        {
            InitializeComponent();
        }

        public DatabaseLoadWindow(DatabaseLoadViewModel viewModel) : this()
        {
            DataContext = viewModel;
            viewModel.RequestClose = Close;
        }

        private void CancelButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }
    }
}
