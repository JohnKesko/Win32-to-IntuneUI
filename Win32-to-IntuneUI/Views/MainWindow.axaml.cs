using System;
using System.Globalization;
using System.IO;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Win32_to_IntuneUI.Models;
using Win32_to_IntuneUI.ViewModels;

namespace Win32_to_IntuneUI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Set the window reference in the ViewModel when DataContext changes
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.MainWindow = this;
        }
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

public class FileNameConverter : IValueConverter
{
    public static readonly FileNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path))
        {
            return Path.GetFileName(path);
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}