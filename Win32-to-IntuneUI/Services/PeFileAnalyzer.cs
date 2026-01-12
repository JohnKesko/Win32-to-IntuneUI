using System;
using System.IO;
using System.Linq;
using PeNet;

namespace Win32_to_IntuneUI.Services;

/// <summary>
/// Analyzes PE (Portable Executable) files to extract metadata and detect installer types.
/// </summary>
public static class PeFileAnalyzer
{
    /// <summary>
    /// Result of PE file analysis containing version info and installer detection.
    /// </summary>
    public class AnalysisResult
    {
        public string? ProductName { get; set; }
        public string? CompanyName { get; set; }
        public string? FileDescription { get; set; }
        public string? FileVersion { get; set; }
        public string? ProductVersion { get; set; }
        public string? OriginalFilename { get; set; }

        /// <summary>
        /// Detected installer framework (NSIS, InnoSetup, InstallShield, WiX, etc.)
        /// </summary>
        public InstallerType DetectedInstaller { get; set; } = InstallerType.Unknown;

        /// <summary>
        /// Suggested silent install switch based on detected installer type.
        /// </summary>
        public string? SuggestedSilentSwitch { get; set; }

        /// <summary>
        /// Suggested silent uninstall switch based on detected installer type.
        /// </summary>
        public string? SuggestedUninstallSwitch { get; set; }

        /// <summary>
        /// Whether the file is a valid PE file.
        /// </summary>
        public bool IsValidPeFile { get; set; }

        /// <summary>
        /// Whether the PE file is a .NET assembly.
        /// </summary>
        public bool IsDotNet { get; set; }

        /// <summary>
        /// Whether the PE file is 64-bit.
        /// </summary>
        public bool Is64Bit { get; set; }
    }

    /// <summary>
    /// Known installer framework types.
    /// </summary>
    public enum InstallerType
    {
        Unknown,
        NSIS,           // Nullsoft Scriptable Install System
        InnoSetup,      // Inno Setup
        InstallShield,  // InstallShield
        WiX,            // Windows Installer XML
        MSI,            // Windows Installer (MSI)
        Wise,           // Wise Installer
        AdvancedInstaller,
        SetupFactory,
        SFXRAR,         // Self-extracting RAR
        SFX7Zip,        // Self-extracting 7-Zip
        Executable      // Standard executable (not an installer)
    }

    /// <summary>
    /// Analyzes a PE file and extracts metadata.
    /// </summary>
    /// <param name="filePath">Path to the PE file (EXE or DLL).</param>
    /// <returns>Analysis result with extracted information.</returns>
    public static AnalysisResult Analyze(string filePath)
    {
        var result = new AnalysisResult();

        if (!File.Exists(filePath))
        {
            return result;
        }

        try
        {
            // Check if it's a valid PE file first
            if (!PeFile.IsPeFile(filePath))
            {
                return result;
            }

            var peFile = new PeFile(filePath);
            result.IsValidPeFile = true;
            result.IsDotNet = peFile.IsDotNet;
            result.Is64Bit = peFile.Is64Bit;

            // Extract version information
            ExtractVersionInfo(peFile, result);

            // Detect installer type
            DetectInstallerType(peFile, filePath, result);

            // Set suggested switches based on installer type
            SetSuggestedSwitches(result);
        }
        catch (Exception)
        {
            // If parsing fails, return what we have
        }

        return result;
    }

    private static void ExtractVersionInfo(PeFile peFile, AnalysisResult result)
    {
        try
        {
            var stringTable = peFile.Resources?.VsVersionInfo?.StringFileInfo?.StringTable?.FirstOrDefault();
            if (stringTable != null)
            {
                result.ProductName = CleanString(stringTable.ProductName);
                result.CompanyName = CleanString(stringTable.CompanyName);
                result.FileDescription = CleanString(stringTable.FileDescription);
                result.FileVersion = CleanString(stringTable.FileVersion);
                result.ProductVersion = CleanString(stringTable.ProductVersion);
                result.OriginalFilename = CleanString(stringTable.OriginalFilename);
            }
        }
        catch
        {
            // Version info not available
        }
    }

    private static void DetectInstallerType(PeFile peFile, string filePath, AnalysisResult result)
    {
        // Check file extension first
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension == ".msi")
        {
            result.DetectedInstaller = InstallerType.MSI;
            return;
        }

