namespace SMU_Revamp.Models
{
    /// <summary>
    /// Represents a physical contact target (row/column or identifier).
    /// </summary>
    public class ContactTarget
    {
        public string Row { get; set; } = string.Empty;
        public string Column { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }
}
