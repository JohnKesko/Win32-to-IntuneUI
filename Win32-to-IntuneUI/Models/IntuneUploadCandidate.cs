using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Win32_to_IntuneUI.Models;

public partial class IntuneUploadCandidate : ObservableObject
{
    // Common install script patterns (priority order - first match wins)
    private static readonly (string Pattern, bool IsInstall)[] InstallScriptPatterns =
    [
        // Exact matches (highest priority)
        ("install.cmd", true),
        ("install.bat", true),
        ("install.ps1", true),
        ("uninstall.cmd", false),
        ("uninstall.bat", false),
        ("uninstall.ps1", false),
        
        // PSADT (PowerShell App Deployment Toolkit)
        ("Deploy-Application.ps1", true),
        
        // Common variations
        ("setup.cmd", true),
        ("setup.bat", true),
        ("setup.ps1", true),
        ("installer.cmd", true),
        ("installer.bat", true),
        ("deploy.cmd", true),
        ("deploy.bat", true),
        ("deploy.ps1", true),
        
        // Uninstall variations
        ("remove.cmd", false),
        ("remove.bat", false),
        ("remove.ps1", false),
        ("cleanup.cmd", false),
        ("cleanup.bat", false),
    ];

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

    /// <summary>
    /// Path to the original source folder (for scanning install scripts)
    /// </summary>
    [ObservableProperty]
    private string _sourceFolderPath = string.Empty;

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
    /// Detects install/uninstall scripts in the source folder and generates appropriate commands.
    /// Falls back to setup file-based commands if no scripts are found.
    /// </summary>
    public void DetectAndGenerateCommands(string? setupFileName = null)
    {
        // First, try to detect scripts in the source folder
        if (!string.IsNullOrEmpty(SourceFolderPath) && Directory.Exists(SourceFolderPath))
        {
            var detectedInstall = DetectScript(SourceFolderPath, isInstall: true);
            var detectedUninstall = DetectScript(SourceFolderPath, isInstall: false);

            if (!string.IsNullOrEmpty(detectedInstall))
            {
                InstallCommand = GenerateCommandForScript(detectedInstall);
            }

            if (!string.IsNullOrEmpty(detectedUninstall))
            {
                UninstallCommand = GenerateCommandForScript(detectedUninstall);
            }

            // If we found at least an install script, we're done
            if (!string.IsNullOrEmpty(InstallCommand))
            {
                // If no uninstall script found, try to derive from install
                if (string.IsNullOrEmpty(UninstallCommand) && !string.IsNullOrEmpty(detectedInstall))
                {
                    UninstallCommand = TryDeriveUninstallCommand(detectedInstall);
                }
                return;
            }
        }

        // Fall back to setup file-based commands
        GenerateDefaultCommands(setupFileName);
    }

    /// <summary>
    /// Detects a script file in the given folder based on common patterns.
    /// </summary>
    private static string? DetectScript(string folderPath, bool isInstall)
    {
        try
        {
            var files = Directory.GetFiles(folderPath)
                .Select(Path.GetFileName)
                .Where(f => f != null)
                .ToList();

            // Check patterns in priority order
            foreach (var (pattern, patternIsInstall) in InstallScriptPatterns)
            {
                if (patternIsInstall != isInstall)
                    continue;

                var match = files.FirstOrDefault(f =>
                    string.Equals(f, pattern, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                    return match;
            }

            // Fallback: look for any script containing "install" or "uninstall"
            var keyword = isInstall ? "install" : "uninstall";
            var scriptExtensions = new[] { ".cmd", ".bat", ".ps1" };

            var fallbackMatch = files.FirstOrDefault(f =>
                f != null &&
                f.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                scriptExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

            return fallbackMatch;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Generates the appropriate command line for a script file.
    /// </summary>
    private static string GenerateCommandForScript(string scriptFileName)
    {
        var extension = Path.GetExtension(scriptFileName).ToLowerInvariant();

        return extension switch
        {
            ".ps1" => $"powershell.exe -ExecutionPolicy Bypass -File \"{scriptFileName}\"",
            ".cmd" or ".bat" => scriptFileName,
            _ => scriptFileName
        };
    }

    /// <summary>
    /// Tries to derive an uninstall command from an install command.
    /// </summary>
    private static string TryDeriveUninstallCommand(string installScript)
    {
        var baseName = Path.GetFileNameWithoutExtension(installScript);
        var extension = Path.GetExtension(installScript);

        // Try common naming patterns
        string[] uninstallNames =
        [
            $"uninstall{extension}",
            $"un{baseName}{extension}",
            $"remove{extension}",
            $"{baseName.Replace("install", "uninstall", StringComparison.OrdinalIgnoreCase)}{extension}"
        ];

        // Just return a reasonable default
        return GenerateCommandForScript($"uninstall{extension}");
    }

    /// <summary>
    /// Generates default install/uninstall commands based on the setup filename.
    /// Used as fallback when no scripts are detected.
    /// </summary>
    public void GenerateDefaultCommands(string? setupFileName = null)
    {
        // Try to determine the original setup file name from the package
        var baseName = setupFileName ?? Path.GetFileNameWithoutExtension(PackageFilePath);

        // Remove .intunewin if present
        if (baseName.EndsWith(".intunewin", StringComparison.OrdinalIgnoreCase))
            baseName = baseName[..^10];

        // Try common patterns
        if (baseName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
        {
            InstallCommand = $"msiexec /i \"{baseName}\" /qn";
            UninstallCommand = $"msiexec /x \"{baseName}\" /qn";
        }
        else if (baseName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
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
