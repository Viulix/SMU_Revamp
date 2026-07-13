using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SMU_Revamp.Models
{
    public enum StepType
    {
        Pulse,
        Point,
        Sweep,
        Measure
    }

    public class SequenceStep : ObservableObject
    {
        public static System.Collections.Generic.List<string> SweepModeOptions { get; } = new()
        {
            "Single Staircase (1)",
            "Double Staircase (3)"
        };

        private StepType _type;
        public StepType Type
        {
            get => _type;
            set
            {
                if (SetProperty(ref _type, value))
                {
                    NotifyAll();
                }
            }
        }

        private string _writeChannel = "2";
        public string WriteChannel
        {
            get => _writeChannel;
            set
            {
                if (SetProperty(ref _writeChannel, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        private string _readingChannel = "2";
        public string ReadingChannel
        {
            get => _readingChannel;
            set
            {
                if (SetProperty(ref _readingChannel, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        private double _delayMs = 0;
        public double DelayMs
        {
            get => _delayMs;
            set
            {
                if (SetProperty(ref _delayMs, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        private double _baseVoltage = 0.0;
        public double BaseVoltage
        {
            get => _baseVoltage;
            set
            {
                if (SetProperty(ref _baseVoltage, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        private double _pulseVoltage = 1.0;
        public double PulseVoltage
        {
            get => _pulseVoltage;
            set
            {
                if (SetProperty(ref _pulseVoltage, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        private double _pulseWidth = 0.001;
        public double PulseWidth
        {
            get => _pulseWidth;
            set
            {
                if (SetProperty(ref _pulseWidth, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        private double _pulsePeriod = 0.01;
        public double PulsePeriod
        {
            get => _pulsePeriod;
            set
            {
                if (SetProperty(ref _pulsePeriod, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        private double _voltage = 0.0;
        public double Voltage
        {
            get => _voltage;
            set
            {
                if (SetProperty(ref _voltage, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        private double _stopVoltage = 1.5;
        public double StopVoltage
        {
            get => _stopVoltage;
            set
            {
                if (SetProperty(ref _stopVoltage, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        private int _points = 41;
        public int Points
        {
            get => _points;
            set
            {
                if (SetProperty(ref _points, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        private double _compliance = 0.1;
        public double Compliance
        {
            get => _compliance;
            set
            {
                if (SetProperty(ref _compliance, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        private int _adcSamples = 1;
        public int AdcSamples
        {
            get => _adcSamples;
            set
            {
                if (SetProperty(ref _adcSamples, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        private string _sweepMode = "Single Staircase (1)";
        public string SweepMode
        {
            get => _sweepMode;
            set
            {
                if (SetProperty(ref _sweepMode, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        private bool _keepCurrentVoltage = true;
        public bool KeepCurrentVoltage
        {
            get => _keepCurrentVoltage;
            set
            {
                if (SetProperty(ref _keepCurrentVoltage, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        public bool IsPulseStep => Type == StepType.Pulse;
        public bool IsPointStep => Type == StepType.Point;
        public bool IsSweepStep => Type == StepType.Sweep;
        public bool IsMeasureStep => Type == StepType.Measure;

        public string Summary
        {
            get
            {
                switch (Type)
                {
                    case StepType.Pulse:
                        return $"Pulse: CH{WriteChannel}->CH{ReadingChannel}, base {BaseVoltage}V, pulse {PulseVoltage}V, width {PulseWidth}s, period {PulsePeriod}s";
                    case StepType.Point:
                        return $"Point: CH{WriteChannel}->CH{ReadingChannel}, volt {Voltage}V, samples {AdcSamples} PLC";
                    case StepType.Sweep:
                        return $"Sweep: CH{WriteChannel}->CH{ReadingChannel}, {Voltage}V -> {StopVoltage}V, {Points} pts, {SweepMode}";
                    case StepType.Measure:
                        string voltStr = KeepCurrentVoltage ? "Keep current" : $"{Voltage}V";
                        return $"Measure: CH{WriteChannel}->CH{ReadingChannel}, volt {voltStr}, samples {AdcSamples} PLC";
                    default:
                        return "Unknown step";
                }
            }
        }

        private void NotifyAll()
        {
            OnPropertyChanged(nameof(IsPulseStep));
            OnPropertyChanged(nameof(IsPointStep));
            OnPropertyChanged(nameof(IsSweepStep));
            OnPropertyChanged(nameof(IsMeasureStep));
            OnPropertyChanged(nameof(Summary));
        }
    }
}
