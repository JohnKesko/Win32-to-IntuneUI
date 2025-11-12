using Avalonia.Controls;

namespace Win32_to_IntuneUI.Views;

public partial class IntuneUploadDialog : Window
{
    public IntuneUploadDialog()
    {
        InitializeComponent();
    }

    private void CancelButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
