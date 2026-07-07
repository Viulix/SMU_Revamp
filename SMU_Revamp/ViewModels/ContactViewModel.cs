using CommunityToolkit.Mvvm.ComponentModel;

namespace SMU_Revamp.ViewModels
{
    public partial class ContactViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        private bool _isSelected;

        public ContactViewModel(string id, bool isSelected = true)
        {
            Id = id;
            IsSelected = isSelected;
        }
    }
}
