using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Win32_to_IntuneUI.Services;
using Win32_to_IntuneUI.Views;

namespace Win32_to_IntuneUI.ViewModels;

/// <summary>
/// Main window ViewModel that orchestrates the specialized ViewModels for each feature area.
/// This keeps each ViewModel focused and maintainable.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>
    /// ViewModel for single package creation functionality
    /// </summary>
    public SinglePackageViewModel SinglePackage { get; }

    /// <summary>
    /// ViewModel for batch processing functionality
    /// </summary>
    public BatchProcessingViewModel BatchProcessing { get; }

    /// <summary>
    /// ViewModel for Intune upload functionality
    /// </summary>
    public IntuneUploadViewModel IntuneUpload { get; }

    /// <summary>
    /// ViewModel for settings and log viewer
    /// </summary>
    public SettingsViewModel Settings { get; }

    /// <summary>
    /// Current application version
    /// </summary>
    public string AppVersion { get; } = GetAppVersion();

    /// <summary>
    /// Update status message
    /// </summary>
    [ObservableProperty]
    private string _updateStatus = string.Empty;

    /// <summary>
    /// Full update error details (for showing in dialog)
    /// </summary>
    [ObservableProperty]
    private string _updateErrorDetails = string.Empty;

    partial void OnUpdateErrorDetailsChanged(string value)
    {
        OnPropertyChanged(nameof(HasUpdateError));
    }

    /// <summary>
    /// Whether there's an error to show
    /// </summary>
    public bool HasUpdateError => !string.IsNullOrEmpty(UpdateErrorDetails);

    /// <summary>
    /// Whether the update check was successful (latest version installed)
    /// </summary>
    [ObservableProperty]
    private bool _updateCheckSuccess;

    /// <summary>
    /// Whether the update check failed
    /// </summary>
    [ObservableProperty]
    private bool _updateCheckFailed;

    /// <summary>
    /// Whether an update is available and ready to install
    /// </summary>
    [ObservableProperty]
    private bool _isUpdateAvailable;

    /// <summary>
    /// Update service instance (set by App.axaml.cs)
    /// </summary>
    public UpdateService? UpdateService { get; set; }

    private Window? _mainWindow;
    public Window? MainWindow
    {
        get => _mainWindow;
        set
        {
            _mainWindow = value;
            // Propagate window reference to child ViewModels
            SinglePackage.MainWindow = value;
            BatchProcessing.MainWindow = value;
            IntuneUpload.MainWindow = value;
            Settings.MainWindow = value;
        }
    }

    public MainWindowViewModel()
    {
        SinglePackage = new SinglePackageViewModel();
        BatchProcessing = new BatchProcessingViewModel();
        IntuneUpload = new IntuneUploadViewModel();
        Settings = new SettingsViewModel();

        // Subscribe to events from child ViewModels
        BatchProcessing.BatchCompleted += (_, candidates) =>
        {
            // Populate upload candidates when batch processing completes
            IntuneUpload.PopulateFromBatchResults(candidates);
        };
    }

    private static string GetAppVersion()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "1.0.0";

        // Remove any +commit hash suffix
        var plusIndex = version.IndexOf('+');
        if (plusIndex > 0)
            version = version[..plusIndex];

        return $"v{version}";
    }

    /// <summary>
    /// Apply the pending update and restart the application
    /// </summary>
    [RelayCommand]
    private void ApplyUpdate()
    {
        UpdateService?.ApplyUpdateAndRestart();
    }

    /// <summary>
    /// Show full update error details in a message box
    /// </summary>
    [RelayCommand]
    private async Task ShowUpdateError()
    {
        if (MainWindow == null || string.IsNullOrEmpty(UpdateErrorDetails)) return;

        var box = new Avalonia.Controls.Window
        {
            Title = "Update Error Details",
            Width = 500,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Avalonia.Controls.TextBox
            {
                Text = UpdateErrorDetails,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(10)
            }
        };
        await box.ShowDialog(MainWindow);
    }

    /// <summary>
    /// Check for updates (works in dev mode for debugging)
    /// </summary>
    [RelayCommand]
    private async Task CheckForUpdates()
    {
        if (MainWindow == null || UpdateService == null) return;

        UpdateStatus = "Checking...";
        UpdateCheckSuccess = false;
        UpdateCheckFailed = false;

        var result = await UpdateService.TestConnectionAsync();
        var isSuccess = result.StartsWith("✓") || result.Contains("Latest:");

        var box = new Avalonia.Controls.Window
        {
            Title = "Update Check",
            Width = 600,
            Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Avalonia.Controls.TextBox
            {
                Text = result,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(10)
            }
        };
        await box.ShowDialog(MainWindow);

        UpdateCheckSuccess = isSuccess;
        UpdateCheckFailed = !isSuccess;
        UpdateStatus = isSuccess ? "Up to date" : "Check failed";
    }

    /// <summary>
    /// Upload packages to Intune - uses recently created package(s) or allows browsing for .intunewin files.
    /// This is the unified upload method used by both Single Package and Batch Processing tabs.
    /// Credentials are shared across all uploads via the IntuneUpload ViewModel.
    /// </summary>
    [RelayCommand]
    private async Task UploadToIntune()
    {
        if (MainWindow == null) return;

        // First, check for recently created package from Single Package tab
        var singlePackagePath = SinglePackage.GetLastCreatedPackagePath();

        // Then check for batch results
        var hasBatchResults = BatchProcessing.BatchCandidates.Any(c =>
            !string.IsNullOrEmpty(c.OutputFilePath) && File.Exists(c.OutputFilePath!));

        if (singlePackagePath != null)
        {
            // Pre-populate with the single package
            IntuneUpload.PopulateFromSinglePackage(singlePackagePath,
                Path.GetFileNameWithoutExtension(singlePackagePath));
            SinglePackage.AppendLog($"Ready to upload: {Path.GetFileName(singlePackagePath)}");
        }
        else if (hasBatchResults)
        {
            // Pre-populate with batch results
            IntuneUpload.PopulateFromBatchResults(BatchProcessing.BatchCandidates);
            BatchProcessing.AppendLog($"{IntuneUpload.UploadCandidates.Count} package(s) ready for upload");
        }
        else
        {
            // No recent packages - let user browse for .intunewin files
            var files = await MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select .intunewin Package(s)",
                AllowMultiple = true,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("Intune Win32 Package") { Patterns = new[] { "*.intunewin" } },
                    FilePickerFileTypes.All
                }
            });

            if (files.Count == 0) return;

            IntuneUpload.PopulateFromFiles(files.Select(f => f.Path.LocalPath).ToList());
        }

        var dialog = new IntuneUploadDialog
        {
            DataContext = IntuneUpload
        };

        await dialog.ShowDialog(MainWindow);
    }
}
