using Avalonia.Controls;
using Avalonia.Interactivity;
using Win32_to_IntuneUI.Models;

namespace Win32_to_IntuneUI.Views;

public partial class PackageDetailsDialog : Window
{
    private readonly IntuneUploadCandidate _candidate;

    public PackageDetailsDialog()
    {
        InitializeComponent();
        _candidate = new IntuneUploadCandidate();
    }

    public PackageDetailsDialog(IntuneUploadCandidate candidate) : this()
    {
        _candidate = candidate;
        LoadCandidateData();
    }

    private void LoadCandidateData()
    {
        // Set header info
        PackageFileLabel.Text = _candidate.PackageFileName;

        // Application info
        DisplayNameTextBox.Text = _candidate.DisplayName;
        VersionTextBox.Text = _candidate.Version;
        PublisherTextBox.Text = _candidate.Publisher;
        DescriptionTextBox.Text = _candidate.Description;

        // Commands
        InstallCommandTextBox.Text = _candidate.InstallCommand;
        UninstallCommandTextBox.Text = _candidate.UninstallCommand;

        // Detection rules summary
        UpdateDetectionRulesSummary();

        // Package info (read-only)
        PackageFileNameLabel.Text = _candidate.PackageFileName;
        PackageSizeLabel.Text = _candidate.PackageFileSizeFormatted;
        SourceFolderLabel.Text = _candidate.FolderName;
    }

    private void UpdateDetectionRulesSummary()
    {
        DetectionRulesSummaryLabel.Text = _candidate.DetectionRulesSummary;
    }

    private async void EditDetectionRules_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new DetectionRulesEditorDialog(_candidate.DisplayName, _candidate.DetectionRules);
        await dialog.ShowDialog(this);

        if (dialog.Result != null)
        {
            _candidate.DetectionRules = dialog.Result;
            UpdateDetectionRulesSummary();
        }
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        // Apply changes back to the candidate
        _candidate.DisplayName = DisplayNameTextBox.Text ?? "";
        _candidate.Version = VersionTextBox.Text ?? "";
        _candidate.Publisher = PublisherTextBox.Text ?? "";
        _candidate.Description = DescriptionTextBox.Text ?? "";
        _candidate.InstallCommand = InstallCommandTextBox.Text ?? "";
        _candidate.UninstallCommand = UninstallCommandTextBox.Text ?? "";

        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
