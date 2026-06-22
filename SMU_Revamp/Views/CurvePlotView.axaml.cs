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

    static CurvePlotView()
    {
        TitleProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.UpdateLabels());
        XAxisLabelProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.UpdateLabels());
        YAxisLabelProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.UpdateLabels());
        PointsProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        SeriesProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        LogarithmicYProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
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

        if (allPoints.Count < 2 || PlotCanvas.Bounds.Width <= 0 || PlotCanvas.Bounds.Height <= 0)
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

        var xMin = allPoints.Min(p => p.X);
        var xMax = allPoints.Max(p => p.X);
        if (Math.Abs(xMax - xMin) < 1e-12)
        {
            xMax = xMin + 1;
        }

        var yValues = allPoints
            .Select(p => LogarithmicY ? Math.Max(Math.Abs(p.Y), 1e-12) : p.Y)
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

    private List<PlotSeries> GetEffectiveSeries()
    {
        var suppliedSeries = Series?
            .Where(s => s.Points.Count >= 2)
            .ToList() ?? new List<PlotSeries>();

        if (suppliedSeries.Count > 0)
        {
            return suppliedSeries;
        }

        var points = Points?.ToList() ?? new List<CurvePoint>();
        return points.Count >= 2
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

            var xValue = xMin + ratio * (xMax - xMin);
            var yValue = LogarithmicY ? Math.Pow(10, yMax - ratio * (yMax - yMin)) : yMax - ratio * (yMax - yMin);
            var xLabel = new TextBlock
            {
                Text = xValue.ToString("0.###", CultureInfo.InvariantCulture),
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
        var polyline = new Polyline
        {
            Stroke = brush,
            StrokeThickness = 2
        };

        foreach (var point in points.OrderBy(p => p.X))
        {
            var x = marginLeft + ((point.X - xMin) / (xMax - xMin)) * width;
            var rawY = LogarithmicY ? Math.Log10(Math.Max(Math.Abs(point.Y), 1e-12)) : point.Y;
            var y = marginTop + height - ((rawY - yMin) / (yMax - yMin)) * height;
            polyline.Points.Add(new Point(x, y));
        }

        PlotCanvas.Children.Add(polyline);
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
