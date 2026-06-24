using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SMU_Revamp.Models;
using SMU_Revamp.Services;

namespace SMU_Revamp.Interfaces
{
    public interface IMeasurementPlan
    {
        string Name { get; }
        string Description { get; }
        List<MeasurementParameter> Parameters { get; }
        List<CurvePoint> ResultPoints { get; }

        /// <summary>
        /// Title shown above the measurement viewer plot.
        /// </summary>
        string PlotTitle => Name;

        /// <summary>
        /// X-axis label shown by the viewer and used by the default CSV export.
        /// </summary>
        string XAxisLabel => "Voltage (V)";

        /// <summary>
        /// Y-axis label shown by the viewer and used by the default CSV export.
        /// </summary>
        string YAxisLabel => "Current (A)";

        /// <summary>
        /// Whether the logarithmic-Y plot should be visible for this plan.
        /// </summary>
        bool ShowLogPlot => true;

        /// <summary>
        /// Named curves shown in the viewer. Classic measurements use one default series.
        /// Advanced measurements can override this and return several series.
        /// </summary>
        IReadOnlyList<PlotSeries> PlotSeries => new List<PlotSeries>
        {
            new PlotSeries(Name, ResultPoints)
        };

        /// <summary>
        /// CSV export hook. By default, export the plotted x/y points.
        /// Plans with richer metadata should override this.
        /// </summary>
        IReadOnlyList<string> GetCsvLines()
        {
            var lines = new List<string>
            {
                $"{CsvEscape(XAxisLabel)}\t{CsvEscape(YAxisLabel)}"
            };

            foreach (var point in ResultPoints)
            {
                lines.Add(FormattableString.Invariant($"{point.X:E6}\t{point.Y:E6}"));
            }

            return lines;
        }

        Task RunMeasurementAsync(E5263_SMU smu, IProgress<double>? progress = null);
        void LoadDefaults();

        private static string CsvEscape(string value)
        {
            if (value.Contains('\t') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }
    }
}
