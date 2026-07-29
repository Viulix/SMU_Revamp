using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SMU_Revamp.Models;
using SMU_Revamp.Interfaces;
using SMU_Revamp.Services;

namespace SMU_Revamp.MeasurementPlans
{
    /// <summary>
    /// Electrical analogue of an eyeblink-conditioning experiment.
    ///
    /// One complete block consists of:
    ///     reset -> small-pulse pre-test -> repeated small/large conditioning pairs
    ///     -> small-pulse post-test.
    ///
    /// The small/large timing parameter is the time between their END points:
    ///     gap = largeEnd - smallEnd, gap >= 0.
    /// The small pulse starts at t = 0. Therefore:
    ///     largeStart = smallLength + gap - largeLength.
    /// When the pulses overlap, their voltages are added.
    ///
    /// The stimulus sequence and all user-configurable stimulus parameters are kept
    /// unchanged. The acquisition has been restructured so current is sampled
    /// repeatedly throughout:
    ///     - every Pre and Post small pulse,
    ///     - every active small-only, overlap and large-only segment of the
    ///       complete small/large conditioning pair,
    ///     - the optional large-only baseline pulse,
    ///     - every enabled monitoring readout pulse.
    ///
    /// High-speed current spot measurements (TI) are requested repeatedly as fast
    /// as the SMU/GPIB path permits. Host-side timestamps are recorded at the midpoint
    /// of each completed acquisition. Voltage transitions are prioritized, and a new
    /// acquisition is not started when the estimated acquisition time no longer fits
    /// before the next requested transition.
    /// </summary>
    public sealed class EyeblinkConditioningMeasurementPlan : MeasurementPlanBase, IMeasurementPlan
    {
        private const double TimeEqualityToleranceMs = 1e-7;
        private const double InitialMeasurementDurationEstimateMs = 2.0;
        private const double MeasurementGuardMs = 0.5;
        private const int AcquisitionIoTimeoutMs = 5_000;

        public override string Name => "Eyeblink Conditioning";
        public override string Description => "Continuously samples current during every active pulse while preserving the original eyeblink-conditioning stimulus sequence.";

        public override string PlotTitle => "Small-Stimulus Response Before and After Conditioning";
        public override string XAxisLabel => "Time within Small Pulse (ms)";
        public override string YAxisLabel => "Measured Current (A)";
        public override bool ShowLogPlot => false;
        public override double PlotAspectRatio => 3.0;
        public override PlotStyle DefaultPlotStyle => PlotStyle.LineAndScatter;

        public List<ConditioningBlockResult> BlockResults { get; } = new();
        public LargePulseBaselineResult? LargePulseBaseline { get; private set; }

        public override IReadOnlyList<PlotSeries> PlotSeries => BuildAveragePlotSeries();

        public EyeblinkConditioningMeasurementPlan()
        {
            Parameters = new List<MeasurementParameter>
            {
                new() { Name = "WriteChannel", DisplayName = "Write Channel:", Type = ParameterType.Text, Tooltip = "SMU source channel number.", Section = "Channel Settings" },
                new() { Name = "ReadingChannel", DisplayName = "Reading Channel:", Type = ParameterType.Text, Tooltip = "SMU measurement channel. Use the write channel for a two-terminal single-SMU connection.", Section = "Channel Settings" },
                new() { Name = "Compliance", DisplayName = "Compliance (A):", Type = ParameterType.Number, Tooltip = "Current compliance for all pulses and measurements.", Section = "Channel Settings" },

                new() { Name = "SmallPulseVoltage", DisplayName = "Small Pulse Voltage (V):", Type = ParameterType.Number, Tooltip = "Voltage contribution of the small/conditioned stimulus.", Section = "Small Stimulus" },
                new() { Name = "SmallPulseLengthMs", DisplayName = "Small Pulse Length (ms):", Type = ParameterType.Number, Tooltip = "Duration of every small pulse.", Section = "Small Stimulus" },
                new() { Name = "NumberOfSmallTestPulses", DisplayName = "Number of Small Test Pulses:", Type = ParameterType.Number, Tooltip = "Number of small pulses in both the pre-conditioning and post-conditioning test phases.", Section = "Small Stimulus" },
                new() { Name = "GapBetweenSmallTestPulsesMs", DisplayName = "Gap Between Small Test Pulses (ms):", Type = ParameterType.Number, Tooltip = "Quiet end-to-start time between consecutive small test pulses.", Section = "Small Stimulus" },

                new() { Name = "LargePulseVoltage", DisplayName = "Large Pulse Voltage Contribution (V):", Type = ParameterType.Number, Tooltip = "Voltage contribution of the large/unconditioned stimulus. During overlap the applied voltage is Small + Large.", Section = "Large Stimulus" },
                new() { Name = "LargePulseLengthMs", DisplayName = "Large Pulse Length (ms):", Type = ParameterType.Number, Tooltip = "Duration of every large pulse.", Section = "Large Stimulus" },
                new() { Name = "EnableLargePulseBaseline", DisplayName = "Measure Large-Pulse Baseline Once:", Type = ParameterType.Checkbox, Tooltip = "At the beginning of the entire measurement: reset, continuously sample one large pulse, optionally apply and continuously sample one monitoring pulse, then reset again.", Section = "Large Stimulus" },

                new() { Name = "NumberOfConditioningPairs", DisplayName = "Number of Conditioning Pairs:", Type = ParameterType.Number, Tooltip = "Number of small/large pairings in each conditioning block.", Section = "Conditioning" },
                new() { Name = "GapBetweenConditioningPairsMs", DisplayName = "Gap Between Conditioning Pairs (ms):", Type = ParameterType.Number, Tooltip = "End-to-start time from the end of one complete pulse pair to the start of the next small pulse. The monitoring pulse must fit inside this interval.", Section = "Conditioning" },
                new() { Name = "SmallLargeEndGapListMs", DisplayName = "Small-Large End Gaps (ms):", Type = ParameterType.Text, Tooltip = "Semicolon-separated list. Each value is large-pulse end minus small-pulse end. 0 means both pulses end together; values below the large-pulse length cause overlap.", Section = "Conditioning" },
                new() { Name = "BlockRepetitionsPerGap", DisplayName = "Whole-Block Repetitions per Gap:", Type = ParameterType.Number, Tooltip = "Independent reset -> pre-test -> conditioning -> post-test repetitions for every selected small-large end gap. Error bars are calculated across these independent blocks.", Section = "Conditioning" },

                new() { Name = "EnablePostPairReadout", DisplayName = "Enable Monitoring Pulse after Large Pulses:", Type = ParameterType.Checkbox, Tooltip = "After the optional large-only baseline and after every conditioning pair, apply one monitoring pulse and continuously sample current throughout it.", Section = "Monitoring Readout" },
                new() { Name = "FirstReadoutDelayMs", DisplayName = "Monitoring Pulse Delay after Large End (ms):", Type = ParameterType.Number, Tooltip = "End-to-start delay between the large-pulse end and the single monitoring pulse.", Section = "Monitoring Readout" },
                new() { Name = "ReadoutVoltage", DisplayName = "Monitoring Pulse Voltage (V):", Type = ParameterType.Number, Tooltip = "Voltage of the continuously sampled monitoring pulse.", Section = "Monitoring Readout" },
                new() { Name = "ReadoutPulseLengthMs", DisplayName = "Monitoring Pulse Length (ms):", Type = ParameterType.Number, Tooltip = "Duration of the single continuously sampled monitoring pulse.", Section = "Monitoring Readout" },

                new() { Name = "ResetVoltage", DisplayName = "Reset Voltage (V):", Type = ParameterType.Number, Tooltip = "Reset pulse voltage used before every full block and around the optional large-pulse baseline.", Section = "Reset Settings" },
                new() { Name = "ResetPulseLengthMs", DisplayName = "Reset Pulse Length (ms):", Type = ParameterType.Number, Tooltip = "Duration of one reset pulse.", Section = "Reset Settings" },
                new() { Name = "ResetRepetitions", DisplayName = "Reset Repetitions:", Type = ParameterType.Number, Tooltip = "Number of reset pulses in every reset sequence.", Section = "Reset Settings" },
                new() { Name = "ResetRecoveryMs", DisplayName = "Reset Recovery Time (ms):", Type = ParameterType.Number, Tooltip = "Wait after every reset sequence before the next experiment phase.", Section = "Reset Settings", ScrollStep = 10.0 }
            };

            LoadDefaults();
        }

