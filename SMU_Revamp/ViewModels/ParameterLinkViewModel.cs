using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SMU_Revamp.Models;

namespace SMU_Revamp.ViewModels
{
    public partial class ParameterLinkViewModel : ObservableObject
    {
        public MeasurementParameter SourceParameter { get; }
        public List<MeasurementParameter> AvailableParameters { get; }

        [ObservableProperty]
        private MeasurementParameter? _selectedTargetParameter;

        [ObservableProperty]
        private double _multiplier = 1.0;

        public ParameterLinkViewModel(MeasurementParameter source, IEnumerable<MeasurementParameter> allParameters)
        {
            SourceParameter = source;
            AvailableParameters = allParameters.Where(p => p != source && p.Type == ParameterType.Number).ToList();
            
            if (source.LinkedParameter != null)
            {
                SelectedTargetParameter = AvailableParameters.FirstOrDefault(p => p.Name == source.LinkedParameter.Name);
                Multiplier = source.LinkedMultiplier;
            }
        }
    }
}
