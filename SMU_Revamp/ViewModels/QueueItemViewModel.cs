using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SMU_Revamp.Models;

namespace SMU_Revamp.ViewModels
{
    public enum QueueItemType
    {
        Preset,
        Pause
    }

    /// <summary>
    /// One entry of the experiment queue: either a saved measurement preset
    /// (optionally repeated several times) or an inserted pause between programs.
    /// </summary>
    public class QueueItemViewModel : ObservableObject
    {
        public QueueItemType Type { get; }

        public MeasurementPreset? Preset { get; }

        private int _repetitions = 1;
        /// <summary>How often the preset is measured in a row (preset items only).</summary>
        public int Repetitions
        {
            get => _repetitions;
            set
            {
                if (SetProperty(ref _repetitions, Math.Max(1, value)))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        private double _pauseSeconds;
        /// <summary>Pause duration in seconds (pause items only).</summary>
        public double PauseSeconds
        {
            get => _pauseSeconds;
            set
            {
                if (SetProperty(ref _pauseSeconds, Math.Max(0, value)))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        private string _status = "Pending";
        /// <summary>Pending | Running | Done | Failed | Stopped</summary>
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty(ref _isRunning, value);
        }

        private bool _isDone;
        public bool IsDone
        {
            get => _isDone;
            set => SetProperty(ref _isDone, value);
        }

        private bool _isFailed;
        public bool IsFailed
        {
            get => _isFailed;
            set => SetProperty(ref _isFailed, value);
        }

        public QueueItemViewModel(QueueItemType type, MeasurementPreset? preset = null)
        {
            Type = type;
            Preset = preset;
        }

        public string DisplayName => Type == QueueItemType.Preset
            ? BuildPresetDisplayName()
            : $"Pause · {FormatDuration(PauseSeconds)}";

        private string BuildPresetDisplayName()
        {
            var name = Preset?.Name ?? "?";
            return Repetitions > 1 ? $"{name} ×{Repetitions}" : name;
        }

        internal static string FormatDuration(double seconds)
        {
            // Invariant culture so logs and UI stay consistent across machines.
            if (seconds >= 60 && seconds % 60 == 0)
            {
                return $"{(seconds / 60).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} min";
            }
            return $"{seconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} s";
        }
    }
}