        protected override Dictionary<string, object> GetParameterDefaults()
        {
            return new Dictionary<string, object>
            {
                { "WriteChannel", "1" },
                { "ReadingChannel", "1" },
                { "Compliance", 0.01 },

                { "SmallPulseVoltage", 0.5 },
                { "SmallPulseLengthMs", 100.0 },
                { "NumberOfSmallTestPulses", 5 },
                { "GapBetweenSmallTestPulsesMs", 500.0 },

                { "LargePulseVoltage", 0.8 },
                { "LargePulseLengthMs", 20.0 },
                { "EnableLargePulseBaseline", true },

                { "NumberOfConditioningPairs", 10 },
                { "GapBetweenConditioningPairsMs", 1000.0 },
                { "SmallLargeEndGapListMs", "200;100;50;20;0" },
                { "BlockRepetitionsPerGap", 3 },

                { "EnablePostPairReadout", true },
                { "FirstReadoutDelayMs", 20.0 },
                { "ReadoutVoltage", 0.2 },
                { "ReadoutPulseLengthMs", 20.0 },

                { "ResetVoltage", -1.0 },
                { "ResetPulseLengthMs", 100.0 },
                { "ResetRepetitions", 1 },
                { "ResetRecoveryMs", 100.0 }
            };
        }

        public override async Task RunMeasurementAsync(E5263_SMU smu, IProgress<double>? progress = null)
        {
            ResultPoints.Clear();
            BlockResults.Clear();
            LargePulseBaseline = null;
            progress?.Report(0.0);

            int originalTimeoutMs = smu.GetTimeout();
            smu.SetTimeout(Math.Min(originalTimeoutMs, AcquisitionIoTimeoutMs));
            bool completedSuccessfully = false;

            try
            {
                var settings = ReadAndValidateSettings();
                await ConfigureSmuAsync(smu, settings);
                progress?.Report(2.0);

                using var cts = new CancellationTokenSource();
                int completedUnits = 0;
                int totalUnits =
                    (settings.EnableLargePulseBaseline ? 1 : 0) +
                    settings.SmallLargeEndGapsMs.Count * settings.BlockRepetitionsPerGap *
                    (settings.NumberOfSmallTestPulses * 2 + settings.NumberOfConditioningPairs);

                void ReportUnitProgress()
                {
                    completedUnits++;
                    double fraction = totalUnits <= 0 ? 1.0 : completedUnits / (double)totalUnits;
                    progress?.Report(2.0 + 96.0 * Math.Clamp(fraction, 0.0, 1.0));
                }

                bool firstBlockAlreadyReset = false;

                if (settings.EnableLargePulseBaseline)
                {
                    await ApplyResetAsync(smu, settings, cts.Token);
                    LargePulseBaseline = await RunLargePulseBaselineAsync(smu, settings, cts.Token);
                    ReportUnitProgress();

                    // This reset both removes the baseline state and serves as the
                    // required reset immediately before the first full block.
                    await ApplyResetAsync(smu, settings, cts.Token);
                    firstBlockAlreadyReset = true;
                }

                for (int gapIndex = 0; gapIndex < settings.SmallLargeEndGapsMs.Count; gapIndex++)
                {
                    double endGapMs = settings.SmallLargeEndGapsMs[gapIndex];

                    for (int repetition = 1; repetition <= settings.BlockRepetitionsPerGap; repetition++)
                    {
                        cts.Token.ThrowIfCancellationRequested();

                        // Every complete pre -> conditioning -> post block starts from a reset state.
                        // The first block may reuse the explicit post-baseline reset above.
                        if (firstBlockAlreadyReset)
                        {
                            firstBlockAlreadyReset = false;
                        }
                        else
                        {
                            await ApplyResetAsync(smu, settings, cts.Token);
                        }

                        var preTraces = await RunSmallTestPhaseAsync(
                            smu,
                            settings,
                            phase: "Pre",
                            waitAfterFinalPulse: true,
                            ct: cts.Token,
                            completedPulseCallback: ReportUnitProgress);

                        var pairResults = await RunConditioningPhaseAsync(
                            smu,
                            settings,
                            endGapMs,
                            cts.Token,
                            ReportUnitProgress);

                        var postTraces = await RunSmallTestPhaseAsync(
                            smu,
                            settings,
                            phase: "Post",
                            waitAfterFinalPulse: false,
                            ct: cts.Token,
                            completedPulseCallback: ReportUnitProgress);

                        var block = new ConditioningBlockResult(
                            GapIndex: gapIndex + 1,
                            SmallLargeEndGapMs: endGapMs,
                            RepetitionIndex: repetition,
                            PreConditioningTraces: preTraces,
                            ConditioningPairs: pairResults,
                            PostConditioningTraces: postTraces);

                        BlockResults.Add(block);
                        UpdateLatestResultPoints(postTraces);

                        var loopError = await smu.CheckErrorAsync();
                        if (loopError != null)
                        {
                            throw new InvalidOperationException($"SMU error during Eyeblink Conditioning block: {loopError}");
                        }
                    }
                }

                progress?.Report(100.0);
                completedSuccessfully = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Eyeblink Conditioning stopped during continuous current acquisition. " +
                    "The source output was disabled. Details: " + ex.Message,
                    ex);
            }
            finally
            {
                // AB is one of the instrument commands that can interrupt an
                // active measurement. Use it on failure before DZ so a timed-out
                // TI request cannot leave the source waiting behind an unfinished
                // acquisition in the instrument command queue.
                if (!completedSuccessfully)
                {
                    try { await smu.SendCommandAsync("AB"); } catch { }
                }

                try { await smu.SendCommandAsync("DZ"); } catch { }
                smu.SetTimeout(originalTimeoutMs);
            }
        }

        // MeasurementPlanBase already implements IMeasurementPlan. Re-declaring the
        // interface and forwarding explicitly ensures calls made through
        // IMeasurementPlan use this plan's detailed export.
        IReadOnlyList<string> IMeasurementPlan.GetCsvLines() => GetCsvLines();

