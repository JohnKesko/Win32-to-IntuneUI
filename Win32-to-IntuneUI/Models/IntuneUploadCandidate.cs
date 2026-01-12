using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Win32_to_IntuneUI.Services;

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

    // Known launcher exe patterns with install/uninstall arguments
    // These are common enterprise package launcher patterns
    private static readonly (string ExePattern, string InstallArg, string UninstallArg)[] LauncherPatterns =
    [
        // Common launcher argument patterns
        ("Launcher.exe", "-i", "-u"),
        ("Launcher.exe", "-install", "-uninstall"),
        ("Launcher.exe", "-os", "-os"),  // Some launchers use same arg
        ("Setup.exe", "-i", "-u"),
        ("Setup.exe", "-install", "-uninstall"),
        ("Installer.exe", "-i", "-u"),
        ("Deploy.exe", "-i", "-u"),
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

    /// <summary>
    /// Custom detection rules from intuneconfig.json
    /// </summary>
    public List<DetectionRule>? DetectionRules { get; set; }

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
    /// Uses PE file analysis for EXE files to detect installer type and extract metadata.
    /// Used as fallback when no scripts are detected.
    /// </summary>
    public void GenerateDefaultCommands(string? setupFileName = null)
    {
        // Try to determine the original setup file name from the package
        var baseName = setupFileName ?? Path.GetFileNameWithoutExtension(PackageFilePath);

        // Remove .intunewin if present
        if (baseName.EndsWith(".intunewin", StringComparison.OrdinalIgnoreCase))
            baseName = baseName[..^10];

        // Determine the actual file path for analysis
        string? setupFilePath = null;
        if (!string.IsNullOrEmpty(SourceFolderPath) && !string.IsNullOrEmpty(setupFileName))
        {
            setupFilePath = Path.Combine(SourceFolderPath, setupFileName);
        }

        // Handle MSI files
        if (baseName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
            (setupFileName?.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            var msiName = setupFileName ?? baseName;
            if (!msiName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                msiName += ".msi";

            InstallCommand = $"msiexec /i \"{msiName}\" /qn /norestart";
            UninstallCommand = $"msiexec /x \"{msiName}\" /qn /norestart";
            return;
        }

        // Handle CMD/BAT files (custom scripts)
        if (setupFileName?.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) == true ||
            setupFileName?.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) == true)
        {
            InstallCommand = setupFileName;
            // Try to find corresponding uninstall script
            var uninstallName = setupFileName
                .Replace("install", "uninstall", StringComparison.OrdinalIgnoreCase)
                .Replace("setup", "uninstall", StringComparison.OrdinalIgnoreCase);
            if (uninstallName != setupFileName)
                UninstallCommand = uninstallName;
            return;
        }

        // Handle EXE files - check for launcher patterns first, then PE analysis
        if (setupFileName?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true ||
            baseName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var exeName = setupFileName ?? (baseName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? baseName
                : baseName + ".exe");

            // Check if this matches a known launcher pattern
            var launcherMatch = TryMatchLauncherPattern(exeName);
            if (launcherMatch.HasValue)
            {
                InstallCommand = $"\"{exeName}\" {launcherMatch.Value.InstallArg}";
                UninstallCommand = $"\"{exeName}\" {launcherMatch.Value.UninstallArg}";

                // Still try to extract metadata from PE file
                if (setupFilePath != null && File.Exists(setupFilePath))
                {
                    var peMetadata = PeFileAnalyzer.Analyze(setupFilePath);
                    ApplyPeMetadata(peMetadata, exeName);
                }
                return;
            }

            // Try PE file analysis if the file exists
            if (setupFilePath != null && File.Exists(setupFilePath))
            {
                var peResult = PeFileAnalyzer.Analyze(setupFilePath);

                // Apply extracted metadata if not already set
                ApplyPeMetadata(peResult, exeName);

                // Use detected installer switches if available
                if (!string.IsNullOrEmpty(peResult.SuggestedSilentSwitch))
                {
                    InstallCommand = $"\"{exeName}\" {peResult.SuggestedSilentSwitch}";

                    if (!string.IsNullOrEmpty(peResult.SuggestedUninstallSwitch))
                    {
                        UninstallCommand = $"\"{exeName}\" {peResult.SuggestedUninstallSwitch}";
                    }
                    return;
                }
            }

            // Default EXE handling - try common silent switches
            InstallCommand = $"\"{exeName}\" /S";
            UninstallCommand = $"\"{exeName}\" /S /uninstall";
            return;
        }

        // Fallback: assume .exe
        var defaultExeName = baseName + ".exe";
        InstallCommand = $"\"{defaultExeName}\" /S";
        UninstallCommand = $"\"{defaultExeName}\" /S /uninstall";
    }

    /// <summary>
    /// Tries to match the exe name against known launcher patterns.
    /// </summary>
    private static (string InstallArg, string UninstallArg)? TryMatchLauncherPattern(string exeName)
    {
        var fileName = Path.GetFileName(exeName);

        foreach (var (exePattern, installArg, uninstallArg) in LauncherPatterns)
        {
            if (string.Equals(fileName, exePattern, StringComparison.OrdinalIgnoreCase))
            {
                return (installArg, uninstallArg);
            }
        }

        // Also check for common launcher naming patterns
        var lowerName = fileName.ToLowerInvariant();
        if (lowerName.Contains("launcher"))
        {
            // Default launcher args
            return ("-i", "-u");
        }

        return null;
    }

    /// <summary>
    /// Applies metadata extracted from PE file analysis to fill in missing fields.
    /// </summary>
    private void ApplyPeMetadata(PeFileAnalyzer.AnalysisResult peResult, string exeName)
    {
        // Only apply if fields are not already set (from intuneconfig.json or .txt files)

        // Apply display name from PE file
        if (string.IsNullOrEmpty(DisplayName) || DisplayName == FolderName)
        {
            var appName = PeFileAnalyzer.GetBestAppName(peResult);
            var version = PeFileAnalyzer.GetBestVersion(peResult);

            if (!string.IsNullOrEmpty(appName))
            {
                DisplayName = !string.IsNullOrEmpty(version)
                    ? $"{appName} {version}"
                    : appName;
            }
        }

        // Apply publisher from PE file
        if (string.IsNullOrEmpty(Publisher))
        {
            if (!string.IsNullOrEmpty(peResult.CompanyName))
            {
                Publisher = peResult.CompanyName;
            }
        }

        // Apply description from PE file
        if (string.IsNullOrEmpty(Description))
        {
            if (!string.IsNullOrEmpty(peResult.FileDescription))
            {
                Description = peResult.FileDescription;
            }
        }
    }
}
