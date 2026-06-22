using System.Collections.Generic;

namespace SMU_Revamp.Models;

/// <summary>
/// One named curve in the measurement viewer.
/// For classic I/V measurements there is usually one series.
/// For time-constant spike timing, one series represents one gap-order pattern.
/// </summary>
public sealed class PlotSeries
{
    public string Name { get; }
    public IReadOnlyList<CurvePoint> Points { get; }

    public PlotSeries(string name, IReadOnlyList<CurvePoint> points)
    {
        Name = name;
        Points = points;
    }
}
