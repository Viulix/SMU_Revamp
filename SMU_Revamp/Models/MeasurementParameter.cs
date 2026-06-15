using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SMU_Revamp.Models
{
    public enum ParameterType
    {
        Text,
        Number,
        Checkbox,
        Dropdown
    }

    public partial class MeasurementParameter : ObservableObject
    {
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private string _displayName = string.Empty;
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        private string _section = string.Empty;
        public string Section
        {
            get => _section;
            set => SetProperty(ref _section, value);
        }

        private object _value = string.Empty;
        public object Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value))
                {
                    OnPropertyChanged(nameof(IsTextOrNumber));
                    OnPropertyChanged(nameof(IsCheckbox));
                    OnPropertyChanged(nameof(IsDropdown));
                }
            }
        }

        private ParameterType _type = ParameterType.Text;
        public ParameterType Type
        {
            get => _type;
            set
            {
                if (SetProperty(ref _type, value))
                {
                    OnPropertyChanged(nameof(IsTextOrNumber));
                    OnPropertyChanged(nameof(IsCheckbox));
                    OnPropertyChanged(nameof(IsDropdown));
                }
            }
        }

        private string? _tooltip;
        public string? Tooltip
        {
            get => _tooltip;
            set => SetProperty(ref _tooltip, value);
        }

        private List<string>? _options;
        public List<string>? Options
        {
            get => _options;
            set => SetProperty(ref _options, value);
        }

        public bool IsTextOrNumber => Type == ParameterType.Text || Type == ParameterType.Number;
        public bool IsCheckbox => Type == ParameterType.Checkbox;
        public bool IsDropdown => Type == ParameterType.Dropdown;

        public double GetValueAsDouble(double defaultValue = 0.0)
        {
            if (Value is double d) return d;
            if (Value is float f) return f;
            if (Value is int i) return i;
            if (Value != null && double.TryParse(Value.ToString()?.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }
            return defaultValue;
        }

        public int GetValueAsInt(int defaultValue = 0)
        {
            if (Value is int i) return i;
            if (Value is double d) return (int)d;
            if (Value != null && int.TryParse(Value.ToString(), out int result))
            {
                return result;
            }
            return defaultValue;
        }

        public bool GetValueAsBool(bool defaultValue = false)
        {
            if (Value is bool b) return b;
            if (Value != null && bool.TryParse(Value.ToString(), out bool result))
            {
                return result;
            }
            return defaultValue;
        }

        public string GetValueAsString()
        {
            return Value?.ToString() ?? string.Empty;
        }
    }
}
