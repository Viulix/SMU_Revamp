using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SMU_Revamp.Models;

namespace SMU_Revamp.Services
{
    public interface IMeasurementPlan
    {
        string Name { get; }
        string Description { get; }
        List<MeasurementParameter> Parameters { get; }
        List<CurvePoint> ResultPoints { get; }
        Task RunMeasurementAsync(E5263_SMU smu, IProgress<double>? progress = null);
        void LoadDefaults();
    }
}
