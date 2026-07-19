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
    private double GetHueForColor(string colorName)
    {
        return colorName switch
        {
            "Red" => 0.0,
            "Orange" => 30.0,
            "Green" => 120.0,
            "Purple" => 280.0,
            "Blue" => 220.0,
            _ => 220.0,
        };
    }

    private void InitializeResultTab()
    {
        // Initialize an empty 16x16 grid
        ResultCells.Clear();
        for (int r = 1; r <= 16; r++)
        {
            for (int c = 1; c <= 16; c++)
            {
                ResultCells.Add(new ResultCellViewModel { Row = r, Col = c });
            }
        }
    }

    public async Task LoadScanFolderAsync(string folderPath)
    {
        IsLoadingResultData = true;
        await Task.Delay(50); // Yield to UI to show loading overlay

        try
        {
            InitializeResultTab();

            var csvFiles = Directory.GetFiles(folderPath, "*.csv");

            // Regex pattern: "Cell0104_R1C5_Contact3"
            var regex = new Regex(@"Cell(?<cR>\d{2})(?<cC>\d{2})_R(?<sR>\d)C(?<sC>\d)_Contact(?<cont>\d)");

            bool filesFound = false;
            foreach (var file in csvFiles)
            {
                var filename = Path.GetFileName(file);
                var match = regex.Match(filename);
                if (!match.Success) continue;

                filesFound = true;
                int cellRow = int.Parse(match.Groups["cR"].Value);
                int cellCol = int.Parse(match.Groups["cC"].Value);
                int subRow = int.Parse(match.Groups["sR"].Value);
                int subCol = int.Parse(match.Groups["sC"].Value);
                int contact = int.Parse(match.Groups["cont"].Value);

                var cell = ResultCells.FirstOrDefault(c => c.Row == cellRow && c.Col == cellCol);
                if (cell == null) continue;

                var subCell = cell.SubCells.FirstOrDefault(s => s.Row == subRow && s.Col == subCol);
                if (subCell == null)
                {
                    subCell = new ResultSubCellViewModel { Row = subRow, Col = subCol };
                    cell.SubCells.Add(subCell);
                }

                var contactVm = subCell.Contacts.FirstOrDefault(c => c.ContactNumber == contact);
                if (contactVm == null)
                {
                    contactVm = new ResultContactViewModel { ContactNumber = contact };
                    subCell.Contacts.Add(contactVm);
                }

                // Read points
                contactVm.CurveData = ParseCsvPoints(file);
            }

            if (filesFound)
            {
                RecalculateResultMetrics();
                IsResultFolderLoaded = true;
                NotificationRequested?.Invoke("Success", $"Loaded {csvFiles.Length} measurements.", null);
            }
            else
            {
                NotificationRequested?.Invoke("Error", "No valid contact files found in folder.", null);
            }
        }
        catch (Exception ex)
        {
            NotificationRequested?.Invoke("Error", $"Failed to load scan folder: {ex.Message}", null);
        }
        finally
        {
            IsLoadingResultData = false;
        }
    }

    public async Task LoadWafermapFromDatabaseAsync(List<Services.DatabaseService.MeasurementSummary> measurements)
    {
        SelectedTabIndex = 3; // Switch to Result tab immediately so they see the loading overlay!
        IsLoadingResultData = true;
        await Task.Delay(50); // Yield to UI to show loading overlay

        try
        {
            InitializeResultTab();

            var regex = new Regex(@"Cell(?<cR>\d{2})(?<cC>\d{2})_R(?<sR>\d)C(?<sC>\d)_Contact(?<cont>\d)");

            bool filesFound = false;
            foreach (var meas in measurements)
            {
                if (string.IsNullOrEmpty(meas.SourceFilename)) continue;

                var match = regex.Match(meas.SourceFilename);
                if (!match.Success) continue;

                filesFound = true;
                int cellRow = int.Parse(match.Groups["cR"].Value);
                int cellCol = int.Parse(match.Groups["cC"].Value);
                int subRow = int.Parse(match.Groups["sR"].Value);
                int subCol = int.Parse(match.Groups["sC"].Value);
                int contact = int.Parse(match.Groups["cont"].Value);

                var cell = ResultCells.FirstOrDefault(c => c.Row == cellRow && c.Col == cellCol);
                if (cell == null) continue;

                var subCell = cell.SubCells.FirstOrDefault(s => s.Row == subRow && s.Col == subCol);
                if (subCell == null)
                {
                    subCell = new ResultSubCellViewModel { Row = subRow, Col = subCol };
                    cell.SubCells.Add(subCell);
                }

                var contactVm = subCell.Contacts.FirstOrDefault(c => c.ContactNumber == contact);
                if (contactVm == null)
                {
                    contactVm = new ResultContactViewModel { ContactNumber = contact };
                    subCell.Contacts.Add(contactVm);
                }

                // Read points from DB
                var dbData = await Services.DatabaseService.Instance.LoadMeasurementDataAsync(meas.Id);
                contactVm.CurveData = dbData.Points;
            }

            if (filesFound)
            {
                RecalculateResultMetrics();
                IsResultFolderLoaded = true;
                NotificationRequested?.Invoke("Success", $"Loaded {measurements.Count} measurements from database.", null);
            }
            else
            {
                NotificationRequested?.Invoke("Error", "No valid contact measurements found in selected node.", null);
            }
        }
        catch (Exception ex)
        {
            NotificationRequested?.Invoke("Error", $"Failed to load wafermap from DB: {ex.Message}", null);
        }
        finally
        {
            IsLoadingResultData = false;
        }
    }

    private void RecalculateResultMetrics()
    {
        bool useMaxAggregation = SelectedResultMetric == "Memristor Check" && !UseAverageForMemristorCheck;

        // 1. Calculate values for every single Contact
        foreach (var cell in ResultCells)
        {
            foreach (var subCell in cell.SubCells)
            {
                foreach (var contact in subCell.Contacts)
                {
                    contact.AggregatedValue = CalculateMetric(contact.CurveData, SelectedResultMetric);
                }
                // 2. Aggregate to SubCell
                subCell.RecalculateValue(useMaxAggregation);
            }
            // 3. Aggregate to Cell
            cell.RecalculateValue(useMaxAggregation);
        }

        // 4. Find global min / max on Cell level
        var validCells = ResultCells.Where(c => c.SubCells.Any() && !double.IsNaN(c.AggregatedValue)).ToList();
        if (!validCells.Any()) return;

        double minVal = validCells.Min(c => c.AggregatedValue);
        double maxVal = validCells.Max(c => c.AggregatedValue);

        double hueLow = GetHueForColor(SelectedHeatmapColorLow);
        double hueHigh = GetHueForColor(SelectedHeatmapColorHigh);

        // Apply colors to Cells
        foreach (var cell in ResultCells)
        {
            if (!cell.SubCells.Any())
            {
                cell.Color = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F8FAFC"));
            }
            else
            {
                cell.Color = HeatmapHelper.GetColorForValue(cell.AggregatedValue, minVal, maxVal, hueLow, hueHigh);
            }
        }

        // Apply colors to SubCells based on subcell min/max
        var allSubCells = ResultCells.SelectMany(c => c.SubCells).Where(s => !double.IsNaN(s.AggregatedValue)).ToList();
        if (allSubCells.Any())
        {
            double minSub = allSubCells.Min(s => s.AggregatedValue);
            double maxSub = allSubCells.Max(s => s.AggregatedValue);
            foreach (var cell in ResultCells)
            {
                foreach (var sub in cell.SubCells)
                {
                    sub.Color = HeatmapHelper.GetColorForValue(sub.AggregatedValue, minSub, maxSub, hueLow, hueHigh);
                    sub.MetricLabel = GetMetricLabel(SelectedResultMetric, sub.AggregatedValue);
                }
            }
        }

        // Apply colors and labels to Contacts
        var allContacts = ResultCells.SelectMany(c => c.SubCells).SelectMany(s => s.Contacts).Where(co => !double.IsNaN(co.AggregatedValue)).ToList();
        if (allContacts.Any())
        {
            double minContact = allContacts.Min(co => co.AggregatedValue);
            double maxContact = allContacts.Max(co => co.AggregatedValue);
            foreach (var cell in ResultCells)
            {
                foreach (var sub in cell.SubCells)
                {
                    foreach (var contact in sub.Contacts)
                    {
                        contact.Color = HeatmapHelper.GetColorForValue(contact.AggregatedValue, minContact, maxContact, hueLow, hueHigh);
                        contact.MetricLabel = GetMetricLabel(SelectedResultMetric, contact.AggregatedValue);
                    }
                }
            }
        }
        
        foreach (var cell in ResultCells)
        {
            cell.MetricLabel = GetMetricLabel(SelectedResultMetric, cell.AggregatedValue);
        }
    }

    private double CalculateMetric(List<CurvePoint> points, string metric)
    {
        if (points == null || points.Count == 0) return double.NaN;

        if (metric == "Average Resistance")
        {
            var validPoints = points.Where(p => Math.Abs(p.Current) > 1e-12).ToList(); // Ignore ~0 current
            if (!validPoints.Any()) return double.NaN;
            return validPoints.Average(p => Math.Abs(p.Voltage / p.Current));
        }
        else if (metric == "Max Current")
        {
            return points.Max(p => Math.Abs(p.Current));
        }
        else if (metric == "Max Voltage")
        {
            return points.Max(p => Math.Abs(p.Voltage));
        }
        else if (metric == "Gap At Voltage")
        {
            // Calculate absolute difference in Current at GapTargetVoltage for ascending and descending sweeps
            var targetV = GapTargetVoltage;
            
            // Collect crossings (interpolate I at V = targetV)
            List<double> crossedCurrents = new List<double>();
            
            for (int i = 0; i < points.Count - 1; i++)
            {
                var p1 = points[i];
                var p2 = points[i + 1];
                
                // Check if targetV is between p1.Voltage and p2.Voltage
                if ((p1.Voltage <= targetV && p2.Voltage >= targetV) ||
                    (p1.Voltage >= targetV && p2.Voltage <= targetV))
                {
                    // Avoid division by zero
                    if (Math.Abs(p2.Voltage - p1.Voltage) < 1e-12)
                    {
                        crossedCurrents.Add(p1.Current);
                    }
                    else
                    {
                        // Interpolate current
                        double fraction = (targetV - p1.Voltage) / (p2.Voltage - p1.Voltage);
                        double iInterp = p1.Current + fraction * (p2.Current - p1.Current);
                        crossedCurrents.Add(iInterp);
                    }
                }
            }
            
            if (crossedCurrents.Count >= 2)
            {
                // Typically we want the max difference if there are multiple crossings (e.g. multi-cycle)
                // Or just the difference between the first two crossings. Let's return the max difference among all crossings.
                double maxAbsI = crossedCurrents.Max(c => Math.Abs(c));
                double minAbsI = crossedCurrents.Min(c => Math.Abs(c));
                if (minAbsI < 1e-15) return double.NaN; // Avoid division by zero
                return maxAbsI / minAbsI;
            }
            return double.NaN;
        }
        else if (metric == "Memristor Check")
        {
            var result = MemristorCheckService.Calculate(points);
            return result.Score3;
        }

        return double.NaN;
    }

}
