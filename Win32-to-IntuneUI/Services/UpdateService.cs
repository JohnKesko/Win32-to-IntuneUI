using System;
using System.Threading;
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
    private const int TimeoutSeconds = 15;

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
        if (_updateManager == null)
        {
            onStatusChanged?.Invoke("Update manager not initialized");
            return false;
        }

        if (!_updateManager.IsInstalled)
        {
            // Not installed via Velopack - running in dev mode or extracted from ZIP
            onStatusChanged?.Invoke("Dev mode - updates disabled");
            return false;
        }

        // Check if token is configured for private repo
        var token = UpdateConfig.GitHubToken;
        var hasValidToken = !string.IsNullOrEmpty(token) && token != "__UPDATE_PAT_PLACEHOLDER__";
        
        if (!hasValidToken)
        {
            var tokenPreview = token.Length > 10
                ? $"{token[..4]}...{token[^4..]} (len={token.Length})"
                : $"(short, len={token.Length})";
            onStatusChanged?.Invoke($"Invalid token: {tokenPreview}");
            return false;
        }

        try
        {
            onStatusChanged?.Invoke("Checking for updates...");

            // Add timeout to prevent hanging
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));

            var checkTask = _updateManager.CheckForUpdatesAsync();
            var completedTask = await Task.WhenAny(checkTask, Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds), cts.Token));

            if (completedTask != checkTask)
            {
                onStatusChanged?.Invoke("Update check timed out");
                return false;
            }

            var updateInfo = await checkTask;
            if (updateInfo == null)
            {
                onStatusChanged?.Invoke("Up to date");
                return false;
            }

            onStatusChanged?.Invoke($"Downloading v{updateInfo.TargetFullRelease.Version}...");

            await _updateManager.DownloadUpdatesAsync(updateInfo);

            _pendingUpdate = updateInfo;
            onStatusChanged?.Invoke($"Update v{updateInfo.TargetFullRelease.Version} ready");

            return true;
        }
        catch (OperationCanceledException)
        {
            onStatusChanged?.Invoke("Update check timed out");
            return false;
        }
        catch (Exception ex)
        {
            var shortMessage = ex.Message.Length > 50 ? ex.Message[..50] + "..." : ex.Message;
            onStatusChanged?.Invoke($"Update failed: {shortMessage}");
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
