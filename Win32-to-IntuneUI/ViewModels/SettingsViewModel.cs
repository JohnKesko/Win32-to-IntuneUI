using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Win32_to_IntuneUI.Services;

namespace Win32_to_IntuneUI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _logOutput = string.Empty;

    public Window? MainWindow { get; set; }

    public SettingsViewModel()
    {
        // Subscribe to log updates
        AppLogService.Instance.LogUpdated += OnLogUpdated;

        // Initialize with current log content
        LogOutput = AppLogService.Instance.LogContent;
    }

    private void OnLogUpdated(object? sender, string logContent)
    {
        LogOutput = logContent;
    }

    [RelayCommand]
    private void ClearLog()
    {
        AppLogService.Instance.Clear();
    }

    [RelayCommand]
    private async Task ExportLog()
    {
        if (MainWindow == null) return;

        var storageProvider = MainWindow.StorageProvider;
        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Log",
            SuggestedFileName = $"Win32-to-IntuneUI-Log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Text File")
                {
                    Patterns = new[] { "*.txt" }
                }
            }
        });

        if (file != null)
        {
            var success = await AppLogService.Instance.ExportToFileAsync(file.Path.LocalPath);
            if (success)
            {
                AppLogService.Instance.Log("Settings", $"Log exported to {file.Path.LocalPath}");
            }
        }
    }
}
