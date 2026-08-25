using System.Globalization;
using System.Linq;
using SMU_Revamp.MeasurementPlans;
using Xunit;

namespace SMU_Revamp.Tests
{
    public class PointParserTests
    {
        private static string Raw(params double[] currents)
        {
            return string.Join(",", currents.Select(c => $"N2I{c.ToString("E6", CultureInfo.InvariantCulture)}"));
        }

        [Fact]
        public void MeasurePoint_ExtractsCurrentsWithForcedVoltageAsX()
        {
            var plan = new MeasurePointMeasurementPlan();
            var result = plan.ParseSmuData(Raw(1e-6, -2e-6), 0.7);

            Assert.Equal(2, result.Count);
            Assert.Equal(0.7, result[0].X, 9);
            Assert.Equal(1e-6, result[0].Y, 15);
            Assert.Equal(0.7, result[1].X, 9);
            Assert.Equal(-2e-6, result[1].Y, 15);
        }

        [Fact]
        public void MeasurePoint_InvertsCurrentForSeparateReadingChannel()
        {
            var plan = new MeasurePointMeasurementPlan();
            plan.Parameters.Find(p => p.Name == "ReadingChannel")!.Value = "2";
            try
            {
                var result = plan.ParseSmuData(Raw(1e-6), 0.5);
                Assert.Single(result);
                Assert.Equal(-1e-6, result[0].Y, 15);
            }
            finally
            {
                plan.Parameters.Find(p => p.Name == "ReadingChannel")!.Value = "1";
            }
        }

        [Fact]
        public void PulseSpot_ParsesPulseVoltageAsX()
        {
            var plan = new PulseSpotMeasurementPlan();
            var result = plan.ParseSmuData(Raw(3.5e-4), 1.25);

            Assert.Single(result);
            Assert.Equal(1.25, result[0].X, 9);
            Assert.Equal(3.5e-4, result[0].Y, 15);
        }

        [Theory]
        [InlineData("")]
        [InlineData("no data here")]
        public void MeasurePoint_EmptyOrGarbage_ReturnsNoPoints(string rawData)
        {
            var plan = new MeasurePointMeasurementPlan();
            Assert.Empty(plan.ParseSmuData(rawData, 1.0));
        }

        // ------------------------------------------------------------------
        // PotDep single-reading parser
        // ------------------------------------------------------------------

        [Fact]
        public void PotDep_ParseReading_ReturnsFirstCurrentValue()
        {
            var plan = new PotDepMeasurementPlan();
            double value = plan.ParseReading(Raw(9.9, 1.5e-3), invertCurrent: false);

            Assert.Equal(9.9, value, 15);
        }

        [Fact]
        public void PotDep_ParseReading_InvertsWhenConfigured()
        {
            var plan = new PotDepMeasurementPlan();
            double value = plan.ParseReading(Raw(2e-6), invertCurrent: true);

            Assert.Equal(-2e-6, value, 15);
        }

        [Theory]
        [InlineData("")]
        [InlineData("N2V3.3")]
        public void PotDep_ParseReading_MissingCurrentReturnsZero(string rawData)
        {
            var plan = new PotDepMeasurementPlan();
            Assert.Equal(0.0, plan.ParseReading(rawData, invertCurrent: false), 15);
        }
    }
}
