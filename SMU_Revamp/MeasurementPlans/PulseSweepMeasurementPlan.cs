using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SMU_Revamp.Models;
using SMU_Revamp.Services;
using SMU_Revamp.Interfaces;

namespace SMU_Revamp.MeasurementPlans
{
    public sealed class PulseSweepMeasurementPlan : MeasurementPlanBase
    {
        public override string Name => "Pulse Sweep";
        public override string Description => "Performs a pulsed linear voltage sweep and measures the resulting current.";
                
                        
        public PulseSweepMeasurementPlan()
        {
            var startVolt = new MeasurementParameter { Name = "StartVoltage", DisplayName = "Start Voltage (V):", Type = ParameterType.Number, Tooltip = "The starting voltage of the pulsed sweep", Section = "Voltage Settings" };
            var stopVolt = new MeasurementParameter { Name = "StopVoltage", DisplayName = "Stop Voltage (V):", Type = ParameterType.Number, Tooltip = "The ending voltage of the pulsed sweep", Section = "Voltage Settings" };

            Parameters = new List<MeasurementParameter>
            {
                new() { Name = "WriteChannel", DisplayName = "Write Channel:", Type = ParameterType.Text, Tooltip = "The SMU channel number (e.g. 2)", Section = "Channel Settings" },
                new() { Name = "ReadingChannel", DisplayName = "Reading Channel:", Type = ParameterType.Text, Tooltip = "The SMU channel to measure (e.g. 1 or 2)", Section = "Channel Settings" },
                new() { Name = "BaseVoltage", DisplayName = "Base Voltage (V):", Type = ParameterType.Number, Tooltip = "The base DC voltage before the pulse (in Volts)", Section = "Voltage Settings" },
                startVolt,
                stopVolt,
                new() { Name = "Points", DisplayName = "Points:", Type = ParameterType.Number, Tooltip = "The number of sweep measurement points", Section = "Sweep Settings" },
                new() { Name = "HoldTime", DisplayName = "Hold Time (s):", Type = ParameterType.Number, Tooltip = "Hold time before pulse sweep starts (in seconds)", Section = "Pulse Settings" },
                new() { Name = "PulseWidth", DisplayName = "Pulse Width (s):", Type = ParameterType.Number, Tooltip = "Width of the pulse (in seconds)", Section = "Pulse Settings" },
                new() { Name = "PulsePeriod", DisplayName = "Pulse Period (s):", Type = ParameterType.Number, Tooltip = "Period of the pulse (in seconds)", Section = "Pulse Settings" },
                new() { Name = "Compliance", DisplayName = "Compliance (A):", Type = ParameterType.Number, Tooltip = "The current compliance limit (in Amperes)", Section = "Voltage Settings" },
                new() { Name = "SweepMode", DisplayName = "Sweep Mode:", Type = ParameterType.Dropdown, Options = new List<string> { "Single Staircase (1)", "Double Staircase (3)" }, Tooltip = "The sweep mode: single (1) or double (3) staircase", Section = "Sweep Settings" }
            };
            LoadDefaults();
        }

        protected override Dictionary<string, object> GetParameterDefaults()
        {
            return new Dictionary<string, object>
            {
                { "WriteChannel", "1" },
                { "ReadingChannel", "1" },
                { "BaseVoltage", 0.0 },
                { "StartVoltage", 0 },
                { "StopVoltage", 0 },
                { "Points", 0 },
                { "HoldTime", 0.0 },
                { "PulseWidth", 0.001 },
                { "PulsePeriod", 0.01 },
                { "Compliance", 0.01 },
                { "SweepMode", 0 }
            };
        }

