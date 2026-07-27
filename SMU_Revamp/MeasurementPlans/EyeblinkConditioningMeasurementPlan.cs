using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SMU_Revamp.Models;
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
    /// The optional large-only baseline is executed once at the beginning of the
    /// entire measurement, followed by a reset. Every full conditioning block is
    /// reset independently.
    /// </summary>
    public sealed class EyeblinkConditioningMeasurementPlan : MeasurementPlanBase
    {
        public override string Name => "Eyeblink Conditioning";
        public override string Description => "Compares small-pulse current traces before and after repeated small/large pulse pairing while sweeping the end-to-end timing between both stimuli.";

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
                new() { Name = "SmallPulseSamplingIntervalMs", DisplayName = "Small-Pulse Sampling Interval (ms):", Type = ParameterType.Number, Tooltip = "Requested spacing between current samples while the small voltage is applied. Actual sample times are exported because software/SMU latency can cause jitter.", Section = "Small Stimulus" },

                new() { Name = "LargePulseVoltage", DisplayName = "Large Pulse Voltage Contribution (V):", Type = ParameterType.Number, Tooltip = "Voltage contribution of the large/unconditioned stimulus. During overlap the applied voltage is Small + Large.", Section = "Large Stimulus" },
                new() { Name = "LargePulseLengthMs", DisplayName = "Large Pulse Length (ms):", Type = ParameterType.Number, Tooltip = "Duration of every large pulse.", Section = "Large Stimulus" },
                new() { Name = "EnableLargePulseBaseline", DisplayName = "Measure Large-Pulse Baseline Once:", Type = ParameterType.Checkbox, Tooltip = "At the beginning of the entire measurement: reset, apply one large pulse, acquire the optional monitoring readouts, then reset again.", Section = "Large Stimulus" },

                new() { Name = "NumberOfConditioningPairs", DisplayName = "Number of Conditioning Pairs:", Type = ParameterType.Number, Tooltip = "Number of small/large pairings in each conditioning block.", Section = "Conditioning" },
                new() { Name = "GapBetweenConditioningPairsMs", DisplayName = "Gap Between Conditioning Pairs (ms):", Type = ParameterType.Number, Tooltip = "End-to-start time from the end of one complete pulse pair to the start of the next small pulse. Monitoring readouts must fit inside this interval.", Section = "Conditioning" },
                new() { Name = "SmallLargeEndGapListMs", DisplayName = "Small-Large End Gaps (ms):", Type = ParameterType.Text, Tooltip = "Semicolon-separated list. Each value is large-pulse end minus small-pulse end. 0 means both pulses end together; values below the large-pulse length cause overlap.", Section = "Conditioning" },
                new() { Name = "BlockRepetitionsPerGap", DisplayName = "Whole-Block Repetitions per Gap:", Type = ParameterType.Number, Tooltip = "Independent reset -> pre-test -> conditioning -> post-test repetitions for every selected small-large end gap. Error bars are calculated across these independent blocks.", Section = "Conditioning" },

                new() { Name = "EnablePostPairReadout", DisplayName = "Enable Post-Pair Monitoring Readouts:", Type = ParameterType.Checkbox, Tooltip = "Apply low-voltage readout pulses after each large pulse. These readouts monitor the conditioning process and are not the primary result.", Section = "Monitoring Readout" },
                new() { Name = "NumberOfReadoutPulses", DisplayName = "Number of Readout Pulses:", Type = ParameterType.Number, Tooltip = "Number of monitoring readout pulses after each large pulse.", Section = "Monitoring Readout" },
                new() { Name = "FirstReadoutDelayMs", DisplayName = "First Readout Delay after Large End (ms):", Type = ParameterType.Number, Tooltip = "End-to-start delay between the large-pulse end and the first monitoring readout.", Section = "Monitoring Readout" },
                new() { Name = "GapBetweenReadoutPulsesMs", DisplayName = "Gap Between Readout Pulses (ms):", Type = ParameterType.Number, Tooltip = "Quiet end-to-start interval between consecutive monitoring readout pulses.", Section = "Monitoring Readout" },
                new() { Name = "ReadoutVoltage", DisplayName = "Readout Voltage (V):", Type = ParameterType.Number, Tooltip = "Low voltage applied during every monitoring readout pulse.", Section = "Monitoring Readout" },
                new() { Name = "ReadoutPulseLengthMs", DisplayName = "Readout Pulse Length (ms):", Type = ParameterType.Number, Tooltip = "Integration/pulse duration of every monitoring readout.", Section = "Monitoring Readout" },

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
                { "SmallPulseSamplingIntervalMs", 20.0 },

                { "LargePulseVoltage", 0.8 },
                { "LargePulseLengthMs", 20.0 },
                { "EnableLargePulseBaseline", true },

                { "NumberOfConditioningPairs", 10 },
                { "GapBetweenConditioningPairsMs", 1000.0 },
                { "SmallLargeEndGapListMs", "200;100;50;20;0" },
                { "BlockRepetitionsPerGap", 3 },

                { "EnablePostPairReadout", true },
                { "NumberOfReadoutPulses", 3 },
                { "FirstReadoutDelayMs", 20.0 },
                { "GapBetweenReadoutPulsesMs", 20.0 },
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

            try
            {
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
            }
            finally
            {
                try { await smu.SendCommandAsync("DZ"); } catch { }
            }

            progress?.Report(100.0);
        }

        public IReadOnlyList<string> GetCsvLines()
        {
            var lines = new List<string>
            {
                "sep=\t",
                "RowType\tGapIndex\tSmallLargeEndGap_ms\tBlockRepetition\tPhase\tSmallPulseNumber\tSampleIndex\tSampleTargetTime_ms\tSampleActualTime_ms\tActualSmallPulseDuration_ms\tPairNumber\tLargeStartTarget_ms\tSmallEndTarget_ms\tLargeEndTarget_ms\tLargeStartActual_ms\tSmallEndActual_ms\tLargeEndActual_ms\tReadoutNumber\tReadoutTargetStartAfterLargeEnd_ms\tReadoutActualStartAfterLargeEnd_ms\tReadoutVoltage_V\tReadoutPulseLength_ms\tCurrent_A\tSmallPulseVoltage_V\tSmallPulseLength_ms\tLargePulseVoltageContribution_V\tLargePulseLength_ms\tNumberOfSmallTestPulses\tGapBetweenSmallTestPulses_ms\tSmallPulseSamplingInterval_ms\tNumberOfConditioningPairs\tGapBetweenConditioningPairs_ms\tBlockRepetitionsPerGap\tPostPairReadoutEnabled\tResetVoltage_V\tResetPulseLength_ms\tResetRepetitions\tResetRecovery_ms\tCompliance_A"
            };

            var settings = ReadAndValidateSettings();

            if (LargePulseBaseline != null)
            {
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
                            currentA: readout.CurrentA,
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
                            readoutLengthMs: settings.ReadoutPulseLengthMs));
                    }
                }
            }

            foreach (var block in BlockResults.OrderBy(b => b.GapIndex).ThenBy(b => b.RepetitionIndex))
            {
                WriteSmallTraceRows(lines, settings, block, "Pre", block.PreConditioningTraces);

                foreach (var pair in block.ConditioningPairs)
                {
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
                            currentA: double.NaN));
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
                                currentA: readout.CurrentA));
                        }
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
                        currentA: sample.CurrentA));
                }
            }
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
            double? readoutLengthMs = null)
        {
            string N(double? value, string format = "G9") => value.HasValue && double.IsFinite(value.Value)
                ? value.Value.ToString(format, CultureInfo.InvariantCulture)
                : string.Empty;
            string I(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
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
                settings.SmallPulseSamplingIntervalMs.ToString("G9", CultureInfo.InvariantCulture),
                settings.NumberOfConditioningPairs.ToString(CultureInfo.InvariantCulture),
                settings.GapBetweenConditioningPairsMs.ToString("G9", CultureInfo.InvariantCulture),
                settings.BlockRepetitionsPerGap.ToString(CultureInfo.InvariantCulture),
                settings.EnablePostPairReadout ? "true" : "false",
                settings.ResetVoltage.ToString("G9", CultureInfo.InvariantCulture),
                settings.ResetPulseLengthMs.ToString("G9", CultureInfo.InvariantCulture),
                settings.ResetRepetitions.ToString(CultureInfo.InvariantCulture),
                settings.ResetRecoveryMs.ToString("G9", CultureInfo.InvariantCulture),
                settings.Compliance.ToString("G9", CultureInfo.InvariantCulture)
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
                SmallPulseSamplingIntervalMs = GetParamValueDouble("SmallPulseSamplingIntervalMs"),

                LargePulseVoltage = GetParamValueDouble("LargePulseVoltage"),
                LargePulseLengthMs = GetParamValueDouble("LargePulseLengthMs"),
                EnableLargePulseBaseline = GetParamValueBool("EnableLargePulseBaseline"),

                NumberOfConditioningPairs = GetParamValueInt("NumberOfConditioningPairs"),
                GapBetweenConditioningPairsMs = GetParamValueDouble("GapBetweenConditioningPairsMs"),
                SmallLargeEndGapsMs = ParseDoubleList(GetParamValueString("SmallLargeEndGapListMs"), "Small-Large End Gaps"),
                BlockRepetitionsPerGap = GetParamValueInt("BlockRepetitionsPerGap"),

                EnablePostPairReadout = GetParamValueBool("EnablePostPairReadout"),
                NumberOfReadoutPulses = GetParamValueInt("NumberOfReadoutPulses"),
                FirstReadoutDelayMs = GetParamValueDouble("FirstReadoutDelayMs"),
                GapBetweenReadoutPulsesMs = GetParamValueDouble("GapBetweenReadoutPulsesMs"),
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
            if (settings.SmallPulseSamplingIntervalMs <= 0) throw new InvalidOperationException("Small-pulse sampling interval must be > 0 ms.");
            if (settings.SmallPulseSamplingIntervalMs >= settings.SmallPulseLengthMs)
                throw new InvalidOperationException("Small-pulse sampling interval must be shorter than the small-pulse length so that a current trace contains at least two requested samples.");
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
                if (settings.NumberOfReadoutPulses < 1) throw new InvalidOperationException("Number of readout pulses must be at least 1 when monitoring is enabled.");
                if (settings.FirstReadoutDelayMs < 0) throw new InvalidOperationException("First readout delay must be >= 0 ms.");
                if (settings.GapBetweenReadoutPulsesMs < 0) throw new InvalidOperationException("Gap between readout pulses must be >= 0 ms.");
                if (settings.ReadoutPulseLengthMs <= 0) throw new InvalidOperationException("Readout pulse length must be > 0 ms.");

                double monitoringEnd =
                    settings.FirstReadoutDelayMs +
                    settings.NumberOfReadoutPulses * settings.ReadoutPulseLengthMs +
                    Math.Max(0, settings.NumberOfReadoutPulses - 1) * settings.GapBetweenReadoutPulsesMs;

                if (monitoringEnd > settings.GapBetweenConditioningPairsMs)
                {
                    throw new InvalidOperationException(
                        FormattableString.Invariant(
                            $"The monitoring sequence requires at least {monitoringEnd:G9} ms after each large-pulse end, but the gap between conditioning pairs is only {settings.GapBetweenConditioningPairsMs:G9} ms. Increase the pair gap or shorten/disable the monitoring readouts."));
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
            await smu.SendCommandAsync("TSC 1");
            await smu.SendCommandAsync("AV -1,0");
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
            await ForceVoltageAsync(smu, settings.WriteChannel, settings.LargePulseVoltage, settings.Compliance);
            var pulseStopwatch = Stopwatch.StartNew();
            await WaitUntilElapsedAsync(pulseStopwatch, settings.LargePulseLengthMs, ct);
            await smu.SendCommandAsync($"DZ {settings.WriteChannel}");
            double actualPulseDuration = pulseStopwatch.Elapsed.TotalMilliseconds;

            var readoutClock = Stopwatch.StartNew();
            var readouts = settings.EnablePostPairReadout
                ? await MeasureMonitoringReadoutsAsync(smu, settings, readoutClock, ct)
                : new List<MonitoringReadoutSample>();

            return new LargePulseBaselineResult(actualPulseDuration, readouts);
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
            await ConfigurePointMeasurementModeAsync(smu, settings);
            await ForceVoltageAsync(smu, settings.WriteChannel, settings.SmallPulseVoltage, settings.Compliance);
            var sw = Stopwatch.StartNew();

            var samples = new List<SmallPulseSample>();
            var targetTimes = BuildSmallPulseSampleTimes(settings.SmallPulseLengthMs, settings.SmallPulseSamplingIntervalMs);

            for (int sampleIndex = 0; sampleIndex < targetTimes.Count; sampleIndex++)
            {
                double targetTime = targetTimes[sampleIndex];
                await WaitUntilElapsedAsync(sw, targetTime, ct);

                if (sw.Elapsed.TotalMilliseconds >= settings.SmallPulseLengthMs && samples.Count > 0)
                    break;

                double actualSampleTime = sw.Elapsed.TotalMilliseconds;
                double current = await MeasureCurrentAtCurrentVoltageAsync(smu, settings, ct);
                samples.Add(new SmallPulseSample(sampleIndex + 1, targetTime, actualSampleTime, current));
            }

            await WaitUntilElapsedAsync(sw, settings.SmallPulseLengthMs, ct);
            await smu.SendCommandAsync($"DZ {settings.WriteChannel}");
            double actualDuration = sw.Elapsed.TotalMilliseconds;

            return new SmallPulseTrace(pulseNumber, phase, actualDuration, samples);
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
                var pair = await ApplyConditioningPairAsync(smu, settings, endGapMs, pairNumber, ct);
                results.Add(pair);
                completedPairCallback();

                // The pair gap is defined from the large-pulse end to the next small-pulse start.
                // Readouts are scheduled within this same interval.
                var pairGapClock = Stopwatch.StartNew();
                List<MonitoringReadoutSample> readouts = settings.EnablePostPairReadout
                    ? await MeasureMonitoringReadoutsAsync(smu, settings, pairGapClock, ct)
                    : new();

                pair.Readouts.AddRange(readouts);
                await WaitUntilElapsedAsync(pairGapClock, settings.GapBetweenConditioningPairsMs, ct);
            }

            return results;
        }

        private async Task<ConditioningPairResult> ApplyConditioningPairAsync(
            E5263_SMU smu,
            ConditioningSettings settings,
            double endGapMs,
            int pairNumber,
            CancellationToken ct)
        {
            double smallEndTarget = settings.SmallPulseLengthMs;
            double largeEndTarget = settings.SmallPulseLengthMs + endGapMs;
            double largeStartTarget = largeEndTarget - settings.LargePulseLengthMs;

            await ForceVoltageAsync(smu, settings.WriteChannel, settings.SmallPulseVoltage, settings.Compliance);
            var sw = Stopwatch.StartNew();

            double largeStartActual;
            double smallEndActual;
            double largeEndActual;

            if (largeStartTarget < smallEndTarget)
            {
                // Overlap: small alone -> summed voltage -> optional large-alone tail.
                await WaitUntilElapsedAsync(sw, largeStartTarget, ct);
                await ForceVoltageWithoutZeroAsync(
                    smu,
                    settings.WriteChannel,
                    settings.SmallPulseVoltage + settings.LargePulseVoltage,
                    settings.Compliance);
                largeStartActual = sw.Elapsed.TotalMilliseconds;

                await WaitUntilElapsedAsync(sw, smallEndTarget, ct);
                if (largeEndTarget > smallEndTarget)
                {
                    await ForceVoltageWithoutZeroAsync(
                        smu,
                        settings.WriteChannel,
                        settings.LargePulseVoltage,
                        settings.Compliance);
                    smallEndActual = sw.Elapsed.TotalMilliseconds;
                }
                else
                {
                    await smu.SendCommandAsync($"DZ {settings.WriteChannel}");
                    smallEndActual = sw.Elapsed.TotalMilliseconds;
                }
            }
            else
            {
                // Non-overlap: small -> either direct large transition or quiet interval -> large.
                await WaitUntilElapsedAsync(sw, smallEndTarget, ct);

                if (Math.Abs(largeStartTarget - smallEndTarget) < 1e-9)
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
                    await WaitUntilElapsedAsync(sw, largeStartTarget, ct);
                    await ForceVoltageAsync(smu, settings.WriteChannel, settings.LargePulseVoltage, settings.Compliance);
                    largeStartActual = sw.Elapsed.TotalMilliseconds;
                }
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
                Readouts: new List<MonitoringReadoutSample>());
        }

        private async Task<List<MonitoringReadoutSample>> MeasureMonitoringReadoutsAsync(
            E5263_SMU smu,
            ConditioningSettings settings,
            Stopwatch timeAfterLargeEnd,
            CancellationToken ct)
        {
            var samples = new List<MonitoringReadoutSample>();
            if (!settings.EnablePostPairReadout) return samples;

            for (int readoutNumber = 1; readoutNumber <= settings.NumberOfReadoutPulses; readoutNumber++)
            {
                double targetStart =
                    settings.FirstReadoutDelayMs +
                    (readoutNumber - 1) * (settings.ReadoutPulseLengthMs + settings.GapBetweenReadoutPulsesMs);

                await WaitUntilElapsedAsync(timeAfterLargeEnd, targetStart, ct);
                var measured = await ReadCurrentPulseAsync(smu, settings, ct, timeAfterLargeEnd);

                samples.Add(new MonitoringReadoutSample(
                    ReadoutNumber: readoutNumber,
                    TargetStartAfterLargeEndMs: targetStart,
                    ActualStartAfterLargeEndMs: measured.ActualStartMs,
                    CurrentA: measured.CurrentA));
            }

            return samples;
        }

        private async Task<(double CurrentA, double ActualStartMs)> ReadCurrentPulseAsync(
            E5263_SMU smu,
            ConditioningSettings settings,
            CancellationToken ct,
            Stopwatch referenceClock)
        {
            await ConfigurePointMeasurementModeAsync(smu, settings);
            await smu.SendCommandAsync($"DZ {settings.WriteChannel}");
            await smu.SendCommandAsync(FormattableString.Invariant(
                $"DV {settings.WriteChannel},0,{settings.ReadoutVoltage},{settings.Compliance}"));
            double actualStart = referenceClock.Elapsed.TotalMilliseconds;

            await smu.SendCommandAsync("TSR");
            await smu.SendCommandAsync("XE");
            await WaitMillisecondsAccurateAsync(settings.ReadoutPulseLengthMs, ct);
            await smu.SendCommandAsync("TSQ");

            string response = await smu.ReadResponseAsync(512);
            try { _ = await smu.ReadResponseAsync(50); } catch { }
            await smu.SendCommandAsync($"DZ {settings.WriteChannel}");

            return (ParseCurrent(response, settings.InvertCurrent), actualStart);
        }

        private async Task<double> MeasureCurrentAtCurrentVoltageAsync(
            E5263_SMU smu,
            ConditioningSettings settings,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await smu.SendCommandAsync("TSR");
            await smu.SendCommandAsync("XE");
            await smu.SendCommandAsync("TSQ");

            string response = await smu.ReadResponseAsync(512);
            try { _ = await smu.ReadResponseAsync(50); } catch { }
            return ParseCurrent(response, settings.InvertCurrent);
        }

        private static async Task ForceVoltageAsync(E5263_SMU smu, string channel, double voltage, double compliance)
        {
            await smu.SendCommandAsync($"DZ {channel}");
            await smu.SendCommandAsync(FormattableString.Invariant($"DV {channel},0,{voltage},{compliance}"));
        }

        private static async Task ForceVoltageWithoutZeroAsync(E5263_SMU smu, string channel, double voltage, double compliance)
        {
            await smu.SendCommandAsync(FormattableString.Invariant($"DV {channel},0,{voltage},{compliance}"));
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
                var samples = blockMeans.Where(d => d.ContainsKey(index)).Select(d => d[index]).ToList();
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

            foreach (var group in traces.SelectMany(t => t.Samples).GroupBy(s => s.SampleIndex).OrderBy(g => g.Key))
            {
                ResultPoints.Add(new CurvePoint(
                    group.Average(s => s.TargetTimeMs),
                    group.Average(s => s.CurrentA)));
            }
        }

        private static List<double> BuildSmallPulseSampleTimes(double pulseLengthMs, double samplingIntervalMs)
        {
            var targets = new List<double> { 0.0 };
            for (double t = samplingIntervalMs; t < pulseLengthMs; t += samplingIntervalMs)
                targets.Add(t);
            return targets;
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
                throw new InvalidOperationException("No SMU response received during current measurement.");

            var items = rawData.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in items)
            {
                var trimmed = item.Trim();
                if (trimmed.Length >= 4 && trimmed[2] == 'I')
                {
                    string numeric = trimmed.Substring(3);
                    if (double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out double current))
                        return invertCurrent ? -current : current;
                }
            }

            throw new InvalidOperationException($"Could not parse current from SMU response: '{rawData}'");
        }

        private static List<double> ParseDoubleList(string raw, string name)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException($"{name} list must not be empty.");

            var values = new List<double>();
            var parts = raw.Split(new[] { ';', '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);

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

        private static async Task WaitMillisecondsAccurateAsync(double milliseconds, CancellationToken ct)
        {
            if (milliseconds <= 0) return;
            var sw = Stopwatch.StartNew();
            await WaitUntilElapsedAsync(sw, milliseconds, ct);
        }

        private static async Task WaitUntilElapsedAsync(Stopwatch sw, double targetMs, CancellationToken ct)
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
            if (value.Contains('\t') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
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
            public double SmallPulseSamplingIntervalMs { get; init; }

            public double LargePulseVoltage { get; init; }
            public double LargePulseLengthMs { get; init; }
            public bool EnableLargePulseBaseline { get; init; }

            public int NumberOfConditioningPairs { get; init; }
            public double GapBetweenConditioningPairsMs { get; init; }
            public List<double> SmallLargeEndGapsMs { get; init; } = new();
            public int BlockRepetitionsPerGap { get; init; }

            public bool EnablePostPairReadout { get; init; }
            public int NumberOfReadoutPulses { get; init; }
            public double FirstReadoutDelayMs { get; init; }
            public double GapBetweenReadoutPulsesMs { get; init; }
            public double ReadoutVoltage { get; init; }
            public double ReadoutPulseLengthMs { get; init; }

            public double ResetVoltage { get; init; }
            public double ResetPulseLengthMs { get; init; }
            public int ResetRepetitions { get; init; }
            public double ResetRecoveryMs { get; init; }
        }

        private sealed record BlockMeanSample(int SampleIndex, double TargetTimeMs, double MeanCurrentA);

        public sealed record SmallPulseSample(
            int SampleIndex,
            double TargetTimeMs,
            double ActualTimeMs,
            double CurrentA);

        public sealed record SmallPulseTrace(
            int PulseNumber,
            string Phase,
            double ActualPulseDurationMs,
            List<SmallPulseSample> Samples);

        public sealed record MonitoringReadoutSample(
            int ReadoutNumber,
            double TargetStartAfterLargeEndMs,
            double ActualStartAfterLargeEndMs,
            double CurrentA);

        public sealed record ConditioningPairResult(
            int PairNumber,
            double LargeStartTargetMs,
            double SmallEndTargetMs,
            double LargeEndTargetMs,
            double LargeStartActualMs,
            double SmallEndActualMs,
            double LargeEndActualMs,
            List<MonitoringReadoutSample> Readouts);

        public sealed record ConditioningBlockResult(
            int GapIndex,
            double SmallLargeEndGapMs,
            int RepetitionIndex,
            List<SmallPulseTrace> PreConditioningTraces,
            List<ConditioningPairResult> ConditioningPairs,
            List<SmallPulseTrace> PostConditioningTraces);

        public sealed record LargePulseBaselineResult(
            double ActualLargePulseDurationMs,
            List<MonitoringReadoutSample> Readouts);
    }
}
