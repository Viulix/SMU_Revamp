using System;
using System.Linq;
using System.Threading.Tasks;
using SMU_Revamp.MeasurementPlans;
using SMU_Revamp.Services;
using Xunit;

namespace SMU_Revamp.Tests
{
    /// <summary>
    /// End-to-end runs of complete measurement plans against the simulated
    /// E5263 connection. All tests share the E5263_SMU singleton, so they must
    /// not run in parallel with other classes touching the same singleton —
    /// keeping them in this single class guarantees sequential execution.
    /// </summary>
    public class SmuSimulationIntegrationTests : IDisposable
    {
        private readonly E5263_SMU _smu = E5263_SMU.Instance;

        public SmuSimulationIntegrationTests()
        {
            _smu.SetSimulationMode(true);
        }

        public void Dispose()
        {
            _smu.SetSimulationMode(false);
        }

        [Fact]
        public async Task MeasurePoint_ProducesOnePointAtForcedVoltage()
        {
            await _smu.ConnectAsync();
            Assert.True(_smu.IsSimulationActive);

            var plan = new MeasurePointMeasurementPlan();
            plan.Parameters.Find(p => p.Name == "Voltage")!.Value = 0.7;

            await plan.RunMeasurementAsync(_smu, null);

            var point = Assert.Single(plan.ResultPoints);
            Assert.Equal(0.7, point.X, 9);
            Assert.NotEqual(0, point.Y, 9);
        }

        [Fact]
        public async Task USweep_ProducesFullStaircaseWithAscendingAndDescendingHalf()
        {
            await _smu.ConnectAsync();

            var plan = new USweepMeasurementPlan();
            plan.Parameters.Find(p => p.Name == "StartVoltage")!.Value = -1.0;
            plan.Parameters.Find(p => p.Name == "StopVoltage")!.Value = 1.0;
            plan.Parameters.Find(p => p.Name == "Points")!.Value = 11;
            // Default sweep mode is the single staircase; use double explicitly.
            var modeParam = plan.Parameters.Find(p => p.Name == "SweepMode");
            if (modeParam != null) modeParam.Value = "Double Staircase (3)";

            await plan.RunMeasurementAsync(_smu, null);

            Assert.Equal(22, plan.ResultPoints.Count);
            // Ascending half: -1 .. +1
            Assert.Equal(-1, plan.ResultPoints[0].X, 6);
            Assert.Equal(1, plan.ResultPoints[10].X, 6);
            // Descending half starts back at +1.
            Assert.Equal(1, plan.ResultPoints[11].X, 6);
            // Currents follow the device model: positive voltage -> positive current.
            Assert.True(plan.ResultPoints[0].Y < 0);
            Assert.True(plan.ResultPoints[10].Y > 0);
        }

        [Fact]
        public async Task MemristorSweep_CompletesCycleWithParsedData()
        {
            await _smu.ConnectAsync();

            var plan = new MemristorSweepMeasurementPlan();
            plan.Parameters.Find(p => p.Name == "PositiveVoltage")!.Value = 1.0;
            plan.Parameters.Find(p => p.Name == "NegativeVoltage")!.Value = -1.0;
            plan.Parameters.Find(p => p.Name == "PointsPerSweep")!.Value = 9;
            plan.Parameters.Find(p => p.Name == "Cycles")!.Value = 2;

            await plan.RunMeasurementAsync(_smu, null);

            // 2 double sweeps per cycle * 18 points per double sweep.
            Assert.Equal(72, plan.ResultPoints.Count);
            Assert.Equal(2, plan.CycleData.Count);
            Assert.All(plan.CycleData, c => Assert.Equal(36, c.Count));
        }

        [Fact]
        public async Task ModularSequence_RunsPointAndMeasureSteps()
        {
            await _smu.ConnectAsync();

            var plan = new ModularSequenceMeasurementPlan();
            plan.Steps.Add(new SMU_Revamp.Models.SequenceStep { Type = SMU_Revamp.Models.StepType.Point, WriteChannel = "1", ReadingChannel = "1", Voltage = 0.5 });
            plan.Steps.Add(new SMU_Revamp.Models.SequenceStep { Type = SMU_Revamp.Models.StepType.Measure, WriteChannel = "1", ReadingChannel = "1", Voltage = 0.5 });

            await plan.RunMeasurementAsync(_smu, null);

            Assert.Equal(2, plan.ResultPoints.Count);
            Assert.All(plan.ResultPoints, p => Assert.True(double.IsFinite(p.Y)));
        }

        [Fact]
        public async Task PotDep_CompletesRequestedCycles()
        {
            await _smu.ConnectAsync();

            var plan = new PotDepMeasurementPlan();
            plan.Parameters.Find(p => p.Name == "CyclesPot")!.Value = 2;
            plan.Parameters.Find(p => p.Name == "CyclesDep")!.Value = 1;
            plan.Parameters.Find(p => p.Name == "CyclesRep")!.Value = 1;

            await plan.RunMeasurementAsync(_smu, null);

            Assert.Equal(3, plan.ResultPoints.Count);
            // X axis counts cycles globally starting at 1.
            Assert.Equal(1, plan.ResultPoints[0].X, 9);
            Assert.Equal(3, plan.ResultPoints[2].X, 9);
        }

        [Fact]
        public async Task CheckErrorAfterSimulatedRun_ReportsNoError()
        {
            await _smu.ConnectAsync();
            Assert.True(_smu.IsSimulationActive, $"simActive expected; connected={_smu.IsConnected}");
            await _smu.SendCommandAsync("*RST");

            var error = await _smu.CheckErrorAsync();
            Assert.True(_smu.IsConnected, $"connected lost before CheckError; sim={_smu.IsSimulationActive}");
            Assert.Null(error);
        }
    }
}
