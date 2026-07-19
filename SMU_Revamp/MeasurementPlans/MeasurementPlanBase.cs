using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SMU_Revamp.Models;
using SMU_Revamp.Services;
using SMU_Revamp.Interfaces;

namespace SMU_Revamp.MeasurementPlans
{
    /// <summary>
    /// Base class for Measurement Plans that reduces boilerplate by handling
    /// parameter loading, parameter accessors, and common properties.
    /// </summary>
    public abstract class MeasurementPlanBase : IMeasurementPlan
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        
        public List<MeasurementParameter> Parameters { get; protected set; } = new();
        public List<CurvePoint> ResultPoints { get; } = new();

        public virtual string PlotTitle => Name;
        public virtual string XAxisLabel => "Voltage (V)";
        public virtual string YAxisLabel => "Current (A)";
        public virtual bool ShowLogPlot => true;
        public virtual double PlotAspectRatio => 1.333;
        public virtual PlotStyle DefaultPlotStyle => PlotStyle.Line;

        public virtual IReadOnlyList<PlotSeries> PlotSeries => new List<PlotSeries>
        {
            new PlotSeries(Name, ResultPoints)
        };

        /// <summary>
        /// Defines the default value for each parameter by its Name.
        /// This dictionary is used by LoadDefaults() to automatically parse and load
        /// values from the persistent ConfigurationService.
        /// </summary>
        protected abstract Dictionary<string, object> GetParameterDefaults();

        public virtual void LoadDefaults()
        {
            var defaults = GetParameterDefaults();
            var config = ConfigurationService.Instance.GetConfig();
            
            foreach (var param in Parameters)
            {
                if (defaults.TryGetValue(param.Name, out var defaultValue))
                {
                    // Handle legacy fallbacks automatically mapped to global app config
                    if (param.Name == "WriteChannel" || param.Name == "ReadingChannel" || param.Name == "Channel")
                    {
                        var sweepChan = string.IsNullOrWhiteSpace(config.SweepChannel) ? defaultValue : config.SweepChannel;
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, param.Name, sweepChan);
                    }
                    else if (param.Name == "Compliance")
                    {
                        var comp = config.SweepCompliance != 0 ? config.SweepCompliance : defaultValue;
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, param.Name, comp);
                    }
                    else
                    {
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, param.Name, defaultValue);
                    }
                }
            }
        }

        public abstract Task RunMeasurementAsync(E5263_SMU smu, IProgress<double>? progress = null);

        public string GetParamValueString(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsString() ?? string.Empty;
        public double GetParamValueDouble(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsDouble() ?? 0.0;
        public int GetParamValueInt(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsInt() ?? 0;
        public bool GetParamValueBool(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsBool() ?? false;
    }
}
