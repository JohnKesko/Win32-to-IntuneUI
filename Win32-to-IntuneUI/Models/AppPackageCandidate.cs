using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Win32_to_IntuneUI.Models;

public partial class AppPackageCandidate : ObservableObject
{
    [ObservableProperty]
    private string _folderName = string.Empty;

    [ObservableProperty]
    private string _folderPath = string.Empty;

    [ObservableProperty]
    private List<string> _detectedInstallers = [];

    [ObservableProperty]
    private string? _selectedInstaller;

    [ObservableProperty]
    private PackageStatus _status = PackageStatus.Pending;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _outputFilePath;

    public bool CanProcess => Status == PackageStatus.Ready || Status == PackageStatus.Failed;

    public bool NeedsUserInput => Status == PackageStatus.NeedsAttention;

    partial void OnStatusChanged(PackageStatus value)
    {
        OnPropertyChanged(nameof(CanProcess));
        OnPropertyChanged(nameof(NeedsUserInput));
    }
}

public enum PackageStatus
{
    Pending,        // Not yet scanned
    Ready,          // Auto-detected, ready to process
    NeedsAttention, // Multiple or no installers found
    Processing,     // Currently being processed
    Success,        // Successfully created .intunewin
    Failed,         // Failed to create package
    Skipped         // User chose to skip
}
