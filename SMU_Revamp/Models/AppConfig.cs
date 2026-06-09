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
}