        public IReadOnlyList<string> GetCsvLines()
        {
            var lines = new List<string>
            {
                "sep=\t",
                "# Acquisition: current traces during every small pulse, active conditioning-pair segment, optional large-only baseline pulse, and enabled monitoring pulse.",
                "# Sampling: TI high-speed spot measurements are requested repeatedly as fast as the SMU/GPIB path permits. SampleActualTime_ms is the host-time midpoint of each acquisition.",
                "# Section: Plotted summary - these are the same averaged Pre/Post curves and error bars shown in the measurement viewer"
            };

            var plottedSeries = BuildAveragePlotSeries();
            if (plottedSeries.Count > 0)
            {
                var summaryHeader = new List<string> { "TimeWithinSmallPulse_ms" };
                foreach (var plotSeries in plottedSeries)
                {
                    summaryHeader.Add(Csv($"{plotSeries.Name} MeanCurrent_A"));
                    summaryHeader.Add(Csv($"{plotSeries.Name} StdDev_A"));
                }
                lines.Add(string.Join("\t", summaryHeader));

                int maximumPointCount = plottedSeries.Max(series => series.Points.Count);
                for (int pointIndex = 0; pointIndex < maximumPointCount; pointIndex++)
                {
                    var firstAvailablePoint = plottedSeries
                        .Where(series => series.Points.Count > pointIndex)
                        .Select(series => series.Points[pointIndex])
                        .FirstOrDefault();

                    var row = new List<string>
                    {
                        firstAvailablePoint == null
                            ? string.Empty
                            : firstAvailablePoint.X.ToString("G9", CultureInfo.InvariantCulture)
                    };

                    foreach (var plotSeries in plottedSeries)
                    {
                        if (plotSeries.Points.Count > pointIndex)
                        {
                            var point = plotSeries.Points[pointIndex];
                            row.Add(point.Y.ToString("E9", CultureInfo.InvariantCulture));
                            row.Add(point.YError.HasValue
                                ? point.YError.Value.ToString("E9", CultureInfo.InvariantCulture)
                                : string.Empty);
                        }
                        else
                        {
                            row.Add(string.Empty);
                            row.Add(string.Empty);
                        }
                    }

                    lines.Add(string.Join("\t", row));
                }

                lines.Add(string.Empty);
            }

            lines.Add("# Section: Raw measurements - every current sample with its pulse, pair, block, timing and applied-voltage context");
            lines.Add("# New sample RowTypes: LargeBaselinePulseSample, ConditioningPairSample, LargeBaselineReadoutSample, ConditioningReadoutSample. Existing SmallPulseSample and legacy summary RowTypes are retained.");
            lines.Add("# Non-overlap quiet intervals remain high impedance (DZ) exactly as before and therefore do not produce ConditioningPairSample rows.");

            // The original columns are kept in their original order for backward
            // compatibility. New continuous-trace context columns are appended.
            lines.Add(
                "RowType\tGapIndex\tSmallLargeEndGap_ms\tBlockRepetition\tPhase\tSmallPulseNumber\tSampleIndex\tSampleTargetTime_ms\tSampleActualTime_ms\tActualSmallPulseDuration_ms\tPairNumber\tLargeStartTarget_ms\tSmallEndTarget_ms\tLargeEndTarget_ms\tLargeStartActual_ms\tSmallEndActual_ms\tLargeEndActual_ms\tReadoutNumber\tReadoutTargetStartAfterLargeEnd_ms\tReadoutActualStartAfterLargeEnd_ms\tReadoutVoltage_V\tReadoutPulseLength_ms\tCurrent_A\tSmallPulseVoltage_V\tSmallPulseLength_ms\tLargePulseVoltageContribution_V\tLargePulseLength_ms\tNumberOfSmallTestPulses\tGapBetweenSmallTestPulses_ms\tSmallPulseSamplingInterval_ms\tNumberOfConditioningPairs\tGapBetweenConditioningPairs_ms\tBlockRepetitionsPerGap\tPostPairReadoutEnabled\tResetVoltage_V\tResetPulseLength_ms\tResetRepetitions\tResetRecovery_ms\tCompliance_A\tAppliedVoltage_V\tPulseSegment\tSmallPulseActive\tLargePulseActive\tActualTraceDuration_ms");

            var settings = ReadAndValidateSettings();

            if (LargePulseBaseline != null)
            {
                // Preserve the legacy summary rows so existing analysis scripts can
                // still identify the baseline/readout sequence. Continuous samples
                // are exported additionally below.
                if (LargePulseBaseline.Readouts.Count == 0)
                {
                    lines.Add(BuildCsvRow(
                        rowType: "LargeBaseline",
                        settings: settings,
                        currentA: double.NaN,
                        phase: "Baseline",
                        largeStartTargetMs: 0.0,
                        smallEndTargetMs: double.NaN,
                        largeEndTargetMs: settings.LargePulseLengthMs,
                        largeStartActualMs: 0.0,
                        smallEndActualMs: double.NaN,
                        largeEndActualMs: LargePulseBaseline.ActualLargePulseDurationMs));
                }
                else
                {
                    foreach (var readout in LargePulseBaseline.Readouts)
                    {
                        lines.Add(BuildCsvRow(
                            rowType: "LargeBaselineReadout",
                            settings: settings,
                            currentA: CalculateTimeWeightedMeanCurrent(readout.Samples),
                            phase: "Baseline",
                            largeStartTargetMs: 0.0,
                            smallEndTargetMs: double.NaN,
                            largeEndTargetMs: settings.LargePulseLengthMs,
                            largeStartActualMs: 0.0,
                            smallEndActualMs: double.NaN,
                            largeEndActualMs: LargePulseBaseline.ActualLargePulseDurationMs,
                            readoutNumber: readout.ReadoutNumber,
                            readoutTargetMs: readout.TargetStartAfterLargeEndMs,
                            readoutActualMs: readout.ActualStartAfterLargeEndMs,
                            readoutVoltage: settings.ReadoutVoltage,
                            readoutLengthMs: settings.ReadoutPulseLengthMs,
                            appliedVoltageV: settings.ReadoutVoltage,
                            pulseSegment: "ReadoutSummary",
                            smallPulseActive: false,
                            largePulseActive: false,
                            actualTraceDurationMs: readout.ActualPulseDurationMs));
                    }
                }

                foreach (var sample in LargePulseBaseline.LargePulseSamples)
                {
                    lines.Add(BuildCsvRow(
                        rowType: "LargeBaselinePulseSample",
                        settings: settings,
                        currentA: sample.CurrentA,
                        phase: "Baseline",
                        sampleIndex: sample.SampleIndex,
                        sampleTargetMs: sample.TargetTimeMs,
                        sampleActualMs: sample.ActualTimeMs,
                        largeStartTargetMs: 0.0,
                        smallEndTargetMs: double.NaN,
                        largeEndTargetMs: settings.LargePulseLengthMs,
                        largeStartActualMs: 0.0,
                        smallEndActualMs: double.NaN,
                        largeEndActualMs: LargePulseBaseline.ActualLargePulseDurationMs,
                        appliedVoltageV: sample.AppliedVoltageV,
                        pulseSegment: "LargeOnly",
                        smallPulseActive: false,
                        largePulseActive: true,
                        actualTraceDurationMs: LargePulseBaseline.ActualLargePulseDurationMs));
                }

                foreach (var readout in LargePulseBaseline.Readouts)
                {
                    WriteMonitoringReadoutRows(
                        lines,
                        settings,
                        rowType: "LargeBaselineReadoutSample",
                        phase: "Baseline",
                        readout: readout,
                        baselineLargeEndActualMs: LargePulseBaseline.ActualLargePulseDurationMs);
                }
            }

            foreach (var block in BlockResults.OrderBy(b => b.GapIndex).ThenBy(b => b.RepetitionIndex))
            {
                WriteSmallTraceRows(lines, settings, block, "Pre", block.PreConditioningTraces);

                foreach (var pair in block.ConditioningPairs)
                {
                    // Preserve the legacy one-row-per-pair/readout export while
                    // adding the complete sampled traces below.
                    if (pair.Readouts.Count == 0)
                    {
                        lines.Add(BuildCsvRow(
                            rowType: "ConditioningPair",
                            settings: settings,
                            gapIndex: block.GapIndex,
                            endGapMs: block.SmallLargeEndGapMs,
                            blockRepetition: block.RepetitionIndex,
                            phase: "Conditioning",
                            pairNumber: pair.PairNumber,
                            largeStartTargetMs: pair.LargeStartTargetMs,
                            smallEndTargetMs: pair.SmallEndTargetMs,
                            largeEndTargetMs: pair.LargeEndTargetMs,
                            largeStartActualMs: pair.LargeStartActualMs,
                            smallEndActualMs: pair.SmallEndActualMs,
                            largeEndActualMs: pair.LargeEndActualMs,
                            currentA: double.NaN,
                            actualTraceDurationMs: pair.ActualPairDurationMs));
                    }
                    else
                    {
                        foreach (var readout in pair.Readouts)
                        {
                            lines.Add(BuildCsvRow(
                                rowType: "ConditioningReadout",
                                settings: settings,
                                gapIndex: block.GapIndex,
                                endGapMs: block.SmallLargeEndGapMs,
                                blockRepetition: block.RepetitionIndex,
                                phase: "Conditioning",
                                pairNumber: pair.PairNumber,
                                largeStartTargetMs: pair.LargeStartTargetMs,
                                smallEndTargetMs: pair.SmallEndTargetMs,
                                largeEndTargetMs: pair.LargeEndTargetMs,
                                largeStartActualMs: pair.LargeStartActualMs,
                                smallEndActualMs: pair.SmallEndActualMs,
                                largeEndActualMs: pair.LargeEndActualMs,
                                readoutNumber: readout.ReadoutNumber,
                                readoutTargetMs: readout.TargetStartAfterLargeEndMs,
                                readoutActualMs: readout.ActualStartAfterLargeEndMs,
                                readoutVoltage: settings.ReadoutVoltage,
                                readoutLengthMs: settings.ReadoutPulseLengthMs,
                                currentA: CalculateTimeWeightedMeanCurrent(readout.Samples),
                                appliedVoltageV: settings.ReadoutVoltage,
                                pulseSegment: "ReadoutSummary",
                                smallPulseActive: false,
                                largePulseActive: false,
                                actualTraceDurationMs: readout.ActualPulseDurationMs));
                        }
                    }

                    foreach (var sample in pair.Samples)
                    {
                        lines.Add(BuildCsvRow(
                            rowType: "ConditioningPairSample",
                            settings: settings,
                            gapIndex: block.GapIndex,
                            endGapMs: block.SmallLargeEndGapMs,
                            blockRepetition: block.RepetitionIndex,
                            phase: "Conditioning",
                            sampleIndex: sample.SampleIndex,
                            sampleTargetMs: sample.TargetTimeMs,
                            sampleActualMs: sample.ActualTimeMs,
                            pairNumber: pair.PairNumber,
                            largeStartTargetMs: pair.LargeStartTargetMs,
                            smallEndTargetMs: pair.SmallEndTargetMs,
                            largeEndTargetMs: pair.LargeEndTargetMs,
                            largeStartActualMs: pair.LargeStartActualMs,
                            smallEndActualMs: pair.SmallEndActualMs,
                            largeEndActualMs: pair.LargeEndActualMs,
                            currentA: sample.CurrentA,
                            appliedVoltageV: sample.AppliedVoltageV,
                            pulseSegment: sample.PulseSegment,
                            smallPulseActive: sample.SmallPulseActive,
                            largePulseActive: sample.LargePulseActive,
                            actualTraceDurationMs: pair.ActualPairDurationMs));
                    }

                    foreach (var readout in pair.Readouts)
                    {
                        WriteMonitoringReadoutRows(
                            lines,
                            settings,
                            rowType: "ConditioningReadoutSample",
                            phase: "Conditioning",
                            readout: readout,
                            gapIndex: block.GapIndex,
                            endGapMs: block.SmallLargeEndGapMs,
                            blockRepetition: block.RepetitionIndex,
                            pair: pair);
                    }
                }

                WriteSmallTraceRows(lines, settings, block, "Post", block.PostConditioningTraces);
            }

            return lines;
        }

