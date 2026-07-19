import re
with open(r'c:\Codingprojekte\Nano-Code\SMU_Revamp\SMU_Revamp\MeasurementPlans\SpikeTimingMeasurementPlan.cs', 'r', encoding='utf-8') as f:
    code = f.read()

# Remove Parameters and ResultPoints declarations
code = re.sub(r'public List<MeasurementParameter> Parameters \{ get; \}', '', code)
code = re.sub(r'public List<CurvePoint> ResultPoints \{ get; \} = new\(\);', '', code)

# Remove GetParamValueString etc.
code = re.sub(r'private string GetParamValueString.*?\n', '', code)
code = re.sub(r'private double GetParamValueDouble.*?\n', '', code)
code = re.sub(r'private int GetParamValueInt.*?\n', '', code)
code = re.sub(r'private bool GetParamValueBool.*?\n', '', code)

# Replace LoadDefaults with GetParameterDefaults
load_defaults_pattern = r'public void LoadDefaults\(\).*?}\s*}'
replacement = '''protected override Dictionary<string, object> GetParameterDefaults()
        {
            return new Dictionary<string, object>
            {
                { "WriteChannel", "1" },
                { "ReadingChannel", "1" },
                { "TimeConstantA_Ms", 100.0 },
                { "TimeConstantB_Ms", 500.0 },
                { "TimeConstantC_Ms", 1000.0 },
                { "RepetitionsPerPattern", 3 },
                { "ShuffleExecutionOrder", true },
                { "SpikeVoltage", 1.0 },
                { "SpikeLengthMs", 30.0 },
                { "NumberOfReadoutPulses", 3 },
                { "ReadoutStartTimesMs", "100;500;1000" },
                { "ReadoutVoltages", "0.3" },
                { "ReadoutPulseLengthsMs", "20" },
                { "Compliance", 0.01 },
                { "ShuffleSeed", 12345 },
                { "BaselineReadEnabled", true },
                { "ResetEnabled", true },
                { "ResetVoltage", -1.0 },
                { "ResetPulseLengthMs", 100.0 },
                { "ResetRepetitions", 1 },
                { "ResetRecoveryMs", 100.0 }
            };
        }'''
code = re.sub(load_defaults_pattern, replacement, code, flags=re.DOTALL)

with open(r'c:\Codingprojekte\Nano-Code\SMU_Revamp\SMU_Revamp\MeasurementPlans\SpikeTimingMeasurementPlan.cs', 'w', encoding='utf-8') as f:
    f.write(code)
