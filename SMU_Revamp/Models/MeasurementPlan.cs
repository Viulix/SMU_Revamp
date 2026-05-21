using System;

namespace SMU_Revamp.Models
{
    /// <summary>
    /// Minimal measurement plan model used as a starting point.
    /// Extend with strategy-specific fields as needed.
    /// </summary>
    public class MeasurementPlan
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string MeasurementType { get; set; } = string.Empty;
        public double Start { get; set; }
        public double Stop { get; set; }
        public int Points { get; set; }
        public double Compliance { get; set; }
        public string? Channel { get; set; }
    }
}
