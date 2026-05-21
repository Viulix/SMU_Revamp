using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SMU_Revamp.Services;

namespace SMU_Revamp.Views;

public partial class DebugWindow : Window
{
    private readonly IDeviceDebugService _debugService;

    public DebugWindow()
    {
        InitializeComponent();
        _debugService = DeviceDebugService.Instance;
    }

    private async void TestProberConnection_Click(object? sender, RoutedEventArgs e)
    {
        ProberOutputTextBox.Text = "Testing Prober connection...";
        var result = await _debugService.TestProberConnectionAsync();
        ProberOutputTextBox.Text = result;
    }

    private async void QueryProberIdentity_Click(object? sender, RoutedEventArgs e)
    {
        ProberOutputTextBox.Text = "Querying Prober identity...";
        var result = await _debugService.QueryProberIdentityAsync();
        ProberOutputTextBox.Text = result;
    }

    private async void TestSwitchConnection_Click(object? sender, RoutedEventArgs e)
    {
        SwitchOutputTextBox.Text = "Testing Switch Matrix connection...";
        var result = await _debugService.TestSwitchMatrixConnectionAsync();
        SwitchOutputTextBox.Text = result;
    }

    private async void QuerySwitchIdentity_Click(object? sender, RoutedEventArgs e)
    {
        SwitchOutputTextBox.Text = "Querying Switch Matrix identity...";
        var result = await _debugService.QuerySwitchMatrixIdentityAsync();
        SwitchOutputTextBox.Text = result;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
