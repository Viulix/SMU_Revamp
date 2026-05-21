using System;
using System.Threading.Tasks;

namespace SMU_Revamp.Services;

/// <summary>
/// Implementation of IDeviceDebugService for testing device connections.
/// Uses the existing Prober and Switch Matrix services to validate connectivity.
/// </summary>
public class DeviceDebugService : IDeviceDebugService
{
    private static readonly Lazy<DeviceDebugService> _instance = new(() => new DeviceDebugService());

    public static DeviceDebugService Instance => _instance.Value;

    private DeviceDebugService()
    {
    }

    /// <summary>
    /// Tests the connection to the Prober device by attempting to open a session.
    /// </summary>
    public async Task<string> TestProberConnectionAsync()
    {
        try
        {
            var session = new VisaGpibSession();
            await session.OpenAsync("GPIB0::22::INSTR", 5000);
            
            // Try to send a simple command to verify communication
            await session.WriteAsync("*RST");
            await Task.Delay(100);
            
            await session.CloseAsync();
            return "✓ Prober connection successful!\nResource: GPIB0::22::INSTR";
        }
        catch (Exception ex)
        {
            return "✗ Prober connection failed:\n" + ex.Message;
        }
    }

    /// <summary>
    /// Tests the connection to the Switch Matrix device by attempting to open a session.
    /// </summary>
    public async Task<string> TestSwitchMatrixConnectionAsync()
    {
        try
        {
            var session = new VisaGpibSession();
            await session.OpenAsync("GPIB0::23::INSTR", 5000);
            
            // Try to send a simple command to verify communication
            await session.WriteAsync("*RST");
            await Task.Delay(100);
            
            await session.CloseAsync();
            return "✓ Switch Matrix connection successful!\nResource: GPIB0::23::INSTR";
        }
        catch (Exception ex)
        {
            return "✗ Switch Matrix connection failed:\n" + ex.Message;
        }
    }

    /// <summary>
    /// Queries the Prober device identity.
    /// </summary>
    public async Task<string> QueryProberIdentityAsync()
    {
        try
        {
            var session = new VisaGpibSession();
            await session.OpenAsync("GPIB0::22::INSTR", 5000);
            
            await session.WriteAsync("*IDN?");
            await Task.Delay(100);
            var response = await session.ReadAsync(256);
            
            await session.CloseAsync();
            return "✓ Prober Identity:\n" + response?.Trim();
        }
        catch (Exception ex)
        {
            return "✗ Query failed:\n" + ex.Message;
        }
    }

    /// <summary>
    /// Queries the Switch Matrix device identity.
    /// </summary>
    public async Task<string> QuerySwitchMatrixIdentityAsync()
    {
        try
        {
            var session = new VisaGpibSession();
            await session.OpenAsync("GPIB0::23::INSTR", 5000);
            
            await session.WriteAsync("*IDN?");
            await Task.Delay(100);
            var response = await session.ReadAsync(256);
            
            await session.CloseAsync();
            return "✓ Switch Matrix Identity:\n" + response?.Trim();
        }
        catch (Exception ex)
        {
            return "✗ Query failed:\n" + ex.Message;
        }
    }
}
