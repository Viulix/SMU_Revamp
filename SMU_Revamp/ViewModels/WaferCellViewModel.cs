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
    }
}
