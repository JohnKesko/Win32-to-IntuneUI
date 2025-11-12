using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Win32_to_IntuneUI.Services;

public class IntuneToolDownloader
{
    private const string GithubRepoUrl = "https://github.com/microsoft/Microsoft-Win32-Content-Prep-Tool";
    private const string RawFileUrl = "https://raw.githubusercontent.com/microsoft/Microsoft-Win32-Content-Prep-Tool/master/IntuneWinAppUtil.exe";

    private static readonly string ToolDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Win32-to-IntuneUI",
        "tools"
    );

    private static readonly string ToolPath = Path.Combine(ToolDirectory, "IntuneWinAppUtil.exe");

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<int>? ProgressChanged;

    /// <summary>
    /// Gets the path to the IntuneWinAppUtil.exe tool
    /// </summary>
    public string GetToolPath() => ToolPath;

    /// <summary>
    /// Checks if the tool exists locally
    /// </summary>
    public bool IsToolAvailable() => File.Exists(ToolPath);

    /// <summary>
    /// Ensures the tool is available, downloading it if necessary
    /// </summary>
    public async Task<bool> EnsureToolAvailableAsync()
    {
        if (IsToolAvailable())
        {
            OnStatusChanged("IntuneWinAppUtil.exe found locally");
            return true;
        }

        OnStatusChanged("IntuneWinAppUtil.exe not found, downloading from GitHub...");
        return await DownloadToolAsync();
    }

    /// <summary>
    /// Downloads the latest IntuneWinAppUtil.exe from GitHub
    /// </summary>
    public async Task<bool> DownloadToolAsync()
    {
        try
        {
            // Ensure directory exists
            if (!Directory.Exists(ToolDirectory))
            {
                Directory.CreateDirectory(ToolDirectory);
                OnStatusChanged($"Created directory: {ToolDirectory}");
            }

            OnStatusChanged("Connecting to GitHub...");

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Win32-to-IntuneUI");

            // Get the file with progress tracking
            using var response = await httpClient.GetAsync(RawFileUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            OnStatusChanged($"Downloading IntuneWinAppUtil.exe ({FormatBytes(totalBytes)})...");

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(ToolPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalBytesRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalBytesRead += bytesRead;

                if (totalBytes > 0)
                {
                    var progress = (int)((totalBytesRead * 100) / totalBytes);
                    OnProgressChanged(progress);
                }
            }

            OnStatusChanged($"Successfully downloaded IntuneWinAppUtil.exe to: {ToolPath}");
            OnProgressChanged(100);

            // Make the file executable on Unix-like systems (macOS/Linux)
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"+x \"{ToolPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    await process.WaitForExitAsync();
                }
                catch (Exception ex)
                {
                    OnStatusChanged($"Warning: Could not make file executable: {ex.Message}");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            OnStatusChanged($"Error downloading tool: {ex.Message}");

            // Clean up partial download
            if (File.Exists(ToolPath))
            {
                try
                {
                    File.Delete(ToolPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Deletes the locally cached tool (useful for forcing a re-download)
    /// </summary>
    public bool DeleteLocalTool()
    {
        try
        {
            if (File.Exists(ToolPath))
            {
                File.Delete(ToolPath);
                OnStatusChanged("Local tool deleted");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            OnStatusChanged($"Error deleting tool: {ex.Message}");
            return false;
        }
    }

    private void OnStatusChanged(string status)
    {
        StatusChanged?.Invoke(this, status);
    }

    private void OnProgressChanged(int progress)
    {
        ProgressChanged?.Invoke(this, progress);
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}
