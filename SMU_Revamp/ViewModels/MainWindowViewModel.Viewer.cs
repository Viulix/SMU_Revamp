using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SMU_Revamp.Models;
using SMU_Revamp.Services;
using SMU_Revamp.MeasurementPlans;
using System.IO;
using System.Text.RegularExpressions;
using SMU_Revamp.Interfaces;
using Avalonia.Platform.Storage;

namespace SMU_Revamp.ViewModels;

public partial class MainWindowViewModel
{
    private static readonly string[] DefaultSeriesColors = { "#1f77b4", "#ff7f0e", "#2ca02c", "#d62728", "#9467bd", "#8c564b", "#e377c2", "#7f7f7f", "#bcbd22", "#17becf" };

    private void InitializeSeriesSettings()
    {
        var series = PlotSeries;
        var seriesCount = series?.Count ?? 1;

        var names = new string[seriesCount];
        for (int i = 0; i < seriesCount; i++)
        {
            names[i] = series != null && series.Count > i ? series[i].Name : $"Series {i + 1}";
        }

        // Fast path: PlotSeries is replaced on every live refresh (~200 ms) while the
        // series names stay the same. Rebuilding the ObservableCollection each time
        // fires one CollectionChanged per item, which made every bound plot redraw
        // repeatedly and caused the stutter during long wafer scans.
        if (SeriesSettings.Count == seriesCount)
        {
            bool unchanged = true;
            for (int i = 0; i < seriesCount; i++)
            {
                if (SeriesSettings[i].SeriesName != names[i]) { unchanged = false; break; }
            }
            if (unchanged) return;
        }

        // Preserve existing custom color for this series name if possible, otherwise use default
        var existingColors = new Dictionary<string, string>();
        foreach (var s in SeriesSettings)
        {
            existingColors.TryAdd(s.SeriesName, s.ColorHex);
        }

        SeriesSettings.Clear();
        for (int i = 0; i < seriesCount; i++)
        {
            var colorToUse = existingColors.TryGetValue(names[i], out var c)
                ? c
                : DefaultSeriesColors[i % DefaultSeriesColors.Length];

            SeriesSettings.Add(new SeriesSetting(names[i], colorToUse));
        }
    }

    private void ResetAdvancedSettings()
    {
        CustomPlotTitle = null;
        CustomXAxisTitle = null;
        CustomYAxisTitle = null;
        CustomXMin = null;
        CustomXMax = null;
        CustomYMin = null;
        CustomYMax = null;
        AutoFitDataX = false;
        AutoFitDataY = false;
        IsViewerLogarithmicX = false;
        CustomAspectRatioString = null;
        
        var defaultColors = new[] { "#1f77b4", "#ff7f0e", "#2ca02c", "#d62728", "#9467bd", "#8c564b", "#e377c2", "#7f7f7f", "#bcbd22", "#17becf" };
        for (int i = 0; i < SeriesSettings.Count; i++)
        {
            SeriesSettings[i].ColorHex = defaultColors[i % defaultColors.Length];
            SeriesSettings[i].LineWidth = 2.0;
            SeriesSettings[i].LineStyle = "Solid";
        }
    }

    /// <summary>
    /// Maximum number of finished-contact series kept for the live wafer-scan plot.
    /// Configured in Settings (default: 10).
    /// </summary>
    public int MaxWaferScanPlotSeries => Settings?.WaferScanMaxPlotSeries > 0 
        ? Settings.WaferScanMaxPlotSeries 
        : (ConfigurationService.Instance.GetConfig().WaferScanMaxPlotSeries > 0 ? ConfigurationService.Instance.GetConfig().WaferScanMaxPlotSeries : 10);

    /// <summary>
    /// Rolling window of the most recent finished-contact series during a wafer scan.
    /// Capped at <see cref="MaxWaferScanPlotSeries"/> so memory stays constant on long scans.
    /// </summary>
    public List<PlotSeries> WaferScanAccumulatedSeries { get; } = new();

    private void RefreshPlotDataFromPlottedPlan()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshPlotDataFromPlottedPlan);
            return;
        }

        if (PlottedPlan == null)
        {
            CurvePoints = Array.Empty<CurvePoint>();
            PlotSeries = Array.Empty<PlotSeries>();
            return;
        }

        if (IsScanningWafer && WaferScanAccumulatedSeries.Count > 0)
        {
            int maxSeries = MaxWaferScanPlotSeries;
            int start = Math.Max(0, WaferScanAccumulatedSeries.Count - maxSeries);
            var activeSeriesList = new List<PlotSeries>(maxSeries + 4);
            for (int i = start; i < WaferScanAccumulatedSeries.Count; i++)
            {
                activeSeriesList.Add(WaferScanAccumulatedSeries[i]);
            }
            
            if (PlottedPlan.PlotSeries != null && PlottedPlan.PlotSeries.Count > 0)
            {
                foreach (var s in PlottedPlan.PlotSeries)
                {
                    activeSeriesList.Add(new PlotSeries($"Live {s.Name}", new List<CurvePoint>(s.Points)));
                }
            }
            else if (PlottedPlan.ResultPoints != null && PlottedPlan.ResultPoints.Count > 0)
            {
                activeSeriesList.Add(new PlotSeries("Live", new List<CurvePoint>(PlottedPlan.ResultPoints)));
            }

            PlotSeries = activeSeriesList;
            CurvePoints = Array.Empty<CurvePoint>();
        }
        else
        {
            CurvePoints = new List<CurvePoint>(PlottedPlan.ResultPoints);
            PlotSeries = PlottedPlan.PlotSeries
                .Where(s => s.Points.Count > 0)
                .ToList();
        }
    }

    [RelayCommand]
    private void OpenSelectedResultInViewer()
    {
        if (SelectedResultContact == null) return;

        string title = SelectedResultContact.DisplayName ?? "I/V Curve";
        if (SelectedResultCell != null && SelectedResultSubCell != null)
        {
            title = $"Cell: {SelectedResultCell.Id} | Sub: {SelectedResultSubCell.Id} | {title}";
        }

        var plan = new PulseSweepMeasurementPlan();
        plan.ResultPoints.Clear();
        plan.ResultPoints.AddRange(SelectedResultContact.CurveData);

        CustomPlotTitle = title;
        PlottedPlan = plan;
        CurvePoints = new List<CurvePoint>(SelectedResultContact.CurveData);

        if (SelectedResultContact.Series != null && SelectedResultContact.Series.Count > 0)
        {
            PlotSeries = new List<PlotSeries>(SelectedResultContact.Series);
        }
        else
        {
            PlotSeries = new List<PlotSeries>
            {
                new PlotSeries(title, SelectedResultContact.CurveData.ToList())
            };
        }

        CustomXAxisTitle = SelectedResultContact.XAxisLabel;
        CustomYAxisTitle = SelectedResultContact.YAxisLabel;
        OnPropertyChanged(nameof(XAxisTitle));
        OnPropertyChanged(nameof(YAxisTitle));
        OnPropertyChanged(nameof(IsPlottedPlanLoaded));
        InitializeSeriesSettings();

        SelectedTabIndex = 0; // Switch to Viewer tab
    }
}
