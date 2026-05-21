using System.Threading.Tasks;

namespace SMU_Revamp.Services;

/// <summary>
/// Service for debugging and testing device connections.
/// Provides methods to test connectivity to the Prober and Switch Matrix devices.
/// </summary>
public interface IDeviceDebugService
{
    /// <summary>
    /// Tests the connection to the Prober device.
    /// </summary>
    /// <returns>A string describing the connection result (success or error details).</returns>
    Task<string> TestProberConnectionAsync();

    /// <summary>
    /// Tests the connection to the Switch Matrix device.
    /// </summary>
    /// <returns>A string describing the connection result (success or error details).</returns>
    Task<string> TestSwitchMatrixConnectionAsync();

    /// <summary>
    /// Queries the Prober device identity (e.g., *IDN? command).
    /// </summary>
    /// <returns>The device identity string or error message.</returns>
    Task<string> QueryProberIdentityAsync();

    /// <summary>
    /// Queries the Switch Matrix device identity (e.g., *IDN? command).
    /// </summary>
    /// <returns>The device identity string or error message.</returns>
    Task<string> QuerySwitchMatrixIdentityAsync();
}
