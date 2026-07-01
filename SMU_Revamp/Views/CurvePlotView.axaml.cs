using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using SMU_Revamp.Models;

namespace SMU_Revamp.Views;

public partial class CurvePlotView : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<CurvePlotView, string?>(nameof(Title));

    public static readonly StyledProperty<string?> XAxisLabelProperty =
        AvaloniaProperty.Register<CurvePlotView, string?>(nameof(XAxisLabel));

    public static readonly StyledProperty<string?> YAxisLabelProperty =
        AvaloniaProperty.Register<CurvePlotView, string?>(nameof(YAxisLabel));

    /// <summary>
    /// Legacy single-series input. Kept so old XAML/views continue to work.
    /// Prefer Series for new multi-curve measurements.
    /// </summary>
    public static readonly StyledProperty<IEnumerable<CurvePoint>?> PointsProperty =
        AvaloniaProperty.Register<CurvePlotView, IEnumerable<CurvePoint>?>(nameof(Points));

    /// <summary>
    /// Multi-series input. If this contains valid series, it takes precedence over Points.
    /// </summary>
    public static readonly StyledProperty<IEnumerable<PlotSeries>?> SeriesProperty =
        AvaloniaProperty.Register<CurvePlotView, IEnumerable<PlotSeries>?>(nameof(Series));

    public static readonly StyledProperty<bool> LogarithmicYProperty =
        AvaloniaProperty.Register<CurvePlotView, bool>(nameof(LogarithmicY));

    // Backward-compatible property used by MainWindow.axaml.
    // For non-positive x values the drawing code clamps to a small positive value.
    public static readonly StyledProperty<bool> LogarithmicXProperty =
        AvaloniaProperty.Register<CurvePlotView, bool>(nameof(LogarithmicX));

    // Backward-compatible property used by MainWindow.axaml.
    // The custom canvas renderer currently supports the most important modes directly.
    public static readonly StyledProperty<SMU_Revamp.Models.PlotStyle> PlotStyleProperty =
        AvaloniaProperty.Register<CurvePlotView, SMU_Revamp.Models.PlotStyle>(
            nameof(PlotStyle),
            SMU_Revamp.Models.PlotStyle.LineAndScatter);

    static CurvePlotView()
    {
        TitleProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.UpdateLabels());
        XAxisLabelProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.UpdateLabels());
        YAxisLabelProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.UpdateLabels());
        PointsProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        SeriesProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        LogarithmicYProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        LogarithmicXProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        PlotStyleProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? XAxisLabel
    {
        get => GetValue(XAxisLabelProperty);
        set => SetValue(XAxisLabelProperty, value);
    }

    public string? YAxisLabel
    {
        get => GetValue(YAxisLabelProperty);
        set => SetValue(YAxisLabelProperty, value);
    }

    public IEnumerable<CurvePoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public IEnumerable<PlotSeries>? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public bool LogarithmicY
    {
        get => GetValue(LogarithmicYProperty);
        set => SetValue(LogarithmicYProperty, value);
    }

    public bool LogarithmicX
    {
        get => GetValue(LogarithmicXProperty);
        set => SetValue(LogarithmicXProperty, value);
    }

    public SMU_Revamp.Models.PlotStyle PlotStyle
    {
        get => GetValue(PlotStyleProperty);
        set => SetValue(PlotStyleProperty, value);
    }

    public CurvePlotView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) =>
        {
            UpdateLabels();
            Redraw();
        };

        SizeChanged += (_, _) => Redraw();
    }

    private void UpdateLabels()
    {
        if (TitleText is not null)
        {
            TitleText.Text = Title ?? string.Empty;
        }

        if (XAxisText is not null)
        {
            XAxisText.Text = XAxisLabel ?? string.Empty;
        }

        if (YAxisText is not null)
        {
            YAxisText.Text = YAxisLabel ?? string.Empty;
        }
    }

    private void Redraw()
    {
        if (PlotCanvas is null)
        {
            return;
        }

        PlotCanvas.Children.Clear();

        var series = GetEffectiveSeries();
        var allPoints = series.SelectMany(s => s.Points).ToList();

        if (allPoints.Count < 1 || PlotCanvas.Bounds.Width <= 0 || PlotCanvas.Bounds.Height <= 0)
        {
            DrawEmptyState();
            return;
        }

        var marginLeft = 58.0;
        var marginRight = series.Count > 1 ? 120.0 : 18.0;
        var marginTop = 16.0;
        var marginBottom = 28.0;

        var width = Math.Max(1, PlotCanvas.Bounds.Width - marginLeft - marginRight);
        var height = Math.Max(1, PlotCanvas.Bounds.Height - marginTop - marginBottom);

        var xValues = allPoints
            .Select(p => TransformX(p.X))
            .ToList();

        var xMin = xValues.Min();
        var xMax = xValues.Max();
        if (Math.Abs(xMax - xMin) < 1e-12)
        {
            xMax = xMin + 1;
        }

        var yBounds = allPoints.SelectMany(GetPointYBounds).ToList();
        var yValues = yBounds
            .Select(y => LogarithmicY ? Math.Max(Math.Abs(y), 1e-12) : y)
            .Select(y => LogarithmicY ? Math.Log10(y) : y)
            .ToList();

        var yMin = yValues.Min();
        var yMax = yValues.Max();
        if (Math.Abs(yMax - yMin) < 1e-12)
        {
            yMax = yMin + 1;
        }

        DrawBackground(width, height, marginLeft, marginTop, marginBottom, marginRight, xMin, xMax, yMin, yMax);

        for (int i = 0; i < series.Count; i++)
        {
            DrawCurve(series[i].Points, width, height, marginLeft, marginTop, xMin, xMax, yMin, yMax, GetSeriesBrush(i));
        }

        if (series.Count > 1)
        {
            DrawLegend(series, marginLeft + width + 16, marginTop);
        }
    }

    private static IEnumerable<double> GetPointYBounds(CurvePoint point)
    {
        yield return point.Y;

        if (point.YError is double error && error > 0 && !double.IsNaN(error) && !double.IsInfinity(error))
        {
            yield return point.Y - error;
            yield return point.Y + error;
        }
    }

    private List<PlotSeries> GetEffectiveSeries()
    {
        var suppliedSeries = Series?
            .Where(s => s.Points.Count > 0)
            .ToList() ?? new List<PlotSeries>();

        if (suppliedSeries.Count > 0)
        {
            return suppliedSeries;
        }

        var points = Points?.ToList() ?? new List<CurvePoint>();
        return points.Count > 0
            ? new List<PlotSeries> { new PlotSeries(Title ?? "Data", points) }
            : new List<PlotSeries>();
    }

    private void DrawEmptyState()
    {
        var text = new TextBlock
        {
            Text = "No curve data",
            Foreground = new SolidColorBrush(Color.Parse("#606060"))
        };

        Canvas.SetLeft(text, 16);
        Canvas.SetTop(text, 16);
        PlotCanvas.Children.Add(text);
    }

    private void DrawBackground(double width, double height, double marginLeft, double marginTop, double marginBottom, double marginRight, double xMin, double xMax, double yMin, double yMax)
    {
        var xAxisY = marginTop + height;
        var yAxisX = marginLeft;

        PlotCanvas.Children.Add(new Line
        {
            StartPoint = new Point(yAxisX, marginTop),
            EndPoint = new Point(yAxisX, xAxisY),
            Stroke = new SolidColorBrush(Color.Parse("#202020")),
            StrokeThickness = 1
        });

        PlotCanvas.Children.Add(new Line
        {
            StartPoint = new Point(yAxisX, xAxisY),
            EndPoint = new Point(yAxisX + width, xAxisY),
            Stroke = new SolidColorBrush(Color.Parse("#202020")),
            StrokeThickness = 1
        });

        var gridBrush = new SolidColorBrush(Color.Parse("#D0D0D0"));
        var tickBrush = new SolidColorBrush(Color.Parse("#404040"));

        for (var i = 0; i <= 4; i++)
        {
            var ratio = i / 4.0;
            var x = yAxisX + ratio * width;
            var y = marginTop + ratio * height;

            PlotCanvas.Children.Add(new Line
            {
                StartPoint = new Point(x, marginTop),
                EndPoint = new Point(x, xAxisY),
                Stroke = gridBrush,
                StrokeThickness = 0.75
            });

            PlotCanvas.Children.Add(new Line
            {
                StartPoint = new Point(yAxisX, y),
                EndPoint = new Point(yAxisX + width, y),
                Stroke = gridBrush,
                StrokeThickness = 0.75
            });

            var xValue = InverseTransformX(xMin + ratio * (xMax - xMin));
            var yValue = LogarithmicY ? Math.Pow(10, yMax - ratio * (yMax - yMin)) : yMax - ratio * (yMax - yMin);
            var xLabel = new TextBlock
            {
                Text = FormatAxisValue(xValue),
                FontSize = 11,
                Foreground = tickBrush
            };
            Canvas.SetLeft(xLabel, x - 12);
            Canvas.SetTop(xLabel, xAxisY + 4);
            PlotCanvas.Children.Add(xLabel);

            var yLabelText = LogarithmicY
                ? $"10^{Math.Round(Math.Log10(yValue))}"
                : yValue.ToString("0.###E0", CultureInfo.InvariantCulture);
            var yLabel = new TextBlock
            {
                Text = yLabelText,
                FontSize = 11,
                Foreground = tickBrush
            };
            Canvas.SetLeft(yLabel, 4);
            Canvas.SetTop(yLabel, y - 8);
            PlotCanvas.Children.Add(yLabel);
        }
    }

    private void DrawCurve(IReadOnlyList<CurvePoint> points, double width, double height, double marginLeft, double marginTop, double xMin, double xMax, double yMin, double yMax, IBrush brush)
    {
        // IMPORTANT: preserve acquisition order for hysteretic sweeps.
        // Do not sort by X here: I/V sweeps and memristor hysteresis curves can visit
        // the same voltage multiple times in different device states. The measurement plan
        // is responsible for supplying already-sorted points if sorted plotting is desired
        // (for example, FrequencyMemoryMeasurementPlan supplies mean points ordered by interval).
        var orderedPoints = points.ToList();

        if (ShouldDrawLine() && orderedPoints.Count >= 2)
        {
            var polyline = new Polyline
            {
                Stroke = brush,
                StrokeThickness = 2
            };

            foreach (var point in orderedPoints)
            {
                polyline.Points.Add(ToCanvasPoint(point.X, point.Y, width, height, marginLeft, marginTop, xMin, xMax, yMin, yMax));
            }

            PlotCanvas.Children.Add(polyline);
        }

        foreach (var point in orderedPoints)
        {
            DrawErrorBar(point, width, height, marginLeft, marginTop, xMin, xMax, yMin, yMax, brush);

            if (ShouldDrawMarker())
            {
                DrawMarker(point, width, height, marginLeft, marginTop, xMin, xMax, yMin, yMax, brush);
            }
        }
    }

    private Point ToCanvasPoint(double xValue, double yValue, double width, double height, double marginLeft, double marginTop, double xMin, double xMax, double yMin, double yMax)
    {
        var rawX = TransformX(xValue);
        var x = marginLeft + ((rawX - xMin) / (xMax - xMin)) * width;
        var rawY = LogarithmicY ? Math.Log10(Math.Max(Math.Abs(yValue), 1e-12)) : yValue;
        var y = marginTop + height - ((rawY - yMin) / (yMax - yMin)) * height;
        return new Point(x, y);
    }

    private double TransformX(double value)
    {
        return LogarithmicX ? Math.Log10(Math.Max(value, 1e-12)) : value;
    }

    private double InverseTransformX(double value)
    {
        return LogarithmicX ? Math.Pow(10, value) : value;
    }

    private static string FormatAxisValue(double value)
    {
        var abs = Math.Abs(value);
        return abs >= 10000 || (abs > 0 && abs < 0.001)
            ? value.ToString("0.###E0", CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private bool ShouldDrawLine()
    {
        return PlotStyle != SMU_Revamp.Models.PlotStyle.Scatter;
    }

    private bool ShouldDrawMarker()
    {
        return PlotStyle == SMU_Revamp.Models.PlotStyle.Scatter ||
               PlotStyle == SMU_Revamp.Models.PlotStyle.LineAndScatter ||
               PlotStyle == SMU_Revamp.Models.PlotStyle.InterpolatedLineAndScatter;
    }

    private void DrawErrorBar(CurvePoint point, double width, double height, double marginLeft, double marginTop, double xMin, double xMax, double yMin, double yMax, IBrush brush)
    {
        if (LogarithmicY)
        {
            return;
        }

        if (point.YError is not double error || error <= 0 || double.IsNaN(error) || double.IsInfinity(error))
        {
            return;
        }

        var center = ToCanvasPoint(point.X, point.Y, width, height, marginLeft, marginTop, xMin, xMax, yMin, yMax);
        var upper = ToCanvasPoint(point.X, point.Y + error, width, height, marginLeft, marginTop, xMin, xMax, yMin, yMax);
        var lower = ToCanvasPoint(point.X, point.Y - error, width, height, marginLeft, marginTop, xMin, xMax, yMin, yMax);
        const double cap = 4.0;

        PlotCanvas.Children.Add(new Line
        {
            StartPoint = upper,
            EndPoint = lower,
            Stroke = brush,
            StrokeThickness = 1.2
        });

        PlotCanvas.Children.Add(new Line
        {
            StartPoint = new Point(center.X - cap, upper.Y),
            EndPoint = new Point(center.X + cap, upper.Y),
            Stroke = brush,
            StrokeThickness = 1.2
        });

        PlotCanvas.Children.Add(new Line
        {
            StartPoint = new Point(center.X - cap, lower.Y),
            EndPoint = new Point(center.X + cap, lower.Y),
            Stroke = brush,
            StrokeThickness = 1.2
        });
    }

    private void DrawMarker(CurvePoint point, double width, double height, double marginLeft, double marginTop, double xMin, double xMax, double yMin, double yMax, IBrush brush)
    {
        var center = ToCanvasPoint(point.X, point.Y, width, height, marginLeft, marginTop, xMin, xMax, yMin, yMax);
        const double r = 3.0;
        var marker = new Ellipse
        {
            Width = 2 * r,
            Height = 2 * r,
            Fill = brush,
            Stroke = brush,
            StrokeThickness = 1
        };

        Canvas.SetLeft(marker, center.X - r);
        Canvas.SetTop(marker, center.Y - r);
        PlotCanvas.Children.Add(marker);
    }

    private void DrawLegend(IReadOnlyList<PlotSeries> series, double left, double top)
    {
        for (int i = 0; i < series.Count; i++)
        {
            var y = top + i * 20;
            var brush = GetSeriesBrush(i);

            PlotCanvas.Children.Add(new Line
            {
                StartPoint = new Point(left, y + 8),
                EndPoint = new Point(left + 18, y + 8),
                Stroke = brush,
                StrokeThickness = 2
            });

            var label = new TextBlock
            {
                Text = series[i].Name,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#303030"))
            };
            Canvas.SetLeft(label, left + 24);
            Canvas.SetTop(label, y);
            PlotCanvas.Children.Add(label);
        }
    }

    private static IBrush GetSeriesBrush(int index)
    {
        string[] colors =
        {
            "#0A66C2", "#C2185B", "#388E3C", "#F57C00", "#7B1FA2", "#0097A7", "#5D4037", "#455A64"
        };

        return new SolidColorBrush(Color.Parse(colors[index % colors.Length]));
    }
}
