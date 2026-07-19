using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SMU_Revamp.Views.Controls;

public partial class ResultEmptyStateControl : UserControl
{
    public event EventHandler<RoutedEventArgs>? LoadFolderRequested;
    public event EventHandler<RoutedEventArgs>? LoadDatabaseRequested;

    public ResultEmptyStateControl()
    {
        InitializeComponent();
    }

    private void OnLoadFolderClick(object? sender, RoutedEventArgs e)
    {
        LoadFolderRequested?.Invoke(this, e);
    }

    private void OnLoadDatabaseClick(object? sender, RoutedEventArgs e)
    {
        LoadDatabaseRequested?.Invoke(this, e);
    }
}
