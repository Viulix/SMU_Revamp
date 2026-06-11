using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SMU_Revamp.Models;

namespace SMU_Revamp.Services
{
    public sealed class USweepMeasurementPlan : IMeasurementPlan
    {
        public string Name => "U-Sweep";
        public string Description => "Performs a linear voltage sweep (staircase sweep) and measures the resulting current.";
        public List<MeasurementParameter> Parameters { get; }
        public List<CurvePoint> ResultPoints { get; } = new();

        private string GetParamValueString(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsString() ?? string.Empty;
        private double GetParamValueDouble(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsDouble() ?? 0.0;
        private int GetParamValueInt(string name) => Parameters.Find(p => p.Name == name)?.GetValueAsInt() ?? 0;

        public USweepMeasurementPlan()
        {
            Parameters = new List<MeasurementParameter>
            {
                new() { Name = "Channel", DisplayName = "Channel:", Type = ParameterType.Text, Tooltip = "The SMU channel number (e.g. 2)" },
                new() { Name = "StartVoltage", DisplayName = "Start Voltage (V):", Type = ParameterType.Number, Tooltip = "The starting voltage of the linear sweep" },
                new() { Name = "StopVoltage", DisplayName = "Stop Voltage (V):", Type = ParameterType.Number, Tooltip = "The ending voltage of the linear sweep" },
                new() { Name = "Points", DisplayName = "Points:", Type = ParameterType.Number, Tooltip = "The number of sweep measurement points" },
                new() { Name = "Compliance", DisplayName = "Compliance (A):", Type = ParameterType.Number, Tooltip = "The current compliance limit (in Amperes)" },
                new() { Name = "AdcSamples", DisplayName = "ADC Samples (PLC):", Type = ParameterType.Number, Tooltip = "Number of Power Line Cycles (PLC) for averaging" },
                new() { Name = "SweepMode", DisplayName = "Sweep Mode:", Type = ParameterType.Dropdown, Options = new List<string> { "Single Staircase (1)", "Double Staircase (3)" }, Tooltip = "The sweep mode: single (1) or double (3) staircase" }
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
                    case "Channel":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "Channel", config.SweepChannel);
                        break;
                    case "StartVoltage":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "StartVoltage", config.SweepStart);
                        break;
                    case "StopVoltage":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "StopVoltage", config.SweepStop);
                        break;
                    case "Points":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "Points", config.SweepPoints);
                        break;
                    case "Compliance":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "Compliance", config.SweepCompliance);
                        break;
                    case "AdcSamples":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "AdcSamples", config.SweepAdcSamples);
                        break;
                    case "SweepMode":
                        param.Value = ParameterConfigHelper.GetDefaultValue(Name, "SweepMode", config.SelectedSweepMode);
                        break;
                }
            }
        }

        public async Task RunMeasurementAsync(E5263_SMU smu)
        {
            ResultPoints.Clear();

            string channel = GetParamValueString("Channel");
            double start = GetParamValueDouble("StartVoltage");
            double stop = GetParamValueDouble("StopVoltage");
            int pointsCount = GetParamValueInt("Points");
            double compliance = GetParamValueDouble("Compliance");
            int adcSamples = GetParamValueInt("AdcSamples");
            string mode = GetParamValueString("SweepMode");

            await smu.SendCommandAsync("*RST");
            await smu.SendCommandAsync("FMT 1");
            await smu.SendCommandAsync("TSC 1");
            await smu.SendCommandAsync($"CN {channel}");
            await smu.SendCommandAsync($"AV -{adcSamples},0");

            int modeValue = 3;
            if (mode.Contains("(1)")) modeValue = 1;
            else if (mode.Contains("(3)")) modeValue = 3;

            var wvCommand = System.FormattableString.Invariant($"WV {channel},{modeValue},0,{start},{stop},{pointsCount},{compliance}");
            await smu.SendCommandAsync(wvCommand);

            var wvError = await smu.CheckErrorAsync();
            if (wvError != null)
            {
                throw new InvalidOperationException($"SMU rejected WV command parameters: {wvError}");
            }

            await smu.SendCommandAsync($"RI {channel},0");
            await smu.SendCommandAsync($"MM 2,{channel}");
            var mmError = await smu.CheckErrorAsync();
            if (mmError != null)
            {
                throw new InvalidOperationException($"SMU rejected MM command: {mmError}");
            }

            await smu.SendCommandAsync($"CMM {channel},1");
            var cmmError = await smu.CheckErrorAsync();
            if (cmmError != null)
            {
                throw new InvalidOperationException($"SMU rejected CMM command: {cmmError}");
            }

            await smu.SendCommandAsync("TSR");
            await smu.SendCommandAsync("XE");

            await smu.SendCommandAsync("TSQ");

            // Calculate buffer size
            int expectedBufferLength = pointsCount * 32 * (modeValue == 3 ? 2 : 1) + 200;
            string rawData = await smu.ReadResponseAsync(expectedBufferLength);

            // Read the TSQ response block to clear it from the session output queue
            string tsqResponse = await smu.ReadResponseAsync(50);

            var parsed = ParseSmuData(rawData, modeValue, start, stop);
            ResultPoints.AddRange(parsed);
        }

        private List<CurvePoint> ParseSmuData(string rawData, int modeValue, double sweepStart, double sweepStop)
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

            if (parsedVoltages.Count == count)
            {
                for (int i = 0; i < count; i++)
                {
                    points.Add(new CurvePoint(parsedVoltages[i], parsedCurrents[i]));
                }
            }
            else
            {
                if (modeValue == 1)
                {
                    // Single sweep: start -> stop
                    for (int i = 0; i < count; i++)
                    {
                        double v = sweepStart;
                        if (count > 1)
                        {
                            v = sweepStart + i * (sweepStop - sweepStart) / (count - 1);
                        }
                        points.Add(new CurvePoint(v, parsedCurrents[i]));
                    }
                }
                else
                {
                    // Double sweep: start -> stop -> start
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
                        points.Add(new CurvePoint(v, parsedCurrents[i]));
                    }
                }
            }

            return points;
        }
    }
}
