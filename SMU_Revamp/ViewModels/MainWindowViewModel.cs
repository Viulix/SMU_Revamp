using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SMU_Revamp.Models;
using SMU_Revamp.Services;

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

    public bool HasCurvePoints => _curvePoints != null && _curvePoints.Count > 0;

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
                CurvePoints = _selectedPlan?.ResultPoints ?? new List<CurvePoint>();
                UpdateWarningMessage();
                OnPropertyChanged(nameof(IsMeasuringSweep));
                OnPropertyChanged(nameof(XAxisTitle));
            }
        }
    }

    public string XAxisTitle => SelectedPlan is PotDepMeasurementPlan ? "Cycle" : "Voltage (V)";

    private List<ParameterSection> _selectedPlanSections = new();
    public List<ParameterSection> SelectedPlanSections
    {
        get => _selectedPlanSections;
        set => SetProperty(ref _selectedPlanSections, value);
    }

    public SettingsViewModel Settings { get; }

    private int _selectedTabIndex = 2; // Default to Measurements tab
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
        set => SetProperty(ref _waferScanProgress, value);
    }

    private string _waferScanLog = string.Empty;
    public string WaferScanLog
    {
        get => _waferScanLog;
        set => SetProperty(ref _waferScanLog, value);
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
        set => SetProperty(ref _waferScanEstimatedFinish, value);
    }

    private string _waferScanCountText = string.Empty;
    public string WaferScanCountText
    {
        get => _waferScanCountText;
        set => SetProperty(ref _waferScanCountText, value);
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

    public MainWindowViewModel()
    {
        CurvePoints = CreateCurvePoints();
        Settings = new SettingsViewModel();

        MeasurementPlans = new List<IMeasurementPlan>
        {
            new MeasurePointMeasurementPlan(),
            new USweepMeasurementPlan(),
            new PulseSpotMeasurementPlan(),
            new PulseSweepMeasurementPlan(),
            new PotDepMeasurementPlan()
        };
        SelectedPlan = MeasurementPlans[0]; // Default to Measure Point

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
            if (DontShowAlignmentWarning)
            {
                Settings.ShowAlignmentWarning = false;
                var config = ConfigurationService.Instance.GetConfig();
                config.ShowAlignmentWarning = false;
                await ConfigurationService.Instance.SaveAsync(config);
            }
            IsAlignmentWarningVisible = false;
            await ExecuteWaferScanAsync();
        });
        CancelAlignmentWarningCommand = new RelayCommand(() => IsAlignmentWarningVisible = false);

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
        var invalidCells = new System.Collections.Generic.HashSet<string>
        {
            "0101", "0102", "0103", "0114", "0115", "0116",
            "0201", "0202", "0215", "0216",
            "0301", "0316",
            "1401", "1416",
            "1501", "1502", "1515", "1516",
            "1601", "1602", "1603", "1614", "1615", "1616"
        };

        for (int y = 1; y <= 16; y++)
        {
            for (int x = 1; x <= 16; x++)
            {
                string cellId = $"{y:D2}{x:D2}";
                WaferCells.Add(new WaferCellViewModel(cellId, !invalidCells.Contains(cellId)));
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

        if (Settings.ShowAlignmentWarning)
        {
            IsAlignmentWarningVisible = true;
        }
        else
        {
            await ExecuteWaferScanAsync();
        }
    }

    private async Task ExecuteWaferScanAsync()
    {
        IsScanningWafer = true;
        _scanCts = new System.Threading.CancellationTokenSource();

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
        var (deltaX, deltaY) = ComputeHugeDeltaB(cellPosition, row, col, contact);

        if (!stayHere)
        {
            await ProberService.Instance.ConnectAsync();
            await ProberService.Instance.MoveProberAbsoluteAsync(-deltaX, deltaY);
        }

        if (!string.IsNullOrWhiteSpace(advPathA) && !string.IsNullOrWhiteSpace(advPathB))
        {
            await SwitchMatrixService.Instance.ConnectAsync();
            await SwitchMatrixService.Instance.CreateConnectionAsync(advPathA, advPathB, overrideCheck: true);
        }
    }

    private static (int deltaX, int deltaY) ComputeHugeDeltaB(string cellPosition, int row, int col, int contact)
    {
        var (deltaxContact, deltayContact) = contact switch
        {
            1 => (0, 0),
            2 => (290, 0),
            3 => (580, 0),
            4 => (0, 350),
            5 => (290, 350),
            6 => (580, 350),
            _ => (0, 0)
        };

        var cellRow = int.Parse(cellPosition.Substring(0, 2));
        var cellCol = int.Parse(cellPosition.Substring(2, 2));

        var grossX = (cellCol - 4) * 5000;
        var grossY = (cellRow - 1) * 5000;

        var deltaX = grossX + (col - 1) * 1000 + deltaxContact;
        var deltaY = grossY + (row - 1) * 1000 + deltayContact;

        return (deltaX, deltaY);
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
        set => SetProperty(ref _measurementStatus, value);
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
            }
        }
    }

    public string MeasurementProgressText => IsProgressIndeterminate ? "Running..." : $"{MeasurementProgress:F0}%";

    public bool IsMeasuringSweep => IsMeasuring && (SelectedPlan is USweepMeasurementPlan || SelectedPlan is PulseSweepMeasurementPlan);

    public ICommand RunMeasurementCommand { get; }

    private async Task RunMeasurementAsync()
    {
        if (IsMeasuring) return;
        if (SelectedPlan == null) return;

        IsMeasuring = true;
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
            MeasurementStatus = "Connecting to E5263 SMU...";
            var smu = E5263_SMU.Instance;

            // Ensure timeout configuration is synced
            var config = ConfigurationService.Instance.GetConfig();
            smu.ResourceString = config.SMUResource;
            smu.SetTimeout(config.SMUTimeoutMs);

            await smu.ConnectAsync();

            MeasurementStatus = $"Executing plan {SelectedPlan.Name}...";
            var progressReporter = new Progress<double>(p =>
            {
                MeasurementProgress = p;
            });
            await SelectedPlan.RunMeasurementAsync(smu, progressReporter);

            // Update CurvePoints to selected plan's result points
            CurvePoints = new List<CurvePoint>(SelectedPlan.ResultPoints);

            if (CurvePoints.Count > 0)
            {
                if (CurvePoints.Count == 1)
                {
                    var pt = CurvePoints[0];
                    MeasurementStatus = System.FormattableString.Invariant($"Finished. Measured Point - V: {pt.Voltage:F4} V, I: {pt.Current:E6} A");
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
                            folderPath = System.IO.Path.Combine(documentsPath, "SMU_Measurements", profile);
                        }
                        catch
                        {
                            folderPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SMU_Measurements", profile);
                        }
                        
                        if (!System.IO.Directory.Exists(folderPath))
                        {
                            System.IO.Directory.CreateDirectory(folderPath);
                        }
                        
                        var planName = SelectedPlan.Name.Replace(" ", "_").Replace("-", "_");
                        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        var fileName = $"{sampleName}_{planName}_{timestamp}.csv";
                        var fullPath = System.IO.Path.Combine(folderPath, fileName);
                        
                        var lines = new List<string> { "Voltage (V),Current (A)" };
                        foreach (var point in SelectedPlan.ResultPoints)
                        {
                            lines.Add(System.FormattableString.Invariant($"{point.Voltage},{point.Current}"));
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
                SelectedTabIndex = 1; // Auto switch to Viewer tab
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
            var lines = new List<string> { "Voltage (V),Current (A)" };
            foreach (var point in CurvePoints)
            {
                lines.Add(System.FormattableString.Invariant($"{point.Voltage},{point.Current}"));
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
        MeasurementPlans = new List<IMeasurementPlan>
        {
            new MeasurePointMeasurementPlan(),
            new USweepMeasurementPlan(),
            new PulseSpotMeasurementPlan(),
            new PulseSweepMeasurementPlan(),
            new PotDepMeasurementPlan()
        };
        var prevPlanName = SelectedPlan?.Name;
        SelectedPlan = MeasurementPlans.Find(p => p.Name == prevPlanName) ?? MeasurementPlans[0];
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
}
