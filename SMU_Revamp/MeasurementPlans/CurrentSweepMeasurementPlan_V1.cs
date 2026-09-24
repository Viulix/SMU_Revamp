using System;
using System.Collections.Generic;
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
    /// Host-stepped current sweep. Voltage and current are measured sequentially
    /// at each current setting (not simultaneously). No inferred-current axis.
    /// </summary>
    public sealed class CurrentSweepMeasurementPlan : MeasurementPlanBase, IMeasurementPlan
    {
        public override string Name => "Current Sweep";
        public override string Description => "Current-controlled I-V sweep: 0 to maximum, optionally back to 0. Measures voltage and current sequentially at each step, with hardware voltage compliance.";

        private readonly List<(double SetCurrent, char VoltageStatus, char CurrentStatus)> _readings = new();

        public CurrentSweepMeasurementPlan()
        {
            Parameters = new List<MeasurementParameter>
            {
                new() { Name = "WriteChannel", DisplayName = "Current Source Channel:", Type = ParameterType.Text, Section = "Channel Settings", Tooltip = "SMU channel sourcing current and measuring voltage." },
                new() { Name = "ReadingChannel", DisplayName = "Current Reading / Return Channel:", Type = ParameterType.Text, Section = "Channel Settings", Tooltip = "Same as source for one SMU with DUT connected to its low terminal. A different channel is held at 0 V as the return and measures current with reversed sign. Uses the existing two-SMU wiring convention." },
                new() { Name = "MaxCurrent", DisplayName = "Maximum Current (A):", Type = ParameterType.Number, Section = "Sweep Settings", Tooltip = "Positive maximum source current in amperes." },
                new() { Name = "VoltageCompliance", DisplayName = "Voltage Compliance (V):", Type = ParameterType.Number, Section = "Sweep Settings", Tooltip = "Positive voltage limit of the current-source channel. Device/module limits still apply." },
                new() { Name = "CurrentStep", DisplayName = "Current Step Size (A):", Type = ParameterType.Number, Section = "Sweep Settings", Tooltip = "Positive current increment. Includes the exact maximum; the final increment can be shorter. Return sweep retraces the same settings." },
                new() { Name = "AdcSamples", DisplayName = "ADC Samples:", Type = ParameterType.Number, Section = "Measurement Settings", Tooltip = "Actual averaging samples per reading, integer 1 to 1023 (not PLC). Default 128. Low sample counts can reduce specified accuracy." },
                new() { Name = "ReturnToZero", DisplayName = "Sweep back to zero", Type = ParameterType.Checkbox, Section = "Sweep Settings", Tooltip = "Checked: 0 to maximum to 0. Unchecked: measure 0 to maximum, then disable output." }
            };
            LoadDefaults();
        }

        protected override Dictionary<string, object> GetParameterDefaults() => new()
        {
            ["WriteChannel"] = "1", ["ReadingChannel"] = "1",
            ["MaxCurrent"] = 1e-4, ["VoltageCompliance"] = 1.0,
            ["CurrentStep"] = 5e-6, ["AdcSamples"] = 128, ["ReturnToZero"] = true
        };

        internal static List<double> BuildCurrentSteps(double maximum, double step, bool returnToZero)
        {
            if (!double.IsFinite(maximum) || maximum <= 0)
                throw new InvalidOperationException("Maximum current must be finite and greater than zero.");
            if (!double.IsFinite(step) || step <= 0)
                throw new InvalidOperationException("Current step must be finite and greater than zero.");
            double intervals = maximum / step;
            // Bound allocations before converting to an integer; tolerate floating
            // point noise near an exact multiple without adding a duplicate peak.
            if (!double.IsFinite(intervals) || intervals > 99999)
                throw new InvalidOperationException("Too many current steps (maximum 100,000 points per direction).");
            var values = new List<double> { 0 };
            double tolerance = maximum * 1e-12;
            int count = (int)Math.Ceiling(intervals);
            for (int i = 1; i < count; i++)
            {
                double value = i * step;
                if (value < maximum - tolerance) values.Add(value);
            }
            values.Add(maximum);
            if (returnToZero)
                for (int i = values.Count - 2; i >= 0; i--) values.Add(values[i]);
            return values;
        }

        public override async Task RunMeasurementAsync(E5263_SMU smu, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            ResultPoints.Clear();
            _readings.Clear();
            progress?.Report(0);
            string source = Channel(GetParamValueString("WriteChannel"));
            string readText = GetParamValueString("ReadingChannel");
            string reading = string.IsNullOrWhiteSpace(readText) ? source : Channel(readText);
            double maximum = GetParamValueDouble("MaxCurrent");
            double compliance = GetParamValueDouble("VoltageCompliance");
            double sampleValue = GetParamValueDouble("AdcSamples");
            if (!double.IsFinite(compliance) || compliance <= 0)
                throw new InvalidOperationException("Voltage compliance must be finite and greater than zero.");
            if (!double.IsFinite(sampleValue) || sampleValue < 1 || sampleValue > 1023 || sampleValue != Math.Truncate(sampleValue))
                throw new InvalidOperationException("ADC samples must be an integer from 1 to 1023.");
            var steps = BuildCurrentSteps(maximum, GetParamValueDouble("CurrentStep"), GetParamValueBool("ReturnToZero"));
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await smu.SendCommandAsync("*RST");
                await smu.SendCommandAsync("FMT 1");
                await smu.SendCommandAsync("TSC 0");
                await smu.SendCommandAsync(source == reading ? $"CN {source}" : $"CN {source},{reading}");
                if (source != reading)
                {
                    // Configure the 0 V return explicitly: CN otherwise leaves it
                    // at 100 uA. This current limit is separate from source Vcomp.
                    await smu.SendCommandAsync(FormattableString.Invariant($"DV {reading},0,0,{maximum}"));
                    await smu.SendCommandAsync($"RI {reading},0");
                }
                await smu.SendCommandAsync(FormattableString.Invariant($"DI {source},0,0,{compliance}"));
                await smu.SendCommandAsync($"RV {source},0");
                await smu.SendCommandAsync($"RI {source},0");
                await smu.SendCommandAsync(FormattableString.Invariant($"AV {(int)sampleValue},1"));
                await RequireNoError(smu, "current sweep setup");

                for (int i = 0; i < steps.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await smu.SendCommandAsync(FormattableString.Invariant($"DI {source},0,{steps[i]},{compliance}"));
                    await RequireNoError(smu, "current step");
                    var voltage = await Measure(smu, source, 'V', cancellationToken);
                    if (source != reading)
                    {
                        var returnVoltage = await Measure(smu, reading, 'V', cancellationToken);
                        voltage = (voltage.Value - returnVoltage.Value,
                            voltage.Status != 'N' ? voltage.Status : returnVoltage.Status);
                    }
                    var current = await Measure(smu, reading, 'I', cancellationToken);
                    await RequireNoError(smu, "current sweep measurement");
                    double measuredCurrent = source == reading ? current.Value : -current.Value;
                    ResultPoints.Add(new CurvePoint(voltage.Value, measuredCurrent));
                    _readings.Add((steps[i], voltage.Status, current.Status));
                    if (voltage.Status != 'N' || current.Status != 'N')
                        LogService.Instance.Warning($"[Current Sweep] Point {i + 1}: voltage status {voltage.Status}, current status {current.Status}; plotting measured values.");
                    progress?.Report(100.0 * (i + 1) / steps.Count);
                }
            }
            catch
            {
                try { await smu.SendCommandAsync("AB"); } catch { }
                throw;
            }
            finally
            {
                try { await smu.SendCommandAsync("DZ"); } catch { }
                // Unqualified CL also works when Vcomp > 42 V. This plan resets
                // the entire SMU on entry and owns its outputs while it runs.
                try { await smu.SendCommandAsync("CL"); } catch { }
            }
        }

        private static string Channel(string value)
        {
            if (!int.TryParse(value.Trim(), out int channel) || channel < 1 || channel > 8)
                throw new InvalidOperationException("SMU channels must be integers from 1 to 8.");
            return channel.ToString(CultureInfo.InvariantCulture);
        }

        private static async Task RequireNoError(E5263_SMU smu, string context)
        {
            // Use ERR? directly: the existing CheckErrorAsync treats query failures
            // as success. A source command must be confirmed before measuring.
            string raw = await smu.QueryAsync("ERR? 1", 64);
            if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
                throw new InvalidOperationException($"Invalid error response during {context}: {raw}");
            if (code != 0)
                throw new InvalidOperationException($"SMU error {code} during {context}.");
        }

        private static async Task<(double Value, char Status)> Measure(E5263_SMU smu, string channel, char quantity, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await smu.SendCommandAsync($"MM 1,{channel}");
            await smu.SendCommandAsync($"CMM {channel},{(quantity == 'V' ? 2 : 1)}");
            await RequireNoError(smu, "spot measurement setup");
            token.ThrowIfCancellationRequested();
            await smu.SendCommandAsync("XE");
            string response = await smu.ReadResponseAsync(256, token);
            return ParseReading(response, channel, quantity);
        }

        internal static (double Value, char Status) ParseReading(string response, string channel, char quantity)
        {
            // FMT 1 uses A..H for channels 1..8. Also accept digit headers
            // produced by the application's existing simulator/test fixtures.
            char header = (char)('A' + int.Parse(channel, CultureInfo.InvariantCulture) - 1);
            var matches = response.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim()).Where(t => t.Length >= 4 && t[2] == quantity && (t[1] == header || t[1].ToString() == channel)).ToList();
            if (matches.Count != 1)
                throw new InvalidOperationException($"Expected one {quantity} reading from channel {channel}, received: {response}");
            string item = matches[0];
            // N: normal, C: this channel in compliance, T: another channel in
            // compliance. Reject overflow/oscillation/invalid status data.
            if (item[0] != 'N' && item[0] != 'C' && item[0] != 'T')
                throw new InvalidOperationException($"Invalid/unsettled SMU reading: {item}");
            if (!double.TryParse(item.Substring(3), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
                throw new InvalidOperationException($"Invalid numeric SMU reading: {item}");
            return (value, item[0]);
        }

        public IReadOnlyList<string> GetCsvLines()
        {
            var lines = new List<string> { "Voltage (V)\tCurrent (A)\tSet Current (A)\tVoltage Status\tCurrent Status" };
            for (int i = 0; i < ResultPoints.Count; i++)
            {
                var p = ResultPoints[i];
                string line = FormattableString.Invariant($"{p.X:E10}\t{p.Y:E10}");
                if (i < _readings.Count && double.IsFinite(_readings[i].SetCurrent))
                {
                    var r = _readings[i];
                    line += FormattableString.Invariant($"\t{r.SetCurrent:E10}\t{r.VoltageStatus}\t{r.CurrentStatus}");
                }
                else line += "\t\t\t";
                lines.Add(line);
            }
            return lines;
        }

        public void LoadFromCsvLines(IReadOnlyList<string> lines)
        {
            ResultPoints.Clear();
            _readings.Clear();
            char separator = '\t';
            bool explicitSeparator = false;
            bool headerSeen = false;
            foreach (string line in lines)
            {
                string text = line.Trim();
                if (text.Length == 0 || text.StartsWith("#")) continue;
                if (text.StartsWith("sep=", StringComparison.OrdinalIgnoreCase))
                {
                    if (text.Length > 4) { separator = text[4]; explicitSeparator = true; }
                    continue;
                }
                if (!headerSeen)
                {
                    if (!explicitSeparator) separator = text.Contains('\t') ? '\t' : text.Contains(';') ? ';' : ',';
                    headerSeen = true;
                    continue;
                }
                string[] fields = line.Split(separator);
                if (fields.Length < 2 || !ParameterConfigHelper.TryParseDoubleRobust(fields[0], out double voltage) ||
                    !ParameterConfigHelper.TryParseDoubleRobust(fields[1], out double current)) continue;
                ResultPoints.Add(new CurvePoint(voltage, current));
                if (fields.Length >= 5 && ParameterConfigHelper.TryParseDoubleRobust(fields[2], out double setCurrent) &&
                    fields[3].Trim().Length == 1 && fields[4].Trim().Length == 1)
                    _readings.Add((setCurrent, fields[3].Trim()[0], fields[4].Trim()[0]));
                else _readings.Add((double.NaN, ' ', ' '));
            }
        }
    }
}
