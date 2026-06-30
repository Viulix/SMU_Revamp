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
    }

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? XAxisLabel { get => GetValue(XAxisLabelProperty); set => SetValue(XAxisLabelProperty, value); }
    public string? YAxisLabel { get => GetValue(YAxisLabelProperty); set => SetValue(YAxisLabelProperty, value); }
    public IEnumerable<CurvePoint>? Points { get => GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public IEnumerable<PlotSeries>? Series { get => GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public bool LogarithmicY { get => GetValue(LogarithmicYProperty); set => SetValue(LogarithmicYProperty, value); }
    public bool LogarithmicX { get => GetValue(LogarithmicXProperty); set => SetValue(LogarithmicXProperty, value); }
    public SMU_Revamp.Models.PlotStyle PlotStyle { get => GetValue(PlotStyleProperty); set => SetValue(PlotStyleProperty, value); }

    public CurvePlotView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Redraw();
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

        AvaPlot.Refresh();
    }
}
