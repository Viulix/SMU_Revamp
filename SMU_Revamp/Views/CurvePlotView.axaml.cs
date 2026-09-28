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

    public static readonly StyledProperty<bool> AutoFitDataXProperty =
        AvaloniaProperty.Register<CurvePlotView, bool>(nameof(AutoFitDataX));

    public static readonly StyledProperty<bool> AutoFitDataYProperty =
        AvaloniaProperty.Register<CurvePlotView, bool>(nameof(AutoFitDataY));

    static CurvePlotView()
    {
        TitleProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.RequestRedraw());
        XAxisLabelProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.RequestRedraw());
        YAxisLabelProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.RequestRedraw());
        PointsProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.RequestRedraw());
        SeriesProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.RequestRedraw());
        LogarithmicYProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.RequestRedraw());
        LogarithmicXProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.RequestRedraw());
        PlotStyleProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.RequestRedraw());
        PlotAspectRatioProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.UpdateAspectRatio());
        XMinProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.RequestRedraw());
        XMaxProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.RequestRedraw());
        YMinProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.RequestRedraw());
        YMaxProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.RequestRedraw());
        SeriesSettingsProperty.Changed.AddClassHandler<CurvePlotView>((control, e) => control.OnSeriesSettingsChanged(e));
        AutoFitDataXProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.RequestRedraw());
        AutoFitDataYProperty.Changed.AddClassHandler<CurvePlotView>((control, _) => control.RequestRedraw());
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
        if (e.PropertyName == "PickerColor" || e.PropertyName == "ColorHex" || e.PropertyName == "LineWidth" || e.PropertyName == "LineStyle")
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
    public bool AutoFitDataX { get => GetValue(AutoFitDataXProperty); set => SetValue(AutoFitDataXProperty, value); }
    public bool AutoFitDataY { get => GetValue(AutoFitDataYProperty); set => SetValue(AutoFitDataYProperty, value); }

    private bool _isRedrawQueued;
    private bool _needsRedrawWhenVisible;

    public void RequestRedraw()
    {
        if (!IsEffectivelyVisible)
        {
            _needsRedrawWhenVisible = true;
            return;
        }

        if (_isRedrawQueued) return;
        _isRedrawQueued = true;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _isRedrawQueued = false;
            if (IsEffectivelyVisible)
            {
                Redraw();
            }
            else
            {
                _needsRedrawWhenVisible = true;
            }
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    public CurvePlotView()
    {
        InitializeComponent();
        
        if (ContainerGrid != null)
        {
            ContainerGrid.SizeChanged += (s, e) => UpdateAspectRatio();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_needsRedrawWhenVisible || (AvaPlot?.Plot.PlottableList.Count ?? 0) == 0)
        {
            _needsRedrawWhenVisible = false;
            RequestRedraw();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty)
        {
            UpdateAspectRatio();
        }
        else if (change.Property == IsVisibleProperty)
        {
            if (IsEffectivelyVisible && _needsRedrawWhenVisible)
            {
                _needsRedrawWhenVisible = false;
                RequestRedraw();
            }
        }
    }

    private void UpdateAspectRatio()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(UpdateAspectRatio);
            return;
        }

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
        var suppliedSeries = Series?.Where(s => s.Points.Count >= 1).ToList() ?? new List<PlotSeries>();
        if (suppliedSeries.Count > 0) return suppliedSeries;

        var points = Points?.ToList() ?? new List<CurvePoint>();
        return points.Count >= 1
            ? new List<PlotSeries> { new PlotSeries(Title ?? "Data", points) }
            : new List<PlotSeries>();
    }

    private void Redraw()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(Redraw);
            return;
        }

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
        bool isInterpolated = PlotStyle == SMU_Revamp.Models.PlotStyle.InterpolatedLine || PlotStyle == SMU_Revamp.Models.PlotStyle.InterpolatedLineAndScatter;

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
            sp.Smooth = isInterpolated && xs.Length > 2;
            
            var seriesSettingsList = SeriesSettings?.ToList();
            if (seriesSettingsList != null)
            {
                var setting = seriesSettingsList.FirstOrDefault(set => set.SeriesName == s.Name);
                if (setting != null)
                {
                    if (!string.IsNullOrWhiteSpace(setting.ColorHex))
                    {
                        try
                        {
                            var color = ScottPlot.Color.FromHex(setting.ColorHex);
                            sp.Color = color;
                        }
                        catch { }
                    }
                    
                    sp.LineWidth = (float)setting.LineWidth;
                    
                    switch (setting.LineStyle)
                    {
                        case "Dashed": sp.LinePattern = ScottPlot.LinePattern.Dashed; break;
                        case "Dotted": sp.LinePattern = ScottPlot.LinePattern.Dotted; break;
                        default: sp.LinePattern = ScottPlot.LinePattern.Solid; break;
                    }
                }
            }
            
            if (xs.Length == 1)
            {
                // Single-point spot measurements must have a visible marker
                sp.LineWidth = 0;
                sp.MarkerSize = 7;
            }
            else if (drawLine && drawScatter)
            {
                sp.MarkerSize = 5;
            }
            else if (drawLine)
            {
                sp.MarkerSize = 0;
            }
            else if (drawScatter)
            {
                sp.LineWidth = 0;
                sp.MarkerSize = 5;
            }

            DrawYErrorBars(s.Points, sp.Color);
        }

        if (series.Count > 1 && series.Count <= 20)
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

        AvaPlot.Plot.Axes.Margins(AutoFitDataX ? 0 : 0.05, AutoFitDataY ? 0 : 0.05);

        // AutoScale both axes with the configured margins first
        AvaPlot.Plot.Axes.AutoScale();

        if (XMin.HasValue || XMax.HasValue || YMin.HasValue || YMax.HasValue)
        {
            var currentLimits = AvaPlot.Plot.Axes.GetLimits();
            double xMin = currentLimits.Left;
            double xMax = currentLimits.Right;
            double yMin = currentLimits.Bottom;
            double yMax = currentLimits.Top;
            bool limitsChanged = false;

            if (!AutoFitDataX)
            {
                if (XMin.HasValue) { xMin = XMin.Value; limitsChanged = true; }
                if (XMax.HasValue) { xMax = XMax.Value; limitsChanged = true; }
                
                if (LogarithmicX && limitsChanged)
                {
                    if (XMin.HasValue) xMin = Math.Log10(Math.Max(Math.Abs(XMin.Value), 1e-12));
                    if (XMax.HasValue) xMax = Math.Log10(Math.Max(Math.Abs(XMax.Value), 1e-12));
                }
            }

            if (!AutoFitDataY)
            {
                if (YMin.HasValue) { yMin = YMin.Value; limitsChanged = true; }
                if (YMax.HasValue) { yMax = YMax.Value; limitsChanged = true; }

                if (LogarithmicY && limitsChanged)
                {
                    if (YMin.HasValue) yMin = Math.Log10(Math.Max(Math.Abs(YMin.Value), 1e-12));
                    if (YMax.HasValue) yMax = Math.Log10(Math.Max(Math.Abs(YMax.Value), 1e-12));
                }
            }

            if (limitsChanged)
            {
                // Ensure valid range
                if (xMin >= xMax) xMax = xMin + 1;
                if (yMin >= yMax) yMax = yMin + 1;

                AvaPlot.Plot.Axes.SetLimits(xMin, xMax, yMin, yMax);
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

        var barXs = new List<double>(points.Count * 9);
        var barYs = new List<double>(points.Count * 9);

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

            // Vertical line
            barXs.Add(x); barYs.Add(yLow);
            barXs.Add(x); barYs.Add(yHigh);
            barXs.Add(double.NaN); barYs.Add(double.NaN);

            // Lower cap
            barXs.Add(x - capHalfWidth); barYs.Add(yLow);
            barXs.Add(x + capHalfWidth); barYs.Add(yLow);
            barXs.Add(double.NaN); barYs.Add(double.NaN);

            // Upper cap
            barXs.Add(x - capHalfWidth); barYs.Add(yHigh);
            barXs.Add(x + capHalfWidth); barYs.Add(yHigh);
            barXs.Add(double.NaN); barYs.Add(double.NaN);
        }

        if (barXs.Count > 0)
        {
            var errPlot = AvaPlot.Plot.Add.Scatter(barXs.ToArray(), barYs.ToArray());
            errPlot.Color = color;
            errPlot.LineWidth = 1;
            errPlot.MarkerSize = 0;
            errPlot.LegendText = string.Empty;
        }
    }

}
