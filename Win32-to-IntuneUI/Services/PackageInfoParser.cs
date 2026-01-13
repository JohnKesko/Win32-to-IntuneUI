using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Win32_to_IntuneUI.Models;

namespace Win32_to_IntuneUI.Services;

/// <summary>
/// Parses package information from .txt files commonly used in enterprise packaging.
/// Supports standard package info format with sections like [Application information], 
/// [Package information], and [Detection Rule N].
/// </summary>
public static class PackageInfoParser
{
    /// <summary>
    /// Parsed package information from a package info file
    /// </summary>
    public class PackageInfo
    {
        // Application information
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Architecture { get; set; }
        public string? Language { get; set; }
        public string? Publisher { get; set; }
        public string? AppDbId { get; set; }

        // Package information
        public string? PackageVersion { get; set; }
        public string? InstallCommand { get; set; }
        public string? UninstallCommand { get; set; }
        public string? ProductId { get; set; }
        public string? Description { get; set; }
        public string? Prerequisites { get; set; }
        public bool RestartRequired { get; set; }

        // Detection rules
        public List<DetectionRule> DetectionRules { get; set; } = new();
    }

    /// <summary>
    /// Attempts to find and parse a package info file in the specified folder.
    /// Looks for .txt files that contain package information sections.
    /// </summary>
    public static PackageInfo? TryParseFromFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return null;

        // Look for .txt files in the folder
        var txtFiles = Directory.GetFiles(folderPath, "*.txt", SearchOption.TopDirectoryOnly);

        foreach (var txtFile in txtFiles)
        {
            try
            {
                var content = File.ReadAllText(txtFile);

                // Check if this looks like a package info file
                if (content.Contains("[Application information]", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("[Package information]", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("[Detection Rule", StringComparison.OrdinalIgnoreCase))
                {
                    var parsed = Parse(content);
                    if (parsed != null && !string.IsNullOrEmpty(parsed.Name))
                    {
                        return parsed;
                    }
                }
            }
            catch
            {
                // Skip files that can't be read
            }
        }

        return null;
    }

    /// <summary>
    /// Parses package info content from a string
    /// </summary>
    public static PackageInfo? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var info = new PackageInfo();
        var lines = content.Split('\n', StringSplitOptions.None);
        string? currentSection = null;
        int? currentRuleNumber = null;
        DetectionRule? currentRule = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Check for section headers
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                // Save previous detection rule if we were parsing one
                if (currentRule != null && currentRuleNumber.HasValue)
                {
                    info.DetectionRules.Add(currentRule);
                }

                currentSection = line.ToLowerInvariant();

                // Check if this is a detection rule section
                var ruleMatch = Regex.Match(line, @"\[Detection Rule (\d+)\]", RegexOptions.IgnoreCase);
                if (ruleMatch.Success)
                {
                    currentRuleNumber = int.Parse(ruleMatch.Groups[1].Value);
                    currentRule = new DetectionRule();
                }
                else
                {
                    currentRuleNumber = null;
                    currentRule = null;
                }

                continue;
            }

            // Parse key-value pairs (supports "Key - Value" and "Key- Value" formats)
            var kvMatch = Regex.Match(line, @"^([^-]+?)\s*[-–—]\s*(.*)$");
            if (!kvMatch.Success)
                continue;

            var key = kvMatch.Groups[1].Value.Trim().ToLowerInvariant();
            var value = kvMatch.Groups[2].Value.Trim();

            // Skip empty values
            if (string.IsNullOrWhiteSpace(value))
                continue;

