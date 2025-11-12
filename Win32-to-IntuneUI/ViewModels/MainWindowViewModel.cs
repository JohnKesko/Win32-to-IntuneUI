using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Win32_to_IntuneUI.Services;

namespace Win32_to_IntuneUI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _sourceFolder = string.Empty;

    [ObservableProperty]
    private string _setupFile = string.Empty;

    [ObservableProperty]
    private string _outputFolder = string.Empty;

    [ObservableProperty]
    private string _catalogFolder = string.Empty;

    [ObservableProperty]
    private bool _isProcessing = false;

    [ObservableProperty]
    private string _logOutput = string.Empty;

    [ObservableProperty]
    private double _progressValue = 0;

    [ObservableProperty]
    private bool _isProgressVisible = false;

    [ObservableProperty]
    private string _toolStatus = "Checking tool availability...";

    private readonly IntuneToolDownloader _toolDownloader;

    public Window? MainWindow { get; set; }

    public MainWindowViewModel()
    {
        _toolDownloader = new IntuneToolDownloader();
        _toolDownloader.StatusChanged += OnToolDownloaderStatusChanged;
        _toolDownloader.ProgressChanged += OnToolDownloaderProgressChanged;

        // Check tool availability on startup
        _ = InitializeToolAsync();
    }

    private async Task InitializeToolAsync()
    {
        AppendLog("Initializing...");

        var isAvailable = await _toolDownloader.EnsureToolAvailableAsync();

        if (isAvailable)
        {
            ToolStatus = "✓ IntuneWinAppUtil.exe ready";
            AppendLog($"Tool ready at: {_toolDownloader.GetToolPath()}");
        }
        else
        {
            ToolStatus = "✗ Failed to download IntuneWinAppUtil.exe";
            AppendLog("ERROR: Could not download the required tool. Please check your internet connection.");
        }
    }

    private void OnToolDownloaderStatusChanged(object? sender, string status)
    {
        AppendLog(status);
    }

    private void OnToolDownloaderProgressChanged(object? sender, int progress)
    {
        if (!IsProcessing)
        {
            ProgressValue = progress;
            IsProgressVisible = progress > 0 && progress < 100;
        }
    }

    [RelayCommand]
    private async Task BrowseSourceFolder()
    {
        if (MainWindow?.StorageProvider is not { } storageProvider) return;

        var folder = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Source Folder",
            AllowMultiple = false
        });

        if (folder.Count > 0)
        {
            SourceFolder = folder[0].Path.LocalPath;
            AppendLog($"Source folder selected: {SourceFolder}");
        }
    }

    [RelayCommand]
    private async Task BrowseSetupFile()
    {
        if (MainWindow?.StorageProvider is not { } storageProvider) return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Setup File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Executable Files")
                {
                    Patterns = new[] { "*.exe", "*.msi", "*.cmd", "*.bat" }
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        if (files.Count > 0)
        {
            SetupFile = files[0].Path.LocalPath;
            AppendLog($"Setup file selected: {SetupFile}");
        }
    }

    [RelayCommand]
    private async Task BrowseOutputFolder()
    {
        if (MainWindow?.StorageProvider is not { } storageProvider) return;

        var folder = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Output Folder",
            AllowMultiple = false
        });

        if (folder.Count > 0)
        {
            OutputFolder = folder[0].Path.LocalPath;
            AppendLog($"Output folder selected: {OutputFolder}");
        }
    }

    [RelayCommand]
    private async Task BrowseCatalogFolder()
    {
        if (MainWindow?.StorageProvider is not { } storageProvider) return;

        var folder = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Catalog Folder (Optional)",
            AllowMultiple = false
        });

        if (folder.Count > 0)
        {
            CatalogFolder = folder[0].Path.LocalPath;
            AppendLog($"Catalog folder selected: {CatalogFolder}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreatePackage))]
    private async Task CreatePackage()
    {
        var toolPath = _toolDownloader.GetToolPath();

        if (!File.Exists(toolPath))
        {
            AppendLog("ERROR: IntuneWinAppUtil.exe not available. Attempting to download...");
            var downloaded = await _toolDownloader.EnsureToolAvailableAsync();
            if (!downloaded)
            {
                AppendLog("ERROR: Failed to download IntuneWinAppUtil.exe");
                return;
            }
        }

        IsProcessing = true;
        IsProgressVisible = true;
        ProgressValue = 0;
        LogOutput = string.Empty;

        try
        {
            AppendLog($"Starting package creation...");
            AppendLog($"Source folder: {SourceFolder}");
            AppendLog($"Setup file: {SetupFile}");
            AppendLog($"Output folder: {OutputFolder}");
            if (!string.IsNullOrWhiteSpace(CatalogFolder))
            {
                AppendLog($"Catalog folder: {CatalogFolder}");
            }
            AppendLog(new string('-', 80));

            await RunIntuneWinAppUtil(toolPath);

            ProgressValue = 100;
            AppendLog(new string('-', 80));
            AppendLog("Package creation completed!");
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private bool CanCreatePackage()
    {
        return !string.IsNullOrWhiteSpace(SourceFolder) &&
               !string.IsNullOrWhiteSpace(SetupFile) &&
               !string.IsNullOrWhiteSpace(OutputFolder) &&
               !IsProcessing;
    }

    private async Task RunIntuneWinAppUtil(string toolPath)
    {
        // The IntuneWinAppUtil.exe requires .NET Framework 4.7.2 and only runs on Windows
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The Microsoft Win32 Content Prep Tool (IntuneWinAppUtil.exe) only runs on Windows. " +
                "This tool requires .NET Framework 4.7.2 which is Windows-only.");
        }

        var arguments = $"-c \"{SourceFolder}\" -s \"{SetupFile}\" -o \"{OutputFolder}\"";

        if (!string.IsNullOrWhiteSpace(CatalogFolder))
        {
            arguments += $" -a \"{CatalogFolder}\"";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = toolPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                AppendLog(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                AppendLog($"ERROR: {e.Data}");
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"IntuneWinAppUtil exited with code {process.ExitCode}");
        }
    }

    private void AppendLog(string message)
    {
        LogOutput += $"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}";
    }

    partial void OnSourceFolderChanged(string value) => CreatePackageCommand.NotifyCanExecuteChanged();
    partial void OnSetupFileChanged(string value) => CreatePackageCommand.NotifyCanExecuteChanged();
    partial void OnOutputFolderChanged(string value) => CreatePackageCommand.NotifyCanExecuteChanged();
    partial void OnIsProcessingChanged(bool value) => CreatePackageCommand.NotifyCanExecuteChanged();
}