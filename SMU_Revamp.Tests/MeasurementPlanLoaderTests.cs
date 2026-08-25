using System.Linq;
using SMU_Revamp.MeasurementPlans;
using Xunit;

namespace SMU_Revamp.Tests
{
    public class MeasurementPlanLoaderTests
    {
        [Fact]
        public void LoadPlans_ReturnsPlans()
        {
            var plans = MeasurementPlanLoader.LoadPlans();

            Assert.NotEmpty(plans);
        }

        [Fact]
        public void LoadPlans_AllNamesAreUniqueAndNotEmpty()
        {
            var plans = MeasurementPlanLoader.LoadPlans();

            Assert.All(plans, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
            Assert.Equal(plans.Count, plans.Select(p => p.Name).Distinct().Count());
        }

        [Fact]
        public void LoadPlans_EveryPlanHasParametersWithDefaults()
        {
            var plans = MeasurementPlanLoader.LoadPlans();

            foreach (var plan in plans)
            {
                Assert.NotNull(plan.Parameters);
                // Every parameter must have been initialized by the constructor
                // (LoadDefaults) so a measurement never runs with unset values.
                Assert.All(plan.Parameters, p => Assert.True(p.Value != null, $"Parameter '{plan.Name}.{p.Name}' has no value."));
            }
        }

        [Fact]
        public void LoadPlans_PlotDefaultsAreSane()
        {
            var plans = MeasurementPlanLoader.LoadPlans();

            Assert.All(plans, p => Assert.True(p.PlotAspectRatio > 0));
            Assert.All(plans, plan => Assert.False(string.IsNullOrWhiteSpace(plan.XAxisLabel)));
            Assert.False(string.IsNullOrWhiteSpace(plans[0].YAxisLabel));
        }
    }
}
