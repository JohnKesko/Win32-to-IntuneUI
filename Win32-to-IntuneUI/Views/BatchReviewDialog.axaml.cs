using Avalonia.Controls;
using Avalonia.Interactivity;
using Win32_to_IntuneUI.ViewModels;

namespace Win32_to_IntuneUI.Views;

public partial class BatchReviewDialog : Window
{
    public BatchReviewDialog()
    {
        InitializeComponent();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ContinueButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