        try
        {
            // Check section names for installer signatures
            var sectionNames = peFile.ImageSectionHeaders?
                .Select(s => s.Name?.Trim('\0') ?? string.Empty)
                .ToArray() ?? Array.Empty<string>();

            // NSIS has a specific section name
            if (sectionNames.Any(s => s.Equals(".ndata", StringComparison.OrdinalIgnoreCase)))
            {
                result.DetectedInstaller = InstallerType.NSIS;
                return;
            }

            // Check imported functions for clues
            var imports = peFile.ImportedFunctions?
                .Select(f => f.DLL?.ToLowerInvariant() ?? string.Empty)
                .Distinct()
                .ToArray() ?? Array.Empty<string>();

            // Check for common installer patterns in file description
            var description = result.FileDescription?.ToLowerInvariant() ?? string.Empty;
            var productName = result.ProductName?.ToLowerInvariant() ?? string.Empty;

            // Inno Setup detection
            if (description.Contains("inno setup") ||
                productName.Contains("inno setup") ||
                CheckForInnoSetup(peFile))
            {
                result.DetectedInstaller = InstallerType.InnoSetup;
                return;
            }

            // InstallShield detection
            if (imports.Any(d => d.Contains("isrt.dll") || d.Contains("iside.dll")) ||
                description.Contains("installshield"))
            {
                result.DetectedInstaller = InstallerType.InstallShield;
                return;
            }

            // WiX/MSI-based detection (but not a pure MSI)
            if (imports.Any(d => d.Contains("msi.dll")))
            {
                result.DetectedInstaller = InstallerType.WiX;
                return;
            }

            // Wise Installer detection
            if (description.Contains("wise") || imports.Any(d => d.Contains("wise")))
            {
                result.DetectedInstaller = InstallerType.Wise;
                return;
            }

            // Self-extracting archives
            if (productName.Contains("7-zip") || description.Contains("7-zip sfx"))
            {
                result.DetectedInstaller = InstallerType.SFX7Zip;
                return;
            }

            if (productName.Contains("winrar") || description.Contains("sfx"))
            {
                result.DetectedInstaller = InstallerType.SFXRAR;
                return;
            }

            // Check for generic "setup" or "installer" in the filename
            var fileName = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
            if (fileName.Contains("setup") || fileName.Contains("install"))
            {
                // It's likely some kind of installer, but we don't know which
                // Keep as Unknown but the switches will still be attempted
            }
        }
        catch
        {
            // Detection failed, leave as Unknown
        }
    }

    private static bool CheckForInnoSetup(PeFile peFile)
    {
        try
        {
            // Inno Setup installers typically have specific overlay data
            // Check for common Inno Setup section patterns
            var sections = peFile.ImageSectionHeaders;
            if (sections != null)
            {
                // Inno Setup often has CODE, DATA, BSS sections with specific characteristics
                var hasCodeSection = sections.Any(s => s.Name?.Trim('\0') == "CODE");
                var hasDataSection = sections.Any(s => s.Name?.Trim('\0') == "DATA");

                if (hasCodeSection && hasDataSection)
                {
                    // This pattern is common in Delphi/Inno Setup executables
                    return true;
                }
            }
        }
        catch
        {
            // Ignore detection errors
        }

        return false;
    }

    private static void SetSuggestedSwitches(AnalysisResult result)
    {
        result.SuggestedSilentSwitch = result.DetectedInstaller switch
        {
            InstallerType.NSIS => "/S",
            InstallerType.InnoSetup => "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
            InstallerType.InstallShield => "/s /v\"/qn\"",
            InstallerType.WiX => "/quiet /norestart",
            InstallerType.MSI => "/qn /norestart",
            InstallerType.Wise => "/S",
            InstallerType.SFX7Zip => "-y",
            InstallerType.SFXRAR => "-s",
            InstallerType.Unknown => "/S /silent /quiet",  // Try common switches
            _ => null
        };

        result.SuggestedUninstallSwitch = result.DetectedInstaller switch
        {
            InstallerType.NSIS => "/S",
            InstallerType.InnoSetup => "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
            InstallerType.InstallShield => "/s /v\"/qn\"",
            InstallerType.WiX => "/quiet /norestart",
            InstallerType.MSI => "/qn /norestart",
            InstallerType.Wise => "/S",
            _ => null
        };
    }

    private static string? CleanString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // Remove null characters and trim
        return value.Replace("\0", "").Trim();
    }

    /// <summary>
    /// Attempts to extract the best available app name from PE file analysis.
    /// </summary>
    public static string? GetBestAppName(AnalysisResult result)
    {
        // Prefer ProductName, fall back to FileDescription
        return !string.IsNullOrWhiteSpace(result.ProductName)
            ? result.ProductName
            : result.FileDescription;
    }

    /// <summary>
    /// Attempts to extract version string, preferring ProductVersion over FileVersion.
    /// </summary>
    public static string? GetBestVersion(AnalysisResult result)
    {
        // Prefer ProductVersion, fall back to FileVersion
        var version = !string.IsNullOrWhiteSpace(result.ProductVersion)
            ? result.ProductVersion
            : result.FileVersion;

        // Clean up version string (remove extra info after space)
        if (!string.IsNullOrEmpty(version) && version.Contains(' '))
        {
            version = version.Split(' ')[0];
        }

        return version;
    }
}