        private static void WriteSmallTraceRows(
            List<string> lines,
            ConditioningSettings settings,
            ConditioningBlockResult block,
            string phase,
            IReadOnlyList<SmallPulseTrace> traces)
        {
            foreach (var trace in traces)
            {
                foreach (var sample in trace.Samples)
                {
                    lines.Add(BuildCsvRow(
                        rowType: "SmallPulseSample",
                        settings: settings,
                        gapIndex: block.GapIndex,
                        endGapMs: block.SmallLargeEndGapMs,
                        blockRepetition: block.RepetitionIndex,
                        phase: phase,
                        smallPulseNumber: trace.PulseNumber,
                        sampleIndex: sample.SampleIndex,
                        sampleTargetMs: sample.TargetTimeMs,
                        sampleActualMs: sample.ActualTimeMs,
                        actualSmallPulseDurationMs: trace.ActualPulseDurationMs,
                        currentA: sample.CurrentA,
                        appliedVoltageV: sample.AppliedVoltageV,
                        pulseSegment: "SmallOnly",
                        smallPulseActive: true,
                        largePulseActive: false,
                        actualTraceDurationMs: trace.ActualPulseDurationMs));
                }
            }
        }

        private static void WriteMonitoringReadoutRows(
            List<string> lines,
            ConditioningSettings settings,
            string rowType,
            string phase,
            MonitoringReadoutTrace readout,
            int? gapIndex = null,
            double? endGapMs = null,
            int? blockRepetition = null,
            ConditioningPairResult? pair = null,
            double? baselineLargeEndActualMs = null)
        {
            foreach (var sample in readout.Samples)
            {
                lines.Add(BuildCsvRow(
                    rowType: rowType,
                    settings: settings,
                    gapIndex: gapIndex,
                    endGapMs: endGapMs,
                    blockRepetition: blockRepetition,
                    phase: phase,
                    sampleIndex: sample.SampleIndex,
                    sampleTargetMs: sample.TargetTimeMs,
                    sampleActualMs: sample.ActualTimeMs,
                    pairNumber: pair?.PairNumber,
                    largeStartTargetMs: pair?.LargeStartTargetMs ?? 0.0,
                    smallEndTargetMs: pair?.SmallEndTargetMs,
                    largeEndTargetMs: pair?.LargeEndTargetMs ?? settings.LargePulseLengthMs,
                    largeStartActualMs: pair?.LargeStartActualMs ?? 0.0,
                    smallEndActualMs: pair?.SmallEndActualMs,
                    largeEndActualMs: pair?.LargeEndActualMs ?? baselineLargeEndActualMs,
                    readoutNumber: readout.ReadoutNumber,
                    readoutTargetMs: readout.TargetStartAfterLargeEndMs,
                    readoutActualMs: readout.ActualStartAfterLargeEndMs,
                    readoutVoltage: settings.ReadoutVoltage,
                    readoutLengthMs: settings.ReadoutPulseLengthMs,
                    currentA: sample.CurrentA,
                    appliedVoltageV: sample.AppliedVoltageV,
                    pulseSegment: "Readout",
                    smallPulseActive: false,
                    largePulseActive: false,
                    actualTraceDurationMs: readout.ActualPulseDurationMs));
            }
        }

        private static double CalculateTimeWeightedMeanCurrent(
            IReadOnlyList<ContinuousPulseSample> samples)
        {
            var valid = samples
                .Where(sample =>
                    double.IsFinite(sample.ActualTimeMs) &&
                    double.IsFinite(sample.CurrentA))
                .OrderBy(sample => sample.ActualTimeMs)
                .ToList();

            if (valid.Count == 0) return double.NaN;
            if (valid.Count == 1) return valid[0].CurrentA;

            double integral = 0.0;
            for (int i = 1; i < valid.Count; i++)
            {
                double dt = valid[i].ActualTimeMs - valid[i - 1].ActualTimeMs;
                if (dt <= 0) continue;

                integral += 0.5 *
                    (valid[i - 1].CurrentA + valid[i].CurrentA) * dt;
            }

            double sampledDuration =
                valid[^1].ActualTimeMs - valid[0].ActualTimeMs;

            return sampledDuration > 0
                ? integral / sampledDuration
                : valid.Average(sample => sample.CurrentA);
        }

