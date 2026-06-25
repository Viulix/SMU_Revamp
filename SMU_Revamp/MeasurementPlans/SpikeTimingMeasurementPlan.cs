using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SMU_Revamp.Models;
using SMU_Revamp.Services;
using SMU_Revamp.Interfaces;

namespace SMU_Revamp.MeasurementPlans
{
    /// <summary>
    /// Spike timing experiment based on three device time constants.
    ///
    /// The plan creates the six permutations of three user-defined time constants A, B and C.
    /// Each permutation defines the three quiet breaks between four spikes:
    ///     spike 1 -> gap 1 -> spike 2 -> gap 2 -> spike 3 -> gap 3 -> spike 4
    ///
    /// For every trial:
    ///     reset -> baseline read -> four-spike pattern -> read after A/B/C from the last spike end.
    ///
    /// CSV export writes one row per trial. The three readouts after the same pattern are stored
    /// as Readout1/Readout2/Readout3 columns in that same row.
    ///
    /// The normal ResultPoints list contains one point per trial:
    ///     x = trial index, y = readout 3 current.
    /// Detailed metadata is exported through GetCsvLines().
    /// </summary>
    public sealed class SpikeTimingMeasurementPlan : IMeasurementPlan
    {
        public string Name => "Spike Timing";
        public string Description => "Applies four-spike patterns generated from the six permutations of three device time constants, resets between trials, and records three readout currents after each pattern.";

        public string PlotTitle => "Time-Constant Spike Response";
        public string XAxisLabel => "Delay after Last Spike End (ms)";
        public string YAxisLabel => "Read Current (A)";
        public bool ShowLogPlot => true;
        public double PlotAspectRatio => 3.0;
        public PlotStyle DefaultPlotStyle => PlotStyle.LineAndScatter;

        public List<MeasurementParameter> Parameters { get; }

        /// <summary>
        /// Contains the latest completed trial as I(t). This keeps the legacy single-series viewer path useful.
        /// The multi-series viewer uses PlotSeries below.
        /// </summary>
        public List<CurvePoint> ResultPoints { get; } = new();

        /// <summary>
        /// Six averaged I(t) curves, one per gap-order pattern.
        /// Each curve has three points: readout after A/B/C from the last spike end.
        /// </summary>
        public IReadOnlyList<PlotSeries> PlotSeries => BuildAveragePlotSeries();

        /// <summary>
        /// Full metadata for every trial. There is one result row per spike pattern execution.
        /// </summary>
        public List<SpikeTimingTrialResult> TrialResults { get; } = new();

        /// <summary>
        /// The six generated gap-order patterns for the most recent run.
        /// </summary>
        public List<SpikeTimingPatternInfo> GeneratedPatterns { get; } = new();

        private string GetParamValueString(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsString() ?? string.Empty;
        private double GetParamValueDouble(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsDouble() ?? 0.0;
        private int GetParamValueInt(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsInt() ?? 0;
        private bool GetParamValueBool(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsBool() ?? false;

        public SpikeTimingMeasurementPlan()
        {
            Parameters = new List<MeasurementParameter>
            {
                new() { Name = "WriteChannel", DisplayName = "Write Channel:", Type = ParameterType.Text, Tooltip = "The SMU source channel number (e.g. 2).", Section = "Channel Settings" },
                new() { Name = "ReadingChannel", DisplayName = "Reading Channel:", Type = ParameterType.Text, Tooltip = "The SMU channel to measure. Leave identical to Write Channel for two-terminal devices.", Section = "Channel Settings" },

                new() { Name = "TimeConstantA_Ms", DisplayName = "Time Constant A (ms):", Type = ParameterType.Number, Tooltip = "First device time constant. Used as one possible break between spikes and as one readout delay after the last spike.", Section = "Time Constants" },
                new() { Name = "TimeConstantB_Ms", DisplayName = "Time Constant B (ms):", Type = ParameterType.Number, Tooltip = "Second device time constant. Used as one possible break between spikes and as one readout delay after the last spike.", Section = "Time Constants" },
                new() { Name = "TimeConstantC_Ms", DisplayName = "Time Constant C (ms):", Type = ParameterType.Number, Tooltip = "Third device time constant. Used as one possible break between spikes and as one readout delay after the last spike.", Section = "Time Constants" },
                new() { Name = "RepetitionsPerPattern", DisplayName = "Repetitions per Pattern:", Type = ParameterType.Number, Tooltip = "How many times every A/B/C permutation is measured. The device is reset before each trial.", Section = "Time Constants" },
                new() { Name = "ShuffleExecutionOrder", DisplayName = "Shuffle Execution Order:", Type = ParameterType.Checkbox, Tooltip = "Measure the repeated pattern list in pseudo-random order instead of grouped order. The shuffle is reproducible through the seed.", Section = "Time Constants" },

                new() { Name = "SpikeVoltage", DisplayName = "Spike Voltage (V):", Type = ParameterType.Number, Tooltip = "Voltage amplitude of each timing spike.", Section = "Spike Settings" },
                new() { Name = "SpikeLengthMs", DisplayName = "Spike Length (ms):", Type = ParameterType.Number, Tooltip = "Duration of each spike in milliseconds. The plan always uses four spikes.", Section = "Spike Settings" },

                new() { Name = "ReadVoltage", DisplayName = "Read Voltage (V):", Type = ParameterType.Number, Tooltip = "Small non-switching readout voltage.", Section = "Readout Settings" },
                new() { Name = "ReadPulseLengthMs", DisplayName = "Read Pulse Length (ms):", Type = ParameterType.Number, Tooltip = "Duration of each readout pulse in milliseconds.", Section = "Readout Settings" },

                new() { Name = "Compliance", DisplayName = "Compliance (A):", Type = ParameterType.Number, Tooltip = "Current compliance used for spike, reset, baseline, and read pulses.", Section = "Advanced / Safety" },
                new() { Name = "ShuffleSeed", DisplayName = "Shuffle Seed:", Type = ParameterType.Number, Tooltip = "Integer seed for reproducible pseudo-random execution order when shuffling is enabled.", Section = "Advanced / Safety" },
                new() { Name = "BaselineReadEnabled", DisplayName = "Baseline Read Before Pattern:", Type = ParameterType.Checkbox, Tooltip = "Measure baseline current after reset and before the spike train.", Section = "Advanced / Safety" },

                new() { Name = "ResetEnabled", DisplayName = "Reset Before Each Trial:", Type = ParameterType.Checkbox, Tooltip = "Apply reset pulse(s) before every pattern repetition.", Section = "Reset Settings" },
                new() { Name = "ResetVoltage", DisplayName = "Reset Voltage (V):", Type = ParameterType.Number, Tooltip = "Reset pulse voltage.", Section = "Reset Settings" },
                new() { Name = "ResetPulseLengthMs", DisplayName = "Reset Pulse Length (ms):", Type = ParameterType.Number, Tooltip = "Duration of one reset pulse.", Section = "Reset Settings" },
                new() { Name = "ResetRepetitions", DisplayName = "Reset Repetitions:", Type = ParameterType.Number, Tooltip = "Number of reset pulses before each trial.", Section = "Reset Settings" },
                new() { Name = "ResetRecoveryMs", DisplayName = "Reset Recovery Time (ms):", Type = ParameterType.Number, Tooltip = "Wait time after the reset sequence before baseline/spike train.", Section = "Reset Settings", ScrollStep = 10.0 }
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

                    case "TimeConstantA_Ms": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "TimeConstantA_Ms", 100.0); break;
                    case "TimeConstantB_Ms": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "TimeConstantB_Ms", 500.0); break;
                    case "TimeConstantC_Ms": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "TimeConstantC_Ms", 1000.0); break;
                    case "RepetitionsPerPattern": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "RepetitionsPerPattern", 3); break;
                    case "ShuffleExecutionOrder": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "ShuffleExecutionOrder", true); break;

                    case "SpikeVoltage": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "SpikeVoltage", 1.0); break;
                    case "SpikeLengthMs": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "SpikeLengthMs", 30.0); break;

