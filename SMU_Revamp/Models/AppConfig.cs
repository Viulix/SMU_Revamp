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
    /// Default values for plan parameters, keyed by Plan Name -> Parameter Name -> Parameter Value.
    /// </summary>
    public System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> DefaultPlanParameters { get; set; } = new();

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
