using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SMU_Revamp.Models;
using SMU_Revamp.Services;

namespace SMU_Revamp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public string Greeting { get; } = "Welcome to Avalonia!";

    public IReadOnlyList<CurvePoint> CurvePoints { get; }

    public ReadOnlyCollection<string> MeasurementModes { get; }

    public SettingsViewModel Settings { get; }

    private string _selectedMeasurementMode = "Measure Point";
    public string SelectedMeasurementMode
    {
        get => _selectedMeasurementMode;
        set => SetProperty(ref _selectedMeasurementMode, value);
    }

    private string _targetCell = string.Empty;
    public string TargetCell
    {
        get => _targetCell;
        set => SetProperty(ref _targetCell, value);
    }

    private string _targetRow = string.Empty;
    public string TargetRow
    {
        get => _targetRow;
        set => SetProperty(ref _targetRow, value);
    }

    private string _targetColumn = string.Empty;
    public string TargetColumn
    {
        get => _targetColumn;
        set => SetProperty(ref _targetColumn, value);
    }

    private string _targetContact = string.Empty;
    public string TargetContact
    {
        get => _targetContact;
        set => SetProperty(ref _targetContact, value);
    }

    private string _errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public ICommand GoToContactCommand { get; }
    public ICommand SaveSettingsCommand { get; }

    public MainWindowViewModel()
    {
        CurvePoints = CreateCurvePoints();
        MeasurementModes = new ReadOnlyCollection<string>([
            "Measure Point",
            "U-Sweep"
        ]);
        Settings = new SettingsViewModel();
        GoToContactCommand = new AsyncRelayCommand(GoToContactAsync);
        SaveSettingsCommand = new AsyncRelayCommand(Settings.ApplySettingsAsync);
    }

    private async Task GoToContactAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            // Ensure connection before moving
            await ProberService.Instance.ConnectAsync();

            // The exact logic to move to a specific Zelle, Reihe, Spalte, Kontakt 
            // should be implemented in ProberService or orchestrated here.
            // For now, we perform the contact step.
            Console.WriteLine($"Going to Target: Cell={TargetCell}, Row={TargetRow}, Column={TargetColumn}, Contact={TargetContact}");
            await ProberService.Instance.ProberContactAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
            Console.WriteLine($"Error moving to contact: {ex.Message}");
        }
    }

    private static IReadOnlyList<CurvePoint> CreateCurvePoints()
    {
        var points = new CurvePoint[41];

        for (var i = 0; i < points.Length; i++)
        {
            var voltage = -2.0 + i * 0.1;
            var current = SimulateIuCurve(voltage);
            points[i] = new CurvePoint(voltage, current);
        }

        return points;
    }

    private static double SimulateIuCurve(double voltage)
    {
        var forward = Math.Exp(Math.Min(voltage, 1.5) * 2.2) - 1.0;
        var reverse = -Math.Exp(Math.Min(-voltage, 1.5) * 0.8) * 1e-9;
        return voltage >= 0 ? forward * 1e-6 : reverse;
    }
}
