namespace SMU_Revamp.Models;

/// <summary>
/// Application configuration data model.
/// </summary>
public class AppConfig
{
    /// <summary>
    /// Prober quiet mode setting.
    /// </summary>
    public bool ProberQuietMode { get; set; } = false;

    /// <summary>
    /// Whether to show the alignment warning before starting a scan.
    /// </summary>
    public bool ShowAlignmentWarning { get; set; } = true;

    /// <summary>
    /// Prober GPIB resource string.
    /// </summary>
    public string ProberResource { get; set; } = "GPIB0::22::INSTR";

    /// <summary>
    /// Prober timeout in milliseconds.
    /// </summary>
    public int ProberTimeoutMs { get; set; } = 20000;

    /// <summary>
    /// Switch matrix GPIB resource string.
    /// </summary>
    public string SwitchMatrixResource { get; set; } = "GPIB0::23::INSTR";

    /// <summary>
    /// Switch matrix timeout in milliseconds.
    /// </summary>
    public int SwitchMatrixTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// File storing profile.
    /// </summary>
    public string Profile { get; set; } = "";

    /// <summary>
    /// File storing sample name (SampleName).
    /// </summary>
    public string SampleName { get; set; } = "";

    /// <summary>
    /// E5263 SMU GPIB resource string.
    /// </summary>
    public string SMUResource { get; set; } = "GPIB0::17::INSTR";

    /// <summary>
    /// E5263 SMU timeout in milliseconds.
    /// </summary>
    public int SMUTimeoutMs { get; set; } = 300000;

    /// <summary>
    /// Sweep channel configuration.
    /// </summary>
    public string SweepChannel { get; set; } = "2";

    /// <summary>
    /// Sweep start voltage (V).
    /// </summary>
    public double SweepStart { get; set; } = 0.0;

    /// <summary>
    /// Sweep stop voltage (V).
    /// </summary>
    public double SweepStop { get; set; } = 1.5;

    /// <summary>
    /// Number of sweep points.
    /// </summary>
    public int SweepPoints { get; set; } = 41;

    /// <summary>
    /// Sweep compliance current (A).
    /// </summary>
    public double SweepCompliance { get; set; } = 0.1;

    /// <summary>
    /// Sweep ADC Samples (PLC).
    /// </summary>
    public int SweepAdcSamples { get; set; } = 1;

    /// <summary>
    /// Sweep Mode selection.
    /// </summary>
    public string SelectedSweepMode { get; set; } = "Double Staircase (3)";

    /// <summary>
    /// Last used values for plan parameters, keyed by Plan Name -> Parameter Name -> Parameter Value.
    /// </summary>
    public System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> LastPlanParameters { get; set; } = new();

    /// <summary>
    /// Presets for measurement plans, keyed by Plan Name -> List of Presets.
    /// </summary>
    public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<MeasurementPreset>> PlanPresets { get; set; } = new();

    /// <summary>
    /// Global list of presets.
    /// </summary>
    public System.Collections.Generic.List<MeasurementPreset> Presets { get; set; } = new();

    /// <summary>
    /// Flag to automatically save measurement results to a Profile-named folder.
    /// </summary>
    public bool AutoSaveMeasurements { get; set; } = true;

    /// <summary>
    /// Flag to save measurement results to the MySQL database.
    /// </summary>
    public bool SaveToDatabase { get; set; } = false;

    /// <summary>
    /// MySQL Database IP/Address.
    /// </summary>
    public string DbAddress { get; set; } = "134.245.242.39";

    /// <summary>
    /// MySQL Database Username.
    /// </summary>
    public string DbUser { get; set; } = "root";

    /// <summary>
    /// MySQL Database Password.
    /// </summary>
    public string DbPassword { get; set; } = "";

    /// <summary>
    /// MySQL Database Name.
    /// </summary>
    public string DbName { get; set; } = "smu_measurements";

    /// <summary>
    /// Dynamic parameter links, keyed by Plan Name -> Parameter Name -> Link Config.
    /// </summary>
    public System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, ParameterLinkConfig>> ParameterLinks { get; set; } = new();

    /// <summary>
    /// Visualization Heatmap Color Low.
    /// </summary>
    public string VisualizationHeatmapColorLow { get; set; } = "Blue";

    /// <summary>
    /// Visualization Heatmap Color High.
    /// </summary>
    public string VisualizationHeatmapColorHigh { get; set; } = "Red";

    /// <summary>
    /// Presets for wafer scans.
    /// </summary>
    public System.Collections.Generic.List<WaferScanPreset> WaferScanPresets { get; set; } = new();

    // Memristor Check weights
    public double MemristorWeightSnr { get; set; } = 0.20;
    public double MemristorWeightNonlinearity { get; set; } = 0.15;
    public double MemristorWeightHysteresis { get; set; } = 0.25;
    public double MemristorWeightBranchSep { get; set; } = 0.15;
    public double MemristorWeightPinch { get; set; } = 0.20;
    public double MemristorWeightSmoothness { get; set; } = 0.05;

    // Visualization Result settings
    public string SelectedResultMetric { get; set; } = "Average Resistance";
    public double GapTargetVoltage { get; set; } = 1.0;
    public bool UseAverageForMemristorCheck { get; set; } = false;
}

/// <summary>
/// Preset containing saved parameters for a measurement plan.
/// </summary>
public class MeasurementPreset
{
    public string Name { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public System.Collections.Generic.Dictionary<string, string> Parameters { get; set; } = new();
}

/// <summary>
/// Configuration for a dynamic parameter link.
/// </summary>
public class ParameterLinkConfig
{
    public string LinkedParameterName { get; set; } = string.Empty;
    public double Multiplier { get; set; } = 1.0;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Preset containing saved configuration for wafer scans.
/// </summary>
public class WaferScanPreset
{
    public string Name { get; set; } = string.Empty;
    public string DelayMs { get; set; } = "500";
    public System.Collections.Generic.List<string> SelectedSubCells { get; set; } = new();
    public System.Collections.Generic.List<int> SelectedContacts { get; set; } = new();
    public System.Collections.Generic.List<string> SelectedWaferCells { get; set; } = new();
}
