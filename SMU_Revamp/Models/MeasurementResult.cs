using System;

namespace SMU_Revamp.Models
{
    /// <summary>
    /// Basic measurement result container.
    /// </summary>
    public class MeasurementResult
    {
        public string PlanId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public double[] X { get; set; } = [];
        public double[] Y { get; set; } = [];
        public string? Notes { get; set; }
    }
}
