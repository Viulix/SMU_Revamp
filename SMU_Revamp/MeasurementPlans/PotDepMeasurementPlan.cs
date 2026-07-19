using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SMU_Revamp.Models;
using SMU_Revamp.Services;
using SMU_Revamp.Interfaces;

namespace SMU_Revamp.MeasurementPlans
{
    public sealed class PotDepMeasurementPlan : MeasurementPlanBase
    {
        public override string Name => "PotDep";
        public override string Description => "Performs cycles of Potentiation and Depression and measures read current.";
                        public override double PlotAspectRatio => 3.0;
        public override PlotStyle DefaultPlotStyle => PlotStyle.LineAndScatter;

                        
        public PotDepMeasurementPlan()
        {
            Parameters = new List<MeasurementParameter>
            {
                new() { Name = "WriteChannel", DisplayName = "Write Channel:", Type = ParameterType.Text, Tooltip = "The SMU channel number (e.g. 2)", Section = "Channel Settings" },
                new() { Name = "ReadingChannel", DisplayName = "Reading Channel:", Type = ParameterType.Text, Tooltip = "The SMU channel to measure (e.g. 1 or 2)", Section = "Channel Settings" },
                
                new() { Name = "Vpot", DisplayName = "V_pot (V):", Type = ParameterType.Number, Tooltip = "Voltage for potentiation (in Volts)", Section = "Voltage Settings" },
                new() { Name = "Vdep", DisplayName = "V_dep (V):", Type = ParameterType.Number, Tooltip = "Voltage for depression (in Volts)", Section = "Voltage Settings" },
                new() { Name = "VreadPD", DisplayName = "Vread (V):", Type = ParameterType.Number, Tooltip = "Read voltage (in Volts)", Section = "Voltage Settings" },
                new() { Name = "Compliance", DisplayName = "Compliance (A):", Type = ParameterType.Number, Tooltip = "The current compliance limit (in Amperes)", Section = "Voltage Settings" },
                
                new() { Name = "tpot", DisplayName = "t_pot (ms):", Type = ParameterType.Number, Tooltip = "Time for potentiation (in ms)", Section = "Time Settings" },
                new() { Name = "tdep", DisplayName = "t_dep (ms):", Type = ParameterType.Number, Tooltip = "Time for depression (in ms)", Section = "Time Settings" },
                new() { Name = "treadPD", DisplayName = "tread (ms):", Type = ParameterType.Number, Tooltip = "Read time (in ms)", Section = "Time Settings" },
                new() { Name = "WaitBeforeRead", DisplayName = "Wait Before Read (ms):", Type = ParameterType.Number, Tooltip = "Effective wait time before reading (in ms)", Section = "Time Settings" },
                
                new() { Name = "CyclesPot", DisplayName = "Cycles Pot:", Type = ParameterType.Number, Tooltip = "Number of potentiation cycles per repetition", Section = "Cycle Settings" },
                new() { Name = "CyclesDep", DisplayName = "Cycles Dep:", Type = ParameterType.Number, Tooltip = "Number of depression cycles per repetition", Section = "Cycle Settings" },
                new() { Name = "CyclesRep", DisplayName = "Cycles Rep:", Type = ParameterType.Number, Tooltip = "Number of repetitions of the loop", Section = "Cycle Settings" }
            };
            LoadDefaults();
        }

        protected override Dictionary<string, object> GetParameterDefaults()
        {
            return new Dictionary<string, object>
            {
                { "WriteChannel", "1" },
                { "ReadingChannel", "1" },
                { "Vpot", 1.0 },
                { "Vdep", -1.0 },
                { "VreadPD", 0.1 },
                { "Compliance", 0.1 },
                { "tpot", 10.0 },
                { "tdep", 10.0 },
                { "treadPD", 5.0 },
                { "WaitBeforeRead", 0.0 },
                { "CyclesPot", 1 },
                { "CyclesDep", 1 },
                { "CyclesRep", 1 }
            };
        }

