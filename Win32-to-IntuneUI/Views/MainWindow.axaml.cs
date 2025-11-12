using System;
using Avalonia.Controls;
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
}