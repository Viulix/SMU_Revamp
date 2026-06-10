using Avalonia.Controls;
using Avalonia.Controls.Templates;
using SMU_Revamp.Models;

namespace SMU_Revamp.Views
{
    public class ParameterTemplateSelector : IDataTemplate
    {
        public IDataTemplate? TextTemplate { get; set; }
        public IDataTemplate? CheckboxTemplate { get; set; }
        public IDataTemplate? DropdownTemplate { get; set; }

        public Control? Build(object? param)
        {
            if (param is MeasurementParameter mp)
            {
                if (mp.IsTextOrNumber && TextTemplate != null)
                {
                    return TextTemplate.Build(param);
                }
                if (mp.IsCheckbox && CheckboxTemplate != null)
                {
                    return CheckboxTemplate.Build(param);
                }
                if (mp.IsDropdown && DropdownTemplate != null)
                {
                    return DropdownTemplate.Build(param);
                }
            }
            return null;
        }

        public bool Match(object? data)
        {
            return data is MeasurementParameter;
        }
    }
}
