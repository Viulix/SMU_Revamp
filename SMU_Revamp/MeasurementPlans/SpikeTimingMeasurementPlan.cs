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
    ///     reset -> baseline read -> four-spike pattern -> user-defined readout pulses after the last spike end.
    ///
    /// CSV export writes one row per readout pulse. Readout pulse start time, voltage, pulse length, and measured current are exported explicitly.
    ///
    /// The normal ResultPoints list contains the latest completed trial as I(t):
    ///     x = actual readout pulse start after last spike end, y = measured current.
    /// Detailed metadata is exported through GetCsvLines().
    /// </summary>
    public sealed class SpikeTimingMeasurementPlan : MeasurementPlanBase
    {
        public override string Name => "Spike Timing";
        public override string Description => "Applies four-spike patterns generated from the six permutations of three device time constants, resets between trials, and records user-defined pulsed readouts after each pattern.";

        public override string PlotTitle => "Time-Constant Spike Response";
        public override string XAxisLabel => "Delay after Last Spike End (ms)";
        public override string YAxisLabel => "Read Current (A)";
        public override bool ShowLogPlot => true;
        public override double PlotAspectRatio => 3.0;
        public override PlotStyle DefaultPlotStyle => PlotStyle.LineAndScatter;

        public override IReadOnlyList<PlotSeries> PlotSeries => BuildAveragePlotSeries();

        /// <summary>
        /// Full metadata for every trial. There is one result row per spike pattern execution.
        /// </summary>
        public List<SpikeTimingTrialResult> TrialResults { get; } = new();

        /// <summary>
        /// The six generated gap-order patterns for the most recent run.
        /// </summary>
        public List<SpikeTimingPatternInfo> GeneratedPatterns { get; } = new();



        public SpikeTimingMeasurementPlan()
        {
            var spikeVolt = new MeasurementParameter { Name = "SpikeVoltage", DisplayName = "Spike Voltage (V):", Type = ParameterType.Number, Tooltip = "Voltage amplitude of each timing spike.", Section = "Spike Settings" };
            var resetVolt = new MeasurementParameter { Name = "ResetVoltage", DisplayName = "Reset Voltage (V):", Type = ParameterType.Number, Tooltip = "Reset pulse voltage.", Section = "Reset Settings" };

            Parameters = new List<MeasurementParameter>
            {
                new() { Name = "WriteChannel", DisplayName = "Write Channel:", Type = ParameterType.Text, Tooltip = "The SMU source channel number (e.g. 2).", Section = "Channel Settings" },
                new() { Name = "ReadingChannel", DisplayName = "Reading Channel:", Type = ParameterType.Text, Tooltip = "The SMU channel to measure. Leave identical to Write Channel for two-terminal devices.", Section = "Channel Settings" },

                new() { Name = "TimeConstantA_Ms", DisplayName = "Time Constant A (ms):", Type = ParameterType.Number, Tooltip = "First device time constant. Used as one possible break between spikes and as one readout delay after the last spike.", Section = "Time Constants" },
                new() { Name = "TimeConstantB_Ms", DisplayName = "Time Constant B (ms):", Type = ParameterType.Number, Tooltip = "Second device time constant. Used as one possible break between spikes and as one readout delay after the last spike.", Section = "Time Constants" },
                new() { Name = "TimeConstantC_Ms", DisplayName = "Time Constant C (ms):", Type = ParameterType.Number, Tooltip = "Third device time constant. Used as one possible break between spikes and as one readout delay after the last spike.", Section = "Time Constants" },
                new() { Name = "RepetitionsPerPattern", DisplayName = "Repetitions per Pattern:", Type = ParameterType.Number, Tooltip = "How many times every A/B/C permutation is measured. The device is reset before each trial.", Section = "Time Constants" },
                new() { Name = "ShuffleExecutionOrder", DisplayName = "Shuffle Execution Order:", Type = ParameterType.Checkbox, Tooltip = "Measure the repeated pattern list in pseudo-random order instead of grouped order. The shuffle is reproducible through the seed.", Section = "Time Constants" },

                spikeVolt,
                new() { Name = "SpikeLengthMs", DisplayName = "Spike Length (ms):", Type = ParameterType.Number, Tooltip = "Duration of each spike in milliseconds. The plan always uses four spikes.", Section = "Spike Settings" },

                new() { Name = "NumberOfReadoutPulses", DisplayName = "Number of Readout Pulses:", Type = ParameterType.Number, Tooltip = "How many readout pulses are applied after the last input spike.", Section = "Readout Settings" },
                new() { Name = "ReadoutStartTimesMs", DisplayName = "Readout Start Times after Last Spike (ms):", Type = ParameterType.Text, Tooltip = "Semicolon separated list. Each value is the start time of one readout pulse after the last spike end, e.g. 50;100;200.", Section = "Readout Settings" },
                new() { Name = "ReadoutVoltages", DisplayName = "Readout Voltages (V):", Type = ParameterType.Text, Tooltip = "Semicolon separated list of readout voltages. Use one value to apply the same voltage to all readout pulses, e.g. 0.3.", Section = "Readout Settings" },
                new() { Name = "ReadoutPulseLengthsMs", DisplayName = "Readout Pulse Lengths (ms):", Type = ParameterType.Text, Tooltip = "Semicolon separated list of readout pulse lengths. Use one value to apply the same pulse length to all readout pulses, e.g. 20.", Section = "Readout Settings" },

                new() { Name = "Compliance", DisplayName = "Compliance (A):", Type = ParameterType.Number, Tooltip = "Current compliance used for spike, reset, baseline, and read pulses.", Section = "Advanced / Safety" },
                new() { Name = "ShuffleSeed", DisplayName = "Shuffle Seed:", Type = ParameterType.Number, Tooltip = "Integer seed for reproducible pseudo-random execution order when shuffling is enabled.", Section = "Advanced / Safety" },
                new() { Name = "BaselineReadEnabled", DisplayName = "Baseline Read Before Pattern:", Type = ParameterType.Checkbox, Tooltip = "Measure baseline current after reset and before the spike train.", Section = "Advanced / Safety" },

                new() { Name = "ResetEnabled", DisplayName = "Reset Before Each Trial:", Type = ParameterType.Checkbox, Tooltip = "Apply reset pulse(s) before every pattern repetition.", Section = "Reset Settings" },
                resetVolt,
                new() { Name = "ResetPulseLengthMs", DisplayName = "Reset Pulse Length (ms):", Type = ParameterType.Number, Tooltip = "Duration of one reset pulse.", Section = "Reset Settings" },
                new() { Name = "ResetRepetitions", DisplayName = "Reset Repetitions:", Type = ParameterType.Number, Tooltip = "Number of reset pulses before each trial.", Section = "Reset Settings" },
                new() { Name = "ResetRecoveryMs", DisplayName = "Reset Recovery Time (ms):", Type = ParameterType.Number, Tooltip = "Wait time after the reset sequence before baseline/spike train.", Section = "Reset Settings", ScrollStep = 10.0 }
            };

            LoadDefaults();
        }

        protected override Dictionary<string, object> GetParameterDefaults()
        {
            return new Dictionary<string, object>
            {
                { "WriteChannel", "1" },
                { "ReadingChannel", "1" },
                { "TimeConstantA_Ms", 100.0 },
                { "TimeConstantB_Ms", 500.0 },
                { "TimeConstantC_Ms", 1000.0 },
                { "RepetitionsPerPattern", 3 },
                { "ShuffleExecutionOrder", true },
                { "SpikeVoltage", 1.0 },
                { "SpikeLengthMs", 30.0 },
                { "NumberOfReadoutPulses", 3 },
                { "ReadoutStartTimesMs", "100;500;1000" },
                { "ReadoutVoltages", "0.3" },
                { "ReadoutPulseLengthsMs", "20" },
                { "Compliance", 0.01 },
                { "ShuffleSeed", 12345 },
                { "BaselineReadEnabled", true },
                { "ResetEnabled", true },
                { "ResetVoltage", -1.0 },
                { "ResetPulseLengthMs", 100.0 },
                { "ResetRepetitions", 1 },
                { "ResetRecoveryMs", 100.0 }
            };
        }

        public override async Task RunMeasurementAsync(E5263_SMU smu, IProgress<double>? progress = null)
        {
            ResultPoints.Clear();
            TrialResults.Clear();
            GeneratedPatterns.Clear();
            progress?.Report(0);

            var settings = ReadAndValidateSettings();
            var patterns = GenerateTimeConstantPatterns(settings);
            var schedule = BuildExecutionSchedule(patterns, settings);

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
                        baselineCurrent = await ReadCurrentPulseAsync(smu, settings, settings.ReadoutPulses[0].Voltage, settings.ReadoutPulses[0].PulseLengthMs, cts.Token);
                    }
                    ReportTrialProgress(0.22);

                    var trialStopwatch = Stopwatch.StartNew();
                    await RunSpikePatternAsync(smu, settings, item.Pattern, trialStopwatch, cts.Token, patternFraction =>
                    {
                        ReportTrialProgress(0.22 + 0.38 * patternFraction);
                    });
                    ReportTrialProgress(0.60);

                    var sampledPoints = await MeasureReadoutPulsesAsync(smu, settings, item.Pattern, trialStopwatch, cts.Token, readoutFraction =>
                    {
                        ReportTrialProgress(0.60 + 0.35 * readoutFraction);
                    });
                    ReportTrialProgress(0.95);

                    ResultPoints.Clear();
                    ResultPoints.AddRange(sampledPoints);

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
                        BaselineCurrentA: baselineCurrent,
                        SampledPoints: sampledPoints));

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
            int idxBaselineCurrent = GetIndex("BaselineCurrent_A");
            int idxTime = GetIndex("ReadoutActualStartAfterLastSpike_ms");
            if (idxTime == -1) idxTime = GetIndex("TimeAfterLastSpike_ms");
            int idxCurrent = GetIndex("MeasuredCurrent_A");

            if (idxTrial == -1 || idxTime == -1 || idxCurrent == -1)
            {
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

            var trialGroups = new Dictionary<int, (int rep, int pat, string gap, double g1, double g2, double g3, string st, string set, double lss, double lse, double bl, List<CurvePoint> pts)>();

            foreach (var line in dataLines)
            {
                var parts = line.Split(separator).Select(p => p.Trim()).ToArray();
                if (parts.Length < headers.Count) continue;

                if (!int.TryParse(parts[idxTrial], out var trialIndex)) continue;
                if (!SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxTime], out var timeVal)) continue;
                if (!SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxCurrent], out var currentVal)) continue;

                if (!trialGroups.TryGetValue(trialIndex, out var entry))
                {
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
                    double baseline = idxBaselineCurrent != -1 && SMU_Revamp.Services.ParameterConfigHelper.TryParseDoubleRobust(parts[idxBaselineCurrent], out var bl) ? bl : 0.0;

                    entry = (repIndex, patIndex, gapOrder, gap1, gap2, gap3, spikeTimes, spikeEndTimes, lastSpikeStart, lastSpikeEnd, baseline, new List<CurvePoint>());
                    trialGroups[trialIndex] = entry;
                }

                entry.pts.Add(new CurvePoint(timeVal, currentVal));
            }

            foreach (var kp in trialGroups.OrderBy(kv => kv.Key))
            {
                var val = kp.Value;
                var trialResult = new SpikeTimingTrialResult(
                    kp.Key, val.rep, val.pat, val.gap,
                    val.g1, val.g2, val.g3, val.st, val.set,
                    val.lss, val.lse, val.bl, val.pts
                );
                TrialResults.Add(trialResult);
            }

            if (TrialResults.Any())
            {
                ResultPoints.Clear();
                ResultPoints.AddRange(TrialResults.Last().SampledPoints);
            }
        }

        private IReadOnlyList<PlotSeries> BuildAveragePlotSeries()
        {
            var series = new List<PlotSeries>();

            foreach (var group in TrialResults
                         .GroupBy(r => new { r.PatternIndex, r.GapOrder })
                         .OrderBy(g => g.Key.PatternIndex))
            {
                var trials = group.ToList();
                if (trials.Count == 0) continue;

                int maxPoints = trials.Max(t => t.SampledPoints.Count);
                var avgPoints = new List<CurvePoint>();

                for (int k = 0; k < maxPoints; k++)
                {
                    var pointsAtIndex = trials
                        .Where(t => k < t.SampledPoints.Count)
                        .Select(t => t.SampledPoints[k])
                        .ToList();

                    if (pointsAtIndex.Count > 0)
                    {
                        double avgX = pointsAtIndex.Average(p => p.X);
                        double avgY = pointsAtIndex.Average(p => p.Y);
                        double? yError = null;

                        if (pointsAtIndex.Count > 1)
                        {
                            double variance = pointsAtIndex.Sum(p => Math.Pow(p.Y - avgY, 2.0)) / (pointsAtIndex.Count - 1);
                            yError = Math.Sqrt(variance);
                        }

                        avgPoints.Add(new CurvePoint(avgX, avgY, yError));
                    }
                }

                if (avgPoints.Count >= 1)
                {
                    series.Add(new PlotSeries(group.Key.GapOrder, avgPoints));
                }
            }

            if (series.Count > 0)
            {
                return series;
            }

            return ResultPoints.Count >= 1
                ? new List<PlotSeries> { new PlotSeries("Latest trial", ResultPoints.ToList()) }
                : new List<PlotSeries>();
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
                "TrialIndex\tRepetitionIndex\tPatternIndex\tGapOrder\tGap1_ms\tGap2_ms\tGap3_ms\tSpikeTimes_ms\tSpikeEndTimes_ms\tLastSpikeStart_ms\tLastSpikeEnd_ms\tBaselineCurrent_A\tReadoutNumber\tReadoutTargetStartAfterLastSpike_ms\tReadoutActualStartAfterLastSpike_ms\tReadoutVoltage_V\tReadoutPulseLength_ms\tMeasuredCurrent_A\tDeltaCurrent_A\tTimeConstantA_ms\tTimeConstantB_ms\tTimeConstantC_ms\tSpikeVoltage_V\tSpikeLength_ms\tCompliance_A\tShuffleExecutionOrder\tShuffleSeed\tResetEnabled\tResetVoltage_V\tResetPulseLength_ms\tResetRepetitions\tResetRecovery_ms"
            };

            var settings = ReadAndValidateSettings();
            foreach (var r in TrialResults)
            {
                for (int k = 0; k < r.SampledPoints.Count; k++)
                {
                    var pt = r.SampledPoints[k];
                    var ro = k < settings.ReadoutPulses.Count ? settings.ReadoutPulses[k] : new ReadoutPulseSetting(k + 1, double.NaN, double.NaN, double.NaN);
                    double deltaCurrent = double.IsNaN(r.BaselineCurrentA) ? double.NaN : pt.Y - r.BaselineCurrentA;

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
                        r.BaselineCurrentA.ToString("E9", CultureInfo.InvariantCulture),
                        ro.ReadoutNumber.ToString(CultureInfo.InvariantCulture),
                        ro.StartAfterLastSpikeMs.ToString("G9", CultureInfo.InvariantCulture),
                        pt.X.ToString("G9", CultureInfo.InvariantCulture),
                        ro.Voltage.ToString("G9", CultureInfo.InvariantCulture),
                        ro.PulseLengthMs.ToString("G9", CultureInfo.InvariantCulture),
                        pt.Y.ToString("E9", CultureInfo.InvariantCulture),
                        deltaCurrent.ToString("E9", CultureInfo.InvariantCulture),

                        settings.TimeConstantA_Ms.ToString("G9", CultureInfo.InvariantCulture),
                        settings.TimeConstantB_Ms.ToString("G9", CultureInfo.InvariantCulture),
                        settings.TimeConstantC_Ms.ToString("G9", CultureInfo.InvariantCulture),
                        settings.SpikeVoltage.ToString("G9", CultureInfo.InvariantCulture),
                        settings.SpikeLengthMs.ToString("G9", CultureInfo.InvariantCulture),
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

                NumberOfReadoutPulses = GetParamValueInt("NumberOfReadoutPulses"),
                ReadoutStartTimesRaw = GetParamValueString("ReadoutStartTimesMs"),
                ReadoutVoltagesRaw = GetParamValueString("ReadoutVoltages"),
                ReadoutPulseLengthsRaw = GetParamValueString("ReadoutPulseLengthsMs"),

                Compliance = GetParamValueDouble("Compliance"),
                ShuffleSeed = GetParamValueInt("ShuffleSeed"),
                BaselineReadEnabled = GetParamValueBool("BaselineReadEnabled"),

                ResetEnabled = GetParamValueBool("ResetEnabled"),
                ResetVoltage = GetParamValueDouble("ResetVoltage"),
                ResetPulseLengthMs = GetParamValueDouble("ResetPulseLengthMs"),
                ResetRepetitions = GetParamValueInt("ResetRepetitions"),
                ResetRecoveryMs = GetParamValueDouble("ResetRecoveryMs")
            };

            settings.ReadoutPulses = BuildReadoutPulseSettings(settings);

            if (settings.TimeConstantA_Ms <= 0) throw new InvalidOperationException("Time Constant A must be > 0 ms.");
            if (settings.TimeConstantB_Ms <= 0) throw new InvalidOperationException("Time Constant B must be > 0 ms.");
            if (settings.TimeConstantC_Ms <= 0) throw new InvalidOperationException("Time Constant C must be > 0 ms.");
            if (settings.RepetitionsPerPattern < 1) throw new InvalidOperationException("Repetitions per pattern must be at least 1.");
            if (settings.SpikeLengthMs <= 0) throw new InvalidOperationException("Spike length must be > 0 ms.");
            if (settings.NumberOfReadoutPulses < 1) throw new InvalidOperationException("Number of readout pulses must be at least 1.");
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

                await ApplyVoltagePulseAsync(smu, s.WriteChannel, s.SpikeVoltage, s.SpikeLengthMs, s.Compliance, ct, 0.0);
                progressCallback?.Invoke(Math.Clamp(trialStopwatch.Elapsed.TotalMilliseconds / Math.Max(pattern.LastSpikeEndMs, 1.0), 0.0, 1.0));
            }

            await WaitUntilElapsedAsync(trialStopwatch, pattern.LastSpikeEndMs, ct, progressCallback, Math.Max(pattern.LastSpikeEndMs, 1.0));
            progressCallback?.Invoke(1.0);
        }

        private async Task ApplyVoltagePulseAsync(E5263_SMU smu, string channel, double voltage, double pulseLengthMs, double compliance, CancellationToken ct, double endingVoltage = 0.0)
        {
            ct.ThrowIfCancellationRequested();
            await smu.SendCommandAsync($"DZ {channel}");
            await smu.SendCommandAsync(FormattableString.Invariant($"DV {channel},0,{voltage},{compliance}"));
            await WaitMillisecondsAccurateAsync(pulseLengthMs, ct);
            if (endingVoltage == 0.0)
            {
                await smu.SendCommandAsync($"DZ {channel}");
            }
            else
            {
                await smu.SendCommandAsync(FormattableString.Invariant($"DV {channel},0,{endingVoltage},{compliance}"));
            }
        }

        private async Task<List<CurvePoint>> MeasureReadoutPulsesAsync(E5263_SMU smu, SpikeTimingSettings s, SpikePattern pattern, Stopwatch trialStopwatch, CancellationToken ct, Action<double>? progressCallback = null)
        {
            var points = new List<CurvePoint>();
            if (s.ReadoutPulses.Count == 0) return points;

            await ConfigurePointMeasurementModeAsync(smu, s);

            for (int i = 0; i < s.ReadoutPulses.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var readout = s.ReadoutPulses[i];
                double targetElapsedMs = pattern.LastSpikeEndMs + readout.StartAfterLastSpikeMs;

                await WaitUntilElapsedAsync(trialStopwatch, targetElapsedMs, ct, null, 0.0);
                double actualStartAfterLastSpikeMs = Math.Max(0.0, trialStopwatch.Elapsed.TotalMilliseconds - pattern.LastSpikeEndMs);

                double current = await ReadCurrentPulseAsync(smu, s, readout.Voltage, readout.PulseLengthMs, ct);
                points.Add(new CurvePoint(actualStartAfterLastSpikeMs, current));

                progressCallback?.Invoke((i + 1.0) / s.ReadoutPulses.Count);
            }

            await smu.SendCommandAsync($"DZ {s.WriteChannel}");
            return points;
        }

        private async Task ConfigurePointMeasurementModeAsync(E5263_SMU smu, SpikeTimingSettings s)
        {
            await smu.SendCommandAsync($"MM 1,{s.ReadingChannel}");
            await smu.SendCommandAsync($"CMM {s.ReadingChannel},1");
            await smu.SendCommandAsync($"RI {s.ReadingChannel},0");
        }

        private async Task<double> ReadCurrentPulseAsync(E5263_SMU smu, SpikeTimingSettings s, double voltage, double pulseLengthMs, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            await ConfigurePointMeasurementModeAsync(smu, s);
            await smu.SendCommandAsync($"DZ {s.WriteChannel}");
            await smu.SendCommandAsync(FormattableString.Invariant($"DV {s.WriteChannel},0,{voltage},{s.Compliance}"));

            await smu.SendCommandAsync("TSR");
            await smu.SendCommandAsync("XE");
            await WaitMillisecondsAccurateAsync(pulseLengthMs, ct);
            await smu.SendCommandAsync("TSQ");

            string response = await smu.ReadResponseAsync(512);
            try { _ = await smu.ReadResponseAsync(50); } catch { }

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

        private static List<ReadoutPulseSetting> BuildReadoutPulseSettings(SpikeTimingSettings s)
        {
            int count = s.NumberOfReadoutPulses;
            if (count < 1) throw new InvalidOperationException("Number of readout pulses must be at least 1.");

            var starts = ExpandList(ParseDoubleList(s.ReadoutStartTimesRaw, "Readout start times"), count, "Readout start times");
            var voltages = ExpandList(ParseDoubleList(s.ReadoutVoltagesRaw, "Readout voltages"), count, "Readout voltages");
            var lengths = ExpandList(ParseDoubleList(s.ReadoutPulseLengthsRaw, "Readout pulse lengths"), count, "Readout pulse lengths");

            var readouts = new List<ReadoutPulseSetting>();
            for (int i = 0; i < count; i++)
            {
                if (starts[i] < 0) throw new InvalidOperationException($"Readout pulse {i + 1}: start time must be >= 0 ms.");
                if (lengths[i] <= 0) throw new InvalidOperationException($"Readout pulse {i + 1}: pulse length must be > 0 ms.");

                if (i > 0)
                {
                    double previousEnd = starts[i - 1] + lengths[i - 1];
                    if (starts[i] < previousEnd)
                    {
                        throw new InvalidOperationException($"Readout pulse {i + 1} starts before readout pulse {i} has ended. Use non-overlapping readout pulses.");
                    }
                }

                readouts.Add(new ReadoutPulseSetting(i + 1, starts[i], voltages[i], lengths[i]));
            }

            return readouts;
        }

        private static List<double> ParseDoubleList(string raw, string name)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException($"{name} list must not be empty.");
            }

            var parts = raw.Split(new[] { ';', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var values = new List<double>();

            foreach (var part in parts)
            {
                var cleaned = part.Trim().Trim(',');
                if (cleaned.Length == 0) continue;

                if (!ParameterConfigHelper.TryParseDoubleRobust(cleaned, out double value))
                {
                    throw new InvalidOperationException($"Could not parse value '{part}' in {name} list.");
                }
                values.Add(value);
            }

            if (values.Count == 0)
            {
                throw new InvalidOperationException($"{name} list must contain at least one value.");
            }

            return values;
        }

        private static List<double> ExpandList(List<double> values, int count, string name)
        {
            if (values.Count == count) return values;
            if (values.Count == 1) return Enumerable.Repeat(values[0], count).ToList();
            throw new InvalidOperationException($"{name} must contain either one value or exactly {count} values.");
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

            public int NumberOfReadoutPulses { get; init; }
            public string ReadoutStartTimesRaw { get; init; } = string.Empty;
            public string ReadoutVoltagesRaw { get; init; } = string.Empty;
            public string ReadoutPulseLengthsRaw { get; init; } = string.Empty;
            public List<ReadoutPulseSetting> ReadoutPulses { get; set; } = new();

            public double Compliance { get; init; }
            public bool BaselineReadEnabled { get; init; }

            public bool ResetEnabled { get; init; }
            public double ResetVoltage { get; init; }
            public double ResetPulseLengthMs { get; init; }
            public int ResetRepetitions { get; init; }
            public double ResetRecoveryMs { get; init; }
        }

        public sealed record ReadoutPulseSetting(
            int ReadoutNumber,
            double StartAfterLastSpikeMs,
            double Voltage,
            double PulseLengthMs);

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
            double BaselineCurrentA,
            List<CurvePoint> SampledPoints);
    }
}
