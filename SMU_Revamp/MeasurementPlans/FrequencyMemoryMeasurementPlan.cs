using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SMU_Revamp.Interfaces;
using SMU_Revamp.Models;
using SMU_Revamp.Services;

namespace SMU_Revamp.MeasurementPlans
{
    /// <summary>
    /// Frequency-memory experiment for volatile memristive devices.
    ///
    /// The only swept experimental variable is the quiet inter-spike interval between
    /// the end of one input spike and the start of the next input spike.
    /// For every interval value and repetition, the plan executes:
    ///
    ///     reset by negative I-V sweep -> optional baseline read -> input spike train
    ///     -> fixed delay -> readout pulse -> reset by negative I-V sweep
    ///
    /// CSV export writes one row per executed trial.
    /// The plotted result is the mean delayed readout response versus inter-spike interval.
    /// </summary>
    public sealed class FrequencyMemoryMeasurementPlan : IMeasurementPlan
    {
        private const int ResetSweepPoints = 51;
        private const int ResetSweepAdcSamples = 1;

        public string Name => "Frequency Memory";
        public string Description => "Applies input spike trains with linearly swept inter-spike intervals, waits a fixed delay, reads the delayed current response, and resets each trial using a negative I-V sweep.";

        public string PlotTitle => "Frequency Memory Response";
        public string XAxisLabel => "Inter-spike interval (ms)";
        public string YAxisLabel => GetParamValueBool("BaselineReadEnabled") ? "Δ Read Current (A)" : "Read Current (A)";
        public bool ShowLogPlot => true;
        public double PlotAspectRatio => 2.0;
        public PlotStyle DefaultPlotStyle => PlotStyle.LineAndScatter;

        public List<MeasurementParameter> Parameters { get; }
        public List<CurvePoint> ResultPoints { get; } = new();
        public List<FrequencyMemoryTrialResult> TrialResults { get; } = new();

        public IReadOnlyList<PlotSeries> PlotSeries => BuildPlotSeries();

