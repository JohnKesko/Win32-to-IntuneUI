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
    [ObservableProperty] private string _sourceFolder = string.Empty;
    [ObservableProperty] private string _setupFile = string.Empty;
    [ObservableProperty] private string _outputFolder = string.Empty;
    [ObservableProperty] private string _catalogFolder = string.Empty;
    [ObservableProperty] private string _installFolder = string.Empty;
    [ObservableProperty] private bool _isProcessing = false;
    [ObservableProperty] private string _logOutput = string.Empty;
    [ObservableProperty] private double _progressValue = 0;
    [ObservableProperty] private bool _isProgressVisible = false;
    [ObservableProperty] private bool _isIndeterminate = false;
    [ObservableProperty] private string _toolStatus = "Checking tool availability...";
    [ObservableProperty] private string _batchParentFolder = string.Empty;
    [ObservableProperty] private string _batchOutputFolder = string.Empty;
    [ObservableProperty] private string _batchStatusText = "Scan folders to begin batch processing";
    [ObservableProperty] private ObservableCollection<AppPackageCandidate> _batchCandidates = new();
    [ObservableProperty] private bool _hasBatchCandidates = false;

    public int ReadyCount => BatchCandidates.Count(c => c.Status == PackageStatus.Ready);
    public int NeedsAttentionCount => BatchCandidates.Count(c => c.Status == PackageStatus.NeedsAttention);
    public int SkippedCount => BatchCandidates.Count(c => c.Status == PackageStatus.Skipped);

    // Intune Upload Properties
    [ObservableProperty] private ObservableCollection<IntuneUploadCandidate> _uploadCandidates = [];
    [ObservableProperty] private string _graphAccessToken = string.Empty;
    [ObservableProperty] private string? _graphConnectionStatus;
    [ObservableProperty] private string _graphConnectionStatusColor = "Gray";
    [ObservableProperty] private string _uploadStatusText = string.Empty;
    public string UploadSelectionCount => $"{UploadCandidates.Count(c => c.IsSelected)} of {UploadCandidates.Count} selected";
    private readonly IntuneToolDownloader _toolDownloader;
    private readonly IntuneGraphService _intuneGraphService;
    private static readonly string[] InstallerExtensions = { ".msi", ".exe", ".cmd", ".bat" };

    public Window? MainWindow { get; set; }

    public MainWindowViewModel()
    {
        _toolDownloader = new IntuneToolDownloader();
        _toolDownloader.StatusChanged += OnToolDownloaderStatusChanged;
        _toolDownloader.ProgressChanged += OnToolDownloaderProgressChanged;

        _intuneGraphService = new IntuneGraphService();

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

    [RelayCommand(CanExecute = nameof(CanUploadSinglePackage))]
    private async Task UploadSinglePackage()
    {
        // Check if there's a recently created package in the output folder
        var setupFileName = Path.GetFileNameWithoutExtension(SetupFile);
        var expectedOutputFile = Path.Combine(OutputFolder, $"{setupFileName}.intunewin");

        if (!File.Exists(expectedOutputFile))
        {
            AppendLog("ERROR: No .intunewin package found. Please create a package first.");
            return;
        }

        // Create an upload candidate for the single file
        UploadCandidates.Clear();

        var uploadCandidate = new IntuneUploadCandidate
        {
            DisplayName = setupFileName, // Use installer name as initial display name
            FolderName = Path.GetFileName(SourceFolder),
            PackageFilePath = expectedOutputFile,
            UploadStatus = "Ready",
            IsSelected = true
        };

        UploadCandidates.Add(uploadCandidate);
        UploadStatusText = "1 package ready";

        // Show the upload dialog
        var dialog = new Views.IntuneUploadDialog
        {
            DataContext = this
        };

        if (MainWindow != null)
        {
            await dialog.ShowDialog(MainWindow);
        }
    }

    private bool CanUploadSinglePackage()
    {
        if (string.IsNullOrWhiteSpace(SetupFile) || string.IsNullOrWhiteSpace(OutputFolder))
            return false;

        var setupFileName = Path.GetFileNameWithoutExtension(SetupFile);
        var expectedOutputFile = Path.Combine(OutputFolder, $"{setupFileName}.intunewin");

        return File.Exists(expectedOutputFile) && !IsProcessing;
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

    [RelayCommand]
    private async Task SelectInstallerForCandidate(AppPackageCandidate candidate)
    {
        if (MainWindow?.StorageProvider is not { } storageProvider) return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select Setup File for {candidate.FolderName}",
            AllowMultiple = false,
            SuggestedStartLocation = await storageProvider.TryGetFolderFromPathAsync(candidate.FolderPath),
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
            candidate.SelectedInstaller = files[0].Path.LocalPath;
            candidate.Status = PackageStatus.Ready;
            candidate.ErrorMessage = null;
            AppendLog($"Manually selected installer for {candidate.FolderName}: {Path.GetFileName(candidate.SelectedInstaller)}");

            // Update status counts
            var readyCount = BatchCandidates.Count(c => c.Status == PackageStatus.Ready);
            var needsAttentionCount = BatchCandidates.Count(c => c.Status == PackageStatus.NeedsAttention);
            BatchStatusText = $"{readyCount} ready, {needsAttentionCount} need attention";

            ProcessBatchCommand.NotifyCanExecuteChanged();
            UpdateBatchCounts();
        }
    }

    [RelayCommand]
    private void SkipCandidate(AppPackageCandidate candidate)
    {
        candidate.Status = PackageStatus.Skipped;
        candidate.ErrorMessage = "Skipped by user";
        AppendLog($"Skipped: {candidate.FolderName}");

        // Update status counts
        var readyCount = BatchCandidates.Count(c => c.Status == PackageStatus.Ready);
        var needsAttentionCount = BatchCandidates.Count(c => c.Status == PackageStatus.NeedsAttention);
        var skippedCount = BatchCandidates.Count(c => c.Status == PackageStatus.Skipped);
        BatchStatusText = $"{readyCount} ready, {needsAttentionCount} need attention, {skippedCount} skipped";

        ProcessBatchCommand.NotifyCanExecuteChanged();
        UpdateBatchCounts();
    }

    private void UpdateBatchCounts()
    {
        OnPropertyChanged(nameof(ReadyCount));
        OnPropertyChanged(nameof(NeedsAttentionCount));
        OnPropertyChanged(nameof(SkippedCount));
    }

    private async Task ShowBatchReviewDialog()
    {
        var dialog = new Views.BatchReviewDialog
        {
            DataContext = this
        };

        if (MainWindow != null)
        {
            await dialog.ShowDialog(MainWindow);
        }
    }

    [RelayCommand]
    private void CloseReviewDialog()
    {
        // This will be called from the dialog to close it
        // The actual closing is handled by the view
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
        HasBatchCandidates = false;
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
            HasBatchCandidates = BatchCandidates.Count > 0;

            // Show review dialog if there are candidates
            if (BatchCandidates.Count > 0)
            {
                await ShowBatchReviewDialog();
            }
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
        var baseOutputFolder = BatchOutputFolder.TrimEnd('\\', '/');

        // Create a unique output subfolder for this app to prevent filename collisions
        var appOutputFolder = Path.Combine(baseOutputFolder, candidate.FolderName);
        Directory.CreateDirectory(appOutputFolder);

        var arguments = $"-c \"{sourceFolder}\" -s \"{candidate.SelectedInstaller}\" -o \"{appOutputFolder}\" -q";

        AppendLog($"  Command: {Path.GetFileName(toolPath)} {arguments}");

        var startInfo = new ProcessStartInfo
        {
            FileName = toolPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
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

        // Automatically send Enter key to handle "Press any key to continue" prompt
        try
        {
            await process.StandardInput.WriteLineAsync();
            await process.StandardInput.FlushAsync();
        }
        catch { }

        // Add timeout to prevent hanging indefinitely (5 minutes)
        var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));
        var processTask = process.WaitForExitAsync();

        var completedTask = await Task.WhenAny(processTask, timeoutTask);

        if (completedTask == timeoutTask)
        {
            try
            {
                process.Kill();
            }
            catch { }
            throw new Exception("Process timed out after 5 minutes");
        }

        AppendLog($"  Exit code: {process.ExitCode}");

        if (process.ExitCode != 0)
        {
            throw new Exception($"IntuneWinAppUtil exited with code {process.ExitCode}");
        }

        // Verify output file was created
        var setupFileName = Path.GetFileNameWithoutExtension(candidate.SelectedInstaller);
        var expectedOutputFile = Path.Combine(appOutputFolder, $"{setupFileName}.intunewin");

        if (File.Exists(expectedOutputFile))
        {
            candidate.OutputFilePath = expectedOutputFile;
            AppendLog($"  Output: {candidate.FolderName}\\{Path.GetFileName(expectedOutputFile)}");
        }
        else
        {
            AppendLog($"  WARNING: Output file not found: {Path.GetFileName(expectedOutputFile)}");
        }
    }

    private bool CanProcessBatch()
    {
        return !IsProcessing &&
               BatchCandidates.Any(c => c.Status == PackageStatus.Ready) &&
               !string.IsNullOrWhiteSpace(BatchOutputFolder);
    }

    partial void OnSourceFolderChanged(string value) => CreatePackageCommand.NotifyCanExecuteChanged();
    partial void OnSetupFileChanged(string value)
    {
        CreatePackageCommand.NotifyCanExecuteChanged();
        UploadSinglePackageCommand.NotifyCanExecuteChanged();
    }
    partial void OnOutputFolderChanged(string value)
    {
        CreatePackageCommand.NotifyCanExecuteChanged();
        UploadSinglePackageCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsProcessingChanged(bool value)
    {
        CreatePackageCommand.NotifyCanExecuteChanged();
        ScanBatchFoldersCommand.NotifyCanExecuteChanged();
        ProcessBatchCommand.NotifyCanExecuteChanged();
    }
    partial void OnBatchParentFolderChanged(string value) => ScanBatchFoldersCommand.NotifyCanExecuteChanged();
    partial void OnBatchOutputFolderChanged(string value) => ProcessBatchCommand.NotifyCanExecuteChanged();

    // Intune Upload Methods

    [RelayCommand]
    private async Task TestGraphConnection()
    {
        if (string.IsNullOrWhiteSpace(GraphAccessToken))
        {
            GraphConnectionStatus = "Please enter an access token";
            GraphConnectionStatusColor = "#E74C3C";
            return;
        }

        GraphConnectionStatus = "Testing connection...";
        GraphConnectionStatusColor = "Gray";

        try
        {
            _intuneGraphService.InitializeWithAccessToken(GraphAccessToken);
            var (success, message) = await _intuneGraphService.TestConnectionAsync();

            GraphConnectionStatus = message;
            GraphConnectionStatusColor = success ? "#27AE60" : "#E74C3C";
        }
        catch (Exception ex)
        {
            GraphConnectionStatus = $"Error: {ex.Message}";
            GraphConnectionStatusColor = "#E74C3C";
        }
    }

    [RelayCommand]
    public async Task ShowIntuneUploadDialog()
    {
        // Gather all successfully created .intunewin files from batch processing
        UploadCandidates.Clear();

        foreach (var candidate in BatchCandidates.Where(c => !string.IsNullOrEmpty(c.OutputFilePath) && File.Exists(c.OutputFilePath!)))
        {
            var uploadCandidate = new IntuneUploadCandidate
            {
                DisplayName = candidate.FolderName, // Use folder name as initial display name
                FolderName = candidate.FolderName,
                PackageFilePath = candidate.OutputFilePath!,
                UploadStatus = "Ready"
            };

            UploadCandidates.Add(uploadCandidate);
        }

        if (UploadCandidates.Count == 0)
        {
            AppendLog("No packages available to upload");
            return;
        }

        UploadStatusText = $"{UploadCandidates.Count} package(s) ready";

        // Show the upload dialog
        var dialog = new Views.IntuneUploadDialog
        {
            DataContext = this
        };

        if (MainWindow != null)
        {
            await dialog.ShowDialog(MainWindow);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartUpload))]
    private async Task StartIntuneUpload()
    {
        if (string.IsNullOrWhiteSpace(GraphAccessToken))
        {
            GraphConnectionStatus = "Please configure your access token first";
            GraphConnectionStatusColor = "#E74C3C";
            return;
        }

        // Filter to only selected candidates
        var selectedCandidates = UploadCandidates.Where(c => c.IsSelected).ToList();

        if (selectedCandidates.Count == 0)
        {
            AppendLog("No packages selected for upload");
            return;
        }

        IsProcessing = true;
        AppendLog("");
        AppendLog($"Starting Intune upload for {selectedCandidates.Count} selected package(s)...");
        AppendLog(new string('-', 80));

        _intuneGraphService.InitializeWithAccessToken(GraphAccessToken);

        int successCount = 0;
        int failedCount = 0;
        int skippedCount = UploadCandidates.Count - selectedCandidates.Count;

        foreach (var candidate in selectedCandidates)
        {
            candidate.UploadStatus = "Uploading...";
            UploadStatusText = $"Uploading {candidate.DisplayName}...";

            AppendLog("");
            AppendLog($"Uploading: {candidate.DisplayName}");

            var (success, message, appId) = await _intuneGraphService.UploadWin32AppAsync(
                candidate.PackageFilePath,
                candidate.DisplayName,
                $"Uploaded from {candidate.FolderName}",
                AppendLog);

            if (success)
            {
                candidate.UploadStatus = "✓ Uploaded";
                candidate.IntuneAppId = appId;
                successCount++;
            }
            else
            {
                candidate.UploadStatus = $"✗ Failed";
                AppendLog($"[FAILED] {message}");
                failedCount++;
            }
        }

        // Mark skipped items
        foreach (var candidate in UploadCandidates.Where(c => !c.IsSelected))
        {
            if (candidate.UploadStatus == "Ready")
            {
                candidate.UploadStatus = "Skipped";
            }
        }

        IsProcessing = false;
        AppendLog("");
        AppendLog(new string('-', 80));
        AppendLog($"Upload complete:");
        AppendLog($"  Success: {successCount}");
        AppendLog($"  Failed: {failedCount}");
        if (skippedCount > 0)
        {
            AppendLog($"  Skipped: {skippedCount}");
        }

        UploadStatusText = $"Complete: {successCount} uploaded, {failedCount} failed";
    }

    private bool CanStartUpload()
    {
        return !IsProcessing &&
               UploadCandidates.Any(c => c.IsSelected) &&
               !string.IsNullOrWhiteSpace(GraphAccessToken);
    }

    partial void OnGraphAccessTokenChanged(string value) => StartIntuneUploadCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void SelectAllUploadCandidates()
    {
        foreach (var candidate in UploadCandidates)
        {
            candidate.IsSelected = true;
        }
        OnPropertyChanged(nameof(UploadSelectionCount));
        StartIntuneUploadCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void DeselectAllUploadCandidates()
    {
        foreach (var candidate in UploadCandidates)
        {
            candidate.IsSelected = false;
        }
        OnPropertyChanged(nameof(UploadSelectionCount));
        StartIntuneUploadCommand.NotifyCanExecuteChanged();
    }
}
