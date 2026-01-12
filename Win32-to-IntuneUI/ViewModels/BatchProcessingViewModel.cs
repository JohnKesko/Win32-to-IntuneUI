using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Win32_to_IntuneUI.Models;
using Win32_to_IntuneUI.Services;
using Win32_to_IntuneUI.Views;

namespace Win32_to_IntuneUI.ViewModels;

public partial class BatchProcessingViewModel : ViewModelBase
{
    [ObservableProperty] private string _batchParentFolder = string.Empty;
    [ObservableProperty] private string _batchOutputFolder = string.Empty;
    [ObservableProperty] private string _batchStatusText = "Scan folders to begin batch processing";
    [ObservableProperty] private ObservableCollection<AppPackageCandidate> _batchCandidates = [];
    [ObservableProperty] private bool _hasBatchCandidates;
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string _logOutput = string.Empty;
    [ObservableProperty] private string _toolStatus = string.Empty;

    // Progress tracking
    [ObservableProperty] private int _progressCurrent;
    [ObservableProperty] private int _progressTotal;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private bool _isProgressVisible;
    [ObservableProperty] private int _parallelTasks = 4; // Default parallel tasks

    public int ReadyCount => BatchCandidates.Count(c => c.Status == PackageStatus.Ready);
    public int NeedsAttentionCount => BatchCandidates.Count(c => c.Status == PackageStatus.NeedsAttention);
    public int SkippedCount => BatchCandidates.Count(c => c.Status == PackageStatus.Skipped);

    private readonly IntuneToolDownloader _toolDownloader;
    private static readonly string[] InstallerExtensions = [".msi", ".exe", ".cmd", ".bat"];

    public Window? MainWindow { get; set; }

    /// <summary>
    /// Event raised when batch processing completes successfully
    /// </summary>
    public event EventHandler<ObservableCollection<AppPackageCandidate>>? BatchCompleted;

    public BatchProcessingViewModel()
    {
        _toolDownloader = new IntuneToolDownloader();
        _toolDownloader.StatusChanged += (_, status) => AppendLog(status);
    }

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
            candidate.SelectedInstaller = files[0].Path.LocalPath;
            candidate.Status = PackageStatus.Ready;
            candidate.ErrorMessage = null;
            AppendLog($"Manually selected installer for {candidate.FolderName}: {Path.GetFileName(candidate.SelectedInstaller)}");

            UpdateStatusText();
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

