using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace Win32_to_IntuneUI.Models;

public partial class IntuneUploadCandidate : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true; // Selected by default

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _folderName = string.Empty;

    [ObservableProperty]
    private string _packageFilePath = string.Empty;

    [ObservableProperty]
    private string _uploadStatus = "Ready";

    // Optional Intune metadata fields
    [ObservableProperty]
    private string _installCommand = string.Empty;

    [ObservableProperty]
    private string _uninstallCommand = string.Empty;

    [ObservableProperty]
    private string _publisher = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    public string PackageFileName => Path.GetFileName(PackageFilePath);

    public string PackageFileSizeFormatted
    {
        get
        {
            if (!File.Exists(PackageFilePath))
                return "N/A";

            var fileInfo = new FileInfo(PackageFilePath);
            var bytes = fileInfo.Length;

            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):F1} MB";

            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        }
    }

    [ObservableProperty]
    private string? _intuneAppId;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Generates default install/uninstall commands based on the package filename
    /// </summary>
    public void GenerateDefaultCommands(string? setupFileName = null)
    {
        // Try to determine the original setup file name from the package
        var baseName = setupFileName ?? Path.GetFileNameWithoutExtension(PackageFilePath);

        // Remove .intunewin if present
        if (baseName.EndsWith(".intunewin", System.StringComparison.OrdinalIgnoreCase))
            baseName = baseName[..^10];

        // Try common patterns
        if (baseName.EndsWith(".msi", System.StringComparison.OrdinalIgnoreCase))
        {
            InstallCommand = $"msiexec /i \"{baseName}\" /qn";
            UninstallCommand = $"msiexec /x \"{baseName}\" /qn";
        }
        else if (baseName.EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase))
        {
            InstallCommand = $"\"{baseName}\" /S";
            UninstallCommand = $"\"{baseName}\" /S /uninstall";
        }
        else
        {
            // Default to .exe assumption
            var exeName = baseName + ".exe";
            InstallCommand = $"\"{exeName}\" /S";
            UninstallCommand = $"\"{exeName}\" /S /uninstall";
        }
    }
}
