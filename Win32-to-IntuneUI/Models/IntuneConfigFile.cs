using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Win32_to_IntuneUI.Models;

/// <summary>
/// Configuration file format for specifying installer and package settings per folder.
/// Place a file named "intuneconfig.json" in each application folder.
/// </summary>
public class IntuneConfigFile
{
    /// <summary>
    /// Relative path to the installer file (e.g., "setup.exe" or "installers/setup.msi")
    /// </summary>
    [JsonPropertyName("installer")]
    public string? Installer { get; set; }

    /// <summary>
    /// Display name for the app in Intune (optional, defaults to folder name)
    /// </summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Application version
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>
    /// Description for the app in Intune
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Publisher name
    /// </summary>
    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }

    /// <summary>
    /// Custom install command line (optional)
    /// </summary>
    [JsonPropertyName("installCommand")]
    public string? InstallCommand { get; set; }

    /// <summary>
    /// Custom uninstall command line (optional)
    /// </summary>
    [JsonPropertyName("uninstallCommand")]
    public string? UninstallCommand { get; set; }

    /// <summary>
    /// Whether to skip this folder during batch processing
    /// </summary>
    [JsonPropertyName("skip")]
    public bool Skip { get; set; }

    /// <summary>
    /// Detection rules for the application (optional)
    /// </summary>
    [JsonPropertyName("detectionRules")]
    public List<DetectionRule>? DetectionRules { get; set; }
}

/// <summary>
/// Base detection rule configuration
/// </summary>
public class DetectionRule
{
    /// <summary>
    /// Type of detection rule: "registry", "file", "msi", or "script"
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "file";

    // Registry detection properties
    /// <summary>
    /// Registry hive: "localMachine", "currentUser"
    /// </summary>
    [JsonPropertyName("hive")]
    public string? Hive { get; set; }

    /// <summary>
    /// Registry key path (e.g., "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{GUID}")
    /// </summary>
    [JsonPropertyName("keyPath")]
    public string? KeyPath { get; set; }

    /// <summary>
    /// Registry value name (optional - if empty, checks key existence)
    /// </summary>
    [JsonPropertyName("valueName")]
    public string? ValueName { get; set; }

    /// <summary>
    /// Detection method: "exists", "notExists", "string", "integer", "version"
    /// </summary>
    [JsonPropertyName("detectionMethod")]
    public string? DetectionMethod { get; set; }

    /// <summary>
    /// Comparison operator for value detection: "equal", "notEqual", "greaterThan", "greaterThanOrEqual", "lessThan", "lessThanOrEqual"
    /// </summary>
    [JsonPropertyName("operator")]
    public string? Operator { get; set; }

    /// <summary>
    /// Expected value for comparison
    /// </summary>
    [JsonPropertyName("detectionValue")]
    public string? DetectionValue { get; set; }

    /// <summary>
    /// Check 32-bit registry on 64-bit systems
    /// </summary>
    [JsonPropertyName("check32BitOn64System")]
    public bool Check32BitOn64System { get; set; }

    // File detection properties
    /// <summary>
    /// File/folder path (e.g., "%ProgramFiles%\\MyApp")
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>
    /// File or folder name
    /// </summary>
    [JsonPropertyName("fileOrFolderName")]
    public string? FileOrFolderName { get; set; }

    // MSI detection properties
    /// <summary>
    /// MSI product code (GUID)
    /// </summary>
    [JsonPropertyName("productCode")]
    public string? ProductCode { get; set; }

    /// <summary>
    /// Product version for comparison
    /// </summary>
    [JsonPropertyName("productVersion")]
    public string? ProductVersion { get; set; }

    /// <summary>
    /// Version comparison operator: "notConfigured", "equal", "notEqual", "greaterThan", etc.
    /// </summary>
    [JsonPropertyName("productVersionOperator")]
    public string? ProductVersionOperator { get; set; }

    // Script detection properties
    /// <summary>
    /// PowerShell script content for custom detection
    /// </summary>
    [JsonPropertyName("scriptContent")]
    public string? ScriptContent { get; set; }

    /// <summary>
    /// Run script as 32-bit process
    /// </summary>
    [JsonPropertyName("runAs32Bit")]
    public bool RunAs32Bit { get; set; }

    /// <summary>
    /// Enforce script signature check
    /// </summary>
    [JsonPropertyName("enforceSignatureCheck")]
    public bool EnforceSignatureCheck { get; set; }
}
