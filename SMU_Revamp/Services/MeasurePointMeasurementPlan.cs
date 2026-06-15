using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SMU_Revamp.Models;

namespace SMU_Revamp.Services
{
    public sealed class MeasurePointMeasurementPlan : IMeasurementPlan
    {
        public string Name => "Measure Point";
        public string Description => "Performs a single-point spot measurement of current at a specified voltage.";
        public List<MeasurementParameter> Parameters { get; }
        public List<CurvePoint> ResultPoints { get; } = new();

        private string GetParamValueString(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsString() ?? string.Empty;
        private double GetParamValueDouble(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsDouble() ?? 0.0;
        private int GetParamValueInt(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsInt() ?? 0;

        public MeasurePointMeasurementPlan()
        {
            Parameters = new List<MeasurementParameter>
            {
                new() { Name = "WriteChannel", DisplayName = "Write Channel:", Type = ParameterType.Text, Tooltip = "The SMU channel number (e.g. 2)", Section = "Channel Settings" },
                new() { Name = "ReadingChannel", DisplayName = "Reading Channel:", Type = ParameterType.Text, Tooltip = "The SMU channel to measure (e.g. 1 or 2)", Section = "Channel Settings" },
                new() { Name = "Voltage", DisplayName = "Voltage (V):", Type = ParameterType.Number, Tooltip = "The forced DC voltage (in Volts)", Section = "Voltage Settings" },
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
                    case "Voltage":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "Voltage", config.SweepStart);
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

        public async Task RunMeasurementAsync(E5263_SMU smu, IProgress<double>? progress = null)
        {
            ResultPoints.Clear();
            progress?.Report(0);

            string channel = GetParamValueString("WriteChannel");
            string readingChannel = GetParamValueString("ReadingChannel");
            if (string.IsNullOrWhiteSpace(readingChannel)) readingChannel = channel;

            double voltage = GetParamValueDouble("Voltage");
            double compliance = GetParamValueDouble("Compliance");
            int adcSamples = GetParamValueInt("AdcSamples");

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
            await smu.SendCommandAsync($"AV -{adcSamples},0");
            progress?.Report(30);

            var dvCommand = System.FormattableString.Invariant($"DV {channel},0,{voltage},{compliance}");
            await smu.SendCommandAsync(dvCommand);

            var dvError = await smu.CheckErrorAsync();
            if (dvError != null)
            {
                throw new InvalidOperationException($"SMU rejected DV command parameters: {dvError}");
            }
            progress?.Report(40);

            await smu.SendCommandAsync($"MM 1,{readingChannel}");
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

            var parsed = ParseSmuData(rawData, voltage);
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
                    var msg = System.FormattableString.Invariant($"[Measure Point] Channel: {channel}, Reading Channel: {readingChannel}, Force Voltage: {voltage:F4} V, Measured Voltage: {pt.Voltage:F6} V, Measured Current: {pt.Current:E6} A");
                    Console.WriteLine(msg);
                    System.Diagnostics.Debug.WriteLine(msg);
                }
            }
            else
            {
                var errMsg = $"[Measure Point] Error: No data points parsed. Raw data received: '{rawData}'";
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
            var parsedVoltages = new List<double>();
            var parsedTimes = new List<double>();

            foreach (var item in items)
            {
                var trimmed = item.Trim();
                if (trimmed.Length < 4) continue;

                char firstChar = trimmed[0];
                char thirdChar = trimmed[2];
                string numStr = trimmed.Substring(3);

                if (firstChar == 'T')
                {
                    // Time stamp (e.g. TAV...)
                    if (double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double t))
                    {
                        parsedTimes.Add(t);
                    }
                }
                else if (thirdChar == 'I')
                {
                    // Current measurement (e.g. N2I..., C2I...)
                    if (double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double iVal))
                    {
                        parsedCurrents.Add(iVal);
                    }
                }
                else if (thirdChar == 'V')
                {
                    // Voltage measurement (e.g. N2V..., C2V...)
                    if (double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double vVal))
                    {
                        parsedVoltages.Add(vVal);
                    }
                }
            }

            int count = parsedCurrents.Count;
            if (count == 0) return points;

            string readingChannel = GetParamValueString("ReadingChannel");
            string channel = GetParamValueString("WriteChannel");
            if (string.IsNullOrWhiteSpace(readingChannel)) readingChannel = channel;
            bool invertCurrent = readingChannel != channel;

            if (parsedVoltages.Count == count)
            {
                for (int i = 0; i < count; i++)
                {
                    double current = invertCurrent ? -parsedCurrents[i] : parsedCurrents[i];
                    points.Add(new CurvePoint(parsedVoltages[i], current));
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    double current = invertCurrent ? -parsedCurrents[i] : parsedCurrents[i];
                    points.Add(new CurvePoint(forcedVoltage, current));
                }
            }

            return points;
        }
    }
}
