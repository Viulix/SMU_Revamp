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
    private void InitializeSeriesSettings()
    {
        var currentSettings = SeriesSettings.ToList();
        SeriesSettings.Clear();
        
        var seriesCount = PlotSeries?.Count ?? 1;
        var defaultColors = new[] { "#1f77b4", "#ff7f0e", "#2ca02c", "#d62728", "#9467bd", "#8c564b", "#e377c2", "#7f7f7f", "#bcbd22", "#17becf" };

        for (int i = 0; i < seriesCount; i++)
        {
            var seriesName = PlotSeries != null && PlotSeries.Count > i ? PlotSeries[i].Name : $"Series {i + 1}";
            var defaultColor = defaultColors[i % defaultColors.Length];
            
            // Preserve existing custom color for this series name if possible, otherwise use default
            var existing = currentSettings.FirstOrDefault(s => s.SeriesName == seriesName);
            var colorToUse = existing != null ? existing.ColorHex : defaultColor;

            SeriesSettings.Add(new SeriesSetting(seriesName, colorToUse));
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

    public List<PlotSeries> WaferScanAccumulatedSeries { get; } = new();

    private void RefreshPlotDataFromPlottedPlan()
    {
        if (PlottedPlan == null)
        {
            CurvePoints = Array.Empty<CurvePoint>();
            PlotSeries = Array.Empty<PlotSeries>();
            return;
        }

        if (IsScanningWafer && WaferScanAccumulatedSeries.Count > 0)
        {
            var activeSeriesList = new List<PlotSeries>(WaferScanAccumulatedSeries);
            
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
        PlotSeries = new List<PlotSeries>
        {
            new PlotSeries(title, SelectedResultContact.CurveData.ToList())
        };

        CustomXAxisTitle = null;
        CustomYAxisTitle = null;
        OnPropertyChanged(nameof(XAxisTitle));
        OnPropertyChanged(nameof(YAxisTitle));
        OnPropertyChanged(nameof(IsPlottedPlanLoaded));

        SelectedTabIndex = 0; // Switch to Viewer tab
    }
}
