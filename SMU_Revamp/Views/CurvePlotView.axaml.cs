using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using SMU_Revamp.Models;
using ScottPlot;

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

    public static readonly StyledProperty<IEnumerable<PlotSeries>?> SeriesProperty =
        AvaloniaProperty.Register<CurvePlotView, IEnumerable<PlotSeries>?>(nameof(Series));

    public static readonly StyledProperty<bool> LogarithmicYProperty =
        AvaloniaProperty.Register<CurvePlotView, bool>(nameof(LogarithmicY));

    public static readonly StyledProperty<bool> LogarithmicXProperty =
        AvaloniaProperty.Register<CurvePlotView, bool>(nameof(LogarithmicX));

    public static readonly StyledProperty<SMU_Revamp.Models.PlotStyle> PlotStyleProperty =
        AvaloniaProperty.Register<CurvePlotView, SMU_Revamp.Models.PlotStyle>(nameof(PlotStyle), SMU_Revamp.Models.PlotStyle.Line);

    public static readonly StyledProperty<double> PlotAspectRatioProperty =
        AvaloniaProperty.Register<CurvePlotView, double>(nameof(PlotAspectRatio), 1.333);

    public static readonly StyledProperty<double?> XMinProperty =
        AvaloniaProperty.Register<CurvePlotView, double?>(nameof(XMin));

    public static readonly StyledProperty<double?> XMaxProperty =
        AvaloniaProperty.Register<CurvePlotView, double?>(nameof(XMax));

    public static readonly StyledProperty<double?> YMinProperty =
        AvaloniaProperty.Register<CurvePlotView, double?>(nameof(YMin));

    public static readonly StyledProperty<double?> YMaxProperty =
        AvaloniaProperty.Register<CurvePlotView, double?>(nameof(YMax));

    public static readonly StyledProperty<IEnumerable<SMU_Revamp.ViewModels.SeriesSetting>?> SeriesSettingsProperty =
        AvaloniaProperty.Register<CurvePlotView, IEnumerable<SMU_Revamp.ViewModels.SeriesSetting>?>(nameof(SeriesSettings));

    public static readonly StyledProperty<bool> AutoFitDataProperty =
        AvaloniaProperty.Register<CurvePlotView, bool>(nameof(AutoFitData));

    static CurvePlotView()
    {
        TitleProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        XAxisLabelProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        YAxisLabelProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        PointsProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        SeriesProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        LogarithmicYProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        LogarithmicXProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        PlotStyleProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        PlotAspectRatioProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.UpdateAspectRatio());
        XMinProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        XMaxProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        YMinProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        YMaxProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
        SeriesSettingsProperty.Changed.AddClassHandler<CurvePlotView>((control, e) => control.OnSeriesSettingsChanged(e));
        AutoFitDataProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.Redraw());
    }

    private void OnSeriesSettingsChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is System.Collections.Specialized.INotifyCollectionChanged oldIncc)
        {
            oldIncc.CollectionChanged -= SeriesSettings_CollectionChanged;
        }
        if (e.OldValue is System.Collections.IEnumerable oldList)
        {
            foreach (var item in oldList)
            {
                if (item is System.ComponentModel.INotifyPropertyChanged inpc)
                    inpc.PropertyChanged -= SeriesSetting_PropertyChanged;
            }
        }

        if (e.NewValue is System.Collections.Specialized.INotifyCollectionChanged newIncc)
        {
            newIncc.CollectionChanged += SeriesSettings_CollectionChanged;
        }
        if (e.NewValue is System.Collections.IEnumerable newList)
        {
            foreach (var item in newList)
            {
                if (item is System.ComponentModel.INotifyPropertyChanged inpc)
                    inpc.PropertyChanged += SeriesSetting_PropertyChanged;
            }
        }
        Redraw();
    }

    private void SeriesSettings_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is System.ComponentModel.INotifyPropertyChanged inpc)
                    inpc.PropertyChanged -= SeriesSetting_PropertyChanged;
            }
        }
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is System.ComponentModel.INotifyPropertyChanged inpc)
                    inpc.PropertyChanged += SeriesSetting_PropertyChanged;
            }
        }
        Redraw();
    }

    private void SeriesSetting_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "PickerColor" || e.PropertyName == "ColorHex")
        {
            Redraw();
        }
    }

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? XAxisLabel { get => GetValue(XAxisLabelProperty); set => SetValue(XAxisLabelProperty, value); }
    public string? YAxisLabel { get => GetValue(YAxisLabelProperty); set => SetValue(YAxisLabelProperty, value); }
    public IEnumerable<CurvePoint>? Points { get => GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public IEnumerable<PlotSeries>? Series { get => GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public bool LogarithmicY { get => GetValue(LogarithmicYProperty); set => SetValue(LogarithmicYProperty, value); }
    public bool LogarithmicX { get => GetValue(LogarithmicXProperty); set => SetValue(LogarithmicXProperty, value); }
    public SMU_Revamp.Models.PlotStyle PlotStyle { get => GetValue(PlotStyleProperty); set => SetValue(PlotStyleProperty, value); }
    public double PlotAspectRatio { get => GetValue(PlotAspectRatioProperty); set => SetValue(PlotAspectRatioProperty, value); }
    public double? XMin { get => GetValue(XMinProperty); set => SetValue(XMinProperty, value); }
    public double? XMax { get => GetValue(XMaxProperty); set => SetValue(XMaxProperty, value); }
    public double? YMin { get => GetValue(YMinProperty); set => SetValue(YMinProperty, value); }
    public double? YMax { get => GetValue(YMaxProperty); set => SetValue(YMaxProperty, value); }
    public IEnumerable<SMU_Revamp.ViewModels.SeriesSetting>? SeriesSettings { get => GetValue(SeriesSettingsProperty); set => SetValue(SeriesSettingsProperty, value); }
    public bool AutoFitData { get => GetValue(AutoFitDataProperty); set => SetValue(AutoFitDataProperty, value); }

    public CurvePlotView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Redraw();
        
        if (ContainerGrid != null)
        {
            ContainerGrid.SizeChanged += (s, e) => UpdateAspectRatio();
        }
    }

    private void UpdateAspectRatio()
    {
        if (ContainerGrid == null || AvaPlot == null || ContainerGrid.Bounds.Width <= 0) return;
        
        double availableWidth = ContainerGrid.Bounds.Width;
        double availableHeight = ContainerGrid.Bounds.Height;
        if (availableHeight <= 0) return;
        
        double currentRatio = availableWidth / availableHeight;
        
        if (currentRatio > PlotAspectRatio)
        {
            AvaPlot.Width = availableHeight * PlotAspectRatio;
            AvaPlot.Height = availableHeight;
        }
        else
        {
            AvaPlot.Width = availableWidth;
            AvaPlot.Height = availableWidth / PlotAspectRatio;
        }
    }

    private List<PlotSeries> GetEffectiveSeries()
    {
        var suppliedSeries = Series?.Where(s => s.Points.Count >= 2).ToList() ?? new List<PlotSeries>();
        if (suppliedSeries.Count > 0) return suppliedSeries;

        var points = Points?.ToList() ?? new List<CurvePoint>();
        return points.Count >= 2
            ? new List<PlotSeries> { new PlotSeries(Title ?? "Data", points) }
            : new List<PlotSeries>();
    }

    private void Redraw()
    {
        if (AvaPlot is null) return;
        AvaPlot.Plot.Clear();

        var series = GetEffectiveSeries();
        if (series.Count == 0)
        {
            AvaPlot.Plot.Title("No curve data");
            AvaPlot.Refresh();
            return;
        }

        AvaPlot.Plot.Title(Title ?? string.Empty);
        AvaPlot.Plot.Axes.Bottom.Label.Text = XAxisLabel ?? string.Empty;
        AvaPlot.Plot.Axes.Left.Label.Text = YAxisLabel ?? string.Empty;

        // Configure X Axis
        if (LogarithmicX)
        {
            var tickGen = new ScottPlot.TickGenerators.NumericAutomatic()
            {
                MinorTickGenerator = new ScottPlot.TickGenerators.LogMinorTickGenerator(),
                LabelFormatter = (double val) => $"1E{(int)Math.Round(val)}"
            };
            AvaPlot.Plot.Axes.Bottom.TickGenerator = tickGen;
        }
        else
        {
            AvaPlot.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic();
        }

        // Configure Y Axis
        if (LogarithmicY)
        {
            var tickGen = new ScottPlot.TickGenerators.NumericAutomatic()
            {
                MinorTickGenerator = new ScottPlot.TickGenerators.LogMinorTickGenerator(),
                LabelFormatter = (double val) => $"1E{(int)Math.Round(val)}"
            };
            AvaPlot.Plot.Axes.Left.TickGenerator = tickGen;
        }
        else
        {
            AvaPlot.Plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic();
        }

        bool drawLine = PlotStyle == SMU_Revamp.Models.PlotStyle.Line || PlotStyle == SMU_Revamp.Models.PlotStyle.LineAndScatter || PlotStyle == SMU_Revamp.Models.PlotStyle.InterpolatedLine || PlotStyle == SMU_Revamp.Models.PlotStyle.InterpolatedLineAndScatter;
        bool drawScatter = PlotStyle == SMU_Revamp.Models.PlotStyle.Scatter || PlotStyle == SMU_Revamp.Models.PlotStyle.LineAndScatter || PlotStyle == SMU_Revamp.Models.PlotStyle.InterpolatedLineAndScatter;

        foreach (var s in series)
        {
            var xs = new double[s.Points.Count];
            var ys = new double[s.Points.Count];
            
            for (int i = 0; i < s.Points.Count; i++)
            {
                double x = s.Points[i].X;
                double y = s.Points[i].Y;

                if (LogarithmicX) x = Math.Log10(Math.Max(Math.Abs(x), 1e-12));
                if (LogarithmicY) y = Math.Log10(Math.Max(Math.Abs(y), 1e-12));

                xs[i] = x;
                ys[i] = y;
            }

            var sp = AvaPlot.Plot.Add.Scatter(xs, ys);
            sp.LegendText = s.Name;
            
            var seriesSettingsList = SeriesSettings?.ToList();
            if (seriesSettingsList != null)
            {
                var setting = seriesSettingsList.FirstOrDefault(set => set.SeriesName == s.Name);
                if (setting != null && !string.IsNullOrWhiteSpace(setting.ColorHex))
                {
                    try
                    {
                        var color = ScottPlot.Color.FromHex(setting.ColorHex);
                        sp.Color = color;
                    }
                    catch { }
                }
            }
            
            if (drawLine && drawScatter)
            {
                sp.LineWidth = 2;
                sp.MarkerSize = 5;
            }
            else if (drawLine)
            {
                sp.LineWidth = 2;
                sp.MarkerSize = 0;
            }
            else if (drawScatter)
            {
                sp.LineWidth = 0;
                sp.MarkerSize = 5;
            }

            DrawYErrorBars(s.Points, sp.Color);
        }

        if (series.Count > 1)
        {
            AvaPlot.Plot.ShowLegend();
        }
        else
        {
            AvaPlot.Plot.HideLegend();
        }

        AvaPlot.Plot.Axes.Margins(0.05, 0.05);

        AvaPlot.Plot.Grid.MajorLineColor = ScottPlot.Colors.Black.WithOpacity(0.15);
        AvaPlot.Plot.Grid.MinorLineColor = ScottPlot.Colors.Black.WithOpacity(0.05);
        
        AvaPlot.Plot.Grid.XAxisStyle.MinorLineStyle.Width = LogarithmicX ? 1 : 0;
        AvaPlot.Plot.Grid.YAxisStyle.MinorLineStyle.Width = LogarithmicY ? 1 : 0;

        if (AutoFitData)
        {
            AvaPlot.Plot.Axes.Margins(0, 0);
            AvaPlot.Plot.Axes.AutoScale();
        }
        else
        {
            AvaPlot.Plot.Axes.Margins(0.05, 0.05);
            if (XMin.HasValue || XMax.HasValue || YMin.HasValue || YMax.HasValue)
            {
                var currentLimits = AvaPlot.Plot.Axes.GetLimits();
                double xMin = XMin ?? currentLimits.Left;
                double xMax = XMax ?? currentLimits.Right;
                double yMin = YMin ?? currentLimits.Bottom;
                double yMax = YMax ?? currentLimits.Top;

                if (LogarithmicX)
                {
                    if (XMin.HasValue) xMin = Math.Log10(Math.Max(Math.Abs(XMin.Value), 1e-12));
                    if (XMax.HasValue) xMax = Math.Log10(Math.Max(Math.Abs(XMax.Value), 1e-12));
                }
                if (LogarithmicY)
                {
                    if (YMin.HasValue) yMin = Math.Log10(Math.Max(Math.Abs(YMin.Value), 1e-12));
                    if (YMax.HasValue) yMax = Math.Log10(Math.Max(Math.Abs(YMax.Value), 1e-12));
                }

                // Ensure valid range
                if (xMin >= xMax) xMax = xMin + 1;
                if (yMin >= yMax) yMax = yMin + 1;

                AvaPlot.Plot.Axes.SetLimits(xMin, xMax, yMin, yMax);
            }
            else
            {
                AvaPlot.Plot.Axes.AutoScale();
            }
        }

        AvaPlot.Refresh();
    }
    private void DrawYErrorBars(IReadOnlyList<CurvePoint> points, ScottPlot.Color color)
    {
        // Error bars are optional. Existing measurement plans keep YError = null, so they are unaffected.
        // Frequency Memory mean points use YError = sample standard deviation.
        if (points.Count == 0)
            return;

        // The current log plot implementation transforms Y to log10(abs(Y)).
        // Symmetric linear ±SD bars would be misleading there, so draw them only on linear Y plots.
        if (LogarithmicY)
            return;

        bool hasErrorBars = points.Any(p =>
            p.YError is double error &&
            error > 0 &&
            !double.IsNaN(error) &&
            !double.IsInfinity(error));

        if (!hasErrorBars)
            return;

        double TransformX(double x) => LogarithmicX ? Math.Log10(Math.Max(Math.Abs(x), 1e-12)) : x;

        var transformedXs = points.Select(p => TransformX(p.X)).ToList();
        double xMin = transformedXs.Min();
        double xMax = transformedXs.Max();
        double xSpan = xMax - xMin;

        double capHalfWidth;
        if (double.IsNaN(xSpan) || double.IsInfinity(xSpan) || xSpan <= 0)
        {
            capHalfWidth = Math.Max(Math.Abs(xMin) * 0.01, 0.5);
        }
        else
        {
            capHalfWidth = xSpan * 0.0075;
        }

        foreach (var point in points)
        {
            if (point.YError is not double error ||
                error <= 0 ||
                double.IsNaN(error) ||
                double.IsInfinity(error))
            {
                continue;
            }

            double x = TransformX(point.X);
            double yLow = point.Y - error;
            double yHigh = point.Y + error;

            if (!double.IsFinite(x) || !double.IsFinite(yLow) || !double.IsFinite(yHigh))
                continue;

            var vertical = AvaPlot.Plot.Add.Scatter(new[] { x, x }, new[] { yLow, yHigh });
            vertical.Color = color;
            vertical.LineWidth = 1;
            vertical.MarkerSize = 0;

            var lowerCap = AvaPlot.Plot.Add.Scatter(new[] { x - capHalfWidth, x + capHalfWidth }, new[] { yLow, yLow });
            lowerCap.Color = color;
            lowerCap.LineWidth = 1;
            lowerCap.MarkerSize = 0;

            var upperCap = AvaPlot.Plot.Add.Scatter(new[] { x - capHalfWidth, x + capHalfWidth }, new[] { yHigh, yHigh });
            upperCap.Color = color;
            upperCap.LineWidth = 1;
            upperCap.MarkerSize = 0;
        }
    }

}
