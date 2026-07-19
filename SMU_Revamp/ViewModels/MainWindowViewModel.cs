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

    private bool _stayHere = true;
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
        ResetMemristorWeightsCommand = new RelayCommand(() =>
        {
            MemristorWeightSnr = 0.20;
            MemristorWeightNonlinearity = 0.15;
            MemristorWeightHysteresis = 0.25;
            MemristorWeightBranchSep = 0.15;
            MemristorWeightPinch = 0.20;
            MemristorWeightSmoothness = 0.05;
        });

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







    
    public ICommand GoToScanStartCommand { get; }


    public ICommand RequestStopScanCommand { get; }
    public ICommand ConfirmStopScanCommand { get; }
    public ICommand CancelStopRequestCommand { get; }











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


    /// <summary>
    /// Exports the current curve points to a CSV file.
    /// </summary>



    /// <summary>
    /// Imports curve points and metadata from a file to display in the viewer.
    /// </summary>







    /// <summary>
    /// Reloads measurement plans to reflect new parameter defaults.
    /// </summary>











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
            if (string.IsNullOrEmpty(value)) return;
            if (SetProperty(ref _selectedResultMetric, value))
            {
                var config = ConfigurationService.Instance.GetConfig();
                config.SelectedResultMetric = value;
                _ = ConfigurationService.Instance.SaveAsync(config);

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
                var config = ConfigurationService.Instance.GetConfig();
                config.UseAverageForMemristorCheck = value;
                _ = ConfigurationService.Instance.SaveAsync(config);

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
                var config = ConfigurationService.Instance.GetConfig();
                config.GapTargetVoltage = value;
                _ = ConfigurationService.Instance.SaveAsync(config);

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
            if (string.IsNullOrEmpty(value)) return;
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
            if (string.IsNullOrEmpty(value)) return;
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


    public ICommand LoadScanFolderCommand { get; }
    public ICommand SetSelectedResultCellCommand { get; }
    public ICommand SetSelectedResultSubCellCommand { get; }
    public ICommand ResetMemristorWeightsCommand { get; }


    private bool _isLoadingResultData;
    public bool IsLoadingResultData
    {
        get => _isLoadingResultData;
        set => SetProperty(ref _isLoadingResultData, value);
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


    public Task RunMeasurementAsync(E5263_SMU smu, IProgress<double>? progress = null) => Task.CompletedTask; public void LoadDefaults() { }
}
