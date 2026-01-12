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
}