            // Parse based on current section
            if (currentSection?.Contains("application information") == true)
            {
                ParseApplicationInfo(info, key, value);
            }
            else if (currentSection?.Contains("package information") == true)
            {
                ParsePackageInfo(info, key, value);
            }
            else if (currentRule != null)
            {
                ParseDetectionRule(currentRule, key, value);
            }
        }

        // Don't forget the last detection rule
        if (currentRule != null)
        {
            info.DetectionRules.Add(currentRule);
        }

        // Build description from additional info
        if (!string.IsNullOrEmpty(info.Prerequisites))
        {
            info.Description = $"Prerequisites: {info.Prerequisites}";
        }

        return info;
    }

    private static void ParseApplicationInfo(PackageInfo info, string key, string value)
    {
        switch (key)
        {
            case "name":
                info.Name = value;
                break;
            case "version":
                info.Version = value;
                break;
            case "architecture":
                info.Architecture = value;
                break;
            case "language":
                info.Language = value;
                break;
            case "publisher":
                info.Publisher = value;
                break;
            case "appdb_id":
                info.AppDbId = value;
                break;
        }
    }

    private static void ParsePackageInfo(PackageInfo info, string key, string value)
    {
        // Normalize key: remove underscores, fix common OCR typos
        var normalizedKey = key
            .Replace("_", "")
            .Replace("11ne", "line") // OCR typo
            .Replace("1ine", "line"); // OCR typo

        switch (normalizedKey)
        {
            case "packageversion":
                info.PackageVersion = value;
                break;
            case "installationcdline":
            case "installationcmdline":
            case "osinstallationcmdline":
                // Use the first install command found
                if (string.IsNullOrEmpty(info.InstallCommand))
                    info.InstallCommand = value;
                break;
            case "uninstallationcmdline":
                info.UninstallCommand = value;
                break;
            case "productid":
                info.ProductId = CleanProductId(value);
                break;
            case "restartrequired":
                info.RestartRequired = value.Equals("yes", StringComparison.OrdinalIgnoreCase);
                break;
            case "prerequisites":
                info.Prerequisites = value;
                break;
            case "additionalinformation":
                if (string.IsNullOrEmpty(info.Description))
                    info.Description = value;
                break;
        }
    }

    private static void ParseDetectionRule(DetectionRule rule, string key, string value)
    {
        switch (key)
        {
            case "type":
                rule.Type = ParseDetectionType(value);
                break;
            case "is32on64bit":
            case "check32biton64system":
                rule.Check32BitOn64System = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                break;
            case "hive":
                rule.Hive = value.ToLowerInvariant() switch
                {
                    "localmachine" => "localMachine",
                    "currentuser" => "currentUser",
                    _ => value
                };
                break;
            case "keypath":
                // Clean up the key path and handle typos
                rule.KeyPath = value
                    .Replace("cucrentversion", "currentversion") // Fix common typo
                    .Trim();
                break;
            case "valuename":
                rule.ValueName = value;
                break;
            case "operator":
                rule.Operator = value.ToLowerInvariant();
                rule.DetectionMethod = value.ToLowerInvariant() switch
                {
                    "exists" => "exists",
                    "notexists" => "notExists",
                    _ => value.ToLowerInvariant()
                };
                break;
            case "path":
                rule.Path = value;
                break;
            case "fileorfoldername":
                rule.FileOrFolderName = value;
                break;
            case "productcode":
                rule.ProductCode = CleanProductId(value);
                break;
            case "productversion":
                rule.ProductVersion = value;
                break;
        }
    }

    private static string ParseDetectionType(string value)
    {
        var lower = value.ToLowerInvariant();

        if (lower.Contains("registry"))
            return "registry";
        if (lower.Contains("file") || lower.Contains("folder"))
            return "file";
        if (lower.Contains("msi") || lower.Contains("product"))
            return "msi";
        if (lower.Contains("script") || lower.Contains("powershell"))
            return "script";

        return "registry"; // Default to registry as it's most common
    }

    private static string CleanProductId(string value)
    {
        // Normalize braces - convert (PKG-xxx) to {PKG-xxx}
        var cleaned = value
            .Replace("(", "{")
            .Replace(")", "}")
            .Trim();

        // Ensure it has braces
        if (!cleaned.StartsWith("{"))
            cleaned = "{" + cleaned;
        if (!cleaned.EndsWith("}"))
            cleaned = cleaned + "}";

        return cleaned;
    }

    /// <summary>
    /// Converts parsed PackageInfo to an IntuneConfigFile for compatibility
    /// </summary>
    public static IntuneConfigFile ToIntuneConfig(PackageInfo info)
    {
        var config = new IntuneConfigFile
        {
            DisplayName = !string.IsNullOrEmpty(info.Version)
                ? $"{info.Name} {info.Version}"
                : info.Name,
            Publisher = info.Publisher,
            Description = info.Description,
            InstallCommand = info.InstallCommand,
            UninstallCommand = info.UninstallCommand,
            DetectionRules = info.DetectionRules.Count > 0 ? info.DetectionRules : null
        };

        return config;
    }
}
