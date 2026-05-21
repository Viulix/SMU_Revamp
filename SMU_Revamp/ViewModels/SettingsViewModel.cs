using CommunityToolkit.Mvvm.ComponentModel;
using SMU_Revamp.Services;

namespace SMU_Revamp.ViewModels
{
    /// <summary>
    /// ViewModel for the settings window that binds to service configuration values.
    /// </summary>
    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly IProberService _proberService;
        private readonly SwitchMatrixService _switchMatrixService;

        [ObservableProperty]
        private bool proberQuietMode = false;

        [ObservableProperty]
        private string switchMatrixResource = "GPIB0::23::INSTR";

        [ObservableProperty]
        private int switchMatrixTimeoutMs = 5000;

        [ObservableProperty]
        private string proberResource = "GPIB0::22::INSTR";

        [ObservableProperty]
        private int proberTimeoutMs = 20000;

        public SettingsViewModel()
        {
            // Get singleton instances
            _proberService = ProberService.Instance;
            _switchMatrixService = SwitchMatrixService.Instance;

            // Initialize bindings from service state
            ProberQuietMode = _proberService.QuietMode;
            SwitchMatrixTimeoutMs = _switchMatrixService.GetTimeout();
        }

        /// <summary>
        /// Applies settings changes back to the services.
        /// </summary>
        public void ApplySettings()
        {
            _proberService.QuietMode = ProberQuietMode;
            _switchMatrixService.SetTimeout(SwitchMatrixTimeoutMs);
        }

        /// <summary>
        /// Resets settings to service current values.
        /// </summary>
        public void ResetSettings()
        {
            ProberQuietMode = _proberService.QuietMode;
            SwitchMatrixTimeoutMs = _switchMatrixService.GetTimeout();
        }
    }
}
