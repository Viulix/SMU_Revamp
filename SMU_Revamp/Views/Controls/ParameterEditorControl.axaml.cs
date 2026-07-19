using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SMU_Revamp.ViewModels;
using SMU_Revamp.Services;
using SMU_Revamp.Models;

namespace SMU_Revamp.Views.Controls;

public partial class ParameterEditorControl : UserControl
{
    public ParameterEditorControl()
    {
        InitializeComponent();
    }

    private void ParameterGrid_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control)
        {
            var point = e.GetCurrentPoint(control);
            if (point.Properties.IsRightButtonPressed)
            {
                ManageParameterLink_Click(sender, e);
                e.Handled = true;
            }
        }
    }

    private async void ManageParameterLink_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control && control.DataContext is MeasurementParameter parameter)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedPlan != null)
            {
                var dialog = new ParameterLinkDialog
                {
                    DataContext = new ParameterLinkViewModel(parameter, vm.SelectedPlan.Parameters)
                };

                var parentWindow = TopLevel.GetTopLevel(this) as Window;
                if (parentWindow != null)
                {
                    var result = await dialog.ShowDialog<bool>(parentWindow);
                    
                    if (result)
                    {
                        var linkVm = (ParameterLinkViewModel)dialog.DataContext;
                        
                        if (linkVm.SelectedTargetParameter != null)
                        {
                            parameter.LinkedParameter = linkVm.SelectedTargetParameter;
                            parameter.LinkedMultiplier = linkVm.Multiplier;
                            parameter.IsLinked = true;
                        }
                        else
                        {
                            parameter.LinkedParameter = null;
                            parameter.IsLinked = false;
                        }

                        // Save the link in config
                        var config = ConfigurationService.Instance.GetConfig();
                        if (!config.ParameterLinks.ContainsKey(vm.SelectedPlan.Name))
                        {
                            config.ParameterLinks[vm.SelectedPlan.Name] = new System.Collections.Generic.Dictionary<string, ParameterLinkConfig>();
                        }

                        if (parameter.IsLinked && parameter.LinkedParameter != null)
                        {
                            config.ParameterLinks[vm.SelectedPlan.Name][parameter.Name] = new ParameterLinkConfig
                            {
                                LinkedParameterName = parameter.LinkedParameter.Name,
                                Multiplier = parameter.LinkedMultiplier,
                                IsActive = true
                            };
                        }
                        else
                        {
                            config.ParameterLinks[vm.SelectedPlan.Name].Remove(parameter.Name);
                        }

                        await ConfigurationService.Instance.SaveAsync(config);
                    }
                }
            }
        }
    }

    private async void ToggleParameterLink_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control && control.DataContext is MeasurementParameter parameter)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedPlan != null)
            {
                var config = ConfigurationService.Instance.GetConfig();
                if (config.ParameterLinks != null && 
                    config.ParameterLinks.TryGetValue(vm.SelectedPlan.Name, out var planLinks) &&
                    planLinks.TryGetValue(parameter.Name, out var linkConfig))
                {
                    linkConfig.IsActive = !linkConfig.IsActive;

                    if (linkConfig.IsActive)
                    {
                        var targetParam = vm.SelectedPlan.Parameters.FirstOrDefault(p => p.Name == linkConfig.LinkedParameterName);
                        if (targetParam != null)
                        {
                            parameter.LinkedParameter = targetParam;
                            parameter.LinkedMultiplier = linkConfig.Multiplier;
                            parameter.IsLinked = true;
                        }
                    }
                    else
                    {
                        parameter.LinkedParameter = null;
                        parameter.IsLinked = false;
                    }

                    await ConfigurationService.Instance.SaveAsync(config);
                }
                else
                {
                    // No link configured, open the dialog instead
                    ManageParameterLink_Click(sender, e);
                }
            }
        }
    }
}
