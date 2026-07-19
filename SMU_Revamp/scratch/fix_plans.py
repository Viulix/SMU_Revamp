import os
import re
import glob

directory = r'c:\Codingprojekte\Nano-Code\SMU_Revamp\SMU_Revamp\MeasurementPlans'

for filepath in glob.glob(os.path.join(directory, '*.cs')):
    filename = os.path.basename(filepath)
    if filename in ['MeasurementPlanBase.cs', 'MeasurementPlanLoader.cs', 'FrequencyMemoryMeasurementPlan.cs', 'SpikeTimingMeasurementPlan.cs']:
        continue
    
    with open(filepath, 'r', encoding='utf-8') as f:
        code = f.read()

    # Fix unremoved GetParamValue methods
    code = re.sub(r'private\s+string\s+GetParamValueString.*?\n', '', code)
    code = re.sub(r'private\s+double\s+GetParamValueDouble.*?\n', '', code)
    code = re.sub(r'private\s+int\s+GetParamValueInt.*?\n', '', code)
    code = re.sub(r'private\s+bool\s+GetParamValueBool.*?\n', '', code)
    
    # Fix the config variables inside GetParameterDefaults
    code = re.sub(r'\{\s*"WriteChannel"\s*,\s*config\.[a-zA-Z0-9_]+\s*\}', r'{ "WriteChannel", "1" }', code)
    code = re.sub(r'\{\s*"ReadingChannel"\s*,\s*config\.[a-zA-Z0-9_]+\s*\}', r'{ "ReadingChannel", "1" }', code)
    code = re.sub(r'\{\s*"Compliance"\s*,\s*config\.[a-zA-Z0-9_]+\s*\}', r'{ "Compliance", 0.01 }', code)
    
    # Check if there are any other 'config.' instances remaining
    code = re.sub(r'config\.SweepChannel', r'"1"', code)
    code = re.sub(r'config\.SweepCompliance', r'0.01', code)
    code = re.sub(r'config\.[a-zA-Z0-9_]+', r'0', code)
    
    # One particular plan seems to have 'Channel' instead of WriteChannel
    code = re.sub(r'\{\s*"Channel"\s*,\s*config\.[a-zA-Z0-9_]+\s*\}', r'{ "Channel", "1" }', code)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(code)
