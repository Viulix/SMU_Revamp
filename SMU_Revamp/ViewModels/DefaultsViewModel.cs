using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SMU_Revamp.Models;
using SMU_Revamp.Services;

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

        public DefaultsViewModel()
        {
            // Fresh instances of plans so the user modifies new defaults rather than active values
            MeasurementPlans = new List<IMeasurementPlan>
            {
                new MeasurePointMeasurementPlan(),
                new USweepMeasurementPlan(),
                new PulseSpotMeasurementPlan(),
                new PulseSweepMeasurementPlan(),
                new PotDepMeasurementPlan(),
                new SpikeTimingMeasurementPlan(),
                new MemristorSweepMeasurementPlan()
            };
            _selectedPlan = MeasurementPlans[0];

            SaveDefaultsCommand = new AsyncRelayCommand(SaveDefaultsAsync);
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
                StatusMessage = "Default values successfully saved!";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving defaults: {ex.Message}";
            }
        }
    }
}
