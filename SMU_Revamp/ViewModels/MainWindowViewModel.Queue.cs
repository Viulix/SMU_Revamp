using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SMU_Revamp.Models;
using SMU_Revamp.Services;

namespace SMU_Revamp.ViewModels;

public partial class MainWindowViewModel
{
    public ObservableCollection<QueueItemViewModel> MeasurementQueue { get; } = new();

    private QueueItemViewModel? _selectedQueueItem;
    public QueueItemViewModel? SelectedQueueItem
    {
        get => _selectedQueueItem;
        set
        {
            if (SetProperty(ref _selectedQueueItem, value))
            {
                NotifyQueueSelectionCommandsChanged();
            }
        }
    }

    private double _queuePauseMinutes = 5;
    /// <summary>Pause length in minutes used when adding a pause item.</summary>
    public double QueuePauseMinutes
    {
        get => _queuePauseMinutes;
        set => SetProperty(ref _queuePauseMinutes, Math.Max(0, value));
    }

    private bool _isQueueRunning;
    public bool IsQueueRunning
    {
        get => _isQueueRunning;
        private set
        {
            if (SetProperty(ref _isQueueRunning, value))
            {
                NotifyQueueCommandsChanged();
                OnPropertyChanged(nameof(IsExperimentBusy));
            }
        }
    }

    /// <summary>True while a measurement, wafer scan, or the queue is executing.</summary>
    public bool IsExperimentBusy => IsMeasuring || IsScanningWafer || IsQueueRunning;

    private string _queueStatusText = string.Empty;
    public string QueueStatusText
    {
        get => _queueStatusText;
        set => SetProperty(ref _queueStatusText, value);
    }

    private CancellationTokenSource? _queueCts;

    public ICommand AddSelectedPresetToQueueCommand { get; private set; } = null!;
    public ICommand AddQueuePauseCommand { get; private set; } = null!;
    public ICommand RemoveQueueItemCommand { get; private set; } = null!;
    public ICommand MoveQueueItemUpCommand { get; private set; } = null!;
    public ICommand MoveQueueItemDownCommand { get; private set; } = null!;
    public ICommand ClearQueueCommand { get; private set; } = null!;
    public IAsyncRelayCommand StartQueueCommand { get; private set; } = null!;
    public ICommand StopQueueCommand { get; private set; } = null!;

    private void InitializeQueueCommands()
    {
        AddSelectedPresetToQueueCommand = new RelayCommand(AddSelectedPresetToQueue);
        AddQueuePauseCommand = new RelayCommand(AddQueuePause);
        RemoveQueueItemCommand = new RelayCommand(RemoveSelectedQueueItem, () => SelectedQueueItem != null && !IsQueueRunning);
        MoveQueueItemUpCommand = new RelayCommand(MoveSelectedQueueItemUp, () => SelectedQueueItem != null && !IsQueueRunning);
        MoveQueueItemDownCommand = new RelayCommand(MoveSelectedQueueItemDown, () => SelectedQueueItem != null && !IsQueueRunning);
        ClearQueueCommand = new RelayCommand(ClearQueue, () => MeasurementQueue.Count > 0 && !IsQueueRunning);
        StartQueueCommand = new AsyncRelayCommand(StartQueueAsync, CanStartQueue);
        StopQueueCommand = new RelayCommand(() => _queueCts?.Cancel(), () => IsQueueRunning);

        MeasurementQueue.CollectionChanged += (_, _) =>
        {
            NotifyQueueCommandsChanged();
        };
    }

