using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SMU_Revamp.Models;

namespace SMU_Revamp.ViewModels;

public partial class ResultContactViewModel : ObservableObject
{
    public int ContactNumber { get; set; }
    public string DisplayName => $"Contact {ContactNumber}";
    public List<CurvePoint> CurveData { get; set; } = new();

    [ObservableProperty]
    private double _aggregatedValue = double.NaN;

    public string FormattedValue => FormatValue(AggregatedValue);

    [ObservableProperty]
    private IBrush _color = new SolidColorBrush(Avalonia.Media.Color.Parse("#CBD5E1")); // Neutral color

    [ObservableProperty]
    private string _metricLabel = string.Empty;

    public static string FormatValue(double value)
    {
        if (double.IsNaN(value)) return "-";
        if (Math.Abs(value) >= 1000 || (Math.Abs(value) > 0 && Math.Abs(value) < 0.01))
        {
            return value.ToString("0.0E0");
        }
        return value.ToString("0.##");
    }
}

public partial class ResultSubCellViewModel : ObservableObject
{
    public int Row { get; set; }
    public int Col { get; set; }
    public string Id => $"R{Row}C{Col}";
    
    public bool IsValid => !(Row == 2 && Col == 2) && !(Row == 5 && Col == 5);

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private ObservableCollection<ResultContactViewModel> _contacts = new();

    [ObservableProperty]
    private double _aggregatedValue = double.NaN;

    public string FormattedValue => ResultContactViewModel.FormatValue(AggregatedValue);

    [ObservableProperty]
    private IBrush _color = new SolidColorBrush(Avalonia.Media.Color.Parse("#F8FAFC"));

    [ObservableProperty]
    private string _metricLabel = string.Empty;

    public void RecalculateValue(bool useMaxAggregation = false)
    {
        var validContacts = Contacts.Where(c => !double.IsNaN(c.AggregatedValue)).ToList();
        if (validContacts.Any())
        {
            AggregatedValue = useMaxAggregation 
                ? validContacts.Max(c => c.AggregatedValue) 
                : validContacts.Average(c => c.AggregatedValue);
        }
        else
        {
            AggregatedValue = double.NaN;
        }
        OnPropertyChanged(nameof(FormattedValue));
    }
}

public partial class ResultCellViewModel : ObservableObject
{
    public int Row { get; set; }
    public int Col { get; set; }
    public string Id => $"{Row:D2}{Col:D2}";
    
    // For 16x16 wafer grid
    public bool IsValid => WaferCellViewModel.IsValidCell(Id);

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private ObservableCollection<ResultSubCellViewModel> _subCells = new();

    [ObservableProperty]
    private double _aggregatedValue = double.NaN;

    public string FormattedValue => ResultContactViewModel.FormatValue(AggregatedValue);

    [ObservableProperty]
    private IBrush _color = new SolidColorBrush(Avalonia.Media.Color.Parse("#F8FAFC"));

    [ObservableProperty]
    private string _metricLabel = string.Empty;

    public void RecalculateValue(bool useMaxAggregation = false)
    {
        var validSubCells = SubCells.Where(s => !double.IsNaN(s.AggregatedValue)).ToList();
        if (validSubCells.Any())
        {
            AggregatedValue = useMaxAggregation 
                ? validSubCells.Max(s => s.AggregatedValue) 
                : validSubCells.Average(s => s.AggregatedValue);
        }
        else
        {
            AggregatedValue = double.NaN;
        }
        OnPropertyChanged(nameof(FormattedValue));
    }
}

public static class HeatmapHelper
{
    public static IBrush GetColorForValue(double value, double min, double max, double hueLow = 220.0, double hueHigh = 0.0)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return new SolidColorBrush(Avalonia.Media.Color.Parse("#F8FAFC")); // Light Gray for invalid
        }

        if (max == min)
        {
            return new SolidColorBrush(HslToRgb(hueLow, 0.8, 0.6)); // Fallback color based on hueLow
        }

        // Normalize value between 0 and 1
        double ratio = (value - min) / (max - min);
        ratio = Math.Clamp(ratio, 0.0, 1.0);

        // HSL Interpolation from hueLow (ratio 0) to hueHigh (ratio 1)
        double diff = hueHigh - hueLow;
        if (diff > 180) diff -= 360;
        else if (diff < -180) diff += 360;
        
        double h = hueLow + (diff * ratio);
        if (h < 0) h += 360;
        if (h >= 360) h -= 360;

        double s = 0.8;
        double l = 0.5 + (0.1 * ratio); // slight lightness change if desired, or keep constant

        return new SolidColorBrush(HslToRgb(h, s, l));
    }

    private static Avalonia.Media.Color HslToRgb(double h, double s, double l)
    {
        double c = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
        double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
        double m = l - c / 2.0;

        double r = 0, g = 0, b = 0;

        if (0 <= h && h < 60) { r = c; g = x; b = 0; }
        else if (60 <= h && h < 120) { r = x; g = c; b = 0; }
        else if (120 <= h && h < 180) { r = 0; g = c; b = x; }
        else if (180 <= h && h < 240) { r = 0; g = x; b = c; }
        else if (240 <= h && h < 300) { r = x; g = 0; b = c; }
        else if (300 <= h && h < 360) { r = c; g = 0; b = x; }

        return Avalonia.Media.Color.FromRgb(
            (byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((b + m) * 255)
        );
    }
}
