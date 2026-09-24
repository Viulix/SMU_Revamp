using System;
using System.Globalization;
using System.Linq;
using SMU_Revamp.MeasurementPlans;
using SMU_Revamp.Services;
using Xunit;

namespace SMU_Revamp.Tests
{
    /// <summary>
    /// Unit tests for the E5263 software simulator's command interpretation and
    /// data generation. Uses dedicated simulator instances (no shared state).
    /// </summary>
    public class SmuSimulatorTests
    {
        private const double ConductanceS = 1.0e-4;
        private const double SoftSaturationV = 1.2;

        private static double ExpectedCurrent(double voltage)
        {
            return ConductanceS * Math.Tanh(voltage / SoftSaturationV);
        }

        private static string[] SplitTokens(string response)
        {
            return response.Split(',', StringSplitOptions.RemoveEmptyEntries);
        }

        private static double TokenCurrent(string token)
        {
            Assert.True(token.Length >= 4 && token[2] == 'I', $"Token '{token}' is not a current token.");
            return double.Parse(token.Substring(3), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        [Fact]
        public void Sweep_FullResponse_GeneratesRequestedStaircase()
        {
            var sim = new SmuSimulator();
            // WV <ch>,<mode>,<range>,<start>,<stop>,<points>,<comp>
            sim.Execute("WV 1,3,0,0,2,3,0.01");
            sim.Execute("XE");

            var tokens = SplitTokens(sim.Read());
            Assert.Equal(6, tokens.Length);

            double[] expectedVoltages = { 0, 1, 2, 2, 1, 0 };
            for (int i = 0; i < tokens.Length; i++)
            {
                Assert.Equal(ExpectedCurrent(expectedVoltages[i]), TokenCurrent(tokens[i]), 12);
            }
        }

        [Fact]
        public void Sweep_SingleMode_GeneratesHalfThePoints()
        {
            var sim = new SmuSimulator();
            sim.Execute("WV 1,1,0,-1,1,5,0.01");
            sim.Execute("XE");

            var tokens = SplitTokens(sim.Read());
            Assert.Equal(5, tokens.Length);

            // Ascending ramp from -1 to 1.
            for (int i = 0; i < 5; i++)
            {
                double v = -1 + i * (2.0 / 4);
                Assert.Equal(ExpectedCurrent(v), TokenCurrent(tokens[i]), 12);
            }
        }

        [Fact]
        public void Sweep_ComplianceTruncatesResponseLikeRealInstrument()
        {
            var sim = new SmuSimulator();
            // tanh(1/1.2)*1e-4 = 6.82e-5 stays below 7e-5; the point at 2 V
            // (8.75e-5) trips compliance and truncates the staircase.
            sim.Execute("WV 1,3,0,0,2,3,7e-5");
            sim.Execute("XE");

            var tokens = SplitTokens(sim.Read());
            Assert.Equal(2, tokens.Length);
            Assert.All(tokens, t => Assert.True(Math.Abs(TokenCurrent(t)) <= 7e-5));
        }

        [Fact]
        public void Sweep_SecondTriggerWithoutRearm_ProducesPointReading()
        {
            var sim = new SmuSimulator();
            sim.Execute("WV 1,1,0,0,1,10,0.01");
            sim.Execute("XE");
            Assert.NotEmpty(sim.Read());

            // After the sweep completed, a trigger falls back to the forced output.
            sim.Execute("DV 1,0,0.7,0.01");
            sim.Execute("XE");

            var tokens = SplitTokens(sim.Read());
            Assert.Single(tokens);
            Assert.Equal(ExpectedCurrent(0.7), TokenCurrent(tokens[0]), 12);
        }

        [Fact]
        public void PointMeasurement_ReturnsForcedVoltageReading()
        {
            var sim = new SmuSimulator();
            sim.Execute("CN 1,2");
            sim.Execute("DV 1,0,0.5,0.01");
            sim.Execute("XE");

            var tokens = SplitTokens(sim.Read());
            Assert.Single(tokens);
            Assert.Equal(ExpectedCurrent(0.5), TokenCurrent(tokens[0]), 12);
        }

        [Fact]
        public void PulseMeasurement_ReturnsPulseVoltageReadingOnce()
        {
            var sim = new SmuSimulator();
            sim.Execute("PV 1,0,0,1.5,0.01");
            sim.Execute("XE");
            var first = SplitTokens(sim.Read());
            Assert.Single(first);
            Assert.Equal(ExpectedCurrent(1.5), TokenCurrent(first[0]), 12);

            // The pulse arming is consumed by one trigger.
            sim.Execute("XE");
            Assert.Equal(ExpectedCurrent(0), TokenCurrent(SplitTokens(sim.Read()).First()), 12);
        }

        [Fact]
        public void ErrorQueries_ReportNoError()
        {
            var sim = new SmuSimulator();
            sim.Execute("ERR? 1");
            Assert.Equal("0", sim.Read().Trim());

            sim.Execute("*IDN?");
            Assert.Contains("SIMULATOR", sim.Read());
        }

        [Fact]
        public void Tsq_EnqueuesEmptyFlushBlock()
        {
            var sim = new SmuSimulator();
            sim.Execute("TSQ");
            Assert.Equal(string.Empty, sim.Read());
        }

        [Fact]
        public void ClAndDz_ClearArmedSweep()
        {
            var sim = new SmuSimulator();
            sim.Execute("WV 1,1,0,0,1,10,0.01");
            sim.Execute("DZ");
            sim.Execute("XE");

            // No sweep armed anymore -> plain point reading at 0 V.
            var tokens = SplitTokens(sim.Read());
            Assert.Single(tokens);
            Assert.Equal(0, TokenCurrent(tokens[0]), 12);
        }

        [Fact]
        public void Reset_ClearsEverything()
        {
            var sim = new SmuSimulator();
            sim.Execute("WV 1,1,0,0,1,10,0.01");
            sim.Reset();

            Assert.Empty(sim.Read());
        }

        [Fact]
        public void Output_IsCompatibleWithPlanParsers()
        {
            var sim = new SmuSimulator();
            sim.Execute("WV 1,1,0,-1,1,5,0.01");
            sim.Execute("XE");
            string raw = sim.Read();

            var plan = new MemristorSweepMeasurementPlan();
            var points = plan.ParseDoubleSweepData(raw, -1, 1, 5, "1", "1");

            Assert.Equal(5, points.Count);
            Assert.All(points, p => Assert.Equal(ExpectedCurrent(p.X), p.Y, 9));
        }
    }
}
