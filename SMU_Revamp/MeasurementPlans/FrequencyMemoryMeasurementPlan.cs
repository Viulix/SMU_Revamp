using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SMU_Revamp.Interfaces;
using SMU_Revamp.Models;
using SMU_Revamp.Services;

namespace SMU_Revamp.MeasurementPlans
{
    /// <summary>
    /// Measures whether the delayed readout state can reconstruct the frequency of a preceding spike train.
    /// The only intended independent variable is the inter-spike interval, defined as the pause between
    /// the end of one input spike and the start of the next input spike.
    ///
    /// Plot output: one mean point per inter-spike interval with vertical error bars representing the
    /// sample standard deviation over repetitions.
    /// CSV output: one row per individual trial, preserving all raw repetition data.
    /// </summary>
    public sealed class FrequencyMemoryMeasurementPlan : IMeasurementPlan
    {
        public string Name => "Frequency Memory";
        public string Description => "Applies fixed spike trains with linearly varied inter-spike interval, reads after a fixed delay, and resets with a negative I-V sweep.";

        public string PlotTitle => "Frequency Memory Response";
        public string XAxisLabel => "Inter-spike interval (ms)";
        public string YAxisLabel => GetParamValueBool("BaselineReadEnabled") ? "Mean Δ Read Current (A)" : "Mean Read Current (A)";
        public bool ShowLogPlot => false;
        public double PlotAspectRatio => 1.777;

        public List<MeasurementParameter> Parameters { get; }
        public List<CurvePoint> ResultPoints { get; } = new();
        private List<FrequencyMemoryTrialResult> TrialResults { get; } = new();

        public IReadOnlyList<PlotSeries> PlotSeries
        {
            get
            {
                var points = ResultPoints.ToList();
                return points.Count > 0
                    ? new List<PlotSeries> { new PlotSeries("Mean ± SD", points) }
                    : new List<PlotSeries>();
            }
        }

