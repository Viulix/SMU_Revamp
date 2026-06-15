using CommunityToolkit.Mvvm.ComponentModel;
using SMU_Revamp.Models;
using SMU_Revamp.Services;
using System.Threading.Tasks;

namespace SMU_Revamp.ViewModels
{
    /// <summary>
    /// ViewModel for the settings window that binds to service configuration values.
    /// </summary>
    public class SettingsViewModel : ViewModelBase
    {
        private readonly IProberService _proberService;
        private readonly SwitchMatrixService _switchMatrixService;
        private readonly ConfigurationService _configService;

        private bool _proberQuietMode = false;
        private string _proberResource = "GPIB0::22::INSTR";
        private int _proberTimeoutMs = 20000;
        private string _switchMatrixResource = "GPIB0::23::INSTR";
        private int _switchMatrixTimeoutMs = 5000;
        private string _smuResource = "GPIB0::17::INSTR";
        private int _smuTimeoutMs = 300000;
        private string _applyStatusMessage = string.Empty;

        private string _profile = string.Empty;
        private string _sampleName = string.Empty;

        public bool ProberQuietMode
        {
            get => _proberQuietMode;
            set => SetProperty(ref _proberQuietMode, value);
        }

        public string ProberResource
        {
            get => _proberResource;
            set => SetProperty(ref _proberResource, value ?? "GPIB0::22::INSTR");
        }

        public int ProberTimeoutMs
        {
            get => _proberTimeoutMs;
            set => SetProperty(ref _proberTimeoutMs, value);
        }

        public string SwitchMatrixResource
        {
            get => _switchMatrixResource;
            set => SetProperty(ref _switchMatrixResource, value ?? "GPIB0::23::INSTR");
        }

        public int SwitchMatrixTimeoutMs
        {
            get => _switchMatrixTimeoutMs;
            set => SetProperty(ref _switchMatrixTimeoutMs, value);
        }

        public string SMUResource
        {
            get => _smuResource;
            set => SetProperty(ref _smuResource, value ?? "GPIB0::17::INSTR");
        }

        public int SMUTimeoutMs
        {
            get => _smuTimeoutMs;
            set => SetProperty(ref _smuTimeoutMs, value);
        }

        public string Profile
        {
            get => _profile;
            set => SetProperty(ref _profile, value ?? string.Empty);
        }

        public string SampleName
        {
            get => _sampleName;
            set => SetProperty(ref _sampleName, value ?? string.Empty);
        }

        public string ApplyStatusMessage
        {
            get => _applyStatusMessage;
            set => SetProperty(ref _applyStatusMessage, value);
        }

        public SettingsViewModel()
        {
            // Get singleton instances
            _proberService = ProberService.Instance;
            _switchMatrixService = SwitchMatrixService.Instance;
            _configService = ConfigurationService.Instance;

            // Initialize bindings from service state
            ProberQuietMode = _proberService.QuietMode;
            SwitchMatrixTimeoutMs = _switchMatrixService.GetTimeout();
            SMUTimeoutMs = E5263_SMU.Instance.GetTimeout();

            // Load from config
            var config = _configService.GetConfig();
            ProberResource = config.ProberResource;
            ProberTimeoutMs = config.ProberTimeoutMs;
            SwitchMatrixResource = config.SwitchMatrixResource;
            SMUResource = config.SMUResource;
            Profile = config.Profile;
            SampleName = config.SampleName;
        }

        /// <summary>
        /// Applies settings changes back to the services.
        /// </summary>
        public async Task ApplySettingsAsync()
        {
            _proberService.QuietMode = ProberQuietMode;
            _proberService.ResourceString = ProberResource;
            _switchMatrixService.ResourceString = SwitchMatrixResource;
            _switchMatrixService.SetTimeout(SwitchMatrixTimeoutMs);
            E5263_SMU.Instance.ResourceString = SMUResource;
            E5263_SMU.Instance.SetTimeout(SMUTimeoutMs);

            // Save to persistent configuration
            var config = _configService.GetConfig();
            config.ProberQuietMode = ProberQuietMode;
            config.ProberResource = ProberResource;
            config.ProberTimeoutMs = ProberTimeoutMs;
            config.SwitchMatrixResource = SwitchMatrixResource;
            config.SwitchMatrixTimeoutMs = SwitchMatrixTimeoutMs;
            config.SMUResource = SMUResource;
            config.SMUTimeoutMs = SMUTimeoutMs;
            config.Profile = Profile;
            config.SampleName = SampleName;

            await _configService.SaveAsync(config);
            ApplyStatusMessage = "Settings saved.";
        }

        /// <summary>
        /// Resets settings to service current values.
        /// </summary>
        public void ResetSettings()
        {
            ProberQuietMode = _proberService.QuietMode;
            SwitchMatrixTimeoutMs = _switchMatrixService.GetTimeout();
            SMUTimeoutMs = E5263_SMU.Instance.GetTimeout();
            var config = _configService.GetConfig();
            ProberResource = config.ProberResource;
            ProberTimeoutMs = config.ProberTimeoutMs;
            SwitchMatrixResource = config.SwitchMatrixResource;
            SMUResource = config.SMUResource;
            Profile = config.Profile;
            SampleName = config.SampleName;
        }
    }
}

