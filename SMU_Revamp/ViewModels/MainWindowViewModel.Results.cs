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
            // Regex pattern: "Cell0104_R1C5_Contact3"
            var regex = new Regex(@"Cell(?<cR>\d{2})(?<cC>\d{2})_R(?<sR>\d)C(?<sC>\d)_Contact(?<cont>\d)");

            // Heavy disk I/O and CSV parsing run off the UI thread; the
            // observable collections are only touched afterwards on the
            // caller's (UI) context.
            var parsedFiles = await Task.Run(() =>
            {
                var records = new List<ParsedMeasurementRecord>();
                int matchedFiles = 0;

                foreach (var file in Directory.GetFiles(folderPath, "*.csv"))
                {
                    var record = ParseMeasurementCsvFile(file, regex);
                    if (record == null) continue;

                    matchedFiles++;
                    records.Add(record);
                }

                return (records, matchedFiles);
            });

            InitializeResultTab();

            int loadedCount = 0;
            bool filesFound = parsedFiles.matchedFiles > 0;

            foreach (var record in parsedFiles.records)
            {
                var cell = ResultCells.FirstOrDefault(c => c.Row == record.CellRow && c.Col == record.CellCol);
                if (cell == null) continue;

                var subCell = cell.SubCells.FirstOrDefault(s => s.Row == record.SubRow && s.Col == record.SubCol);
                if (subCell == null)
                {
                    subCell = new ResultSubCellViewModel { Row = record.SubRow, Col = record.SubCol };
                    cell.SubCells.Add(subCell);
                }

                var contactVm = subCell.Contacts.FirstOrDefault(c => c.ContactNumber == record.Contact);
                if (contactVm == null)
                {
                    contactVm = new ResultContactViewModel { ContactNumber = record.Contact };
                    subCell.Contacts.Add(contactVm);
                }

                contactVm.CurveData = record.Points;
                contactVm.Series = record.Series;
                contactVm.PlanName = record.PlanName;
                contactVm.XAxisLabel = record.XAxisLabel;
                contactVm.YAxisLabel = record.YAxisLabel;
                loadedCount++;
            }

            if (filesFound)
            {
                RecalculateResultMetrics();
                IsResultFolderLoaded = true;
                NotificationRequested?.Invoke("Success", $"Loaded {loadedCount} measurements.", null);
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
                contactVm.PlanName = meas.PlanName;

                if (string.Equals(meas.PlanName, "PotDep", StringComparison.OrdinalIgnoreCase))
                {
                    contactVm.XAxisLabel = "Cycle";
                    contactVm.YAxisLabel = "Read Current (A)";
                    contactVm.Series = new List<PlotSeries> { new PlotSeries("PotDep", dbData.Points) };
                }
                else if (string.Equals(meas.PlanName, "Frequency Memory", StringComparison.OrdinalIgnoreCase))
                {
                    contactVm.XAxisLabel = "Inter-spike interval (ms)";
                    contactVm.YAxisLabel = "Mean Read Current (A)";
                    contactVm.Series = new List<PlotSeries> { new PlotSeries("Frequency Memory", dbData.Points) };
                }
                else if (string.Equals(meas.PlanName, "Spike Timing", StringComparison.OrdinalIgnoreCase))
                {
                    contactVm.XAxisLabel = "Delay after Last Spike End (ms)";
                    contactVm.YAxisLabel = "Read Current (A)";
                    contactVm.Series = new List<PlotSeries> { new PlotSeries("Spike Timing", dbData.Points) };
                }
                else if (string.Equals(meas.PlanName, "Memristor Sweep", StringComparison.OrdinalIgnoreCase))
                {
                    contactVm.XAxisLabel = "Voltage (V)";
                    contactVm.YAxisLabel = "Current (A)";

                    if (dbData.Parameters.TryGetValue("Points", out var ptsStr) &&
                        int.TryParse(ptsStr, out int ptsCount) && ptsCount > 0)
                    {
                        int pointsPerCycle = ptsCount * 4;
                        if (pointsPerCycle > 0 && dbData.Points.Count >= pointsPerCycle)
                        {
                            var reconstructedSeries = new List<PlotSeries>();
                            int cycleNum = 1;
                            for (int i = 0; i < dbData.Points.Count; i += pointsPerCycle)
                            {
                                var cyclePoints = dbData.Points.Skip(i).Take(pointsPerCycle).ToList();
                                reconstructedSeries.Add(new PlotSeries($"Cycle {cycleNum++}", cyclePoints));
                            }
                            contactVm.Series = reconstructedSeries;
                        }
                        else
                        {
                            contactVm.Series = new List<PlotSeries> { new PlotSeries("Memristor Sweep", dbData.Points) };
                        }
                    }
                    else
                    {
                        contactVm.Series = new List<PlotSeries> { new PlotSeries("Memristor Sweep", dbData.Points) };
                    }
                }
                else
                {
                    contactVm.XAxisLabel = "Voltage (V)";
                    contactVm.YAxisLabel = "Current (A)";
                    contactVm.Series = new List<PlotSeries> { new PlotSeries(string.IsNullOrEmpty(meas.PlanName) ? "Data" : meas.PlanName, dbData.Points) };
                }
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

    public void NavigateResultSubCell(int dRow, int dCol)
    {
        if (!IsResultFolderLoaded) return;

        if (SelectedResultCell == null)
        {
            SelectedResultCell = ResultCells.FirstOrDefault(c => c.IsValid && c.SubCells.Any()) 
                              ?? ResultCells.FirstOrDefault(c => c.IsValid);
        }

        if (SelectedResultCell == null || SelectedResultCell.SubCells.Count == 0) return;

        if (SelectedResultSubCell == null)
        {
            SelectedResultSubCell = SelectedResultCell.SubCells.FirstOrDefault(s => s.IsValid) 
                                 ?? SelectedResultCell.SubCells.FirstOrDefault();
            return;
        }

        int targetRow = SelectedResultSubCell.Row + dRow;
        int targetCol = SelectedResultSubCell.Col + dCol;

        // SubCell grid is 5x5 (rows 1..5, cols 1..5)
        while (targetRow >= 1 && targetRow <= 5 && targetCol >= 1 && targetCol <= 5)
        {
            var candidate = SelectedResultCell.SubCells.FirstOrDefault(s => s.Row == targetRow && s.Col == targetCol && s.IsValid);
            if (candidate != null)
            {
                SelectedResultSubCell = candidate;
                return;
            }
            targetRow += dRow;
            targetCol += dCol;
        }
    }

    public void NavigateResultWaferCell(int dRow, int dCol)
    {
        if (!IsResultFolderLoaded) return;

        if (SelectedResultCell == null)
        {
            SelectedResultCell = ResultCells.FirstOrDefault(c => c.IsValid && c.SubCells.Any()) 
                              ?? ResultCells.FirstOrDefault(c => c.IsValid);
            return;
        }

        int targetRow = SelectedResultCell.Row + dRow;
        int targetCol = SelectedResultCell.Col + dCol;

        // Wafer grid is 16x16 (rows 1..16, cols 1..16)
        while (targetRow >= 1 && targetRow <= 16 && targetCol >= 1 && targetCol <= 16)
        {
            var candidate = ResultCells.FirstOrDefault(c => c.Row == targetRow && c.Col == targetCol && c.IsValid);
            if (candidate != null)
            {
                SelectedResultCell = candidate;
                return;
            }
            targetRow += dRow;
            targetCol += dCol;
        }
    }

    public void NavigateResultContact(int delta)
    {
        if (!IsResultFolderLoaded) return;
        if (SelectedResultSubCell == null || SelectedResultSubCell.Contacts.Count == 0) return;

        var contacts = SelectedResultSubCell.Contacts;
        int currentIndex = SelectedResultContact != null ? contacts.IndexOf(SelectedResultContact) : -1;

        if (currentIndex == -1)
        {
            SelectedResultContact = contacts.FirstOrDefault();
            return;
        }

        int nextIndex = Math.Clamp(currentIndex + delta, 0, contacts.Count - 1);
        SelectedResultContact = contacts[nextIndex];
    }

    private ParsedMeasurementRecord? ParseMeasurementCsvFile(string filePath, Regex regex)
    {
        var filename = Path.GetFileName(filePath);
        var match = regex.Match(filename);
        if (!match.Success) return null;

        var lines = File.ReadAllLines(filePath);
        string planName = string.Empty;
        char separator = '\t';
        bool hasSeparator = false;
        string? headerLine = null;
        var dataLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (trimmed.StartsWith("sep=", StringComparison.OrdinalIgnoreCase))
            {
                var sepStr = trimmed.Substring(4).Trim();
                if (sepStr.Length > 0)
                {
                    separator = sepStr[0];
                    hasSeparator = true;
                }
                continue;
            }

            if (trimmed.StartsWith("#"))
            {
                var meta = trimmed.Substring(1).Trim();
                if (meta.StartsWith("Plan\t", StringComparison.OrdinalIgnoreCase) || meta.StartsWith("Plan,", StringComparison.OrdinalIgnoreCase) || meta.StartsWith("Plan:", StringComparison.OrdinalIgnoreCase))
                {
                    planName = meta.Substring(5).Trim();
                }
                continue;
            }

            if (headerLine == null)
            {
                headerLine = trimmed;
            }
            else
            {
                dataLines.Add(trimmed);
            }
        }

        if (headerLine == null) return null;

        if (!hasSeparator)
        {
            if (headerLine.Contains('\t')) separator = '\t';
            else if (headerLine.Contains(';')) separator = ';';
            else if (headerLine.Contains(',')) separator = ',';
        }

        var headers = headerLine.Split(separator).Select(h => h.Trim().Trim('"')).ToList();
        var points = new List<CurvePoint>();
        var series = new List<PlotSeries>();
        string xLabel = "Voltage (V)";
        string yLabel = "Current (A)";

        // Check for multi-cycle format (e.g. Cycle 1 Voltage, Cycle 1 Current, Cycle 2 Voltage, Cycle 2 Current...)
        if (headers.Count >= 4 && headers.Any(h => h.StartsWith("Cycle 1", StringComparison.OrdinalIgnoreCase) || h.Contains("Cycle 1 Voltage")))
        {
            int cycleCount = headers.Count / 2;
            var cycleLists = new List<List<CurvePoint>>();
            for (int c = 0; c < cycleCount; c++) cycleLists.Add(new List<CurvePoint>());

            foreach (var line in dataLines)
            {
                var parts = line.Split(separator);
                for (int c = 0; c < cycleCount; c++)
                {
                    int vIdx = c * 2;
                    int iIdx = c * 2 + 1;
                    if (vIdx < parts.Length && iIdx < parts.Length)
                    {
                        if (SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[vIdx], out double vVal) &&
                            SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[iIdx], out double iVal))
                        {
                            cycleLists[c].Add(new CurvePoint(vVal, iVal));
                        }
                    }
                }
            }

            for (int c = 0; c < cycleCount; c++)
            {
                if (cycleLists[c].Count > 0)
                {
                    series.Add(new PlotSeries($"Cycle {c + 1}", cycleLists[c]));
                    points.AddRange(cycleLists[c]);
                }
            }
            if (string.IsNullOrEmpty(planName)) planName = "Memristor Sweep";
        }
        else if (headers.Count >= 2 && (headers[0].Equals("Cycle", StringComparison.OrdinalIgnoreCase) || planName.Equals("PotDep", StringComparison.OrdinalIgnoreCase)))
        {
            xLabel = "Cycle";
            yLabel = "Read Current (A)";
            if (string.IsNullOrEmpty(planName)) planName = "PotDep";

            foreach (var line in dataLines)
            {
                var parts = line.Split(separator);
                if (parts.Length >= 2 &&
                    SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[0], out double cycleVal) &&
                    SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[1], out double iVal))
                {
                    points.Add(new CurvePoint(cycleVal, iVal));
                }
            }
            if (points.Count > 0)
            {
                series.Add(new PlotSeries(planName, points));
            }
        }
        else
        {
            // Standard 2-column or generic format
            if (headers.Count > 0 && !string.IsNullOrWhiteSpace(headers[0])) xLabel = headers[0];
            if (headers.Count > 1 && !string.IsNullOrWhiteSpace(headers[1])) yLabel = headers[1];

            foreach (var line in dataLines)
            {
                var parts = line.Split(separator);
                if (parts.Length >= 2 &&
                    SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[0], out double xVal) &&
                    SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[1], out double yVal))
                {
                    points.Add(new CurvePoint(xVal, yVal));
                }
            }
            if (points.Count > 0)
            {
                series.Add(new PlotSeries(string.IsNullOrEmpty(planName) ? "Data" : planName, points));
            }
        }

        if (points.Count == 0) return null;

        return new ParsedMeasurementRecord(
            int.Parse(match.Groups["cR"].Value),
            int.Parse(match.Groups["cC"].Value),
            int.Parse(match.Groups["sR"].Value),
            int.Parse(match.Groups["sC"].Value),
            int.Parse(match.Groups["cont"].Value),
            points,
            series,
            planName,
            xLabel,
            yLabel);
    }

}

public record ParsedMeasurementRecord(
    int CellRow,
    int CellCol,
    int SubRow,
    int SubCol,
    int Contact,
    List<CurvePoint> Points,
    List<PlotSeries> Series,
    string PlanName,
    string XAxisLabel,
    string YAxisLabel);