        private string GetParamValueString(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsString() ?? string.Empty;
        private double GetParamValueDouble(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsDouble() ?? 0.0;
        private int GetParamValueInt(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsInt() ?? 0;
        private bool GetParamValueBool(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsBool() ?? false;

        public FrequencyMemoryMeasurementPlan()
        {
            Parameters = new List<MeasurementParameter>
            {
                new() { Name = "WriteChannel", DisplayName = "Write Channel:", Type = ParameterType.Text, Tooltip = "The SMU source channel number, e.g. 2.", Section = "Channel Settings" },
                new() { Name = "ReadingChannel", DisplayName = "Reading Channel:", Type = ParameterType.Text, Tooltip = "The SMU channel to measure. Leave identical to Write Channel for two-terminal devices.", Section = "Channel Settings" },

                new() { Name = "InputSpikeVoltage", DisplayName = "Input Spike Voltage (V):", Type = ParameterType.Number, Tooltip = "Voltage amplitude of each input spike.", Section = "Input Spike Train" },
                new() { Name = "InputSpikeLengthMs", DisplayName = "Input Spike Length (ms):", Type = ParameterType.Number, Tooltip = "Duration of each input spike in milliseconds.", Section = "Input Spike Train", ScrollStep = 1.0 },
                new() { Name = "InputSpikeCount", DisplayName = "Number of Input Spikes:", Type = ParameterType.Number, Tooltip = "Number of spikes in every input train.", Section = "Input Spike Train" },
                new() { Name = "MinInterSpikeIntervalMs", DisplayName = "Minimum Inter-spike Interval (ms):", Type = ParameterType.Number, Tooltip = "Minimum quiet pause from the end of one input spike to the start of the next.", Section = "Input Spike Train", ScrollStep = 1.0 },
                new() { Name = "MaxInterSpikeIntervalMs", DisplayName = "Maximum Inter-spike Interval (ms):", Type = ParameterType.Number, Tooltip = "Maximum quiet pause from the end of one input spike to the start of the next.", Section = "Input Spike Train", ScrollStep = 1.0 },
                new() { Name = "InterSpikeIntervalValues", DisplayName = "Number of Interval Values:", Type = ParameterType.Number, Tooltip = "Number of linearly spaced inter-spike interval values between minimum and maximum.", Section = "Input Spike Train" },

                new() { Name = "DelayBeforeMeasurementMs", DisplayName = "Delay Before Measurement (ms):", Type = ParameterType.Number, Tooltip = "Wait time after the last input spike has ended before applying the readout pulse.", Section = "Readout" },
                new() { Name = "ReadoutVoltage", DisplayName = "Readout Spike Voltage (V):", Type = ParameterType.Number, Tooltip = "Small non-switching readout voltage.", Section = "Readout" },
                new() { Name = "ReadoutLengthMs", DisplayName = "Readout Spike Length (ms):", Type = ParameterType.Number, Tooltip = "Duration of the readout pulse in milliseconds.", Section = "Readout", ScrollStep = 1.0 },
                new() { Name = "BaselineReadEnabled", DisplayName = "Baseline Read Enabled:", Type = ParameterType.Checkbox, Tooltip = "Measure a baseline current after reset and before the input spike train. The plotted/exported Δ current is Final - Baseline.", Section = "Readout" },

                new() { Name = "ResetSweepMinimum", DisplayName = "Reset I-V Sweep Minimum (V):", Type = ParameterType.Number, Tooltip = "Minimum negative voltage of the reset sweep. The plan performs a 0 -> minimum -> 0 sweep.", Section = "Reset" },
                new() { Name = "RepetitionsPerInterval", DisplayName = "Repetitions per Interval:", Type = ParameterType.Number, Tooltip = "Number of repeated trials for every inter-spike interval value. Total trials = interval values × repetitions.", Section = "Repetition" },

                new() { Name = "Compliance", DisplayName = "Compliance (A):", Type = ParameterType.Number, Tooltip = "Current compliance used for input spikes, readout pulses, and reset sweep. Required by the SMU commands.", Section = "Advanced / Safety" }
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
                    case "WriteChannel": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "WriteChannel", config.SweepChannel); break;
                    case "ReadingChannel": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "ReadingChannel", config.SweepChannel); break;

                    case "InputSpikeVoltage": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "InputSpikeVoltage", 1.2); break;
                    case "InputSpikeLengthMs": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "InputSpikeLengthMs", 10.0); break;
                    case "InputSpikeCount": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "InputSpikeCount", 10); break;
                    case "MinInterSpikeIntervalMs": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "MinInterSpikeIntervalMs", 0.0); break;
                    case "MaxInterSpikeIntervalMs": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "MaxInterSpikeIntervalMs", 200.0); break;
                    case "InterSpikeIntervalValues": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "InterSpikeIntervalValues", 9); break;

                    case "DelayBeforeMeasurementMs": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "DelayBeforeMeasurementMs", 2000.0); break;
                    case "ReadoutVoltage": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "ReadoutVoltage", 0.3); break;
                    case "ReadoutLengthMs": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "ReadoutLengthMs", 20.0); break;
                    case "BaselineReadEnabled": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "BaselineReadEnabled", true); break;

                    case "ResetSweepMinimum": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "ResetSweepMinimum", -1.0); break;
                    case "RepetitionsPerInterval": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "RepetitionsPerInterval", 5); break;

                    case "Compliance": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "Compliance", config.SweepCompliance); break;
                }
            }
        }

        public async Task RunMeasurementAsync(E5263_SMU smu, IProgress<double>? progress = null)
        {
            ResultPoints.Clear();
            TrialResults.Clear();
            progress?.Report(0);

            var settings = ReadAndValidateSettings();
            var intervalValues = BuildLinearIntervalValues(settings);
            int totalTrials = intervalValues.Count * settings.RepetitionsPerInterval;
            if (totalTrials <= 0) throw new InvalidOperationException("No frequency-memory trials were generated.");

            await smu.SendCommandAsync("*RST");
            await ConfigureSpotModeAsync(smu, settings);
            progress?.Report(1);

            using var cts = new CancellationTokenSource();
            int trialIndex = 1;

            void ReportTrialProgress(double fractionWithinTrial)
            {
                fractionWithinTrial = Math.Clamp(fractionWithinTrial, 0.0, 1.0);
                double completedTrials = trialIndex - 1;
                double overall = (completedTrials + fractionWithinTrial) * 100.0 / totalTrials;
                progress?.Report(Math.Min(99.9, overall));
            }

            try
            {
                // Ensure the first trial also starts from a reset state.
                await ApplyResetSweepAsync(smu, settings, cts.Token);
                await ConfigureSpotModeAsync(smu, settings);

                for (int rep = 1; rep <= settings.RepetitionsPerInterval; rep++)
                {
                    for (int intervalIndex = 0; intervalIndex < intervalValues.Count; intervalIndex++)
                    {
                        double isiMs = intervalValues[intervalIndex];
                        cts.Token.ThrowIfCancellationRequested();
                        ReportTrialProgress(0.02);

                        double baselineCurrent = double.NaN;
                        if (settings.BaselineReadEnabled)
                        {
                            baselineCurrent = await ReadCurrentPulseAsync(smu, settings, cts.Token);
                        }
                        ReportTrialProgress(0.12);

                        var trainStopwatch = Stopwatch.StartNew();
                        await ApplyInputSpikeTrainAsync(smu, settings, isiMs, trainStopwatch, cts.Token, trainFraction =>
                        {
                            ReportTrialProgress(0.12 + 0.38 * trainFraction);
                        });
                        ReportTrialProgress(0.50);

                        await WaitMillisecondsAccurateAsync(settings.DelayBeforeMeasurementMs, cts.Token, waitFraction =>
                        {
                            ReportTrialProgress(0.50 + 0.25 * waitFraction);
                        });
                        ReportTrialProgress(0.75);

                        var readStopwatch = Stopwatch.StartNew();
                        double readCurrent = await ReadCurrentPulseAsync(smu, settings, cts.Token);
                        double actualDelayMs = settings.DelayBeforeMeasurementMs + readStopwatch.Elapsed.TotalMilliseconds;
                        ReportTrialProgress(0.86);

                        double deltaCurrent = double.IsNaN(baselineCurrent) ? double.NaN : readCurrent - baselineCurrent;
                        double normalizedDelta = (!double.IsNaN(deltaCurrent) && Math.Abs(baselineCurrent) > 0.0)
                            ? deltaCurrent / Math.Abs(baselineCurrent)
                            : double.NaN;

                        double startToStartPeriodMs = settings.InputSpikeLengthMs + isiMs;
                        double inputFrequencyHz = startToStartPeriodMs > 0.0 ? 1000.0 / startToStartPeriodMs : double.NaN;
                        double trainDurationMs = settings.InputSpikeCount * settings.InputSpikeLengthMs + Math.Max(0, settings.InputSpikeCount - 1) * isiMs;

                        TrialResults.Add(new FrequencyMemoryTrialResult(
                            TrialIndex: trialIndex,
                            RepetitionIndex: rep,
                            IntervalIndex: intervalIndex + 1,
                            InterSpikeIntervalMs: isiMs,
                            StartToStartPeriodMs: startToStartPeriodMs,
                            InputFrequencyHz: inputFrequencyHz,
                            TrainDurationMs: trainDurationMs,
                            InputSpikeVoltageV: settings.InputSpikeVoltage,
                            InputSpikeLengthMs: settings.InputSpikeLengthMs,
                            InputSpikeCount: settings.InputSpikeCount,
                            DelayBeforeMeasurementMs: settings.DelayBeforeMeasurementMs,
                            ActualReadDelayMs: actualDelayMs,
                            ReadoutVoltageV: settings.ReadoutVoltage,
                            ReadoutLengthMs: settings.ReadoutLengthMs,
                            BaselineReadEnabled: settings.BaselineReadEnabled,
                            BaselineCurrentA: baselineCurrent,
                            ReadCurrentA: readCurrent,
                            DeltaCurrentA: deltaCurrent,
                            NormalizedDelta: normalizedDelta,
                            ResetSweepMinimumV: settings.ResetSweepMinimum,
                            ComplianceA: settings.Compliance));

                        RebuildAverageResultPoints(settings);
                        ReportTrialProgress(0.90);

                        // User-defined sequence: measurement is followed by reset before the next trial.
                        await ApplyResetSweepAsync(smu, settings, cts.Token);
                        await ConfigureSpotModeAsync(smu, settings);
                        ReportTrialProgress(0.98);

                        var loopError = await smu.CheckErrorAsync();
                        if (loopError != null)
                        {
                            await smu.SendCommandAsync("DZ");
                            throw new InvalidOperationException($"SMU error during Frequency Memory trial: {loopError}");
                        }

                        trialIndex++;
                        progress?.Report((trialIndex - 1) * 100.0 / totalTrials);
                    }
                }
            }
            finally
            {
                try { await smu.SendCommandAsync("DZ"); } catch { }
            }

            RebuildAverageResultPoints(settings);
            progress?.Report(100);
        }

        public IReadOnlyList<string> GetCsvLines()
        {
            var lines = new List<string>
            {
                "TrialIndex\tRepetitionIndex\tIntervalIndex\tInterSpikeInterval_ms\tStartToStartPeriod_ms\tInputFrequency_Hz\tTrainDuration_ms\tInputSpikeVoltage_V\tInputSpikeLength_ms\tInputSpikeCount\tDelayBeforeMeasurement_ms\tActualReadDelay_ms\tReadoutVoltage_V\tReadoutLength_ms\tBaselineReadEnabled\tBaselineCurrent_A\tReadCurrent_A\tDeltaCurrent_A\tNormalizedDelta\tResetSweepMinimum_V\tResetSweepPoints\tCompliance_A"
            };

            foreach (var r in TrialResults)
            {
                lines.Add(string.Join("\t", new[]
                {
                    r.TrialIndex.ToString(CultureInfo.InvariantCulture),
                    r.RepetitionIndex.ToString(CultureInfo.InvariantCulture),
                    r.IntervalIndex.ToString(CultureInfo.InvariantCulture),
                    F(r.InterSpikeIntervalMs),
                    F(r.StartToStartPeriodMs),
                    F(r.InputFrequencyHz),
                    F(r.TrainDurationMs),
                    F(r.InputSpikeVoltageV),
                    F(r.InputSpikeLengthMs),
                    r.InputSpikeCount.ToString(CultureInfo.InvariantCulture),
                    F(r.DelayBeforeMeasurementMs),
                    F(r.ActualReadDelayMs),
                    F(r.ReadoutVoltageV),
                    F(r.ReadoutLengthMs),
                    r.BaselineReadEnabled ? "true" : "false",
                    F(r.BaselineCurrentA),
                    F(r.ReadCurrentA),
                    F(r.DeltaCurrentA),
                    F(r.NormalizedDelta),
                    F(r.ResetSweepMinimumV),
                    ResetSweepPoints.ToString(CultureInfo.InvariantCulture),
                    F(r.ComplianceA)
                }));
            }

            return lines;
        }

        public void LoadFromCsvLines(IReadOnlyList<string> lines)
        {
            ResultPoints.Clear();
            TrialResults.Clear();

            if (lines == null || lines.Count == 0) return;

            char separator = DetectSeparator(lines);
            string? headerLine = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l) && !l.Trim().StartsWith("#") && !l.Trim().StartsWith("sep=", StringComparison.OrdinalIgnoreCase));
            if (headerLine == null) return;

            var headers = headerLine.Split(separator).Select(h => h.Trim()).ToList();
            int idxIsi = headers.FindIndex(h => h.Equals("InterSpikeInterval_ms", StringComparison.OrdinalIgnoreCase));
            int idxDelta = headers.FindIndex(h => h.Equals("DeltaCurrent_A", StringComparison.OrdinalIgnoreCase));
            int idxRead = headers.FindIndex(h => h.Equals("ReadCurrent_A", StringComparison.OrdinalIgnoreCase));
            if (idxIsi < 0 || (idxDelta < 0 && idxRead < 0)) return;

            bool firstDataSkipped = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith("sep=", StringComparison.OrdinalIgnoreCase)) continue;

                if (!firstDataSkipped)
                {
                    firstDataSkipped = true;
                    continue;
                }

                var parts = trimmed.Split(separator);
                if (idxIsi >= parts.Length) continue;
                int yIndex = idxDelta >= 0 && idxDelta < parts.Length ? idxDelta : idxRead;
                if (yIndex >= parts.Length) continue;

                if (ParameterConfigHelper.TryParseDoubleRobust(parts[idxIsi], out double x) &&
                    ParameterConfigHelper.TryParseDoubleRobust(parts[yIndex], out double y))
                {
                    if (!double.IsNaN(y)) ResultPoints.Add(new CurvePoint(x, y));
                }
            }
        }

        private FrequencyMemorySettings ReadAndValidateSettings()
        {
            string writeChannel = GetParamValueString("WriteChannel");
            string readingChannel = GetParamValueString("ReadingChannel");
            if (string.IsNullOrWhiteSpace(writeChannel)) throw new InvalidOperationException("Write Channel must not be empty.");
            if (string.IsNullOrWhiteSpace(readingChannel)) readingChannel = writeChannel;

            var s = new FrequencyMemorySettings
            {
                WriteChannel = writeChannel.Trim(),
                ReadingChannel = readingChannel.Trim(),
                InvertCurrent = readingChannel.Trim() != writeChannel.Trim(),

                InputSpikeVoltage = GetParamValueDouble("InputSpikeVoltage"),
                InputSpikeLengthMs = GetParamValueDouble("InputSpikeLengthMs"),
                InputSpikeCount = GetParamValueInt("InputSpikeCount"),
                MinInterSpikeIntervalMs = GetParamValueDouble("MinInterSpikeIntervalMs"),
                MaxInterSpikeIntervalMs = GetParamValueDouble("MaxInterSpikeIntervalMs"),
                InterSpikeIntervalValues = GetParamValueInt("InterSpikeIntervalValues"),

                DelayBeforeMeasurementMs = GetParamValueDouble("DelayBeforeMeasurementMs"),
                ReadoutVoltage = GetParamValueDouble("ReadoutVoltage"),
                ReadoutLengthMs = GetParamValueDouble("ReadoutLengthMs"),
                BaselineReadEnabled = GetParamValueBool("BaselineReadEnabled"),

                ResetSweepMinimum = GetParamValueDouble("ResetSweepMinimum"),
                RepetitionsPerInterval = GetParamValueInt("RepetitionsPerInterval"),
                Compliance = GetParamValueDouble("Compliance")
            };

            if (s.InputSpikeLengthMs <= 0) throw new InvalidOperationException("Input spike length must be > 0 ms.");
            if (s.InputSpikeCount < 1) throw new InvalidOperationException("Number of input spikes must be at least 1.");
            if (s.MinInterSpikeIntervalMs < 0) throw new InvalidOperationException("Minimum inter-spike interval must be >= 0 ms.");
            if (s.MaxInterSpikeIntervalMs < s.MinInterSpikeIntervalMs) throw new InvalidOperationException("Maximum inter-spike interval must be >= minimum inter-spike interval.");
            if (s.InterSpikeIntervalValues < 1) throw new InvalidOperationException("Number of interval values must be at least 1.");
            if (s.DelayBeforeMeasurementMs < 0) throw new InvalidOperationException("Delay before measurement must be >= 0 ms.");
            if (s.ReadoutLengthMs <= 0) throw new InvalidOperationException("Readout spike length must be > 0 ms.");
            if (s.ResetSweepMinimum >= 0) throw new InvalidOperationException("Reset I-V sweep minimum should be negative.");
            if (s.RepetitionsPerInterval < 1) throw new InvalidOperationException("Repetitions per interval must be at least 1.");
            if (s.Compliance <= 0) throw new InvalidOperationException("Compliance must be > 0 A.");

            return s;
        }

        private static List<double> BuildLinearIntervalValues(FrequencyMemorySettings s)
        {
            if (s.InterSpikeIntervalValues == 1)
            {
                return new List<double> { s.MinInterSpikeIntervalMs };
            }

            double step = (s.MaxInterSpikeIntervalMs - s.MinInterSpikeIntervalMs) / (s.InterSpikeIntervalValues - 1);
            var values = new List<double>();
            for (int i = 0; i < s.InterSpikeIntervalValues; i++)
            {
                values.Add(s.MinInterSpikeIntervalMs + i * step);
            }
            return values;
        }

        private async Task ConfigureSpotModeAsync(E5263_SMU smu, FrequencyMemorySettings s)
        {
            if (s.ReadingChannel != s.WriteChannel)
            {
                await smu.SendCommandAsync($"CN {s.WriteChannel},{s.ReadingChannel}");
            }
            else
            {
                await smu.SendCommandAsync($"CN {s.WriteChannel}");
            }

            await smu.SendCommandAsync("FMT 1,0");
            await smu.SendCommandAsync($"MM 1,{s.ReadingChannel}");
            await smu.SendCommandAsync($"CMM {s.ReadingChannel},1");
            await smu.SendCommandAsync($"RV {s.WriteChannel},0");
            await smu.SendCommandAsync($"RI {s.WriteChannel},0");
            await smu.SendCommandAsync($"DZ {s.WriteChannel}");

            var setupError = await smu.CheckErrorAsync();
            if (setupError != null) throw new InvalidOperationException($"SMU spot-mode setup error: {setupError}");
        }

        private async Task ApplyInputSpikeTrainAsync(E5263_SMU smu, FrequencyMemorySettings s, double interSpikeIntervalMs, Stopwatch trainStopwatch, CancellationToken ct, Action<double>? progressCallback = null)
        {
            progressCallback?.Invoke(0.0);

            double trainDurationMs = s.InputSpikeCount * s.InputSpikeLengthMs + Math.Max(0, s.InputSpikeCount - 1) * interSpikeIntervalMs;
            if (trainDurationMs <= 0) trainDurationMs = 1.0;

            for (int i = 0; i < s.InputSpikeCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                await ApplyVoltagePulseAsync(smu, s.WriteChannel, s.InputSpikeVoltage, s.InputSpikeLengthMs, s.Compliance, ct);

                if (i < s.InputSpikeCount - 1 && interSpikeIntervalMs > 0)
                {
                    await WaitMillisecondsAccurateAsync(interSpikeIntervalMs, ct, waitFraction =>
                    {
                        double elapsedEstimate = i * (s.InputSpikeLengthMs + interSpikeIntervalMs) + s.InputSpikeLengthMs + waitFraction * interSpikeIntervalMs;
                        progressCallback?.Invoke(Math.Clamp(elapsedEstimate / trainDurationMs, 0.0, 1.0));
                    });
                }

                progressCallback?.Invoke(Math.Clamp(trainStopwatch.Elapsed.TotalMilliseconds / trainDurationMs, 0.0, 1.0));
            }

            progressCallback?.Invoke(1.0);
        }

        private async Task ApplyVoltagePulseAsync(E5263_SMU smu, string channel, double voltage, double pulseLengthMs, double compliance, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await smu.SendCommandAsync($"DZ {channel}");
            await smu.SendCommandAsync(FormattableString.Invariant($"DV {channel},0,{voltage},{compliance}"));
            await WaitMillisecondsAccurateAsync(pulseLengthMs, ct);
            await smu.SendCommandAsync($"DZ {channel}");
        }

        private async Task<double> ReadCurrentPulseAsync(E5263_SMU smu, FrequencyMemorySettings s, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await smu.SendCommandAsync($"DZ {s.WriteChannel}");
            await smu.SendCommandAsync(FormattableString.Invariant($"DV {s.WriteChannel},0,{s.ReadoutVoltage},{s.Compliance}"));
            await WaitMillisecondsAccurateAsync(2.0, ct);
            await smu.SendCommandAsync("XE");
            await WaitMillisecondsAccurateAsync(s.ReadoutLengthMs, ct);
            await WaitMillisecondsAccurateAsync(5.0, ct);

            string response = await smu.ReadResponseAsync(100);
            await smu.SendCommandAsync($"DZ {s.WriteChannel}");

            return ParseCurrent(response, s.InvertCurrent);
        }

        private async Task ApplyResetSweepAsync(E5263_SMU smu, FrequencyMemorySettings s, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            await smu.SendCommandAsync($"DZ {s.WriteChannel}");
            await smu.SendCommandAsync($"AV -{ResetSweepAdcSamples},0");

            var wvCommand = FormattableString.Invariant($"WV {s.WriteChannel},3,0,0,{s.ResetSweepMinimum},{ResetSweepPoints},{s.Compliance}");
            await smu.SendCommandAsync(wvCommand);

            var wvError = await smu.CheckErrorAsync();
            if (wvError != null)
            {
                throw new InvalidOperationException($"SMU rejected reset sweep WV command: {wvError}");
            }

            await smu.SendCommandAsync($"RI {s.ReadingChannel},0");
            await smu.SendCommandAsync($"MM 2,{s.ReadingChannel}");
            await smu.SendCommandAsync($"CMM {s.ReadingChannel},1");
            await smu.SendCommandAsync("TSR");
            await smu.SendCommandAsync("XE");
            await smu.SendCommandAsync("TSQ");

            int expectedBufferLength = ResetSweepPoints * 2 * 32 + 200;
            try
            {
                string _ = await smu.ReadResponseAsync(expectedBufferLength);
                string __ = await smu.ReadResponseAsync(50);
            }
            catch
            {
                throw;
            }
            finally
            {
                await smu.SendCommandAsync($"DZ {s.WriteChannel}");
            }
        }

        private void RebuildAverageResultPoints(FrequencyMemorySettings s)
        {
            ResultPoints.Clear();

            var groups = TrialResults
                .GroupBy(r => r.InterSpikeIntervalMs)
                .OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                var values = group
                    .Select(r => s.BaselineReadEnabled && !double.IsNaN(r.DeltaCurrentA) ? r.DeltaCurrentA : r.ReadCurrentA)
                    .Where(v => !double.IsNaN(v) && !double.IsInfinity(v))
                    .ToList();

                if (values.Count == 0) continue;
                ResultPoints.Add(new CurvePoint(group.Key, values.Average()));
            }
        }

        private IReadOnlyList<PlotSeries> BuildPlotSeries()
        {
            if (TrialResults.Count == 0)
            {
                return ResultPoints.Count > 0
                    ? new List<PlotSeries> { new PlotSeries("Average", ResultPoints.ToList()) }
                    : Array.Empty<PlotSeries>();
            }

            var averagePoints = TrialResults
                .GroupBy(r => r.InterSpikeIntervalMs)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var ys = g.Select(r => r.BaselineReadEnabled && !double.IsNaN(r.DeltaCurrentA) ? r.DeltaCurrentA : r.ReadCurrentA)
                        .Where(v => !double.IsNaN(v) && !double.IsInfinity(v))
                        .ToList();
                    return ys.Count == 0 ? null : new CurvePoint(g.Key, ys.Average());
                })
                .Where(p => p != null)
                .Cast<CurvePoint>()
                .ToList();

            var rawPoints = TrialResults
                .Select(r => new CurvePoint(r.InterSpikeIntervalMs, r.BaselineReadEnabled && !double.IsNaN(r.DeltaCurrentA) ? r.DeltaCurrentA : r.ReadCurrentA))
                .Where(p => !double.IsNaN(p.Y) && !double.IsInfinity(p.Y))
                .ToList();

            var series = new List<PlotSeries>();
            if (rawPoints.Count > 0) series.Add(new PlotSeries("Individual trials", rawPoints));
            if (averagePoints.Count > 0) series.Add(new PlotSeries("Mean per interval", averagePoints));
            return series;
        }

        private static double ParseCurrent(string rawData, bool invertCurrent)
        {
            if (string.IsNullOrWhiteSpace(rawData))
            {
                throw new InvalidOperationException("No SMU response received during current readout.");
            }

            var items = rawData.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in items)
            {
                var trimmed = item.Trim();
                if (trimmed.Length >= 4 && trimmed[2] == 'I')
                {
                    string numStr = trimmed.Substring(3);
                    if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double iVal))
                    {
                        return invertCurrent ? -iVal : iVal;
                    }
                }
            }

            throw new InvalidOperationException($"Could not parse current from SMU response: '{rawData}'");
        }

        private static async Task WaitMillisecondsAccurateAsync(double ms, CancellationToken ct, Action<double>? progressCallback = null)
        {
            if (ms <= 0)
            {
                progressCallback?.Invoke(1.0);
                return;
            }

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < ms)
            {
                ct.ThrowIfCancellationRequested();
                progressCallback?.Invoke(Math.Clamp(sw.Elapsed.TotalMilliseconds / ms, 0.0, 1.0));

                double remaining = ms - sw.Elapsed.TotalMilliseconds;
                int delay = remaining > 5 ? 2 : 1;
                await Task.Delay(delay, ct);
            }

            progressCallback?.Invoke(1.0);
        }

        private static char DetectSeparator(IReadOnlyList<string> lines)
        {
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("sep=", StringComparison.OrdinalIgnoreCase) && trimmed.Length >= 5)
                {
                    return trimmed[4];
                }
            }

            var header = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l) && !l.Trim().StartsWith("#"));
            if (header == null) return '\t';
            if (header.Contains('\t')) return '\t';
            if (header.Contains(';')) return ';';
            if (header.Contains(',')) return ',';
            return '\t';
        }

        private static string F(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? "NaN"
                : value.ToString("G9", CultureInfo.InvariantCulture);
        }

        private sealed class FrequencyMemorySettings
        {
            public string WriteChannel { get; init; } = string.Empty;
            public string ReadingChannel { get; init; } = string.Empty;
            public bool InvertCurrent { get; init; }

            public double InputSpikeVoltage { get; init; }
            public double InputSpikeLengthMs { get; init; }
            public int InputSpikeCount { get; init; }
            public double MinInterSpikeIntervalMs { get; init; }
            public double MaxInterSpikeIntervalMs { get; init; }
            public int InterSpikeIntervalValues { get; init; }

            public double DelayBeforeMeasurementMs { get; init; }
            public double ReadoutVoltage { get; init; }
            public double ReadoutLengthMs { get; init; }
            public bool BaselineReadEnabled { get; init; }

            public double ResetSweepMinimum { get; init; }
            public int RepetitionsPerInterval { get; init; }
            public double Compliance { get; init; }
        }

        public sealed record FrequencyMemoryTrialResult(
            int TrialIndex,
            int RepetitionIndex,
            int IntervalIndex,
            double InterSpikeIntervalMs,
            double StartToStartPeriodMs,
            double InputFrequencyHz,
            double TrainDurationMs,
            double InputSpikeVoltageV,
            double InputSpikeLengthMs,
            int InputSpikeCount,
            double DelayBeforeMeasurementMs,
            double ActualReadDelayMs,
            double ReadoutVoltageV,
            double ReadoutLengthMs,
            bool BaselineReadEnabled,
            double BaselineCurrentA,
            double ReadCurrentA,
            double DeltaCurrentA,
            double NormalizedDelta,
            double ResetSweepMinimumV,
            double ComplianceA);
    }
}
