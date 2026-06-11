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

    private IMeasurementPlan _selectedPlan;
    public IMeasurementPlan SelectedPlan
    {
        get => _selectedPlan;
        set
        {
            if (SetProperty(ref _selectedPlan, value))
            {
                if (_selectedPlan != null)
                {
                    _selectedPlan.LoadDefaults();
                }
                CurvePoints = _selectedPlan?.ResultPoints ?? new List<CurvePoint>();
            }
        }
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

    private bool _debugging;
    public bool Debugging
    {
        get => _debugging;
        set => SetProperty(ref _debugging, value);
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

    public ICommand GoToContactCommand { get; }
    public ICommand SaveSettingsCommand { get; }

    public MainWindowViewModel()
    {
        CurvePoints = CreateCurvePoints();
        Settings = new SettingsViewModel();

        MeasurementPlans = new List<IMeasurementPlan>
        {
            new MeasurePointMeasurementPlan(),
            new USweepMeasurementPlan()
        };
        _selectedPlan = MeasurementPlans[0]; // Default to Measure Point

        GoToContactCommand = new AsyncRelayCommand(GoToContactAsync);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAndConfigurationAsync);
        RunMeasurementCommand = new AsyncRelayCommand(RunMeasurementAsync);
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
                Debugging,
                AdvPathA,
                AdvPathB);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
            Console.WriteLine($"Error moving to contact: {ex.Message}");
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
        bool debugging,
        string advPathA,
        string advPathB)
    {
        var (deltaX, deltaY) = ComputeHugeDeltaB(cellPosition, row, col, contact);

        if (!stayHere || debugging)
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
        set => SetProperty(ref _isMeasuring, value);
    }

    public ICommand RunMeasurementCommand { get; }

    private async Task RunMeasurementAsync()
    {
        if (IsMeasuring) return;
        if (SelectedPlan == null) return;

        IsMeasuring = true;
        ErrorMessage = string.Empty;
        MeasurementStatus = "Starting...";

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
            await SelectedPlan.RunMeasurementAsync(smu);

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
            }
            else
            {
                MeasurementStatus = "Finished. No data points parsed.";
            }

            if (AutoSwitchToViewer)
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
            new USweepMeasurementPlan()
        };
        var prevPlanName = SelectedPlan?.Name;
        SelectedPlan = MeasurementPlans.Find(p => p.Name == prevPlanName) ?? MeasurementPlans[0];
    }
}
