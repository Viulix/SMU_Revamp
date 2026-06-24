using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using SMU_Revamp.Models;
using SMU_Revamp.Services;
using SMU_Revamp.MeasurementPlans;
using SMU_Revamp.Interfaces;

namespace SMU_Revamp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public event Action<string, string, string?>? NotificationRequested;

    public string Greeting { get; } = "Welcome to Avalonia!";

    private IReadOnlyList<CurvePoint> _curvePoints = Array.Empty<CurvePoint>();
    public IReadOnlyList<CurvePoint> CurvePoints
    {
        get => _curvePoints;
        set
        {
            if (SetProperty(ref _curvePoints, value))
            {
                OnPropertyChanged(nameof(HasCurvePoints));
            }
        }
    }

    private IReadOnlyList<PlotSeries> _plotSeries = Array.Empty<PlotSeries>();
    public IReadOnlyList<PlotSeries> PlotSeries
    {
        get => _plotSeries;
        set
        {
            if (SetProperty(ref _plotSeries, value))
            {
                OnPropertyChanged(nameof(HasCurvePoints));
            }
        }
    }

    public bool HasCurvePoints =>
        (_curvePoints != null && _curvePoints.Count > 0) ||
        (_plotSeries != null && _plotSeries.Any(s => s.Points.Count > 0));

    private List<IMeasurementPlan> _measurementPlans = new();
    public List<IMeasurementPlan> MeasurementPlans
    {
        get => _measurementPlans;
        set => SetProperty(ref _measurementPlans, value);
    }

    private IMeasurementPlan _selectedPlan = null!;
    public IMeasurementPlan SelectedPlan
    {
        get => _selectedPlan;
        set
        {
            var oldPlan = _selectedPlan;
            if (SetProperty(ref _selectedPlan, value))
            {
                if (oldPlan?.Parameters != null)
                {
                    foreach (var param in oldPlan.Parameters)
                    {
                        param.PropertyChanged -= OnParameterPropertyChanged;
                    }
                }

                if (_selectedPlan != null)
                {
                    _selectedPlan.LoadDefaults();
                    SubscribeToParameterChanges();
                }
                UpdateSelectedPlanSections();
                UpdateWarningMessage();
                OnPropertyChanged(nameof(IsMeasuringSweep));
                OnPropertyChanged(nameof(GlobalProgressTitle));
            }
        }
    }

    private IMeasurementPlan? _plottedPlan;
    public IMeasurementPlan? PlottedPlan
    {
        get => _plottedPlan;
        set
        {
            if (SetProperty(ref _plottedPlan, value))
            {
                OnPropertyChanged(nameof(PlotTitle));
                OnPropertyChanged(nameof(LinearPlotTitle));
                OnPropertyChanged(nameof(LogPlotTitle));
                OnPropertyChanged(nameof(XAxisTitle));
                OnPropertyChanged(nameof(YAxisTitle));
                OnPropertyChanged(nameof(ShowLogPlot));
                OnPropertyChanged(nameof(IsLogPlotVisible));
            }
        }
    }

    public string PlotTitle => PlottedPlan?.PlotTitle ?? "Measurement Data";
    public string LinearPlotTitle => $"{PlotTitle} - Linear";
    public string LogPlotTitle => $"{PlotTitle} - Logarithmic Y";
    public string XAxisTitle => PlottedPlan?.XAxisLabel ?? "X";
    public string YAxisTitle => PlottedPlan?.YAxisLabel ?? "Y";
    public bool ShowLogPlot => PlottedPlan?.ShowLogPlot ?? true;

    public System.Collections.Generic.IReadOnlyList<string> AvailablePlotViews { get; } = new[] { "Both", "Linear Only", "Logarithmic Only" };

    private string _selectedPlotView = "Both";
    public string SelectedPlotView
    {
        get => _selectedPlotView;
        set
        {
            if (SetProperty(ref _selectedPlotView, value))
            {
                OnPropertyChanged(nameof(IsLinearPlotVisible));
                OnPropertyChanged(nameof(IsLogPlotVisible));
            }
        }
    }

    public bool IsLinearPlotVisible => SelectedPlotView == "Both" || SelectedPlotView == "Linear Only";

    public bool IsLogPlotVisible => (SelectedPlotView == "Both" || SelectedPlotView == "Logarithmic Only") && ShowLogPlot;

    private List<ParameterSection> _selectedPlanSections = new();
    public List<ParameterSection> SelectedPlanSections
    {
        get => _selectedPlanSections;
        set => SetProperty(ref _selectedPlanSections, value);
    }

    public SettingsViewModel Settings { get; }

    private int _selectedTabIndex = 1; // Default to Measurements tab
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    private string _targetCell = string.Empty;
    public string TargetCell
    {
        get => _targetCell;
        set => SetProperty(ref _targetCell, value);
    }

    private string _targetRow = string.Empty;
    public string TargetRow
    {
        get => _targetRow;
        set => SetProperty(ref _targetRow, value);
    }

    private string _targetColumn = string.Empty;
    public string TargetColumn
    {
        get => _targetColumn;
        set => SetProperty(ref _targetColumn, value);
    }

    private string _targetContact = string.Empty;
    public string TargetContact
    {
        get => _targetContact;
        set => SetProperty(ref _targetContact, value);
    }

    private bool _stayHere;
    public bool StayHere
    {
        get => _stayHere;
        set => SetProperty(ref _stayHere, value);
    }

    private bool _autoSaveMeasurements = true;
    public bool AutoSaveMeasurements
    {
        get => _autoSaveMeasurements;
        set
        {
            if (SetProperty(ref _autoSaveMeasurements, value))
            {
                _ = SaveAutoSaveSettingAsync(value);
            }
        }
    }



    private string _advPathA = string.Empty;
    public string AdvPathA
    {
        get => _advPathA;
        set => SetProperty(ref _advPathA, value);
    }

    private string _advPathB = string.Empty;
    public string AdvPathB
    {
        get => _advPathB;
        set => SetProperty(ref _advPathB, value);
    }

    private string _errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    private string _warningMessage = string.Empty;
    public string WarningMessage
    {
        get => _warningMessage;
        set
        {
            if (SetProperty(ref _warningMessage, value))
            {
                OnPropertyChanged(nameof(HasWarningMessage));
            }
        }
    }

    public bool HasWarningMessage => !string.IsNullOrEmpty(_warningMessage);

    public ICommand GoToContactCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand MoveRelativeCommand { get; }
    public ICommand MoveAbsoluteCommand { get; }
    public ICommand DisconnectRouteCommand { get; }
    public ICommand ClearAllMatrixCommand { get; }

    public ICommand ScanWaferCommand { get; }

    private bool _isScanningWafer;
    public bool IsScanningWafer
    {
        get => _isScanningWafer;
        set
        {
            if (SetProperty(ref _isScanningWafer, value))
            {
                (ScanWaferCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (RequestStopScanCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (RunMeasurementCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                NotifyGlobalProgressPropertiesChanged();
            }
        }
    }

    private bool _isAllContactsChecked;
    public bool IsAllContactsChecked
    {
        get => _isAllContactsChecked;
        set
        {
            if (SetProperty(ref _isAllContactsChecked, value))
            {
                if (value)
                {
                    TargetScanContacts = "1, 2, 3, 4, 5, 6";
                }
            }
        }
    }

    private double _waferScanProgress;
    public double WaferScanProgress
    {
        get => _waferScanProgress;
        set
        {
            if (SetProperty(ref _waferScanProgress, value))
            {
                NotifyGlobalProgressPropertiesChanged();
            }
        }
    }

    private string _waferScanLog = string.Empty;
    public string WaferScanLog
    {
        get => _waferScanLog;
        set
        {
            if (SetProperty(ref _waferScanLog, value))
            {
                NotifyGlobalProgressPropertiesChanged();
            }
        }
    }

    private Avalonia.Media.FontWeight _waferScanLogFontWeight = Avalonia.Media.FontWeight.Normal;
    public Avalonia.Media.FontWeight WaferScanLogFontWeight
    {
        get => _waferScanLogFontWeight;
        set => SetProperty(ref _waferScanLogFontWeight, value);
    }

    private string _waferScanEstimatedFinish = string.Empty;
    public string WaferScanEstimatedFinish
    {
        get => _waferScanEstimatedFinish;
        set
        {
            if (SetProperty(ref _waferScanEstimatedFinish, value))
            {
                NotifyGlobalProgressPropertiesChanged();
            }
        }
    }

    private string _waferScanCountText = string.Empty;
    public string WaferScanCountText
    {
        get => _waferScanCountText;
        set
        {
            if (SetProperty(ref _waferScanCountText, value))
            {
                NotifyGlobalProgressPropertiesChanged();
            }
        }
    }

    public System.Collections.ObjectModel.ObservableCollection<WaferCellViewModel> WaferCells { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<SubCellViewModel> SubCells { get; } = new();

    public ICommand SelectAllCellsCommand { get; }
    public ICommand DeselectAllCellsCommand { get; }
    
    public ICommand SelectAllSubCellsCommand { get; }
    public ICommand DeselectAllSubCellsCommand { get; }

    private int _waferScanDelayMs = 500;
    public int WaferScanDelayMs
    {
        get => _waferScanDelayMs;
        set => SetProperty(ref _waferScanDelayMs, value);
    }

    private string _targetScanContacts = "1, 2, 3";
    public string TargetScanContacts
    {
        get => _targetScanContacts;
        set => SetProperty(ref _targetScanContacts, value);
    }

    private System.Threading.CancellationTokenSource? _scanCts;

    private double _moveX;
    public double MoveX
    {
        get => _moveX;
        set => SetProperty(ref _moveX, value);
    }

    private double _moveY;
    public double MoveY
    {
        get => _moveY;
        set => SetProperty(ref _moveY, value);
    }

    private bool _isCancelPromptVisible;
    public bool IsCancelPromptVisible
    {
        get => _isCancelPromptVisible;
        set => SetProperty(ref _isCancelPromptVisible, value);
    }

    private bool _isErrorPopupVisible;
    public bool IsErrorPopupVisible
    {
        get => _isErrorPopupVisible;
        set => SetProperty(ref _isErrorPopupVisible, value);
    }

    private string _popupErrorMessage = string.Empty;
    public string PopupErrorMessage
    {
        get => _popupErrorMessage;
        set => SetProperty(ref _popupErrorMessage, value);
    }

    private bool _isAlignmentWarningVisible;
    public bool IsAlignmentWarningVisible
    {
        get => _isAlignmentWarningVisible;
        set => SetProperty(ref _isAlignmentWarningVisible, value);
    }

    private bool _dontShowAlignmentWarning;
    public bool DontShowAlignmentWarning
    {
        get => _dontShowAlignmentWarning;
        set => SetProperty(ref _dontShowAlignmentWarning, value);
    }

    public ICommand CloseErrorPopupCommand { get; }
    public ICommand ProceedWithScanCommand { get; }
    public ICommand CancelAlignmentWarningCommand { get; }

    private List<int> _parsedScanContacts = new();
    private int _totalExpectedCells = 0;
    private int _totalExpectedSubCells = 0;
    private string _currentWaferScanFolderName = string.Empty;

    public MainWindowViewModel()
    {
        CurvePoints = CreateCurvePoints();
        Settings = new SettingsViewModel();

        MeasurementPlans = MeasurementPlanLoader.LoadPlans();
        SelectedPlan = MeasurementPlans.Count > 0 ? MeasurementPlans.Find(p => p.Name == "Measure Point") ?? MeasurementPlans[0] : null!;
        PlottedPlan = SelectedPlan;

        GoToContactCommand = new AsyncRelayCommand(GoToContactAsync);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAndConfigurationAsync);
        RunMeasurementCommand = new AsyncRelayCommand(RunMeasurementAsync, () => !IsScanningWafer && !IsMeasuring);
        MoveRelativeCommand = new AsyncRelayCommand(MoveRelativeAsync);
        MoveAbsoluteCommand = new AsyncRelayCommand(MoveAbsoluteAsync);
        GoToScanStartCommand = new AsyncRelayCommand(GoToScanStartAsync);
        
        SelectAllCellsCommand = new RelayCommand(() => 
        {
            foreach (var cell in WaferCells)
            {
                if (cell.IsValid) cell.IsSelected = true;
            }
        });
        
        DeselectAllCellsCommand = new RelayCommand(() => 
        {
            foreach (var cell in WaferCells)
            {
                cell.IsSelected = false;
            }
        });

        SelectAllSubCellsCommand = new RelayCommand(() => 
        {
            foreach (var cell in SubCells)
            {
                if (cell.IsValid) cell.IsSelected = true;
            }
        });
        
        DeselectAllSubCellsCommand = new RelayCommand(() => 
        {
            foreach (var cell in SubCells)
            {
                cell.IsSelected = false;
            }
        });

        RequestStopScanCommand = new RelayCommand(() => IsCancelPromptVisible = true, () => IsScanningWafer);
        ConfirmStopScanCommand = new RelayCommand(ConfirmStopWaferScan);
        CancelStopRequestCommand = new RelayCommand(() => IsCancelPromptVisible = false);

        CloseErrorPopupCommand = new RelayCommand(() => IsErrorPopupVisible = false);
        ProceedWithScanCommand = new AsyncRelayCommand(async () =>
        {
            IsAlignmentWarningVisible = false;
            await ExecuteWaferScanAsync();
        });
        CancelAlignmentWarningCommand = new RelayCommand(() => IsAlignmentWarningVisible = false);

        SetSelectedResultCellCommand = new RelayCommand<ResultCellViewModel>(c => SelectedResultCell = c);
        SetSelectedResultSubCellCommand = new RelayCommand<ResultSubCellViewModel>(c => SelectedResultSubCell = c);

        LoadScanFolderCommand = new AsyncRelayCommand(async () =>
        {
            var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(new Avalonia.Controls.Window()); // Will inject topLevel dynamically from UI or pass the path.
            // Wait, we can't do this easily from ViewModel. We should do it from UI code-behind.
            // Or better, let's keep it in MainWindow.axaml.cs.
        });

        LoadConfigState();

        // Auto-save settings when Profile or SampleName changes
        Settings.PropertyChanged += async (s, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.Profile) || e.PropertyName == nameof(SettingsViewModel.SampleName))
            {
                await ConfigurationService.Instance.SaveAsync(ConfigurationService.Instance.GetConfig());
            }
        };

        DisconnectRouteCommand = new AsyncRelayCommand(DisconnectRouteAsync);
        ClearAllMatrixCommand = new AsyncRelayCommand(ClearAllMatrixAsync);

        InitializeWaferCells();
        InitializeSubCells();

        ScanWaferCommand = new AsyncRelayCommand(StartWaferScanAsync, () => !IsScanningWafer);
    }

    private void InitializeWaferCells()
    {
        for (int y = 1; y <= 16; y++)
        {
            for (int x = 1; x <= 16; x++)
            {
                string cellId = $"{y:D2}{x:D2}";
                WaferCells.Add(new WaferCellViewModel(cellId, WaferCellViewModel.IsValidCell(cellId)));
            }
        }
    }

    private void InitializeSubCells()
    {
        for (int row = 1; row <= 5; row++)
        {
            for (int col = 1; col <= 5; col++)
            {
                bool isInvalid = (row == 2 && col == 2) || (row == 5 && col == 5);
                SubCells.Add(new SubCellViewModel(row, col, !isInvalid));
            }
        }
    }
    
    public ICommand GoToScanStartCommand { get; }

    private async Task GoToScanStartAsync()
    {
        try
        {
            await ProberService.Instance.ConnectAsync();
            await ProberService.Instance.DisconnectChuckAsync();
            await ProberService.Instance.ProberGoHomeAsync();
            WaferScanLogFontWeight = Avalonia.Media.FontWeight.Bold;
            IsScanningWafer = false;
        }
        catch (Exception ex)
        {
            WaferScanLog = $"Error moving to start: {ex.Message}";
        }
    }

    public ICommand RequestStopScanCommand { get; }
    public ICommand ConfirmStopScanCommand { get; }
    public ICommand CancelStopRequestCommand { get; }

    private void ConfirmStopWaferScan()
    {
        IsCancelPromptVisible = false;
        if (_scanCts != null)
        {
            _scanCts.Cancel();
            WaferScanLog = "Wafer scan canceled by user!";
            WaferScanLogFontWeight = Avalonia.Media.FontWeight.Bold;
        }
    }

    private async Task StartWaferScanAsync()
    {
        if (IsScanningWafer) return;

        WaferScanLog = "Parsing target contacts...";
        WaferScanProgress = 0;
        
        _parsedScanContacts.Clear();
        var parts = TargetScanContacts.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (int.TryParse(part, out int c) && c >= 1 && c <= 6)
            {
                _parsedScanContacts.Add(c);
            }
        }

        if (_parsedScanContacts.Count == 0)
        {
            WaferScanLog = "Error: Invalid target contacts.";
            PopupErrorMessage = "Invalid target contacts. Please specify valid contact numbers (1-6).";
            IsErrorPopupVisible = true;
            return;
        }

        _totalExpectedCells = 0;
        foreach (var cell in WaferCells)
        {
            if (cell.IsValid && cell.IsSelected) _totalExpectedCells++;
        }

        _totalExpectedSubCells = 0;
        foreach (var subCell in SubCells)
        {
            if (subCell.IsValid && subCell.IsSelected) _totalExpectedSubCells++;
        }

        if (_totalExpectedCells == 0 || _totalExpectedSubCells == 0)
        {
            WaferScanLog = "Error: No cells selected.";
            PopupErrorMessage = "Please select at least one Target Cell and one Global Sub-cell.";
            IsErrorPopupVisible = true;
            return;
        }

        IsAlignmentWarningVisible = true;
    }

    private async Task ExecuteWaferScanAsync()
    {
        IsScanningWafer = true;
        _scanCts = new System.Threading.CancellationTokenSource();
        _currentWaferScanFolderName = $"Scan_{DateTime.Now:yyyyMMdd_HHmmss}";

        try
        {
            WaferScanLog = "Connecting to Prober...";
            await ProberService.Instance.ConnectAsync();
            
            var targetCells = new System.Collections.Generic.HashSet<string>();
            foreach (var cell in WaferCells)
            {
                if (cell.IsValid && cell.IsSelected)
                {
                    targetCells.Add(cell.Id);
                }
            }

            var targetSubCells = new System.Collections.Generic.HashSet<(int row, int col)>();
            foreach (var subCell in SubCells)
            {
                if (subCell.IsValid && subCell.IsSelected)
                {
                    targetSubCells.Add((subCell.Row, subCell.Column));
                }
            }

            int totalExpectedContacts = _totalExpectedCells * _totalExpectedSubCells * _parsedScanContacts.Count;
            int currentContact = 0;
            var scanStepDurations = new System.Collections.Generic.Queue<TimeSpan>();
            var stepStopwatch = new System.Diagnostics.Stopwatch();

            WaferScanCountText = $"0 / {totalExpectedContacts}";
            WaferScanEstimatedFinish = string.Empty;
            WaferScanLog = "Starting wafer scan...";
            WaferScanLogFontWeight = Avalonia.Media.FontWeight.Normal;
            
            await ProberService.Instance.ScanWaferAsync(targetCells, targetSubCells, _parsedScanContacts, WaferScanDelayMs, async (cell, row, col, contact) =>
            {
                if (stepStopwatch.IsRunning)
                {
                    stepStopwatch.Stop();
                    scanStepDurations.Enqueue(stepStopwatch.Elapsed);
                    if (scanStepDurations.Count > 5) scanStepDurations.Dequeue();
                    
                    double avgMs = System.Linq.Enumerable.Average(scanStepDurations, ts => ts.TotalMilliseconds);
                    int remaining = totalExpectedContacts - currentContact;
                    TimeSpan estimatedRemaining = TimeSpan.FromMilliseconds(avgMs * remaining);
                    DateTime finishTime = DateTime.Now + estimatedRemaining;
                    WaferScanEstimatedFinish = $"Est. Finish: {finishTime:HH:mm:ss}";
                }
                stepStopwatch.Restart();

                WaferScanLog = $"Measuring Cell: {cell}, Row: {row}, Col: {col}, Contact: {contact}";
                
                // Update UI state for auto-save filenames
                TargetCell = cell;
                TargetRow = row.ToString();
                TargetColumn = col.ToString();
                TargetContact = contact.ToString();
                
                // Trigger the actual measurement
                await RunMeasurementAsync();
                
                currentContact++;
                WaferScanProgress = (double)currentContact / totalExpectedContacts * 100.0;
                WaferScanCountText = $"{currentContact} / {totalExpectedContacts}";
            }, _scanCts.Token);

            WaferScanLog = "Wafer scan completed.";
            WaferScanProgress = 100;
            WaferScanCountText = $"{totalExpectedContacts} / {totalExpectedContacts}";
        }
        catch (OperationCanceledException)
        {
            WaferScanLog = "Wafer scan canceled.";
        }
        catch (Exception ex)
        {
            WaferScanLog = $"Error during scan: {ex.Message}";
        }
        finally
        {
            try 
            {
                WaferScanLog = "Separating and returning to home...";
                await ProberService.Instance.DisconnectChuckAsync();
                await ProberService.Instance.ProberGoHomeAsync();
                WaferScanLog = "Wafer scan finished.";
            }
            catch { }

            IsScanningWafer = false;
            _scanCts?.Dispose();
            _scanCts = null;
            (ScanWaferCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            (RequestStopScanCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    private async Task GoToContactAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            if (!TryParseTargetInputs(out var cellPosition, out var row, out var col, out var contact, out var error))
            {
                ErrorMessage = error;
                return;
            }

            await GoToContactHugeDeltaBAsync(
                cellPosition,
                row,
                col,
                contact,
                StayHere,
                AdvPathA,
                AdvPathB);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
            Console.WriteLine($"Error moving to contact: {ex.Message}");
        }
    }

    private async Task DisconnectRouteAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(AdvPathA) || string.IsNullOrWhiteSpace(AdvPathB))
            {
                ErrorMessage = "Please specify Adv Path A and Adv Path B to disconnect.";
                return;
            }

            await SwitchMatrixService.Instance.ConnectAsync();
            var channel = await SwitchMatrixService.Instance.RemoveConnectionAsync(AdvPathA, AdvPathB);
            MeasurementStatus = $"Successfully disconnected route {channel}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error disconnecting route: {ex.Message}";
        }
    }

    private async Task ClearAllMatrixAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            await SwitchMatrixService.Instance.ConnectAsync();
            await SwitchMatrixService.Instance.ClearAllConnectionsAsync();
            MeasurementStatus = "Successfully cleared all switch matrix connections.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error clearing matrix: {ex.Message}";
        }
    }

    private async Task MoveRelativeAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            await ProberService.Instance.ConnectAsync();
            await ProberService.Instance.MoveProberAsync(MoveX, MoveY);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error moving relative: {ex.Message}";
        }
    }

    private async Task MoveAbsoluteAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            await ProberService.Instance.ConnectAsync();
            await ProberService.Instance.MoveProberAbsoluteAsync(MoveX, MoveY);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error moving absolute: {ex.Message}";
        }
    }

    private bool TryParseTargetInputs(
        out string cellPosition,
        out int row,
        out int col,
        out int contact,
        out string error)
    {
        cellPosition = string.Empty;
        row = 0;
        col = 0;
        contact = 0;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(TargetCell))
        {
            error = "Target cell is required (format: RRCC).";
            return false;
        }

        cellPosition = TargetCell.Trim();
        if (cellPosition.Length < 4)
        {
            error = "Target cell must be at least 4 characters (RRCC).";
            return false;
        }

        if (!int.TryParse(cellPosition.Substring(0, 2), out _) || !int.TryParse(cellPosition.Substring(2, 2), out _))
        {
            error = "Target cell must be numeric in format RRCC.";
            return false;
        }

        if (!int.TryParse(TargetRow, out row) || row < 1)
        {
            error = "Row must be a positive number.";
            return false;
        }

        if (!int.TryParse(TargetColumn, out col) || col < 1)
        {
            error = "Column must be a positive number.";
            return false;
        }

        if (!int.TryParse(TargetContact, out contact) || contact < 1 || contact > 6)
        {
            error = "Contact must be a number between 1 and 6.";
            return false;
        }

        return true;
    }

    private async Task GoToContactHugeDeltaBAsync(
        string cellPosition,
        int row,
        int col,
        int contact,
        bool stayHere,
        string advPathA,
        string advPathB)
    {
        if (!stayHere)
        {
            await ProberService.Instance.ConnectAsync();
            await ProberService.Instance.DisconnectChuckAsync();
            // Wait slightly after separating (similar to Autoscan logic)
            await Task.Delay(100); 
            await ProberService.Instance.GoToWaferContactAsync(cellPosition, row, col, contact);
            await Task.Delay(100);
            await ProberService.Instance.ConnectChuckAsync();
        }

        if (!string.IsNullOrWhiteSpace(advPathA) && !string.IsNullOrWhiteSpace(advPathB))
        {
            await SwitchMatrixService.Instance.ConnectAsync();
            await SwitchMatrixService.Instance.CreateConnectionAsync(advPathA, advPathB, overrideCheck: true);
        }
    }

    private static IReadOnlyList<CurvePoint> CreateCurvePoints()
    {
        var points = new CurvePoint[41];

        for (var i = 0; i < points.Length; i++)
        {
            var voltage = -2.0 + i * 0.1;
            var current = SimulateIuCurve(voltage);
            points[i] = new CurvePoint(voltage, current);
        }

        return points;
    }

    private static double SimulateIuCurve(double voltage)
    {
        var forward = Math.Exp(Math.Min(voltage, 1.5) * 2.2) - 1.0;
        var reverse = -Math.Exp(Math.Min(-voltage, 1.5) * 0.8) * 1e-9;
        return voltage >= 0 ? forward * 1e-6 : reverse;
    }

    private string _measurementStatus = "Ready";
    public string MeasurementStatus
    {
        get => _measurementStatus;
        set
        {
            if (SetProperty(ref _measurementStatus, value))
            {
                NotifyGlobalProgressPropertiesChanged();
            }
        }
    }

    private bool _autoSwitchToViewer = true;
    public bool AutoSwitchToViewer
    {
        get => _autoSwitchToViewer;
        set => SetProperty(ref _autoSwitchToViewer, value);
    }

    private bool _isMeasuring;
    public bool IsMeasuring
    {
        get => _isMeasuring;
        set
        {
            if (SetProperty(ref _isMeasuring, value))
            {
                OnPropertyChanged(nameof(IsMeasuringSweep));
                (RunMeasurementCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                NotifyGlobalProgressPropertiesChanged();
            }
        }
    }

    private double _measurementProgress;
    public double MeasurementProgress
    {
        get => _measurementProgress;
        set
        {
            if (SetProperty(ref _measurementProgress, value))
            {
                OnPropertyChanged(nameof(MeasurementProgressText));
                NotifyGlobalProgressPropertiesChanged();
            }
        }
    }

    private bool _isProgressIndeterminate;
    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        set
        {
            if (SetProperty(ref _isProgressIndeterminate, value))
            {
                OnPropertyChanged(nameof(MeasurementProgressText));
                NotifyGlobalProgressPropertiesChanged();
            }
        }
    }

    public string MeasurementProgressText => IsProgressIndeterminate ? "Running..." : $"{MeasurementProgress:F0}%";

    public bool IsMeasuringSweep => IsMeasuring && (SelectedPlan is USweepMeasurementPlan || SelectedPlan is PulseSweepMeasurementPlan || SelectedPlan is SpikeTimingMeasurementPlan || SelectedPlan is MemristorSweepMeasurementPlan);

    public ICommand RunMeasurementCommand { get; }

    private async Task RunMeasurementAsync()
    {
        if (IsMeasuring) return;
        if (SelectedPlan == null) return;

        IsMeasuring = true;

        // Update the plotted plan to be the one we are running
        PlottedPlan = SelectedPlan;

        // Immediately clear old measurement view
        PlottedPlan.ResultPoints.Clear();
        RefreshPlotDataFromPlottedPlan();

        ErrorMessage = string.Empty;
        MeasurementStatus = "Starting...";
        MeasurementProgress = 0;
        IsProgressIndeterminate = false;

        // Check auto-save if active and prompt if Profile or Sample Name is empty
        if (AutoSaveMeasurements)
        {
            if (string.IsNullOrWhiteSpace(Settings.Profile) || string.IsNullOrWhiteSpace(Settings.SampleName))
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                {
                    var promptWindow = new SMU_Revamp.Views.SavePromptWindow(Settings.Profile, Settings.SampleName);
                    var result = await promptWindow.ShowDialog<SMU_Revamp.Views.SavePromptResult>(desktop.MainWindow);
                    if (result == null || result.Cancelled)
                    {
                        MeasurementStatus = "Measurement aborted: Profile and Sample Name are required for auto-saving.";
                        IsMeasuring = false;
                        return;
                    }
                    
                    // Update settings values
                    Settings.Profile = result.Profile;
                    Settings.SampleName = result.SampleName;
                    await SaveSettingsAndConfigurationAsync();
                }
            }
        }

        // Check for PotDep times < 20ms
        if (SelectedPlan is PotDepMeasurementPlan potDep)
        {
            if (potDep.GetParamValueDouble("tpot") < 20 ||
                potDep.GetParamValueDouble("tdep") < 20 ||
                potDep.GetParamValueDouble("treadPD") < 20)
            {
                WarningMessage = "Warning: times below 20ms may be inaccurate!";
            }
            else
            {
                WarningMessage = string.Empty;
            }
        }
        else
        {
            WarningMessage = string.Empty;
        }

        // Persist measurement settings automatically when running
        await SaveMeasurementConfigAsync();

        try
        {
            // Connect to SMU
            // Connect to SMU
            MeasurementStatus = "Connecting to E5263 SMU...";
            var smu = E5263_SMU.Instance;

            // Ensure timeout configuration is synced
            var config = ConfigurationService.Instance.GetConfig();
            smu.ResourceString = config.SMUResource;
            smu.SetTimeout(config.SMUTimeoutMs);

            await smu.ConnectAsync();

            MeasurementStatus = $"Executing plan {PlottedPlan.Name}...";
            int lastPointCount = 0;
            var progressReporter = new Progress<double>(p =>
            {
                MeasurementProgress = p;
                if (PlottedPlan != null && PlottedPlan.ResultPoints.Count != lastPointCount)
                {
                    lastPointCount = PlottedPlan.ResultPoints.Count;
                    RefreshPlotDataFromPlottedPlan();
                }
            });
            await PlottedPlan.RunMeasurementAsync(smu, progressReporter);

            // Final update of viewer data to ensure we didn't miss anything.
            RefreshPlotDataFromPlottedPlan();

            if (HasCurvePoints)
            {
                if (CurvePoints.Count == 1)
                {
                    var pt = CurvePoints[0];
                    MeasurementStatus = System.FormattableString.Invariant($"Finished. Measured Point - {XAxisTitle}: {pt.X:F4}, {YAxisTitle}: {pt.Y:E6}");
                }
                else if (PlotSeries.Count > 1)
                {
                    MeasurementStatus = $"Finished. Measured {CurvePoints.Count} points in {PlotSeries.Count} plot series.";
                }
                else
                {
                    MeasurementStatus = $"Finished. Measured {CurvePoints.Count} points.";
                }

                // Auto-save measurement if enabled and has data points
                if (AutoSaveMeasurements)
                {
                    try
                    {
                        var profile = Settings.Profile;
                        var sampleName = Settings.SampleName;
                        
                        string folderPath;
                        try
                        {
                            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                            if (IsScanningWafer)
                            {
                                folderPath = System.IO.Path.Combine(documentsPath, "SMU_Measurements", profile, "Wafermaps", _currentWaferScanFolderName);
                            }
                            else
                            {
                                string normalFolder = $"{sampleName}_{DateTime.Now:yyyyMMdd}";
                                folderPath = System.IO.Path.Combine(documentsPath, "SMU_Measurements", profile, normalFolder);
                            }
                        }
                        catch
                        {
                            if (IsScanningWafer)
                            {
                                folderPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SMU_Measurements", profile, "Wafermaps", _currentWaferScanFolderName);
                            }
                            else
                            {
                                string normalFolder = $"{sampleName}_{DateTime.Now:yyyyMMdd}";
                                folderPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SMU_Measurements", profile, normalFolder);
                            }
                        }
                        
                        if (!System.IO.Directory.Exists(folderPath))
                        {
                            System.IO.Directory.CreateDirectory(folderPath);
                        }
                        
                        var planName = PlottedPlan.Name.Replace(" ", "_").Replace("-", "_");
                        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        
                        string fileName;
                        if (IsScanningWafer)
                        {
                            fileName = $"{sampleName}_{planName}_Cell{TargetCell}_R{TargetRow}C{TargetColumn}_Contact{TargetContact}_{timestamp}.csv";
                        }
                        else
                        {
                            fileName = $"{sampleName}_{planName}_{timestamp}.csv";
                        }
                        
                        var fullPath = System.IO.Path.Combine(folderPath, fileName);
                        
                        var rawLines = PlottedPlan.GetCsvLines();
                        var lines = new List<string>();
                        int insertIndex = 0;
                        if (rawLines.Count > 0 && rawLines[0].StartsWith("sep="))
                        {
                            lines.Add(rawLines[0]);
                            insertIndex = 1;
                        }
                        
                        lines.Add($"# Plan: {PlottedPlan.Name}");
                        foreach (var p in PlottedPlan.Parameters)
                        {
                            lines.Add(System.FormattableString.Invariant($"# {p.Name}: {p.GetValueAsString()}"));
                        }
                        
                        for (int i = insertIndex; i < rawLines.Count; i++)
                        {
                            lines.Add(rawLines[i]);
                        }
                        await System.IO.File.WriteAllLinesAsync(fullPath, lines);
                        
                        MeasurementStatus = $"Finished. Data autosaved to {System.IO.Path.Combine(profile, fileName)}.";

                        NotificationRequested?.Invoke(
                            "Measurement Saved",
                            $"File saved to {fileName}.\nClick to open in Explorer.",
                            fullPath
                        );
                    }
                    catch (Exception saveEx)
                    {
                        WarningMessage = $"Measurement finished, but failed to autosave: {saveEx.Message}";
                    }
                }
            }
            else
            {
                MeasurementStatus = "Finished. No data points parsed.";
            }

            if (AutoSwitchToViewer && !IsScanningWafer)
            {
                SelectedTabIndex = 0; // Auto switch to Viewer tab
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error during measurement: {ex.Message}";
            MeasurementStatus = $"Error: {ex.Message}";
            Console.WriteLine($"Error running measurement: {ex.Message}");
        }
        finally
        {
            // Close sessions
            try { await E5263_SMU.Instance.DisconnectAsync(); } catch { }
            IsMeasuring = false;
            MeasurementProgress = 100;
            IsProgressIndeterminate = false;
        }
    }

    /// <summary>
    /// Exports the current curve points to a CSV file.
    /// </summary>
    public async Task SaveCurvePointsToCsvAsync(string filePath)
    {
        try
        {
            var lines = new List<string>();
            if (PlottedPlan != null)
            {
                var rawLines = PlottedPlan.GetCsvLines();
                int insertIndex = 0;
                if (rawLines.Count > 0 && rawLines[0].StartsWith("sep="))
                {
                    lines.Add(rawLines[0]);
                    insertIndex = 1;
                }
                
                lines.Add($"# Plan: {PlottedPlan.Name}");
                foreach (var p in PlottedPlan.Parameters)
                {
                    lines.Add(System.FormattableString.Invariant($"# {p.Name}: {p.GetValueAsString()}"));
                }
                
                for (int i = insertIndex; i < rawLines.Count; i++)
                {
                    lines.Add(rawLines[i]);
                }
            }
            await System.IO.File.WriteAllLinesAsync(filePath, lines);
            MeasurementStatus = $"Data successfully exported to {System.IO.Path.GetFileName(filePath)}.";

            NotificationRequested?.Invoke(
                "Export Successful",
                $"File exported to {System.IO.Path.GetFileName(filePath)}.\nClick to open in Explorer.",
                filePath
            );
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to export CSV: {ex.Message}";
            Console.WriteLine($"Error exporting CSV: {ex.Message}");
        }
    }

    private async Task SaveMeasurementConfigAsync()
    {
        try
        {
            var config = ConfigurationService.Instance.GetConfig();

            if (config.DefaultPlanParameters == null)
            {
                config.DefaultPlanParameters = new();
            }

            // Save top-level app config parameters from active plans
            foreach (var plan in MeasurementPlans)
            {
                foreach (var param in plan.Parameters)
                {
                    switch (param.Name)
                    {
                        case "WriteChannel":
                        case "Channel":
                            config.SweepChannel = param.GetValueAsString();
                            break;
                        case "StartVoltage":
                            config.SweepStart = param.GetValueAsDouble();
                            break;
                        case "Voltage":
                            config.SweepStart = param.GetValueAsDouble(); // Map to SweepStart for backward compatibility
                            break;
                        case "StopVoltage":
                            config.SweepStop = param.GetValueAsDouble();
                            break;
                        case "Points":
                            config.SweepPoints = param.GetValueAsInt();
                            break;
                        case "Compliance":
                            config.SweepCompliance = param.GetValueAsDouble();
                            break;
                        case "AdcSamples":
                            config.SweepAdcSamples = param.GetValueAsInt();
                            break;
                        case "SweepMode":
                            config.SelectedSweepMode = param.GetValueAsString();
                            break;
                    }
                }
            }

            await ConfigurationService.Instance.SaveAsync(config);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save measurement configuration: {ex.Message}");
        }
    }

    private async Task SaveSettingsAndConfigurationAsync()
    {
        try
        {
            // First apply settings from Settings VM (which updates ConfigurationService internally)
            await Settings.ApplySettingsAsync();

            // Then retrieve updated config, merge our measurement settings, and save
            await SaveMeasurementConfigAsync();
            Settings.ApplyStatusMessage = "Settings and measurement configuration saved.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save configuration: {ex.Message}";
        }
    }

    /// <summary>
    /// Reloads measurement plans to reflect new parameter defaults.
    /// </summary>
    public void ReloadPlanParameters()
    {
        MeasurementPlans = MeasurementPlanLoader.LoadPlans();
        var prevPlanName = SelectedPlan?.Name;
        SelectedPlan = MeasurementPlans.Find(p => p.Name == prevPlanName) ?? (MeasurementPlans.Count > 0 ? MeasurementPlans[0] : null!);
    }

    private void SubscribeToParameterChanges()
    {
        if (_selectedPlan?.Parameters == null) return;
        foreach (var param in _selectedPlan.Parameters)
        {
            param.PropertyChanged -= OnParameterPropertyChanged;
            param.PropertyChanged += OnParameterPropertyChanged;
        }
    }

    private async void OnParameterPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MeasurementParameter.Value))
        {
            UpdateWarningMessage();
            await SaveSettingsAndConfigurationAsync();
        }
    }

    private void UpdateWarningMessage()
    {
        if (_selectedPlan == null)
        {
            WarningMessage = string.Empty;
            return;
        }

        var writeChannelParam = _selectedPlan.Parameters.Find(p => p.Name == "WriteChannel" || p.Name == "Channel");
        var readChannelParam = _selectedPlan.Parameters.Find(p => p.Name == "ReadingChannel");

        if (writeChannelParam != null && readChannelParam != null)
        {
            var writeVal = writeChannelParam.GetValueAsString()?.Trim();
            var readVal = readChannelParam.GetValueAsString()?.Trim();

            if (!string.IsNullOrEmpty(writeVal) && !string.IsNullOrEmpty(readVal) && writeVal == readVal)
            {
                WarningMessage = "Warning: Write Channel and Reading Channel are the same. The setup is generally designed for different write and read channels.";
                return;
            }
        }

        WarningMessage = string.Empty;
    }

    private void RefreshPlotDataFromPlottedPlan()
    {
        if (PlottedPlan == null)
        {
            CurvePoints = Array.Empty<CurvePoint>();
            PlotSeries = Array.Empty<PlotSeries>();
            return;
        }

        CurvePoints = new List<CurvePoint>(PlottedPlan.ResultPoints);
        PlotSeries = PlottedPlan.PlotSeries
            .Where(s => s.Points.Count > 0)
            .ToList();
    }

    private void UpdateSelectedPlanSections()
    {
        if (SelectedPlan == null)
        {
            SelectedPlanSections = new List<ParameterSection>();
            return;
        }

        var sections = new List<ParameterSection>();
        var grouped = new Dictionary<string, List<MeasurementParameter>>();
        var sectionOrder = new List<string>();

        foreach (var param in SelectedPlan.Parameters)
        {
            var secName = param.Section ?? string.Empty;
            if (!grouped.ContainsKey(secName))
            {
                grouped[secName] = new List<MeasurementParameter>();
                sectionOrder.Add(secName);
            }
            grouped[secName].Add(param);
        }

        foreach (var secName in sectionOrder)
        {
            sections.Add(new ParameterSection
            {
                Name = secName,
                Parameters = grouped[secName]
            });
        }

        SelectedPlanSections = sections;
    }

    public void LoadConfigState()
    {
        var config = ConfigurationService.Instance.GetConfig();
        AutoSaveMeasurements = config.AutoSaveMeasurements;
    }

    private async Task SaveAutoSaveSettingAsync(bool value)
    {
        var config = ConfigurationService.Instance.GetConfig();
        config.AutoSaveMeasurements = value;
        await ConfigurationService.Instance.SaveAsync(config);
    }

    // ==========================================
    // RESULT TAB LOGIC
    // ==========================================

    private ObservableCollection<ResultCellViewModel> _resultCells = new();
    public ObservableCollection<ResultCellViewModel> ResultCells
    {
        get => _resultCells;
        set => SetProperty(ref _resultCells, value);
    }

    private bool _isResultFolderLoaded;
    public bool IsResultFolderLoaded
    {
        get => _isResultFolderLoaded;
        set => SetProperty(ref _isResultFolderLoaded, value);
    }

    private ResultCellViewModel? _selectedResultCell;
    public ResultCellViewModel? SelectedResultCell
    {
        get => _selectedResultCell;
        set => SetProperty(ref _selectedResultCell, value);
    }

    private ResultSubCellViewModel? _selectedResultSubCell;
    public ResultSubCellViewModel? SelectedResultSubCell
    {
        get => _selectedResultSubCell;
        set => SetProperty(ref _selectedResultSubCell, value);
    }

    private ResultContactViewModel? _selectedResultContact;
    public ResultContactViewModel? SelectedResultContact
    {
        get => _selectedResultContact;
        set => SetProperty(ref _selectedResultContact, value);
    }

    private List<string> _availableResultMetrics = new() { "Average Resistance", "Max Current", "Max Voltage" };
    public List<string> AvailableResultMetrics
    {
        get => _availableResultMetrics;
        set => SetProperty(ref _availableResultMetrics, value);
    }

    private string _selectedResultMetric = "Average Resistance";
    public string SelectedResultMetric
    {
        get => _selectedResultMetric;
        set
        {
            if (SetProperty(ref _selectedResultMetric, value))
            {
                RecalculateResultMetrics();
            }
        }
    }

    public ICommand LoadScanFolderCommand { get; }
    public ICommand SetSelectedResultCellCommand { get; }
    public ICommand SetSelectedResultSubCellCommand { get; }

    private void InitializeResultTab()
    {
        // Initialize an empty 16x16 grid
        ResultCells.Clear();
        for (int r = 1; r <= 16; r++)
        {
            for (int c = 1; c <= 16; c++)
            {
                ResultCells.Add(new ResultCellViewModel { Row = r, Col = c });
            }
        }
    }

    public async Task LoadScanFolderAsync(string folderPath)
    {
        try
        {
            InitializeResultTab();

            var csvFiles = Directory.GetFiles(folderPath, "*.csv");

            // Regex pattern: "Cell0104_R1C5_Contact3"
            var regex = new Regex(@"Cell(?<cR>\d{2})(?<cC>\d{2})_R(?<sR>\d)C(?<sC>\d)_Contact(?<cont>\d)");

            bool filesFound = false;
            foreach (var file in csvFiles)
            {
                var filename = Path.GetFileName(file);
                var match = regex.Match(filename);
                if (!match.Success) continue;

                filesFound = true;
                int cellRow = int.Parse(match.Groups["cR"].Value);
                int cellCol = int.Parse(match.Groups["cC"].Value);
                int subRow = int.Parse(match.Groups["sR"].Value);
                int subCol = int.Parse(match.Groups["sC"].Value);
                int contact = int.Parse(match.Groups["cont"].Value);

                var cell = ResultCells.FirstOrDefault(c => c.Row == cellRow && c.Col == cellCol);
                if (cell == null) continue;

                var subCell = cell.SubCells.FirstOrDefault(s => s.Row == subRow && s.Col == subCol);
                if (subCell == null)
                {
                    subCell = new ResultSubCellViewModel { Row = subRow, Col = subCol };
                    cell.SubCells.Add(subCell);
                }

                var contactVm = subCell.Contacts.FirstOrDefault(c => c.ContactNumber == contact);
                if (contactVm == null)
                {
                    contactVm = new ResultContactViewModel { ContactNumber = contact };
                    subCell.Contacts.Add(contactVm);
                }

                // Read points
                contactVm.CurveData = ParseCsvPoints(file);
            }

            if (filesFound)
            {
                RecalculateResultMetrics();
                IsResultFolderLoaded = true;
                NotificationRequested?.Invoke("Success", $"Loaded {csvFiles.Length} measurements.", null);
            }
            else
            {
                IsResultFolderLoaded = false;
                NotificationRequested?.Invoke("No compatible data", "No matching wafer map measurement files were found in the selected folder.", null);
            }
        }
        catch (Exception ex)
        {
            IsResultFolderLoaded = false;
            NotificationRequested?.Invoke("Error Loading Folder", ex.Message, null);
        }
    }

    private List<CurvePoint> ParseCsvPoints(string filepath)
    {
        var points = new List<CurvePoint>();
        var lines = File.ReadAllLines(filepath);
        bool isFirstLine = true;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith("sep="))
            {
                continue;
            }
            if (isFirstLine)
            {
                isFirstLine = false;
                continue; // skip header
            }
            var parts = line.Split(',');
            if (parts.Length >= 2)
            {
                if (double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double v) &&
                    double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double c))
                {
                    points.Add(new CurvePoint(v, c));
                }
            }
        }
        return points;
    }

    private void RecalculateResultMetrics()
    {
        // 1. Calculate values for every single Contact
        foreach (var cell in ResultCells)
        {
            foreach (var subCell in cell.SubCells)
            {
                foreach (var contact in subCell.Contacts)
                {
                    contact.AggregatedValue = CalculateMetric(contact.CurveData, SelectedResultMetric);
                }
                // 2. Aggregate to SubCell
                subCell.RecalculateValue();
            }
            // 3. Aggregate to Cell
            cell.RecalculateValue();
        }

        // 4. Find global min / max on Cell level
        var validCells = ResultCells.Where(c => c.SubCells.Any() && !double.IsNaN(c.AggregatedValue)).ToList();
        if (!validCells.Any()) return;

        double minVal = validCells.Min(c => c.AggregatedValue);
        double maxVal = validCells.Max(c => c.AggregatedValue);

        // Apply colors to Cells
        foreach (var cell in ResultCells)
        {
            if (!cell.SubCells.Any())
            {
                cell.Color = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F8FAFC"));
            }
            else
            {
                cell.Color = HeatmapHelper.GetColorForValue(cell.AggregatedValue, minVal, maxVal);
            }
        }

        // Apply colors to SubCells based on subcell min/max
        var allSubCells = ResultCells.SelectMany(c => c.SubCells).Where(s => !double.IsNaN(s.AggregatedValue)).ToList();
        if (allSubCells.Any())
        {
            double minSub = allSubCells.Min(s => s.AggregatedValue);
            double maxSub = allSubCells.Max(s => s.AggregatedValue);
            foreach (var cell in ResultCells)
            {
                foreach (var sub in cell.SubCells)
                {
                    sub.Color = HeatmapHelper.GetColorForValue(sub.AggregatedValue, minSub, maxSub);
                }
            }
        }
    }

    private double CalculateMetric(List<CurvePoint> points, string metric)
    {
        if (points == null || points.Count == 0) return double.NaN;

        if (metric == "Average Resistance")
        {
            var validPoints = points.Where(p => Math.Abs(p.Current) > 1e-12).ToList(); // Ignore ~0 current
            if (!validPoints.Any()) return double.NaN;
            return validPoints.Average(p => Math.Abs(p.Voltage / p.Current));
        }
        else if (metric == "Max Current")
        {
            return points.Max(p => Math.Abs(p.Current));
        }
        else if (metric == "Max Voltage")
        {
            return points.Max(p => Math.Abs(p.Voltage));
        }

        return double.NaN;
    }

    // --- Global Progress Properties ---

    public bool IsGlobalProgressVisible => IsMeasuring || IsScanningWafer;
    
    public double GlobalProgressValue => IsScanningWafer ? WaferScanProgress : MeasurementProgress;
    
    public bool IsGlobalProgressIndeterminate => !IsScanningWafer && IsProgressIndeterminate;
    
    public string GlobalProgressTitle => IsScanningWafer ? "Wafer Scan in progress" : $"Measurement: {SelectedPlan?.Name}";
    
    public string GlobalProgressStatusText
    {
        get
        {
            if (IsScanningWafer)
            {
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(WaferScanCountText)) parts.Add(WaferScanCountText);
                if (!string.IsNullOrWhiteSpace(WaferScanLog)) parts.Add(WaferScanLog);
                if (!string.IsNullOrWhiteSpace(WaferScanEstimatedFinish)) parts.Add(WaferScanEstimatedFinish);
                return string.Join(" | ", parts);
            }
            else if (IsMeasuring)
            {
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(MeasurementStatus)) parts.Add(MeasurementStatus);
                if (!string.IsNullOrWhiteSpace(MeasurementProgressText)) parts.Add(MeasurementProgressText);
                return string.Join(" - ", parts);
            }
            return string.Empty;
        }
    }

    private void NotifyGlobalProgressPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsGlobalProgressVisible));
        OnPropertyChanged(nameof(GlobalProgressValue));
        OnPropertyChanged(nameof(IsGlobalProgressIndeterminate));
        OnPropertyChanged(nameof(GlobalProgressTitle));
        OnPropertyChanged(nameof(GlobalProgressStatusText));
    }
}
