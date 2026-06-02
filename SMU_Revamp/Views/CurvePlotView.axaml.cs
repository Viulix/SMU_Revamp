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

    public static readonly StyledProperty<IEnumerable<CurvePoint>?> PointsProperty =
        AvaloniaProperty.Register<CurvePlotView, IEnumerable<CurvePoint>?>(nameof(Points));

    public static readonly StyledProperty<bool> LogarithmicYProperty =
        AvaloniaProperty.Register<CurvePlotView, bool>(nameof(LogarithmicY));

    static CurvePlotView()
    {
        TitleProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.UpdateLabels());
        XAxisLabelProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.UpdateLabels());
        YAxisLabelProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.UpdateLabels());
        PointsProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
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

        var points = Points?.ToList() ?? [];
        if (points.Count < 2 || PlotCanvas.Bounds.Width <= 0 || PlotCanvas.Bounds.Height <= 0)
        {
            DrawEmptyState();
            return;
        }

        var marginLeft = 58.0;
        var marginRight = 18.0;
        var marginTop = 16.0;
        var marginBottom = 28.0;

        var width = Math.Max(1, PlotCanvas.Bounds.Width - marginLeft - marginRight);
        var height = Math.Max(1, PlotCanvas.Bounds.Height - marginTop - marginBottom);

        var xMin = points.Min(p => p.Voltage);
        var xMax = points.Max(p => p.Voltage);
        if (Math.Abs(xMax - xMin) < 1e-12)
        {
            xMax = xMin + 1;
        }

        var yValues = points
            .Select(p => LogarithmicY ? Math.Max(Math.Abs(p.Current), 1e-12) : p.Current)
            .Select(y => LogarithmicY ? Math.Log10(y) : y)
            .ToList();

        var yMin = yValues.Min();
        var yMax = yValues.Max();
        if (Math.Abs(yMax - yMin) < 1e-12)
        {
            yMax = yMin + 1;
        }

        DrawBackground(width, height, marginLeft, marginTop, marginBottom, marginRight, xMin, xMax, yMin, yMax);
        DrawCurve(points, width, height, marginLeft, marginTop, marginBottom, xMin, xMax, yMin, yMax);
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

    private void DrawCurve(IReadOnlyList<CurvePoint> points, double width, double height, double marginLeft, double marginTop, double marginBottom, double xMin, double xMax, double yMin, double yMax)
    {
        var plotHeight = height;
        var plotWidth = width;

        var polyline = new Polyline
        {
            Stroke = new SolidColorBrush(Color.Parse("#0A66C2")),
            StrokeThickness = 2
        };

        foreach (var point in points)
        {
            var x = marginLeft + ((point.Voltage - xMin) / (xMax - xMin)) * plotWidth;
            var rawY = LogarithmicY ? Math.Log10(Math.Max(Math.Abs(point.Current), 1e-12)) : point.Current;
            var y = marginTop + plotHeight - ((rawY - yMin) / (yMax - yMin)) * plotHeight;
            polyline.Points.Add(new Point(x, y));
        }

        PlotCanvas.Children.Add(polyline);
    }
}