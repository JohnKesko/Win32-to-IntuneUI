using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Win32_to_IntuneUI.Views;

namespace Win32_to_IntuneUI.ViewModels;

/// <summary>
/// Main window ViewModel that orchestrates the specialized ViewModels for each feature area.
/// This keeps each ViewModel focused and maintainable.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>
    /// ViewModel for single package creation functionality
    /// </summary>
    public SinglePackageViewModel SinglePackage { get; }

    /// <summary>
    /// ViewModel for batch processing functionality
    /// </summary>
    public BatchProcessingViewModel BatchProcessing { get; }

    /// <summary>
    /// ViewModel for Intune upload functionality
    /// </summary>
    public IntuneUploadViewModel IntuneUpload { get; }

    private Window? _mainWindow;
    public Window? MainWindow
    {
        get => _mainWindow;
        set
        {
            _mainWindow = value;
            // Propagate window reference to child ViewModels
            SinglePackage.MainWindow = value;
            BatchProcessing.MainWindow = value;
            IntuneUpload.MainWindow = value;
        }
    }

    public MainWindowViewModel()
    {
        SinglePackage = new SinglePackageViewModel();
        BatchProcessing = new BatchProcessingViewModel();
        IntuneUpload = new IntuneUploadViewModel();

        // Subscribe to events from child ViewModels
        BatchProcessing.BatchCompleted += (_, candidates) =>
        {
            // Populate upload candidates when batch processing completes
            IntuneUpload.PopulateFromBatchResults(candidates);
        };
    }

    /// <summary>
    /// Upload packages to Intune - uses recently created package(s) or allows browsing for .intunewin files.
    /// This is the unified upload method used by both Single Package and Batch Processing tabs.
    /// Credentials are shared across all uploads via the IntuneUpload ViewModel.
    /// </summary>
    [RelayCommand]
    private async Task UploadToIntune()
    {
        if (MainWindow == null) return;

        // First, check for recently created package from Single Package tab
        var singlePackagePath = SinglePackage.GetLastCreatedPackagePath();

        // Then check for batch results
        var hasBatchResults = BatchProcessing.BatchCandidates.Any(c =>
            !string.IsNullOrEmpty(c.OutputFilePath) && File.Exists(c.OutputFilePath!));

        if (singlePackagePath != null)
        {
            // Pre-populate with the single package
            IntuneUpload.PopulateFromSinglePackage(singlePackagePath,
                Path.GetFileNameWithoutExtension(singlePackagePath));
            SinglePackage.AppendLog($"Ready to upload: {Path.GetFileName(singlePackagePath)}");
        }
        else if (hasBatchResults)
        {
            // Pre-populate with batch results
            IntuneUpload.PopulateFromBatchResults(BatchProcessing.BatchCandidates);
            BatchProcessing.AppendLog($"{IntuneUpload.UploadCandidates.Count} package(s) ready for upload");
        }
        else
        {
            // No recent packages - let user browse for .intunewin files
            var files = await MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select .intunewin Package(s)",
                AllowMultiple = true,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("Intune Win32 Package") { Patterns = new[] { "*.intunewin" } },
                    FilePickerFileTypes.All
                }
            });

            if (files.Count == 0) return;

            IntuneUpload.PopulateFromFiles(files.Select(f => f.Path.LocalPath).ToList());
        }

        var dialog = new IntuneUploadDialog
        {
            DataContext = IntuneUpload
        };

        await dialog.ShowDialog(MainWindow);
    }
}
