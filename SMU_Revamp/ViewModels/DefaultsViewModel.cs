using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SMU_Revamp.Models;
using SMU_Revamp.Services;
using SMU_Revamp.MeasurementPlans;
using SMU_Revamp.Interfaces;

namespace SMU_Revamp.ViewModels
{
    /// <summary>
    /// ViewModel for the DefaultsWindow dialog.
    /// Manages editing default plan parameter values persistently.
    /// </summary>
    public partial class DefaultsViewModel : ViewModelBase
    {
        public List<IMeasurementPlan> MeasurementPlans { get; }

        private IMeasurementPlan _selectedPlan;
        public IMeasurementPlan SelectedPlan
        {
            get => _selectedPlan;
            set
            {
                if (SetProperty(ref _selectedPlan, value))
                {
                    OnPropertyChanged(nameof(SelectedPlan));
                }
            }
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand SaveDefaultsCommand { get; }
        public ICommand DiscardChangesCommand { get; }

        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set => SetProperty(ref _hasUnsavedChanges, value);
        }

        private Dictionary<string, object> _originalValuesObj = new();

        public DefaultsViewModel()
        {
            // Fresh instances of plans so the user modifies new defaults rather than active values
            MeasurementPlans = MeasurementPlanLoader.LoadPlans();
            _selectedPlan = MeasurementPlans.Count > 0 ? MeasurementPlans[0] : null!;

            SaveDefaultsCommand = new AsyncRelayCommand(SaveDefaultsAsync);
            DiscardChangesCommand = new RelayCommand(DiscardChanges);

            CaptureOriginalValues();

            foreach (var plan in MeasurementPlans)
            {
                foreach (var param in plan.Parameters)
                {
                    param.PropertyChanged += (s, e) => 
                    {
                        if (e.PropertyName == nameof(MeasurementParameter.Value))
                        {
                            CheckForUnsavedChanges();
                        }
                    };
                }
            }
        }

        private void CaptureOriginalValues()
        {
            _originalValuesObj.Clear();
            foreach (var plan in MeasurementPlans)
            {
                foreach (var param in plan.Parameters)
                {
                    _originalValuesObj[$"{plan.Name}_{param.Name}"] = param.Value;
                }
            }
            HasUnsavedChanges = false;
        }

        private void CheckForUnsavedChanges()
        {
            bool hasChanges = false;
            foreach (var plan in MeasurementPlans)
            {
                foreach (var param in plan.Parameters)
                {
                    if (_originalValuesObj.TryGetValue($"{plan.Name}_{param.Name}", out object originalObj))
                    {
                        var origStr = originalObj?.ToString() ?? "";
                        var currStr = param.GetValueAsString();
                        if (origStr != currStr)
                        {
                            hasChanges = true;
                            break;
                        }
                    }
                }
                if (hasChanges) break;
            }
            HasUnsavedChanges = hasChanges;
        }

        private void DiscardChanges()
        {
            foreach (var plan in MeasurementPlans)
            {
                foreach (var param in plan.Parameters)
                {
                    if (_originalValuesObj.TryGetValue($"{plan.Name}_{param.Name}", out object originalObj))
                    {
                        param.Value = originalObj;
                    }
                }
            }
            HasUnsavedChanges = false;
            StatusMessage = "Changes discarded.";
        }

        private async Task SaveDefaultsAsync()
        {
            try
            {
                var config = ConfigurationService.Instance.GetConfig();
                if (config.DefaultPlanParameters == null)
                {
                    config.DefaultPlanParameters = new();
                }

                foreach (var plan in MeasurementPlans)
                {
                    if (!config.DefaultPlanParameters.TryGetValue(plan.Name, out var planParams))
                    {
                        planParams = new();
                        config.DefaultPlanParameters[plan.Name] = planParams;
                    }

                    foreach (var param in plan.Parameters)
                    {
                        planParams[param.Name] = param.GetValueAsString();
                    }
                }

                await ConfigurationService.Instance.SaveAsync(config);
                CaptureOriginalValues();
                StatusMessage = "Default values successfully saved!";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving defaults: {ex.Message}";
            }
        }
    }
}
