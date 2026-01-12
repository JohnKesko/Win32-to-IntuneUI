using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
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

    public string UploadSelectionCount => $"{UploadCandidates.Count(c => c.IsSelected)} of {UploadCandidates.Count} selected";

    private readonly IntuneGraphService _intuneGraphService;
    private string? _accessToken;

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
                UploadStatus = "Ready"
            };

            UploadCandidates.Add(uploadCandidate);
        }

        UploadStatusText = UploadCandidates.Count > 0
            ? $"{UploadCandidates.Count} package(s) ready"
            : "No packages available";

        OnPropertyChanged(nameof(UploadSelectionCount));
    }

    /// <summary>
    /// Populate upload candidates from a single package
    /// </summary>
    public void PopulateFromSinglePackage(string packagePath, string displayName)
    {
        UploadCandidates.Clear();

        if (File.Exists(packagePath))
        {
            var uploadCandidate = new IntuneUploadCandidate
            {
                DisplayName = displayName,
                FolderName = Path.GetDirectoryName(packagePath) ?? displayName,
                PackageFilePath = packagePath,
                UploadStatus = "Ready",
                IsSelected = true
            };

            UploadCandidates.Add(uploadCandidate);
            UploadStatusText = "1 package ready";
        }
        else
        {
            UploadStatusText = "Package not found";
        }

        OnPropertyChanged(nameof(UploadSelectionCount));
    }

    /// <summary>
    /// Populate upload candidates from a list of file paths
    /// </summary>
    public void PopulateFromFiles(List<string> filePaths)
    {
        UploadCandidates.Clear();

        foreach (var filePath in filePaths.Where(File.Exists))
        {
            var uploadCandidate = new IntuneUploadCandidate
            {
                DisplayName = Path.GetFileNameWithoutExtension(filePath),
                FolderName = Path.GetDirectoryName(filePath) ?? "",
                PackageFilePath = filePath,
                UploadStatus = "Ready",
                IsSelected = true
            };

            UploadCandidates.Add(uploadCandidate);
        }

        UploadStatusText = UploadCandidates.Count > 0
            ? $"{UploadCandidates.Count} package(s) ready"
            : "No valid packages found";

        OnPropertyChanged(nameof(UploadSelectionCount));
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

        IsProcessing = true;
        AppendLog("");
        AppendLog($"Starting Intune upload for {selectedCandidates.Count} selected package(s)...");
        AppendLog(new string('-', 80));

        // Token is already set from authentication
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
                candidate.UploadStatus = "✗ Failed";
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

    public void AppendLog(string message)
    {
        LogOutput += $"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}";
    }

    partial void OnIsProcessingChanged(bool value) => StartIntuneUploadCommand.NotifyCanExecuteChanged();
    partial void OnIsAuthenticatedChanged(bool value) => StartIntuneUploadCommand.NotifyCanExecuteChanged();
}
