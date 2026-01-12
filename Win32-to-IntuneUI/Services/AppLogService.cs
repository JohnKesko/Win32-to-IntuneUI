using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Win32_to_IntuneUI.Services;

/// <summary>
/// Centralized logging service for the application.
/// All log messages are routed through this service.
/// </summary>
public class AppLogService
{
    private static readonly Lazy<AppLogService> _instance = new(() => new AppLogService());
    public static AppLogService Instance => _instance.Value;

    private readonly StringBuilder _logBuilder = new();
    private readonly object _logLock = new();

    /// <summary>
    /// Event raised when new log content is added
    /// </summary>
    public event EventHandler<string>? LogUpdated;

    /// <summary>
    /// Current log content
    /// </summary>
    public string LogContent
    {
        get
        {
            lock (_logLock)
            {
                return _logBuilder.ToString();
            }
        }
    }

    private AppLogService() { }

    /// <summary>
    /// Log a message with timestamp
    /// </summary>
    public void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var formattedMessage = $"[{timestamp}] {message}";

        lock (_logLock)
        {
            _logBuilder.AppendLine(formattedMessage);
        }

        LogUpdated?.Invoke(this, LogContent);
    }

    /// <summary>
    /// Log a message with a category prefix
    /// </summary>
    public void Log(string category, string message)
    {
        Log($"[{category}] {message}");
    }

    /// <summary>
    /// Clear all log content
    /// </summary>
    public void Clear()
    {
        lock (_logLock)
        {
            _logBuilder.Clear();
        }

        LogUpdated?.Invoke(this, string.Empty);
    }

    /// <summary>
    /// Export log to a file
    /// </summary>
    public async Task<bool> ExportToFileAsync(string filePath)
    {
        try
        {
            var content = LogContent;
            await File.WriteAllTextAsync(filePath, content);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
