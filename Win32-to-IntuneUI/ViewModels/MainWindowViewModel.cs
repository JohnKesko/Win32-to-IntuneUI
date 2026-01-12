using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
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
    /// Upload a single package that was just created
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUploadSinglePackage))]
    private async Task UploadSinglePackage()
    {
        var packagePath = SinglePackage.GetLastCreatedPackagePath();
        if (packagePath == null)
        {
            SinglePackage.AppendLog("ERROR: No .intunewin package found. Please create a package first.");
            return;
        }

        var displayName = Path.GetFileNameWithoutExtension(SinglePackage.SetupFile);
        IntuneUpload.PopulateFromSinglePackage(packagePath, displayName);

        var dialog = new IntuneUploadDialog
        {
            DataContext = IntuneUpload
        };

        if (MainWindow != null)
        {
            await dialog.ShowDialog(MainWindow);
        }
    }

    private bool CanUploadSinglePackage()
    {
        return SinglePackage.GetLastCreatedPackagePath() != null && !SinglePackage.IsProcessing;
    }

    /// <summary>
    /// Show upload dialog for batch-processed packages
    /// </summary>
    [RelayCommand]
    private async Task ShowIntuneUploadDialog()
    {
        IntuneUpload.PopulateFromBatchResults(BatchProcessing.BatchCandidates);

        if (IntuneUpload.UploadCandidates.Count == 0)
        {
            BatchProcessing.AppendLog("No packages available to upload");
            return;
        }

        var dialog = new IntuneUploadDialog
        {
            DataContext = IntuneUpload
        };

        if (MainWindow != null)
        {
            await dialog.ShowDialog(MainWindow);
        }
    }
}
