using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SMU_Revamp.Models;
using SMU_Revamp.Services;
using SMU_Revamp.Interfaces;

namespace SMU_Revamp.MeasurementPlans
{
    public sealed class PulseSpotMeasurementPlan : IMeasurementPlan
    {
        public string Name => "Pulse Spot";
        public string Description => "Performs a single-point pulsed measurement of current at a specified pulse voltage.";
        public List<MeasurementParameter> Parameters { get; }
        public List<CurvePoint> ResultPoints { get; } = new();
        public double PlotAspectRatio => 3.0;
        public PlotStyle DefaultPlotStyle => PlotStyle.LineAndScatter;

        private string GetParamValueString(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsString() ?? string.Empty;
        private double GetParamValueDouble(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsDouble() ?? 0.0;
        private int GetParamValueInt(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsInt() ?? 0;

        public PulseSpotMeasurementPlan()
        {
            Parameters = new List<MeasurementParameter>
            {
                new() { Name = "WriteChannel", DisplayName = "Write Channel:", Type = ParameterType.Text, Tooltip = "The SMU channel number (e.g. 2)", Section = "Channel Settings" },
                new() { Name = "ReadingChannel", DisplayName = "Reading Channel:", Type = ParameterType.Text, Tooltip = "The SMU channel to measure (e.g. 1 or 2)", Section = "Channel Settings" },
                new() { Name = "BaseVoltage", DisplayName = "Base Voltage (V):", Type = ParameterType.Number, Tooltip = "The base DC voltage before the pulse (in Volts)", Section = "Voltage Settings" },
                new() { Name = "PulseVoltage", DisplayName = "Pulse Voltage (V):", Type = ParameterType.Number, Tooltip = "The pulse voltage (in Volts)", Section = "Voltage Settings" },
                new() { Name = "HoldTime", DisplayName = "Hold Time (s):", Type = ParameterType.Number, Tooltip = "Hold time before pulse measurement starts (in seconds)", Section = "Pulse Settings" },
                new() { Name = "PulseWidth", DisplayName = "Pulse Width (s):", Type = ParameterType.Number, Tooltip = "Width of the pulse (in seconds)", Section = "Pulse Settings" },
                new() { Name = "PulsePeriod", DisplayName = "Pulse Period (s):", Type = ParameterType.Number, Tooltip = "Period of the pulse (in seconds)", Section = "Pulse Settings" },
                new() { Name = "Compliance", DisplayName = "Compliance (A):", Type = ParameterType.Number, Tooltip = "The current compliance limit (in Amperes)", Section = "Voltage Settings" }
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
                    case "BaseVoltage":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "BaseVoltage", 0.0);
                        break;
                    case "PulseVoltage":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "PulseVoltage", config.SweepStart);
                        break;
                    case "HoldTime":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "HoldTime", 0.0);
                        break;
                    case "PulseWidth":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "PulseWidth", 0.001);
                        break;
                    case "PulsePeriod":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "PulsePeriod", 0.01);
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
            progress?.Report(0);

            string channel = GetParamValueString("WriteChannel");
            string readingChannel = GetParamValueString("ReadingChannel");
            if (string.IsNullOrWhiteSpace(readingChannel)) readingChannel = channel;

            double baseVoltage = GetParamValueDouble("BaseVoltage");
            double pulseVoltage = GetParamValueDouble("PulseVoltage");
            double holdTime = GetParamValueDouble("HoldTime");
            double pulseWidth = GetParamValueDouble("PulseWidth");
            double pulsePeriod = GetParamValueDouble("PulsePeriod");
            double compliance = GetParamValueDouble("Compliance");

            progress?.Report(10);
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
            
            progress?.Report(20);

            var ptCommand = System.FormattableString.Invariant($"PT {holdTime},{pulseWidth},{pulsePeriod}");
            await smu.SendCommandAsync(ptCommand);
            
            var ptError = await smu.CheckErrorAsync();
            if (ptError != null)
            {
                throw new InvalidOperationException($"SMU rejected PT command parameters: {ptError}");
            }

            progress?.Report(30);

            var pvCommand = System.FormattableString.Invariant($"PV {channel},0,{baseVoltage},{pulseVoltage},{compliance}");
            await smu.SendCommandAsync(pvCommand);

            var pvError = await smu.CheckErrorAsync();
            if (pvError != null)
            {
                throw new InvalidOperationException($"SMU rejected PV command parameters: {pvError}");
            }
            progress?.Report(40);

            await smu.SendCommandAsync($"MM 3,{readingChannel}");
            var mmError = await smu.CheckErrorAsync();
            if (mmError != null)
            {
                throw new InvalidOperationException($"SMU rejected MM command: {mmError}");
            }
            progress?.Report(50);

            await smu.SendCommandAsync($"CMM {readingChannel},1");
            var cmmError = await smu.CheckErrorAsync();
            if (cmmError != null)
            {
                throw new InvalidOperationException($"SMU rejected CMM command: {cmmError}");
            }
            progress?.Report(60);

            await smu.SendCommandAsync("TSR");
            await smu.SendCommandAsync("XE");
            progress?.Report(70);

            await smu.SendCommandAsync("TSQ");
            progress?.Report(80);

            // Read the single-point response block
            string rawData = await smu.ReadResponseAsync(100);
            progress?.Report(90);

            // Read the TSQ response block to clear it from the session output queue
            string tsqResponse = await smu.ReadResponseAsync(50);

            var parsed = ParseSmuData(rawData, pulseVoltage);
            ResultPoints.AddRange(parsed);
            progress?.Report(95);

            var finalError = await smu.CheckErrorAsync();
            if (finalError != null)
            {
                throw new InvalidOperationException($"SMU Error during measurement: {finalError}");
            }
            progress?.Report(100);
            
            if (parsed.Count > 0)
            {
                foreach (var pt in parsed)
                {
                    var msg = System.FormattableString.Invariant($"[Pulse Spot] Channel: {channel}, Reading Channel: {readingChannel}, Pulse Voltage: {pulseVoltage:F4} V, Measured Voltage: {pt.Voltage:F6} V, Measured Current: {pt.Current:E6} A");
                    Console.WriteLine(msg);
                    System.Diagnostics.Debug.WriteLine(msg);
                }
            }
            else
            {
                var errMsg = $"[Pulse Spot] Error: No data points parsed. Raw data received: '{rawData}'";
                Console.WriteLine(errMsg);
                System.Diagnostics.Debug.WriteLine(errMsg);
            }
        }

        private List<CurvePoint> ParseSmuData(string rawData, double forcedVoltage)
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

            for (int i = 0; i < count; i++)
            {
                double current = invertCurrent ? -parsedCurrents[i] : parsedCurrents[i];
                points.Add(new CurvePoint(forcedVoltage, current));
            }

            return points;
        }
    }
}
