using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SMU_Revamp.Models;

namespace SMU_Revamp.Services
{
    public sealed class MemristorSweepMeasurementPlan : IMeasurementPlan
    {
        public string Name => "Memristor Sweep";
        public string Description => "Performs a cyclic voltage sweep (0 -> Pos -> 0 -> Neg -> 0) multiple times, designed for memristor hysteresis loops.";
        public List<MeasurementParameter> Parameters { get; }
        public List<CurvePoint> ResultPoints { get; } = new();

        private string GetParamValueString(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsString() ?? string.Empty;
        private double GetParamValueDouble(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsDouble() ?? 0.0;
        private int GetParamValueInt(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsInt() ?? 0;

        public MemristorSweepMeasurementPlan()
        {
            Parameters = new List<MeasurementParameter>
            {
                new() { Name = "WriteChannel", DisplayName = "Write Channel:", Type = ParameterType.Text, Tooltip = "The SMU channel number (e.g. 2)", Section = "Channel Settings" },
                new() { Name = "ReadingChannel", DisplayName = "Reading Channel:", Type = ParameterType.Text, Tooltip = "The SMU channel to measure (e.g. 1 or 2)", Section = "Channel Settings" },
                new() { Name = "PositiveVoltage", DisplayName = "Positive Voltage (V):", Type = ParameterType.Number, Tooltip = "The maximum positive voltage of the sweep", Section = "Voltage Settings" },
                new() { Name = "NegativeVoltage", DisplayName = "Negative Voltage (V):", Type = ParameterType.Number, Tooltip = "The maximum negative voltage of the sweep", Section = "Voltage Settings" },
                new() { Name = "PointsPerSweep", DisplayName = "Points (per half-cycle):", Type = ParameterType.Number, Tooltip = "The number of sweep measurement points for a single direction", Section = "Sweep Settings" },
                new() { Name = "Cycles", DisplayName = "Cycles:", Type = ParameterType.Number, Tooltip = "Number of full loop cycles to perform", Section = "Sweep Settings" },
                new() { Name = "Compliance", DisplayName = "Compliance (A):", Type = ParameterType.Number, Tooltip = "The current compliance limit (in Amperes)", Section = "Voltage Settings" },
                new() { Name = "AdcSamples", DisplayName = "ADC Samples (PLC):", Type = ParameterType.Number, Tooltip = "Number of Power Line Cycles (PLC) for averaging", Section = "Measurement Settings" }
            };
            LoadDefaults();
        }

        public void LoadDefaults()
        {
            var config = ConfigurationService.Instance.GetConfig();
            foreach (var param in Parameters)
            {
                switch (param.Name)
                {
                    case "WriteChannel":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "WriteChannel", config.SweepChannel);
                        break;
                    case "ReadingChannel":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "ReadingChannel", config.SweepChannel);
                        break;
                    case "PositiveVoltage":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "PositiveVoltage", 1.0);
                        break;
                    case "NegativeVoltage":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "NegativeVoltage", -1.0);
                        break;
                    case "PointsPerSweep":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "PointsPerSweep", config.SweepPoints);
                        break;
                    case "Cycles":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "Cycles", 1);
                        break;
                    case "Compliance":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "Compliance", config.SweepCompliance);
                        break;
                    case "AdcSamples":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "AdcSamples", config.SweepAdcSamples);
                        break;
                }
            }
        }

        public List<List<CurvePoint>> CycleData { get; } = new();

        public IReadOnlyList<PlotSeries> PlotSeries
        {
            get
            {
                var series = new List<PlotSeries>();
                for (int i = 0; i < CycleData.Count; i++)
                {
                    series.Add(new PlotSeries($"Cycle {i + 1}", CycleData[i]));
                }
                return series;
            }
        }

        public async Task RunMeasurementAsync(E5263_SMU smu, IProgress<double>? progress = null)
        {
            ResultPoints.Clear();
            CycleData.Clear();
            progress?.Report(0);

            string channel = GetParamValueString("WriteChannel");
            string readingChannel = GetParamValueString("ReadingChannel");
            if (string.IsNullOrWhiteSpace(readingChannel)) readingChannel = channel;

            double posVol = GetParamValueDouble("PositiveVoltage");
            double negVol = GetParamValueDouble("NegativeVoltage");
            int pointsCount = GetParamValueInt("PointsPerSweep");
            int cycles = GetParamValueInt("Cycles");
            double compliance = GetParamValueDouble("Compliance");
            int adcSamples = GetParamValueInt("AdcSamples");

            if (cycles < 1) cycles = 1;

            await smu.SendCommandAsync("*RST");
            await smu.SendCommandAsync("FMT 1");
            await smu.SendCommandAsync("TSC 1");
            
            if (readingChannel != channel)
            {
                await smu.SendCommandAsync($"CN {channel},{readingChannel}");
            }
            else
            {
                await smu.SendCommandAsync($"CN {channel}");
            }
            
            await smu.SendCommandAsync($"AV -{adcSamples},0");
            progress?.Report(5);

            double plcTime = adcSamples * 0.02; // Assuming 50Hz, 1 PLC = 20ms
            int pointsPerDoubleSweep = pointsCount * 2;
            int totalExpectedPoints = pointsPerDoubleSweep * 2 * cycles; // 2 double sweeps per cycle
            double estimatedTotalSeconds = totalExpectedPoints * (plcTime + 0.01) + (cycles * 2.0); // 10ms extra + 1s overhead per sweep part
            if (estimatedTotalSeconds < 1.0) estimatedTotalSeconds = 1.0;
            
            using var cts = new CancellationTokenSource();
            var progressTask = Task.Run(async () =>
            {
                try
                {
                    double currentProgress = 5.0;
                    double targetProgress = 95.0;
                    double totalSteps = estimatedTotalSeconds * 10.0;
                    double stepIndex = 0;

                    while (!cts.Token.IsCancellationRequested && currentProgress < targetProgress)
                    {
                        progress?.Report(currentProgress);
                        await Task.Delay(100, cts.Token);
                        stepIndex++;
                        if (stepIndex < totalSteps)
                            currentProgress = 5.0 + (targetProgress - 5.0) * (stepIndex / totalSteps);
                        else
                            currentProgress += (98.0 - currentProgress) * 0.05;
                    }
                }
                catch (TaskCanceledException) { }
            });

            try
            {
                for (int c = 0; c < cycles; c++)
                {
                    var currentCycleData = new List<CurvePoint>();

                    // PART 1: 0 -> Positive -> 0
                    var parsedPos = await RunDoubleSweepAsync(smu, channel, readingChannel, 0, posVol, pointsCount, compliance);
                    currentCycleData.AddRange(parsedPos);
                    ResultPoints.AddRange(parsedPos);
                    
                    // PART 2: 0 -> Negative -> 0
                    var parsedNeg = await RunDoubleSweepAsync(smu, channel, readingChannel, 0, negVol, pointsCount, compliance);
                    currentCycleData.AddRange(parsedNeg);
                    ResultPoints.AddRange(parsedNeg);

                    CycleData.Add(currentCycleData);
                }

                cts.Cancel();
                try { await progressTask; } catch { }
                progress?.Report(98);

                var finalError = await smu.CheckErrorAsync();
                if (finalError != null)
                {
                    throw new InvalidOperationException($"SMU Error during sweep: {finalError}");
                }
                progress?.Report(100);
            }
            finally
            {
                cts.Cancel();
            }
        }

        private async Task<List<CurvePoint>> RunDoubleSweepAsync(E5263_SMU smu, string channel, string readingChannel, double start, double stop, int pointsCount, double compliance)
        {
            var wvCommand = System.FormattableString.Invariant($"WV {channel},3,0,{start},{stop},{pointsCount},{compliance}");
            await smu.SendCommandAsync(wvCommand);

            var wvError = await smu.CheckErrorAsync();
            if (wvError != null)
                throw new InvalidOperationException($"SMU rejected WV command: {wvError}");

            await smu.SendCommandAsync($"RI {readingChannel},0");
            await smu.SendCommandAsync($"MM 2,{readingChannel}");
            await smu.SendCommandAsync($"CMM {readingChannel},1");

            await smu.SendCommandAsync("TSR");
            await smu.SendCommandAsync("XE");
            await smu.SendCommandAsync("TSQ");

            int expectedBufferLength = pointsCount * 2 * 32 + 200;
            string rawData = await smu.ReadResponseAsync(expectedBufferLength);
            string tsqResponse = await smu.ReadResponseAsync(50); // Clear TSQ

            return ParseDoubleSweepData(rawData, start, stop, pointsCount, channel, readingChannel);
        }

        private List<CurvePoint> ParseDoubleSweepData(string rawData, double sweepStart, double sweepStop, int pointsCount, string channel, string readingChannel)
        {
            var points = new List<CurvePoint>();
            if (string.IsNullOrWhiteSpace(rawData)) return points;

            var items = rawData.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var parsedCurrents = new List<double>();
            
            foreach (var item in items)
            {
                var trimmed = item.Trim();
                if (trimmed.Length < 4) continue;

                char thirdChar = trimmed[2];
                string numStr = trimmed.Substring(3);

                if (thirdChar == 'I') // Current measurement
                {
                    if (double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double iVal))
                    {
                        parsedCurrents.Add(iVal);
                    }
                }
            }

            int count = parsedCurrents.Count;
            if (count == 0) return points;

            bool invertCurrent = readingChannel != channel;

            // Generate voltage points for double sweep
            int halfPoints = (count + 1) / 2;
            for (int i = 0; i < count; i++)
            {
                double v;
                if (i < halfPoints)
                {
                    v = sweepStart;
                    if (halfPoints > 1)
                        v = sweepStart + i * (sweepStop - sweepStart) / (halfPoints - 1);
                }
                else
                {
                    v = sweepStop;
                    if (halfPoints > 1)
                        v = sweepStop - (i - halfPoints) * (sweepStop - sweepStart) / (halfPoints - 1);
                }
                
                double current = invertCurrent ? -parsedCurrents[i] : parsedCurrents[i];
                points.Add(new CurvePoint(v, current));
            }

            return points;
        }

        public IReadOnlyList<string> GetCsvLines()
        {
            var lines = new List<string>();
            if (CycleData.Count == 0) return lines;

            // Generate header
            var headers = new List<string>();
            for (int i = 0; i < CycleData.Count; i++)
            {
                headers.Add($"Cycle {i + 1} Voltage (V)");
                headers.Add($"Cycle {i + 1} Current (A)");
            }
            lines.Add(string.Join(",", headers));

            // Find max length
            int maxPoints = 0;
            foreach (var cycle in CycleData)
            {
                if (cycle.Count > maxPoints) maxPoints = cycle.Count;
            }

            for (int row = 0; row < maxPoints; row++)
            {
                var rowValues = new List<string>();
                for (int c = 0; c < CycleData.Count; c++)
                {
                    var cycle = CycleData[c];
                    if (row < cycle.Count)
                    {
                        var pt = cycle[row];
                        rowValues.Add(System.FormattableString.Invariant($"{pt.X:E6}"));
                        rowValues.Add(System.FormattableString.Invariant($"{pt.Y:E6}"));
                    }
                    else
                    {
                        rowValues.Add("");
                        rowValues.Add("");
                    }
                }
                lines.Add(string.Join(",", rowValues));
            }

            return lines;
        }
    }
}
