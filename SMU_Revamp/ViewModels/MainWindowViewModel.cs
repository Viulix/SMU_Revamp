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
                InitializeSeriesSettings();
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
                    LoadLastConfig();
                    if (!_isLoadingPreset)
                    {
                        SelectedPreset = null;
                    }
                    LoadAvailablePresets();
                    SubscribeToParameterChanges();
                }
                UpdateSelectedPlanSections();
                UpdateWarningMessage();
                OnPropertyChanged(nameof(IsMeasuringSweep));
                OnPropertyChanged(nameof(GlobalProgressTitle));
                OnPropertyChanged(nameof(IsModularPlanActive));
                OnPropertyChanged(nameof(SelectedPlanSteps));
                if (SelectedPlan is ModularSequenceMeasurementPlan modular && modular.Steps.Count > 0)
                {
                    SelectedSequenceStep = modular.Steps[0];
                }
                else
                {
                    SelectedSequenceStep = null;
                }
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
                if (_plottedPlan != null)
                {
                    SelectedPlotStyle = _plottedPlan.DefaultPlotStyle;
                }
                OnPropertyChanged(nameof(IsPlottedPlanLoaded));
                OnPropertyChanged(nameof(PlotTitle));
                OnPropertyChanged(nameof(LinearPlotTitle));
                OnPropertyChanged(nameof(LogPlotTitle));
                OnPropertyChanged(nameof(XAxisTitle));
                OnPropertyChanged(nameof(YAxisTitle));
                OnPropertyChanged(nameof(ShowLogPlot));
                OnPropertyChanged(nameof(IsLogPlotVisible));
                OnPropertyChanged(nameof(IsTwoPlotsVisible));
                OnPropertyChanged(nameof(PlotAspectRatio));
                OnPropertyChanged(nameof(PlotBaseWidth));
                OnPropertyChanged(nameof(PlotBaseHeight));
            }
        }
    }

    private void InitializeSeriesSettings()
    {
        var currentSettings = SeriesSettings.ToList();
        SeriesSettings.Clear();
        
        var seriesCount = PlotSeries?.Count ?? 1;
        var defaultColors = new[] { "#1f77b4", "#ff7f0e", "#2ca02c", "#d62728", "#9467bd", "#8c564b", "#e377c2", "#7f7f7f", "#bcbd22", "#17becf" };

        for (int i = 0; i < seriesCount; i++)
        {
            var seriesName = PlotSeries != null && PlotSeries.Count > i ? PlotSeries[i].Name : $"Series {i + 1}";
            var defaultColor = defaultColors[i % defaultColors.Length];
            
            // Preserve existing custom color for this series name if possible, otherwise use default
            var existing = currentSettings.FirstOrDefault(s => s.SeriesName == seriesName);
            var colorToUse = existing != null ? existing.ColorHex : defaultColor;

            SeriesSettings.Add(new SeriesSetting(seriesName, colorToUse));
        }
    }

    public bool IsPlottedPlanLoaded => PlottedPlan != null;

    public string PlotTitle => !string.IsNullOrWhiteSpace(CustomPlotTitle) ? CustomPlotTitle : (PlottedPlan?.PlotTitle ?? "Measurement Data");
    public string LinearPlotTitle => !string.IsNullOrWhiteSpace(CustomPlotTitle) ? CustomPlotTitle : $"{PlotTitle} - Linear";
    public string LogPlotTitle => !string.IsNullOrWhiteSpace(CustomPlotTitle) ? CustomPlotTitle : $"{PlotTitle} - Logarithmic Y";
    public string XAxisTitle => !string.IsNullOrWhiteSpace(CustomXAxisTitle) ? CustomXAxisTitle : (PlottedPlan?.XAxisLabel ?? "X");
    public string YAxisTitle => !string.IsNullOrWhiteSpace(CustomYAxisTitle) ? CustomYAxisTitle : (PlottedPlan?.YAxisLabel ?? "Y");
    public bool ShowLogPlot => PlottedPlan?.ShowLogPlot ?? true;

    private string? _customPlotTitle;
    public string? CustomPlotTitle { get => _customPlotTitle; set { if (SetProperty(ref _customPlotTitle, value)) { OnPropertyChanged(nameof(PlotTitle)); OnPropertyChanged(nameof(LinearPlotTitle)); OnPropertyChanged(nameof(LogPlotTitle)); } } }

    private string? _customXAxisTitle;
    public string? CustomXAxisTitle { get => _customXAxisTitle; set { if (SetProperty(ref _customXAxisTitle, value)) OnPropertyChanged(nameof(XAxisTitle)); } }

    private string? _customYAxisTitle;
    public string? CustomYAxisTitle { get => _customYAxisTitle; set { if (SetProperty(ref _customYAxisTitle, value)) OnPropertyChanged(nameof(YAxisTitle)); } }

    private double? _customXMin;
    public double? CustomXMin { get => _customXMin; set => SetProperty(ref _customXMin, value); }
    
    private double? _customXMax;
    public double? CustomXMax { get => _customXMax; set => SetProperty(ref _customXMax, value); }
    
    private double? _customYMin;
    public double? CustomYMin { get => _customYMin; set => SetProperty(ref _customYMin, value); }
    
    private double? _customYMax;
    public double? CustomYMax { get => _customYMax; set => SetProperty(ref _customYMax, value); }

    private bool _autoFitDataX;
    public bool AutoFitDataX { get => _autoFitDataX; set => SetProperty(ref _autoFitDataX, value); }
    private bool _autoFitDataY;
    public bool AutoFitDataY { get => _autoFitDataY; set => SetProperty(ref _autoFitDataY, value); }

    private string? _customAspectRatioString;
    public string? CustomAspectRatioString 
    { 
        get => _customAspectRatioString; 
        set 
        { 
            if (SetProperty(ref _customAspectRatioString, value))
            {
                OnPropertyChanged(nameof(PlotAspectRatio));
                OnPropertyChanged(nameof(PlotBaseHeight));
            }
        } 
    }

    public ObservableCollection<SeriesSetting> SeriesSettings { get; } = new();

    public ICommand ResetAdvancedSettingsCommand { get; }

    public double PlotAspectRatio
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CustomAspectRatioString))
            {
                var parts = CustomAspectRatioString.Split(new[] { ':', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && double.TryParse(parts[0], out double w) && double.TryParse(parts[1], out double h) && h > 0)
                {
                    return w / h;
                }
                if (double.TryParse(CustomAspectRatioString, out double val) && val > 0)
                {
                    return val;
                }
            }
            return PlottedPlan?.PlotAspectRatio ?? 1.333;
        }
    }
    public double PlotBaseWidth => 800.0;
    public double PlotBaseHeight => PlotBaseWidth / PlotAspectRatio;

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
                OnPropertyChanged(nameof(IsTwoPlotsVisible));
            }
        }
    }

    public System.Collections.Generic.IReadOnlyList<string> AvailablePlotStyles { get; } = new[] 
    { 
        "Line", 
        "Scatter", 
        "Line & Scatter", 
        "Interpolated Line", 
        "Interpolated Line & Scatter" 
    };

    private string _selectedPlotStyleString = "Line";
    public string SelectedPlotStyleString
    {
        get => _selectedPlotStyleString;
        set
        {
            if (SetProperty(ref _selectedPlotStyleString, value))
            {
                SelectedPlotStyle = value switch
                {
                    "Scatter" => PlotStyle.Scatter,
                    "Line & Scatter" => PlotStyle.LineAndScatter,
                    "Interpolated Line" => PlotStyle.InterpolatedLine,
                    "Interpolated Line & Scatter" => PlotStyle.InterpolatedLineAndScatter,
                    _ => PlotStyle.Line
                };
            }
        }
    }

    private PlotStyle _selectedPlotStyle = PlotStyle.Line;
    public PlotStyle SelectedPlotStyle
    {
        get => _selectedPlotStyle;
        private set => SetProperty(ref _selectedPlotStyle, value);
    }

    public bool IsLinearPlotVisible => SelectedPlotView == "Both" || SelectedPlotView == "Linear Only";

    public bool IsLogPlotVisible => (SelectedPlotView == "Both" || SelectedPlotView == "Logarithmic Only") && ShowLogPlot;

    public bool IsTwoPlotsVisible => IsLinearPlotVisible && IsLogPlotVisible;

    private bool _isMeasurementLogarithmic = false;
    public bool IsMeasurementLogarithmic
    {
        get => _isMeasurementLogarithmic;
        set => SetProperty(ref _isMeasurementLogarithmic, value);
    }

    private bool _isMeasurementLogarithmicX = false;
    public bool IsMeasurementLogarithmicX
    {
        get => _isMeasurementLogarithmicX;
        set => SetProperty(ref _isMeasurementLogarithmicX, value);
    }

    private bool _isViewerLogarithmicX = false;
    public bool IsViewerLogarithmicX
    {
        get => _isViewerLogarithmicX;
        set => SetProperty(ref _isViewerLogarithmicX, value);
    }

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
                OnPropertyChanged(nameof(HasWaferScanLog));
                NotifyGlobalProgressPropertiesChanged();
            }
        }
    }

    public bool HasWaferScanLog => !string.IsNullOrWhiteSpace(WaferScanLog);

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

    private bool _isLoadingWaferScanPreset;
    
    private int _waferScanDelayMs = 500;
    public int WaferScanDelayMs
    {
        get => _waferScanDelayMs;
        set
        {
            if (SetProperty(ref _waferScanDelayMs, value))
            {
                if (!_isLoadingWaferScanPreset && !string.IsNullOrEmpty(SelectedWaferScanPreset))
                {
                    SelectedWaferScanPreset = string.Empty;
                }
            }
        }
    }

    private System.Collections.ObjectModel.ObservableCollection<string> _waferScanPresetNames = new();
    public System.Collections.ObjectModel.ObservableCollection<string> WaferScanPresetNames
    {
        get => _waferScanPresetNames;
        set => SetProperty(ref _waferScanPresetNames, value);
    }

    private string _selectedWaferScanPreset = string.Empty;
    public string SelectedWaferScanPreset
    {
        get => _selectedWaferScanPreset;
        set
        {
            if (SetProperty(ref _selectedWaferScanPreset, value))
            {
                if (!string.IsNullOrEmpty(value))
                {
                    _ = LoadWaferScanPresetAsync(value);
                }
            }
        }
    }

    private string _newWaferScanPresetName = string.Empty;
    public string NewWaferScanPresetName
    {
        get => _newWaferScanPresetName;
        set => SetProperty(ref _newWaferScanPresetName, value);
    }

    public System.Collections.ObjectModel.ObservableCollection<ContactViewModel> Contacts { get; } = new();

    public ICommand SelectAllContactsCommand { get; }
    public ICommand DeselectAllContactsCommand { get; }

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

    // Sequencer properties and commands
    public bool IsModularPlanActive => SelectedPlan is ModularSequenceMeasurementPlan;

    public ObservableCollection<SequenceStep>? SelectedPlanSteps
    {
        get
        {
            if (SelectedPlan is ModularSequenceMeasurementPlan modular)
            {
                return modular.Steps;
            }
            return null;
        }
    }

    private SequenceStep? _selectedSequenceStep;
    public SequenceStep? SelectedSequenceStep
    {
        get => _selectedSequenceStep;
        set
        {
            if (SetProperty(ref _selectedSequenceStep, value))
            {
                (MoveStepUpCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (MoveStepDownCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (DeleteStepCommand as RelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public ICommand AddPulseStepCommand { get; }
    public ICommand AddPointStepCommand { get; }
    public ICommand AddSweepStepCommand { get; }
    public ICommand AddMeasureStepCommand { get; }
    public ICommand MoveStepUpCommand { get; }
    public ICommand MoveStepDownCommand { get; }
    public ICommand DeleteStepCommand { get; }

    // Preset properties and commands
    private ObservableCollection<MeasurementPreset> _availablePresets = new();
    public ObservableCollection<MeasurementPreset> AvailablePresets
    {
        get => _availablePresets;
        set => SetProperty(ref _availablePresets, value);
    }

    private bool _isLoadingPreset;
    private MeasurementPreset? _selectedPreset;
    public MeasurementPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetProperty(ref _selectedPreset, value) && value != null)
            {
                _isLoadingPreset = true;
                
                // 1. Switch the plan if needed
                if (!string.IsNullOrEmpty(value.PlanName) && (SelectedPlan == null || SelectedPlan.Name != value.PlanName))
                {
                    var targetPlan = MeasurementPlans.FirstOrDefault(p => p.Name == value.PlanName);
                    if (targetPlan != null)
                    {
                        SelectedPlan = targetPlan;
                    }
                }
                
                // 2. Load the parameter values for the active plan
                if (SelectedPlan != null)
                {
                    foreach (var param in SelectedPlan.Parameters)
                    {
                        if (value.Parameters.TryGetValue(param.Name, out var stringVal))
                        {
                            if (param.Type == ParameterType.Number)
                            {
                                if (ParameterConfigHelper.TryParseDoubleRobust(stringVal, out double d))
                                    param.Value = d;
                            }
                            else if (param.Type == ParameterType.Checkbox)
                            {
                                if (bool.TryParse(stringVal, out bool b))
                                    param.Value = b;
                            }
                            else
                            {
                                param.Value = stringVal;
                            }
                        }
                    }
                }
                // Save immediately when preset is loaded so the new config becomes the "last" config
                _ = SaveSettingsAndConfigurationAsync();
                _isLoadingPreset = false;
            }
        }
    }

    private string _newPresetName = string.Empty;
    public string NewPresetName
    {
        get => _newPresetName;
        set 
        {
            if (SetProperty(ref _newPresetName, value))
            {
                IsPresetNameInvalid = false;
            }
        }
    }

    private bool _isPresetNameInvalid;
    public bool IsPresetNameInvalid
    {
        get => _isPresetNameInvalid;
        set => SetProperty(ref _isPresetNameInvalid, value);
    }

    private string _presetNameErrorMessage = string.Empty;
    public string PresetNameErrorMessage
    {
        get => _presetNameErrorMessage;
        set => SetProperty(ref _presetNameErrorMessage, value);
    }

    private bool _isOverwriteWarningVisible;
    public bool IsOverwriteWarningVisible
    {
        get => _isOverwriteWarningVisible;
        set => SetProperty(ref _isOverwriteWarningVisible, value);
    }

    private bool _isDeleteWarningVisible;
    public bool IsDeleteWarningVisible
    {
        get => _isDeleteWarningVisible;
        set => SetProperty(ref _isDeleteWarningVisible, value);
    }

    public ICommand SavePresetCommand { get; }
    public ICommand ConfirmSavePresetCommand { get; }
    public ICommand CancelSavePresetCommand { get; }
    public ICommand DeletePresetCommand { get; }
    public ICommand ConfirmDeletePresetCommand { get; }
    public ICommand CancelDeletePresetCommand { get; }

    private List<int> _parsedScanContacts = new();
    private int _totalExpectedCells = 0;
    private int _totalExpectedSubCells = 0;
    private string _currentWaferScanFolderName = string.Empty;

    private void ResetAdvancedSettings()
    {
        CustomPlotTitle = null;
        CustomXAxisTitle = null;
        CustomYAxisTitle = null;
        CustomXMin = null;
        CustomXMax = null;
        CustomYMin = null;
        CustomYMax = null;
        AutoFitDataX = false;
        AutoFitDataY = false;
        CustomAspectRatioString = null;
        
        var defaultColors = new[] { "#1f77b4", "#ff7f0e", "#2ca02c", "#d62728", "#9467bd", "#8c564b", "#e377c2", "#7f7f7f", "#bcbd22", "#17becf" };
        for (int i = 0; i < SeriesSettings.Count; i++)
        {
            SeriesSettings[i].ColorHex = defaultColors[i % defaultColors.Length];
        }
    }

    public MainWindowViewModel()
    {
        var config = ConfigurationService.Instance.GetConfig();
        if (!string.IsNullOrEmpty(config.VisualizationHeatmapColorLow))
        {
            _selectedHeatmapColorLow = config.VisualizationHeatmapColorLow;
        }
        if (!string.IsNullOrEmpty(config.VisualizationHeatmapColorHigh))
        {
            _selectedHeatmapColorHigh = config.VisualizationHeatmapColorHigh;
        }

        _memristorWeightSnr = config.MemristorWeightSnr;
        _memristorWeightNonlinearity = config.MemristorWeightNonlinearity;
        _memristorWeightHysteresis = config.MemristorWeightHysteresis;
        _memristorWeightBranchSep = config.MemristorWeightBranchSep;
        _memristorWeightPinch = config.MemristorWeightPinch;
        _memristorWeightSmoothness = config.MemristorWeightSmoothness;

        for (int i = 1; i <= 6; i++)
        {
            Contacts.Add(new ContactViewModel(i.ToString(), true));
        }

        SelectAllContactsCommand = new RelayCommand(() => { foreach (var c in Contacts) c.IsSelected = true; });
        DeselectAllContactsCommand = new RelayCommand(() => { foreach (var c in Contacts) c.IsSelected = false; });

        SavePresetCommand = new RelayCommand(() =>
        {
            if (string.IsNullOrWhiteSpace(NewPresetName))
            {
                IsPresetNameInvalid = true;
                PresetNameErrorMessage = "Bitte geben Sie einen Preset-Namen ein.";
                return;
            }
            
            IsPresetNameInvalid = false;
            var name = NewPresetName.Trim();
            
            if (AvailablePresets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                IsOverwriteWarningVisible = true;
            }
            else
            {
                ExecuteSavePreset(name);
            }
        });

        ConfirmSavePresetCommand = new RelayCommand(() =>
        {
            IsOverwriteWarningVisible = false;
            if (!string.IsNullOrWhiteSpace(NewPresetName))
            {
                ExecuteSavePreset(NewPresetName.Trim());
            }
        });

        CancelSavePresetCommand = new RelayCommand(() => IsOverwriteWarningVisible = false);

        DeletePresetCommand = new RelayCommand(() =>
        {
            if (SelectedPreset == null) return;
            IsDeleteWarningVisible = true;
        });

        ConfirmDeletePresetCommand = new AsyncRelayCommand(async () =>
        {
            if (SelectedPreset == null) return;
            var config = ConfigurationService.Instance.GetConfig();
            if (config.Presets != null)
            {
                config.Presets.RemoveAll(p => p.Name == SelectedPreset.Name);
                await ConfigurationService.Instance.SaveAsync(config);
                LoadAvailablePresets();
            }
            IsDeleteWarningVisible = false;
        });

        CancelDeletePresetCommand = new RelayCommand(() => IsDeleteWarningVisible = false);

        AddPulseStepCommand = new RelayCommand(() => AddSequenceStep(StepType.Pulse));
        AddPointStepCommand = new RelayCommand(() => AddSequenceStep(StepType.Point));
        AddSweepStepCommand = new RelayCommand(() => AddSequenceStep(StepType.Sweep));
        AddMeasureStepCommand = new RelayCommand(() => AddSequenceStep(StepType.Measure));
        MoveStepUpCommand = new RelayCommand(MoveSelectedStepUp, () => SelectedSequenceStep != null);
        MoveStepDownCommand = new RelayCommand(MoveSelectedStepDown, () => SelectedSequenceStep != null);
        DeleteStepCommand = new RelayCommand(DeleteSelectedStep, () => SelectedSequenceStep != null);

        ResetAdvancedSettingsCommand = new RelayCommand(ResetAdvancedSettings);
        CurvePoints = Array.Empty<CurvePoint>();
        Settings = new SettingsViewModel();

        MeasurementPlans = MeasurementPlanLoader.LoadPlans();
        SelectedPlan = MeasurementPlans.Count > 0 ? MeasurementPlans.Find(p => p.Name == "Measure Point") ?? MeasurementPlans[0] : null!;
        PlottedPlan = null;

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
        SubscribeToWaferMapChanges();

        ScanWaferCommand = new AsyncRelayCommand(StartWaferScanAsync, () => !IsScanningWafer);
    }

    private void SubscribeToWaferMapChanges()
    {
        foreach (var c in WaferCells) c.PropertyChanged += OnWaferMapPropertyChanged;
        foreach (var c in SubCells) c.PropertyChanged += OnWaferMapPropertyChanged;
        foreach (var c in Contacts) c.PropertyChanged += OnWaferMapPropertyChanged;
    }

    private void OnWaferMapPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "IsSelected")
        {
            if (!_isLoadingWaferScanPreset && !string.IsNullOrEmpty(SelectedWaferScanPreset))
            {
                SelectedWaferScanPreset = string.Empty;
            }
        }
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

    private async Task LoadWaferScanPresetAsync(string presetName)
    {
        _isLoadingWaferScanPreset = true;
        try
        {
            var config = ConfigurationService.Instance.GetConfig();
            var preset = config.WaferScanPresets?.FirstOrDefault(p => p.Name == presetName);
            if (preset == null) return;

            if (int.TryParse(preset.DelayMs, out int delay))
                WaferScanDelayMs = delay;

            foreach (var c in SubCells)
            {
            if (!c.IsValid) continue;
            c.IsSelected = preset.SelectedSubCells.Contains(c.Id);
        }

        foreach (var c in Contacts)
        {
            if (int.TryParse(c.Id, out int cId))
            {
                c.IsSelected = preset.SelectedContacts.Contains(cId);
            }
        }

        foreach (var c in WaferCells)
        {
            if (!c.IsValid) continue;
            c.IsSelected = preset.SelectedWaferCells.Contains(c.Id);
        }
        
        NewWaferScanPresetName = presetName;
        NotificationRequested?.Invoke("Preset Loaded", $"Wafer scan preset '{presetName}' loaded.", null);
        }
        finally
        {
            _isLoadingWaferScanPreset = false;
        }
    }

    [RelayCommand]
    private async Task SaveWaferScanPresetAsync()
    {
        if (string.IsNullOrWhiteSpace(NewWaferScanPresetName))
        {
            NotificationRequested?.Invoke("Error", "Please enter a preset name.", null);
            return;
        }

        var config = ConfigurationService.Instance.GetConfig();
        if (config.WaferScanPresets == null) config.WaferScanPresets = new();
        
        var preset = config.WaferScanPresets.FirstOrDefault(p => p.Name == NewWaferScanPresetName);
        if (preset == null)
        {
            preset = new Models.WaferScanPreset { Name = NewWaferScanPresetName };
            config.WaferScanPresets.Add(preset);
            WaferScanPresetNames.Add(NewWaferScanPresetName);
        }

        preset.DelayMs = WaferScanDelayMs.ToString();
        preset.SelectedSubCells = SubCells.Where(c => c.IsSelected).Select(c => c.Id).ToList();
        preset.SelectedContacts = Contacts.Where(c => c.IsSelected && int.TryParse(c.Id, out _)).Select(c => int.Parse(c.Id)).ToList();
        preset.SelectedWaferCells = WaferCells.Where(c => c.IsSelected).Select(c => c.Id).ToList();

        await ConfigurationService.Instance.SaveAsync(config);
        
        SelectedWaferScanPreset = NewWaferScanPresetName;
        NotificationRequested?.Invoke("Preset Saved", $"Wafer scan preset '{NewWaferScanPresetName}' saved.", null);
    }

    [RelayCommand]
    private async Task DeleteWaferScanPresetAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedWaferScanPreset)) return;

        var config = ConfigurationService.Instance.GetConfig();
        if (config.WaferScanPresets == null) return;

        var preset = config.WaferScanPresets.FirstOrDefault(p => p.Name == SelectedWaferScanPreset);
        if (preset != null)
        {
            var dialog = new Views.SavePromptWindow("Delete Preset", $"Are you sure you want to delete the wafer scan preset '{SelectedWaferScanPreset}'?");
            
            bool result = false;
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                result = await dialog.ShowDialog<bool>(desktop.MainWindow);
            }
            
            if (result)
            {
                config.WaferScanPresets.Remove(preset);
                WaferScanPresetNames.Remove(SelectedWaferScanPreset);
                await ConfigurationService.Instance.SaveAsync(config);
                SelectedWaferScanPreset = string.Empty;
                NewWaferScanPresetName = string.Empty;
                NotificationRequested?.Invoke("Preset Deleted", "Wafer scan preset removed.", null);
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
        foreach (var c in Contacts)
        {
            if (c.IsSelected && int.TryParse(c.Id, out int cId))
            {
                _parsedScanContacts.Add(cId);
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

    public bool IsMeasuringSweep =>
        IsMeasuring &&
        (SelectedPlan is USweepMeasurementPlan ||
         SelectedPlan is PulseSweepMeasurementPlan ||
         SelectedPlan is SpikeTimingMeasurementPlan ||
         SelectedPlan is MemristorSweepMeasurementPlan ||
         SelectedPlan is FrequencyMemoryMeasurementPlan);
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
                        
                        string folderName = "";
                        string folderPath;
                        try
                        {
                            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                            if (IsScanningWafer)
                            {
                                folderName = _currentWaferScanFolderName;
                                folderPath = System.IO.Path.Combine(documentsPath, "SMU_Measurements", profile, "Wafermaps", folderName);
                            }
                            else
                            {
                                folderName = $"{sampleName}_{DateTime.Now:yyyyMMdd}";
                                folderPath = System.IO.Path.Combine(documentsPath, "SMU_Measurements", profile, folderName);
                            }
                        }
                        catch
                        {
                            if (IsScanningWafer)
                            {
                                folderName = _currentWaferScanFolderName;
                                folderPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SMU_Measurements", profile, "Wafermaps", folderName);
                            }
                            else
                            {
                                folderName = $"{sampleName}_{DateTime.Now:yyyyMMdd}";
                                folderPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SMU_Measurements", profile, folderName);
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
                        
                        // Always prepend sep=\t for instant Excel compatibility
                        lines.Add("sep=\t");
                        
                        int insertIndex = 0;
                        if (rawLines.Count > 0 && rawLines[0].StartsWith("sep="))
                        {
                            insertIndex = 1;
                        }
                        
                        lines.Add($"# Plan\t{PlottedPlan.Name}");
                        foreach (var p in PlottedPlan.Parameters)
                        {
                            lines.Add(System.FormattableString.Invariant($"# {p.Name}\t{p.GetValueAsString()}"));
                        }
                        
                        for (int i = insertIndex; i < rawLines.Count; i++)
                        {
                            lines.Add(rawLines[i]);
                        }
                        await System.IO.File.WriteAllLinesAsync(fullPath, lines);
                        
                        MeasurementStatus = $"Finished. Data autosaved to {System.IO.Path.Combine(profile, fileName)}.";

                        if (ConfigurationService.Instance.GetConfig().SaveToDatabase)
                        {
                            try
                            {
                                int dbId = await DatabaseService.Instance.SaveMeasurementAsync(PlottedPlan, Settings.Profile, sampleName, DateTime.Now, folderName, fileName);
                                MeasurementStatus += $" (DB ID: {dbId})";
                            }
                            catch (Exception dbEx)
                            {
                                WarningMessage = $"CSV saved, but database save failed: {dbEx.Message}";
                                System.Diagnostics.Debug.WriteLine($"DB Save Error: {dbEx}");
                            }
                        }

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
            
            // Always prepend sep=\t for instant Excel compatibility
            lines.Add("sep=\t");
            
            if (PlottedPlan != null)
            {
                var rawLines = PlottedPlan.GetCsvLines();
                int insertIndex = 0;
                if (rawLines.Count > 0 && rawLines[0].StartsWith("sep="))
                {
                    insertIndex = 1;
                }
                
                lines.Add($"# Plan\t{PlottedPlan.Name}");
                foreach (var p in PlottedPlan.Parameters)
                {
                    lines.Add(System.FormattableString.Invariant($"# {p.Name}\t{p.GetValueAsString()}"));
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

    public async Task LoadMeasurementFromDatabaseAsync(int measurementId)
    {
        try
        {
            var (parameters, points) = await Services.DatabaseService.Instance.LoadMeasurementDataAsync(measurementId);
            
            // Instantiate a new plan so we don't corrupt the shared instances
            // We use PulseSweepMeasurementPlan as a generic fallback since we don't save the plan name in DB
            Interfaces.IMeasurementPlan plan = new MeasurementPlans.PulseSweepMeasurementPlan();
            plan.ResultPoints.Clear();
            plan.ResultPoints.AddRange(points);
            
            // For any matching parameters from the DB, populate them in our new plan
            foreach (var p in plan.Parameters)
            {
                if (parameters.TryGetValue(p.Name, out string? val) && val != null)
                {
                    p.Value = val;
                }
            }

            PlottedPlan = plan;
            CurvePoints = new System.Collections.ObjectModel.ObservableCollection<Models.CurvePoint>(plan.ResultPoints);
            PlotSeries = new System.Collections.ObjectModel.ObservableCollection<Models.PlotSeries>(plan.PlotSeries);
            
            CustomXAxisTitle = null;
            CustomYAxisTitle = null;
            OnPropertyChanged(nameof(XAxisTitle));
            OnPropertyChanged(nameof(YAxisTitle));
            
            IsMeasurementLogarithmicX = false;
            IsMeasurementLogarithmic = plan.ShowLogPlot;

            MeasurementStatus = $"Finished. Data loaded from database (ID: {measurementId}).";
            
            NotificationRequested?.Invoke(
                "Load Successful",
                $"Successfully loaded measurement {measurementId} from database.",
                null
            );
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load from database: {ex.Message}";
        }
    }

    public async Task UploadCurrentMeasurementToDatabaseAsync()
    {
        if (PlottedPlan == null || PlottedPlan.ResultPoints.Count == 0)
        {
            ErrorMessage = "No measurement data to upload.";
            return;
        }

        try
        {
            // For duplicate checking, use a dummy filename if none exists, or empty
            string dummyFilename = $"{Settings.SampleName}_{PlottedPlan.Name}_{DateTime.Now:yyyyMMdd_HHmmss}";
            bool isUploaded = await Services.DatabaseService.Instance.IsMeasurementUploadedAsync(dummyFilename);
            
            if (isUploaded)
            {
                NotificationRequested?.Invoke("Upload Skipped", "This measurement appears to have already been uploaded.", null);
                return;
            }

            int dbId = await Services.DatabaseService.Instance.SaveMeasurementAsync(PlottedPlan, Settings.Profile, Settings.SampleName, DateTime.Now, dummyFilename);
            
            MeasurementStatus = $"Finished. Data uploaded to database (ID: {dbId}).";
            NotificationRequested?.Invoke("Upload Successful", $"Successfully uploaded to database. ID: {dbId}", null);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to upload to database: {ex.Message}";
        }
    }

    /// <summary>
    /// Imports curve points and metadata from a file to display in the viewer.
    /// </summary>
    public async Task ImportCurvePointsFromFileAsync(string filePath)
    {
        try
        {
            var lines = await Task.Run(() => File.ReadAllLines(filePath));
            string planName = string.Empty;
            var paramDict = new Dictionary<string, string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                if (trimmed.StartsWith("#"))
                {
                    var content = trimmed.Substring(1).Trim();
                    
                    // Robust delimiter detection in metadata comment
                    char[] delimiters = { '\t', ';', ':', ',' };
                    int bestIdx = -1;
                    foreach (var delim in delimiters)
                    {
                        int idx = content.IndexOf(delim);
                        if (idx > 0 && (bestIdx == -1 || idx < bestIdx))
                        {
                            bestIdx = idx;
                        }
                    }
                    if (bestIdx == -1)
                    {
                        bestIdx = content.IndexOf(' ');
                    }

                    if (bestIdx > 0)
                    {
                        var key = content.Substring(0, bestIdx).Trim();
                        var val = content.Substring(bestIdx + 1).Trim();
                        
                        // Clean up leading delimiter/equality characters
                        if (val.StartsWith(":") || val.StartsWith(";") || val.StartsWith(",") || val.StartsWith("="))
                        {
                            val = val.Substring(1).Trim();
                        }

                        if (key.Equals("Plan", StringComparison.OrdinalIgnoreCase))
                        {
                            planName = val;
                        }
                        else
                        {
                            paramDict[key] = val;
                        }
                    }
                }
            }

            // Find separator and header line for heuristics/auto-detection
            char? detectedSeparator = null;
            string? headerLine = null;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("sep=", StringComparison.OrdinalIgnoreCase))
                {
                    var sepStr = trimmed.Substring(4).Trim();
                    if (sepStr.Length > 0)
                    {
                        detectedSeparator = sepStr[0];
                    }
                }
                else if (!trimmed.StartsWith("#") && !string.IsNullOrEmpty(trimmed))
                {
                    if (headerLine == null)
                    {
                        headerLine = trimmed;
                    }
                }
            }

            if (headerLine != null && detectedSeparator == null)
            {
                if (headerLine.Contains('\t'))
                {
                    detectedSeparator = '\t';
                }
                else if (headerLine.Contains(';'))
                {
                    detectedSeparator = ';';
                }
                else if (headerLine.Contains(','))
                {
                    detectedSeparator = ',';
                }
                else
                {
                    detectedSeparator = '\t';
                }
            }

            List<string> headers = new List<string>();
            if (headerLine != null && detectedSeparator.HasValue)
            {
                headers = headerLine.Split(detectedSeparator.Value)
                                    .Select(h => h.Trim().Trim('"'))
                                    .ToList();
            }

            // Heuristics to auto-detect plan name if missing or unrecognized
            if (string.IsNullOrWhiteSpace(planName) && headers.Count > 0)
            {
                if (headers.Any(h => h.Contains("Cycle 1 Voltage") || h.Contains("Cycle 2 Voltage") || h.Contains("Cycle 1 Current")))
                {
                    planName = "Memristor Sweep";
                }
                else if (headers.Any(h => h.Contains("TrialIndex") || h.Contains("Readout1_") || h.Contains("Readout2_")))
                {
                    planName = "Spike Timing";
                }
                else if (headers.Any(h => h.Equals("Cycle", StringComparison.OrdinalIgnoreCase)) && headers.Any(h => h.Contains("Current")))
                {
                    planName = "PotDep";
                }
            }

            IMeasurementPlan? plan = null;
            if (!string.IsNullOrWhiteSpace(planName))
            {
                var matchingPlan = MeasurementPlans.FirstOrDefault(p => string.Equals(p.Name, planName, StringComparison.OrdinalIgnoreCase));
                if (matchingPlan != null)
                {
                    try
                    {
                        plan = Activator.CreateInstance(matchingPlan.GetType()) as IMeasurementPlan;
                    }
                    catch { }
                }
            }

            if (plan == null)
            {
                plan = new ImportedMeasurementPlan
                {
                    Name = string.IsNullOrWhiteSpace(planName) ? Path.GetFileName(filePath) : planName,
                    Description = $"Data loaded from {Path.GetFileName(filePath)}."
                };
            }

            foreach (var param in plan.Parameters)
            {
                if (paramDict.TryGetValue(param.Name, out var valStr))
                {
                    if (param.Type == ParameterType.Number)
                    {
                        if (SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(valStr, out double dVal))
                        {
                            param.Value = dVal;
                        }
                    }
                    else
                    {
                        param.Value = valStr;
                    }
                }
            }

            // Let the plan load the actual data points (standard or custom multi-column layout)
            plan.LoadFromCsvLines(lines);

            int totalPoints = plan.ResultPoints.Count;
            if (plan.PlotSeries != null && plan.PlotSeries.Count > 0)
            {
                totalPoints = plan.PlotSeries.Sum(s => s.Points.Count);
            }

            if (totalPoints == 0)
            {
                NotificationRequested?.Invoke("Import Error", "No data points could be parsed from the file.", null);
                return;
            }

            PlottedPlan = plan;
            RefreshPlotDataFromPlottedPlan();

            NotificationRequested?.Invoke("Success", $"Successfully loaded {totalPoints} points from {Path.GetFileName(filePath)}.", null);
        }
        catch (Exception ex)
        {
            NotificationRequested?.Invoke("Import Error", $"Failed to load file: {ex.Message}", null);
        }
    }

    private async Task SaveMeasurementConfigAsync()
    {
        try
        {
            var config = ConfigurationService.Instance.GetConfig();

            if (config.LastPlanParameters == null)
            {
                config.LastPlanParameters = new();
            }

            // Save top-level app config parameters from active plans
            foreach (var plan in MeasurementPlans)
            {
                if (!config.LastPlanParameters.ContainsKey(plan.Name))
                {
                    config.LastPlanParameters[plan.Name] = new Dictionary<string, string>();
                }

                foreach (var param in plan.Parameters)
                {
                    config.LastPlanParameters[plan.Name][param.Name] = param.GetValueAsString() ?? string.Empty;

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

    private void AddSequenceStep(StepType type)
    {
        if (SelectedPlan is ModularSequenceMeasurementPlan modular)
        {
            SelectedPreset = null; // Clear preset
            var step = new SequenceStep 
            { 
                Type = type,
                WriteChannel = "2",
                ReadingChannel = "2",
                Compliance = 0.1,
                AdcSamples = 1
            };
            if (type == StepType.Pulse)
            {
                step.BaseVoltage = 0.0;
                step.PulseVoltage = 1.0;
                step.PulseWidth = 0.001;
                step.PulsePeriod = 0.01;
            }
            else if (type == StepType.Sweep)
            {
                step.Voltage = 0.0;
                step.StopVoltage = 1.5;
                step.Points = 41;
                step.SweepMode = "Single Staircase (1)";
            }
            else if (type == StepType.Point)
            {
                step.Voltage = 1.0;
            }
            else if (type == StepType.Measure)
            {
                step.KeepCurrentVoltage = true;
                step.Voltage = 0.0;
            }

            modular.Steps.Add(step);
            SelectedSequenceStep = step;
        }
    }

    private void MoveSelectedStepUp()
    {
        if (SelectedPlan is ModularSequenceMeasurementPlan modular && SelectedSequenceStep != null)
        {
            int index = modular.Steps.IndexOf(SelectedSequenceStep);
            if (index > 0)
            {
                SelectedPreset = null; // Clear preset
                var step = SelectedSequenceStep;
                modular.Steps.RemoveAt(index);
                modular.Steps.Insert(index - 1, step);
                SelectedSequenceStep = step;
            }
        }
    }

    private void MoveSelectedStepDown()
    {
        if (SelectedPlan is ModularSequenceMeasurementPlan modular && SelectedSequenceStep != null)
        {
            int index = modular.Steps.IndexOf(SelectedSequenceStep);
            if (index >= 0 && index < modular.Steps.Count - 1)
            {
                SelectedPreset = null; // Clear preset
                var step = SelectedSequenceStep;
                modular.Steps.RemoveAt(index);
                modular.Steps.Insert(index + 1, step);
                SelectedSequenceStep = step;
            }
        }
    }

    private void DeleteSelectedStep()
    {
        if (SelectedPlan is ModularSequenceMeasurementPlan modular && SelectedSequenceStep != null)
        {
            SelectedPreset = null; // Clear preset
            int index = modular.Steps.IndexOf(SelectedSequenceStep);
            modular.Steps.Remove(SelectedSequenceStep);
            if (modular.Steps.Count > 0)
            {
                int newIndex = Math.Clamp(index, 0, modular.Steps.Count - 1);
                SelectedSequenceStep = modular.Steps[newIndex];
            }
            else
            {
                SelectedSequenceStep = null;
            }
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

    private void LoadAvailablePresets()
    {
        var config = ConfigurationService.Instance.GetConfig();
        if (config.Presets != null)
        {
            AvailablePresets = new ObservableCollection<MeasurementPreset>(config.Presets);
        }
        else
        {
            AvailablePresets = new ObservableCollection<MeasurementPreset>();
        }
    }

    private void LoadLastConfig()
    {
        if (SelectedPlan == null) return;
        var config = ConfigurationService.Instance.GetConfig();
        if (config.LastPlanParameters != null && config.LastPlanParameters.TryGetValue(SelectedPlan.Name, out var lastParams))
        {
            foreach (var param in SelectedPlan.Parameters)
            {
                if (lastParams.TryGetValue(param.Name, out var stringVal))
                {
                    try
                    {
                        if (param.Type == ParameterType.Number)
                        {
                            if (ParameterConfigHelper.TryParseDoubleRobust(stringVal, out double d))
                                param.Value = d;
                        }
                        else if (param.Type == ParameterType.Checkbox)
                        {
                            if (bool.TryParse(stringVal, out bool b))
                                param.Value = b;
                        }
                        else
                        {
                            param.Value = stringVal;
                        }
                    }
                    catch { }
                }
            }
        }
    }

    private async void ExecuteSavePreset(string name)
    {
        if (SelectedPlan == null) return;

        var config = ConfigurationService.Instance.GetConfig();
        if (config.Presets == null)
        {
            config.Presets = new List<MeasurementPreset>();
        }

        var existing = config.Presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            config.Presets.Remove(existing);
        }

        var newPreset = new MeasurementPreset 
        { 
            Name = name,
            PlanName = SelectedPlan.Name
        };
        foreach (var param in SelectedPlan.Parameters)
        {
            newPreset.Parameters[param.Name] = param.GetValueAsString() ?? string.Empty;
        }

        config.Presets.Add(newPreset);
        await ConfigurationService.Instance.SaveAsync(config);
        
        LoadAvailablePresets();
        SelectedPreset = config.Presets.FirstOrDefault(p => p.Name == name);
        NewPresetName = string.Empty;
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
            if (!_isLoadingPreset && SelectedPreset != null)
            {
                SelectedPreset = null;
            }
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

        var config = ConfigurationService.Instance.GetConfig();
        if (config.ParameterLinks != null && config.ParameterLinks.TryGetValue(SelectedPlan.Name, out var planLinks))
        {
            foreach (var param in SelectedPlan.Parameters)
            {
                if (planLinks.TryGetValue(param.Name, out var linkConfig) && linkConfig.IsActive)
                {
                    var targetParam = SelectedPlan.Parameters.FirstOrDefault(p => p.Name == linkConfig.LinkedParameterName);
                    if (targetParam != null)
                    {
                        param.LinkedParameter = targetParam;
                        param.LinkedMultiplier = linkConfig.Multiplier;
                        param.IsLinked = true;
                    }
                    else
                    {
                        param.IsLinked = false;
                        param.LinkedParameter = null;
                    }
                }
                else
                {
                    param.IsLinked = false;
                    param.LinkedParameter = null;
                }
            }
        }
        else
        {
            foreach (var param in SelectedPlan.Parameters)
            {
                param.IsLinked = false;
                param.LinkedParameter = null;
            }
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
        
        if (config.WaferScanPresets != null)
        {
            WaferScanPresetNames.Clear();
            foreach (var preset in config.WaferScanPresets)
            {
                WaferScanPresetNames.Add(preset.Name);
            }
        }
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

    private int? _lastSelectedSubCellRow;
    private int? _lastSelectedSubCellCol;
    private int? _lastSelectedContactNumber;

    private ResultCellViewModel? _selectedResultCell;
    public ResultCellViewModel? SelectedResultCell
    {
        get => _selectedResultCell;
        set
        {
            if (_selectedResultCell != null) _selectedResultCell.IsSelected = false;
            if (SetProperty(ref _selectedResultCell, value))
            {
                if (_selectedResultCell != null) 
                {
                    _selectedResultCell.IsSelected = true;
                    
                    ResultSubCellViewModel? nextSubCell = null;
                    if (_lastSelectedSubCellRow.HasValue && _lastSelectedSubCellCol.HasValue)
                    {
                        nextSubCell = _selectedResultCell.SubCells.FirstOrDefault(s => s.Row == _lastSelectedSubCellRow.Value && s.Col == _lastSelectedSubCellCol.Value);
                    }
                    if (nextSubCell == null)
                    {
                        nextSubCell = _selectedResultCell.SubCells.FirstOrDefault();
                    }
                    SelectedResultSubCell = nextSubCell;
                }
                else
                {
                    SelectedResultSubCell = null;
                }
            }
        }
    }

    private ResultSubCellViewModel? _selectedResultSubCell;
    public ResultSubCellViewModel? SelectedResultSubCell
    {
        get => _selectedResultSubCell;
        set
        {
            if (_selectedResultSubCell != null) _selectedResultSubCell.IsSelected = false;
            if (SetProperty(ref _selectedResultSubCell, value))
            {
                if (_selectedResultSubCell != null) 
                {
                    _selectedResultSubCell.IsSelected = true;
                    _lastSelectedSubCellRow = _selectedResultSubCell.Row;
                    _lastSelectedSubCellCol = _selectedResultSubCell.Col;

                    ResultContactViewModel? nextContact = null;
                    if (_lastSelectedContactNumber.HasValue)
                    {
                        nextContact = _selectedResultSubCell.Contacts.FirstOrDefault(c => c.ContactNumber == _lastSelectedContactNumber.Value);
                    }
                    if (nextContact == null)
                    {
                        nextContact = _selectedResultSubCell.Contacts.FirstOrDefault();
                    }
                    SelectedResultContact = nextContact;
                }
                else
                {
                    SelectedResultContact = null;
                }
            }
        }
    }

    private ResultContactViewModel? _selectedResultContact;
    public ResultContactViewModel? SelectedResultContact
    {
        get => _selectedResultContact;
        set
        {
            if (SetProperty(ref _selectedResultContact, value))
            {
                if (_selectedResultContact != null)
                {
                    _lastSelectedContactNumber = _selectedResultContact.ContactNumber;
                }
                UpdateSelectedMemristorCheckResult();
            }
        }
    }

    private MemristorCheckResult? _selectedMemristorCheckResult;
    public MemristorCheckResult? SelectedMemristorCheckResult
    {
        get => _selectedMemristorCheckResult;
        set => SetProperty(ref _selectedMemristorCheckResult, value);
    }

    private void UpdateSelectedMemristorCheckResult()
    {
        if (_selectedResultContact != null && _selectedResultContact.CurveData != null && _selectedResultContact.CurveData.Count > 0)
        {
            SelectedMemristorCheckResult = MemristorCheckService.Calculate(_selectedResultContact.CurveData);
        }
        else
        {
            SelectedMemristorCheckResult = null;
        }
    }

    private List<string> _availableResultMetrics = new() { "Average Resistance", "Max Current", "Max Voltage", "Gap At Voltage", "Memristor Check" };
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
                OnPropertyChanged(nameof(IsGapMetricSelected));
                OnPropertyChanged(nameof(IsMemristorCheckSelected));
            }
        }
    }

    public bool IsGapMetricSelected => SelectedResultMetric == "Gap At Voltage";
    public bool IsMemristorCheckSelected => SelectedResultMetric == "Memristor Check";

    private bool _useAverageForMemristorCheck = false;
    public bool UseAverageForMemristorCheck
    {
        get => _useAverageForMemristorCheck;
        set
        {
            if (SetProperty(ref _useAverageForMemristorCheck, value))
            {
                RecalculateResultMetrics();
            }
        }
    }

    private double _gapTargetVoltage = 0.6;
    public double GapTargetVoltage
    {
        get => _gapTargetVoltage;
        set
        {
            if (SetProperty(ref _gapTargetVoltage, value))
            {
                if (IsGapMetricSelected)
                {
                    RecalculateResultMetrics();
                }
            }
        }
    }

    private List<string> _availableHeatmapColors = new() { "Blue", "Red", "Green", "Purple", "Orange" };
    public List<string> AvailableHeatmapColors
    {
        get => _availableHeatmapColors;
        set => SetProperty(ref _availableHeatmapColors, value);
    }

    private string _selectedHeatmapColorLow = "Blue";
    public string SelectedHeatmapColorLow
    {
        get => _selectedHeatmapColorLow;
        set
        {
            if (SetProperty(ref _selectedHeatmapColorLow, value))
            {
                var config = ConfigurationService.Instance.GetConfig();
                config.VisualizationHeatmapColorLow = value;
                _ = ConfigurationService.Instance.SaveAsync(config);
                RecalculateResultMetrics();
            }
        }
    }

    private string _selectedHeatmapColorHigh = "Red";
    public string SelectedHeatmapColorHigh
    {
        get => _selectedHeatmapColorHigh;
        set
        {
            if (SetProperty(ref _selectedHeatmapColorHigh, value))
            {
                var config = ConfigurationService.Instance.GetConfig();
                config.VisualizationHeatmapColorHigh = value;
                _ = ConfigurationService.Instance.SaveAsync(config);
                RecalculateResultMetrics();
            }
        }
    }

    private double _memristorWeightSnr = 0.20;
    public double MemristorWeightSnr
    {
        get => _memristorWeightSnr;
        set
        {
            if (SetProperty(ref _memristorWeightSnr, value))
            {
                var config = ConfigurationService.Instance.GetConfig();
                config.MemristorWeightSnr = value;
                _ = ConfigurationService.Instance.SaveAsync(config);
                RecalculateResultMetrics();
                UpdateSelectedMemristorCheckResult();
            }
        }
    }

    private double _memristorWeightNonlinearity = 0.15;
    public double MemristorWeightNonlinearity
    {
        get => _memristorWeightNonlinearity;
        set
        {
            if (SetProperty(ref _memristorWeightNonlinearity, value))
            {
                var config = ConfigurationService.Instance.GetConfig();
                config.MemristorWeightNonlinearity = value;
                _ = ConfigurationService.Instance.SaveAsync(config);
                RecalculateResultMetrics();
                UpdateSelectedMemristorCheckResult();
            }
        }
    }

    private double _memristorWeightHysteresis = 0.25;
    public double MemristorWeightHysteresis
    {
        get => _memristorWeightHysteresis;
        set
        {
            if (SetProperty(ref _memristorWeightHysteresis, value))
            {
                var config = ConfigurationService.Instance.GetConfig();
                config.MemristorWeightHysteresis = value;
                _ = ConfigurationService.Instance.SaveAsync(config);
                RecalculateResultMetrics();
                UpdateSelectedMemristorCheckResult();
            }
        }
    }

    private double _memristorWeightBranchSep = 0.15;
    public double MemristorWeightBranchSep
    {
        get => _memristorWeightBranchSep;
        set
        {
            if (SetProperty(ref _memristorWeightBranchSep, value))
            {
                var config = ConfigurationService.Instance.GetConfig();
                config.MemristorWeightBranchSep = value;
                _ = ConfigurationService.Instance.SaveAsync(config);
                RecalculateResultMetrics();
                UpdateSelectedMemristorCheckResult();
            }
        }
    }

    private double _memristorWeightPinch = 0.20;
    public double MemristorWeightPinch
    {
        get => _memristorWeightPinch;
        set
        {
            if (SetProperty(ref _memristorWeightPinch, value))
            {
                var config = ConfigurationService.Instance.GetConfig();
                config.MemristorWeightPinch = value;
                _ = ConfigurationService.Instance.SaveAsync(config);
                RecalculateResultMetrics();
                UpdateSelectedMemristorCheckResult();
            }
        }
    }

    private double _memristorWeightSmoothness = 0.05;
    public double MemristorWeightSmoothness
    {
        get => _memristorWeightSmoothness;
        set
        {
            if (SetProperty(ref _memristorWeightSmoothness, value))
            {
                var config = ConfigurationService.Instance.GetConfig();
                config.MemristorWeightSmoothness = value;
                _ = ConfigurationService.Instance.SaveAsync(config);
                RecalculateResultMetrics();
                UpdateSelectedMemristorCheckResult();
            }
        }
    }

    private double GetHueForColor(string colorName)
    {
        return colorName switch
        {
            "Red" => 0.0,
            "Orange" => 30.0,
            "Green" => 120.0,
            "Purple" => 280.0,
            "Blue" => 220.0,
            _ => 220.0,
        };
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

    private bool _isLoadingResultData;
    public bool IsLoadingResultData
    {
        get => _isLoadingResultData;
        set => SetProperty(ref _isLoadingResultData, value);
    }

    public async Task LoadScanFolderAsync(string folderPath)
    {
        IsLoadingResultData = true;
        await Task.Delay(50); // Yield to UI to show loading overlay

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
                NotificationRequested?.Invoke("Error", "No valid contact files found in folder.", null);
            }
        }
        catch (Exception ex)
        {
            NotificationRequested?.Invoke("Error", $"Failed to load scan folder: {ex.Message}", null);
        }
        finally
        {
            IsLoadingResultData = false;
        }
    }

    public async Task LoadWafermapFromDatabaseAsync(List<Services.DatabaseService.MeasurementSummary> measurements)
    {
        IsLoadingResultData = true;
        await Task.Delay(50); // Yield to UI to show loading overlay

        try
        {
            InitializeResultTab();

            var regex = new Regex(@"Cell(?<cR>\d{2})(?<cC>\d{2})_R(?<sR>\d)C(?<sC>\d)_Contact(?<cont>\d)");

            bool filesFound = false;
            foreach (var meas in measurements)
            {
                if (string.IsNullOrEmpty(meas.SourceFilename)) continue;

                var match = regex.Match(meas.SourceFilename);
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

                // Read points from DB
                var dbData = await Services.DatabaseService.Instance.LoadMeasurementDataAsync(meas.Id);
                contactVm.CurveData = dbData.Points;
            }

            if (filesFound)
            {
                RecalculateResultMetrics();
                IsResultFolderLoaded = true;
                SelectedTabIndex = 3; // Auto switch to Result tab
                NotificationRequested?.Invoke("Success", $"Loaded {measurements.Count} measurements from database.", null);
            }
            else
            {
                NotificationRequested?.Invoke("Error", "No valid contact measurements found in selected node.", null);
            }
        }
        catch (Exception ex)
        {
            NotificationRequested?.Invoke("Error", $"Failed to load wafermap from DB: {ex.Message}", null);
        }
        finally
        {
            IsLoadingResultData = false;
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
            var parts = trimmed.Contains('\t') ? trimmed.Split('\t') : trimmed.Split(',');
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
        bool useMaxAggregation = SelectedResultMetric == "Memristor Check" && !UseAverageForMemristorCheck;

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
                subCell.RecalculateValue(useMaxAggregation);
            }
            // 3. Aggregate to Cell
            cell.RecalculateValue(useMaxAggregation);
        }

        // 4. Find global min / max on Cell level
        var validCells = ResultCells.Where(c => c.SubCells.Any() && !double.IsNaN(c.AggregatedValue)).ToList();
        if (!validCells.Any()) return;

        double minVal = validCells.Min(c => c.AggregatedValue);
        double maxVal = validCells.Max(c => c.AggregatedValue);

        double hueLow = GetHueForColor(SelectedHeatmapColorLow);
        double hueHigh = GetHueForColor(SelectedHeatmapColorHigh);

        // Apply colors to Cells
        foreach (var cell in ResultCells)
        {
            if (!cell.SubCells.Any())
            {
                cell.Color = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F8FAFC"));
            }
            else
            {
                cell.Color = HeatmapHelper.GetColorForValue(cell.AggregatedValue, minVal, maxVal, hueLow, hueHigh);
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
                    sub.Color = HeatmapHelper.GetColorForValue(sub.AggregatedValue, minSub, maxSub, hueLow, hueHigh);
                    sub.MetricLabel = GetMetricLabel(SelectedResultMetric, sub.AggregatedValue);
                }
            }
        }

        // Apply colors and labels to Contacts
        var allContacts = ResultCells.SelectMany(c => c.SubCells).SelectMany(s => s.Contacts).Where(co => !double.IsNaN(co.AggregatedValue)).ToList();
        if (allContacts.Any())
        {
            double minContact = allContacts.Min(co => co.AggregatedValue);
            double maxContact = allContacts.Max(co => co.AggregatedValue);
            foreach (var cell in ResultCells)
            {
                foreach (var sub in cell.SubCells)
                {
                    foreach (var contact in sub.Contacts)
                    {
                        contact.Color = HeatmapHelper.GetColorForValue(contact.AggregatedValue, minContact, maxContact, hueLow, hueHigh);
                        contact.MetricLabel = GetMetricLabel(SelectedResultMetric, contact.AggregatedValue);
                    }
                }
            }
        }
        
        foreach (var cell in ResultCells)
        {
            cell.MetricLabel = GetMetricLabel(SelectedResultMetric, cell.AggregatedValue);
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
        else if (metric == "Gap At Voltage")
        {
            // Calculate absolute difference in Current at GapTargetVoltage for ascending and descending sweeps
            var targetV = GapTargetVoltage;
            
            // Collect crossings (interpolate I at V = targetV)
            List<double> crossedCurrents = new List<double>();
            
            for (int i = 0; i < points.Count - 1; i++)
            {
                var p1 = points[i];
                var p2 = points[i + 1];
                
                // Check if targetV is between p1.Voltage and p2.Voltage
                if ((p1.Voltage <= targetV && p2.Voltage >= targetV) ||
                    (p1.Voltage >= targetV && p2.Voltage <= targetV))
                {
                    // Avoid division by zero
                    if (Math.Abs(p2.Voltage - p1.Voltage) < 1e-12)
                    {
                        crossedCurrents.Add(p1.Current);
                    }
                    else
                    {
                        // Interpolate current
                        double fraction = (targetV - p1.Voltage) / (p2.Voltage - p1.Voltage);
                        double iInterp = p1.Current + fraction * (p2.Current - p1.Current);
                        crossedCurrents.Add(iInterp);
                    }
                }
            }
            
            if (crossedCurrents.Count >= 2)
            {
                // Typically we want the max difference if there are multiple crossings (e.g. multi-cycle)
                // Or just the difference between the first two crossings. Let's return the max difference among all crossings.
                double maxAbsI = crossedCurrents.Max(c => Math.Abs(c));
                double minAbsI = crossedCurrents.Min(c => Math.Abs(c));
                if (minAbsI < 1e-15) return double.NaN; // Avoid division by zero
                return maxAbsI / minAbsI;
            }
            return double.NaN;
        }
        else if (metric == "Memristor Check")
        {
            var result = MemristorCheckService.Calculate(points);
            return result.Score3;
        }

        return double.NaN;
    }

    // --- Memristor Check Helper Methods ---

    private string GetMetricLabel(string metric, double value)
    {
        if (double.IsNaN(value)) return string.Empty;
        if (metric == "Memristor Check")
        {
            if (value < 0.75) return "Poor / probably noise";
            if (value < 1.35) return "Signal, but weak";
            if (value < 1.95) return "Possible candidate";
            if (value <= 2.40) return "Good candidate";
            return "Very good candidate";
        }
        return string.Empty;
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

public class ImportedMeasurementPlan : IMeasurementPlan
{
    public string Name { get; set; } = "Imported Data";
    public string Description { get; set; } = "Imported measurement data from file.";
    public List<MeasurementParameter> Parameters { get; } = new();
    public List<CurvePoint> ResultPoints { get; } = new();

    public Task RunMeasurementAsync(E5263_SMU smu, IProgress<double>? progress = null)
    {
        return Task.CompletedTask;
    }

    public void LoadDefaults() { }
}
