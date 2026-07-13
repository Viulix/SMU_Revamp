using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SMU_Revamp.ViewModels;
using SMU_Revamp.Services;

namespace SMU_Revamp.Views;

public partial class MeasurementsTabView : UserControl
{
    public MeasurementsTabView()
    {
        InitializeComponent();
    }

    private void MeasurementScrollViewer_ScrollChanged(object? sender, Avalonia.Controls.ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            var indicator = this.FindControl<Border>("MeasurementScrollIndicator");
            if (indicator != null)
            {
                // Show indicator if there is still content to scroll down to
                bool canScrollDown = sv.Extent.Height > sv.Viewport.Height && 
                                     sv.Offset.Y < (sv.Extent.Height - sv.Viewport.Height) - 1.0;
                indicator.Opacity = canScrollDown ? 1.0 : 0.0;
            }
        }
    }

    private void ParameterGrid_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
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

    private async void ManageParameterLink_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control control && control.DataContext is Models.MeasurementParameter parameter)
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
                            config.ParameterLinks[vm.SelectedPlan.Name] = new System.Collections.Generic.Dictionary<string, Models.ParameterLinkConfig>();
                        }

                        if (parameter.IsLinked && parameter.LinkedParameter != null)
                        {
                            config.ParameterLinks[vm.SelectedPlan.Name][parameter.Name] = new Models.ParameterLinkConfig
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

    private async void ToggleParameterLink_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control control && control.DataContext is Models.MeasurementParameter parameter)
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

    private async void ExportCsvButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Plot Data to CSV",
                DefaultExtension = "csv",
                SuggestedFileName = "measurement_data.csv",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("CSV Files (*.csv)") { Patterns = new[] { "*.csv" } }
                }
            });

            if (file != null)
            {
                var path = file.Path.LocalPath;
                await vm.SaveCurvePointsToCsvAsync(path);
            }
        }
    }
}
