using System.Collections.Generic;

namespace SMU_Revamp.Models
{
    public class ParameterSection
    {
        public string Name { get; set; } = string.Empty;
        public List<MeasurementParameter> Parameters { get; set; } = new();
        public bool HasName => !string.IsNullOrEmpty(Name);
    }
}