        private string GetParamValueString(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsString() ?? string.Empty;
        private double GetParamValueDouble(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsDouble() ?? 0.0;
        private int GetParamValueInt(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsInt() ?? 0;
        private bool GetParamValueBool(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsBool() ?? false;

        public FrequencyMemoryMeasurementPlan()
        {
            Parameters = new List<MeasurementParameter>
            {
                new() { Name = "WriteChannel", DisplayName = "Write Channel:", Type = ParameterType.Text, Tooltip = "SMU channel used to force the input/read/reset voltages.", Section = "Channel Settings" },
                new() { Name = "ReadingChannel", DisplayName = "Reading Channel:", Type = ParameterType.Text, Tooltip = "SMU channel used for current readout. Leave equal to write channel for same-channel readout.", Section = "Channel Settings" },

                new() { Name = "InputSpikeVoltage", DisplayName = "Input Spike Voltage (V):", Type = ParameterType.Number, Tooltip = "Voltage amplitude of each input spike.", Section = "Input Spike Train" },
                new() { Name = "InputSpikeLengthMs", DisplayName = "Input Spike Length (ms):", Type = ParameterType.Number, Tooltip = "Duration of each input spike.", Section = "Input Spike Train" },
                new() { Name = "InputSpikeCount", DisplayName = "Number of Input Spikes:", Type = ParameterType.Number, Tooltip = "Number of identical input spikes in each train.", Section = "Input Spike Train" },
                new() { Name = "MinInterSpikeIntervalMs", DisplayName = "Min. Inter-spike Interval (ms):", Type = ParameterType.Number, Tooltip = "Shortest pause between end of one input spike and start of the next.", Section = "Input Spike Train" },
                new() { Name = "MaxInterSpikeIntervalMs", DisplayName = "Max. Inter-spike Interval (ms):", Type = ParameterType.Number, Tooltip = "Longest pause between end of one input spike and start of the next.", Section = "Input Spike Train" },
                new() { Name = "IntervalValueCount", DisplayName = "Number of Interval Values:", Type = ParameterType.Number, Tooltip = "Number of linearly spaced inter-spike-interval values.", Section = "Input Spike Train" },

                new() { Name = "DelayBeforeMeasurementMs", DisplayName = "Delay Before Measurement (ms):", Type = ParameterType.Number, Tooltip = "Delay from the end of the last input spike to the readout pulse.", Section = "Readout" },
                new() { Name = "ReadoutVoltage", DisplayName = "Readout Spike Voltage (V):", Type = ParameterType.Number, Tooltip = "Small non-switching readout voltage.", Section = "Readout" },
                new() { Name = "ReadoutLengthMs", DisplayName = "Readout Spike Length (ms):", Type = ParameterType.Number, Tooltip = "Duration of the readout pulse before measuring current.", Section = "Readout" },
                new() { Name = "BaselineReadEnabled", DisplayName = "Baseline Read Enabled:", Type = ParameterType.Checkbox, Tooltip = "If enabled, measure a baseline current after reset and before the input spike train.", Section = "Readout" },

                new() { Name = "ResetSweepMinimum", DisplayName = "Reset I-V Sweep Minimum (V):", Type = ParameterType.Number, Tooltip = "Most negative voltage reached by the reset sweep. The reset sweep goes 0 → minimum → 0.", Section = "Reset" },

                new() { Name = "RepetitionsPerInterval", DisplayName = "Repetitions per Interval:", Type = ParameterType.Number, Tooltip = "Number of repetitions for every inter-spike-interval value.", Section = "Repetition" },

                new() { Name = "Compliance", DisplayName = "Compliance (A):", Type = ParameterType.Number, Tooltip = "Current compliance used for input, readout, and reset commands.", Section = "Advanced / Safety" }
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
                    case "InputSpikeVoltage":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "InputSpikeVoltage", 1.0);
                        break;
                    case "InputSpikeLengthMs":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "InputSpikeLengthMs", 10.0);
                        break;
                    case "InputSpikeCount":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "InputSpikeCount", 10);
                        break;
                    case "MinInterSpikeIntervalMs":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "MinInterSpikeIntervalMs", 0.0);
                        break;
                    case "MaxInterSpikeIntervalMs":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "MaxInterSpikeIntervalMs", 200.0);
                        break;
                    case "IntervalValueCount":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "IntervalValueCount", 11);
                        break;
                    case "DelayBeforeMeasurementMs":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "DelayBeforeMeasurementMs", 1000.0);
                        break;
                    case "ReadoutVoltage":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "ReadoutVoltage", 0.3);
                        break;
                    case "ReadoutLengthMs":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "ReadoutLengthMs", 20.0);
                        break;
                    case "BaselineReadEnabled":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "BaselineReadEnabled", true);
                        break;
                    case "ResetSweepMinimum":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "ResetSweepMinimum", -1.0);
                        break;
                    case "RepetitionsPerInterval":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "RepetitionsPerInterval", 3);
                        break;
                    case "Compliance":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "Compliance", config.SweepCompliance);
                        break;
                }
            }
        }

        public async Task RunMeasurementAsync(E5263_SMU smu, IProgress<double>? progress = null)
        {
            ResultPoints.Clear();
            TrialResults.Clear();
            progress?.Report(0);

            var settings = ReadAndValidateSettings();
            var intervals = BuildLinearIntervals(settings.MinInterSpikeIntervalMs, settings.MaxInterSpikeIntervalMs, settings.IntervalValueCount);
            int totalTrials = intervals.Count * settings.RepetitionsPerInterval;
            int completedTrials = 0;
            int trialIndex = 0;

            await InitializeSmuAsync(smu, settings);
            await PerformResetSweepAsync(smu, settings);

            try
            {
                for (int rep = 1; rep <= settings.RepetitionsPerInterval; rep++)
                {
                    for (int intervalIndex = 0; intervalIndex < intervals.Count; intervalIndex++)
                    {
                        double intervalMs = intervals[intervalIndex];
                        trialIndex++;
                        double trialStartProgress = 100.0 * completedTrials / Math.Max(1, totalTrials);
                        double trialProgressSpan = 100.0 / Math.Max(1, totalTrials);

                        progress?.Report(trialStartProgress + 0.05 * trialProgressSpan);

                        double? baselineCurrent = null;
                        if (settings.BaselineReadEnabled)
                        {
                            baselineCurrent = await ReadCurrentPulseAsync(smu, settings, settings.ReadoutVoltage, settings.ReadoutLengthMs);
                        }

                        progress?.Report(trialStartProgress + 0.15 * trialProgressSpan);

                        await ApplyInputSpikeTrainAsync(smu, settings, intervalMs, p =>
                        {
                            progress?.Report(trialStartProgress + (0.15 + 0.45 * p) * trialProgressSpan);
                        });

                        await Task.Delay(ToDelayMilliseconds(settings.DelayBeforeMeasurementMs));
                        progress?.Report(trialStartProgress + 0.72 * trialProgressSpan);

                        double readCurrent = await ReadCurrentPulseAsync(smu, settings, settings.ReadoutVoltage, settings.ReadoutLengthMs);
                        double deltaCurrent = baselineCurrent.HasValue ? readCurrent - baselineCurrent.Value : readCurrent;
                        double normalizedDelta = baselineCurrent.HasValue && Math.Abs(baselineCurrent.Value) > 1e-30
                            ? deltaCurrent / Math.Abs(baselineCurrent.Value)
                            : double.NaN;

                        var result = new FrequencyMemoryTrialResult(
                            TrialIndex: trialIndex,
                            RepetitionIndex: rep,
                            IntervalIndex: intervalIndex + 1,
                            InterSpikeIntervalMs: intervalMs,
                            StartToStartPeriodMs: settings.InputSpikeLengthMs + intervalMs,
                            InputFrequencyHz: 1000.0 / Math.Max(1e-12, settings.InputSpikeLengthMs + intervalMs),
                            TrainDurationMs: CalculateTrainDurationMs(settings.InputSpikeCount, settings.InputSpikeLengthMs, intervalMs),
                            InputSpikeVoltage: settings.InputSpikeVoltage,
                            InputSpikeLengthMs: settings.InputSpikeLengthMs,
                            InputSpikeCount: settings.InputSpikeCount,
                            DelayBeforeMeasurementMs: settings.DelayBeforeMeasurementMs,
                            ActualReadDelayMs: settings.DelayBeforeMeasurementMs,
                            ReadoutVoltage: settings.ReadoutVoltage,
                            ReadoutLengthMs: settings.ReadoutLengthMs,
                            BaselineReadEnabled: settings.BaselineReadEnabled,
                            BaselineCurrentA: baselineCurrent,
                            ReadCurrentA: readCurrent,
                            DeltaCurrentA: deltaCurrent,
                            NormalizedDelta: normalizedDelta,
                            ResetSweepMinimum: settings.ResetSweepMinimum,
                            Compliance: settings.Compliance
                        );

                        TrialResults.Add(result);
                        RebuildMeanPlotPoints(settings.BaselineReadEnabled);
                        progress?.Report(trialStartProgress + 0.80 * trialProgressSpan);

                        await PerformResetSweepAsync(smu, settings);

                        completedTrials++;
                        progress?.Report(100.0 * completedTrials / Math.Max(1, totalTrials));
                    }
                }
            }
            finally
            {
                try { await smu.SendCommandAsync("DZ"); } catch { }
            }

            RebuildMeanPlotPoints(settings.BaselineReadEnabled);
            progress?.Report(100);
        }

        public IReadOnlyList<string> GetCsvLines() => GetDetailedCsvLines();

        public List<string> GetDetailedCsvLines()
        {
            var lines = new List<string>
            {
                "sep=\t",
                "TrialIndex\tRepetitionIndex\tIntervalIndex\tInterSpikeInterval_ms\tStartToStartPeriod_ms\tInputFrequency_Hz\tTrainDuration_ms\tInputSpikeVoltage_V\tInputSpikeLength_ms\tInputSpikeCount\tDelayBeforeMeasurement_ms\tActualReadDelay_ms\tReadoutVoltage_V\tReadoutLength_ms\tBaselineReadEnabled\tBaselineCurrent_A\tReadCurrent_A\tDeltaCurrent_A\tNormalizedDelta\tResetSweepMinimum_V\tCompliance_A"
            };

            foreach (var r in TrialResults.OrderBy(t => t.TrialIndex))
            {
                lines.Add(string.Join("\t", new[]
                {
                    r.TrialIndex.ToString(CultureInfo.InvariantCulture),
                    r.RepetitionIndex.ToString(CultureInfo.InvariantCulture),
                    r.IntervalIndex.ToString(CultureInfo.InvariantCulture),
                    r.InterSpikeIntervalMs.ToString("G9", CultureInfo.InvariantCulture),
                    r.StartToStartPeriodMs.ToString("G9", CultureInfo.InvariantCulture),
                    r.InputFrequencyHz.ToString("G9", CultureInfo.InvariantCulture),
                    r.TrainDurationMs.ToString("G9", CultureInfo.InvariantCulture),
                    r.InputSpikeVoltage.ToString("G9", CultureInfo.InvariantCulture),
                    r.InputSpikeLengthMs.ToString("G9", CultureInfo.InvariantCulture),
                    r.InputSpikeCount.ToString(CultureInfo.InvariantCulture),
                    r.DelayBeforeMeasurementMs.ToString("G9", CultureInfo.InvariantCulture),
                    r.ActualReadDelayMs.ToString("G9", CultureInfo.InvariantCulture),
                    r.ReadoutVoltage.ToString("G9", CultureInfo.InvariantCulture),
                    r.ReadoutLengthMs.ToString("G9", CultureInfo.InvariantCulture),
                    r.BaselineReadEnabled ? "true" : "false",
                    r.BaselineCurrentA.HasValue ? r.BaselineCurrentA.Value.ToString("E9", CultureInfo.InvariantCulture) : string.Empty,
                    r.ReadCurrentA.ToString("E9", CultureInfo.InvariantCulture),
                    r.DeltaCurrentA.ToString("E9", CultureInfo.InvariantCulture),
                    double.IsNaN(r.NormalizedDelta) ? string.Empty : r.NormalizedDelta.ToString("E9", CultureInfo.InvariantCulture),
                    r.ResetSweepMinimum.ToString("G9", CultureInfo.InvariantCulture),
                    r.Compliance.ToString("G9", CultureInfo.InvariantCulture)
                }));
            }

            lines.Add(string.Empty);
            lines.Add("# Summary: mean points plotted in the viewer. YError is sample standard deviation over repetitions.");
            lines.Add("# Summary columns: InterSpikeInterval_ms\tMeanY_A\tStdDevY_A\tN");
            foreach (var p in ResultPoints.OrderBy(p => p.X))
            {
                int n = TrialResults.Count(r => Math.Abs(r.InterSpikeIntervalMs - p.X) < 1e-9);
                lines.Add(FormattableString.Invariant($"# {p.X:G9}\t{p.Y:E9}\t{(p.YError ?? 0.0):E9}\t{n}"));
            }

            return lines;
        }

        public void LoadFromCsvLines(IReadOnlyList<string> lines)
        {
            TrialResults.Clear();
            ResultPoints.Clear();

            char separator = '\t';
            string? headerLine = null;
            var dataLines = new List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                if (trimmed.StartsWith("sep=", StringComparison.OrdinalIgnoreCase))
                {
                    var sep = trimmed.Substring(4);
                    if (sep.Length > 0) separator = sep[0];
                    continue;
                }

                if (trimmed.StartsWith("#")) continue;

                if (headerLine == null)
                {
                    headerLine = trimmed;
                }
                else
                {
                    dataLines.Add(trimmed);
                }
            }

            if (headerLine == null) return;

            if (headerLine.Contains('\t')) separator = '\t';
            else if (headerLine.Contains(';')) separator = ';';
            else if (headerLine.Contains(',')) separator = ',';

            var headers = headerLine.Split(separator).Select(h => h.Trim().Trim('"')).ToList();
            int Idx(string name) => headers.FindIndex(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));

            int idxTrial = Idx("TrialIndex");
            int idxRep = Idx("RepetitionIndex");
            int idxInterval = Idx("IntervalIndex");
            int idxIsi = Idx("InterSpikeInterval_ms");
            int idxPeriod = Idx("StartToStartPeriod_ms");
            int idxFreq = Idx("InputFrequency_Hz");
            int idxTrain = Idx("TrainDuration_ms");
            int idxVSpike = Idx("InputSpikeVoltage_V");
            int idxTSpike = Idx("InputSpikeLength_ms");
            int idxSpikeCount = Idx("InputSpikeCount");
            int idxDelay = Idx("DelayBeforeMeasurement_ms");
            int idxActualDelay = Idx("ActualReadDelay_ms");
            int idxVRead = Idx("ReadoutVoltage_V");
            int idxTRead = Idx("ReadoutLength_ms");
            int idxBaselineEnabled = Idx("BaselineReadEnabled");
            int idxBaseline = Idx("BaselineCurrent_A");
            int idxRead = Idx("ReadCurrent_A");
            int idxDelta = Idx("DeltaCurrent_A");
            int idxNorm = Idx("NormalizedDelta");
            int idxReset = Idx("ResetSweepMinimum_V");
            int idxComp = Idx("Compliance_A");

            if (idxIsi < 0 || idxRead < 0) return;

            foreach (var line in dataLines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                var parts = line.Split(separator).Select(p => p.Trim()).ToArray();
                if (parts.Length <= Math.Max(idxIsi, idxRead)) continue;

                double GetDouble(int idx, double fallback = 0.0) => idx >= 0 && idx < parts.Length && ParameterConfigHelper.TryParseDoubleRobust(parts[idx], out var value) ? value : fallback;
                int GetInt(int idx, int fallback = 0) => idx >= 0 && idx < parts.Length && int.TryParse(parts[idx], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
                bool GetBool(int idx, bool fallback = false) => idx >= 0 && idx < parts.Length && bool.TryParse(parts[idx], out var value) ? value : fallback;
                double? GetNullableDouble(int idx) => idx >= 0 && idx < parts.Length && ParameterConfigHelper.TryParseDoubleRobust(parts[idx], out var value) ? value : null;

                var baselineEnabled = GetBool(idxBaselineEnabled, idxBaseline >= 0 && !string.IsNullOrWhiteSpace(parts.ElementAtOrDefault(idxBaseline)));
                var baseline = GetNullableDouble(idxBaseline);
                var read = GetDouble(idxRead);
                var delta = idxDelta >= 0 ? GetDouble(idxDelta, baseline.HasValue ? read - baseline.Value : read) : (baseline.HasValue ? read - baseline.Value : read);

                TrialResults.Add(new FrequencyMemoryTrialResult(
                    TrialIndex: GetInt(idxTrial, TrialResults.Count + 1),
                    RepetitionIndex: GetInt(idxRep, 1),
                    IntervalIndex: GetInt(idxInterval, 1),
                    InterSpikeIntervalMs: GetDouble(idxIsi),
                    StartToStartPeriodMs: GetDouble(idxPeriod),
                    InputFrequencyHz: GetDouble(idxFreq),
                    TrainDurationMs: GetDouble(idxTrain),
                    InputSpikeVoltage: GetDouble(idxVSpike),
                    InputSpikeLengthMs: GetDouble(idxTSpike),
                    InputSpikeCount: GetInt(idxSpikeCount),
                    DelayBeforeMeasurementMs: GetDouble(idxDelay),
                    ActualReadDelayMs: GetDouble(idxActualDelay),
                    ReadoutVoltage: GetDouble(idxVRead),
                    ReadoutLengthMs: GetDouble(idxTRead),
                    BaselineReadEnabled: baselineEnabled,
                    BaselineCurrentA: baseline,
                    ReadCurrentA: read,
                    DeltaCurrentA: delta,
                    NormalizedDelta: GetDouble(idxNorm, double.NaN),
                    ResetSweepMinimum: GetDouble(idxReset),
                    Compliance: GetDouble(idxComp)
                ));
            }

            bool useDelta = TrialResults.Any(t => t.BaselineReadEnabled);
            RebuildMeanPlotPoints(useDelta);
        }

        private FrequencyMemorySettings ReadAndValidateSettings()
        {
            string writeChannel = NormalizeSingleChannel(GetParamValueString("WriteChannel"), "Write Channel");
            string readingChannelRaw = GetParamValueString("ReadingChannel");
            string readingChannel = string.IsNullOrWhiteSpace(readingChannelRaw)
                ? writeChannel
                : NormalizeSingleChannel(readingChannelRaw, "Reading Channel");

            var settings = new FrequencyMemorySettings
            {
                WriteChannel = writeChannel,
                ReadingChannel = readingChannel,
                InvertCurrent = readingChannel != writeChannel,
                InputSpikeVoltage = GetParamValueDouble("InputSpikeVoltage"),
                InputSpikeLengthMs = GetParamValueDouble("InputSpikeLengthMs"),
                InputSpikeCount = GetParamValueInt("InputSpikeCount"),
                MinInterSpikeIntervalMs = GetParamValueDouble("MinInterSpikeIntervalMs"),
                MaxInterSpikeIntervalMs = GetParamValueDouble("MaxInterSpikeIntervalMs"),
                IntervalValueCount = GetParamValueInt("IntervalValueCount"),
                DelayBeforeMeasurementMs = GetParamValueDouble("DelayBeforeMeasurementMs"),
                ReadoutVoltage = GetParamValueDouble("ReadoutVoltage"),
                ReadoutLengthMs = GetParamValueDouble("ReadoutLengthMs"),
                BaselineReadEnabled = GetParamValueBool("BaselineReadEnabled"),
                ResetSweepMinimum = GetParamValueDouble("ResetSweepMinimum"),
                RepetitionsPerInterval = GetParamValueInt("RepetitionsPerInterval"),
                Compliance = GetParamValueDouble("Compliance")
            };

            if (settings.InputSpikeLengthMs <= 0) throw new InvalidOperationException("Input Spike Length must be > 0 ms.");
            if (settings.InputSpikeCount < 1) throw new InvalidOperationException("Number of Input Spikes must be at least 1.");
            if (settings.MinInterSpikeIntervalMs < 0) throw new InvalidOperationException("Minimum Inter-spike Interval must be >= 0 ms.");
            if (settings.MaxInterSpikeIntervalMs < settings.MinInterSpikeIntervalMs) throw new InvalidOperationException("Maximum Inter-spike Interval must be >= minimum interval.");
            if (settings.IntervalValueCount < 1) throw new InvalidOperationException("Number of Interval Values must be at least 1.");
            if (settings.DelayBeforeMeasurementMs < 0) throw new InvalidOperationException("Delay Before Measurement must be >= 0 ms.");
            if (settings.ReadoutLengthMs <= 0) throw new InvalidOperationException("Readout Spike Length must be > 0 ms.");
            if (settings.ResetSweepMinimum >= 0) throw new InvalidOperationException("Reset I-V Sweep Minimum should be negative.");
            if (settings.RepetitionsPerInterval < 1) throw new InvalidOperationException("Repetitions per Interval must be at least 1.");
            if (settings.Compliance <= 0) throw new InvalidOperationException("Compliance must be > 0 A.");

            return settings;
        }

        private static List<double> BuildLinearIntervals(double minMs, double maxMs, int count)
        {
            if (count <= 1) return new List<double> { minMs };
            var values = new List<double>();
            double step = (maxMs - minMs) / (count - 1);
            for (int i = 0; i < count; i++)
            {
                values.Add(minMs + i * step);
            }
            return values;
        }

        private async Task InitializeSmuAsync(E5263_SMU smu, FrequencyMemorySettings s)
        {
            await smu.SendCommandAsync("*RST");
            await smu.SendCommandAsync("FMT 1,0");
            await smu.SendCommandAsync("TSC 1");

            if (s.ReadingChannel != s.WriteChannel)
            {
                await smu.SendCommandAsync($"CN {s.WriteChannel},{s.ReadingChannel}");
            }
            else
            {
                await smu.SendCommandAsync($"CN {s.WriteChannel}");
            }

            await smu.SendCommandAsync("AV -1,0");
            await ConfigurePointMeasurementModeAsync(smu, s);

            var error = await smu.CheckErrorAsync();
            if (error != null)
            {
                throw new InvalidOperationException($"SMU rejected initialization command: {error}");
            }
        }

        private static async Task ConfigurePointMeasurementModeAsync(E5263_SMU smu, FrequencyMemorySettings s)
        {
            // The SMU keeps its last measurement mode. After a WV reset sweep the mode is
            // staircase sweep (MM 2). DV commands for spike/read pulses must be issued in
            // spot/point measurement mode again, matching the existing pulse plans.
            await smu.SendCommandAsync($"MM 1,{s.ReadingChannel}");
            await smu.SendCommandAsync($"CMM {s.ReadingChannel},1");
            await smu.SendCommandAsync($"RV {s.WriteChannel},0");
            await smu.SendCommandAsync($"RI {s.WriteChannel},0");
        }

        private async Task ApplyInputSpikeTrainAsync(E5263_SMU smu, FrequencyMemorySettings s, double interSpikeIntervalMs, Action<double>? progress = null)
        {
            for (int i = 0; i < s.InputSpikeCount; i++)
            {
                await ForceVoltageAsync(smu, s.WriteChannel, s.InputSpikeVoltage, s.Compliance);
                await Task.Delay(ToDelayMilliseconds(s.InputSpikeLengthMs));
                await ForceVoltageAsync(smu, s.WriteChannel, 0.0, s.Compliance);

                progress?.Invoke((i + 1) / (double)Math.Max(1, s.InputSpikeCount));

                if (i < s.InputSpikeCount - 1 && interSpikeIntervalMs > 0)
                {
                    await Task.Delay(ToDelayMilliseconds(interSpikeIntervalMs));
                }
            }
        }

        private async Task<double> ReadCurrentPulseAsync(E5263_SMU smu, FrequencyMemorySettings s, double voltage, double durationMs)
        {
            await ConfigurePointMeasurementModeAsync(smu, s);
            await ForceVoltageAsync(smu, s.WriteChannel, voltage, s.Compliance);
            await Task.Delay(ToDelayMilliseconds(durationMs));

            await smu.SendCommandAsync("TSR");
            await smu.SendCommandAsync("XE");
            await smu.SendCommandAsync("TSQ");
            string response = await smu.ReadResponseAsync(512);

            await ForceVoltageAsync(smu, s.WriteChannel, 0.0, s.Compliance);
            return ParseCurrent(response, s.InvertCurrent);
        }

        private async Task PerformResetSweepAsync(E5263_SMU smu, FrequencyMemorySettings s)
        {
            const int resetPoints = 51;

            await smu.SendCommandAsync(FormattableString.Invariant($"WV {s.WriteChannel},1,0,0,{s.ResetSweepMinimum},{resetPoints},{s.Compliance}"));
            await smu.SendCommandAsync($"RI {s.ReadingChannel},0");
            await smu.SendCommandAsync($"MM 2,{s.ReadingChannel}");
            await smu.SendCommandAsync($"CMM {s.ReadingChannel},1");

            var firstSweepSetupError = await smu.CheckErrorAsync();
            if (firstSweepSetupError != null)
            {
                throw new InvalidOperationException($"SMU rejected reset sweep setup 0→minimum: {firstSweepSetupError}");
            }

            await smu.SendCommandAsync("TSR");
            await smu.SendCommandAsync("XE");
            await smu.SendCommandAsync("TSQ");
            try { _ = await smu.ReadResponseAsync(4096); } catch { }

            await smu.SendCommandAsync(FormattableString.Invariant($"WV {s.WriteChannel},1,0,{s.ResetSweepMinimum},0,{resetPoints},{s.Compliance}"));
            await smu.SendCommandAsync($"RI {s.ReadingChannel},0");
            await smu.SendCommandAsync($"MM 2,{s.ReadingChannel}");
            await smu.SendCommandAsync($"CMM {s.ReadingChannel},1");

            var secondSweepSetupError = await smu.CheckErrorAsync();
            if (secondSweepSetupError != null)
            {
                throw new InvalidOperationException($"SMU rejected reset sweep setup minimum→0: {secondSweepSetupError}");
            }

            await smu.SendCommandAsync("TSR");
            await smu.SendCommandAsync("XE");
            await smu.SendCommandAsync("TSQ");
            try { _ = await smu.ReadResponseAsync(4096); } catch { }

            await ConfigurePointMeasurementModeAsync(smu, s);
            await smu.SendCommandAsync($"DZ {s.WriteChannel}");

            var pointModeError = await smu.CheckErrorAsync();
            if (pointModeError != null)
            {
                throw new InvalidOperationException($"SMU rejected point-mode reconfiguration after reset sweep: {pointModeError}");
            }
        }

        private static string NormalizeSingleChannel(string rawChannel, string parameterName)
        {
            string channel = (rawChannel ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(channel))
            {
                throw new InvalidOperationException($"{parameterName} must not be empty.");
            }

            if (!Regex.IsMatch(channel, @"^\d+$"))
            {
                throw new InvalidOperationException($"{parameterName} must be one single SMU channel number, for example 1 or 2. Current value: '{rawChannel}'. Do not enter comma-separated channel lists here.");
            }

            return channel;
        }

        private static async Task ForceVoltageAsync(E5263_SMU smu, string channel, double voltage, double compliance)
        {
            if (Math.Abs(voltage) <= 1e-15)
            {
                await smu.SendCommandAsync($"DZ {channel}");
            }
            else
            {
                await smu.SendCommandAsync($"DZ {channel}");
                await smu.SendCommandAsync(FormattableString.Invariant($"DV {channel},0,{voltage},{compliance}"));
            }

            var error = await smu.CheckErrorAsync();
            if (error != null)
            {
                throw new InvalidOperationException($"SMU rejected voltage command on channel {channel}: {error}");
            }
        }

        private static int ToDelayMilliseconds(double valueMs)
        {
            if (valueMs <= 0) return 0;
            if (valueMs > int.MaxValue) return int.MaxValue;
            return Math.Max(1, (int)Math.Round(valueMs));
        }

        private static double CalculateTrainDurationMs(int spikeCount, double spikeLengthMs, double interSpikeIntervalMs)
        {
            if (spikeCount <= 0) return 0.0;
            return spikeCount * spikeLengthMs + Math.Max(0, spikeCount - 1) * interSpikeIntervalMs;
        }

        private void RebuildMeanPlotPoints(bool useDelta)
        {
            ResultPoints.Clear();

            foreach (var group in TrialResults.GroupBy(t => t.InterSpikeIntervalMs).OrderBy(g => g.Key))
            {
                var values = group
                    .Select(t => useDelta ? t.DeltaCurrentA : t.ReadCurrentA)
                    .Where(v => !double.IsNaN(v) && !double.IsInfinity(v))
                    .ToList();

                if (values.Count == 0) continue;

                double mean = values.Average();
                double? stdDev = values.Count > 1
                    ? Math.Sqrt(values.Sum(v => Math.Pow(v - mean, 2)) / (values.Count - 1))
                    : null;

                ResultPoints.Add(new CurvePoint(group.Key, mean, stdDev));
            }
        }

        private static double ParseCurrent(string rawData, bool invertCurrent)
        {
            if (string.IsNullOrWhiteSpace(rawData))
            {
                throw new InvalidOperationException("The SMU returned an empty current response.");
            }

            var matches = Regex.Matches(rawData, @"[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[Ee][+-]?\d+)?");
            foreach (Match match in matches)
            {
                if (double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    return invertCurrent ? -value : value;
                }
            }

            throw new InvalidOperationException($"Could not parse current from SMU response: {rawData}");
        }

        private sealed record FrequencyMemorySettings
        {
            public string WriteChannel { get; init; } = "2";
            public string ReadingChannel { get; init; } = "1";
            public bool InvertCurrent { get; init; }
            public double InputSpikeVoltage { get; init; }
            public double InputSpikeLengthMs { get; init; }
            public int InputSpikeCount { get; init; }
            public double MinInterSpikeIntervalMs { get; init; }
            public double MaxInterSpikeIntervalMs { get; init; }
            public int IntervalValueCount { get; init; }
            public double DelayBeforeMeasurementMs { get; init; }
            public double ReadoutVoltage { get; init; }
            public double ReadoutLengthMs { get; init; }
            public bool BaselineReadEnabled { get; init; }
            public double ResetSweepMinimum { get; init; }
            public int RepetitionsPerInterval { get; init; }
            public double Compliance { get; init; }
        }

        private sealed record FrequencyMemoryTrialResult(
            int TrialIndex,
            int RepetitionIndex,
            int IntervalIndex,
            double InterSpikeIntervalMs,
            double StartToStartPeriodMs,
            double InputFrequencyHz,
            double TrainDurationMs,
            double InputSpikeVoltage,
            double InputSpikeLengthMs,
            int InputSpikeCount,
            double DelayBeforeMeasurementMs,
            double ActualReadDelayMs,
            double ReadoutVoltage,
            double ReadoutLengthMs,
            bool BaselineReadEnabled,
            double? BaselineCurrentA,
            double ReadCurrentA,
            double DeltaCurrentA,
            double NormalizedDelta,
            double ResetSweepMinimum,
            double Compliance
        );
    }
}
