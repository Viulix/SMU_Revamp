using System.Linq;
using SMU_Revamp.MeasurementPlans;
using SMU_Revamp.Models;
using Xunit;

namespace SMU_Revamp.Tests
{
    public class ModularSequenceTests
    {
        private static string Raw(params double[] currents)
        {
            return string.Join(",", currents.Select(c => $"N2I{c.ToString("E6", System.Globalization.CultureInfo.InvariantCulture)}"));
        }

        // ------------------------------------------------------------------
        // Sweep response parsing
        // ------------------------------------------------------------------

        [Fact]
        public void ParseSweepData_SingleMode_FullResponse()
        {
            var plan = new ModularSequenceMeasurementPlan();
            var result = plan.ParseSweepData(Raw(1, 2, 3), 1, 0, 2, 3, "1", "1");

            Assert.Equal(3, result.Count);
            double[] expectedX = { 0, 1, 2 };
            for (int i = 0; i < result.Count; i++)
            {
                Assert.Equal(expectedX[i], result[i].X, 9);
                Assert.Equal(i + 1.0, result[i].Y, 15);
            }
        }

        [Fact]
        public void ParseSweepData_DoubleMode_TruncatedInAscendingRamp_KeepsTrueVoltages()
        {
            var plan = new ModularSequenceMeasurementPlan();
            // Requested 4+4 points over [-2,2], compliance tripped after 3:
            // true staircase voltages [-2,-0.666...,0.666...].
            var result = plan.ParseSweepData(Raw(1, 2, 3), 3, -2, 2, 4, "1", "1");

            Assert.Equal(3, result.Count);
            Assert.Equal(-2, result[0].X, 9);
            Assert.Equal(-2.0 / 3.0, result[1].X, 6);
            Assert.Equal(2.0 / 3.0, result[2].X, 6);
        }

        [Fact]
        public void ParseSweepData_InvertsCurrentForSeparateReadingChannel()
        {
            var plan = new ModularSequenceMeasurementPlan();
            var result = plan.ParseSweepData(Raw(1e-6), 1, 0, 1, 5, "2", "1");

            Assert.Single(result);
            Assert.Equal(-1e-6, result[0].Y, 15);
        }

        [Fact]
        public void ParsePointStep_AssignsForcedVoltageToAllPoints()
        {
            var plan = new ModularSequenceMeasurementPlan();
            var result = plan.ParseSmuData(Raw(1e-9, 2e-9, 3e-9), 0.35, "1", "1");

            Assert.Equal(3, result.Count);
            Assert.All(result, p => Assert.Equal(0.35, p.X, 9));
        }

        // ------------------------------------------------------------------
        // Sequence serialization round-trip (used for presets and persistence)
        // ------------------------------------------------------------------

        [Fact]
        public void Steps_RoundTripThroughSerializedParameter()
        {
            var source = new ModularSequenceMeasurementPlan();
            source.Steps.Add(new SequenceStep { Type = StepType.Point, WriteChannel = "1", ReadingChannel = "2", Voltage = 0.7 });
            source.Steps.Add(new SequenceStep { Type = StepType.Sweep, Voltage = -1, StopVoltage = 1, Points = 21, Compliance = 0.05 });

            source.SerializeSteps();
            string json = source.Parameters.Find(p => p.Name == "SequenceSteps")!.GetValueAsString()!;
            Assert.False(string.IsNullOrWhiteSpace(json));

            var target = new ModularSequenceMeasurementPlan();
            target.Parameters.Find(p => p.Name == "SequenceSteps")!.Value = json;
            target.DeserializeSteps();

            Assert.Equal(2, target.Steps.Count);

            var point = target.Steps[0];
            Assert.Equal(StepType.Point, point.Type);
            Assert.Equal("1", point.WriteChannel);
            Assert.Equal("2", point.ReadingChannel);
            Assert.Equal(0.7, point.Voltage, 9);

            var sweep = target.Steps[1];
            Assert.Equal(StepType.Sweep, sweep.Type);
            Assert.Equal(-1, sweep.Voltage, 9);
            Assert.Equal(1, sweep.StopVoltage, 9);
            Assert.Equal(21, sweep.Points);
            Assert.Equal(0.05, sweep.Compliance, 9);
        }

        [Fact]
        public void PlotSeries_PartitionsPointsPerStep()
        {
            var plan = new ModularSequenceMeasurementPlan();
            plan.Steps.Add(new SequenceStep { Type = StepType.Point, Voltage = 0.7 });
            plan.Steps.Add(new SequenceStep { Type = StepType.Sweep, Voltage = -1, StopVoltage = 1, Points = 3, SweepMode = "Double Staircase (3)" });

            // Step 1 contributes 1 point, step 2 a full double sweep of 6 points.
            var parsedPoint = plan.ParseSmuData(Raw(1e-9), 0.7, "1", "1");
            var parsedSweep = plan.ParseSweepData(Raw(1, 2, 3, 4, 5, 6), 3, -1, 1, 3, "1", "1");
            plan.ResultPoints.AddRange(parsedPoint);
            plan.ResultPoints.AddRange(parsedSweep);

            var series = plan.PlotSeries.ToList();
            Assert.Equal(2, series.Count);
            Assert.Single(series[0].Points);
            Assert.Equal(6, series[1].Points.Count);
            Assert.DoesNotContain(series, s => s.Name == "Other Data");
        }
    }
}
