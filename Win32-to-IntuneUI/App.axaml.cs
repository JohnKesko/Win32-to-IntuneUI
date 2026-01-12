using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using Win32_to_IntuneUI.Services;
using Win32_to_IntuneUI.ViewModels;
using Win32_to_IntuneUI.Views;

namespace Win32_to_IntuneUI;

public partial class App : Application
{
    private readonly UpdateService _updateService = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            var viewModel = new MainWindowViewModel();
            viewModel.UpdateService = _updateService;

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            // Check for updates at startup
            _updateService.Initialize();
            desktop.MainWindow.Opened += async (_, _) => await CheckForUpdatesAsync(viewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task CheckForUpdatesAsync(MainWindowViewModel viewModel)
    {
        var hasUpdate = await _updateService.CheckAndDownloadAsync(status =>
        {
            viewModel.UpdateStatus = status;
        });

        if (hasUpdate)
        {
            viewModel.UpdateStatus = $"Update v{_updateService.PendingVersion} ready - restart to apply";
            viewModel.IsUpdateAvailable = true;
        }
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}