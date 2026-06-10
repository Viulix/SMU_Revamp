using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.Linq;
using Avalonia.Markup.Xaml;
using SMU_Revamp.ViewModels;
using SMU_Revamp.Views;
using SMU_Revamp.Services;

namespace SMU_Revamp;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        // Load configuration asynchronously after UI is initialized
        _ = LoadConfigurationAsync();

        base.OnFrameworkInitializationCompleted();
    }

    private async System.Threading.Tasks.Task LoadConfigurationAsync()
    {
        try
        {
            var configService = ConfigurationService.Instance;
            await configService.LoadAsync();
            var config = configService.GetConfig();

            // Initialize services with loaded configuration
            ProberService.Instance.ResourceString = config.ProberResource;
            ProberService.Instance.QuietMode = config.ProberQuietMode;
            SwitchMatrixService.Instance.SetTimeout(config.SwitchMatrixTimeoutMs);

            // Trigger loading defaults for the initial selected plan once config is loaded
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow?.DataContext is MainWindowViewModel vm)
            {
                vm.SelectedPlan?.LoadDefaults();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Configuration loading error: {ex.Message}");
            // Continue with defaults if config fails to load
        }
    }
}