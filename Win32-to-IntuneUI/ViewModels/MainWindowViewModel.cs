using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Win32_to_IntuneUI.Models;
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

    [ObservableProperty]
    private string _batchParentFolder = string.Empty;

    [ObservableProperty]
    private string _batchOutputFolder = string.Empty;

    [ObservableProperty]
    private string _batchStatusText = "Scan folders to begin batch processing";

    [ObservableProperty]
    private ObservableCollection<AppPackageCandidate> _batchCandidates = new();

    private readonly IntuneToolDownloader _toolDownloader;
    private static readonly string[] InstallerExtensions = { ".msi", ".exe", ".cmd", ".bat" };

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

        // Remove trailing backslashes from folder paths to avoid issues with quoted arguments
        var sourceFolder = SourceFolder.TrimEnd('\\', '/');
        var outputFolder = OutputFolder.TrimEnd('\\', '/');

        var arguments = $"-c \"{sourceFolder}\" -s \"{SetupFile}\" -o \"{outputFolder}\"";

        if (!string.IsNullOrWhiteSpace(CatalogFolder))
        {
            var catalogFolder = CatalogFolder.TrimEnd('\\', '/');
            arguments += $" -a \"{catalogFolder}\"";
        }

        AppendLog($"Executing command:");
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

    private void AppendLog(string message)
    {
        LogOutput += $"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}";
    }

    // Batch Processing Methods

    [RelayCommand]
    private async Task BrowseBatchFolder()
    {
        if (MainWindow?.StorageProvider is not { } storageProvider) return;

        var folder = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Parent Folder Containing App Subfolders",
            AllowMultiple = false
        });

        if (folder.Count > 0)
        {
            BatchParentFolder = folder[0].Path.LocalPath;
            AppendLog($"Batch parent folder selected: {BatchParentFolder}");
            BatchStatusText = "Click 'Scan Folders' to detect applications";
        }
    }

    [RelayCommand]
    private async Task BrowseBatchOutputFolder()
    {
        if (MainWindow?.StorageProvider is not { } storageProvider) return;

        var folder = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Output Folder for Batch Packages",
            AllowMultiple = false
        });

        if (folder.Count > 0)
        {
            BatchOutputFolder = folder[0].Path.LocalPath;
            AppendLog($"Batch output folder selected: {BatchOutputFolder}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanScanBatchFolders))]
    private async Task ScanBatchFolders()
    {
        if (string.IsNullOrWhiteSpace(BatchParentFolder) || !Directory.Exists(BatchParentFolder))
        {
            AppendLog("ERROR: Please select a valid parent folder");
            return;
        }

        IsProcessing = true;
        BatchCandidates.Clear();
        LogOutput = string.Empty;
        AppendLog("Scanning for applications...");
        AppendLog(new string('-', 80));

        try
        {
            var subfolders = Directory.GetDirectories(BatchParentFolder);
            AppendLog($"Found {subfolders.Length} subfolder(s)");
            AppendLog("");

            foreach (var subfolder in subfolders)
            {
                var folderName = Path.GetFileName(subfolder);
                AppendLog($"Scanning: {folderName}");

                var installers = Directory.GetFiles(subfolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => InstallerExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                var candidate = new AppPackageCandidate
                {
                    FolderName = folderName,
                    FolderPath = subfolder,
                    DetectedInstallers = installers
                };

                if (installers.Count == 0)
                {
                    candidate.Status = PackageStatus.NeedsAttention;
                    candidate.ErrorMessage = "No installer files found";
                    AppendLog($"  [!] No installer found");
                }
                else if (installers.Count == 1)
                {
                    candidate.SelectedInstaller = installers[0];
                    candidate.Status = PackageStatus.Ready;
                    AppendLog($"  [OK] Auto-selected: {Path.GetFileName(installers[0])}");
                }
                else
                {
                    // Multiple installers - try to auto-select based on priority
                    var autoSelected = TryAutoSelectInstaller(installers);
                    if (autoSelected != null)
                    {
                        candidate.SelectedInstaller = autoSelected;
                        candidate.Status = PackageStatus.Ready;
                        AppendLog($"  [OK] Auto-selected: {Path.GetFileName(autoSelected)} (from {installers.Count} installers)");
                    }
                    else
                    {
                        candidate.Status = PackageStatus.NeedsAttention;
                        candidate.ErrorMessage = $"Multiple installers found ({installers.Count})";
                        AppendLog($"  [!] Multiple installers found ({installers.Count}) - needs user selection");
                    }
                }

                BatchCandidates.Add(candidate);
            }

            AppendLog("");
            AppendLog(new string('-', 80));
            var readyCount = BatchCandidates.Count(c => c.Status == PackageStatus.Ready);
            var needsAttentionCount = BatchCandidates.Count(c => c.Status == PackageStatus.NeedsAttention);

            AppendLog($"Scan complete:");
            AppendLog($"  Ready to process: {readyCount}");
            AppendLog($"  Needs attention: {needsAttentionCount}");

            BatchStatusText = $"{readyCount} ready, {needsAttentionCount} need attention";
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            BatchStatusText = "Scan failed";
        }
        finally
        {
            IsProcessing = false;
            ScanBatchFoldersCommand.NotifyCanExecuteChanged();
            ProcessBatchCommand.NotifyCanExecuteChanged();
        }
    }

    private string? TryAutoSelectInstaller(List<string> installers)
    {
        // Priority 1: .msi files
        var msiFiles = installers.Where(f => f.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)).ToList();
        if (msiFiles.Count == 1) return msiFiles[0];

        // Priority 2: Files with setup/install/installer in the name
        var setupFiles = installers.Where(f =>
        {
            var name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
            return name.Contains("setup") || name.Contains("install");
        }).ToList();
        if (setupFiles.Count == 1) return setupFiles[0];

        // Priority 3: Largest file
        if (installers.Count > 0)
        {
            return installers.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
        }

        return null;
    }

    private bool CanScanBatchFolders()
    {
        return !string.IsNullOrWhiteSpace(BatchParentFolder) && !IsProcessing;
    }

    [RelayCommand(CanExecute = nameof(CanProcessBatch))]
    private async Task ProcessBatch()
    {
        if (string.IsNullOrWhiteSpace(BatchOutputFolder) || !Directory.Exists(BatchOutputFolder))
        {
            // Create output folder if it doesn't exist
            if (!string.IsNullOrWhiteSpace(BatchOutputFolder))
            {
                Directory.CreateDirectory(BatchOutputFolder);
            }
            else
            {
                AppendLog("ERROR: Please select an output folder");
                return;
            }
        }

        var toolPath = _toolDownloader.GetToolPath();
        if (!File.Exists(toolPath))
        {
            AppendLog("ERROR: IntuneWinAppUtil.exe not available");
            return;
        }

        IsProcessing = true;
        LogOutput = string.Empty;
        AppendLog("Starting batch processing...");
        AppendLog(new string('-', 80));

        var processableCandidates = BatchCandidates.Where(c => c.Status == PackageStatus.Ready).ToList();
        var totalCount = processableCandidates.Count;
        var successCount = 0;
        var failedCount = 0;

        foreach (var candidate in processableCandidates)
        {
            try
            {
                candidate.Status = PackageStatus.Processing;
                AppendLog("");
                AppendLog($"Processing: {candidate.FolderName}");
                AppendLog($"  Installer: {Path.GetFileName(candidate.SelectedInstaller)}");

                await ProcessSingleBatchApp(toolPath, candidate);

                candidate.Status = PackageStatus.Success;
                successCount++;
                AppendLog($"  [SUCCESS] Package created for {candidate.FolderName}");
            }
            catch (Exception ex)
            {
                candidate.Status = PackageStatus.Failed;
                candidate.ErrorMessage = ex.Message;
                failedCount++;
                AppendLog($"  [FAILED] {candidate.FolderName}: {ex.Message}");
            }
        }

        IsProcessing = false;
        AppendLog("");
        AppendLog(new string('-', 80));
        AppendLog($"Batch processing complete:");
        AppendLog($"  Success: {successCount}/{totalCount}");
        AppendLog($"  Failed: {failedCount}/{totalCount}");

        BatchStatusText = $"Complete: {successCount} success, {failedCount} failed";

        // TODO: Show batch results dialog here
        ProcessBatchCommand.NotifyCanExecuteChanged();
    }

    private async Task ProcessSingleBatchApp(string toolPath, AppPackageCandidate candidate)
    {
        var sourceFolder = candidate.FolderPath.TrimEnd('\\', '/');
        var outputFolder = BatchOutputFolder.TrimEnd('\\', '/');
        var arguments = $"-c \"{sourceFolder}\" -s \"{candidate.SelectedInstaller}\" -o \"{outputFolder}\"";

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
                AppendLog($"    {e.Data}");
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                AppendLog($"    ERROR: {e.Data}");
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

        // Verify output file was created
        var setupFileName = Path.GetFileNameWithoutExtension(candidate.SelectedInstaller);
        var expectedOutputFile = Path.Combine(BatchOutputFolder, $"{setupFileName}.intunewin");

        if (File.Exists(expectedOutputFile))
        {
            candidate.OutputFilePath = expectedOutputFile;
        }
    }

    private bool CanProcessBatch()
    {
        return !IsProcessing &&
               BatchCandidates.Any(c => c.Status == PackageStatus.Ready) &&
               !string.IsNullOrWhiteSpace(BatchOutputFolder);
    }

    partial void OnSourceFolderChanged(string value) => CreatePackageCommand.NotifyCanExecuteChanged();
    partial void OnSetupFileChanged(string value) => CreatePackageCommand.NotifyCanExecuteChanged();
    partial void OnOutputFolderChanged(string value) => CreatePackageCommand.NotifyCanExecuteChanged();
    partial void OnIsProcessingChanged(bool value)
    {
        CreatePackageCommand.NotifyCanExecuteChanged();
        ScanBatchFoldersCommand.NotifyCanExecuteChanged();
        ProcessBatchCommand.NotifyCanExecuteChanged();
    }
    partial void OnBatchParentFolderChanged(string value) => ScanBatchFoldersCommand.NotifyCanExecuteChanged();
    partial void OnBatchOutputFolderChanged(string value) => ProcessBatchCommand.NotifyCanExecuteChanged();
}
