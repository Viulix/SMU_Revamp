using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SMU_Revamp.Interfaces;
using SMU_Revamp.Models;
using SMU_Revamp.Services;

namespace SMU_Revamp.MeasurementPlans
{
    public sealed class ModularSequenceMeasurementPlan : MeasurementPlanBase
    {
        public override string Name => "Modular Sequence";
        public override string Description => "A modular, configurable measurement plan where you can arrange pulses, point measurements, sweeps, and pure measurements in any order.";
        
                
        // This holds the actual steps. Since preset saving/loading operates on Parameters,
        // we will serialize this list into a single JSON-string parameter.
        public ObservableCollection<SequenceStep> Steps { get; } = new();

        public override string PlotTitle => Name;
        public override string XAxisLabel => "Forced Voltage (V)";
        public override string YAxisLabel => "Measured Current (A)";
        public override bool ShowLogPlot => true;
        public override double PlotAspectRatio => 1.333;
        public override PlotStyle DefaultPlotStyle => PlotStyle.LineAndScatter;

        // Custom Multi-Series Plot: Each step has its own series!
        public override IReadOnlyList<PlotSeries> PlotSeries
        {
            get
            {
                var seriesList = new List<PlotSeries>();
                int pointOffset = 0;
                
                for (int i = 0; i < Steps.Count; i++)
                {
                    var step = Steps[i];
                    int expectedCount = 1;
                    if (step.Type == StepType.Sweep)
                    {
                        int modeVal = step.SweepMode.Contains("(3)") ? 3 : 1;
                        expectedCount = modeVal == 3 ? step.Points * 2 : step.Points;
                    }
                    
                    var stepPoints = ResultPoints.Skip(pointOffset).Take(expectedCount).ToList();
                    pointOffset += stepPoints.Count;
                    
                    seriesList.Add(new PlotSeries($"Step {i + 1}: {step.Type}", stepPoints));
                }
                
                // If there are left-over points, add a default series
                if (pointOffset < ResultPoints.Count)
                {
                    seriesList.Add(new PlotSeries("Other Data", ResultPoints.Skip(pointOffset).ToList()));
                }
                
                return seriesList;
            }
        }

