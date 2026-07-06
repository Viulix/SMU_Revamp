using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SMU_Revamp.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace SMU_Revamp.ViewModels
{
    public class DatabaseLoadViewModel : ViewModelBase
    {
        private readonly DatabaseService _dbService;

        private ObservableCollection<DatabaseService.MeasurementSummary> _measurements = new();
        public ObservableCollection<DatabaseService.MeasurementSummary> Measurements
        {
            get => _measurements;
            set => SetProperty(ref _measurements, value);
        }

        private DatabaseService.MeasurementSummary? _selectedMeasurement;
        public DatabaseService.MeasurementSummary? SelectedMeasurement
        {
            get => _selectedMeasurement;
            set
            {
                if (SetProperty(ref _selectedMeasurement, value))
                {
                    LoadCommand.NotifyCanExecuteChanged();
                }
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }
        
        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public IAsyncRelayCommand LoadCommand { get; }

        public Action<int>? RequestLoadMeasurement { get; set; }
        public Action? RequestClose { get; set; }

        public DatabaseLoadViewModel()
        {
            _dbService = DatabaseService.Instance;
            LoadCommand = new AsyncRelayCommand(LoadSelectedMeasurementAsync, () => SelectedMeasurement != null);
            _ = LoadRecentMeasurementsAsync();
        }

        public async Task LoadRecentMeasurementsAsync()
        {
            IsLoading = true;
            StatusMessage = "Loading recent measurements from database...";
            try
            {
                var list = await _dbService.GetRecentMeasurementsAsync(200);
                Measurements = new ObservableCollection<DatabaseService.MeasurementSummary>(list);
                StatusMessage = $"Loaded {Measurements.Count} measurements.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load measurements: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadSelectedMeasurementAsync()
        {
            if (SelectedMeasurement == null) return;
            
            RequestLoadMeasurement?.Invoke(SelectedMeasurement.Id);
            RequestClose?.Invoke();
            await Task.CompletedTask;
        }
    }
}
