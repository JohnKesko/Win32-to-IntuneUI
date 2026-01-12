using System;
using System.Reflection;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Win32_to_IntuneUI.Services;

public class UpdateService
{
    // TODO: Update this to your actual GitHub repository
    private const string GitHubRepoUrl = "https://github.com/JohnKesko/Win32-to-IntuneUI";

    private readonly UpdateManager _updateManager;
    private UpdateInfo? _updateInfo;

    /// <summary>
    /// Gets the current app version from Velopack (if installed) or from assembly
    /// </summary>
    public string CurrentVersion =>
        _updateManager.CurrentVersion?.ToString()
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "1.0.0";

    /// <summary>
    /// Gets the version from the assembly (defined in .csproj)
    /// </summary>
    public static string AssemblyVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public bool IsUpdateAvailable => _updateInfo?.TargetFullRelease != null;
    public string? NewVersion => _updateInfo?.TargetFullRelease?.Version.ToString();

    public UpdateService()
    {
        // Use GitHub Releases as the update source
        var source = new GithubSource(GitHubRepoUrl, null, false);
        _updateManager = new UpdateManager(source);
    }

    /// <summary>
    /// Check for updates from GitHub Releases
    /// </summary>
    /// <returns>True if an update is available</returns>
    public async Task<bool> CheckForUpdatesAsync()
    {
        try
        {
            // Don't check for updates in development/debug mode
            if (!_updateManager.IsInstalled)
            {
                return false;
            }

            _updateInfo = await _updateManager.CheckForUpdatesAsync();
            return _updateInfo?.TargetFullRelease != null;
        }
        catch (Exception)
        {
            // Silently fail - update checking should not break the app
            return false;
        }
    }

    /// <summary>
    /// Download and apply the update, then restart the application
    /// </summary>
    public async Task<bool> DownloadAndApplyUpdateAsync(Action<int>? progressCallback = null)
    {
        if (_updateInfo?.TargetFullRelease == null)
            return false;

        try
        {
            // Download the update
            await _updateManager.DownloadUpdatesAsync(
                _updateInfo,
                progress => progressCallback?.Invoke(progress));

            // Apply update and restart
            _updateManager.ApplyUpdatesAndRestart(_updateInfo);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Download update without applying (apply on next restart)
    /// </summary>
    public async Task<bool> DownloadUpdateAsync(Action<int>? progressCallback = null)
    {
        if (_updateInfo?.TargetFullRelease == null)
            return false;

        try
        {
            await _updateManager.DownloadUpdatesAsync(
                _updateInfo,
                progress => progressCallback?.Invoke(progress));

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Apply downloaded updates and restart
    /// </summary>
    public void ApplyUpdatesAndRestart()
    {
        if (_updateInfo != null)
        {
            _updateManager.ApplyUpdatesAndRestart(_updateInfo);
        }
    }

    /// <summary>
    /// Wait for the update to be applied on next launch (no restart)
    /// </summary>
    public void ApplyUpdatesOnExit()
    {
        if (_updateInfo != null)
        {
            _updateManager.ApplyUpdatesAndExit(_updateInfo);
        }
    }
}