        UpdateStatusText();
        ProcessBatchCommand.NotifyCanExecuteChanged();
        UpdateBatchCounts();
    }

    private void UpdateStatusText()
    {
        var readyCount = BatchCandidates.Count(c => c.Status == PackageStatus.Ready);
        var needsAttentionCount = BatchCandidates.Count(c => c.Status == PackageStatus.NeedsAttention);
        var skippedCount = BatchCandidates.Count(c => c.Status == PackageStatus.Skipped);

        BatchStatusText = skippedCount > 0
            ? $"{readyCount} ready, {needsAttentionCount} need attention, {skippedCount} skipped"
            : $"{readyCount} ready, {needsAttentionCount} need attention";
    }

    private void UpdateBatchCounts()
    {
        OnPropertyChanged(nameof(ReadyCount));
        OnPropertyChanged(nameof(NeedsAttentionCount));
        OnPropertyChanged(nameof(SkippedCount));
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

                // Check for config file first
                var configInstaller = TryReadInstallerFromConfig(subfolder);

                var installers = Directory.GetFiles(subfolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => InstallerExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                var candidate = new AppPackageCandidate
                {
                    FolderName = folderName,
                    FolderPath = subfolder,
                    DetectedInstallers = installers
                };

                // Priority 1: Config file specifies installer
                if (configInstaller != null)
                {
                    candidate.SelectedInstaller = configInstaller;
                    candidate.Status = PackageStatus.Ready;
                    AppendLog($"  [OK] From config: {Path.GetFileName(configInstaller)}");
                }
                else if (installers.Count == 0)
                {
                    candidate.Status = PackageStatus.NeedsAttention;
                    candidate.ErrorMessage = "No installer files found";
                    AppendLog("  [!] No installer found");
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
                    var autoSelected = TryAutoSelectInstaller(installers, folderName);
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

            UpdateStatusText();
            UpdateBatchCounts();

            AppendLog($"Scan complete:");
            AppendLog($"  Ready to process: {ReadyCount}");
            AppendLog($"  Needs attention: {NeedsAttentionCount}");

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

    private async Task ShowBatchReviewDialog()
    {
        var dialog = new BatchReviewDialog
        {
            DataContext = this
        };

        if (MainWindow != null)
        {
            await dialog.ShowDialog(MainWindow);
        }
    }

    /// <summary>
    /// Try to read installer path from a config file in the folder
    /// Supports: intuneconfig.json, package.json with "installer" field
    /// </summary>
    private static string? TryReadInstallerFromConfig(string folderPath)
    {
        // Check for intuneconfig.json
        var configPath = Path.Combine(folderPath, "intuneconfig.json");
        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<IntuneConfigFile>(json);
                if (!string.IsNullOrWhiteSpace(config?.Installer))
                {
                    var installerPath = Path.Combine(folderPath, config.Installer);
                    if (File.Exists(installerPath))
                    {
                        return installerPath;
                    }
                }
            }
            catch { /* Ignore parse errors */ }
        }

        return null;
    }

    /// <summary>
    /// Improved auto-selection with more patterns and folder name matching
    /// </summary>
    private static string? TryAutoSelectInstaller(List<string> installers, string folderName)
    {
        if (installers.Count == 0) return null;

        // Priority 1: Single .msi file (MSI is almost always the right choice)
        var msiFiles = installers.Where(f => f.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)).ToList();
        if (msiFiles.Count == 1) return msiFiles[0];

        // Priority 2: PSADT Deploy-Application.exe
        var psadtFile = installers.FirstOrDefault(f =>
            Path.GetFileName(f).Equals("Deploy-Application.exe", StringComparison.OrdinalIgnoreCase));
        if (psadtFile != null) return psadtFile;

        // Priority 3: File name matches folder name (e.g., "7-Zip" folder with "7-Zip.msi")
        var folderNameLower = folderName.ToLowerInvariant()
            .Replace(" ", "").Replace("-", "").Replace("_", "");
        var matchingFile = installers.FirstOrDefault(f =>
        {
            var fileName = Path.GetFileNameWithoutExtension(f).ToLowerInvariant()
                .Replace(" ", "").Replace("-", "").Replace("_", "");
            return fileName.Contains(folderNameLower) || folderNameLower.Contains(fileName);
        });
        if (matchingFile != null) return matchingFile;

        // Priority 4: Files with setup/install/installer in the name
        var setupPatterns = new[] { "setup", "install", "installer" };
        var setupFiles = installers.Where(f =>
        {
            var name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
            return setupPatterns.Any(p => name.Contains(p));
        }).ToList();
        if (setupFiles.Count == 1) return setupFiles[0];

        // Priority 5: Prefer .exe over .msi if multiple, then by size
        if (setupFiles.Count > 1)
        {
            // Prefer setup.exe pattern
            var setupExe = setupFiles.FirstOrDefault(f =>
                Path.GetFileName(f).Equals("setup.exe", StringComparison.OrdinalIgnoreCase));
            if (setupExe != null) return setupExe;

            return setupFiles.OrderByDescending(f => new FileInfo(f).Length).First();
        }

        // Priority 6: Largest .exe file (likely the main installer)
        var exeFiles = installers.Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
        if (exeFiles.Count > 0)
        {
            return exeFiles.OrderByDescending(f => new FileInfo(f).Length).First();
        }

        // Priority 7: Any largest file
        return installers.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
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
        IsProgressVisible = true;
        LogOutput = string.Empty;

        var processableCandidates = BatchCandidates.Where(c => c.Status == PackageStatus.Ready).ToList();
        ProgressTotal = processableCandidates.Count;
        ProgressCurrent = 0;

        AppendLog($"Starting parallel batch processing ({ParallelTasks} concurrent tasks)...");
        AppendLog($"Processing {ProgressTotal} application(s)");
        AppendLog(new string('-', 80));

        var successCount = 0;
        var failedCount = 0;
        var processedCount = 0;
        var startTime = DateTime.Now;

        // Use semaphore to limit concurrent processes
        using var semaphore = new SemaphoreSlim(ParallelTasks);
        var lockObj = new object();

        var tasks = processableCandidates.Select(async candidate =>
        {
            await semaphore.WaitAsync();
            try
            {
                // Update status on UI thread
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    candidate.Status = PackageStatus.Processing;
                });

                AppendLogThreadSafe($"[START] {candidate.FolderName}");

                await ProcessSingleBatchApp(toolPath, candidate);

                lock (lockObj)
                {
                    successCount++;
                    processedCount++;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    candidate.Status = PackageStatus.Success;
                    ProgressCurrent = processedCount;
                    UpdateProgressText(processedCount, ProgressTotal, startTime);
                });

                AppendLogThreadSafe($"[SUCCESS] {candidate.FolderName}");
            }
            catch (Exception ex)
            {
                lock (lockObj)
                {
                    failedCount++;
                    processedCount++;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    candidate.Status = PackageStatus.Failed;
                    candidate.ErrorMessage = ex.Message;
                    ProgressCurrent = processedCount;
                    UpdateProgressText(processedCount, ProgressTotal, startTime);
                });

                AppendLogThreadSafe($"[FAILED] {candidate.FolderName}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);

        var elapsed = DateTime.Now - startTime;

        IsProcessing = false;
        IsProgressVisible = false;
        AppendLog("");
        AppendLog(new string('-', 80));
        AppendLog($"Batch processing complete in {elapsed:mm\\:ss}:");
        AppendLog($"  Success: {successCount}/{ProgressTotal}");
        AppendLog($"  Failed: {failedCount}/{ProgressTotal}");
        AppendLog($"  Average: {(elapsed.TotalSeconds / ProgressTotal):F1}s per package");

        BatchStatusText = $"Complete: {successCount} success, {failedCount} failed";
        ProgressText = string.Empty;

        // Notify that batch is complete
        BatchCompleted?.Invoke(this, BatchCandidates);

        ProcessBatchCommand.NotifyCanExecuteChanged();
    }

    private void UpdateProgressText(int current, int total, DateTime startTime)
    {
        var elapsed = DateTime.Now - startTime;
        var percentage = (int)((double)current / total * 100);

        if (current > 0)
        {
            var avgPerItem = elapsed.TotalSeconds / current;
            var remaining = TimeSpan.FromSeconds(avgPerItem * (total - current));
            ProgressText = $"Processing {current}/{total} ({percentage}%) - ~{remaining:mm\\:ss} remaining";
        }
        else
        {
            ProgressText = $"Processing {current}/{total} ({percentage}%)";
        }
    }

    private readonly object _logLock = new();

    private void AppendLogThreadSafe(string message)
    {
        lock (_logLock)
        {
            Dispatcher.UIThread.Post(() =>
            {
                LogOutput += $"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}";
            });
        }
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
        catch { /* Ignore */ }

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
            catch { /* Ignore */ }
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

    public void AppendLog(string message)
    {
        LogOutput += $"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}";
    }

    partial void OnBatchParentFolderChanged(string value) => ScanBatchFoldersCommand.NotifyCanExecuteChanged();
    partial void OnBatchOutputFolderChanged(string value) => ProcessBatchCommand.NotifyCanExecuteChanged();
    partial void OnIsProcessingChanged(bool value)
    {
        ScanBatchFoldersCommand.NotifyCanExecuteChanged();
        ProcessBatchCommand.NotifyCanExecuteChanged();
    }
}
