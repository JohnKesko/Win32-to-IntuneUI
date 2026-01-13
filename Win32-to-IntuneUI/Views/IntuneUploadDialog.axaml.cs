using Avalonia.Controls;
using Avalonia.Interactivity;
using Win32_to_IntuneUI.Models;

namespace Win32_to_IntuneUI.Views;

public partial class IntuneUploadDialog : Window
{
    public IntuneUploadDialog()
    {
        InitializeComponent();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void EditPackageDetails_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is IntuneUploadCandidate candidate)
        {
            var dialog = new PackageDetailsDialog(candidate);
            await dialog.ShowDialog(this);
        }
    }
}
