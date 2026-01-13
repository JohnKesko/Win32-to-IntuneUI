using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Win32_to_IntuneUI.Models;
using Win32_to_IntuneUI.Services;

namespace Win32_to_IntuneUI.ViewModels;

public partial class IntuneUploadViewModel : ViewModelBase
{
    [ObservableProperty] private ObservableCollection<IntuneUploadCandidate> _uploadCandidates = [];

    // Authentication fields
    [ObservableProperty] private string _tenantId = string.Empty;
    [ObservableProperty] private string _clientId = string.Empty;
    [ObservableProperty] private string _clientSecret = string.Empty;
    [ObservableProperty] private string? _graphConnectionStatus;
    [ObservableProperty] private string _graphConnectionStatusColor = "Gray";
    [ObservableProperty] private bool _isAuthenticated;
    [ObservableProperty] private string? _tokenExpiresAt;

    [ObservableProperty] private string _uploadStatusText = string.Empty;
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string _logOutput = string.Empty;

    // Progress tracking
    [ObservableProperty] private int _progressCurrent;
    [ObservableProperty] private int _progressTotal;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private bool _isProgressVisible;
    [ObservableProperty] private bool _isUploading; // True while uploads are in progress (for indeterminate progress bar)
    [ObservableProperty] private int _parallelUploads = 3; // Default parallel uploads (conservative for API rate limits)

    // Completion banner
    [ObservableProperty] private bool _isUploadComplete;
    [ObservableProperty] private string _completionIcon = "✓";
    [ObservableProperty] private string _completionTitle = string.Empty;
    [ObservableProperty] private string _completionMessage = string.Empty;
    [ObservableProperty] private string _completionBannerBackground = "#D4EDDA";
    [ObservableProperty] private string _completionBannerBorder = "#28A745";

    public string UploadSelectionCount => $"{UploadCandidates.Count(c => c.IsSelected)} of {UploadCandidates.Count} selected";

    private readonly IntuneGraphService _intuneGraphService;
    private string? _accessToken;
    private readonly object _logLock = new();

    public Window? MainWindow { get; set; }

    public IntuneUploadViewModel()
    {
        _intuneGraphService = new IntuneGraphService();
    }

    /// <summary>
    /// Populate upload candidates from batch processing results
    /// </summary>
    public void PopulateFromBatchResults(ObservableCollection<AppPackageCandidate> batchCandidates)
    {
        UploadCandidates.Clear();

        foreach (var candidate in batchCandidates.Where(c =>
            !string.IsNullOrEmpty(c.OutputFilePath) && File.Exists(c.OutputFilePath!)))
        {
            var uploadCandidate = new IntuneUploadCandidate
            {
                DisplayName = candidate.FolderName,
                FolderName = candidate.FolderName,
                PackageFilePath = candidate.OutputFilePath!,
                SourceFolderPath = candidate.FolderPath, // Pass source folder for script detection
                UploadStatus = "Ready"
            };

            // Priority 1: Apply config from intuneconfig.json if available
            if (candidate.IntuneConfig != null)
            {
                ApplyIntuneConfig(uploadCandidate, candidate.IntuneConfig);
            }
            // Priority 2: Try to parse package info from .txt files in the source folder
            else
            {
                var packageInfo = PackageInfoParser.TryParseFromFolder(candidate.FolderPath);
                if (packageInfo != null)
                {
                    ApplyPackageInfo(uploadCandidate, packageInfo);
                }
            }

            // Detect scripts and generate commands for any fields not set by config
            if (string.IsNullOrEmpty(uploadCandidate.InstallCommand))
                uploadCandidate.DetectAndGenerateCommands(candidate.SetupFileName);

            UploadCandidates.Add(uploadCandidate);
        }

        UploadStatusText = UploadCandidates.Count > 0
            ? $"{UploadCandidates.Count} package(s) ready"
            : "No packages available";

        OnPropertyChanged(nameof(UploadSelectionCount));
    }

