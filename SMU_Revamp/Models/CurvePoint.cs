namespace SMU_Revamp.Models;

/// <summary>
/// Generic two-dimensional measurement point.
/// X and Y are used by the plot/export infrastructure.
/// Voltage and Current are kept as compatibility aliases for older I/V-specific code.
/// YError is optional and is used by the viewer to draw vertical error bars.
/// </summary>
public sealed record CurvePoint(double X, double Y, double? YError = null)
{
    public double Voltage => X;
    public double Current => Y;
}
