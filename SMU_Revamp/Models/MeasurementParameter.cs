using System;
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
                    OnPropertyChanged(nameof(IsLinkable));
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

        private double? _scrollStep;
        public double ScrollStep
        {
            get
            {
                if (_scrollStep.HasValue)
                {
                    return _scrollStep.Value;
                }
                
                if (string.IsNullOrEmpty(Name))
                {
                    return 1.0;
                }

                if (Name.Contains("Voltage", StringComparison.OrdinalIgnoreCase) || Name.StartsWith("V", StringComparison.OrdinalIgnoreCase))
                {
                    return 0.1;
                }
                if (Name.Contains("Compliance", StringComparison.OrdinalIgnoreCase) || 
                    Name.Contains("Current", StringComparison.OrdinalIgnoreCase) ||
                    Name.StartsWith("I", StringComparison.OrdinalIgnoreCase))
                {
                    return 0.01;
                }
                if (Name.Contains("Width", StringComparison.OrdinalIgnoreCase) || Name.Contains("Period", StringComparison.OrdinalIgnoreCase))
                {
                    if (Name.EndsWith("Ms", StringComparison.OrdinalIgnoreCase))
                        return 1.0;
                    return 0.001;
                }
                if (Name.Contains("Constant", StringComparison.OrdinalIgnoreCase) || Name.EndsWith("Time", StringComparison.OrdinalIgnoreCase) || Name.EndsWith("Ms", StringComparison.OrdinalIgnoreCase) || Name.StartsWith("t", StringComparison.OrdinalIgnoreCase))
                {
                    return 1.0;
                }
                if (Name.Contains("Points", StringComparison.OrdinalIgnoreCase) || Name.Contains("Cycles", StringComparison.OrdinalIgnoreCase) || Name.Contains("Repetitions", StringComparison.OrdinalIgnoreCase) || Name.Contains("Samples", StringComparison.OrdinalIgnoreCase) || Name.Contains("Seed", StringComparison.OrdinalIgnoreCase))
                {
                    return 1.0;
                }

                return 1.0;
            }
            set => SetProperty(ref _scrollStep, value);
        }

        private double? _minValue;
        public double? MinValue
        {
            get
            {
                if (_minValue.HasValue) return _minValue.Value;
                
                if (string.IsNullOrEmpty(Name)) return null;

                if (Name.Contains("Channel", StringComparison.OrdinalIgnoreCase))
                {
                    return 1.0;
                }
                if (Name.Contains("Compliance", StringComparison.OrdinalIgnoreCase))
                {
                    return 0.0;
                }
                if (Name.Contains("Points", StringComparison.OrdinalIgnoreCase) || 
                    Name.Contains("Cycles", StringComparison.OrdinalIgnoreCase) || 
                    Name.Contains("Repetitions", StringComparison.OrdinalIgnoreCase) ||
                    Name.Contains("Samples", StringComparison.OrdinalIgnoreCase))
                {
                    return 1.0;
                }
                if (Name.Contains("Width", StringComparison.OrdinalIgnoreCase) || 
                    Name.Contains("Period", StringComparison.OrdinalIgnoreCase) ||
                    Name.Contains("Time", StringComparison.OrdinalIgnoreCase) || 
                    Name.EndsWith("Ms", StringComparison.OrdinalIgnoreCase) ||
                    Name.StartsWith("t", StringComparison.OrdinalIgnoreCase) ||
                    Name.Contains("Delay", StringComparison.OrdinalIgnoreCase))
                {
                    return 0.0;
                }
                return null;
            }
            set => SetProperty(ref _minValue, value);
        }

        private double? _maxValue;
        public double? MaxValue
        {
            get
            {
                if (_maxValue.HasValue) return _maxValue.Value;
                if (!string.IsNullOrEmpty(Name) && Name.Contains("Channel", StringComparison.OrdinalIgnoreCase))
                {
                    return 2.0;
                }
                return null;
            }
            set => SetProperty(ref _maxValue, value);
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

        public bool IsLinkable => Type == ParameterType.Number;

        private bool _isLinked = false;
        public bool IsLinked
        {
            get => _isLinked;
            set
            {
                if (SetProperty(ref _isLinked, value))
                {
                    OnPropertyChanged(nameof(LinkTooltip));
                    if (value && LinkedParameter != null)
                    {
                        UpdateFromLinkedParameter();
                    }
                }
            }
        }

        private MeasurementParameter? _linkedParameter;
        public MeasurementParameter? LinkedParameter
        {
            get => _linkedParameter;
            set
            {
                if (_linkedParameter != null)
                {
                    _linkedParameter.PropertyChanged -= LinkedParameter_PropertyChanged;
                }
                if (SetProperty(ref _linkedParameter, value))
                {
                    OnPropertyChanged(nameof(LinkTooltip));
                    if (_linkedParameter != null)
                    {
                        _linkedParameter.PropertyChanged += LinkedParameter_PropertyChanged;
                    }
                }
            }
        }

        private double _linkedMultiplier = 1.0;
        public double LinkedMultiplier
        {
            get => _linkedMultiplier;
            set
            {
                if (SetProperty(ref _linkedMultiplier, value))
                {
                    OnPropertyChanged(nameof(LinkTooltip));
                }
            }
        }

        public string LinkTooltip
        {
            get
            {
                if (IsLinked && LinkedParameter != null)
                {
                    string multiplierStr = LinkedMultiplier == 1.0 ? "" : $" (x {LinkedMultiplier})";
                    return $"Linked to {LinkedParameter.DisplayName}{multiplierStr}";
                }
                return Tooltip ?? "Click to toggle or right-click to manage parameter link";
            }
        }

        private void LinkedParameter_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Value) && IsLinked)
            {
                UpdateFromLinkedParameter();
            }
        }

        private void UpdateFromLinkedParameter()
        {
            if (LinkedParameter == null) return;
            
            if (double.TryParse(LinkedParameter.GetValueAsString().Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double numVal))
            {
                double linkedVal = numVal * LinkedMultiplier;
                Value = linkedVal.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
