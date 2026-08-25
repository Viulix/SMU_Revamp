using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SMU_Revamp.MeasurementPlans;
using Xunit;

namespace SMU_Revamp.Tests
{
    /// <summary>
    /// Tests for the E5263 response parsers of the sweep plans, with focus on the
    /// voltage-axis reconstruction for compliance-truncated sweeps.
    ///
    /// The instrument returns an ordered prefix of the requested staircase when it
    /// stops early (compliance trip), so every received point must keep its true
    /// voltage instead of being stretched over the full sweep range.
    /// </summary>
    public class SweepParserTests
    {
        private static string Raw(params double[] currents)
        {
            return string.Join(",", currents.Select(c => $"N2I{c.ToString("E6", CultureInfo.InvariantCulture)}"));
        }

        private static void AssertPoint(SMU_Revamp.Models.CurvePoint pt, double x, double y, int index)
        {
            Assert.Equal(x, pt.X, 9);
            Assert.Equal(y, pt.Y, 9);
            _ = index;
        }

        // ------------------------------------------------------------------
        // Double staircase (mode 3): 0 -> +V -> 0, e.g. Memristor Sweep
        // ------------------------------------------------------------------

        [Fact]
        public void DoubleSweep_FullResponse_MapsAscendingAndDescending()
        {
            var plan = new MemristorSweepMeasurementPlan();
            // start=0, stop=2, pointsCount=3 -> full response is 6 points
            var result = plan.ParseDoubleSweepData(Raw(1e-9, 2e-9, 3e-9, 4e-9, 5e-9, 6e-9), 0, 2, 3, "1", "1");

            Assert.Equal(6, result.Count);
            double[] expectedX = { 0, 1, 2, 2, 1, 0 };
            for (int i = 0; i < result.Count; i++)
            {
                Assert.Equal(expectedX[i], result[i].X, 9);
                Assert.Equal((i + 1) * 1e-9, result[i].Y, 15);
            }
        }

        [Fact]
        public void DoubleSweep_TruncatedInAscendingRamp_KeepsTrueVoltages()
        {
            var plan = new MemristorSweepMeasurementPlan();
            // Requested 3 points per ramp but compliance tripped after 2 points:
            // true voltages are the first two staircase values [0, 1].
            var result = plan.ParseDoubleSweepData(Raw(1e-9, 2e-9), 0, 2, 3, "1", "1");

            Assert.Equal(2, result.Count);
            AssertPoint(result[0], 0, 1e-9, 0);
            AssertPoint(result[1], 1, 2e-9, 1);
        }

        [Fact]
        public void DoubleSweep_TruncatedInDescendingRamp_KeepsTrueVoltages()
        {
            var plan = new MemristorSweepMeasurementPlan();
            // Full ascent (3 points) plus only the first descent point.
            var result = plan.ParseDoubleSweepData(Raw(1e-9, 2e-9, 3e-9, 4e-9), 0, 2, 3, "1", "1");

            Assert.Equal(4, result.Count);
            double[] expectedX = { 0, 1, 2, 2 };
            for (int i = 0; i < result.Count; i++)
            {
                Assert.Equal(expectedX[i], result[i].X, 9);
            }
        }

        [Fact]
        public void DoubleSweep_InvertsCurrentForSeparateReadingChannel()
        {
            var plan = new MemristorSweepMeasurementPlan();
            var result = plan.ParseDoubleSweepData(Raw(1e-9, 2e-9), 0, 2, 3, "1", "2");

            Assert.Equal(2, result.Count);
            Assert.Equal(-1e-9, result[0].Y, 15);
            Assert.Equal(-2e-9, result[1].Y, 15);
        }

        // ------------------------------------------------------------------
        // U-Sweep: single (mode 1) and double (mode 3) staircase
        // ------------------------------------------------------------------

        [Fact]
        public void USweep_SingleSweep_FullResponse_CoversFullRange()
        {
            var plan = new USweepMeasurementPlan();
            var result = plan.ParseSmuData(Raw(1, 2, 3, 4), 1, 0, 3, 4);

            Assert.Equal(4, result.Count);
            double[] expectedX = { 0, 1, 2, 3 };
            for (int i = 0; i < result.Count; i++)
            {
                Assert.Equal(expectedX[i], result[i].X, 9);
                Assert.Equal(i + 1.0, result[i].Y, 15);
            }
        }

        [Fact]
        public void USweep_SingleSweep_TruncatedResponse_KeepsTrueVoltages()
        {
            var plan = new USweepMeasurementPlan();
            // Requested 4 points over [0,3], received only 2: true voltages [0,1],
            // not the old behaviour which stretched them to [0,3].
            var result = plan.ParseSmuData(Raw(1, 2), 1, 0, 3, 4);

            Assert.Equal(2, result.Count);
            Assert.Equal(0, result[0].X, 9);
            Assert.Equal(1, result[1].X, 9);
        }

        [Fact]
        public void USweep_DoubleSweep_TruncatedInAscendingRamp_KeepsTrueVoltages()
        {
            var plan = new USweepMeasurementPlan();
            // Requested 3+3, received only 1: single ascending point at the start.
            var result = plan.ParseSmuData(Raw(5), 3, 0, 2, 3);

            Assert.Single(result);
            Assert.Equal(0, result[0].X, 9);
        }

        [Fact]
        public void USweep_PulseSweepStyle_TruncatedInDescendingRamp_KeepsTrueVoltages()
        {
            var pulsePlan = new PulseSweepMeasurementPlan();
            var result = pulsePlan.ParseSmuData(Raw(1, 2, 3, 4), 3, -1, 1, 2);

            // N=2: ascent [-1,1], descent starts back at +1
            Assert.Equal(4, result.Count);
            double[] expectedX = { -1, 1, 1, -1 };
            for (int i = 0; i < result.Count; i++)
            {
                Assert.Equal(expectedX[i], result[i].X, 9);
            }
        }

        // ------------------------------------------------------------------
        // Robustness
        // ------------------------------------------------------------------

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Parser_EmptyResponse_ReturnsNoPoints(string rawData)
        {
            var memristor = new MemristorSweepMeasurementPlan();
            Assert.Empty(memristor.ParseDoubleSweepData(rawData, 0, 1, 10, "1", "1"));

            var uSweep = new USweepMeasurementPlan();
            Assert.Empty(uSweep.ParseSmuData(rawData, 1, 0, 1, 10));
        }

        [Fact]
        public void Parser_IgnoresNonCurrentTokens()
        {
            var plan = new MemristorSweepMeasurementPlan();
            // Voltage tokens ("N2V...") and short garbage must be ignored.
            string raw = $"N2V{1.0.ToString("E6", CultureInfo.InvariantCulture)},xx,N2I{2.5e-3.ToString("E6", CultureInfo.InvariantCulture)}";
            var result = plan.ParseDoubleSweepData(raw, 0, 1, 1, "1", "1");

            Assert.Single(result);
            Assert.Equal(2.5e-3, result[0].Y, 15);
        }
    }
}
