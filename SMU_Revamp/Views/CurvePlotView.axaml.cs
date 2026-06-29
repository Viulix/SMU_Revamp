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

    public static readonly StyledProperty<PlotStyle> PlotStyleProperty =
        AvaloniaProperty.Register<CurvePlotView, PlotStyle>(nameof(PlotStyle), PlotStyle.Line);

    static CurvePlotView()
    {
        TitleProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.UpdateLabels());
        XAxisLabelProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.UpdateLabels());
        YAxisLabelProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.UpdateLabels());
        PointsProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        SeriesProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        LogarithmicYProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
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

    public PlotStyle PlotStyle
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

        if (allPoints.Count < 2 || PlotCanvas.Bounds.Width <= 0 || PlotCanvas.Bounds.Height <= 0)
        {
            DrawEmptyState();
            return;
        }

        var marginLeft = 58.0;
        var marginRight = 18.0;
        var marginTop = series.Count > 1 ? 36.0 : 16.0;
        var marginBottom = 28.0;

        var availableWidth = Math.Max(1, PlotCanvas.Bounds.Width - marginLeft - marginRight);
        var availableHeight = Math.Max(1, PlotCanvas.Bounds.Height - marginTop - marginBottom);

        double targetAspectRatio = 1.6;
        double currentAspectRatio = availableWidth / availableHeight;

        double width = availableWidth;
        double height = availableHeight;

        if (currentAspectRatio > targetAspectRatio)
        {
            width = availableHeight * targetAspectRatio;
            marginLeft += (availableWidth - width) / 2.0;
        }
        else
        {
            height = availableWidth / targetAspectRatio;
            marginTop += (availableHeight - height) / 2.0;
        }

        LegendPanel.Margin = new Avalonia.Thickness(0, Math.Max(0, marginTop - 25), marginRight, 0);

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

        BuildLegend(series);
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
        bool drawLine = PlotStyle == PlotStyle.Line || PlotStyle == PlotStyle.LineAndScatter;
        bool drawScatter = PlotStyle == PlotStyle.Scatter || PlotStyle == PlotStyle.LineAndScatter || PlotStyle == PlotStyle.InterpolatedLineAndScatter;
        bool drawInterpolated = PlotStyle == PlotStyle.InterpolatedLine || PlotStyle == PlotStyle.InterpolatedLineAndScatter;

        var canvasPoints = new List<Point>();

        foreach (var point in points)
        {
            var x = marginLeft + ((point.X - xMin) / (xMax - xMin)) * width;
            var rawY = LogarithmicY ? Math.Log10(Math.Max(Math.Abs(point.Y), 1e-12)) : point.Y;
            var y = marginTop + height - ((rawY - yMin) / (yMax - yMin)) * height;
            
            canvasPoints.Add(new Point(x, y));

            if (drawScatter)
            {
                var ellipse = new Ellipse
                {
                    Fill = brush,
                    Width = 6,
                    Height = 6
                };
                Canvas.SetLeft(ellipse, x - 3);
                Canvas.SetTop(ellipse, y - 3);
                PlotCanvas.Children.Add(ellipse);
            }
        }

        if (drawLine && canvasPoints.Count > 0)
        {
            var polyline = new Polyline
            {
                Stroke = brush,
                StrokeThickness = 2
            };
            foreach (var p in canvasPoints) polyline.Points.Add(p);
            PlotCanvas.Children.Add(polyline);
        }
        else if (drawInterpolated && canvasPoints.Count > 1)
        {
            var path = new Path
            {
                Stroke = brush,
                StrokeThickness = 2,
                Data = CreateAkimaSpline(canvasPoints)
            };
            PlotCanvas.Children.Add(path);
        }
    }

    private PathGeometry CreateAkimaSpline(List<Point> points)
    {
        var geometry = new PathGeometry();
        if (points.Count < 2) return geometry;

        var figure = new PathFigure { StartPoint = points[0], IsClosed = false };

        if (points.Count == 2)
        {
            figure.Segments.Add(new LineSegment { Point = points[1] });
            geometry.Figures.Add(figure);
            return geometry;
        }

        int n = points.Count;
        double[] m = new double[n + 3];

        for (int i = 0; i < n - 1; i++)
        {
            double dx = points[i + 1].X - points[i].X;
            m[i + 2] = Math.Abs(dx) > 1e-12 ? (points[i + 1].Y - points[i].Y) / dx : 0.0;
        }

        m[1] = 2 * m[2] - m[3];
        m[0] = 2 * m[1] - m[2];
        m[n + 1] = 2 * m[n] - m[n - 1];
        m[n + 2] = 2 * m[n + 1] - m[n];

        double[] t = new double[n];
        for (int i = 0; i < n; i++)
        {
            double m_im2 = m[i];
            double m_im1 = m[i + 1];
            double m_i = m[i + 2];
            double m_ip1 = m[i + 3];

            double w1 = Math.Abs(m_ip1 - m_i);
            double w2 = Math.Abs(m_im1 - m_im2);

            if (w1 + w2 > 1e-12)
            {
                t[i] = (w1 * m_im1 + w2 * m_i) / (w1 + w2);
            }
            else
            {
                t[i] = 0.5 * (m_im1 + m_i);
            }
        }

        for (int i = 0; i < n - 1; i++)
        {
            var p1 = points[i];
            var p2 = points[i + 1];
            double h = p2.X - p1.X;

            var cp1 = new Point(p1.X + h / 3.0, p1.Y + t[i] * h / 3.0);
            var cp2 = new Point(p2.X - h / 3.0, p2.Y - t[i + 1] * h / 3.0);

            figure.Segments.Add(new BezierSegment { Point1 = cp1, Point2 = cp2, Point3 = p2 });
        }

        geometry.Figures.Add(figure);
        return geometry;
    }

    private void BuildLegend(IReadOnlyList<PlotSeries> series)
    {
        LegendPanel.Children.Clear();
        if (series.Count <= 1)
        {
            LegendPanel.IsVisible = false;
            return;
        }

        LegendPanel.IsVisible = true;
        for (int i = 0; i < series.Count; i++)
        {
            var brush = GetSeriesBrush(i);
            
            var stack = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Margin = new Thickness(15, 0, 0, 0) };
            
            var line = new Line
            {
                StartPoint = new Point(0, 8),
                EndPoint = new Point(18, 8),
                Stroke = brush,
                StrokeThickness = 2,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            };
            
            var text = new TextBlock
            {
                Text = series[i].Name,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#303030")),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            
            stack.Children.Add(line);
            stack.Children.Add(text);
            LegendPanel.Children.Add(stack);
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
