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
    public string Profil { get; set; } = "";

    /// <summary>
    /// File storing sample name (Probename).
    /// </summary>
    public string Probename { get; set; } = "";

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
}
