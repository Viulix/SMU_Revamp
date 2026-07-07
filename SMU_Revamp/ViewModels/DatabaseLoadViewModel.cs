using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SMU_Revamp.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace SMU_Revamp.ViewModels
{
    public class DbNode : ObservableObject
    {
        public string Header { get; set; } = string.Empty;
        public ObservableCollection<DbNode> Children { get; set; } = new();
        public DatabaseService.MeasurementSummary? Measurement { get; set; }
        
        public bool IsMeasurement => Measurement != null;
    }

    public class DatabaseLoadViewModel : ViewModelBase
    {
        private readonly DatabaseService _dbService;

        private ObservableCollection<DbNode> _rootNodes = new();
        public ObservableCollection<DbNode> RootNodes
        {
            get => _rootNodes;
            set => SetProperty(ref _rootNodes, value);
        }

        private DbNode? _selectedNode;
        public DbNode? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (SetProperty(ref _selectedNode, value))
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
            LoadCommand = new AsyncRelayCommand(LoadSelectedMeasurementAsync, () => SelectedNode?.Measurement != null);
            _ = LoadRecentMeasurementsAsync();
        }

        public async Task LoadRecentMeasurementsAsync()
        {
            IsLoading = true;
            StatusMessage = "Loading measurements from database...";
            try
            {
                var list = await _dbService.GetRecentMeasurementsAsync(1000);
                
                var roots = new ObservableCollection<DbNode>();
                var byYear = list.GroupBy(m => m.Timestamp.Year).OrderByDescending(g => g.Key);
                foreach (var yearGroup in byYear)
                {
                    var yearNode = new DbNode { Header = yearGroup.Key.ToString() };
                    
                    var byMonth = yearGroup.GroupBy(m => m.Timestamp.Month).OrderByDescending(g => g.Key);
                    foreach (var monthGroup in byMonth)
                    {
                        var monthName = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(monthGroup.Key);
                        var monthNode = new DbNode { Header = $"{monthGroup.Key:D2} - {monthName}" };
                        
                        var byPlan = monthGroup.GroupBy(m => m.PlanName).OrderBy(g => g.Key);
                        foreach (var planGroup in byPlan)
                        {
                            var planNode = new DbNode { Header = string.IsNullOrEmpty(planGroup.Key) ? "Unknown Plan" : planGroup.Key };
                            
                            foreach (var meas in planGroup.OrderByDescending(m => m.Timestamp))
                            {
                                var sampleDisplay = string.IsNullOrEmpty(meas.SampleName) ? "Unknown Sample" : meas.SampleName;
                                var measNode = new DbNode 
                                { 
                                    Header = $"{meas.Timestamp:dd.MM. yyyy HH:mm:ss} | {sampleDisplay}",
                                    Measurement = meas
                                };
                                planNode.Children.Add(measNode);
                            }
                            monthNode.Children.Add(planNode);
                        }
                        yearNode.Children.Add(monthNode);
                    }
                    roots.Add(yearNode);
                }

                RootNodes = roots;
                StatusMessage = $"Loaded {list.Count} measurements.";
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
            if (SelectedNode?.Measurement == null) return;
            
            RequestLoadMeasurement?.Invoke(SelectedNode.Measurement.Id);
            RequestClose?.Invoke();
            await Task.CompletedTask;
        }
    }
}
