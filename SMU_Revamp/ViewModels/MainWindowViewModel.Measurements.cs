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
    private async Task RunMeasurementAsync()
    {
        if (IsMeasuring) return;
        if (SelectedPlan == null) return;

        IsMeasuring = true;

        // Update the plotted plan to be the one we are running
        PlottedPlan = SelectedPlan;

        // Immediately clear old measurement view
        PlottedPlan.ResultPoints.Clear();
        RefreshPlotDataFromPlottedPlan();

        ErrorMessage = string.Empty;
        MeasurementStatus = "Starting...";
        MeasurementProgress = 0;
        IsProgressIndeterminate = false;

        // Check auto-save if active and prompt if Profile or Sample Name is empty
        if (AutoSaveMeasurements)
        {
            if (string.IsNullOrWhiteSpace(Settings.Profile) || string.IsNullOrWhiteSpace(Settings.SampleName))
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                {
                    var promptWindow = new SMU_Revamp.Views.SavePromptWindow(Settings.Profile, Settings.SampleName);
                    var result = await promptWindow.ShowDialog<SMU_Revamp.Views.SavePromptResult>(desktop.MainWindow);
                    if (result == null || result.Cancelled)
                    {
                        MeasurementStatus = "Measurement aborted: Profile and Sample Name are required for auto-saving.";
                        IsMeasuring = false;
                        return;
                    }
                    
                    // Update settings values
                    Settings.Profile = result.Profile;
                    Settings.SampleName = result.SampleName;
                    await SaveSettingsAndConfigurationAsync();
                }
            }
        }

        // Check for PotDep times < 20ms
        if (SelectedPlan is PotDepMeasurementPlan potDep)
        {
            if (potDep.GetParamValueDouble("tpot") < 20 ||
                potDep.GetParamValueDouble("tdep") < 20 ||
                potDep.GetParamValueDouble("treadPD") < 20)
            {
                WarningMessage = "Warning: times below 20ms may be inaccurate!";
            }
            else
            {
                WarningMessage = string.Empty;
            }
        }
        else
        {
            WarningMessage = string.Empty;
        }

        // Persist measurement settings automatically when running
        await SaveMeasurementConfigAsync();

        try
        {
            // Connect to SMU
            // Connect to SMU
            MeasurementStatus = "Connecting to E5263 SMU...";
            var smu = E5263_SMU.Instance;

            // Ensure timeout configuration is synced
            var config = ConfigurationService.Instance.GetConfig();
            smu.ResourceString = config.SMUResource;
            smu.SetTimeout(config.SMUTimeoutMs);

            await smu.ConnectAsync();

            MeasurementStatus = $"Executing plan {PlottedPlan.Name}...";
            int lastPointCount = 0;
            var progressReporter = new Progress<double>(p =>
            {
                MeasurementProgress = p;
                if (PlottedPlan != null && PlottedPlan.ResultPoints.Count != lastPointCount)
                {
                    lastPointCount = PlottedPlan.ResultPoints.Count;
                    RefreshPlotDataFromPlottedPlan();
                }
            });
            await PlottedPlan.RunMeasurementAsync(smu, progressReporter);

            // Final update of viewer data to ensure we didn't miss anything.
            RefreshPlotDataFromPlottedPlan();

            if (HasCurvePoints)
            {
                if (CurvePoints.Count == 1)
                {
                    var pt = CurvePoints[0];
                    MeasurementStatus = System.FormattableString.Invariant($"Finished. Measured Point - {XAxisTitle}: {pt.X:F4}, {YAxisTitle}: {pt.Y:E6}");
                }
                else if (PlotSeries.Count > 1)
                {
                    MeasurementStatus = $"Finished. Measured {CurvePoints.Count} points in {PlotSeries.Count} plot series.";
                }
                else
                {
                    MeasurementStatus = $"Finished. Measured {CurvePoints.Count} points.";
                }

                // Auto-save measurement if enabled and has data points
                if (AutoSaveMeasurements)
                {
                    try
                    {
                        var profile = Settings.Profile;
                        var sampleName = Settings.SampleName;
                        
                        string folderName = "";
                        string folderPath;
                        try
                        {
                            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                            if (IsScanningWafer)
                            {
                                folderName = _currentWaferScanFolderName;
                                folderPath = System.IO.Path.Combine(documentsPath, "SMU_Measurements", profile, "Wafermaps", folderName);
                            }
                            else
                            {
                                folderName = $"{sampleName}_{DateTime.Now:yyyyMMdd}";
                                folderPath = System.IO.Path.Combine(documentsPath, "SMU_Measurements", profile, folderName);
                            }
                        }
                        catch
                        {
                            if (IsScanningWafer)
                            {
                                folderName = _currentWaferScanFolderName;
                                folderPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SMU_Measurements", profile, "Wafermaps", folderName);
                            }
                            else
                            {
                                folderName = $"{sampleName}_{DateTime.Now:yyyyMMdd}";
                                folderPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SMU_Measurements", profile, folderName);
                            }
                        }
                        
                        if (!System.IO.Directory.Exists(folderPath))
                        {
                            System.IO.Directory.CreateDirectory(folderPath);
                        }
                        
                        var planName = PlottedPlan.Name.Replace(" ", "_").Replace("-", "_");
                        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        
                        string fileName;
                        if (IsScanningWafer)
                        {
                            fileName = $"{sampleName}_{planName}_Cell{TargetCell}_R{TargetRow}C{TargetColumn}_Contact{TargetContact}_{timestamp}.csv";
                        }
                        else
                        {
                            fileName = $"{sampleName}_{planName}_{timestamp}.csv";
                        }
                        
                        var fullPath = System.IO.Path.Combine(folderPath, fileName);
                        
                        var rawLines = PlottedPlan.GetCsvLines();
                        var lines = new List<string>();
                        
                        // Always prepend sep=\t for instant Excel compatibility
                        lines.Add("sep=\t");
                        
                        int insertIndex = 0;
                        if (rawLines.Count > 0 && rawLines[0].StartsWith("sep="))
                        {
                            insertIndex = 1;
                        }
                        
                        lines.Add($"# Plan\t{PlottedPlan.Name}");
                        foreach (var p in PlottedPlan.Parameters)
                        {
                            lines.Add(System.FormattableString.Invariant($"# {p.Name}\t{p.GetValueAsString()}"));
                        }
                        
                        for (int i = insertIndex; i < rawLines.Count; i++)
                        {
                            lines.Add(rawLines[i]);
                        }
                        await System.IO.File.WriteAllLinesAsync(fullPath, lines);
                        
                        MeasurementStatus = $"Finished. Data autosaved to {System.IO.Path.Combine(profile, fileName)}.";

                        if (ConfigurationService.Instance.GetConfig().SaveToDatabase)
                        {
                            try
                            {
                                int dbId = await DatabaseService.Instance.SaveMeasurementAsync(PlottedPlan, Settings.Profile, sampleName, DateTime.Now, folderName, fileName);
                                MeasurementStatus += $" (DB ID: {dbId})";
                            }
                            catch (Exception dbEx)
                            {
                                WarningMessage = $"CSV saved, but database save failed: {dbEx.Message}";
                                System.Diagnostics.Debug.WriteLine($"DB Save Error: {dbEx}");
                            }
                        }

                        NotificationRequested?.Invoke(
                            "Measurement Saved",
                            $"File saved to {fileName}.\nClick to open in Explorer.",
                            fullPath
                        );
                    }
                    catch (Exception saveEx)
                    {
                        WarningMessage = $"Measurement finished, but failed to autosave: {saveEx.Message}";
                    }
                }
            }
            else
            {
                MeasurementStatus = "Finished. No data points parsed.";
            }

            if (AutoSwitchToViewer && !IsScanningWafer)
            {
                SelectedTabIndex = 0; // Auto switch to Viewer tab
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error during measurement: {ex.Message}";
            MeasurementStatus = $"Error: {ex.Message}";
            Console.WriteLine($"Error running measurement: {ex.Message}");
        }
        finally
        {
            // Close sessions
            try { await E5263_SMU.Instance.DisconnectAsync(); } catch { }
            IsMeasuring = false;
            MeasurementProgress = 100;
            IsProgressIndeterminate = false;
        }
    }

    public async Task SaveCurvePointsToCsvAsync(string filePath)
    {
        try
        {
            var lines = new List<string>();
            
            // Always prepend sep=\t for instant Excel compatibility
            lines.Add("sep=\t");
            
            if (PlottedPlan != null)
            {
                var rawLines = PlottedPlan.GetCsvLines();
                int insertIndex = 0;
                if (rawLines.Count > 0 && rawLines[0].StartsWith("sep="))
                {
                    insertIndex = 1;
                }
                
                lines.Add($"# Plan\t{PlottedPlan.Name}");
                foreach (var p in PlottedPlan.Parameters)
                {
                    lines.Add(System.FormattableString.Invariant($"# {p.Name}\t{p.GetValueAsString()}"));
                }
                
                for (int i = insertIndex; i < rawLines.Count; i++)
                {
                    lines.Add(rawLines[i]);
                }
            }
            await System.IO.File.WriteAllLinesAsync(filePath, lines);
            MeasurementStatus = $"Data successfully exported to {System.IO.Path.GetFileName(filePath)}.";

            NotificationRequested?.Invoke(
                "Export Successful",
                $"File exported to {System.IO.Path.GetFileName(filePath)}.\nClick to open in Explorer.",
                filePath
            );
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to export CSV: {ex.Message}";
            Console.WriteLine($"Error exporting CSV: {ex.Message}");
        }
    }

    public async Task LoadMeasurementFromDatabaseAsync(int measurementId)
    {
        try
        {
            var (parameters, points) = await Services.DatabaseService.Instance.LoadMeasurementDataAsync(measurementId);
            
            // Instantiate a new plan so we don't corrupt the shared instances
            // We use PulseSweepMeasurementPlan as a generic fallback since we don't save the plan name in DB
            Interfaces.IMeasurementPlan plan = new MeasurementPlans.PulseSweepMeasurementPlan();
            plan.ResultPoints.Clear();
            plan.ResultPoints.AddRange(points);
            
            // For any matching parameters from the DB, populate them in our new plan
            foreach (var p in plan.Parameters)
            {
                if (parameters.TryGetValue(p.Name, out string? val) && val != null)
                {
                    p.Value = val;
                }
            }

            PlottedPlan = plan;
            CurvePoints = new System.Collections.ObjectModel.ObservableCollection<Models.CurvePoint>(plan.ResultPoints);
            PlotSeries = new System.Collections.ObjectModel.ObservableCollection<Models.PlotSeries>(plan.PlotSeries);
            
            CustomXAxisTitle = null;
            CustomYAxisTitle = null;
            OnPropertyChanged(nameof(XAxisTitle));
            OnPropertyChanged(nameof(YAxisTitle));
            
            IsMeasurementLogarithmicX = false;
            IsMeasurementLogarithmic = plan.ShowLogPlot;

            MeasurementStatus = $"Finished. Data loaded from database (ID: {measurementId}).";
            
            SelectedTabIndex = 0; // Auto switch to Viewer tab
            
            NotificationRequested?.Invoke(
                "Load Successful",
                $"Successfully loaded measurement {measurementId} from database.",
                null
            );
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load from database: {ex.Message}";
        }
    }

    public async Task UploadCurrentMeasurementToDatabaseAsync()
    {
        if (PlottedPlan == null || PlottedPlan.ResultPoints.Count == 0)
        {
            ErrorMessage = "No measurement data to upload.";
            return;
        }

        try
        {
            // For duplicate checking, use a dummy filename if none exists, or empty
            string dummyFilename = $"{Settings.SampleName}_{PlottedPlan.Name}_{DateTime.Now:yyyyMMdd_HHmmss}";
            bool isUploaded = await Services.DatabaseService.Instance.IsMeasurementUploadedAsync(dummyFilename);
            
            if (isUploaded)
            {
                NotificationRequested?.Invoke("Upload Skipped", "This measurement appears to have already been uploaded.", null);
                return;
            }

            int dbId = await Services.DatabaseService.Instance.SaveMeasurementAsync(PlottedPlan, Settings.Profile, Settings.SampleName, DateTime.Now, dummyFilename);
            
            MeasurementStatus = $"Finished. Data uploaded to database (ID: {dbId}).";
            NotificationRequested?.Invoke("Upload Successful", $"Successfully uploaded to database. ID: {dbId}", null);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to upload to database: {ex.Message}";
        }
    }

    public async Task ImportCurvePointsFromFileAsync(string filePath)
    {
        try
        {
            var lines = await Task.Run(() => File.ReadAllLines(filePath));
            string planName = string.Empty;
            var paramDict = new Dictionary<string, string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                if (trimmed.StartsWith("#"))
                {
                    var content = trimmed.Substring(1).Trim();
                    
                    // Robust delimiter detection in metadata comment
                    char[] delimiters = { '\t', ';', ':', ',' };
                    int bestIdx = -1;
                    foreach (var delim in delimiters)
                    {
                        int idx = content.IndexOf(delim);
                        if (idx > 0 && (bestIdx == -1 || idx < bestIdx))
                        {
                            bestIdx = idx;
                        }
                    }
                    if (bestIdx == -1)
                    {
                        bestIdx = content.IndexOf(' ');
                    }

                    if (bestIdx > 0)
                    {
                        var key = content.Substring(0, bestIdx).Trim();
                        var val = content.Substring(bestIdx + 1).Trim();
                        
                        // Clean up leading delimiter/equality characters
                        if (val.StartsWith(":") || val.StartsWith(";") || val.StartsWith(",") || val.StartsWith("="))
                        {
                            val = val.Substring(1).Trim();
                        }

                        if (key.Equals("Plan", StringComparison.OrdinalIgnoreCase))
                        {
                            planName = val;
                        }
                        else
                        {
                            paramDict[key] = val;
                        }
                    }
                }
            }

            // Find separator and header line for heuristics/auto-detection
            char? detectedSeparator = null;
            string? headerLine = null;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("sep=", StringComparison.OrdinalIgnoreCase))
                {
                    var sepStr = trimmed.Substring(4).Trim();
                    if (sepStr.Length > 0)
                    {
                        detectedSeparator = sepStr[0];
                    }
                }
                else if (!trimmed.StartsWith("#") && !string.IsNullOrEmpty(trimmed))
                {
                    if (headerLine == null)
                    {
                        headerLine = trimmed;
                    }
                }
            }

            if (headerLine != null && detectedSeparator == null)
            {
                if (headerLine.Contains('\t'))
                {
                    detectedSeparator = '\t';
                }
                else if (headerLine.Contains(';'))
                {
                    detectedSeparator = ';';
                }
                else if (headerLine.Contains(','))
                {
                    detectedSeparator = ',';
                }
                else
                {
                    detectedSeparator = '\t';
                }
            }

            List<string> headers = new List<string>();
            if (headerLine != null && detectedSeparator.HasValue)
            {
                headers = headerLine.Split(detectedSeparator.Value)
                                    .Select(h => h.Trim().Trim('"'))
                                    .ToList();
            }

            // Heuristics to auto-detect plan name if missing or unrecognized
            if (string.IsNullOrWhiteSpace(planName) && headers.Count > 0)
            {
                if (headers.Any(h => h.Contains("Cycle 1 Voltage") || h.Contains("Cycle 2 Voltage") || h.Contains("Cycle 1 Current")))
                {
                    planName = "Memristor Sweep";
                }
                else if (headers.Any(h => h.Contains("TrialIndex") || h.Contains("Readout1_") || h.Contains("Readout2_")))
                {
                    planName = "Spike Timing";
                }
                else if (headers.Any(h => h.Equals("Cycle", StringComparison.OrdinalIgnoreCase)) && headers.Any(h => h.Contains("Current")))
                {
                    planName = "PotDep";
                }
            }

            IMeasurementPlan? plan = null;
            if (!string.IsNullOrWhiteSpace(planName))
            {
                var matchingPlan = MeasurementPlans.FirstOrDefault(p => string.Equals(p.Name, planName, StringComparison.OrdinalIgnoreCase));
                if (matchingPlan != null)
                {
                    try
                    {
                        plan = Activator.CreateInstance(matchingPlan.GetType()) as IMeasurementPlan;
                    }
                    catch { }
                }
            }

            if (plan == null)
            {
                plan = new ImportedMeasurementPlan
                {
                    Name = string.IsNullOrWhiteSpace(planName) ? Path.GetFileName(filePath) : planName,
                    Description = $"Data loaded from {Path.GetFileName(filePath)}."
                };
            }

            foreach (var param in plan.Parameters)
            {
                if (paramDict.TryGetValue(param.Name, out var valStr))
                {
                    if (param.Type == ParameterType.Number)
                    {
                        if (SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(valStr, out double dVal))
                        {
                            param.Value = dVal;
                        }
                    }
                    else
                    {
                        param.Value = valStr;
                    }
                }
            }

            // Let the plan load the actual data points (standard or custom multi-column layout)
            plan.LoadFromCsvLines(lines);

            int totalPoints = plan.ResultPoints.Count;
            if (plan.PlotSeries != null && plan.PlotSeries.Count > 0)
            {
                totalPoints = plan.PlotSeries.Sum(s => s.Points.Count);
            }

            if (totalPoints == 0)
            {
                NotificationRequested?.Invoke("Import Error", "No data points could be parsed from the file.", null);
                return;
            }

            PlottedPlan = plan;
            RefreshPlotDataFromPlottedPlan();

            NotificationRequested?.Invoke("Success", $"Successfully loaded {totalPoints} points from {Path.GetFileName(filePath)}.", null);
        }
        catch (Exception ex)
        {
            NotificationRequested?.Invoke("Import Error", $"Failed to load file: {ex.Message}", null);
        }
    }

    private async Task SaveMeasurementConfigAsync()
    {
        try
        {
            var config = ConfigurationService.Instance.GetConfig();

            if (config.LastPlanParameters == null)
            {
                config.LastPlanParameters = new();
            }

            // Save top-level app config parameters from active plans
            foreach (var plan in MeasurementPlans)
            {
                if (!config.LastPlanParameters.ContainsKey(plan.Name))
                {
                    config.LastPlanParameters[plan.Name] = new Dictionary<string, string>();
                }

                foreach (var param in plan.Parameters)
                {
                    config.LastPlanParameters[plan.Name][param.Name] = param.GetValueAsString() ?? string.Empty;

                    switch (param.Name)
                    {
                        case "WriteChannel":
                        case "Channel":
                            config.SweepChannel = param.GetValueAsString();
                            break;
                        case "StartVoltage":
                            config.SweepStart = param.GetValueAsDouble();
                            break;
                        case "Voltage":
                            config.SweepStart = param.GetValueAsDouble(); // Map to SweepStart for backward compatibility
                            break;
                        case "StopVoltage":
                            config.SweepStop = param.GetValueAsDouble();
                            break;
                        case "Points":
                            config.SweepPoints = param.GetValueAsInt();
                            break;
                        case "Compliance":
                            config.SweepCompliance = param.GetValueAsDouble();
                            break;
                        case "AdcSamples":
                            config.SweepAdcSamples = param.GetValueAsInt();
                            break;
                        case "SweepMode":
                            config.SelectedSweepMode = param.GetValueAsString();
                            break;
                    }
                }
            }

            await ConfigurationService.Instance.SaveAsync(config);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save measurement configuration: {ex.Message}");
        }
    }

    private async Task SaveSettingsAndConfigurationAsync()
    {
        try
        {
            // First apply settings from Settings VM (which updates ConfigurationService internally)
            await Settings.ApplySettingsAsync();

            // Then retrieve updated config, merge our measurement settings, and save
            await SaveMeasurementConfigAsync();
            Settings.ApplyStatusMessage = "Settings and measurement configuration saved.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save configuration: {ex.Message}";
        }
    }

    private void AddSequenceStep(StepType type)
    {
        if (SelectedPlan is ModularSequenceMeasurementPlan modular)
        {
            SelectedPreset = null; // Clear preset
            var step = new SequenceStep 
            { 
                Type = type,
                WriteChannel = "2",
                ReadingChannel = "2",
                Compliance = 0.1,
                AdcSamples = 1
            };
            if (type == StepType.Pulse)
            {
                step.BaseVoltage = 0.0;
                step.PulseVoltage = 1.0;
                step.PulseWidth = 0.001;
                step.PulsePeriod = 0.01;
            }
            else if (type == StepType.Sweep)
            {
                step.Voltage = 0.0;
                step.StopVoltage = 1.5;
                step.Points = 41;
                step.SweepMode = "Single Staircase (1)";
            }
            else if (type == StepType.Point)
            {
                step.Voltage = 1.0;
            }
            else if (type == StepType.Measure)
            {
                step.KeepCurrentVoltage = true;
                step.Voltage = 0.0;
            }

            modular.Steps.Add(step);
            SelectedSequenceStep = step;
        }
    }

    private void MoveSelectedStepUp()
    {
        if (SelectedPlan is ModularSequenceMeasurementPlan modular && SelectedSequenceStep != null)
        {
            int index = modular.Steps.IndexOf(SelectedSequenceStep);
            if (index > 0)
            {
                SelectedPreset = null; // Clear preset
                var step = SelectedSequenceStep;
                modular.Steps.RemoveAt(index);
                modular.Steps.Insert(index - 1, step);
                SelectedSequenceStep = step;
            }
        }
    }

    private void MoveSelectedStepDown()
    {
        if (SelectedPlan is ModularSequenceMeasurementPlan modular && SelectedSequenceStep != null)
        {
            int index = modular.Steps.IndexOf(SelectedSequenceStep);
            if (index >= 0 && index < modular.Steps.Count - 1)
            {
                SelectedPreset = null; // Clear preset
                var step = SelectedSequenceStep;
                modular.Steps.RemoveAt(index);
                modular.Steps.Insert(index + 1, step);
                SelectedSequenceStep = step;
            }
        }
    }

    private void DeleteSelectedStep()
    {
        if (SelectedPlan is ModularSequenceMeasurementPlan modular && SelectedSequenceStep != null)
        {
            SelectedPreset = null; // Clear preset
            int index = modular.Steps.IndexOf(SelectedSequenceStep);
            modular.Steps.Remove(SelectedSequenceStep);
            if (modular.Steps.Count > 0)
            {
                int newIndex = Math.Clamp(index, 0, modular.Steps.Count - 1);
                SelectedSequenceStep = modular.Steps[newIndex];
            }
            else
            {
                SelectedSequenceStep = null;
            }
        }
    }

    public void ReloadPlanParameters()
    {
        MeasurementPlans = MeasurementPlanLoader.LoadPlans();
        var prevPlanName = SelectedPlan?.Name;
        SelectedPlan = MeasurementPlans.Find(p => p.Name == prevPlanName) ?? (MeasurementPlans.Count > 0 ? MeasurementPlans[0] : null!);
    }

    private void LoadAvailablePresets()
    {
        var config = ConfigurationService.Instance.GetConfig();
        if (config.Presets != null)
        {
            AvailablePresets = new ObservableCollection<MeasurementPreset>(config.Presets);
        }
        else
        {
            AvailablePresets = new ObservableCollection<MeasurementPreset>();
        }
    }

    private void LoadLastConfig()
    {
        if (SelectedPlan == null) return;
        var config = ConfigurationService.Instance.GetConfig();
        if (config.LastPlanParameters != null && config.LastPlanParameters.TryGetValue(SelectedPlan.Name, out var lastParams))
        {
            foreach (var param in SelectedPlan.Parameters)
            {
                if (lastParams.TryGetValue(param.Name, out var stringVal))
                {
                    try
                    {
                        if (param.Type == ParameterType.Number)
                        {
                            if (ParameterConfigHelper.TryParseDoubleRobust(stringVal, out double d))
                                param.Value = d;
                        }
                        else if (param.Type == ParameterType.Checkbox)
                        {
                            if (bool.TryParse(stringVal, out bool b))
                                param.Value = b;
                        }
                        else
                        {
                            param.Value = stringVal;
                        }
                    }
                    catch { }
                }
            }
        }
    }

    private async void ExecuteSavePreset(string name)
    {
        if (SelectedPlan == null) return;

        var config = ConfigurationService.Instance.GetConfig();
        if (config.Presets == null)
        {
            config.Presets = new List<MeasurementPreset>();
        }

        var existing = config.Presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            config.Presets.Remove(existing);
        }

        var newPreset = new MeasurementPreset 
        { 
            Name = name,
            PlanName = SelectedPlan.Name
        };
        foreach (var param in SelectedPlan.Parameters)
        {
            newPreset.Parameters[param.Name] = param.GetValueAsString() ?? string.Empty;
        }

        config.Presets.Add(newPreset);
        await ConfigurationService.Instance.SaveAsync(config);
        
        LoadAvailablePresets();
        SelectedPreset = config.Presets.FirstOrDefault(p => p.Name == name);
        NewPresetName = string.Empty;
    }

    private void SubscribeToParameterChanges()
    {
        if (_selectedPlan?.Parameters == null) return;
        foreach (var param in _selectedPlan.Parameters)
        {
            param.PropertyChanged -= OnParameterPropertyChanged;
            param.PropertyChanged += OnParameterPropertyChanged;
        }
    }

    private async void OnParameterPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MeasurementParameter.Value))
        {
            if (!_isLoadingPreset && SelectedPreset != null)
            {
                SelectedPreset = null;
            }
            UpdateWarningMessage();
            await SaveSettingsAndConfigurationAsync();
        }
    }

    private void UpdateWarningMessage()
    {
        if (_selectedPlan == null)
        {
            WarningMessage = string.Empty;
            return;
        }

        var writeChannelParam = _selectedPlan.Parameters.Find(p => p.Name == "WriteChannel" || p.Name == "Channel");
        var readChannelParam = _selectedPlan.Parameters.Find(p => p.Name == "ReadingChannel");

        if (writeChannelParam != null && readChannelParam != null)
        {
            var writeVal = writeChannelParam.GetValueAsString()?.Trim();
            var readVal = readChannelParam.GetValueAsString()?.Trim();

            if (!string.IsNullOrEmpty(writeVal) && !string.IsNullOrEmpty(readVal) && writeVal == readVal)
            {
                WarningMessage = "Warning: Write Channel and Reading Channel are the same. The setup is generally designed for different write and read channels.";
                return;
            }
        }

        WarningMessage = string.Empty;
    }

    private void UpdateSelectedPlanSections()
    {
        if (SelectedPlan == null)
        {
            SelectedPlanSections = new List<ParameterSection>();
            return;
        }

        var config = ConfigurationService.Instance.GetConfig();
        if (config.ParameterLinks != null && config.ParameterLinks.TryGetValue(SelectedPlan.Name, out var planLinks))
        {
            foreach (var param in SelectedPlan.Parameters)
            {
                if (planLinks.TryGetValue(param.Name, out var linkConfig) && linkConfig.IsActive)
                {
                    var targetParam = SelectedPlan.Parameters.FirstOrDefault(p => p.Name == linkConfig.LinkedParameterName);
                    if (targetParam != null)
                    {
                        param.LinkedParameter = targetParam;
                        param.LinkedMultiplier = linkConfig.Multiplier;
                        param.IsLinked = true;
                    }
                    else
                    {
                        param.IsLinked = false;
                        param.LinkedParameter = null;
                    }
                }
                else
                {
                    param.IsLinked = false;
                    param.LinkedParameter = null;
                }
            }
        }
        else
        {
            foreach (var param in SelectedPlan.Parameters)
            {
                param.IsLinked = false;
                param.LinkedParameter = null;
            }
        }

        var sections = new List<ParameterSection>();
        var grouped = new Dictionary<string, List<MeasurementParameter>>();
        var sectionOrder = new List<string>();

        foreach (var param in SelectedPlan.Parameters)
        {
            var secName = param.Section ?? string.Empty;
            if (!grouped.ContainsKey(secName))
            {
                grouped[secName] = new List<MeasurementParameter>();
                sectionOrder.Add(secName);
            }
            grouped[secName].Add(param);
        }

        foreach (var secName in sectionOrder)
        {
            sections.Add(new ParameterSection
            {
                Name = secName,
                Parameters = grouped[secName]
            });
        }

        SelectedPlanSections = sections;
    }

    public void LoadConfigState()
    {
        var config = ConfigurationService.Instance.GetConfig();
        AutoSaveMeasurements = config.AutoSaveMeasurements;
        
        if (config.WaferScanPresets != null)
        {
            WaferScanPresetNames.Clear();
            foreach (var preset in config.WaferScanPresets)
            {
                WaferScanPresetNames.Add(preset.Name);
            }
        }

        // Load result visualization settings using public properties to trigger UI and recalculation!
        if (!string.IsNullOrEmpty(config.SelectedResultMetric))
        {
            SelectedResultMetric = config.SelectedResultMetric;
        }
        GapTargetVoltage = config.GapTargetVoltage;
        UseAverageForMemristorCheck = config.UseAverageForMemristorCheck;

        if (!string.IsNullOrEmpty(config.VisualizationHeatmapColorLow))
        {
            SelectedHeatmapColorLow = config.VisualizationHeatmapColorLow;
        }
        if (!string.IsNullOrEmpty(config.VisualizationHeatmapColorHigh))
        {
            SelectedHeatmapColorHigh = config.VisualizationHeatmapColorHigh;
        }

        // Load Memristor check weights (use backing fields to avoid multiple saves, but raise property changed)
        _memristorWeightSnr = config.MemristorWeightSnr;
        _memristorWeightNonlinearity = config.MemristorWeightNonlinearity;
        _memristorWeightHysteresis = config.MemristorWeightHysteresis;
        _memristorWeightBranchSep = config.MemristorWeightBranchSep;
        _memristorWeightPinch = config.MemristorWeightPinch;
        _memristorWeightSmoothness = config.MemristorWeightSmoothness;

        OnPropertyChanged(nameof(MemristorWeightSnr));
        OnPropertyChanged(nameof(MemristorWeightNonlinearity));
        OnPropertyChanged(nameof(MemristorWeightHysteresis));
        OnPropertyChanged(nameof(MemristorWeightBranchSep));
        OnPropertyChanged(nameof(MemristorWeightPinch));
        OnPropertyChanged(nameof(MemristorWeightSmoothness));

        LoadAvailablePresets();
        LoadLastConfig();
    }

    private async Task SaveAutoSaveSettingAsync(bool value)
    {
        var config = ConfigurationService.Instance.GetConfig();
        config.AutoSaveMeasurements = value;
        await ConfigurationService.Instance.SaveAsync(config);
    }

    public Task RunMeasurementAsync(E5263_SMU smu, IProgress<double>? progress = null)
    {
        return Task.CompletedTask;
    }

}
