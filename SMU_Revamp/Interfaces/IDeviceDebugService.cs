using System.Threading.Tasks;

namespace SMU_Revamp.Interfaces
{
    /// <summary>
    /// Basic debug helper surface for instruments used by the debug UI.
    /// Provides simple connection tests and identity/query helpers.
    /// </summary>
    public interface IDeviceDebugService
    {
        Task<string> TestProberConnectionAsync();

        Task<string> TestSwitchMatrixConnectionAsync();
        Task<string> QuerySwitchMatrixIdentityAsync();
        Task<string> CreateSwitchMatrixConnectionAsync(string x, string y);
        Task<string> RemoveSwitchMatrixConnectionAsync(string x, string y);
        Task<string> ClearAllSwitchMatrixConnectionsAsync();
        Task<string> ReadSwitchMatrixConnectionAsync();

        Task<string> TestSMUConnectionAsync();
        Task<string> QuerySMUIdentityAsync();
        Task<string> ForceSMUDCVoltageAsync(string channel, double voltage, double compliance, double seconds);
    }
}
