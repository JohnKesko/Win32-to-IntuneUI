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
}
