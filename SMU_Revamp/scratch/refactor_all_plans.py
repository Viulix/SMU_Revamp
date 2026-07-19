import os
import re
import glob

directory = r'c:\Codingprojekte\Nano-Code\SMU_Revamp\SMU_Revamp\MeasurementPlans'

for filepath in glob.glob(os.path.join(directory, '*.cs')):
    filename = os.path.basename(filepath)
    if filename in ['MeasurementPlanBase.cs', 'MeasurementPlanLoader.cs', 'FrequencyMemoryMeasurementPlan.cs', 'SpikeTimingMeasurementPlan.cs']:
        continue
    
    print(f"Processing {filename}...")
    with open(filepath, 'r', encoding='utf-8') as f:
        code = f.read()

    # 1. Base Class
    code = re.sub(r':\s*IMeasurementPlan', ': MeasurementPlanBase', code)
    
    # 2. Overrides
    props_to_override = ['Name', 'Description', 'PlotTitle', 'XAxisLabel', 'YAxisLabel', 'ShowLogPlot', 'PlotAspectRatio', 'DefaultPlotStyle', 'PlotSeries']
    for prop in props_to_override:
        code = re.sub(rf'public\s+([A-Za-z0-9_<>\s]+)\s+{prop}\s*=>', rf'public override \1 {prop} =>', code)
        code = re.sub(rf'public\s+([A-Za-z0-9_<>\s]+)\s+{prop}\s*\n\s*{{', rf'public override \1 {prop}\n        {{', code)
        
    # 3. Remove Fields
    code = re.sub(r'public\s+List<MeasurementParameter>\s+Parameters\s*{\s*get;\s*}.*?\n', '', code)
    code = re.sub(r'public\s+List<CurvePoint>\s+ResultPoints\s*{\s*get;\s*}\s*=\s*new\(\);.*?\n', '', code)
    code = re.sub(r'public\s+List<CurvePoint>\s+ResultPoints\s*=>.*?\n', '', code)
    
    code = re.sub(r'private\s+string\s+GetParamValueString.*?\n', '', code)
    code = re.sub(r'private\s+double\s+GetParamValueDouble.*?\n', '', code)
    code = re.sub(r'private\s+int\s+GetParamValueInt.*?\n', '', code)
    code = re.sub(r'private\s+bool\s+GetParamValueBool.*?\n', '', code)
    
    # 4. Override RunMeasurementAsync
    code = code.replace('public async Task RunMeasurementAsync', 'public override async Task RunMeasurementAsync')
    
    # 5. Extract default values and replace LoadDefaults
    # Find the LoadDefaults method block
    load_defaults_match = re.search(r'public\s+void\s+LoadDefaults\(\)\s*\{(?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*\}', code)
    if load_defaults_match:
        load_defaults_code = load_defaults_match.group(0)
        
        # Extract all cases
        # Pattern: case "ParamName": param.Value = ParameterConfigHelper.GetDefaultValue(Name, "ParamName", VALUE); break;
        cases = re.findall(r'case\s+"([^"]+)":.*?GetDefaultValue\(.*?,.*?,(.*?)\);', load_defaults_code, re.DOTALL)
        
        dict_entries = []
        for param_name, default_val in cases:
            val = default_val.strip()
            if val.startswith('config.'):
                if 'SweepChannel' in val:
                    val = '"1"'
                elif 'SweepCompliance' in val:
                    val = '0.01'
            dict_entries.append(f'                {{ "{param_name}", {val} }}')
            
        dict_string = ",\n".join(dict_entries)
        
        replacement = f'''protected override Dictionary<string, object> GetParameterDefaults()
        {{
            return new Dictionary<string, object>
            {{
{dict_string}
            }};
        }}'''
        
        code = code.replace(load_defaults_code, replacement)
        
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(code)
    print(f"Saved {filename}.")
