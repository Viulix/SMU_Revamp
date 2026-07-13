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
        public bool IsFolderNode { get; set; }
    }

    public class DatabaseLoadViewModel : ViewModelBase
    {
        private static readonly System.Text.RegularExpressions.Regex WafermapRegex = 
            new(@"Cell\d{2}\d{2}_R\dC\d_Contact\d", System.Text.RegularExpressions.RegexOptions.Compiled);

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
                    LoadWafermapCommand?.NotifyCanExecuteChanged();
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
        public IAsyncRelayCommand LoadWafermapCommand { get; }

        public Action<int>? RequestLoadMeasurement { get; set; }
        public Action<System.Collections.Generic.List<DatabaseService.MeasurementSummary>>? RequestLoadWafermap { get; set; }
        public Action? RequestClose { get; set; }

        public DatabaseLoadViewModel()
        {
            _dbService = DatabaseService.Instance;
            LoadCommand = new AsyncRelayCommand(LoadSelectedMeasurementAsync, () => SelectedNode?.Measurement != null);
            LoadWafermapCommand = new AsyncRelayCommand(LoadSelectedWafermapAsync, () => SelectedNode?.IsFolderNode == true);
            _ = LoadRecentMeasurementsAsync();
        }

        private async Task LoadSelectedWafermapAsync()
        {
            if (SelectedNode == null || !SelectedNode.IsFolderNode) return;
            
            var measurements = new System.Collections.Generic.List<DatabaseService.MeasurementSummary>();
            
            // Collect all measurements under this folder node
            void CollectMeasurements(DbNode node)
            {
                if (node.IsMeasurement && node.Measurement != null)
                {
                    measurements.Add(node.Measurement);
                }
                else
                {
                    foreach (var child in node.Children)
                        CollectMeasurements(child);
                }
            }
            CollectMeasurements(SelectedNode);

            if (measurements.Count > 0)
            {
                RequestLoadWafermap?.Invoke(measurements);
                RequestClose?.Invoke();
            }
        }

        public async Task LoadRecentMeasurementsAsync()
        {
            IsLoading = true;
            StatusMessage = "Loading measurements from database...";
            try
            {
                var list = await _dbService.GetRecentMeasurementsAsync(1000);
                
                var roots = new ObservableCollection<DbNode>();
                var byProfile = list.GroupBy(m => m.ProfileName).OrderBy(g => g.Key);
                foreach (var profileGroup in byProfile)
                {
                    var profileNode = new DbNode { Header = string.IsNullOrEmpty(profileGroup.Key) ? "Unknown User" : profileGroup.Key };
                    
                    var bySample = profileGroup.GroupBy(m => m.SampleName).OrderBy(g => g.Key);
                    foreach (var sampleGroup in bySample)
                    {
                        var sampleNode = new DbNode { Header = string.IsNullOrEmpty(sampleGroup.Key) ? "Unknown Probe" : sampleGroup.Key };

                        var byFolder = sampleGroup.GroupBy(m => m.FolderName).OrderByDescending(g => g.Max(m => m.Timestamp));
                        foreach (var folderGroup in byFolder)
                        {
                            bool canLoadAsWafermap = folderGroup.Any(m => !string.IsNullOrEmpty(m.SourceFilename) && WafermapRegex.IsMatch(m.SourceFilename));
                            
                            string folderHeader = string.IsNullOrEmpty(folderGroup.Key) ? "Unknown Folder" : folderGroup.Key;
                            if (canLoadAsWafermap)
                            {
                                folderHeader += " (Wafermap)";
                            }

                            var folderNode = new DbNode { 
                                Header = folderHeader,
                                IsFolderNode = canLoadAsWafermap 
                            };

                            var byPlan = folderGroup.GroupBy(m => m.PlanName).OrderByDescending(g => g.Max(m => m.Timestamp));
                            foreach (var planGroup in byPlan)
                            {
                                var planNode = new DbNode { Header = string.IsNullOrEmpty(planGroup.Key) ? "Unknown Plan" : planGroup.Key };
                                
                                foreach (var meas in planGroup.OrderByDescending(m => m.Timestamp))
                                {
                                    var measNode = new DbNode 
                                    { 
                                        Header = $"{meas.Timestamp:dd.MM.yyyy HH:mm:ss}",
                                        Measurement = meas
                                    };
                                    planNode.Children.Add(measNode);
                                }
                                folderNode.Children.Add(planNode);
                            }
                            sampleNode.Children.Add(folderNode);
                        }
                        profileNode.Children.Add(sampleNode);
                    }
                    roots.Add(profileNode);
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