    /// <summary>
    /// Applies IntuneConfigFile settings to an upload candidate
    /// </summary>
    private static void ApplyIntuneConfig(IntuneUploadCandidate candidate, IntuneConfigFile config)
    {
        if (!string.IsNullOrWhiteSpace(config.DisplayName))
            candidate.DisplayName = config.DisplayName;
        if (!string.IsNullOrWhiteSpace(config.Version))
            candidate.Version = config.Version;
        if (!string.IsNullOrWhiteSpace(config.InstallCommand))
            candidate.InstallCommand = config.InstallCommand;
        if (!string.IsNullOrWhiteSpace(config.UninstallCommand))
            candidate.UninstallCommand = config.UninstallCommand;
        if (!string.IsNullOrWhiteSpace(config.Publisher))
            candidate.Publisher = config.Publisher;
        if (!string.IsNullOrWhiteSpace(config.Description))
            candidate.Description = config.Description;
        if (config.DetectionRules != null && config.DetectionRules.Count > 0)
            candidate.DetectionRules = config.DetectionRules;
    }

    /// <summary>
    /// Applies parsed PackageInfo to an upload candidate
    /// </summary>
    private static void ApplyPackageInfo(IntuneUploadCandidate candidate, PackageInfoParser.PackageInfo info)
    {
        // Apply version
        if (!string.IsNullOrWhiteSpace(info.Version))
            candidate.Version = info.Version;

        // Build display name with version
        if (!string.IsNullOrWhiteSpace(info.Name))
        {
            candidate.DisplayName = !string.IsNullOrWhiteSpace(info.Version)
                ? $"{info.Name} {info.Version}"
                : info.Name;
        }

        if (!string.IsNullOrWhiteSpace(info.InstallCommand))
            candidate.InstallCommand = info.InstallCommand;
        if (!string.IsNullOrWhiteSpace(info.UninstallCommand))
            candidate.UninstallCommand = info.UninstallCommand;
        if (!string.IsNullOrWhiteSpace(info.Publisher))
            candidate.Publisher = info.Publisher;
        if (!string.IsNullOrWhiteSpace(info.Description))
            candidate.Description = info.Description;
        if (info.DetectionRules.Count > 0)
            candidate.DetectionRules = info.DetectionRules;
    }