        public override async Task RunMeasurementAsync(E5263_SMU smu, IProgress<double>? progress = null)
        {
            ResultPoints.Clear();
            progress?.Report(0);

            string channel = GetParamValueString("WriteChannel");
            string readingChannel = GetParamValueString("ReadingChannel");
            if (string.IsNullOrWhiteSpace(readingChannel)) readingChannel = channel;

            double baseVoltage = GetParamValueDouble("BaseVoltage");
            double start = GetParamValueDouble("StartVoltage");
            double stop = GetParamValueDouble("StopVoltage");
            int pointsCount = GetParamValueInt("Points");
            double holdTime = GetParamValueDouble("HoldTime");
            double pulseWidth = GetParamValueDouble("PulseWidth");
            double pulsePeriod = GetParamValueDouble("PulsePeriod");
            double compliance = GetParamValueDouble("Compliance");
            string mode = GetParamValueString("SweepMode");

            progress?.Report(1);
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
            
            progress?.Report(2);

            var ptCommand = System.FormattableString.Invariant($"PT {holdTime},{pulseWidth},{pulsePeriod}");
            await smu.SendCommandAsync(ptCommand);
            
            var ptError = await smu.CheckErrorAsync();
            if (ptError != null)
            {
                throw new InvalidOperationException($"SMU rejected PT command parameters: {ptError}");
            }

            int modeValue = 3;
            if (mode.Contains("(1)")) modeValue = 1;
            else if (mode.Contains("(3)")) modeValue = 3;

            var pwvCommand = System.FormattableString.Invariant($"PWV {channel},{modeValue},0,{baseVoltage},{start},{stop},{pointsCount},{compliance}");
            await smu.SendCommandAsync(pwvCommand);

            var pwvError = await smu.CheckErrorAsync();
            if (pwvError != null)
            {
                throw new InvalidOperationException($"SMU rejected PWV command parameters: {pwvError}");
            }
            progress?.Report(3);

            await smu.SendCommandAsync($"RI {readingChannel},0");
            await smu.SendCommandAsync($"MM 4,{readingChannel}");
            var mmError = await smu.CheckErrorAsync();
            if (mmError != null)
            {
                throw new InvalidOperationException($"SMU rejected MM command: {mmError}");
            }
            progress?.Report(4);

            await smu.SendCommandAsync($"CMM {readingChannel},1");
            var cmmError = await smu.CheckErrorAsync();
            if (cmmError != null)
            {
                throw new InvalidOperationException($"SMU rejected CMM command: {cmmError}");
            }
            progress?.Report(5);

            await smu.SendCommandAsync("TSR");
            await smu.SendCommandAsync("XE");
            await smu.SendCommandAsync("TSQ");

            // Calculate estimated duration
            int totalPoints = modeValue == 3 ? pointsCount * 2 : pointsCount;
            double estimatedDurationSeconds = totalPoints * pulsePeriod + holdTime + 0.5;
            if (estimatedDurationSeconds < 0.5) estimatedDurationSeconds = 0.5;

            using var cts = new CancellationTokenSource();
            var progressTask = Task.Run(async () =>
            {
                try
                {
                    double currentProgress = 5.0;
                    double targetProgress = 95.0;
                    double totalSteps = estimatedDurationSeconds * 10.0; // 100ms interval
                    double stepIndex = 0;

                    while (!cts.Token.IsCancellationRequested && currentProgress < targetProgress)
                    {
                        progress?.Report(currentProgress);
                        await Task.Delay(100, cts.Token);
                        stepIndex++;

                        if (stepIndex < totalSteps)
                        {
                            currentProgress = 5.0 + (targetProgress - 5.0) * (stepIndex / totalSteps);
                        }
                        else
                        {
                            currentProgress += (98.0 - currentProgress) * 0.05;
                        }
                    }
                }
                catch (TaskCanceledException) { }
                catch (Exception) { }
            });

            try
            {
                // Calculate buffer size
                int expectedBufferLength = pointsCount * 32 * (modeValue == 3 ? 2 : 1) + 200;
                string rawData = await smu.ReadResponseAsync(expectedBufferLength);

                cts.Cancel();
                try { await progressTask; } catch { }
                progress?.Report(95);

                string tsqResponse = await smu.ReadResponseAsync(50);

                var parsed = ParseSmuData(rawData, modeValue, start, stop);
                ResultPoints.AddRange(parsed);
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

        private List<CurvePoint> ParseSmuData(string rawData, int modeValue, double sweepStart, double sweepStop)
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

                if (thirdChar == 'I')
                {
                    if (double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double iVal))
                    {
                        parsedCurrents.Add(iVal);
                    }
                }
            }

            int count = parsedCurrents.Count;
            if (count == 0) return points;

            string readingChannel = GetParamValueString("ReadingChannel");
            string channel = GetParamValueString("WriteChannel");
            if (string.IsNullOrWhiteSpace(readingChannel)) readingChannel = channel;
            bool invertCurrent = readingChannel != channel;

            if (modeValue == 1)
            {
                for (int i = 0; i < count; i++)
                {
                    double v = sweepStart;
                    if (count > 1)
                    {
                        v = sweepStart + i * (sweepStop - sweepStart) / (count - 1);
                    }
                    double current = invertCurrent ? -parsedCurrents[i] : parsedCurrents[i];
                    points.Add(new CurvePoint(v, current));
                }
            }
            else
            {
                int halfPoints = (count + 1) / 2;
                for (int i = 0; i < count; i++)
                {
                    double v;
                    if (i < halfPoints)
                    {
                        v = sweepStart;
                        if (halfPoints > 1)
                        {
                            v = sweepStart + i * (sweepStop - sweepStart) / (halfPoints - 1);
                        }
                    }
                    else
                    {
                        v = sweepStop;
                        if (halfPoints > 1)
                        {
                            v = sweepStop - (i - halfPoints) * (sweepStop - sweepStart) / (halfPoints - 1);
                        }
                    }
                    double current = invertCurrent ? -parsedCurrents[i] : parsedCurrents[i];
                    points.Add(new CurvePoint(v, current));
                }
            }

            return points;
        }
    }
}
