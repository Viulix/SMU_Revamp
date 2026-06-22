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
    private ObservableCollection<ResultContactViewModel> _contacts = new();

    [ObservableProperty]
    private double _aggregatedValue = double.NaN;

    public string FormattedValue => ResultContactViewModel.FormatValue(AggregatedValue);

    [ObservableProperty]
    private IBrush _color = new SolidColorBrush(Avalonia.Media.Color.Parse("#F8FAFC"));

    public void RecalculateValue()
    {
        var validContacts = Contacts.Where(c => !double.IsNaN(c.AggregatedValue)).ToList();
        if (validContacts.Any())
        {
            AggregatedValue = validContacts.Average(c => c.AggregatedValue);
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
    private ObservableCollection<ResultSubCellViewModel> _subCells = new();

    [ObservableProperty]
    private double _aggregatedValue = double.NaN;

    public string FormattedValue => ResultContactViewModel.FormatValue(AggregatedValue);

    [ObservableProperty]
    private IBrush _color = new SolidColorBrush(Avalonia.Media.Color.Parse("#F8FAFC"));

    public void RecalculateValue()
    {
        var validSubCells = SubCells.Where(s => !double.IsNaN(s.AggregatedValue)).ToList();
        if (validSubCells.Any())
        {
            AggregatedValue = validSubCells.Average(s => s.AggregatedValue);
        }
        else
        {
            AggregatedValue = double.NaN;
        }
        OnPropertyChanged(nameof(FormattedValue));
    }
}

