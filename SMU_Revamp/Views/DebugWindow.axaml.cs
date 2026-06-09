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

    private async void CreateSwitchConnection_Click(object? sender, RoutedEventArgs e)
    {
        var x = ConnectionXTextBox.Text ?? string.Empty;
        var y = ConnectionYTextBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(x) || string.IsNullOrWhiteSpace(y))
        {
            SwitchOutputTextBox.Text = "Error: Please specify endpoints X and Y to create a connection.";
            return;
        }

        SwitchOutputTextBox.Text = $"Creating connection between {x} and {y}...";
        var result = await _debugService.CreateSwitchMatrixConnectionAsync(x, y);
        SwitchOutputTextBox.Text = result;
    }

    private async void ReadSwitchConnection_Click(object? sender, RoutedEventArgs e)
    {
        SwitchOutputTextBox.Text = "Reading active Switch Matrix connections...";
        var result = await _debugService.ReadSwitchMatrixConnectionAsync();
        SwitchOutputTextBox.Text = result;
    }

    private async void TestSMUConnection_Click(object? sender, RoutedEventArgs e)
    {
        SmuOutputTextBox.Text = "Testing SMU connection...";
        var result = await _debugService.TestSMUConnectionAsync();
        SmuOutputTextBox.Text = result;
    }

    private async void QuerySMUIdentity_Click(object? sender, RoutedEventArgs e)
    {
        SmuOutputTextBox.Text = "Querying SMU identity...";
        var result = await _debugService.QuerySMUIdentityAsync();
        SmuOutputTextBox.Text = result;
    }

    private async void ForceSmuVoltage_Click(object? sender, RoutedEventArgs e)
    {
        var channel = SmuChannelTextBox.Text ?? string.Empty;
        var voltStr = (SmuVoltageTextBox.Text ?? string.Empty).Replace(',', '.');
        var compStr = (SmuComplianceTextBox.Text ?? string.Empty).Replace(',', '.');
        var durStr = (SmuDurationTextBox.Text ?? string.Empty).Replace(',', '.');

        if (string.IsNullOrWhiteSpace(channel))
        {
            SmuOutputTextBox.Text = "Error: Please specify a channel.";
            return;
        }

        if (!double.TryParse(voltStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double voltage))
        {
            SmuOutputTextBox.Text = "Error: Voltage must be a valid number.";
            return;
        }

        if (!double.TryParse(compStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double compliance))
        {
            SmuOutputTextBox.Text = "Error: Compliance must be a valid number.";
            return;
        }

        if (!double.TryParse(durStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double duration) || duration <= 0)
        {
            SmuOutputTextBox.Text = "Error: Duration must be a positive number.";
            return;
        }

        SmuOutputTextBox.Text = $"Connecting and forcing {voltage:F3} V on channel {channel} for {duration:F1} seconds...";
        var result = await _debugService.ForceSMUDCVoltageAsync(channel, voltage, compliance, duration);
        SmuOutputTextBox.Text = result;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
