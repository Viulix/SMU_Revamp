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
    private void SubscribeToWaferMapChanges()
    {
        foreach (var c in WaferCells) c.PropertyChanged += OnWaferMapPropertyChanged;
        foreach (var c in SubCells) c.PropertyChanged += OnWaferMapPropertyChanged;
        foreach (var c in Contacts) c.PropertyChanged += OnWaferMapPropertyChanged;
    }

    private void OnWaferMapPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "IsSelected")
        {
            if (!_isLoadingWaferScanPreset && !string.IsNullOrEmpty(SelectedWaferScanPreset))
            {
                SelectedWaferScanPreset = string.Empty;
            }
        }
    }

    private void InitializeWaferCells()
    {
        for (int y = 1; y <= 16; y++)
        {
            for (int x = 1; x <= 16; x++)
            {
                string cellId = $"{y:D2}{x:D2}";
                WaferCells.Add(new WaferCellViewModel(cellId, WaferCellViewModel.IsValidCell(cellId)));
            }
        }
    }

    private void InitializeSubCells()
    {
        for (int row = 1; row <= 5; row++)
        {
            for (int col = 1; col <= 5; col++)
            {
                bool isInvalid = (row == 2 && col == 2) || (row == 5 && col == 5);
                SubCells.Add(new SubCellViewModel(row, col, !isInvalid));
            }
        }
    }

    private async Task LoadWaferScanPresetAsync(string presetName)
    {
        _isLoadingWaferScanPreset = true;
        try
        {
            var config = ConfigurationService.Instance.GetConfig();
            var preset = config.WaferScanPresets?.FirstOrDefault(p => p.Name == presetName);
            if (preset == null) return;

            if (int.TryParse(preset.DelayMs, out int delay))
                WaferScanDelayMs = delay;

            foreach (var c in SubCells)
            {
            if (!c.IsValid) continue;
            c.IsSelected = preset.SelectedSubCells.Contains(c.Id);
        }

        foreach (var c in Contacts)
        {
            if (int.TryParse(c.Id, out int cId))
            {
                c.IsSelected = preset.SelectedContacts.Contains(cId);
            }
        }

        foreach (var c in WaferCells)
        {
            if (!c.IsValid) continue;
            c.IsSelected = preset.SelectedWaferCells.Contains(c.Id);
        }
        
        NewWaferScanPresetName = presetName;
        NotificationRequested?.Invoke("Preset Loaded", $"Wafer scan preset '{presetName}' loaded.", null);
        }
        finally
        {
            _isLoadingWaferScanPreset = false;
        }
    }

    [RelayCommand]
    private async Task SaveWaferScanPresetAsync()
    {
        if (string.IsNullOrWhiteSpace(NewWaferScanPresetName))
        {
            NotificationRequested?.Invoke("Error", "Please enter a preset name.", null);
            return;
        }

        var config = ConfigurationService.Instance.GetConfig();
        if (config.WaferScanPresets == null) config.WaferScanPresets = new();
        
        var preset = config.WaferScanPresets.FirstOrDefault(p => p.Name == NewWaferScanPresetName);
        if (preset == null)
        {
            preset = new Models.WaferScanPreset { Name = NewWaferScanPresetName };
            config.WaferScanPresets.Add(preset);
            WaferScanPresetNames.Add(NewWaferScanPresetName);
        }

        preset.DelayMs = WaferScanDelayMs.ToString();
        preset.SelectedSubCells = SubCells.Where(c => c.IsSelected).Select(c => c.Id).ToList();
        preset.SelectedContacts = Contacts.Where(c => c.IsSelected && int.TryParse(c.Id, out _)).Select(c => int.Parse(c.Id)).ToList();
        preset.SelectedWaferCells = WaferCells.Where(c => c.IsSelected).Select(c => c.Id).ToList();

        await ConfigurationService.Instance.SaveAsync(config);
        
        SelectedWaferScanPreset = NewWaferScanPresetName;
        NotificationRequested?.Invoke("Preset Saved", $"Wafer scan preset '{NewWaferScanPresetName}' saved.", null);
    }

    [RelayCommand]
    private async Task DeleteWaferScanPresetAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedWaferScanPreset)) return;

        var config = ConfigurationService.Instance.GetConfig();
        if (config.WaferScanPresets == null) return;

        var preset = config.WaferScanPresets.FirstOrDefault(p => p.Name == SelectedWaferScanPreset);
        if (preset != null)
        {
            var dialog = new Views.SavePromptWindow("Delete Preset", $"Are you sure you want to delete the wafer scan preset '{SelectedWaferScanPreset}'?");
            
            bool result = false;
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                result = await dialog.ShowDialog<bool>(desktop.MainWindow);
            }
            
            if (result)
            {
                config.WaferScanPresets.Remove(preset);
                WaferScanPresetNames.Remove(SelectedWaferScanPreset);
                await ConfigurationService.Instance.SaveAsync(config);
                SelectedWaferScanPreset = string.Empty;
                NewWaferScanPresetName = string.Empty;
                NotificationRequested?.Invoke("Preset Deleted", "Wafer scan preset removed.", null);
            }
        }
    }

    private async Task GoToScanStartAsync()
    {
        try
        {
            await ProberService.Instance.ConnectAsync();
            await ProberService.Instance.DisconnectChuckAsync();
            await ProberService.Instance.ProberGoHomeAsync();
            WaferScanLogFontWeight = Avalonia.Media.FontWeight.Bold;
            IsScanningWafer = false;
        }
        catch (Exception ex)
        {
            WaferScanLog = $"Error moving to start: {ex.Message}";
        }
    }

    private void ConfirmStopWaferScan()
    {
        IsCancelPromptVisible = false;
        if (_scanCts != null)
        {
            _scanCts.Cancel();
            WaferScanLog = "Wafer scan canceled by user!";
            WaferScanLogFontWeight = Avalonia.Media.FontWeight.Bold;
        }
    }

    private async Task StartWaferScanAsync()
    {
        if (IsScanningWafer) return;

        WaferScanLog = "Parsing target contacts...";
        WaferScanProgress = 0;
        
        _parsedScanContacts.Clear();
        foreach (var c in Contacts)
        {
            if (c.IsSelected && int.TryParse(c.Id, out int cId))
            {
                _parsedScanContacts.Add(cId);
            }
        }

        if (_parsedScanContacts.Count == 0)
        {
            WaferScanLog = "Error: Invalid target contacts.";
            PopupErrorMessage = "Invalid target contacts. Please specify valid contact numbers (1-6).";
            IsErrorPopupVisible = true;
            return;
        }

        _totalExpectedCells = 0;
        foreach (var cell in WaferCells)
        {
            if (cell.IsValid && cell.IsSelected) _totalExpectedCells++;
        }

        _totalExpectedSubCells = 0;
        foreach (var subCell in SubCells)
        {
            if (subCell.IsValid && subCell.IsSelected) _totalExpectedSubCells++;
        }

        if (_totalExpectedCells == 0 || _totalExpectedSubCells == 0)
        {
            WaferScanLog = "Error: No cells selected.";
            PopupErrorMessage = "Please select at least one Target Cell and one Global Sub-cell.";
            IsErrorPopupVisible = true;
            return;
        }

        IsAlignmentWarningVisible = true;
    }

    private async Task ExecuteWaferScanAsync()
    {
        IsScanningWafer = true;
        _scanCts = new System.Threading.CancellationTokenSource();
        _currentWaferScanFolderName = $"Scan_{DateTime.Now:yyyyMMdd_HHmmss}";

        try
        {
            WaferScanLog = "Connecting to Prober...";
            await ProberService.Instance.ConnectAsync();
            
            var targetCells = new System.Collections.Generic.HashSet<string>();
            foreach (var cell in WaferCells)
            {
                if (cell.IsValid && cell.IsSelected)
                {
                    targetCells.Add(cell.Id);
                }
            }

            var targetSubCells = new System.Collections.Generic.HashSet<(int row, int col)>();
            foreach (var subCell in SubCells)
            {
                if (subCell.IsValid && subCell.IsSelected)
                {
                    targetSubCells.Add((subCell.Row, subCell.Column));
                }
            }

            int totalExpectedContacts = _totalExpectedCells * _totalExpectedSubCells * _parsedScanContacts.Count;
            int currentContact = 0;
            var scanStepDurations = new System.Collections.Generic.Queue<TimeSpan>();
            var stepStopwatch = new System.Diagnostics.Stopwatch();

            WaferScanCountText = $"0 / {totalExpectedContacts}";
            WaferScanEstimatedFinish = string.Empty;
            WaferScanLog = "Starting wafer scan...";
            WaferScanLogFontWeight = Avalonia.Media.FontWeight.Normal;
            
            await ProberService.Instance.ScanWaferAsync(targetCells, targetSubCells, _parsedScanContacts, WaferScanDelayMs, async (cell, row, col, contact) =>
            {
                if (stepStopwatch.IsRunning)
                {
                    stepStopwatch.Stop();
                    scanStepDurations.Enqueue(stepStopwatch.Elapsed);
                    if (scanStepDurations.Count > 5) scanStepDurations.Dequeue();
                    
                    double avgMs = System.Linq.Enumerable.Average(scanStepDurations, ts => ts.TotalMilliseconds);
                    int remaining = totalExpectedContacts - currentContact;
                    TimeSpan estimatedRemaining = TimeSpan.FromMilliseconds(avgMs * remaining);
                    DateTime finishTime = DateTime.Now + estimatedRemaining;
                    WaferScanEstimatedFinish = $"Est. Finish: {finishTime:HH:mm:ss}";
                }
                stepStopwatch.Restart();

                WaferScanLog = $"Measuring Cell: {cell}, Row: {row}, Col: {col}, Contact: {contact}";
                
                // Update UI state for auto-save filenames
                TargetCell = cell;
                TargetRow = row.ToString();
                TargetColumn = col.ToString();
                TargetContact = contact.ToString();
                
                // Trigger the actual measurement
                await RunMeasurementAsync();
                
                currentContact++;
                WaferScanProgress = (double)currentContact / totalExpectedContacts * 100.0;
                WaferScanCountText = $"{currentContact} / {totalExpectedContacts}";
            }, _scanCts.Token);

            WaferScanLog = "Wafer scan completed.";
            WaferScanProgress = 100;
            WaferScanCountText = $"{totalExpectedContacts} / {totalExpectedContacts}";
        }
        catch (OperationCanceledException)
        {
            WaferScanLog = "Wafer scan canceled.";
        }
        catch (Exception ex)
        {
            WaferScanLog = $"Error during scan: {ex.Message}";
        }
        finally
        {
            try 
            {
                WaferScanLog = "Separating and returning to home...";
                await ProberService.Instance.DisconnectChuckAsync();
                await ProberService.Instance.ProberGoHomeAsync();
                WaferScanLog = "Wafer scan finished.";
            }
            catch { }

            IsScanningWafer = false;
            _scanCts?.Dispose();
            _scanCts = null;
            (ScanWaferCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            (RequestStopScanCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    private async Task GoToContactAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            if (!TryParseTargetInputs(out var cellPosition, out var row, out var col, out var contact, out var error))
            {
                ErrorMessage = error;
                return;
            }

            await GoToContactHugeDeltaBAsync(
                cellPosition,
                row,
                col,
                contact,
                StayHere,
                AdvPathA,
                AdvPathB);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
            Console.WriteLine($"Error moving to contact: {ex.Message}");
        }
    }

    private async Task DisconnectRouteAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(AdvPathA) || string.IsNullOrWhiteSpace(AdvPathB))
            {
                ErrorMessage = "Please specify Adv Path A and Adv Path B to disconnect.";
                return;
            }

            await SwitchMatrixService.Instance.ConnectAsync();
            var channel = await SwitchMatrixService.Instance.RemoveConnectionAsync(AdvPathA, AdvPathB);
            MeasurementStatus = $"Successfully disconnected route {channel}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error disconnecting route: {ex.Message}";
        }
    }

    private async Task ClearAllMatrixAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            await SwitchMatrixService.Instance.ConnectAsync();
            await SwitchMatrixService.Instance.ClearAllConnectionsAsync();
            MeasurementStatus = "Successfully cleared all switch matrix connections.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error clearing matrix: {ex.Message}";
        }
    }

    private async Task MoveRelativeAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            await ProberService.Instance.ConnectAsync();
            await ProberService.Instance.MoveProberAsync(MoveX, MoveY);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error moving relative: {ex.Message}";
        }
    }

    private async Task MoveAbsoluteAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            await ProberService.Instance.ConnectAsync();
            await ProberService.Instance.MoveProberAbsoluteAsync(MoveX, MoveY);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error moving absolute: {ex.Message}";
        }
    }

    private bool TryParseTargetInputs(
        out string cellPosition,
        out int row,
        out int col,
        out int contact,
        out string error)
    {
        cellPosition = string.Empty;
        row = 0;
        col = 0;
        contact = 0;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(TargetCell))
        {
            error = "Target cell is required (format: RRCC).";
            return false;
        }

        cellPosition = TargetCell.Trim();
        if (cellPosition.Length < 4)
        {
            error = "Target cell must be at least 4 characters (RRCC).";
            return false;
        }

        if (!int.TryParse(cellPosition.Substring(0, 2), out _) || !int.TryParse(cellPosition.Substring(2, 2), out _))
        {
            error = "Target cell must be numeric in format RRCC.";
            return false;
        }

        if (!int.TryParse(TargetRow, out row) || row < 1)
        {
            error = "Row must be a positive number.";
            return false;
        }

        if (!int.TryParse(TargetColumn, out col) || col < 1)
        {
            error = "Column must be a positive number.";
            return false;
        }

        if (!int.TryParse(TargetContact, out contact) || contact < 1 || contact > 6)
        {
            error = "Contact must be a number between 1 and 6.";
            return false;
        }

        return true;
    }

    private async Task GoToContactHugeDeltaBAsync(
        string cellPosition,
        int row,
        int col,
        int contact,
        bool stayHere,
        string advPathA,
        string advPathB)
    {
        if (!stayHere)
        {
            await ProberService.Instance.ConnectAsync();
            await ProberService.Instance.DisconnectChuckAsync();
            // Wait slightly after separating (similar to Autoscan logic)
            await Task.Delay(100); 
            await ProberService.Instance.GoToWaferContactAsync(cellPosition, row, col, contact);
            await Task.Delay(100);
            await ProberService.Instance.ConnectChuckAsync();
        }

        if (!string.IsNullOrWhiteSpace(advPathA) && !string.IsNullOrWhiteSpace(advPathB))
        {
            await SwitchMatrixService.Instance.ConnectAsync();
            await SwitchMatrixService.Instance.CreateConnectionAsync(advPathA, advPathB, overrideCheck: true);
        }
    }

}