        private async Task WaitMillisecondsAccurateAsync(double ms, CancellationToken ct)
        {
            if (ms <= 0) return;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < ms)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(1, ct);
            }
        }

        private double ParseReading(string rawData, bool invertCurrent)
        {
            if (string.IsNullOrWhiteSpace(rawData)) return 0.0;
            var items = rawData.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in items)
            {
                var trimmed = item.Trim();
                if (trimmed.Length >= 4 && trimmed[2] == 'I')
                {
                    string numStr = trimmed.Substring(3);
                    if (double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double iVal))
                    {
                        return invertCurrent ? -iVal : iVal;
                    }
                }
            }
            return 0.0;
        }

        public override async Task RunMeasurementAsync(E5263_SMU smu, IProgress<double>? progress = null)
        {
            ResultPoints.Clear();
            progress?.Report(0);

            string channel = GetParamValueString("WriteChannel");
            string readingChannel = GetParamValueString("ReadingChannel");
            if (string.IsNullOrWhiteSpace(readingChannel)) readingChannel = channel;
            bool invertCurrent = readingChannel != channel;

            double vpot = GetParamValueDouble("Vpot");
            double tpot = GetParamValueDouble("tpot");
            double vdep = GetParamValueDouble("Vdep");
            double tdep = GetParamValueDouble("tdep");
            double vreadPD = GetParamValueDouble("VreadPD");
            double treadPD = GetParamValueDouble("treadPD");
            double waitBR = GetParamValueDouble("WaitBeforeRead");
            double compliance = GetParamValueDouble("Compliance");
            
            int cyclesPot = GetParamValueInt("CyclesPot");
            int cyclesDep = GetParamValueInt("CyclesDep");
            int cyclesRep = GetParamValueInt("CyclesRep");

            int totalCycles = cyclesRep * (cyclesPot + cyclesDep);
            if (totalCycles <= 0) return;

            await smu.SendCommandAsync("*RST");
            if (readingChannel != channel)
            {
                await smu.SendCommandAsync($"CN {channel},{readingChannel}");
            }
            else
            {
                await smu.SendCommandAsync($"CN {channel}");
            }
            await smu.SendCommandAsync($"MM 1,{readingChannel}");
            await smu.SendCommandAsync($"CMM {readingChannel},1");
            await smu.SendCommandAsync($"RV {channel},0");
            await smu.SendCommandAsync($"RI {channel},0");
            await smu.SendCommandAsync("FMT 1,0");
            await smu.SendCommandAsync($"DZ {channel}");
            
            var setupError = await smu.CheckErrorAsync();
            if (setupError != null) throw new InvalidOperationException($"SMU setup error: {setupError}");

            int cycleGlobal = 1;
            using var cts = new CancellationTokenSource();
            
            for (int rep = 1; rep <= cyclesRep; rep++)
            {
                // Potentiation
                for (int cyc = 1; cyc <= cyclesPot; cyc++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    
                    await smu.SendCommandAsync($"DZ {channel}");
                    await smu.SendCommandAsync(System.FormattableString.Invariant($"DV {channel},0,{vpot},{compliance}"));
                    await WaitMillisecondsAccurateAsync(tpot, cts.Token);
                    await smu.SendCommandAsync($"DZ {channel}");

                    if (waitBR > 0) await WaitMillisecondsAccurateAsync(waitBR, cts.Token);

                    await WaitMillisecondsAccurateAsync(1, cts.Token);
                    await smu.SendCommandAsync(System.FormattableString.Invariant($"DV {channel},0,{vreadPD},{compliance}"));
                    await WaitMillisecondsAccurateAsync(5, cts.Token);
                    
                    await smu.SendCommandAsync("XE");
                    await WaitMillisecondsAccurateAsync(treadPD, cts.Token);
                    await WaitMillisecondsAccurateAsync(10, cts.Token);
                    
                    string resp = await smu.ReadResponseAsync(100);
                    double iRead = ParseReading(resp, invertCurrent);
                    await smu.SendCommandAsync($"DZ {channel}");

                    ResultPoints.Add(new CurvePoint(cycleGlobal, iRead));
                    progress?.Report(cycleGlobal * 100.0 / totalCycles);
                    cycleGlobal++;

                    var loopError = await smu.CheckErrorAsync();
                    if (loopError != null)
                    {
                        await smu.SendCommandAsync("DZ");
                        throw new InvalidOperationException($"SMU error during Potentiation: {loopError}");
                    }
                }

                // Depression
                for (int cyc = 1; cyc <= cyclesDep; cyc++)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    await smu.SendCommandAsync($"DZ {channel}");
                    await smu.SendCommandAsync(System.FormattableString.Invariant($"DV {channel},0,{vdep},{compliance}"));
                    await WaitMillisecondsAccurateAsync(tdep, cts.Token);
                    await smu.SendCommandAsync($"DZ {channel}");

                    if (waitBR > 0) await WaitMillisecondsAccurateAsync(waitBR, cts.Token);

                    await WaitMillisecondsAccurateAsync(1, cts.Token);
                    await smu.SendCommandAsync(System.FormattableString.Invariant($"DV {channel},0,{vreadPD},{compliance}"));
                    await WaitMillisecondsAccurateAsync(5, cts.Token);
                    
                    await smu.SendCommandAsync("XE");
                    await WaitMillisecondsAccurateAsync(treadPD, cts.Token);
                    await WaitMillisecondsAccurateAsync(10, cts.Token);

                    string resp = await smu.ReadResponseAsync(100);
                    double iRead = ParseReading(resp, invertCurrent);
                    await smu.SendCommandAsync($"DZ {channel}");

                    ResultPoints.Add(new CurvePoint(cycleGlobal, iRead));
                    progress?.Report(cycleGlobal * 100.0 / totalCycles);
                    cycleGlobal++;

                    var loopError = await smu.CheckErrorAsync();
                    if (loopError != null)
                    {
                        await smu.SendCommandAsync("DZ");
                        throw new InvalidOperationException($"SMU error during Depression: {loopError}");
                    }
            }
        }
    }

        public IReadOnlyList<string> GetCsvLines()
        {
            var lines = new List<string>
            {
                "Cycle\tCurrent (A)"
            };

            foreach (var point in ResultPoints)
            {
                lines.Add(System.FormattableString.Invariant($"{(int)point.X}\t{point.Y:E6}"));
            }

            return lines;
        }
    }
}