                    case "ReadVoltage": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "ReadVoltage", 0.3); break;
                    case "ReadPulseLengthMs": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "ReadPulseLengthMs", 30.0); break;

                    case "Compliance": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "Compliance", config.SweepCompliance); break;
                    case "ShuffleSeed": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "ShuffleSeed", 12345); break;
                    case "BaselineReadEnabled": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "BaselineReadEnabled", true); break;

                    case "ResetEnabled": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "ResetEnabled", true); break;
                    case "ResetVoltage": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "ResetVoltage", -1.0); break;
                    case "ResetPulseLengthMs": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "ResetPulseLengthMs", 100.0); break;
                    case "ResetRepetitions": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "ResetRepetitions", 1); break;
                    case "ResetRecoveryMs": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "ResetRecoveryMs", 100.0); break;
                }
            }
        }

        public async Task RunMeasurementAsync(E5263_SMU smu, IProgress<double>? progress = null)
        {
            ResultPoints.Clear();
            TrialResults.Clear();
            GeneratedPatterns.Clear();
            progress?.Report(0);

            var settings = ReadAndValidateSettings();
            var patterns = GenerateTimeConstantPatterns(settings);
            var schedule = BuildExecutionSchedule(patterns, settings);
            var chronologicalReadouts = settings.Readouts
                .OrderBy(r => r.TargetDelayAfterLastSpikeEndMs)
                .ThenBy(r => r.ReadoutNumber)
                .ToArray();

            if (schedule.Count == 0)
            {
                throw new InvalidOperationException("No spike timing trials were generated. Check repetitions and time constants.");
            }

            GeneratedPatterns.AddRange(patterns.Select(p => p.ToInfo()));

            await ConfigureSmuAsync(smu, settings);
            progress?.Report(2);

            int totalTrials = schedule.Count;
            int trialIndex = 1;
            using var cts = new CancellationTokenSource();

            void ReportTrialProgress(double fractionWithinTrial)
            {
                fractionWithinTrial = Math.Clamp(fractionWithinTrial, 0.0, 1.0);
                double completedTrials = trialIndex - 1;
                double overallProgress = (completedTrials + fractionWithinTrial) * 100.0 / totalTrials;
                progress?.Report(Math.Min(99.9, overallProgress));
            }

            try
            {
                foreach (var item in schedule)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    ReportTrialProgress(0.01);

                    if (settings.ResetEnabled)
                    {
                        await ApplyResetAsync(smu, settings, cts.Token);
                    }
                    else
                    {
                        await smu.SendCommandAsync($"DZ {settings.WriteChannel}");
                    }
                    ReportTrialProgress(0.12);

                    double baselineCurrent = double.NaN;
                    if (settings.BaselineReadEnabled)
                    {
                        baselineCurrent = await ReadCurrentPulseAsync(smu, settings, cts.Token);
                    }
                    ReportTrialProgress(0.22);

                    var trialStopwatch = Stopwatch.StartNew();
                    await RunSpikePatternAsync(smu, settings, item.Pattern, trialStopwatch, cts.Token, patternFraction =>
                    {
                        // Reset/baseline account for roughly the first 22% of the trial progress.
                        // Spike train and three readouts use the remaining portion.
                        ReportTrialProgress(0.22 + 0.38 * patternFraction);
                    });
                    ReportTrialProgress(0.60);

                    var readoutMeasurements = new ReadoutMeasurement?[3];
                    var actualReadoutOrder = new List<string>();

                    for (int chronologicalIndex = 0; chronologicalIndex < chronologicalReadouts.Length; chronologicalIndex++)
                    {
                        var spec = chronologicalReadouts[chronologicalIndex];
                        double targetElapsed = item.Pattern.LastSpikeEndMs + spec.TargetDelayAfterLastSpikeEndMs;

                        await WaitUntilElapsedAsync(trialStopwatch, targetElapsed, cts.Token, elapsedFraction =>
                        {
                            double fraction = (chronologicalIndex + elapsedFraction) / chronologicalReadouts.Length;
                            ReportTrialProgress(0.60 + 0.35 * fraction);
                        }, Math.Max(targetElapsed, 1.0));

                        double actualReadStartMs = trialStopwatch.Elapsed.TotalMilliseconds;
                        double actualDelayAfterLastEnd = actualReadStartMs - item.Pattern.LastSpikeEndMs;
                        double readCurrent = await ReadCurrentPulseAsync(smu, settings, cts.Token);
                        double deltaCurrent = double.IsNaN(baselineCurrent) ? double.NaN : readCurrent - baselineCurrent;
                        double conductance = settings.ReadVoltage == 0 ? double.NaN : readCurrent / settings.ReadVoltage;

                        var measurement = new ReadoutMeasurement(
                            ReadoutNumber: spec.ReadoutNumber,
                            ReadoutLabel: spec.Label,
                            TargetDelayAfterLastSpikeEndMs: spec.TargetDelayAfterLastSpikeEndMs,
                            ActualDelayAfterLastSpikeEndMs: actualDelayAfterLastEnd,
                            ReadCurrentA: readCurrent,
                            DeltaCurrentA: deltaCurrent,
                            ConductanceS: conductance);

                        readoutMeasurements[spec.ReadoutNumber - 1] = measurement;
                        actualReadoutOrder.Add(spec.Label);

                        var readMsg = FormattableString.Invariant($"[Spike Timing] Trial {trialIndex}/{totalTrials}, Rep {item.RepetitionIndex}, Pattern {item.Pattern.GapOrder}, Readout {spec.Label}: target={spec.TargetDelayAfterLastSpikeEndMs:G9} ms, actual={actualDelayAfterLastEnd:G9} ms, I={readCurrent:E6} A");
                        Console.WriteLine(readMsg);
                        Debug.WriteLine(readMsg);

                        ReportTrialProgress(0.60 + 0.40 * ((chronologicalIndex + 1.0) / chronologicalReadouts.Length));
                    }

                    if (readoutMeasurements.Any(r => r is null))
                    {
                        throw new InvalidOperationException("Internal error: at least one readout was not measured.");
                    }

                    var readouts = readoutMeasurements.Cast<ReadoutMeasurement>().ToArray();
                    UpdateLatestTrialCurve(readouts);

                    TrialResults.Add(new SpikeTimingTrialResult(
                        TrialIndex: trialIndex,
                        RepetitionIndex: item.RepetitionIndex,
                        PatternIndex: item.Pattern.PatternIndex,
                        GapOrder: item.Pattern.GapOrder,
                        Gap1Ms: item.Pattern.GapsMs[0],
                        Gap2Ms: item.Pattern.GapsMs[1],
                        Gap3Ms: item.Pattern.GapsMs[2],
                        SpikeTimesMs: string.Join(";", item.Pattern.SpikeStartTimesMs.Select(v => v.ToString("G9", CultureInfo.InvariantCulture))),
                        SpikeEndTimesMs: string.Join(";", item.Pattern.SpikeEndTimesMs.Select(v => v.ToString("G9", CultureInfo.InvariantCulture))),
                        LastSpikeStartMs: item.Pattern.LastSpikeStartMs,
                        LastSpikeEndMs: item.Pattern.LastSpikeEndMs,
                        ActualReadoutOrder: string.Join(";", actualReadoutOrder),
                        BaselineCurrentA: baselineCurrent,
                        Readout1: readouts[0],
                        Readout2: readouts[1],
                        Readout3: readouts[2]));

                    trialIndex++;
                    progress?.Report((trialIndex - 1) * 100.0 / totalTrials);

                    var loopError = await smu.CheckErrorAsync();
                    if (loopError != null)
                    {
                        await smu.SendCommandAsync("DZ");
                        throw new InvalidOperationException($"SMU error during Spike Timing trial: {loopError}");
                    }
                }
            }
            finally
            {
                try { await smu.SendCommandAsync("DZ"); } catch { }
            }

            progress?.Report(100);
        }

        /// <summary>
        /// Generic export hook used by the refactored IMeasurementPlan interface.
        /// </summary>
        public IReadOnlyList<string> GetCsvLines() => GetDetailedCsvLines();

        public void LoadFromCsvLines(IReadOnlyList<string> lines)
        {
            TrialResults.Clear();
            ResultPoints.Clear();

            string? headerLine = null;
            var dataLines = new List<string>();
            char separator = '\t';
            bool hasSeparator = false;

            // Detect separator from sep= line if present
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("sep=", StringComparison.OrdinalIgnoreCase))
                {
                    var sepStr = trimmed.Substring(4).Trim();
                    if (sepStr.Length > 0)
                    {
                        separator = sepStr[0];
                        hasSeparator = true;
                        break;
                    }
                }
            }

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith("sep="))
                {
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

            if (headerLine == null) return;

            if (!hasSeparator)
            {
                if (headerLine.Contains('\t')) separator = '\t';
                else if (headerLine.Contains(';')) separator = ';';
                else if (headerLine.Contains(',')) separator = ',';
            }

            var headers = headerLine.Split(separator).Select(h => h.Trim().Trim('"')).ToList();

            int GetIndex(string name) => headers.FindIndex(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));

            int idxTrial = GetIndex("TrialIndex");
            int idxRep = GetIndex("RepetitionIndex");
            int idxPat = GetIndex("PatternIndex");
            int idxGapOrder = GetIndex("GapOrder");
            int idxGap1 = GetIndex("Gap1_ms");
            int idxGap2 = GetIndex("Gap2_ms");
            int idxGap3 = GetIndex("Gap3_ms");
            int idxSpikeTimes = GetIndex("SpikeTimes_ms");
            int idxSpikeEndTimes = GetIndex("SpikeEndTimes_ms");
            int idxLastSpikeStart = GetIndex("LastSpikeStart_ms");
            int idxLastSpikeEnd = GetIndex("LastSpikeEnd_ms");
            int idxActualReadoutOrder = GetIndex("ActualReadoutOrder");
            int idxBaselineCurrent = GetIndex("BaselineCurrent_A");

            // Readouts
            int idxR1Label = GetIndex("Readout1_Label");
            int idxR1TargetDelay = GetIndex("Readout1_TargetDelayAfterLastSpikeEnd_ms");
            int idxR1ActualDelay = GetIndex("Readout1_ActualDelayAfterLastSpikeEnd_ms");
            int idxR1Current = GetIndex("Readout1_Current_A");
            int idxR1Delta = GetIndex("Readout1_DeltaCurrent_A");
            int idxR1Cond = GetIndex("Readout1_Conductance_S");

            int idxR2Label = GetIndex("Readout2_Label");
            int idxR2TargetDelay = GetIndex("Readout2_TargetDelayAfterLastSpikeEnd_ms");
            int idxR2ActualDelay = GetIndex("Readout2_ActualDelayAfterLastSpikeEnd_ms");
            int idxR2Current = GetIndex("Readout2_Current_A");
            int idxR2Delta = GetIndex("Readout2_DeltaCurrent_A");
            int idxR2Cond = GetIndex("Readout2_Conductance_S");

            int idxR3Label = GetIndex("Readout3_Label");
            int idxR3TargetDelay = GetIndex("Readout3_TargetDelayAfterLastSpikeEnd_ms");
            int idxR3ActualDelay = GetIndex("Readout3_ActualDelayAfterLastSpikeEnd_ms");
            int idxR3Current = GetIndex("Readout3_Current_A");
            int idxR3Delta = GetIndex("Readout3_DeltaCurrent_A");
            int idxR3Cond = GetIndex("Readout3_Conductance_S");

            // If we don't have essential columns, fallback to default parsing
            if (idxTrial == -1 || idxR1Current == -1)
            {
                // Fallback: just parse first two columns into ResultPoints
                foreach (var line in dataLines)
                {
                    var parts = line.Split(separator);
                    if (parts.Length >= 2)
                    {
                        if (SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[0], out double x) &&
                            SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[1], out double y))
                        {
                            ResultPoints.Add(new CurvePoint(x, y));
                        }
                    }
                }
                return;
            }

            foreach (var line in dataLines)
            {
                var parts = line.Split(separator).Select(p => p.Trim()).ToArray();
                if (parts.Length < headers.Count) continue;

                int trialIndex = int.TryParse(parts[idxTrial], out var tri) ? tri : 0;
                int repIndex = int.TryParse(parts[idxRep], out var rep) ? rep : 0;
                int patIndex = int.TryParse(parts[idxPat], out var pat) ? pat : 0;
                string gapOrder = idxGapOrder != -1 ? parts[idxGapOrder] : string.Empty;
                double gap1 = idxGap1 != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxGap1], out var g1) ? g1 : 0.0;
                double gap2 = idxGap2 != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxGap2], out var g2) ? g2 : 0.0;
                double gap3 = idxGap3 != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxGap3], out var g3) ? g3 : 0.0;
                string spikeTimes = idxSpikeTimes != -1 ? parts[idxSpikeTimes] : string.Empty;
                string spikeEndTimes = idxSpikeEndTimes != -1 ? parts[idxSpikeEndTimes] : string.Empty;
                double lastSpikeStart = idxLastSpikeStart != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxLastSpikeStart], out var lss) ? lss : 0.0;
                double lastSpikeEnd = idxLastSpikeEnd != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxLastSpikeEnd], out var lse) ? lse : 0.0;
                string actualReadoutOrder = idxActualReadoutOrder != -1 ? parts[idxActualReadoutOrder] : string.Empty;
                double baseline = idxBaselineCurrent != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxBaselineCurrent], out var bl) ? bl : 0.0;

                // Readout 1
                string r1Label = idxR1Label != -1 ? parts[idxR1Label] : "A";
                double r1TargetDelay = idxR1TargetDelay != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxR1TargetDelay], out var r1td) ? r1td : 0.0;
                double r1ActualDelay = idxR1ActualDelay != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxR1ActualDelay], out var r1ad) ? r1ad : 0.0;
                double r1Current = SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxR1Current], out var r1c) ? r1c : 0.0;
                double r1Delta = idxR1Delta != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxR1Delta], out var r1d) ? r1d : 0.0;
                double r1Cond = idxR1Cond != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxR1Cond], out var r1co) ? r1co : 0.0;

                var readout1 = new ReadoutMeasurement(1, r1Label, r1TargetDelay, r1ActualDelay, r1Current, r1Delta, r1Cond);

                // Readout 2
                string r2Label = idxR2Label != -1 ? parts[idxR2Label] : "B";
                double r2TargetDelay = idxR2TargetDelay != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxR2TargetDelay], out var r2td) ? r2td : 0.0;
                double r2ActualDelay = idxR2ActualDelay != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxR2ActualDelay], out var r2ad) ? r2ad : 0.0;
                double r2Current = SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxR2Current], out var r2c) ? r2c : 0.0;
                double r2Delta = idxR2Delta != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxR2Delta], out var r2d) ? r2d : 0.0;
                double r2Cond = idxR2Cond != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxR2Cond], out var r2co) ? r2co : 0.0;

                var readout2 = new ReadoutMeasurement(2, r2Label, r2TargetDelay, r2ActualDelay, r2Current, r2Delta, r2Cond);

                // Readout 3
                string r3Label = idxR3Label != -1 ? parts[idxR3Label] : "C";
                double r3TargetDelay = idxR3TargetDelay != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxR3TargetDelay], out var r3td) ? r3td : 0.0;
                double r3ActualDelay = idxR3ActualDelay != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxR3ActualDelay], out var r3ad) ? r3ad : 0.0;
                double r3Current = SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxR3Current], out var r3c) ? r3c : 0.0;
                double r3Delta = idxR3Delta != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxR3Delta], out var r3d) ? r3d : 0.0;
                double r3Cond = idxR3Cond != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxR3Cond], out var r3co) ? r3co : 0.0;

                var readout3 = new ReadoutMeasurement(3, r3Label, r3TargetDelay, r3ActualDelay, r3Current, r3Delta, r3Cond);

                var trialResult = new SpikeTimingTrialResult(
                    trialIndex, repIndex, patIndex, gapOrder,
                    gap1, gap2, gap3, spikeTimes, spikeEndTimes,
                    lastSpikeStart, lastSpikeEnd, actualReadoutOrder,
                    baseline, readout1, readout2, readout3
                );

                TrialResults.Add(trialResult);
            }

            // Also populate ResultPoints from the last trial (as is done in a normal measurement run)
            if (TrialResults.Any())
            {
                var lastTrial = TrialResults.Last();
                UpdateLatestTrialCurve(new[] { lastTrial.Readout1, lastTrial.Readout2, lastTrial.Readout3 });
            }
        }

        private void UpdateLatestTrialCurve(IEnumerable<ReadoutMeasurement> readouts)
        {
            ResultPoints.Clear();
            foreach (var r in readouts.OrderBy(r => r.TargetDelayAfterLastSpikeEndMs))
            {
                ResultPoints.Add(new CurvePoint(r.TargetDelayAfterLastSpikeEndMs, r.ReadCurrentA));
            }
        }

        private IReadOnlyList<PlotSeries> BuildAveragePlotSeries()
        {
            var series = new List<PlotSeries>();

            foreach (var group in TrialResults
                         .GroupBy(r => new { r.PatternIndex, r.GapOrder })
                         .OrderBy(g => g.Key.PatternIndex))
            {
                var readout1 = group.Select(r => r.Readout1).ToList();
                var readout2 = group.Select(r => r.Readout2).ToList();
                var readout3 = group.Select(r => r.Readout3).ToList();

                var points = new[]
                {
                    AverageReadoutPoint(readout1),
                    AverageReadoutPoint(readout2),
                    AverageReadoutPoint(readout3)
                }
                .Where(p => p is not null)
                .Cast<CurvePoint>()
                .OrderBy(p => p.X)
                .ToList();

                if (points.Count >= 2)
                {
                    series.Add(new PlotSeries(group.Key.GapOrder, points));
                }
            }

            if (series.Count > 0)
            {
                return series;
            }

            return ResultPoints.Count >= 2
                ? new List<PlotSeries> { new PlotSeries("Latest trial", ResultPoints.ToList()) }
                : new List<PlotSeries>();
        }

        private static CurvePoint? AverageReadoutPoint(IReadOnlyCollection<ReadoutMeasurement> readouts)
        {
            if (readouts.Count == 0) return null;

            double meanDelay = readouts.Average(r => r.TargetDelayAfterLastSpikeEndMs);
            double meanCurrent = readouts.Average(r => r.ReadCurrentA);
            return new CurvePoint(meanDelay, meanCurrent);
        }

        /// <summary>
        /// Detailed CSV export for this measurement plan. One row is written per full trial.
        /// The three readouts after the same spike pattern are stored in separate columns.
        /// </summary>
        public List<string> GetDetailedCsvLines()
        {
            var lines = new List<string>
            {
                "sep=\t",
                "TrialIndex\tRepetitionIndex\tPatternIndex\tGapOrder\tGap1_ms\tGap2_ms\tGap3_ms\tSpikeTimes_ms\tSpikeEndTimes_ms\tLastSpikeStart_ms\tLastSpikeEnd_ms\tActualReadoutOrder\tBaselineCurrent_A\tReadout1_Label\tReadout1_TargetDelayAfterLastSpikeEnd_ms\tReadout1_ActualDelayAfterLastSpikeEnd_ms\tReadout1_Current_A\tReadout1_DeltaCurrent_A\tReadout1_Conductance_S\tReadout2_Label\tReadout2_TargetDelayAfterLastSpikeEnd_ms\tReadout2_ActualDelayAfterLastSpikeEnd_ms\tReadout2_Current_A\tReadout2_DeltaCurrent_A\tReadout2_Conductance_S\tReadout3_Label\tReadout3_TargetDelayAfterLastSpikeEnd_ms\tReadout3_ActualDelayAfterLastSpikeEnd_ms\tReadout3_Current_A\tReadout3_DeltaCurrent_A\tReadout3_Conductance_S\tTimeConstantA_ms\tTimeConstantB_ms\tTimeConstantC_ms\tSpikeVoltage_V\tSpikeLength_ms\tReadVoltage_V\tReadPulseLength_ms\tCompliance_A\tShuffleExecutionOrder\tShuffleSeed\tResetEnabled\tResetVoltage_V\tResetPulseLength_ms\tResetRepetitions\tResetRecovery_ms"
            };

            var settings = ReadAndValidateSettings();
            foreach (var r in TrialResults)
            {
                lines.Add(string.Join("\t", new[]
                {
                    r.TrialIndex.ToString(CultureInfo.InvariantCulture),
                    r.RepetitionIndex.ToString(CultureInfo.InvariantCulture),
                    r.PatternIndex.ToString(CultureInfo.InvariantCulture),
                    Csv(r.GapOrder),
                    r.Gap1Ms.ToString("G9", CultureInfo.InvariantCulture),
                    r.Gap2Ms.ToString("G9", CultureInfo.InvariantCulture),
                    r.Gap3Ms.ToString("G9", CultureInfo.InvariantCulture),
                    Csv(r.SpikeTimesMs),
                    Csv(r.SpikeEndTimesMs),
                    r.LastSpikeStartMs.ToString("G9", CultureInfo.InvariantCulture),
                    r.LastSpikeEndMs.ToString("G9", CultureInfo.InvariantCulture),
                    Csv(r.ActualReadoutOrder),
                    r.BaselineCurrentA.ToString("E9", CultureInfo.InvariantCulture),

                    Csv(r.Readout1.ReadoutLabel),
                    r.Readout1.TargetDelayAfterLastSpikeEndMs.ToString("G9", CultureInfo.InvariantCulture),
                    r.Readout1.ActualDelayAfterLastSpikeEndMs.ToString("G9", CultureInfo.InvariantCulture),
                    r.Readout1.ReadCurrentA.ToString("E9", CultureInfo.InvariantCulture),
                    r.Readout1.DeltaCurrentA.ToString("E9", CultureInfo.InvariantCulture),
                    r.Readout1.ConductanceS.ToString("E9", CultureInfo.InvariantCulture),

                    Csv(r.Readout2.ReadoutLabel),
                    r.Readout2.TargetDelayAfterLastSpikeEndMs.ToString("G9", CultureInfo.InvariantCulture),
                    r.Readout2.ActualDelayAfterLastSpikeEndMs.ToString("G9", CultureInfo.InvariantCulture),
                    r.Readout2.ReadCurrentA.ToString("E9", CultureInfo.InvariantCulture),
                    r.Readout2.DeltaCurrentA.ToString("E9", CultureInfo.InvariantCulture),
                    r.Readout2.ConductanceS.ToString("E9", CultureInfo.InvariantCulture),

                    Csv(r.Readout3.ReadoutLabel),
                    r.Readout3.TargetDelayAfterLastSpikeEndMs.ToString("G9", CultureInfo.InvariantCulture),
                    r.Readout3.ActualDelayAfterLastSpikeEndMs.ToString("G9", CultureInfo.InvariantCulture),
                    r.Readout3.ReadCurrentA.ToString("E9", CultureInfo.InvariantCulture),
                    r.Readout3.DeltaCurrentA.ToString("E9", CultureInfo.InvariantCulture),
                    r.Readout3.ConductanceS.ToString("E9", CultureInfo.InvariantCulture),

                    settings.TimeConstantA_Ms.ToString("G9", CultureInfo.InvariantCulture),
                    settings.TimeConstantB_Ms.ToString("G9", CultureInfo.InvariantCulture),
                    settings.TimeConstantC_Ms.ToString("G9", CultureInfo.InvariantCulture),
                    settings.SpikeVoltage.ToString("G9", CultureInfo.InvariantCulture),
                    settings.SpikeLengthMs.ToString("G9", CultureInfo.InvariantCulture),
                    settings.ReadVoltage.ToString("G9", CultureInfo.InvariantCulture),
                    settings.ReadPulseLengthMs.ToString("G9", CultureInfo.InvariantCulture),
                    settings.Compliance.ToString("G9", CultureInfo.InvariantCulture),
                    settings.ShuffleExecutionOrder ? "true" : "false",
                    settings.ShuffleSeed.ToString(CultureInfo.InvariantCulture),
                    settings.ResetEnabled ? "true" : "false",
                    settings.ResetVoltage.ToString("G9", CultureInfo.InvariantCulture),
                    settings.ResetPulseLengthMs.ToString("G9", CultureInfo.InvariantCulture),
                    settings.ResetRepetitions.ToString(CultureInfo.InvariantCulture),
                    settings.ResetRecoveryMs.ToString("G9", CultureInfo.InvariantCulture)
                }));
            }

            return lines;
        }

        private SpikeTimingSettings ReadAndValidateSettings()
        {
            string writeChannel = GetParamValueString("WriteChannel");
            string readingChannel = GetParamValueString("ReadingChannel");
            if (string.IsNullOrWhiteSpace(writeChannel)) throw new InvalidOperationException("Write Channel must not be empty.");
            if (string.IsNullOrWhiteSpace(readingChannel)) readingChannel = writeChannel;

            var settings = new SpikeTimingSettings
            {
                WriteChannel = writeChannel.Trim(),
                ReadingChannel = readingChannel.Trim(),
                InvertCurrent = readingChannel.Trim() != writeChannel.Trim(),

                TimeConstantA_Ms = GetParamValueDouble("TimeConstantA_Ms"),
                TimeConstantB_Ms = GetParamValueDouble("TimeConstantB_Ms"),
                TimeConstantC_Ms = GetParamValueDouble("TimeConstantC_Ms"),
                RepetitionsPerPattern = GetParamValueInt("RepetitionsPerPattern"),
                ShuffleExecutionOrder = GetParamValueBool("ShuffleExecutionOrder"),

                SpikeVoltage = GetParamValueDouble("SpikeVoltage"),
                SpikeLengthMs = GetParamValueDouble("SpikeLengthMs"),

                ReadVoltage = GetParamValueDouble("ReadVoltage"),
                ReadPulseLengthMs = GetParamValueDouble("ReadPulseLengthMs"),

                Compliance = GetParamValueDouble("Compliance"),
                ShuffleSeed = GetParamValueInt("ShuffleSeed"),
                BaselineReadEnabled = GetParamValueBool("BaselineReadEnabled"),

                ResetEnabled = GetParamValueBool("ResetEnabled"),
                ResetVoltage = GetParamValueDouble("ResetVoltage"),
                ResetPulseLengthMs = GetParamValueDouble("ResetPulseLengthMs"),
                ResetRepetitions = GetParamValueInt("ResetRepetitions"),
                ResetRecoveryMs = GetParamValueDouble("ResetRecoveryMs")
            };

            if (settings.TimeConstantA_Ms <= 0) throw new InvalidOperationException("Time Constant A must be > 0 ms.");
            if (settings.TimeConstantB_Ms <= 0) throw new InvalidOperationException("Time Constant B must be > 0 ms.");
            if (settings.TimeConstantC_Ms <= 0) throw new InvalidOperationException("Time Constant C must be > 0 ms.");
            if (settings.RepetitionsPerPattern < 1) throw new InvalidOperationException("Repetitions per pattern must be at least 1.");
            if (settings.SpikeLengthMs <= 0) throw new InvalidOperationException("Spike length must be > 0 ms.");
            if (settings.ReadPulseLengthMs <= 0) throw new InvalidOperationException("Read pulse length must be > 0 ms.");
            if (settings.Compliance <= 0) throw new InvalidOperationException("Compliance must be > 0 A.");

            if (settings.ResetEnabled)
            {
                if (settings.ResetPulseLengthMs <= 0) throw new InvalidOperationException("Reset pulse length must be > 0 ms when reset is enabled.");
                if (settings.ResetRepetitions < 1) throw new InvalidOperationException("Reset repetitions must be at least 1 when reset is enabled.");
                if (settings.ResetRecoveryMs < 0) throw new InvalidOperationException("Reset recovery time must be >= 0 ms.");
            }

            return settings;
        }

        private async Task ConfigureSmuAsync(E5263_SMU smu, SpikeTimingSettings s)
        {
            await smu.SendCommandAsync("*RST");
            if (s.ReadingChannel != s.WriteChannel)
            {
                await smu.SendCommandAsync($"CN {s.WriteChannel},{s.ReadingChannel}");
            }
            else
            {
                await smu.SendCommandAsync($"CN {s.WriteChannel}");
            }

            await smu.SendCommandAsync($"MM 1,{s.ReadingChannel}");
            await smu.SendCommandAsync($"CMM {s.ReadingChannel},1");
            await smu.SendCommandAsync($"RV {s.WriteChannel},0");
            await smu.SendCommandAsync($"RI {s.WriteChannel},0");
            await smu.SendCommandAsync("FMT 1,0");
            await smu.SendCommandAsync($"DZ {s.WriteChannel}");

            var setupError = await smu.CheckErrorAsync();
            if (setupError != null) throw new InvalidOperationException($"SMU setup error: {setupError}");
        }

        private async Task ApplyResetAsync(E5263_SMU smu, SpikeTimingSettings s, CancellationToken ct)
        {
            for (int i = 0; i < s.ResetRepetitions; i++)
            {
                ct.ThrowIfCancellationRequested();
                await ApplyVoltagePulseAsync(smu, s.WriteChannel, s.ResetVoltage, s.ResetPulseLengthMs, s.Compliance, ct);
            }

            if (s.ResetRecoveryMs > 0)
            {
                await WaitMillisecondsAccurateAsync(s.ResetRecoveryMs, ct);
            }
        }

        private async Task RunSpikePatternAsync(E5263_SMU smu, SpikeTimingSettings s, SpikePattern pattern, Stopwatch trialStopwatch, CancellationToken ct, Action<double>? progressCallback = null)
        {
            progressCallback?.Invoke(0.0);

            for (int i = 0; i < pattern.SpikeStartTimesMs.Length; i++)
            {
                double targetStartMs = pattern.SpikeStartTimesMs[i];
                await WaitUntilElapsedAsync(trialStopwatch, targetStartMs, ct, progressCallback, Math.Max(pattern.LastSpikeEndMs, 1.0));
                await ApplyVoltagePulseAsync(smu, s.WriteChannel, s.SpikeVoltage, s.SpikeLengthMs, s.Compliance, ct);
                progressCallback?.Invoke(Math.Clamp(trialStopwatch.Elapsed.TotalMilliseconds / Math.Max(pattern.LastSpikeEndMs, 1.0), 0.0, 1.0));
            }

            await WaitUntilElapsedAsync(trialStopwatch, pattern.LastSpikeEndMs, ct, progressCallback, Math.Max(pattern.LastSpikeEndMs, 1.0));
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

        private async Task<double> ReadCurrentPulseAsync(E5263_SMU smu, SpikeTimingSettings s, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await smu.SendCommandAsync($"DZ {s.WriteChannel}");
            await smu.SendCommandAsync(FormattableString.Invariant($"DV {s.WriteChannel},0,{s.ReadVoltage},{s.Compliance}"));
            await WaitMillisecondsAccurateAsync(5, ct);
            await smu.SendCommandAsync("XE");
            await WaitMillisecondsAccurateAsync(s.ReadPulseLengthMs, ct);
            await WaitMillisecondsAccurateAsync(10, ct);

            string response = await smu.ReadResponseAsync(100);
            await smu.SendCommandAsync($"DZ {s.WriteChannel}");

            return ParseCurrent(response, s.InvertCurrent);
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

        private static async Task WaitMillisecondsAccurateAsync(double ms, CancellationToken ct)
        {
            if (ms <= 0) return;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < ms)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(1, ct);
            }
        }

        private static async Task WaitUntilElapsedAsync(Stopwatch sw, double targetElapsedMs, CancellationToken ct, Action<double>? progressCallback = null, double totalDurationMs = 0.0)
        {
            while (sw.Elapsed.TotalMilliseconds < targetElapsedMs)
            {
                ct.ThrowIfCancellationRequested();
                if (progressCallback != null && totalDurationMs > 0)
                {
                    progressCallback(Math.Clamp(sw.Elapsed.TotalMilliseconds / totalDurationMs, 0.0, 1.0));
                }

                double remaining = targetElapsedMs - sw.Elapsed.TotalMilliseconds;
                int delay = remaining > 5 ? 2 : 1;
                await Task.Delay(delay, ct);
            }

            if (progressCallback != null && totalDurationMs > 0)
            {
                progressCallback(Math.Clamp(targetElapsedMs / totalDurationMs, 0.0, 1.0));
            }
        }

        private static List<SpikePattern> GenerateTimeConstantPatterns(SpikeTimingSettings s)
        {
            var labels = new[] { "A", "B", "C" };
            var values = new[] { s.TimeConstantA_Ms, s.TimeConstantB_Ms, s.TimeConstantC_Ms };
            int[][] orders =
            {
                new[] { 0, 1, 2 },
                new[] { 0, 2, 1 },
                new[] { 1, 0, 2 },
                new[] { 1, 2, 0 },
                new[] { 2, 0, 1 },
                new[] { 2, 1, 0 }
            };

            var patterns = new List<SpikePattern>();

            foreach (var order in orders)
            {
                var gaps = order.Select(i => values[i]).ToArray();
                var orderLabel = string.Join("-", order.Select(i => labels[i]));

                double[] starts = new double[4];
                starts[0] = 0.0;
                for (int i = 1; i < starts.Length; i++)
                {
                    starts[i] = starts[i - 1] + s.SpikeLengthMs + gaps[i - 1];
                }

                double[] ends = starts.Select(t => t + s.SpikeLengthMs).ToArray();
                patterns.Add(new SpikePattern(
                    PatternIndex: patterns.Count + 1,
                    GapOrder: orderLabel,
                    GapsMs: gaps,
                    SpikeStartTimesMs: starts,
                    SpikeEndTimesMs: ends));
            }

            return patterns;
        }

        private static List<ScheduledTrial> BuildExecutionSchedule(List<SpikePattern> patterns, SpikeTimingSettings s)
        {
            var schedule = new List<ScheduledTrial>();
            for (int rep = 1; rep <= s.RepetitionsPerPattern; rep++)
            {
                foreach (var pattern in patterns)
                {
                    schedule.Add(new ScheduledTrial(rep, pattern));
                }
            }

            if (s.ShuffleExecutionOrder)
            {
                var rng = new Random(s.ShuffleSeed);
                for (int i = schedule.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (schedule[i], schedule[j]) = (schedule[j], schedule[i]);
                }
            }

            return schedule;
        }

        private static string Csv(string value)
        {
            if (value.Contains('\t') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return value;
        }

        private sealed class SpikeTimingSettings
        {
            public string WriteChannel { get; init; } = string.Empty;
            public string ReadingChannel { get; init; } = string.Empty;
            public bool InvertCurrent { get; init; }

            public double TimeConstantA_Ms { get; init; }
            public double TimeConstantB_Ms { get; init; }
            public double TimeConstantC_Ms { get; init; }
            public int RepetitionsPerPattern { get; init; }
            public bool ShuffleExecutionOrder { get; init; }
            public int ShuffleSeed { get; init; }

            public double SpikeVoltage { get; init; }
            public double SpikeLengthMs { get; init; }

            public double ReadVoltage { get; init; }
            public double ReadPulseLengthMs { get; init; }

            public double Compliance { get; init; }
            public bool BaselineReadEnabled { get; init; }

            public bool ResetEnabled { get; init; }
            public double ResetVoltage { get; init; }
            public double ResetPulseLengthMs { get; init; }
            public int ResetRepetitions { get; init; }
            public double ResetRecoveryMs { get; init; }

            public ReadoutSpec[] Readouts => new[]
            {
                new ReadoutSpec(1, "A", TimeConstantA_Ms),
                new ReadoutSpec(2, "B", TimeConstantB_Ms),
                new ReadoutSpec(3, "C", TimeConstantC_Ms)
            };
        }

        private sealed record ReadoutSpec(
            int ReadoutNumber,
            string Label,
            double TargetDelayAfterLastSpikeEndMs);

        public sealed record ReadoutMeasurement(
            int ReadoutNumber,
            string ReadoutLabel,
            double TargetDelayAfterLastSpikeEndMs,
            double ActualDelayAfterLastSpikeEndMs,
            double ReadCurrentA,
            double DeltaCurrentA,
            double ConductanceS);

        private sealed record SpikePattern(
            int PatternIndex,
            string GapOrder,
            double[] GapsMs,
            double[] SpikeStartTimesMs,
            double[] SpikeEndTimesMs)
        {
            public double LastSpikeStartMs => SpikeStartTimesMs.Last();
            public double LastSpikeEndMs => SpikeEndTimesMs.Last();

            public SpikeTimingPatternInfo ToInfo() => new(
                PatternIndex,
                GapOrder,
                string.Join(";", GapsMs.Select(v => v.ToString("G9", CultureInfo.InvariantCulture))),
                string.Join(";", SpikeStartTimesMs.Select(v => v.ToString("G9", CultureInfo.InvariantCulture))),
                LastSpikeEndMs);
        }

        private sealed record ScheduledTrial(int RepetitionIndex, SpikePattern Pattern);

        public sealed record SpikeTimingPatternInfo(
            int PatternIndex,
            string GapOrder,
            string GapsMs,
            string SpikeStartTimesMs,
            double LastSpikeEndMs);

        public sealed record SpikeTimingTrialResult(
            int TrialIndex,
            int RepetitionIndex,
            int PatternIndex,
            string GapOrder,
            double Gap1Ms,
            double Gap2Ms,
            double Gap3Ms,
            string SpikeTimesMs,
            string SpikeEndTimesMs,
            double LastSpikeStartMs,
            double LastSpikeEndMs,
            string ActualReadoutOrder,
            double BaselineCurrentA,
            ReadoutMeasurement Readout1,
            ReadoutMeasurement Readout2,
            ReadoutMeasurement Readout3);
    }
}
