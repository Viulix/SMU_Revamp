using CommunityToolkit.Mvvm.ComponentModel;

namespace SMU_Revamp.ViewModels
{
    public partial class WaferCellViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        private bool _isValid;

        [ObservableProperty]
        private bool _isSelected;

        public WaferCellViewModel(string id, bool isValid)
        {
            Id = id;
            IsValid = isValid;
            IsSelected = isValid; // Default to selected if valid
        }

        private static readonly System.Collections.Generic.HashSet<string> InvalidCells = new()
        {
            "0101", "0102", "0103", "0114", "0115", "0116",
            "0201", "0202", "0215", "0216",
            "0301", "0316",
            "1401", "1416",
            "1501", "1502", "1515", "1516",
            "1601", "1602", "1603", "1614", "1615", "1616"
        };

        public static bool IsValidCell(string id) => !InvalidCells.Contains(id);
    }
}
