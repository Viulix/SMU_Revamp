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

    public ReadOnlyCollection<string> MeasurementModes { get; }

    public SettingsViewModel Settings { get; }

    private int _selectedTabIndex = 2; // Default to Measurements tab
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    private string _selectedMeasurementMode = "Measure Point";
    public string SelectedMeasurementMode
    {
        get => _selectedMeasurementMode;
        set => SetProperty(ref _selectedMeasurementMode, value);
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
        MeasurementModes = new ReadOnlyCollection<string>([
            "Measure Point",
            "U-Sweep"
        ]);
        Settings = new SettingsViewModel();

        // Load measurement configuration from settings
        var config = ConfigurationService.Instance.GetConfig();
        _sweepChannel = config.SweepChannel;
        _sweepStart = config.SweepStart;
        _sweepStop = config.SweepStop;
        _sweepPoints = config.SweepPoints;
        _sweepCompliance = config.SweepCompliance;
        _sweepAdcSamples = config.SweepAdcSamples;
        _selectedSweepMode = config.SelectedSweepMode;

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

    private string _sweepChannel = "2";
    public string SweepChannel
    {
        get => _sweepChannel;
        set => SetProperty(ref _sweepChannel, value);
    }

    private double _sweepStart = 0.0;
    public double SweepStart
    {
        get => _sweepStart;
        set => SetProperty(ref _sweepStart, value);
    }

    private double _sweepStop = 1.5;
    public double SweepStop
    {
        get => _sweepStop;
        set => SetProperty(ref _sweepStop, value);
    }

    private int _sweepPoints = 41;
    public int SweepPoints
    {
        get => _sweepPoints;
        set => SetProperty(ref _sweepPoints, value);
    }

    private double _sweepCompliance = 0.1;
    public double SweepCompliance
    {
        get => _sweepCompliance;
        set => SetProperty(ref _sweepCompliance, value);
    }

    private int _sweepAdcSamples = 1;
    public int SweepAdcSamples
    {
        get => _sweepAdcSamples;
        set => SetProperty(ref _sweepAdcSamples, value);
    }

    private string _selectedSweepMode = "Double Staircase (3)";
    public string SelectedSweepMode
    {
        get => _selectedSweepMode;
        set => SetProperty(ref _selectedSweepMode, value);
    }

    public ReadOnlyCollection<string> SweepModes { get; } = new ReadOnlyCollection<string>([
        "Single Staircase (1)",
        "Double Staircase (3)"
    ]);

    private string _measurementStatus = "Ready";
    public string MeasurementStatus
    {
        get => _measurementStatus;
        set => SetProperty(ref _measurementStatus, value);
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
        IsMeasuring = true;
        ErrorMessage = string.Empty;
        MeasurementStatus = "Starting...";
        SelectedTabIndex = 1; // Auto switch to Viewer tab

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

            MeasurementStatus = "Configuring SMU...";
            // Setup sequence according to legacy guidelines
            await smu.SendCommandAsync("*RST");
            await smu.SendCommandAsync("FMT 1");
            await smu.SendCommandAsync("TSC 1");
            await smu.SendCommandAsync($"CN {SweepChannel}");
            await smu.SendCommandAsync($"AV -{SweepAdcSamples},0");

            // Extract the numeric sweep mode (e.g. 1 or 3) from SelectedSweepMode
            int modeValue = 3;
            if (SelectedSweepMode.Contains("(1)")) modeValue = 1;
            else if (SelectedSweepMode.Contains("(3)")) modeValue = 3;

            // WV command defines sweep
            // Format: WV <channel>,<mode>,<range>,<start>,<stop>,<steps>,<Icomp>
            // We use System.FormattableString.Invariant to format floating-point parameters (Start, Stop, Compliance) 
            // with a dot decimal separator, regardless of the system's locale (e.g., German).
            var wvCommand = System.FormattableString.Invariant($"WV {SweepChannel},{modeValue},0,{SweepStart},{SweepStop},{SweepPoints},{SweepCompliance}");
            await smu.SendCommandAsync(wvCommand);

            // Error detection right after the WV configuration command
            var wvError = await smu.CheckErrorAsync();
            if (wvError != null)
            {
                throw new InvalidOperationException($"SMU rejected WV command parameters: {wvError}");
            }

            await smu.SendCommandAsync($"RI {SweepChannel},0");
            await smu.SendCommandAsync($"MM 2,{SweepChannel}");
            var mmError = await smu.CheckErrorAsync();
            if (mmError != null)
            {
                throw new InvalidOperationException($"SMU rejected MM command: {mmError}");
            }

            await smu.SendCommandAsync($"CMM {SweepChannel},1");
            var cmmError = await smu.CheckErrorAsync();
            if (cmmError != null)
            {
                throw new InvalidOperationException($"SMU rejected CMM command: {cmmError}");
            }

            MeasurementStatus = "Executing Sweep Measurement...";
            await smu.SendCommandAsync("TSR");
            await smu.SendCommandAsync("XE");

            // Wait for completion using TSQ query
            string tsqResponse = await smu.QueryAsync("TSQ", readBufferChars: 50);

            MeasurementStatus = "Reading sweep results...";
            // Calculate buffer size:
            // Under FMT 1 + TSC 1: each data point outputs 32 bytes (2 blocks: time block 16 chars + data block 16 chars).
            int expectedBufferLength = SweepPoints * 32 * (modeValue == 3 ? 2 : 1) + 200;
            string rawData = await smu.ReadResponseAsync(expectedBufferLength);

            MeasurementStatus = "Parsing sweep results...";
            var parsedPoints = ParseSmuData(rawData, modeValue);

            if (parsedPoints.Count > 0)
            {
                CurvePoints = parsedPoints;
                MeasurementStatus = $"Finished. Measured {parsedPoints.Count} points.";
            }
            else
            {
                MeasurementStatus = "Finished. No data points parsed.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error during sweep: {ex.Message}";
            MeasurementStatus = $"Error: {ex.Message}";
            Console.WriteLine($"Error running sweep: {ex.Message}");
        }
        finally
        {
            // Close sessions
            try { await E5263_SMU.Instance.DisconnectAsync(); } catch { }
            IsMeasuring = false;
        }
    }

    private List<CurvePoint> ParseSmuData(string rawData, int modeValue)
    {
        var points = new List<CurvePoint>();
        if (string.IsNullOrWhiteSpace(rawData)) return points;

        // Under FMT 1 + TSC 1, data is comma-separated ASCII items
        var items = rawData.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var parsedCurrents = new List<double>();
        var parsedVoltages = new List<double>();
        var parsedTimes = new List<double>();

        foreach (var item in items)
        {
            var trimmed = item.Trim();
            if (trimmed.Length < 4) continue;

            char firstChar = trimmed[0];
            char thirdChar = trimmed[2];
            string numStr = trimmed.Substring(3);

            if (firstChar == 'T')
            {
                // Time stamp (e.g. TAV...)
                if (double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double t))
                {
                    parsedTimes.Add(t);
                }
            }
            else if (thirdChar == 'I')
            {
                // Current measurement (e.g. N2I..., C2I...)
                if (double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double iVal))
                {
                    parsedCurrents.Add(iVal);
                }
            }
            else if (thirdChar == 'V')
            {
                // Voltage measurement (e.g. N2V..., C2V...)
                if (double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double vVal))
                {
                    parsedVoltages.Add(vVal);
                }
            }
        }

        int count = parsedCurrents.Count;
        if (count == 0) return points;

        // If the instrument returned both voltage and current measurements for each point, pair them directly
        if (parsedVoltages.Count == count)
        {
            for (int i = 0; i < count; i++)
            {
                points.Add(new CurvePoint(parsedVoltages[i], parsedCurrents[i]));
            }
        }
        else
        {
            // Otherwise, fall back to calculating step voltage based on sweep parameters
            if (modeValue == 1)
            {
                // Single sweep: start -> stop
                for (int i = 0; i < count; i++)
                {
                    double v = SweepStart;
                    if (count > 1)
                    {
                        v = SweepStart + i * (SweepStop - SweepStart) / (count - 1);
                    }
                    points.Add(new CurvePoint(v, parsedCurrents[i]));
                }
            }
            else
            {
                // Double sweep: start -> stop -> start
                int halfPoints = (count + 1) / 2;
                for (int i = 0; i < count; i++)
                {
                    double v;
                    if (i < halfPoints)
                    {
                        v = SweepStart;
                        if (halfPoints > 1)
                        {
                            v = SweepStart + i * (SweepStop - SweepStart) / (halfPoints - 1);
                        }
                    }
                    else
                    {
                        v = SweepStop;
                        if (halfPoints > 1)
                        {
                            v = SweepStop - (i - halfPoints + 1) * (SweepStop - SweepStart) / (halfPoints - 1);
                        }
                    }
                    points.Add(new CurvePoint(v, parsedCurrents[i]));
                }
            }
        }

        return points;
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
            config.SweepChannel = SweepChannel;
            config.SweepStart = SweepStart;
            config.SweepStop = SweepStop;
            config.SweepPoints = SweepPoints;
            config.SweepCompliance = SweepCompliance;
            config.SweepAdcSamples = SweepAdcSamples;
            config.SelectedSweepMode = SelectedSweepMode;
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
            var config = ConfigurationService.Instance.GetConfig();
            config.SweepChannel = SweepChannel;
            config.SweepStart = SweepStart;
            config.SweepStop = SweepStop;
            config.SweepPoints = SweepPoints;
            config.SweepCompliance = SweepCompliance;
            config.SweepAdcSamples = SweepAdcSamples;
            config.SelectedSweepMode = SelectedSweepMode;

            await ConfigurationService.Instance.SaveAsync(config);
            Settings.ApplyStatusMessage = "Settings and measurement configuration saved.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save configuration: {ex.Message}";
        }
    }
}
