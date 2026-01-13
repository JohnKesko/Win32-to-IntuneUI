using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Win32_to_IntuneUI.Services;

namespace Win32_to_IntuneUI.ViewModels;

public partial class SinglePackageViewModel : ViewModelBase
{
    [ObservableProperty] private string _sourceFolder = string.Empty;
    [ObservableProperty] private string _setupFile = string.Empty;
    [ObservableProperty] private string _outputFolder = string.Empty;
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string _logOutput = string.Empty;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isProgressVisible;
    [ObservableProperty] private bool _isPackaging; // True while packaging is in progress (for indeterminate progress bar)
    [ObservableProperty] private string _toolStatus = "Checking tool availability...";

    private readonly IntuneToolDownloader _toolDownloader;

    public Window? MainWindow { get; set; }

    /// <summary>
    /// Event raised when a package is created successfully
    /// </summary>
    public event EventHandler<string>? PackageCreated;

    public SinglePackageViewModel()
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

        // Check if running on non-Windows platform
        if (!OperatingSystem.IsWindows())
        {
            ToolStatus = "ℹ UI Preview Mode (Windows required for packaging)";
            AppendLog("Running on macOS/Linux - UI preview mode only");
            AppendLog("Package creation requires Windows and IntuneWinAppUtil.exe");
            return;
        }

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
            FileTypeFilter =
            [
                new FilePickerFileType("Executable Files")
                {
                    Patterns = ["*.exe", "*.msi", "*.cmd", "*.bat"]
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = ["*.*"]
                }
            ]
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

    [RelayCommand(CanExecute = nameof(CanCreatePackage))]
    private async Task CreatePackage()
    {
        IsProcessing = true;
        IsProgressVisible = true;
        IsPackaging = true;
        ProgressValue = 0;
        LogOutput = string.Empty;

        try
        {
            AppendLog("Starting package creation...");
            AppendLog($"Source folder: {SourceFolder}");
            AppendLog($"Setup file: {SetupFile}");
            AppendLog($"Output folder: {OutputFolder}");
            AppendLog(new string('-', 80));

            // Check if running on non-Windows platform - show preview mode
            if (!OperatingSystem.IsWindows())
            {
                AppendLog("⚠ UI Preview Mode - Package creation requires Windows");
                AppendLog("Simulating package creation for UI preview...");

                // Simulate progress for UI preview (2 seconds)
                await Task.Delay(2000);

                IsPackaging = false;
                ProgressValue = 100;
                AppendLog(new string('-', 80));
                AppendLog("Preview complete - actual packaging requires Windows");
                IsProcessing = false;
                return;
            }

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

            await RunIntuneWinAppUtil(toolPath);

            ProgressValue = 100;
            IsPackaging = false;
            AppendLog(new string('-', 80));
            AppendLog("Package creation completed!");

            // Notify that package was created
            var setupFileName = Path.GetFileNameWithoutExtension(SetupFile);
            var outputFile = Path.Combine(OutputFolder, $"{setupFileName}.intunewin");
            if (File.Exists(outputFile))
            {
                PackageCreated?.Invoke(this, outputFile);
            }
        }
        catch (Exception ex)
        {
            IsPackaging = false;
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

    /// <summary>
    /// Gets the path to the last created package (if it exists)
    /// </summary>
    public string? GetLastCreatedPackagePath()
    {
        if (string.IsNullOrWhiteSpace(SetupFile) || string.IsNullOrWhiteSpace(OutputFolder))
            return null;

        var setupFileName = Path.GetFileNameWithoutExtension(SetupFile);
        var expectedOutputFile = Path.Combine(OutputFolder, $"{setupFileName}.intunewin");

        return File.Exists(expectedOutputFile) ? expectedOutputFile : null;
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

        // Remove trailing backslashes from folder paths to avoid issues with quoted arguments
        var sourceFolder = SourceFolder.TrimEnd('\\', '/');
        var outputFolder = OutputFolder.TrimEnd('\\', '/');

        var arguments = $"-c \"{sourceFolder}\" -s \"{SetupFile}\" -o \"{outputFolder}\" -q";

        AppendLog("Executing command:");
        AppendLog($"{toolPath} {arguments}");
        AppendLog("");

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

        AppendLog("");
        AppendLog($"Process exited with code: {process.ExitCode}");

        if (process.ExitCode != 0)
        {
            throw new Exception($"IntuneWinAppUtil exited with code {process.ExitCode}");
        }

        // Check if the .intunewin file was created
        var setupFileName = Path.GetFileNameWithoutExtension(SetupFile);
        var expectedOutputFile = Path.Combine(OutputFolder, $"{setupFileName}.intunewin");

        if (File.Exists(expectedOutputFile))
        {
            var fileInfo = new FileInfo(expectedOutputFile);
            AppendLog($"✓ Successfully created: {expectedOutputFile}");
            AppendLog($"  File size: {fileInfo.Length:N0} bytes");
        }
        else
        {
            AppendLog($"⚠ Warning: Expected output file not found: {expectedOutputFile}");
        }
    }

    public void AppendLog(string message)
    {
        // Route to centralized log service
        AppLogService.Instance.Log("Package", message);
    }

    partial void OnSourceFolderChanged(string value) => CreatePackageCommand.NotifyCanExecuteChanged();
    partial void OnSetupFileChanged(string value) => CreatePackageCommand.NotifyCanExecuteChanged();
    partial void OnOutputFolderChanged(string value) => CreatePackageCommand.NotifyCanExecuteChanged();
    partial void OnIsProcessingChanged(bool value) => CreatePackageCommand.NotifyCanExecuteChanged();
}