        private static string BuildCsvRow(
            string rowType,
            ConditioningSettings settings,
            double currentA,
            int? gapIndex = null,
            double? endGapMs = null,
            int? blockRepetition = null,
            string phase = "",
            int? smallPulseNumber = null,
            int? sampleIndex = null,
            double? sampleTargetMs = null,
            double? sampleActualMs = null,
            double? actualSmallPulseDurationMs = null,
            int? pairNumber = null,
            double? largeStartTargetMs = null,
            double? smallEndTargetMs = null,
            double? largeEndTargetMs = null,
            double? largeStartActualMs = null,
            double? smallEndActualMs = null,
            double? largeEndActualMs = null,
            int? readoutNumber = null,
            double? readoutTargetMs = null,
            double? readoutActualMs = null,
            double? readoutVoltage = null,
            double? readoutLengthMs = null,
            double? appliedVoltageV = null,
            string pulseSegment = "",
            bool? smallPulseActive = null,
            bool? largePulseActive = null,
            double? actualTraceDurationMs = null)
        {
            string N(double? value, string format = "G9") => value.HasValue && double.IsFinite(value.Value)
                ? value.Value.ToString(format, CultureInfo.InvariantCulture)
                : string.Empty;
            string I(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            string B(bool? value) => value.HasValue ? (value.Value ? "true" : "false") : string.Empty;
            string current = double.IsFinite(currentA) ? currentA.ToString("E9", CultureInfo.InvariantCulture) : string.Empty;

            return string.Join("\t", new[]
            {
                Csv(rowType),
                I(gapIndex),
                N(endGapMs),
                I(blockRepetition),
                Csv(phase),
                I(smallPulseNumber),
                I(sampleIndex),
                N(sampleTargetMs),
                N(sampleActualMs),
                N(actualSmallPulseDurationMs),
                I(pairNumber),
                N(largeStartTargetMs),
                N(smallEndTargetMs),
                N(largeEndTargetMs),
                N(largeStartActualMs),
                N(smallEndActualMs),
                N(largeEndActualMs),
                I(readoutNumber),
                N(readoutTargetMs),
                N(readoutActualMs),
                N(readoutVoltage),
                N(readoutLengthMs),
                current,
                settings.SmallPulseVoltage.ToString("G9", CultureInfo.InvariantCulture),
                settings.SmallPulseLengthMs.ToString("G9", CultureInfo.InvariantCulture),
                settings.LargePulseVoltage.ToString("G9", CultureInfo.InvariantCulture),
                settings.LargePulseLengthMs.ToString("G9", CultureInfo.InvariantCulture),
                settings.NumberOfSmallTestPulses.ToString(CultureInfo.InvariantCulture),
                settings.GapBetweenSmallTestPulsesMs.ToString("G9", CultureInfo.InvariantCulture),
                string.Empty, // legacy SmallPulseSamplingInterval_ms column; no longer a user input
                settings.NumberOfConditioningPairs.ToString(CultureInfo.InvariantCulture),
                settings.GapBetweenConditioningPairsMs.ToString("G9", CultureInfo.InvariantCulture),
                settings.BlockRepetitionsPerGap.ToString(CultureInfo.InvariantCulture),
                settings.EnablePostPairReadout ? "true" : "false",
                settings.ResetVoltage.ToString("G9", CultureInfo.InvariantCulture),
                settings.ResetPulseLengthMs.ToString("G9", CultureInfo.InvariantCulture),
                settings.ResetRepetitions.ToString(CultureInfo.InvariantCulture),
                settings.ResetRecoveryMs.ToString("G9", CultureInfo.InvariantCulture),
                settings.Compliance.ToString("G9", CultureInfo.InvariantCulture),
                N(appliedVoltageV),
                Csv(pulseSegment),
                B(smallPulseActive),
                B(largePulseActive),
                N(actualTraceDurationMs)
            });
        }

        private ConditioningSettings ReadAndValidateSettings()
        {
            string writeChannel = GetParamValueString("WriteChannel").Trim();
            string readingChannel = GetParamValueString("ReadingChannel").Trim();
            if (string.IsNullOrWhiteSpace(writeChannel)) throw new InvalidOperationException("Write Channel must not be empty.");
            if (string.IsNullOrWhiteSpace(readingChannel)) readingChannel = writeChannel;

            var settings = new ConditioningSettings
            {
                WriteChannel = writeChannel,
                ReadingChannel = readingChannel,
                InvertCurrent = readingChannel != writeChannel,
                Compliance = GetParamValueDouble("Compliance"),

                SmallPulseVoltage = GetParamValueDouble("SmallPulseVoltage"),
                SmallPulseLengthMs = GetParamValueDouble("SmallPulseLengthMs"),
                NumberOfSmallTestPulses = GetParamValueInt("NumberOfSmallTestPulses"),
                GapBetweenSmallTestPulsesMs = GetParamValueDouble("GapBetweenSmallTestPulsesMs"),

                LargePulseVoltage = GetParamValueDouble("LargePulseVoltage"),
                LargePulseLengthMs = GetParamValueDouble("LargePulseLengthMs"),
                EnableLargePulseBaseline = GetParamValueBool("EnableLargePulseBaseline"),

                NumberOfConditioningPairs = GetParamValueInt("NumberOfConditioningPairs"),
                GapBetweenConditioningPairsMs = GetParamValueDouble("GapBetweenConditioningPairsMs"),
                SmallLargeEndGapsMs = ParseDoubleList(GetParamValueString("SmallLargeEndGapListMs"), "Small-Large End Gaps"),
                BlockRepetitionsPerGap = GetParamValueInt("BlockRepetitionsPerGap"),

                EnablePostPairReadout = GetParamValueBool("EnablePostPairReadout"),
                FirstReadoutDelayMs = GetParamValueDouble("FirstReadoutDelayMs"),
                ReadoutVoltage = GetParamValueDouble("ReadoutVoltage"),
                ReadoutPulseLengthMs = GetParamValueDouble("ReadoutPulseLengthMs"),

                ResetVoltage = GetParamValueDouble("ResetVoltage"),
                ResetPulseLengthMs = GetParamValueDouble("ResetPulseLengthMs"),
                ResetRepetitions = GetParamValueInt("ResetRepetitions"),
                ResetRecoveryMs = GetParamValueDouble("ResetRecoveryMs")
            };

            if (settings.Compliance <= 0) throw new InvalidOperationException("Compliance must be > 0 A.");
            if (settings.SmallPulseLengthMs <= 0) throw new InvalidOperationException("Small pulse length must be > 0 ms.");
            if (settings.LargePulseLengthMs <= 0) throw new InvalidOperationException("Large pulse length must be > 0 ms.");
            if (settings.NumberOfSmallTestPulses < 1) throw new InvalidOperationException("Number of small test pulses must be at least 1.");
            if (settings.GapBetweenSmallTestPulsesMs < 0) throw new InvalidOperationException("Gap between small test pulses must be >= 0 ms.");
            if (settings.NumberOfConditioningPairs < 1) throw new InvalidOperationException("Number of conditioning pairs must be at least 1.");
            if (settings.GapBetweenConditioningPairsMs < 0) throw new InvalidOperationException("Gap between conditioning pairs must be >= 0 ms.");
            if (settings.BlockRepetitionsPerGap < 1) throw new InvalidOperationException("Whole-block repetitions per gap must be at least 1.");
            if (settings.SmallLargeEndGapsMs.Count == 0) throw new InvalidOperationException("At least one small-large end gap is required.");

            foreach (double gap in settings.SmallLargeEndGapsMs)
            {
                if (gap < 0) throw new InvalidOperationException("Small-large end gaps must be >= 0 ms.");

                double largeStart = settings.SmallPulseLengthMs + gap - settings.LargePulseLengthMs;
                if (largeStart <= 0)
                {
                    throw new InvalidOperationException(
                        FormattableString.Invariant(
                            $"For end gap {gap:G9} ms the large pulse would start at {largeStart:G9} ms. The large pulse must start after the small pulse has already been present alone. Increase the small-pulse length and/or the end gap, or reduce the large-pulse length."));
                }
            }

            if (settings.EnablePostPairReadout)
            {
                if (settings.FirstReadoutDelayMs < 0)
                    throw new InvalidOperationException("Monitoring-pulse delay must be >= 0 ms.");
                if (settings.ReadoutPulseLengthMs <= 0)
                    throw new InvalidOperationException("Monitoring-pulse length must be > 0 ms.");

                double monitoringEnd =
                    settings.FirstReadoutDelayMs + settings.ReadoutPulseLengthMs;

                if (monitoringEnd > settings.GapBetweenConditioningPairsMs)
                {
                    throw new InvalidOperationException(
                        FormattableString.Invariant(
                            $"The monitoring pulse ends {monitoringEnd:G9} ms after the large-pulse end, but the gap between conditioning pairs is only {settings.GapBetweenConditioningPairsMs:G9} ms. Increase the pair gap or shorten/disable the monitoring pulse."));
                }
            }

            if (settings.ResetPulseLengthMs <= 0) throw new InvalidOperationException("Reset pulse length must be > 0 ms.");
            if (settings.ResetRepetitions < 1) throw new InvalidOperationException("Reset repetitions must be at least 1.");
            if (settings.ResetRecoveryMs < 0) throw new InvalidOperationException("Reset recovery time must be >= 0 ms.");

            return settings;
        }

        private async Task ConfigureSmuAsync(E5263_SMU smu, ConditioningSettings settings)
        {
            await smu.SendCommandAsync("*RST");

            if (settings.ReadingChannel == settings.WriteChannel)
                await smu.SendCommandAsync($"CN {settings.WriteChannel}");
            else
                await smu.SendCommandAsync($"CN {settings.WriteChannel},{settings.ReadingChannel}");

            await smu.SendCommandAsync("FMT 1,0");
            await smu.SendCommandAsync("TSC 0");
            await smu.SendCommandAsync("FL 0");
            await smu.SendCommandAsync("AV -1,0");

            // Keep the same spot-measurement configuration used by the last
            // working plan. TI itself does not require MM/XE, but retaining this
            // configuration avoids changing any other plan behaviour.
            await ConfigurePointMeasurementModeAsync(smu, settings);
            await smu.SendCommandAsync($"RV {settings.WriteChannel},0");
            await smu.SendCommandAsync($"RI {settings.WriteChannel},0");
            await smu.SendCommandAsync($"DZ {settings.WriteChannel}");

            var error = await smu.CheckErrorAsync();
            if (error != null) throw new InvalidOperationException($"SMU setup error: {error}");
        }

        private async Task ConfigurePointMeasurementModeAsync(E5263_SMU smu, ConditioningSettings settings)
        {
            await smu.SendCommandAsync($"MM 1,{settings.ReadingChannel}");
            await smu.SendCommandAsync($"CMM {settings.ReadingChannel},1");
            await smu.SendCommandAsync($"RI {settings.ReadingChannel},0");
        }

        private async Task ApplyResetAsync(E5263_SMU smu, ConditioningSettings settings, CancellationToken ct)
        {
            for (int i = 0; i < settings.ResetRepetitions; i++)
            {
                await ApplyVoltagePulseAsync(
                    smu,
                    settings.WriteChannel,
                    settings.ResetVoltage,
                    settings.ResetPulseLengthMs,
                    settings.Compliance,
                    ct);
            }

            if (settings.ResetRecoveryMs > 0)
                await WaitMillisecondsAccurateAsync(settings.ResetRecoveryMs, ct);
        }

        private async Task<LargePulseBaselineResult> RunLargePulseBaselineAsync(
            E5263_SMU smu,
            ConditioningSettings settings,
            CancellationToken ct)
        {
            var largePulse = await AcquireConstantPulseTraceAsync(
                smu,
                settings,
                voltage: settings.LargePulseVoltage,
                durationMs: settings.LargePulseLengthMs,
                ct: ct);

            var readoutClock = Stopwatch.StartNew();
            var readouts = settings.EnablePostPairReadout
                ? await MeasureMonitoringReadoutsAsync(smu, settings, readoutClock, ct)
                : new List<MonitoringReadoutTrace>();

            return new LargePulseBaselineResult(
                ActualLargePulseDurationMs: largePulse.ActualPulseDurationMs,
                LargePulseSamples: largePulse.Samples,
                Readouts: readouts);
        }

        private async Task<List<SmallPulseTrace>> RunSmallTestPhaseAsync(
            E5263_SMU smu,
            ConditioningSettings settings,
            string phase,
            bool waitAfterFinalPulse,
            CancellationToken ct,
            Action completedPulseCallback)
        {
            var traces = new List<SmallPulseTrace>();

            for (int pulseNumber = 1; pulseNumber <= settings.NumberOfSmallTestPulses; pulseNumber++)
            {
                ct.ThrowIfCancellationRequested();
                var trace = await MeasureSmallPulseTraceAsync(smu, settings, pulseNumber, phase, ct);
                traces.Add(trace);
                completedPulseCallback();

                bool mustWait = pulseNumber < settings.NumberOfSmallTestPulses || waitAfterFinalPulse;
                if (mustWait && settings.GapBetweenSmallTestPulsesMs > 0)
                    await WaitMillisecondsAccurateAsync(settings.GapBetweenSmallTestPulsesMs, ct);
            }

            return traces;
        }

        private async Task<SmallPulseTrace> MeasureSmallPulseTraceAsync(
            E5263_SMU smu,
            ConditioningSettings settings,
            int pulseNumber,
            string phase,
            CancellationToken ct)
        {
            var acquisition = await AcquireConstantPulseTraceAsync(
                smu,
                settings,
                voltage: settings.SmallPulseVoltage,
                durationMs: settings.SmallPulseLengthMs,
                ct: ct);

            var samples = acquisition.Samples
                .Select(sample => new SmallPulseSample(
                    SampleIndex: sample.SampleIndex,
                    TargetTimeMs: sample.TargetTimeMs,
                    ActualTimeMs: sample.ActualTimeMs,
                    AppliedVoltageV: sample.AppliedVoltageV,
                    CurrentA: sample.CurrentA))
                .ToList();

            return new SmallPulseTrace(
                PulseNumber: pulseNumber,
                Phase: phase,
                ActualPulseDurationMs: acquisition.ActualPulseDurationMs,
                Samples: samples);
        }

        private async Task<List<ConditioningPairResult>> RunConditioningPhaseAsync(
            E5263_SMU smu,
            ConditioningSettings settings,
            double endGapMs,
            CancellationToken ct,
            Action completedPairCallback)
        {
            var results = new List<ConditioningPairResult>();

            for (int pairNumber = 1; pairNumber <= settings.NumberOfConditioningPairs; pairNumber++)
            {
                ct.ThrowIfCancellationRequested();

                var pair = await ApplyAndMeasureConditioningPairAsync(
                    smu,
                    settings,
                    endGapMs,
                    pairNumber,
                    ct);

                results.Add(pair);
                completedPairCallback();

                // The pair gap remains defined from the large-pulse end to the next
                // small-pulse start. The single monitoring pulse is
                // scheduled inside this same interval.
                var pairGapClock = Stopwatch.StartNew();
                List<MonitoringReadoutTrace> readouts = settings.EnablePostPairReadout
                    ? await MeasureMonitoringReadoutsAsync(smu, settings, pairGapClock, ct)
                    : new();

                pair.Readouts.AddRange(readouts);
                await WaitUntilElapsedAsync(pairGapClock, settings.GapBetweenConditioningPairsMs, ct);
            }

            return results;
        }

        private async Task<ConditioningPairResult> ApplyAndMeasureConditioningPairAsync(
            E5263_SMU smu,
            ConditioningSettings settings,
            double endGapMs,
            int pairNumber,
            CancellationToken ct)
        {
            double smallEndTarget = settings.SmallPulseLengthMs;
            double largeEndTarget = settings.SmallPulseLengthMs + endGapMs;
            double largeStartTarget = largeEndTarget - settings.LargePulseLengthMs;

            // Preserve the source sequence of the last working plan exactly:
            // DZ -> small voltage, then direct DV transitions between active
            // levels, and DZ only where the original waveform was quiet.
            await ForceVoltageAsync(
                smu,
                settings.WriteChannel,
                settings.SmallPulseVoltage,
                settings.Compliance);

            var sw = Stopwatch.StartNew();
            var samples = new List<ConditioningPairSample>();
            double measurementEstimateMs = InitialMeasurementDurationEstimateMs;

            double largeStartActual;
            double smallEndActual;
            double largeEndActual;

            try
            {
                if (largeStartTarget < smallEndTarget)
                {
                    // Small-only segment.
                    measurementEstimateMs = await AcquireConditioningSegmentAsync(
                        smu, settings, sw, largeStartTarget,
                        settings.SmallPulseVoltage,
                        "SmallOnly", true, false,
                        samples, measurementEstimateMs, ct);

                    await ForceVoltageWithoutZeroAsync(
                        smu,
                        settings.WriteChannel,
                        settings.SmallPulseVoltage + settings.LargePulseVoltage,
                        settings.Compliance);
                    largeStartActual = sw.Elapsed.TotalMilliseconds;

                    // Overlap segment.
                    measurementEstimateMs = await AcquireConditioningSegmentAsync(
                        smu, settings, sw, smallEndTarget,
                        settings.SmallPulseVoltage + settings.LargePulseVoltage,
                        "Overlap", true, true,
                        samples, measurementEstimateMs, ct);

                    if (largeEndTarget > smallEndTarget + TimeEqualityToleranceMs)
                    {
                        await ForceVoltageWithoutZeroAsync(
                            smu,
                            settings.WriteChannel,
                            settings.LargePulseVoltage,
                            settings.Compliance);
                        smallEndActual = sw.Elapsed.TotalMilliseconds;

                        // Large-only tail.
                        measurementEstimateMs = await AcquireConditioningSegmentAsync(
                            smu, settings, sw, largeEndTarget,
                            settings.LargePulseVoltage,
                            "LargeOnly", false, true,
                            samples, measurementEstimateMs, ct);
                    }
                    else
                    {
                        await smu.SendCommandAsync($"DZ {settings.WriteChannel}");
                        smallEndActual = sw.Elapsed.TotalMilliseconds;
                    }
                }
                else
                {
                    // Small-only segment.
                    measurementEstimateMs = await AcquireConditioningSegmentAsync(
                        smu, settings, sw, smallEndTarget,
                        settings.SmallPulseVoltage,
                        "SmallOnly", true, false,
                        samples, measurementEstimateMs, ct);

                    if (NearlyEqual(largeStartTarget, smallEndTarget))
                    {
                        await ForceVoltageWithoutZeroAsync(
                            smu,
                            settings.WriteChannel,
                            settings.LargePulseVoltage,
                            settings.Compliance);
                        smallEndActual = sw.Elapsed.TotalMilliseconds;
                        largeStartActual = smallEndActual;
                    }
                    else
                    {
                        await smu.SendCommandAsync($"DZ {settings.WriteChannel}");
                        smallEndActual = sw.Elapsed.TotalMilliseconds;

                        // Preserve the original high-impedance interval. No current
                        // acquisition is attempted while neither pulse is active.
                        await WaitUntilElapsedAsync(sw, largeStartTarget, ct);
                        await ForceVoltageAsync(
                            smu,
                            settings.WriteChannel,
                            settings.LargePulseVoltage,
                            settings.Compliance);
                        largeStartActual = sw.Elapsed.TotalMilliseconds;
                    }

                    measurementEstimateMs = await AcquireConditioningSegmentAsync(
                        smu, settings, sw, largeEndTarget,
                        settings.LargePulseVoltage,
                        "LargeOnly", false, true,
                        samples, measurementEstimateMs, ct);
                }

                await WaitUntilElapsedAsync(sw, largeEndTarget, ct);
                await smu.SendCommandAsync($"DZ {settings.WriteChannel}");
                largeEndActual = sw.Elapsed.TotalMilliseconds;

                return new ConditioningPairResult(
                    PairNumber: pairNumber,
                    LargeStartTargetMs: largeStartTarget,
                    SmallEndTargetMs: smallEndTarget,
                    LargeEndTargetMs: largeEndTarget,
                    LargeStartActualMs: largeStartActual,
                    SmallEndActualMs: smallEndActual,
                    LargeEndActualMs: largeEndActual,
                    ActualPairDurationMs: largeEndActual,
                    Samples: samples,
                    Readouts: new List<MonitoringReadoutTrace>());
            }
            finally
            {
                try { await smu.SendCommandAsync($"DZ {settings.WriteChannel}"); } catch { }
            }
        }

        private async Task<List<MonitoringReadoutTrace>> MeasureMonitoringReadoutsAsync(
            E5263_SMU smu,
            ConditioningSettings settings,
            Stopwatch timeAfterLargeEnd,
            CancellationToken ct)
        {
            var traces = new List<MonitoringReadoutTrace>();
            if (!settings.EnablePostPairReadout) return traces;

            await WaitUntilElapsedAsync(
                timeAfterLargeEnd,
                settings.FirstReadoutDelayMs,
                ct);

            var acquisition = await AcquireConstantPulseTraceAsync(
                smu,
                settings,
                voltage: settings.ReadoutVoltage,
                durationMs: settings.ReadoutPulseLengthMs,
                ct: ct,
                externalReferenceClock: timeAfterLargeEnd);

            traces.Add(new MonitoringReadoutTrace(
                ReadoutNumber: 1,
                TargetStartAfterLargeEndMs: settings.FirstReadoutDelayMs,
                ActualStartAfterLargeEndMs: acquisition.HostStartOnExternalClockMs,
                ActualPulseDurationMs: acquisition.ActualPulseDurationMs,
                Samples: acquisition.Samples));

            return traces;
        }

        private async Task<double> AcquireConditioningSegmentAsync(
            E5263_SMU smu,
            ConditioningSettings settings,
            Stopwatch pairClock,
            double segmentEndMs,
            double appliedVoltageV,
            string pulseSegment,
            bool smallActive,
            bool largeActive,
            List<ConditioningPairSample> destination,
            double measurementEstimateMs,
            CancellationToken ct)
        {
            int samplesAtSegmentStart = destination.Count;

            while (pairClock.Elapsed.TotalMilliseconds < segmentEndMs)
            {
                ct.ThrowIfCancellationRequested();

                double remainingMs = segmentEndMs - pairClock.Elapsed.TotalMilliseconds;
                bool segmentAlreadyHasSample = destination.Count > samplesAtSegmentStart;

                if (segmentAlreadyHasSample &&
                    remainingMs <= measurementEstimateMs + MeasurementGuardMs)
                {
                    break;
                }

                double requestStartMs = pairClock.Elapsed.TotalMilliseconds;
                var measurementClock = Stopwatch.StartNew();
                double current = await MeasureFastCurrentAsync(smu, settings, ct);
                double measurementDurationMs = measurementClock.Elapsed.TotalMilliseconds;
                double requestEndMs = pairClock.Elapsed.TotalMilliseconds;

                measurementEstimateMs = UpdateMeasurementDurationEstimate(
                    measurementEstimateMs,
                    measurementDurationMs);

                if (requestStartMs < segmentEndMs)
                {
                    double actualTimeMs = 0.5 * (requestStartMs + requestEndMs);
                    destination.Add(new ConditioningPairSample(
                        SampleIndex: destination.Count + 1,
                        TargetTimeMs: requestStartMs,
                        ActualTimeMs: actualTimeMs,
                        AppliedVoltageV: appliedVoltageV,
                        CurrentA: current,
                        PulseSegment: pulseSegment,
                        SmallPulseActive: smallActive,
                        LargePulseActive: largeActive));
                }
            }

            await WaitUntilElapsedAsync(pairClock, segmentEndMs, ct);
            return measurementEstimateMs;
        }

        private static double UpdateMeasurementDurationEstimate(
            double previousEstimateMs,
            double measuredDurationMs)
        {
            if (!double.IsFinite(measuredDurationMs) || measuredDurationMs <= 0)
                return previousEstimateMs;

            // React quickly when communication is slower than expected, but allow
            // the estimate to decrease gradually when operation becomes faster.
            if (measuredDurationMs > previousEstimateMs)
                return measuredDurationMs;

            return 0.8 * previousEstimateMs + 0.2 * measuredDurationMs;
        }

        private async Task<ConstantPulseAcquisition> AcquireConstantPulseTraceAsync(
            E5263_SMU smu,
            ConditioningSettings settings,
            double voltage,
            double durationMs,
            CancellationToken ct,
            Stopwatch? externalReferenceClock = null)
        {
            var samples = new List<ContinuousPulseSample>();

            // This is the same source-start operation used by the last working
            // plan. Only the acquisition performed while the voltage is active has
            // changed.
            await ForceVoltageAsync(
                smu,
                settings.WriteChannel,
                voltage,
                settings.Compliance);

            double hostStartOnExternalClockMs =
                externalReferenceClock?.Elapsed.TotalMilliseconds ?? double.NaN;

            var sw = Stopwatch.StartNew();
            double measurementEstimateMs = InitialMeasurementDurationEstimateMs;

            try
            {
                while (sw.Elapsed.TotalMilliseconds < durationMs)
                {
                    ct.ThrowIfCancellationRequested();

                    double remainingMs = durationMs - sw.Elapsed.TotalMilliseconds;
                    if (samples.Count > 0 &&
                        remainingMs <= measurementEstimateMs + MeasurementGuardMs)
                    {
                        break;
                    }

                    double requestStartMs = sw.Elapsed.TotalMilliseconds;
                    var measurementClock = Stopwatch.StartNew();
                    double current = await MeasureFastCurrentAsync(smu, settings, ct);
                    double measurementDurationMs = measurementClock.Elapsed.TotalMilliseconds;
                    double requestEndMs = sw.Elapsed.TotalMilliseconds;

                    measurementEstimateMs = UpdateMeasurementDurationEstimate(
                        measurementEstimateMs,
                        measurementDurationMs);

                    // A measurement that began while the pulse was active belongs
                    // to the trace even if the GPIB transfer completes just after
                    // the nominal end. The midpoint is the best available host-side
                    // estimate of the ADC acquisition time.
                    if (requestStartMs < durationMs)
                    {
                        double actualTimeMs = 0.5 * (requestStartMs + requestEndMs);
                        samples.Add(new ContinuousPulseSample(
                            SampleIndex: samples.Count + 1,
                            TargetTimeMs: requestStartMs,
                            ActualTimeMs: actualTimeMs,
                            AppliedVoltageV: voltage,
                            CurrentA: current));
                    }
                }

                await WaitUntilElapsedAsync(sw, durationMs, ct);
                await smu.SendCommandAsync($"DZ {settings.WriteChannel}");
                double actualPulseDurationMs = sw.Elapsed.TotalMilliseconds;

                return new ConstantPulseAcquisition(
                    HostStartOnExternalClockMs: hostStartOnExternalClockMs,
                    ActualPulseDurationMs: actualPulseDurationMs,
                    Samples: samples);
            }
            finally
            {
                try { await smu.SendCommandAsync($"DZ {settings.WriteChannel}"); } catch { }
            }
        }

        private async Task<double> MeasureFastCurrentAsync(
            E5263_SMU smu,
            ConditioningSettings settings,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // TI is a high-speed spot measurement. It starts immediately and
            // returns one current datum, without MM/XE and without a second TSQ
            // response. This keeps the output buffer synchronized and avoids the
            // blocking response pattern in the previous replacement.
            await smu.SendCommandAsync($"TI {settings.ReadingChannel},0");
            string response = await smu.ReadResponseAsync(64);
            return ParseCurrent(response, settings.InvertCurrent);
        }

        private static async Task ForceVoltageWithoutZeroAsync(
            E5263_SMU smu,
            string channel,
            double voltage,
            double compliance)
        {
            await smu.SendCommandAsync(FormattableString.Invariant(
                $"DV {channel},0,{voltage},{compliance}"));
        }

        private static async Task ForceVoltageAsync(
            E5263_SMU smu,
            string channel,
            double voltage,
            double compliance)
        {
            await smu.SendCommandAsync($"DZ {channel}");
            await smu.SendCommandAsync(FormattableString.Invariant(
                $"DV {channel},0,{voltage},{compliance}"));
        }

        private static async Task ApplyVoltagePulseAsync(
            E5263_SMU smu,
            string channel,
            double voltage,
            double pulseLengthMs,
            double compliance,
            CancellationToken ct)
        {
            await ForceVoltageAsync(smu, channel, voltage, compliance);
            await WaitMillisecondsAccurateAsync(pulseLengthMs, ct);
            await smu.SendCommandAsync($"DZ {channel}");
        }

        private IReadOnlyList<PlotSeries> BuildAveragePlotSeries()
        {
            if (BlockResults.Count == 0)
            {
                return ResultPoints.Count == 0
                    ? Array.Empty<PlotSeries>()
                    : new[] { new PlotSeries(Name, ResultPoints.ToList()) };
            }

            var series = new List<PlotSeries>();

            foreach (var gapGroup in BlockResults
                         .GroupBy(b => new { b.GapIndex, b.SmallLargeEndGapMs })
                         .OrderBy(g => g.Key.GapIndex))
            {
                var blocks = gapGroup.OrderBy(b => b.RepetitionIndex).ToList();
                var prePoints = BuildPhaseAverageAcrossBlocks(blocks, pre: true);
                var postPoints = BuildPhaseAverageAcrossBlocks(blocks, pre: false);
                string gapLabel = gapGroup.Key.SmallLargeEndGapMs.ToString("G9", CultureInfo.InvariantCulture);

                series.Add(new PlotSeries($"Gap {gapLabel} ms - Pre", prePoints));
                series.Add(new PlotSeries($"Gap {gapLabel} ms - Post", postPoints));
            }

            return series;
        }

        private static List<CurvePoint> BuildPhaseAverageAcrossBlocks(
            IReadOnlyList<ConditioningBlockResult> blocks,
            bool pre)
        {
            var blockMeans = new List<Dictionary<int, BlockMeanSample>>();

            foreach (var block in blocks)
            {
                var traces = pre ? block.PreConditioningTraces : block.PostConditioningTraces;
                var means = traces
                    .SelectMany(t => t.Samples)
                    .GroupBy(s => s.SampleIndex)
                    .ToDictionary(
                        g => g.Key,
                        g => new BlockMeanSample(
                            SampleIndex: g.Key,
                            TargetTimeMs: g.Average(v => v.TargetTimeMs),
                            MeanCurrentA: g.Average(v => v.CurrentA)));

                blockMeans.Add(means);
            }

            var indices = blockMeans.SelectMany(d => d.Keys).Distinct().OrderBy(i => i);
            var points = new List<CurvePoint>();

            foreach (int index in indices)
            {
                var samples = blockMeans
                    .Where(d => d.ContainsKey(index))
                    .Select(d => d[index])
                    .ToList();

                if (samples.Count == 0) continue;

                double x = samples.Average(s => s.TargetTimeMs);
                double mean = samples.Average(s => s.MeanCurrentA);
                double? sd = samples.Count > 1
                    ? SampleStandardDeviation(samples.Select(s => s.MeanCurrentA))
                    : null;

                points.Add(new CurvePoint(x, mean, sd));
            }

            return points;
        }

        private void UpdateLatestResultPoints(IReadOnlyList<SmallPulseTrace> traces)
        {
            ResultPoints.Clear();

            foreach (var group in traces
                         .SelectMany(t => t.Samples)
                         .GroupBy(s => s.SampleIndex)
                         .OrderBy(g => g.Key))
            {
                ResultPoints.Add(new CurvePoint(
                    group.Average(s => s.TargetTimeMs),
                    group.Average(s => s.CurrentA)));
            }
        }









        private static string DescribePairSegment(bool smallActive, bool largeActive)
        {
            if (smallActive && largeActive) return "Overlap";
            if (smallActive) return "SmallOnly";
            if (largeActive) return "LargeOnly";
            return "Quiet";
        }

        private static bool NearlyEqual(double left, double right)
        {
            return Math.Abs(left - right) <= TimeEqualityToleranceMs;
        }

        private static double SampleStandardDeviation(IEnumerable<double> values)
        {
            var data = values.ToList();
            if (data.Count < 2) return 0.0;

            double mean = data.Average();
            double sum = data.Sum(v => (v - mean) * (v - mean));
            return Math.Sqrt(sum / (data.Count - 1));
        }





        private static double ParseCurrent(string rawData, bool invertCurrent)
        {
            if (string.IsNullOrWhiteSpace(rawData))
                throw new InvalidOperationException(
                    "No SMU response received during high-speed current measurement.");

            foreach (string item in SplitResponseElements(rawData))
            {
                if (item.Length < 4 || item[2] != 'I')
                    continue;

                string numeric = item.Substring(3);
                if (double.TryParse(
                        numeric,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double currentA))
                {
                    return invertCurrent ? -currentA : currentA;
                }
            }

            throw new InvalidOperationException(
                $"Could not parse current from SMU response: '{rawData}'");
        }

        private static IEnumerable<string> SplitResponseElements(string rawData)
        {
            return rawData
                .Split(
                    new[] { ',', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim());
        }

        private static List<double> ParseDoubleList(string raw, string name)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException($"{name} list must not be empty.");

            var values = new List<double>();
            var parts = raw.Split(
                new[] { ';', '\r', '\n', '\t', ' ' },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                string cleaned = part.Trim().Trim(',');
                if (!ParameterConfigHelper.TryParseDoubleRobust(cleaned, out double value))
                    throw new InvalidOperationException($"Could not parse '{part}' in the {name} list.");

                values.Add(value);
            }

            if (values.Count == 0)
                throw new InvalidOperationException($"{name} list must contain at least one value.");

            return values;
        }

        private static async Task WaitMillisecondsAccurateAsync(
            double milliseconds,
            CancellationToken ct)
        {
            if (milliseconds <= 0) return;

            var sw = Stopwatch.StartNew();
            await WaitUntilElapsedAsync(sw, milliseconds, ct);
        }

        private static async Task WaitUntilElapsedAsync(
            Stopwatch sw,
            double targetMs,
            CancellationToken ct)
        {
            while (sw.Elapsed.TotalMilliseconds < targetMs)
            {
                ct.ThrowIfCancellationRequested();
                double remaining = targetMs - sw.Elapsed.TotalMilliseconds;
                int delay = remaining > 5.0 ? 2 : 1;
                await Task.Delay(delay, ct);
            }
        }

        private static string Csv(string value)
        {
            if (value.Contains('\t') ||
                value.Contains('"') ||
                value.Contains('\n') ||
                value.Contains('\r'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        private sealed class ConditioningSettings
        {
            public string WriteChannel { get; init; } = string.Empty;
            public string ReadingChannel { get; init; } = string.Empty;
            public bool InvertCurrent { get; init; }
            public double Compliance { get; init; }

            public double SmallPulseVoltage { get; init; }
            public double SmallPulseLengthMs { get; init; }
            public int NumberOfSmallTestPulses { get; init; }
            public double GapBetweenSmallTestPulsesMs { get; init; }

            public double LargePulseVoltage { get; init; }
            public double LargePulseLengthMs { get; init; }
            public bool EnableLargePulseBaseline { get; init; }

            public int NumberOfConditioningPairs { get; init; }
            public double GapBetweenConditioningPairsMs { get; init; }
            public List<double> SmallLargeEndGapsMs { get; init; } = new();
            public int BlockRepetitionsPerGap { get; init; }

            public bool EnablePostPairReadout { get; init; }
            public double FirstReadoutDelayMs { get; init; }
            public double ReadoutVoltage { get; init; }
            public double ReadoutPulseLengthMs { get; init; }

            public double ResetVoltage { get; init; }
            public double ResetPulseLengthMs { get; init; }
            public int ResetRepetitions { get; init; }
            public double ResetRecoveryMs { get; init; }
        }

        private sealed record BlockMeanSample(
            int SampleIndex,
            double TargetTimeMs,
            double MeanCurrentA);

        private sealed record ConstantPulseAcquisition(
            double HostStartOnExternalClockMs,
            double ActualPulseDurationMs,
            List<ContinuousPulseSample> Samples);

        public sealed record SmallPulseSample(
            int SampleIndex,
            double TargetTimeMs,
            double ActualTimeMs,
            double AppliedVoltageV,
            double CurrentA);

        public sealed record ContinuousPulseSample(
            int SampleIndex,
            double TargetTimeMs,
            double ActualTimeMs,
            double AppliedVoltageV,
            double CurrentA);

        public sealed record ConditioningPairSample(
            int SampleIndex,
            double TargetTimeMs,
            double ActualTimeMs,
            double AppliedVoltageV,
            double CurrentA,
            string PulseSegment,
            bool SmallPulseActive,
            bool LargePulseActive);

        public sealed record SmallPulseTrace(
            int PulseNumber,
            string Phase,
            double ActualPulseDurationMs,
            List<SmallPulseSample> Samples);

        public sealed record MonitoringReadoutTrace(
            int ReadoutNumber,
            double TargetStartAfterLargeEndMs,
            double ActualStartAfterLargeEndMs,
            double ActualPulseDurationMs,
            List<ContinuousPulseSample> Samples);

        public sealed record ConditioningPairResult(
            int PairNumber,
            double LargeStartTargetMs,
            double SmallEndTargetMs,
            double LargeEndTargetMs,
            double LargeStartActualMs,
            double SmallEndActualMs,
            double LargeEndActualMs,
            double ActualPairDurationMs,
            List<ConditioningPairSample> Samples,
            List<MonitoringReadoutTrace> Readouts);

        public sealed record ConditioningBlockResult(
            int GapIndex,
            double SmallLargeEndGapMs,
            int RepetitionIndex,
            List<SmallPulseTrace> PreConditioningTraces,
            List<ConditioningPairResult> ConditioningPairs,
            List<SmallPulseTrace> PostConditioningTraces);

        public sealed record LargePulseBaselineResult(
            double ActualLargePulseDurationMs,
            List<ContinuousPulseSample> LargePulseSamples,
            List<MonitoringReadoutTrace> Readouts);
    }
}
