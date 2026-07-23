# Measurement Plans

The `SMU_Revamp` application uses a modular, plugin-like architecture for defining measurement plans. This allows developers to add new measurement types without modifying the core UI or execution engine.

## How to Add a New Measurement Plan

To create a new measurement plan, you do not need to register it manually. The `MeasurementPlanLoader` uses reflection to automatically discover any class that implements the `IMeasurementPlan` interface at runtime.

### Step 1: Create a Class
Create a new C# class in the `MeasurementPlans` namespace. It is highly recommended to inherit from `MeasurementPlanBase`, which handles much of the boilerplate for parameters and default values.

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SMU_Revamp.Models;
using SMU_Revamp.Services;

namespace SMU_Revamp.MeasurementPlans
{
    public class MyCustomMeasurementPlan : MeasurementPlanBase
    {
        // 1. Define UI Metadata
        public override string Name => "My Custom Sweep";
        public override string Description => "Performs a specialized voltage sweep.";

        public MyCustomMeasurementPlan()
        {
            // 2. Define Parameters for the UI
            Parameters.Add(new MeasurementParameter { Name = "StartVoltage", Type = ParameterType.Double });
            Parameters.Add(new MeasurementParameter { Name = "StopVoltage", Type = ParameterType.Double });
        }

        // 3. Provide Default Values
        protected override Dictionary<string, object> GetParameterDefaults()
        {
            return new Dictionary<string, object>
            {
                { "StartVoltage", -1.0 },
                { "StopVoltage", 1.0 }
            };
        }

        // 4. Implement the Measurement Logic
        public override async Task RunMeasurementAsync(E5263_SMU smu, IProgress<double>? progress = null)
        {
            // Extract parameter values
            double start = GetParamValueDouble("StartVoltage");
            double stop = GetParamValueDouble("StopVoltage");

            // TODO: Send SCPI commands to the SMU via `smu.QueryAsync`
            // TODO: Add results to the `ResultPoints` collection
            // ResultPoints.Add(new CurvePoint(voltage, current));
        }
    }
}
```

### Step 2: Parameter Binding and Defaults
The `MeasurementPlanBase` automatically binds the parameters you define in the constructor to the UI's parameter grid. 
When the plan is instantiated, the `LoadDefaults()` method is called. It pulls defaults from your `GetParameterDefaults()` dictionary, and for specific standard keys (like `Compliance` or `Channel`), it seamlessly merges them with the global `AppConfig` preferences.

### Step 3: Run the Application
Because of the reflection-based `MeasurementPlanLoader`, simply compiling the code and running the application will make "My Custom Sweep" appear in the UI's measurement type dropdown list, ordered alphabetically.
