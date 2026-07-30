using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
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
    /// Current traces are acquired with the E5263A quasi-sampling mode: a constant-
    /// voltage staircase sweep (identical start and stop values) is executed inside
    /// the instrument and all timestamped points are read only after the pulse has
    /// completed. Conditioning-pair segments are chained in internal program memory,
    /// so GPIB round trips do not determine the sampling interval or the pair timing.
    /// </summary>
    public sealed class EyeblinkConditioningMeasurementPlan : MeasurementPlanBase, IMeasurementPlan
    {
        private const double TimeEqualityToleranceMs = 1e-7;

        // This is an internal acquisition target, not a stimulus parameter. The
        // E5263A ultimately determines the achievable interval; actual instrument
        // timestamps are exported for every point.
        private const double InternalTargetSamplingIntervalMs = 1.0;
        private const double InstrumentTimingResolutionMs = 0.1;
        private const double MaximumStepDelayMs = 1000.0;
        private const double MaximumPauseSeconds = 655.35;
        private const int MaximumSweepPoints = 1001;
        private const int AcquisitionIoTimeoutMs = 15_000;

        // Reserved volatile E5263A program-memory range used only while this plan
        // is running. The programs are scratched again in the finally block.
        private const int SmallPulseProgramNumber = 1000;
        private const int LargeBaselineProgramNumber = 1001;
        private const int PairProgramNumberBase = 1002;

        private BufferedProgramDefinition? _smallPulseProgram;
        private BufferedProgramDefinition? _largeBaselineProgram;
        private readonly Dictionary<double, BufferedProgramDefinition> _pairPrograms = new();
        private readonly List<int> _storedProgramNumbers = new();

        public override string Name => "Eyeblink Conditioning";
        public override string Description => "Uses instrument-buffered quasi-sampling to record timestamped current traces during every active pulse while preserving the original eyeblink-conditioning stimulus sequence.";

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
            smu.SetTimeout(AcquisitionIoTimeoutMs);
            bool completedSuccessfully = false;

            try
            {
                var settings = ReadAndValidateSettings();
                await ConfigureSmuAsync(smu, settings);
                await PrepareBufferedProgramsAsync(smu, settings);
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
                    "Eyeblink Conditioning stopped during instrument-buffered current acquisition. " +
                    "The source output was disabled. Details: " + ex.Message,
                    ex);
            }
            finally
            {
                // AB is one of the instrument commands that can interrupt an
                // active measurement. Use it on failure before DZ so a timed-out
                // buffered sweep cannot leave the source waiting behind an unfinished
                // acquisition in the instrument command queue.
                if (!completedSuccessfully)
                {
                    try { await smu.SendCommandAsync("AB"); } catch { }
                }

                try { await smu.SendCommandAsync("DZ"); } catch { }
                try { await ScratchBufferedProgramsAsync(smu); } catch { }
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
                "# Acquisition: E5263A instrument-buffered quasi-sampling during every small pulse, active conditioning-pair segment, optional large-only baseline pulse, and enabled monitoring pulse.",
                "# Sampling: constant-voltage staircase sweeps with identical start/stop values are executed in SMU program memory. SampleActualTime_ms comes from the instrument timestamp, not host/GPIB timing.",
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
            // compatibility. New buffered-trace context columns are appended.
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

            // FMT 1 gives fixed-width ASCII elements with a three-character
            // header. TSC 1 inserts an instrument timestamp immediately before
            // every sweep measurement value.
            await smu.SendCommandAsync("FMT 1,0");
            await smu.SendCommandAsync("TSC 1");
            await smu.SendCommandAsync("FL 0");

            // The former AV -1 setting integrated for one power-line cycle and
            // was the reason one point took roughly 20 ms. AV 1,0 selects the
            // fastest permitted automatic averaging for the active current range.
            await smu.SendCommandAsync("AV 1,0");

            await smu.SendCommandAsync($"MM 2,{settings.ReadingChannel}");
            await smu.SendCommandAsync($"CMM {settings.ReadingChannel},1");
            await smu.SendCommandAsync($"RI {settings.ReadingChannel},0");
            // Keep the sweep running through compliance events, as the last working
            // spot-measurement plan did. Automatic sweep abort would otherwise
            // truncate a pulse and return dummy values for all remaining samples.
            // Post-measurement output remains at the stop value; start and stop are
            // identical for every constant-voltage quasi-sampling sweep.
            await smu.SendCommandAsync("WM 1,2");
            await smu.SendCommandAsync($"DZ {settings.WriteChannel}");

            await ThrowIfSmuErrorAsync(smu, "SMU setup");
        }

        private async Task PrepareBufferedProgramsAsync(
            E5263_SMU smu,
            ConditioningSettings settings)
        {
            _storedProgramNumbers.Clear();
            _pairPrograms.Clear();

            _smallPulseProgram = BuildConstantPulseProgram(
                SmallPulseProgramNumber,
                settings,
                voltage: settings.SmallPulseVoltage,
                durationMs: settings.SmallPulseLengthMs,
                segmentName: "SmallPulse",
                smallActive: true,
                largeActive: false);

            await StoreProgramAsync(smu, _smallPulseProgram);

            if (settings.EnableLargePulseBaseline)
            {
                _largeBaselineProgram = BuildLargeBaselineProgram(
                    LargeBaselineProgramNumber,
                    settings);

                await StoreProgramAsync(smu, _largeBaselineProgram);
            }
            else
            {
                _largeBaselineProgram = null;
            }

            if (settings.SmallLargeEndGapsMs.Count > 2000 - PairProgramNumberBase + 1)
            {
                throw new InvalidOperationException(
                    "Too many small-large gap values for the reserved SMU program-memory range.");
            }

            int uniqueGapProgramIndex = 0;
            foreach (double endGapMs in settings.SmallLargeEndGapsMs)
            {
                // The last working plan allowed a gap value to occur more than once
                // in the list. Reuse one internal program for duplicate values while
                // preserving every list entry in the outer measurement sequence.
                if (_pairPrograms.Keys.Any(existing => NearlyEqual(existing, endGapMs)))
                    continue;

                var pairProgram = BuildConditioningPairProgram(
                    PairProgramNumberBase + uniqueGapProgramIndex,
                    settings,
                    endGapMs);

                _pairPrograms.Add(endGapMs, pairProgram);
                await StoreProgramAsync(smu, pairProgram);
                uniqueGapProgramIndex++;
            }

            await ThrowIfSmuErrorAsync(smu, "buffered-program preparation");
        }

        private async Task StoreProgramAsync(
            E5263_SMU smu,
            BufferedProgramDefinition program)
        {
            // Track the reserved program number before storage starts so a partial
            // or failed ST/END operation is still cleaned up in the outer finally block.
            if (!_storedProgramNumbers.Contains(program.ProgramNumber))
                _storedProgramNumbers.Add(program.ProgramNumber);

            await smu.SendCommandAsync($"ST {program.ProgramNumber}");

            foreach (string command in program.Commands)
                await smu.SendCommandAsync(command);

            await smu.SendCommandAsync("END");
            await ThrowIfSmuErrorAsync(
                smu,
                $"storage of buffered program {program.ProgramNumber}");
        }

        private async Task ScratchBufferedProgramsAsync(E5263_SMU smu)
        {
            foreach (int programNumber in _storedProgramNumbers.Distinct())
            {
                try { await smu.SendCommandAsync($"SCR {programNumber}"); }
                catch { }
            }

            _storedProgramNumbers.Clear();
            _pairPrograms.Clear();
            _smallPulseProgram = null;
            _largeBaselineProgram = null;
        }

        private BufferedProgramDefinition BuildConstantPulseProgram(
            int programNumber,
            ConditioningSettings settings,
            double voltage,
            double durationMs,
            string segmentName,
            bool smallActive,
            bool largeActive)
        {
            var program = new BufferedProgramDefinition(programNumber);
            program.Commands.Add("TSR");

            var segment = AddBufferedSegment(
                program,
                settings,
                nominalStartMs: 0.0,
                durationMs: durationMs,
                voltage: voltage,
                segmentName: segmentName,
                smallActive: smallActive,
                largeActive: largeActive);

            program.Commands.Add($"DZ {settings.WriteChannel}");
            string finalMarker = AddMarker(program, "PulseEnd");
            segment.EndMarkerName = finalMarker;
            program.NominalDurationMs = durationMs;
            return program;
        }

        private BufferedProgramDefinition BuildLargeBaselineProgram(
            int programNumber,
            ConditioningSettings settings)
        {
            var program = new BufferedProgramDefinition(programNumber);
            program.Commands.Add("TSR");

            var largeSegment = AddBufferedSegment(
                program,
                settings,
                nominalStartMs: 0.0,
                durationMs: settings.LargePulseLengthMs,
                voltage: settings.LargePulseVoltage,
                segmentName: "LargeOnly",
                smallActive: false,
                largeActive: true);

            program.Commands.Add($"DZ {settings.WriteChannel}");
            string largeEndMarker = AddMarker(program, "LargeEnd");
            largeSegment.EndMarkerName = largeEndMarker;

            double nominalProgramEndMs = settings.LargePulseLengthMs;

            if (settings.EnablePostPairReadout)
            {
                if (settings.FirstReadoutDelayMs > 0)
                {
                    AddPauseCommands(program, settings.FirstReadoutDelayMs);
                }

                var monitoringSegment = AddBufferedSegment(
                    program,
                    settings,
                    nominalStartMs: settings.LargePulseLengthMs + settings.FirstReadoutDelayMs,
                    durationMs: settings.ReadoutPulseLengthMs,
                    voltage: settings.ReadoutVoltage,
                    segmentName: "MonitoringReadout",
                    smallActive: false,
                    largeActive: false);

                program.Commands.Add($"DZ {settings.WriteChannel}");
                string monitoringEndMarker = AddMarker(program, "MonitoringEnd");
                monitoringSegment.EndMarkerName = monitoringEndMarker;

                nominalProgramEndMs =
                    settings.LargePulseLengthMs +
                    settings.FirstReadoutDelayMs +
                    settings.ReadoutPulseLengthMs;
            }

            program.NominalDurationMs = nominalProgramEndMs;
            return program;
        }

        private BufferedProgramDefinition BuildConditioningPairProgram(
            int programNumber,
            ConditioningSettings settings,
            double endGapMs)
        {
            double smallEndMs = settings.SmallPulseLengthMs;
            double largeEndMs = settings.SmallPulseLengthMs + endGapMs;
            double largeStartMs = largeEndMs - settings.LargePulseLengthMs;

            var program = new BufferedProgramDefinition(programNumber);
            program.Commands.Add("TSR");

            BufferedSegmentDefinition? previous = null;

            if (largeStartMs < smallEndMs - TimeEqualityToleranceMs)
            {
                previous = AddBufferedSegment(
                    program,
                    settings,
                    nominalStartMs: 0.0,
                    durationMs: largeStartMs,
                    voltage: settings.SmallPulseVoltage,
                    segmentName: "SmallOnly",
                    smallActive: true,
                    largeActive: false);

                var overlap = AddBufferedSegment(
                    program,
                    settings,
                    nominalStartMs: largeStartMs,
                    durationMs: smallEndMs - largeStartMs,
                    voltage: settings.SmallPulseVoltage + settings.LargePulseVoltage,
                    segmentName: "Overlap",
                    smallActive: true,
                    largeActive: true);

                previous.EndMarkerName = overlap.StartMarkerName;
                previous = overlap;

                if (largeEndMs > smallEndMs + TimeEqualityToleranceMs)
                {
                    var largeOnly = AddBufferedSegment(
                        program,
                        settings,
                        nominalStartMs: smallEndMs,
                        durationMs: largeEndMs - smallEndMs,
                        voltage: settings.LargePulseVoltage,
                        segmentName: "LargeOnly",
                        smallActive: false,
                        largeActive: true);

                    previous.EndMarkerName = largeOnly.StartMarkerName;
                    previous = largeOnly;
                }
            }
            else
            {
                previous = AddBufferedSegment(
                    program,
                    settings,
                    nominalStartMs: 0.0,
                    durationMs: smallEndMs,
                    voltage: settings.SmallPulseVoltage,
                    segmentName: "SmallOnly",
                    smallActive: true,
                    largeActive: false);

                double quietDurationMs = largeStartMs - smallEndMs;
                if (quietDurationMs > TimeEqualityToleranceMs)
                {
                    program.Commands.Add($"DZ {settings.WriteChannel}");
                    string quietStartMarker = AddMarker(program, "QuietStart");
                    previous.EndMarkerName = quietStartMarker;
                    AddPauseCommands(program, quietDurationMs);
                }

                var largeOnly = AddBufferedSegment(
                    program,
                    settings,
                    nominalStartMs: largeStartMs,
                    durationMs: largeEndMs - largeStartMs,
                    voltage: settings.LargePulseVoltage,
                    segmentName: "LargeOnly",
                    smallActive: false,
                    largeActive: true);

                if (string.IsNullOrEmpty(previous.EndMarkerName))
                    previous.EndMarkerName = largeOnly.StartMarkerName;

                previous = largeOnly;
            }

            if (previous == null)
                throw new InvalidOperationException("Conditioning-pair program contains no active segment.");

            program.Commands.Add($"DZ {settings.WriteChannel}");
            string largeEndMarker = AddMarker(program, "LargeEnd");
            previous.EndMarkerName = largeEndMarker;

            double nominalProgramEndMs = largeEndMs;

            if (settings.EnablePostPairReadout)
            {
                if (settings.FirstReadoutDelayMs > 0)
                {
                    AddPauseCommands(program, settings.FirstReadoutDelayMs);
                }

                var monitoringSegment = AddBufferedSegment(
                    program,
                    settings,
                    nominalStartMs: largeEndMs + settings.FirstReadoutDelayMs,
                    durationMs: settings.ReadoutPulseLengthMs,
                    voltage: settings.ReadoutVoltage,
                    segmentName: "MonitoringReadout",
                    smallActive: false,
                    largeActive: false);

                program.Commands.Add($"DZ {settings.WriteChannel}");
                string monitoringEndMarker = AddMarker(program, "MonitoringEnd");
                monitoringSegment.EndMarkerName = monitoringEndMarker;

                nominalProgramEndMs =
                    largeEndMs +
                    settings.FirstReadoutDelayMs +
                    settings.ReadoutPulseLengthMs;
            }

            program.NominalDurationMs = nominalProgramEndMs;
            return program;
        }

        private BufferedSegmentDefinition AddBufferedSegment(
            BufferedProgramDefinition program,
            ConditioningSettings settings,
            double nominalStartMs,
            double durationMs,
            double voltage,
            string segmentName,
            bool smallActive,
            bool largeActive)
        {
            var timing = BuildSweepTiming(durationMs);

            program.Commands.Add(FormattableString.Invariant(
                $"WT 0,0,{timing.StepDelayMs / 1000.0:G17}"));
            program.Commands.Add(FormattableString.Invariant(
                $"WV {settings.WriteChannel},1,0,{voltage:G17},{voltage:G17},{timing.PointCount},{settings.Compliance:G17}"));

            string startMarker = AddMarker(program, $"{segmentName}Start{program.Segments.Count + 1}");

            var segment = new BufferedSegmentDefinition
            {
                SegmentIndex = program.Segments.Count,
                SegmentName = segmentName,
                NominalStartMs = nominalStartMs,
                NominalDurationMs = durationMs,
                AppliedVoltageV = voltage,
                SmallActive = smallActive,
                LargeActive = largeActive,
                Timing = timing,
                StartMarkerName = startMarker
            };

            program.Segments.Add(segment);
            program.Commands.Add("XE");
            program.OutputEvents.Add(ProgramOutputEvent.ForSweep(
                segment.SegmentIndex,
                timing.PointCount));

            if (timing.RemainderMs > TimeEqualityToleranceMs)
            {
                AddPauseCommands(program, timing.RemainderMs);
            }

            return segment;
        }

        private static void AddPauseCommands(
            BufferedProgramDefinition program,
            double pauseMs)
        {
            if (pauseMs <= TimeEqualityToleranceMs)
                return;

            double remainingSeconds = pauseMs / 1000.0;
            while (remainingSeconds > MaximumPauseSeconds + 1e-12)
            {
                program.Commands.Add(FormattableString.Invariant(
                    $"PA {MaximumPauseSeconds:G17}"));
                remainingSeconds -= MaximumPauseSeconds;
            }

            if (remainingSeconds > 1e-12)
            {
                program.Commands.Add(FormattableString.Invariant(
                    $"PA {remainingSeconds:G17}"));
            }
        }

        private static string AddMarker(
            BufferedProgramDefinition program,
            string baseName)
        {
            string markerName = $"{baseName}_{program.MarkerCount + 1}";
            program.MarkerCount++;
            program.Commands.Add("TSQ");
            program.OutputEvents.Add(ProgramOutputEvent.ForMarker(markerName));
            return markerName;
        }

        private static SweepTiming BuildSweepTiming(double durationMs)
        {
            if (durationMs <= 0)
                throw new InvalidOperationException("Buffered pulse duration must be > 0 ms.");

            // A constant staircase sweep with N points has N-1 nominal sampling
            // intervals. Choose enough points to include both the beginning and the
            // end of the requested pulse window whenever the 1001-point hardware
            // limit permits it.
            int pointCount;
            double stepDelayMs;

            if (durationMs < InstrumentTimingResolutionMs)
            {
                pointCount = 1;
                stepDelayMs = 0.0;
            }
            else
            {
                int desiredIntervalCount = Math.Max(
                    1,
                    (int)Math.Floor(
                        durationMs / InternalTargetSamplingIntervalMs + 1e-9));

                pointCount = Math.Clamp(
                    desiredIntervalCount + 1,
                    2,
                    MaximumSweepPoints);

                double unroundedStepMs = durationMs / (pointCount - 1);
                stepDelayMs = Math.Floor(
                    unroundedStepMs / InstrumentTimingResolutionMs + 1e-9) *
                    InstrumentTimingResolutionMs;

                stepDelayMs = Math.Clamp(
                    stepDelayMs,
                    InstrumentTimingResolutionMs,
                    MaximumStepDelayMs);

                while (pointCount > 1 &&
                       (pointCount - 1) * stepDelayMs >
                           durationMs + TimeEqualityToleranceMs)
                {
                    pointCount--;
                }
            }

            double nominalSweepSpanMs =
                pointCount > 1
                    ? (pointCount - 1) * stepDelayMs
                    : 0.0;

            // The source remains at the final (identical) sweep value after XE.
            // PA fills any sub-step remainder before DZ terminates the pulse.
            double remainderMs = Math.Max(
                0.0,
                durationMs - nominalSweepSpanMs);

            return new SweepTiming(
                PointCount: pointCount,
                StepDelayMs: stepDelayMs,
                RemainderMs: remainderMs);
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
            if (_largeBaselineProgram == null)
                throw new InvalidOperationException("Large-pulse baseline program was not prepared.");

            var execution = await ExecuteBufferedProgramAsync(
                smu,
                settings,
                _largeBaselineProgram,
                ct);

            var largeSegment = execution.Segments
                .Single(segment => segment.Definition.LargeActive);

            var largeSamples = ConvertToContinuousPulseSamples(largeSegment);
            var readouts = new List<MonitoringReadoutTrace>();

            var monitoringSegment = execution.Segments
                .SingleOrDefault(segment =>
                    segment.Definition.SegmentName == "MonitoringReadout");

            if (monitoringSegment != null)
            {
                readouts.Add(BuildMonitoringTrace(
                    monitoringSegment,
                    largeEndActualMs: largeSegment.EndTimeMs,
                    targetStartAfterLargeEndMs: settings.FirstReadoutDelayMs));
            }

            return new LargePulseBaselineResult(
                ActualLargePulseDurationMs: largeSegment.EndTimeMs - largeSegment.StartTimeMs,
                LargePulseSamples: largeSamples,
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

                var pulseCycleClock = Stopwatch.StartNew();
                var outcome = await MeasureSmallPulseTraceAsync(
                    smu,
                    settings,
                    pulseNumber,
                    phase,
                    pulseCycleClock,
                    ct);

                traces.Add(outcome.Trace);
                completedPulseCallback();

                bool mustWait = pulseNumber < settings.NumberOfSmallTestPulses || waitAfterFinalPulse;
                if (mustWait && settings.GapBetweenSmallTestPulsesMs > 0)
                {
                    double nextPulseTargetMs =
                        outcome.HostPulseStartMs +
                        outcome.Trace.ActualPulseDurationMs +
                        settings.GapBetweenSmallTestPulsesMs;

                    await WaitUntilElapsedAsync(
                        pulseCycleClock,
                        nextPulseTargetMs,
                        ct);
                }
            }

            return traces;
        }

        private async Task<SmallPulseMeasurementOutcome> MeasureSmallPulseTraceAsync(
            E5263_SMU smu,
            ConditioningSettings settings,
            int pulseNumber,
            string phase,
            Stopwatch pulseCycleClock,
            CancellationToken ct)
        {
            if (_smallPulseProgram == null)
                throw new InvalidOperationException("Small-pulse acquisition program was not prepared.");

            var acquisition = await AcquireConstantPulseTraceAsync(
                smu,
                settings,
                _smallPulseProgram,
                ct,
                externalReferenceClock: pulseCycleClock);

            var samples = acquisition.Samples
                .Select(sample => new SmallPulseSample(
                    SampleIndex: sample.SampleIndex,
                    TargetTimeMs: sample.TargetTimeMs,
                    ActualTimeMs: sample.ActualTimeMs,
                    AppliedVoltageV: sample.AppliedVoltageV,
                    CurrentA: sample.CurrentA))
                .ToList();

            var trace = new SmallPulseTrace(
                PulseNumber: pulseNumber,
                Phase: phase,
                ActualPulseDurationMs: acquisition.ActualPulseDurationMs,
                Samples: samples);

            return new SmallPulseMeasurementOutcome(
                Trace: trace,
                HostPulseStartMs: acquisition.HostStartOnExternalClockMs);
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

                var pairCycleClock = Stopwatch.StartNew();
                var outcome = await ApplyAndMeasureConditioningPairAsync(
                    smu,
                    settings,
                    endGapMs,
                    pairNumber,
                    pairCycleClock,
                    ct);

                results.Add(outcome.PairResult);
                completedPairCallback();

                double nextPairTargetMs =
                    outcome.HostPairStartMs +
                    outcome.PairResult.LargeEndActualMs +
                    settings.GapBetweenConditioningPairsMs;

                await WaitUntilElapsedAsync(
                    pairCycleClock,
                    nextPairTargetMs,
                    ct);
            }

            return results;
        }

        private async Task<ConditioningPairMeasurementOutcome> ApplyAndMeasureConditioningPairAsync(
            E5263_SMU smu,
            ConditioningSettings settings,
            double endGapMs,
            int pairNumber,
            Stopwatch pairCycleClock,
            CancellationToken ct)
        {
            var program = GetPairProgram(endGapMs);
            var execution = await ExecuteBufferedProgramAsync(
                smu,
                settings,
                program,
                ct,
                pairCycleClock);

            double smallEndTarget = settings.SmallPulseLengthMs;
            double largeEndTarget = settings.SmallPulseLengthMs + endGapMs;
            double largeStartTarget = largeEndTarget - settings.LargePulseLengthMs;

            var activeSegments = execution.Segments
                .Where(segment =>
                    segment.Definition.SmallActive ||
                    segment.Definition.LargeActive)
                .ToList();

            var samples = activeSegments
                .SelectMany(segment => segment.Samples.Select(sample =>
                    new ConditioningPairSample(
                        SampleIndex: 0,
                        TargetTimeMs: sample.TargetTimeMs,
                        ActualTimeMs: sample.ActualTimeMs,
                        AppliedVoltageV: segment.Definition.AppliedVoltageV,
                        CurrentA: sample.CurrentA,
                        PulseSegment: segment.Definition.SegmentName,
                        SmallPulseActive: segment.Definition.SmallActive,
                        LargePulseActive: segment.Definition.LargeActive)))
                .OrderBy(sample => sample.ActualTimeMs)
                .Select((sample, index) => sample with { SampleIndex = index + 1 })
                .ToList();

            var firstLargeSegment = activeSegments
                .First(segment => segment.Definition.LargeActive);

            var lastSmallSegment = activeSegments
                .Last(segment => segment.Definition.SmallActive);

            var lastLargeSegment = activeSegments
                .Last(segment => segment.Definition.LargeActive);

            double largeStartActual = firstLargeSegment.StartTimeMs;
            double smallEndActual = lastSmallSegment.EndTimeMs;
            double largeEndActual = lastLargeSegment.EndTimeMs;

            var readouts = new List<MonitoringReadoutTrace>();
            var monitoringSegment = execution.Segments
                .SingleOrDefault(segment =>
                    segment.Definition.SegmentName == "MonitoringReadout");

            if (monitoringSegment != null)
            {
                readouts.Add(BuildMonitoringTrace(
                    monitoringSegment,
                    largeEndActual,
                    settings.FirstReadoutDelayMs));
            }

            var pairResult = new ConditioningPairResult(
                PairNumber: pairNumber,
                LargeStartTargetMs: largeStartTarget,
                SmallEndTargetMs: smallEndTarget,
                LargeEndTargetMs: largeEndTarget,
                LargeStartActualMs: largeStartActual,
                SmallEndActualMs: smallEndActual,
                LargeEndActualMs: largeEndActual,
                ActualPairDurationMs: largeEndActual,
                Samples: samples,
                Readouts: readouts);

            return new ConditioningPairMeasurementOutcome(
                PairResult: pairResult,
                HostPairStartMs: execution.HostStartOnExternalClockMs);
        }

        private BufferedProgramDefinition GetPairProgram(double endGapMs)
        {
            foreach (var entry in _pairPrograms)
            {
                if (NearlyEqual(entry.Key, endGapMs))
                    return entry.Value;
            }

            throw new InvalidOperationException(
                FormattableString.Invariant(
                    $"No buffered conditioning-pair program was prepared for end gap {endGapMs:G9} ms."));
        }

        private async Task<ConstantPulseAcquisition> AcquireConstantPulseTraceAsync(
            E5263_SMU smu,
            ConditioningSettings settings,
            BufferedProgramDefinition program,
            CancellationToken ct,
            Stopwatch? externalReferenceClock = null)
        {
            var execution = await ExecuteBufferedProgramAsync(
                smu,
                settings,
                program,
                ct,
                externalReferenceClock);

            if (execution.Segments.Count != 1)
                throw new InvalidOperationException("Constant-pulse program returned an unexpected segment count.");

            var segment = execution.Segments[0];
            var samples = ConvertToContinuousPulseSamples(segment);

            return new ConstantPulseAcquisition(
                HostStartOnExternalClockMs: execution.HostStartOnExternalClockMs,
                ActualPulseDurationMs: segment.EndTimeMs - segment.StartTimeMs,
                Samples: samples);
        }

        private static List<ContinuousPulseSample> ConvertToContinuousPulseSamples(
            BufferedSegmentAcquisition segment)
        {
            return segment.Samples
                .Select((sample, index) => new ContinuousPulseSample(
                    SampleIndex: index + 1,
                    TargetTimeMs: sample.TargetTimeMs - segment.Definition.NominalStartMs,
                    ActualTimeMs: sample.ActualTimeMs - segment.StartTimeMs,
                    AppliedVoltageV: segment.Definition.AppliedVoltageV,
                    CurrentA: sample.CurrentA))
                .ToList();
        }

        private static MonitoringReadoutTrace BuildMonitoringTrace(
            BufferedSegmentAcquisition monitoringSegment,
            double largeEndActualMs,
            double targetStartAfterLargeEndMs)
        {
            return new MonitoringReadoutTrace(
                ReadoutNumber: 1,
                TargetStartAfterLargeEndMs: targetStartAfterLargeEndMs,
                ActualStartAfterLargeEndMs:
                    monitoringSegment.StartTimeMs - largeEndActualMs,
                ActualPulseDurationMs:
                    monitoringSegment.EndTimeMs - monitoringSegment.StartTimeMs,
                Samples: ConvertToContinuousPulseSamples(monitoringSegment));
        }

        private async Task<BufferedProgramExecution> ExecuteBufferedProgramAsync(
            E5263_SMU smu,
            ConditioningSettings settings,
            BufferedProgramDefinition program,
            CancellationToken ct,
            Stopwatch? externalReferenceClock = null)
        {
            ct.ThrowIfCancellationRequested();

            // The program itself creates output records (TSQ markers and XE sweep
            // results). Do not issue *OPC? or NUB? before reading those records:
            // query responses use the same output buffer and would be interleaved
            // with the measurement data. Instead, the expected record/item count is
            // known exactly from the program definition and is read directly.
            int previousTimeoutMs = smu.GetTimeout();
            int requiredTimeoutMs = Math.Max(
                AcquisitionIoTimeoutMs,
                checked((int)Math.Min(
                    int.MaxValue,
                    Math.Ceiling(program.NominalDurationMs + 10_000.0))));

            smu.SetTimeout(requiredTimeoutMs);

            try
            {
                await smu.SendCommandAsync("BC");
                await smu.SendCommandAsync($"DO {program.ProgramNumber}");

                double hostStartOnExternalClockMs =
                    externalReferenceClock?.Elapsed.TotalMilliseconds ?? double.NaN;

                string rawOutput = await ReadBufferedOutputAsync(
                    smu,
                    program.ExpectedOutputItemCount,
                    program.OutputRecordCount,
                    ct);

                // At this point the measurement/marker records have been drained,
                // so error-register queries cannot corrupt the acquisition stream.
                await ThrowIfSmuErrorAsync(
                    smu,
                    $"buffered program {program.ProgramNumber}");

                var elements = SplitResponseElements(rawOutput).ToList();
                if (elements.Count != program.ExpectedOutputItemCount)
                {
                    throw new InvalidOperationException(
                        $"Read {elements.Count} buffered data elements, but " +
                        $"{program.ExpectedOutputItemCount} were expected for program " +
                        $"{program.ProgramNumber}.");
                }

                int elementIndex = 0;
                var markerTimesSeconds = new Dictionary<string, double>();
                var segmentSamples = program.Segments.ToDictionary(
                    segment => segment.SegmentIndex,
                    _ => new List<BufferedSample>());

                foreach (var outputEvent in program.OutputEvents)
                {
                    if (outputEvent.IsMarker)
                    {
                        markerTimesSeconds[outputEvent.MarkerName] = ParseOutputElement(
                            elements[elementIndex++],
                            expectedDataType: 'T',
                            context: outputEvent.MarkerName);
                        continue;
                    }

                    var definition = program.Segments[outputEvent.SegmentIndex];
                    var destination = segmentSamples[outputEvent.SegmentIndex];

                    for (int pointIndex = 0; pointIndex < outputEvent.PointCount; pointIndex++)
                    {
                        double timeSeconds = ParseOutputElement(
                            elements[elementIndex++],
                            expectedDataType: 'T',
                            context: $"{definition.SegmentName} timestamp {pointIndex + 1}");

                        double currentA = ParseOutputElement(
                            elements[elementIndex++],
                            expectedDataType: 'I',
                            context: $"{definition.SegmentName} current {pointIndex + 1}");

                        if (settings.InvertCurrent)
                            currentA = -currentA;

                        destination.Add(new BufferedSample(
                            TargetTimeMs: definition.NominalStartMs +
                                pointIndex * definition.Timing.StepDelayMs,
                            InstrumentTimeSeconds: timeSeconds,
                            ActualTimeMs: double.NaN,
                            CurrentA: currentA));
                    }
                }

                if (elementIndex != elements.Count)
                    throw new InvalidOperationException(
                        "Buffered output parser did not consume every data element.");

                if (program.Segments.Count == 0)
                    throw new InvalidOperationException(
                        $"Buffered program {program.ProgramNumber} contains no acquisition segment.");

                string zeroMarkerName = program.Segments[0].StartMarkerName;
                if (!markerTimesSeconds.TryGetValue(zeroMarkerName, out double programZeroSeconds))
                {
                    throw new InvalidOperationException(
                        $"Start marker '{zeroMarkerName}' was not returned by program " +
                        $"{program.ProgramNumber}.");
                }

                var acquiredSegments = new List<BufferedSegmentAcquisition>();
                foreach (var definition in program.Segments)
                {
                    if (!markerTimesSeconds.TryGetValue(
                            definition.StartMarkerName,
                            out double startSeconds))
                    {
                        throw new InvalidOperationException(
                            $"Start marker '{definition.StartMarkerName}' was not returned.");
                    }

                    if (string.IsNullOrWhiteSpace(definition.EndMarkerName) ||
                        !markerTimesSeconds.TryGetValue(
                            definition.EndMarkerName,
                            out double endSeconds))
                    {
                        throw new InvalidOperationException(
                            $"End marker '{definition.EndMarkerName}' was not returned for " +
                            $"segment '{definition.SegmentName}'.");
                    }

                    double startTimeMs =
                        (startSeconds - programZeroSeconds) * 1000.0;
                    double endTimeMs =
                        (endSeconds - programZeroSeconds) * 1000.0;

                    if (endTimeMs < startTimeMs - TimeEqualityToleranceMs)
                    {
                        throw new InvalidOperationException(
                            $"Instrument timestamps are not monotonic for segment " +
                            $"'{definition.SegmentName}'.");
                    }

                    var normalizedSamples = segmentSamples[definition.SegmentIndex]
                        .Select(sample => sample with
                        {
                            ActualTimeMs =
                                (sample.InstrumentTimeSeconds - programZeroSeconds) * 1000.0
                        })
                        .ToList();

                    acquiredSegments.Add(new BufferedSegmentAcquisition(
                        Definition: definition,
                        StartTimeMs: startTimeMs,
                        EndTimeMs: endTimeMs,
                        Samples: normalizedSamples));
                }

                double actualDurationMs = acquiredSegments.Max(segment => segment.EndTimeMs);

                return new BufferedProgramExecution(
                    HostStartOnExternalClockMs: hostStartOnExternalClockMs,
                    ActualDurationMs: actualDurationMs,
                    Segments: acquiredSegments);
            }
            finally
            {
                smu.SetTimeout(previousTimeoutMs);
            }
        }

        private static async Task<string> ReadBufferedOutputAsync(
            E5263_SMU smu,
            int expectedItemCount,
            int expectedRecordCount,
            CancellationToken ct)
        {
            var combined = new StringBuilder();
            int maximumReads = Math.Max(2, expectedRecordCount + 3);

            for (int readIndex = 0; readIndex < maximumReads; readIndex++)
            {
                ct.ThrowIfCancellationRequested();

                int receivedItems = SplitResponseElements(combined.ToString()).Count();
                if (receivedItems >= expectedItemCount)
                    break;

                int remainingItems = expectedItemCount - receivedItems;
                // FMT 1 values are fixed-width, but reserve additional room for
                // separators and terminators. ReadString may still return one output
                // record at a time, in which case this loop continues with the next.
                int maximumCharacters = Math.Max(128, 20 * remainingItems + 128);
                string chunk = await smu.ReadResponseAsync(maximumCharacters);

                if (combined.Length > 0)
                    combined.Append('\n');
                combined.Append(chunk);
            }

            int finalItemCount = SplitResponseElements(combined.ToString()).Count();
            if (finalItemCount < expectedItemCount)
            {
                throw new InvalidOperationException(
                    $"Only {finalItemCount} of {expectedItemCount} buffered data items were received.");
            }

            return combined.ToString();
        }

        private static double ParseOutputElement(
            string element,
            char expectedDataType,
            string context)
        {
            string trimmed = element.Trim();
            if (trimmed.Length < 4)
                throw new InvalidOperationException($"Malformed SMU element for {context}: '{element}'.");

            char actualType = trimmed[2];
            if (actualType != expectedDataType)
            {
                throw new InvalidOperationException(
                    $"Expected SMU data type '{expectedDataType}' for {context}, " +
                    $"but received header '{trimmed.Substring(0, Math.Min(3, trimmed.Length))}'.");
            }

            if (!double.TryParse(
                    trimmed.Substring(3),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double value))
            {
                throw new InvalidOperationException(
                    $"Could not parse numeric SMU value for {context}: '{element}'.");
            }

            if (!double.IsFinite(value) || Math.Abs(value) > 1e90)
                throw new InvalidOperationException($"SMU returned invalid/dummy data for {context}: '{element}'.");

            return value;
        }

        private static async Task ThrowIfSmuErrorAsync(
            E5263_SMU smu,
            string context)
        {
            string errorCodeText = await smu.QueryAsync("ERR? 1", readBufferChars: 32);
            if (!int.TryParse(errorCodeText.Trim(), out int errorCode))
            {
                throw new InvalidOperationException(
                    $"Could not parse the SMU error register after {context}: '{errorCodeText.Trim()}'.");
            }

            if (errorCode == 0)
                return;

            string message = await smu.QueryAsync($"EMG? {errorCode}", readBufferChars: 256);
            throw new InvalidOperationException(
                $"SMU error after {context}: {errorCode} - {message.Trim()}");
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

        private sealed record SweepTiming(
            int PointCount,
            double StepDelayMs,
            double RemainderMs);

        private sealed class BufferedProgramDefinition
        {
            public BufferedProgramDefinition(int programNumber)
            {
                ProgramNumber = programNumber;
            }

            public int ProgramNumber { get; }
            public double NominalDurationMs { get; set; }
            public int MarkerCount { get; set; }
            public List<string> Commands { get; } = new();
            public List<BufferedSegmentDefinition> Segments { get; } = new();
            public List<ProgramOutputEvent> OutputEvents { get; } = new();
            public int ExpectedOutputItemCount =>
                OutputEvents.Sum(outputEvent => outputEvent.IsMarker
                    ? 1
                    : 2 * outputEvent.PointCount);
            public int OutputRecordCount => OutputEvents.Count;
        }

        private sealed class BufferedSegmentDefinition
        {
            public int SegmentIndex { get; init; }
            public string SegmentName { get; init; } = string.Empty;
            public double NominalStartMs { get; init; }
            public double NominalDurationMs { get; init; }
            public double AppliedVoltageV { get; init; }
            public bool SmallActive { get; init; }
            public bool LargeActive { get; init; }
            public SweepTiming Timing { get; init; } = new(1, 0.0, 0.0);
            public string StartMarkerName { get; init; } = string.Empty;
            public string EndMarkerName { get; set; } = string.Empty;
        }

        private sealed record ProgramOutputEvent(
            bool IsMarker,
            string MarkerName,
            int SegmentIndex,
            int PointCount)
        {
            public static ProgramOutputEvent ForMarker(string markerName) =>
                new(true, markerName, -1, 0);

            public static ProgramOutputEvent ForSweep(int segmentIndex, int pointCount) =>
                new(false, string.Empty, segmentIndex, pointCount);
        }

        private sealed record BufferedSample(
            double TargetTimeMs,
            double InstrumentTimeSeconds,
            double ActualTimeMs,
            double CurrentA);

        private sealed record BufferedSegmentAcquisition(
            BufferedSegmentDefinition Definition,
            double StartTimeMs,
            double EndTimeMs,
            List<BufferedSample> Samples);

        private sealed record BufferedProgramExecution(
            double HostStartOnExternalClockMs,
            double ActualDurationMs,
            List<BufferedSegmentAcquisition> Segments);

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

        private sealed record SmallPulseMeasurementOutcome(
            SmallPulseTrace Trace,
            double HostPulseStartMs);

        private sealed record ConditioningPairMeasurementOutcome(
            ConditioningPairResult PairResult,
            double HostPairStartMs);

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