    private void NotifyQueueCommandsChanged()
    {
        (RemoveQueueItemCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (MoveQueueItemUpCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (MoveQueueItemDownCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ClearQueueCommand as RelayCommand)?.NotifyCanExecuteChanged();
        StartQueueCommand.NotifyCanExecuteChanged();
        (StopQueueCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void NotifyQueueSelectionCommandsChanged()
    {
        (RemoveQueueItemCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (MoveQueueItemUpCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (MoveQueueItemDownCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    /// <summary>Called whenever another experiment engine changes state so queue buttons stay current.</summary>
    private void NotifyStartQueueCanExecuteChanged()
    {
        OnPropertyChanged(nameof(IsExperimentBusy));
        StartQueueCommand.NotifyCanExecuteChanged();
        (StopQueueCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private bool CanStartQueue()
    {
        return !IsQueueRunning && !IsScanningWafer && !IsMeasuring &&
               MeasurementQueue.Any(i => i.Type == QueueItemType.Preset);
    }

    private void AddSelectedPresetToQueue()
    {
        if (IsQueueRunning) return;
        if (SelectedPreset == null)
        {
            NotificationRequested?.Invoke("Queue", "Select a preset first.", null);
            return;
        }

        var item = new QueueItemViewModel(QueueItemType.Preset, SelectedPreset)
        {
            Repetitions = 1
        };
        MeasurementQueue.Add(item);
        SelectedQueueItem = item;
    }

    private void AddQueuePause()
    {
        if (IsQueueRunning) return;

        double seconds = Math.Max(0, QueuePauseMinutes * 60.0);
        if (seconds <= 0)
        {
            NotificationRequested?.Invoke("Queue", "Pause must be greater than zero minutes.", null);
            return;
        }

        var item = new QueueItemViewModel(QueueItemType.Pause) { PauseSeconds = seconds };
        MeasurementQueue.Add(item);
        SelectedQueueItem = item;
    }

    private void RemoveSelectedQueueItem()
    {
        if (IsQueueRunning || SelectedQueueItem == null) return;
        MeasurementQueue.Remove(SelectedQueueItem);
        SelectedQueueItem = null;
    }

    private void MoveSelectedQueueItem(int offset)
    {
        if (IsQueueRunning || SelectedQueueItem == null) return;
        int index = MeasurementQueue.IndexOf(SelectedQueueItem);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= MeasurementQueue.Count) return;
        MeasurementQueue.Move(index, target);
    }

    private void MoveSelectedQueueItemUp() => MoveSelectedQueueItem(-1);

    private void MoveSelectedQueueItemDown() => MoveSelectedQueueItem(1);

    private void ClearQueue()
    {
        if (IsQueueRunning) return;
        MeasurementQueue.Clear();
        SelectedQueueItem = null;
        QueueStatusText = string.Empty;
    }

    private async Task StartQueueAsync()
    {
        if (!CanStartQueue()) return;

        foreach (var resetItem in MeasurementQueue)
        {
            resetItem.Status = "Pending";
            resetItem.IsRunning = false;
            resetItem.IsDone = false;
            resetItem.IsFailed = false;
        }

        IsQueueRunning = true;
        _queueCts = new CancellationTokenSource();

        int totalItems = MeasurementQueue.Count(i => i.Type == QueueItemType.Preset);
        int executedPresets = 0;
        int failedPresets = 0;

        LogService.Instance.Session($"Experiment queue started ({MeasurementQueue.Count} items)");
        LogService.Instance.Info(string.Join(" -> ", MeasurementQueue.Select(i => i.DisplayName)));

        try
        {
            foreach (var item in MeasurementQueue.ToList())
            {
                _queueCts.Token.ThrowIfCancellationRequested();

                item.IsRunning = true;

                if (item.Type == QueueItemType.Pause)
                {
                    await RunQueuePauseAsync(item);
                    item.Status = "Done";
                }
                else
                {
                    var result = await RunQueuePresetAsync(item);
                    if (result)
                    {
                        failedPresets++;
                        item.IsFailed = true;
                        item.Status = "Failed";
                        continue;
                    }
                    executedPresets++;
                    item.IsDone = true;
                    item.Status = item.Repetitions > 1 ? $"Done ({item.Repetitions}×)" : "Done";
                }
            }

            QueueStatusText = failedPresets > 0
                ? $"Queue finished: {executedPresets}/{totalItems} programs ok, {failedPresets} failed."
                : $"Queue finished: all {executedPresets} program(s) executed successfully.";
            LogService.Instance.Info(QueueStatusText);
        }
        catch (OperationCanceledException)
        {
            QueueStatusText = "Queue stopped by user.";
            foreach (var item in MeasurementQueue.Where(i => !i.IsDone && !i.IsFailed))
            {
                item.IsRunning = false;
                if (item.Type == QueueItemType.Pause || !item.IsFailed) item.Status = "Stopped";
            }
            LogService.Instance.Warning("Experiment queue stopped by user.");
        }
        finally
        {
            IsQueueRunning = false;
            _queueCts?.Dispose();
            _queueCts = null;
        }
    }

    private async Task RunQueuePauseAsync(QueueItemViewModel item)
    {
        var token = _queueCts!.Token;
        QueueStatusText = $"Pausing {QueueItemViewModel.FormatDuration(item.PauseSeconds)} ...";
        LogService.Instance.Info($"Queue pause started ({item.DisplayName}).");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(item.PauseSeconds), token);
        }
        catch (OperationCanceledException)
        {
            item.Status = "Stopped";
            throw;
        }

        LogService.Instance.Info("Queue pause finished.");
    }

    private async Task<bool> RunQueuePresetAsync(QueueItemViewModel item)
    {
        var preset = item.Preset!;
        var token = _queueCts!.Token;

        for (int rep = 1; rep <= Math.Max(1, item.Repetitions); rep++)
        {
            token.ThrowIfCancellationRequested();

            item.Status = item.Repetitions > 1 ? $"Running ({rep}/{item.Repetitions})" : "Running";
            QueueStatusText = $"Executing '{preset.Name}'{(item.Repetitions > 1 ? $" ({rep}/{item.Repetitions})" : string.Empty)} ...";

            // Applying the preset switches the plan and loads its parameters
            // exactly like a manual selection would.
            ApplyQueuePreset(preset);

            ErrorMessage = string.Empty;
            try
            {
                await RunMeasurementAsync();
            }
            catch (OperationCanceledException)
            {
                item.Status = "Stopped";
                throw;
            }

            // Single measurements report failures through ErrorMessage instead of throwing.
            if (!string.IsNullOrWhiteSpace(ErrorMessage))
            {
                LogService.Instance.Warning($"Queue program '{preset.Name}' failed: {ErrorMessage}");
                return true;
            }
        }

        return false;
    }

    private void ApplyQueuePreset(MeasurementPreset preset)
    {
        var match = AvailablePresets.FirstOrDefault(p => p.Name == preset.Name);
        SelectedPreset = match ?? preset;
    }
}