    /// <summary>
    /// Populate upload candidates from a single package
    /// </summary>
    public void PopulateFromSinglePackage(string packagePath, string displayName)
    {
        UploadCandidates.Clear();

        if (File.Exists(packagePath))
        {
            var folderPath = Path.GetDirectoryName(packagePath) ?? "";
            var uploadCandidate = new IntuneUploadCandidate
            {
                DisplayName = displayName,
                FolderName = folderPath,
                PackageFilePath = packagePath,
                SourceFolderPath = folderPath, // For script detection
                UploadStatus = "Ready",
                IsSelected = true
            };

            // Try to parse package info from .txt files in the source folder
            var packageInfo = PackageInfoParser.TryParseFromFolder(folderPath);
            if (packageInfo != null)
            {
                ApplyPackageInfo(uploadCandidate, packageInfo);
            }

            // Detect scripts and generate commands for any fields not set
            uploadCandidate.DetectAndGenerateCommands();

            UploadCandidates.Add(uploadCandidate);
            UploadStatusText = "1 package ready";
        }
        else
        {
            UploadStatusText = "Package not found";
        }

        OnPropertyChanged(nameof(UploadSelectionCount));
        StartIntuneUploadCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Populate upload candidates from a list of file paths
    /// </summary>
    public void PopulateFromFiles(List<string> filePaths)
    {
        UploadCandidates.Clear();

        foreach (var filePath in filePaths.Where(File.Exists))
        {
            var folderPath = Path.GetDirectoryName(filePath) ?? "";
            var uploadCandidate = new IntuneUploadCandidate
            {
                DisplayName = Path.GetFileNameWithoutExtension(filePath),
                FolderName = folderPath,
                PackageFilePath = filePath,
                SourceFolderPath = folderPath, // For script detection
                UploadStatus = "Ready",
                IsSelected = true
            };

            // Try to parse package info from .txt files in the source folder
            var packageInfo = PackageInfoParser.TryParseFromFolder(folderPath);
            if (packageInfo != null)
            {
                ApplyPackageInfo(uploadCandidate, packageInfo);
            }

            // Detect scripts and generate commands for any fields not set
            uploadCandidate.DetectAndGenerateCommands();

            UploadCandidates.Add(uploadCandidate);
        }

        UploadStatusText = UploadCandidates.Count > 0
            ? $"{UploadCandidates.Count} package(s) ready"
            : "No valid packages found";

        OnPropertyChanged(nameof(UploadSelectionCount));
        StartIntuneUploadCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task BrowseForPackages()
    {
        if (MainWindow == null)
        {
            AppendLog("Error: MainWindow reference is null. Cannot open file picker.");
            UploadStatusText = "Error: Window reference not set";
            return;
        }

        var storageProvider = MainWindow.StorageProvider;
        var files = await storageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select .intunewin packages",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Intune Win32 Package")
                {
                    Patterns = new[] { "*.intunewin" }
                }
            }
        });

        if (files.Count > 0)
        {
            var filePaths = files.Select(f => f.Path.LocalPath).ToList();
            PopulateFromFiles(filePaths);
        }
    }

    [RelayCommand]
    private async Task TestGraphConnection()
    {
        if (string.IsNullOrWhiteSpace(TenantId) ||
            string.IsNullOrWhiteSpace(ClientId) ||
            string.IsNullOrWhiteSpace(ClientSecret))
        {
            GraphConnectionStatus = "Please fill in all authentication fields";
            GraphConnectionStatusColor = "#E74C3C";
            IsAuthenticated = false;
            return;
        }

        GraphConnectionStatus = "Acquiring token...";
        GraphConnectionStatusColor = "Gray";

        try
        {
            // First acquire the token
            var (tokenSuccess, tokenMessage, token, expiresAt) = await _intuneGraphService.AcquireTokenAsync(
                TenantId, ClientId, ClientSecret);

            if (!tokenSuccess || string.IsNullOrEmpty(token))
            {
                GraphConnectionStatus = tokenMessage;
                GraphConnectionStatusColor = "#E74C3C";
                IsAuthenticated = false;
                return;
            }

            _accessToken = token;
            TokenExpiresAt = expiresAt?.ToLocalTime().ToString("HH:mm:ss");

            // Then test the connection
            GraphConnectionStatus = "Testing connection...";
            var (success, message) = await _intuneGraphService.TestConnectionAsync();

            GraphConnectionStatus = success
                ? $"{message} (token expires at {TokenExpiresAt})"
                : message;
            GraphConnectionStatusColor = success ? "#27AE60" : "#E74C3C";
            IsAuthenticated = success;
        }
        catch (Exception ex)
        {
            GraphConnectionStatus = $"Error: {ex.Message}";
            GraphConnectionStatusColor = "#E74C3C";
            IsAuthenticated = false;
        }

        StartIntuneUploadCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStartUpload))]
    private async Task StartIntuneUpload()
    {
        if (!IsAuthenticated || string.IsNullOrWhiteSpace(_accessToken))
        {
            GraphConnectionStatus = "Please authenticate first using 'Connect'";
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

        // Hide any previous completion banner
        IsUploadComplete = false;

        IsProcessing = true;
        IsProgressVisible = true;
        IsUploading = true;
        ProgressTotal = selectedCandidates.Count;
        ProgressCurrent = 0;
        ProgressText = $"Uploading 0/{selectedCandidates.Count}...";

        var startTime = DateTime.Now;
        AppendLog("");
        AppendLog($"Starting parallel Intune upload ({ParallelUploads} concurrent) for {selectedCandidates.Count} package(s)...");
        AppendLog(new string('-', 80));

        int successCount = 0;
        int failedCount = 0;
        int processedCount = 0;
        int skippedCount = UploadCandidates.Count - selectedCandidates.Count;

        // Use semaphore to limit concurrent uploads (API rate limiting)
        using var semaphore = new SemaphoreSlim(ParallelUploads);
        var lockObj = new object();

        var tasks = selectedCandidates.Select(async candidate =>
        {
            await semaphore.WaitAsync();
            try
            {
                int currentIndex;
                lock (lockObj)
                {
                    currentIndex = processedCount + 1;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    candidate.UploadStatus = "Uploading...";
                    ProgressText = $"Uploading {currentIndex}/{ProgressTotal}...";
                });

                AppendLogThreadSafe($"[START] {candidate.DisplayName}");

                var (success, message, appId) = await _intuneGraphService.UploadWin32AppAsync(
                    candidate.PackageFilePath,
                    candidate.DisplayName,
                    candidate.Description ?? $"Uploaded from {candidate.FolderName}",
                    candidate.InstallCommand ?? "",
                    candidate.UninstallCommand ?? "",
                    candidate.Publisher ?? "",
                    candidate.DetectionRules,
                    msg => AppendLogThreadSafe($"  [{candidate.DisplayName}] {msg}"));

                lock (lockObj)
                {
                    processedCount++;
                    if (success) successCount++;
                    else failedCount++;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    candidate.UploadStatus = success ? "✓ Uploaded" : "✗ Failed";
                    candidate.IntuneAppId = appId;
                    candidate.ErrorMessage = success ? null : message;
                    ProgressCurrent = processedCount;
                    UpdateProgressText(processedCount, ProgressTotal, startTime);
                });

                AppendLogThreadSafe(success
                    ? $"[SUCCESS] {candidate.DisplayName}"
                    : $"[FAILED] {candidate.DisplayName}: {message}");
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);

        // Mark skipped items
        foreach (var candidate in UploadCandidates.Where(c => !c.IsSelected))
        {
            if (candidate.UploadStatus == "Ready")
            {
                candidate.UploadStatus = "Skipped";
            }
        }

        var elapsed = DateTime.Now - startTime;

        IsProcessing = false;
        IsProgressVisible = false;
        IsUploading = false;
        ProgressText = string.Empty;

        AppendLog("");
        AppendLog(new string('-', 80));
        AppendLog($"Upload complete in {elapsed:mm\\:ss}:");
        AppendLog($"  Success: {successCount}");
        AppendLog($"  Failed: {failedCount}");
        if (skippedCount > 0)
        {
            AppendLog($"  Skipped: {skippedCount}");
        }

        UploadStatusText = $"Complete: {successCount} uploaded, {failedCount} failed";

        // Show completion banner
        ShowCompletionBanner(successCount, failedCount, elapsed);
    }

    private void ShowCompletionBanner(int successCount, int failedCount, TimeSpan elapsed)
    {
        if (failedCount == 0 && successCount > 0)
        {
            // All succeeded
            CompletionIcon = "✓";
            CompletionTitle = "Upload Complete!";
            CompletionMessage = $"{successCount} package(s) uploaded successfully in {elapsed:mm\\:ss}";
            CompletionBannerBackground = "#D4EDDA";
            CompletionBannerBorder = "#28A745";
        }
        else if (successCount == 0 && failedCount > 0)
        {
            // All failed
            CompletionIcon = "✗";
            CompletionTitle = "Upload Failed";
            CompletionMessage = $"All {failedCount} package(s) failed to upload. Check the log for details.";
            CompletionBannerBackground = "#F8D7DA";
            CompletionBannerBorder = "#DC3545";
        }
        else
        {
            // Partial success
            CompletionIcon = "⚠";
            CompletionTitle = "Upload Partially Complete";
            CompletionMessage = $"{successCount} succeeded, {failedCount} failed in {elapsed:mm\\:ss}";
            CompletionBannerBackground = "#FFF3CD";
            CompletionBannerBorder = "#FFC107";
        }

        IsUploadComplete = true;
    }

    [RelayCommand]
    private void DismissCompletion()
    {
        IsUploadComplete = false;
    }

    private void UpdateProgressText(int current, int total, DateTime startTime)
    {
        var elapsed = DateTime.Now - startTime;
        var percentage = (int)((double)current / total * 100);

        if (current > 0)
        {
            var avgPerItem = elapsed.TotalSeconds / current;
            var remaining = TimeSpan.FromSeconds(avgPerItem * (total - current));
            ProgressText = $"Uploading {current}/{total} ({percentage}%) - ~{remaining:mm\\:ss} remaining";
        }
        else
        {
            ProgressText = $"Uploading {current}/{total} ({percentage}%)";
        }
    }

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

    private bool CanStartUpload()
    {
        return !IsProcessing &&
               UploadCandidates.Any(c => c.IsSelected) &&
               IsAuthenticated;
    }

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

    [RelayCommand]
    private async Task ShowUploadLog()
    {
        if (MainWindow == null) return;

        var box = new Window
        {
            Title = "Upload Log",
            Width = 700,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBox
            {
                Text = string.IsNullOrEmpty(LogOutput) ? "No log entries yet." : LogOutput,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(10),
                FontFamily = new Avalonia.Media.FontFamily("Consolas, Courier New, monospace"),
                FontSize = 12
            }
        };
        await box.ShowDialog(MainWindow);
    }

    public void AppendLog(string message)
    {
        // Route to centralized log service
        AppLogService.Instance.Log("Upload", message);
        // Also keep local log for the Show Log dialog
        LogOutput += $"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}";
    }

    partial void OnIsProcessingChanged(bool value) => StartIntuneUploadCommand.NotifyCanExecuteChanged();
    partial void OnIsAuthenticatedChanged(bool value) => StartIntuneUploadCommand.NotifyCanExecuteChanged();
}
