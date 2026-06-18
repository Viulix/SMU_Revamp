using CommunityToolkit.Mvvm.ComponentModel;

namespace SMU_Revamp.ViewModels
{
    public partial class SubCellViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        private int _row;

        [ObservableProperty]
        private int _column;

        [ObservableProperty]
        private bool _isValid;

        [ObservableProperty]
        private bool _isSelected;

        public SubCellViewModel(int row, int col, bool isValid)
        {
            Id = $"R{row}C{col}";
            Row = row;
            Column = col;
            IsValid = isValid;
            IsSelected = isValid; // Default to selected if valid
        }
    }
}
