using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Win32_to_IntuneUI.Services;

/// <summary>
/// Handles application auto-updates via Velopack and GitHub Releases.
/// Uses UpdateConfig for centralized configuration.
/// </summary>
public class UpdateService
{
    private UpdateManager? _updateManager;
    private UpdateInfo? _pendingUpdate;

    /// <summary>
    /// Whether the app is running as an installed Velopack application
    /// </summary>
    public bool IsInstalled => _updateManager?.IsInstalled ?? false;

    /// <summary>
    /// Whether an update has been downloaded and is ready to apply
    /// </summary>
    public bool IsUpdateReady => _pendingUpdate != null;

    /// <summary>
    /// Version string of the pending update
    /// </summary>
    public string? PendingVersion => _pendingUpdate?.TargetFullRelease?.Version?.ToString();

    /// <summary>
    /// Initialize the update manager. Call this once at app startup.
    /// </summary>
    public void Initialize()
    {
        try
        {
            var token = UpdateConfig.HasValidToken ? UpdateConfig.GitHubToken : null;
            var source = new GithubSource(UpdateConfig.GitHubRepoUrl, token, false);
            _updateManager = new UpdateManager(source);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize UpdateManager: {ex.Message}");
        }
    }

    /// <summary>
    /// Check for updates and download if available.
    /// </summary>
    /// <param name="onStatusChanged">Callback for status updates</param>
    /// <returns>True if an update was downloaded and is ready to apply</returns>
    public async Task<bool> CheckAndDownloadAsync(Action<string>? onStatusChanged = null)
    {
        if (_updateManager == null || !_updateManager.IsInstalled)
        {
            return false;
        }

        try
        {
            onStatusChanged?.Invoke("Checking for updates...");

            var updateInfo = await _updateManager.CheckForUpdatesAsync();
            if (updateInfo == null)
            {
                return false;
            }

            onStatusChanged?.Invoke($"Downloading v{updateInfo.TargetFullRelease.Version}...");

            await _updateManager.DownloadUpdatesAsync(updateInfo);

            _pendingUpdate = updateInfo;
            onStatusChanged?.Invoke($"Update v{updateInfo.TargetFullRelease.Version} ready");

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Apply the pending update and restart the application.
    /// </summary>
    public void ApplyUpdateAndRestart()
    {
        if (_updateManager != null && _pendingUpdate != null)
        {
            _updateManager.ApplyUpdatesAndRestart(_pendingUpdate);
        }
    }
}
