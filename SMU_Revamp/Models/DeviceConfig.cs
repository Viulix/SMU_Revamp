namespace SMU_Revamp.Models
{
    /// <summary>
    /// Device configuration details (GPIB addresses, timeouts, types).
    /// </summary>
    public class DeviceConfig
    {
        public string ResourceString { get; set; } = string.Empty;
        public int TimeoutMs { get; set; } = 5000;
        public string? DeviceType { get; set; }
    }
}
