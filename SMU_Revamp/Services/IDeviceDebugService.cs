using System.Threading.Tasks;

namespace SMU_Revamp.Services
{
    /// <summary>
    /// Basic debug helper surface for instruments used by the debug UI.
    /// Provides simple connection tests and identity/query helpers.
    /// </summary>
    public interface IDeviceDebugService
    {
        Task<string> TestProberConnectionAsync();
        Task<string> QueryProberIdentityAsync();

        Task<string> TestSwitchMatrixConnectionAsync();
        Task<string> QuerySwitchMatrixIdentityAsync();
    }
}
