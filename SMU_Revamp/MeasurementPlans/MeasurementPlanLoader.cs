using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SMU_Revamp.Services;
using SMU_Revamp.Interfaces;

namespace SMU_Revamp.MeasurementPlans
{
    public static class MeasurementPlanLoader
    {
        public static List<IMeasurementPlan> LoadPlans()
        {
            var plans = new List<IMeasurementPlan>();
            try
            {
                var planType = typeof(IMeasurementPlan);
                var types = Assembly.GetExecutingAssembly().GetTypes()
                    .Where(t => planType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var type in types)
                {
                    try
                    {
                        if (Activator.CreateInstance(type) is IMeasurementPlan plan)
                        {
                            plans.Add(plan);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to instantiate plan type {type.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load plan types: {ex.Message}");
            }

            // Order alphabetically by plan name for a consistent UI dropdown
            return plans.OrderBy(p => p.Name).ToList();
        }
    }
}