        public ModularSequenceMeasurementPlan()
        {
            Parameters = new List<MeasurementParameter>
            {
                // This parameter will hold the serialized JSON sequence
                new() { Name = "SequenceSteps", DisplayName = "Sequence JSON:", Type = ParameterType.Text, Tooltip = "Internal serialized sequence representation", Section = "Sequencer State" }
            };
            LoadDefaults();
            
            // Subscribe to Steps collection changes
            Steps.CollectionChanged += (s, e) => 
            {
                if (e.NewItems != null)
                {
                    foreach (SequenceStep item in e.NewItems)
                    {
                        item.PropertyChanged += Step_PropertyChanged;
                    }
                }
                if (e.OldItems != null)
                {
                    foreach (SequenceStep item in e.OldItems)
                    {
                        item.PropertyChanged -= Step_PropertyChanged;
                    }
                }
                SerializeSteps();
            };
            
            var seqParam = Parameters.Find(p => p.Name == "SequenceSteps");
            if (seqParam != null)
            {
                seqParam.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(MeasurementParameter.Value))
                    {
                        DeserializeSteps();
                    }
                };
            }
        }

        private void Step_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            SerializeSteps();
        }

        protected override Dictionary<string, object> GetParameterDefaults()
        {
            return new Dictionary<string, object>
            {

            };
        }

        private bool _isDeserializing = false;

        public void SerializeSteps()
        {
            if (_isDeserializing) return;
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = false };
                var json = JsonSerializer.Serialize(Steps, options);
                var param = Parameters.Find(p => p.Name == "SequenceSteps");
                if (param != null)
                {
                    param.Value = json;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to serialize steps: {ex.Message}");
            }
        }

        public void DeserializeSteps()
        {
            var param = Parameters.Find(p => p.Name == "SequenceSteps");
            if (param == null) return;
            
            var json = param.GetValueAsString();
            if (string.IsNullOrWhiteSpace(json)) return;

            // Check if current steps JSON matches
            var options = new JsonSerializerOptions { WriteIndented = false };
            string currentJson = JsonSerializer.Serialize(Steps, options);
            if (json == currentJson) return;

            try
            {
                _isDeserializing = true;
                var list = JsonSerializer.Deserialize<List<SequenceStep>>(json);
                if (list != null)
                {
                    // Clear existing steps carefully
                    foreach (var s in Steps)
                    {
                        s.PropertyChanged -= Step_PropertyChanged;
                    }
                    Steps.Clear();
                    
                    foreach (var step in list)
                    {
                        step.PropertyChanged += Step_PropertyChanged;
                        Steps.Add(step);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to deserialize steps: {ex.Message}");
            }
            finally
            {
                _isDeserializing = false;
            }
        }

        public override async Task RunMeasurementAsync(E5263_SMU smu, IProgress<double>? progress = null)
        {
            // Sync current state
            DeserializeSteps();
            
            ResultPoints.Clear();
            progress?.Report(0);

            if (Steps.Count == 0)
            {
                progress?.Report(100);
                return;
            }

            // Find all channels used across the steps
            var channels = new HashSet<string>();
            foreach (var step in Steps)
            {
                if (!string.IsNullOrWhiteSpace(step.WriteChannel)) channels.Add(step.WriteChannel);
                if (!string.IsNullOrWhiteSpace(step.ReadingChannel)) channels.Add(step.ReadingChannel);
            }

            if (channels.Count == 0)
            {
                throw new InvalidOperationException("No channels configured in modular sequence steps.");
            }

            // 1. Reset and Enable Channels
            await smu.SendCommandAsync("*RST");
            await smu.SendCommandAsync("FMT 1");
            await smu.SendCommandAsync("TSC 1");
            
            string cnCommand = $"CN {string.Join(",", channels)}";
            await smu.SendCommandAsync(cnCommand);

            var cnError = await smu.CheckErrorAsync();
            if (cnError != null)
            {
                throw new InvalidOperationException($"SMU rejected CN command: {cnError}");
            }

            // 2. Execute Steps
            for (int i = 0; i < Steps.Count; i++)
            {
                var step = Steps[i];
                double stepProgressStart = (double)i / Steps.Count * 100.0;
                double stepProgressEnd = (double)(i + 1) / Steps.Count * 100.0;
                progress?.Report(stepProgressStart);

                var stepPoints = await RunStepOnSmuAsync(smu, step);
                ResultPoints.AddRange(stepPoints);

                if (step.DelayMs > 0)
                {
                    await Task.Delay((int)step.DelayMs);
                }
                
                progress?.Report(stepProgressEnd);
            }

            // 3. Turn off channels at the very end
            string clCommand = $"CL {string.Join(",", channels)}";
            await smu.SendCommandAsync(clCommand);

            progress?.Report(100);
        }

        private async Task<List<CurvePoint>> RunStepOnSmuAsync(E5263_SMU smu, SequenceStep step)
        {
            var points = new List<CurvePoint>();
            string channel = step.WriteChannel;
            string readingChannel = step.ReadingChannel;
            double compliance = step.Compliance;
            int adcSamples = step.AdcSamples;

            // Set averaging PLC
            await smu.SendCommandAsync($"AV -{adcSamples},0");

            switch (step.Type)
            {
                case StepType.Point:
                    {
                        var dvCommand = System.FormattableString.Invariant($"DV {channel},0,{step.Voltage},{compliance}");
                        await smu.SendCommandAsync(dvCommand);
                        await smu.SendCommandAsync($"MM 1,{readingChannel}");
                        await smu.SendCommandAsync($"CMM {readingChannel},1");
                        await smu.SendCommandAsync("TSR");
                        await smu.SendCommandAsync("XE");
                        await smu.SendCommandAsync("TSQ");
                        
                        string rawData = await smu.ReadResponseAsync(100);
                        try { _ = await smu.ReadResponseAsync(50); } catch { }
                        
                        points = ParseSmuData(rawData, step.Voltage, readingChannel, channel);
                    }
                    break;

                case StepType.Pulse:
                    {
                        var ptCommand = System.FormattableString.Invariant($"PT 0.0,{step.PulseWidth},{step.PulsePeriod}");
                        await smu.SendCommandAsync(ptCommand);
                        var pvCommand = System.FormattableString.Invariant($"PV {channel},0,{step.BaseVoltage},{step.PulseVoltage},{compliance}");
                        await smu.SendCommandAsync(pvCommand);
                        await smu.SendCommandAsync($"MM 3,{readingChannel}");
                        await smu.SendCommandAsync($"CMM {readingChannel},1");
                        await smu.SendCommandAsync("TSR");
                        await smu.SendCommandAsync("XE");
                        await smu.SendCommandAsync("TSQ");
                        
                        string rawData = await smu.ReadResponseAsync(100);
                        try { _ = await smu.ReadResponseAsync(50); } catch { }
                        
                        points = ParseSmuData(rawData, step.PulseVoltage, readingChannel, channel);
                    }
                    break;

                case StepType.Sweep:
                    {
                        int modeValue = step.SweepMode.Contains("(3)") ? 3 : 1;
                        var wvCommand = System.FormattableString.Invariant($"WV {channel},{modeValue},0,{step.Voltage},{step.StopVoltage},{step.Points},{compliance}");
                        await smu.SendCommandAsync(wvCommand);
                        await smu.SendCommandAsync($"RI {readingChannel},0");
                        await smu.SendCommandAsync($"MM 2,{readingChannel}");
                        await smu.SendCommandAsync($"CMM {readingChannel},1");
                        await smu.SendCommandAsync("TSR");
                        await smu.SendCommandAsync("XE");
                        await smu.SendCommandAsync("TSQ");

                        // Wait estimation
                        int totalPoints = modeValue == 3 ? step.Points * 2 : step.Points;
                        double plcTime = adcSamples * 0.02;
                        double estimatedDurationSeconds = totalPoints * (plcTime + 0.005) + 0.5;
                        await Task.Delay((int)(estimatedDurationSeconds * 1000));

                        int expectedBufferLength = step.Points * 32 * (modeValue == 3 ? 2 : 1) + 200;
                        string rawData = await smu.ReadResponseAsync(expectedBufferLength);
                        try { _ = await smu.ReadResponseAsync(50); } catch { }
                        
                        points = ParseSweepData(rawData, modeValue, step.Voltage, step.StopVoltage, step.Points, readingChannel, channel);
                    }
                    break;

                case StepType.Measure:
                    {
                        if (!step.KeepCurrentVoltage)
                        {
                            var dvCommand = System.FormattableString.Invariant($"DV {channel},0,{step.Voltage},{compliance}");
                            await smu.SendCommandAsync(dvCommand);
                        }
                        await smu.SendCommandAsync($"MM 1,{readingChannel}");
                        await smu.SendCommandAsync($"CMM {readingChannel},1");
                        await smu.SendCommandAsync("TSR");
                        await smu.SendCommandAsync("XE");
                        await smu.SendCommandAsync("TSQ");
                        
                        string rawData = await smu.ReadResponseAsync(100);
                        try { _ = await smu.ReadResponseAsync(50); } catch { }
                        
                        points = ParseSmuData(rawData, step.Voltage, readingChannel, channel);
                    }
                    break;
            }

            return points;
        }

        private List<CurvePoint> ParseSmuData(string rawData, double forcedVoltage, string readingChannel, string channel)
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

            bool invertCurrent = readingChannel != channel;
            foreach (var current in parsedCurrents)
            {
                points.Add(new CurvePoint(forcedVoltage, invertCurrent ? -current : current));
            }

            return points;
        }

        private List<CurvePoint> ParseSweepData(string rawData, int modeValue, double sweepStart, double sweepStop, int pointsCount, string readingChannel, string channel)
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
                    points.Add(new CurvePoint(v, invertCurrent ? -parsedCurrents[i] : parsedCurrents[i]));
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
                    points.Add(new CurvePoint(v, invertCurrent ? -parsedCurrents[i] : parsedCurrents[i]));
                }
            }

            return points;
        }
    }
}
