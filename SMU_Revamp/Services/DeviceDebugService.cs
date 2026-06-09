using System;
using System.Threading.Tasks;

namespace SMU_Revamp.Services
{
    /// <summary>
    /// Minimal device debug service used by the DebugWindow.
    /// Provides non-invasive connection tests and basic info queries.
    /// </summary>
    public sealed class DeviceDebugService : IDeviceDebugService
    {
        private static readonly Lazy<DeviceDebugService> _instance = new(() => new DeviceDebugService());

        /// <summary>
        /// Expose as the interface for easier assignment in views.
        /// </summary>
        public static IDeviceDebugService Instance => _instance.Value;

        private readonly ProberService _prober = ProberService.Instance;
        private readonly SwitchMatrixService _switch = SwitchMatrixService.Instance;

        private DeviceDebugService() { }

        public async Task<string> TestProberConnectionAsync()
        {
            try
            {
                await _prober.ConnectAsync();
                await _prober.DisconnectAsync();
                return $"Prober connected (resource={_prober.ResourceString})";
            }
            catch (Exception ex)
            {
                return $"Prober connection failed: {ex.Message}";
            }
        }

        public Task<string> QueryProberIdentityAsync()
        {
            try
            {
                // Prober does not expose a raw query in the public interface.
                // Provide basic information that is safe to retrieve.
                return Task.FromResult($"Prober resource: {_prober.ResourceString}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Error querying prober identity: {ex.Message}");
            }
        }

        public async Task<string> TestSwitchMatrixConnectionAsync()
        {
            try
            {
                await _switch.ConnectAsync();
                var info = await _switch.ReadConnectionAsync();
                await _switch.DisconnectAsync();
                return $"Switch matrix connected. Info: {info}";
            }
            catch (Exception ex)
            {
                return $"Switch matrix connection failed: {ex.Message}";
            }
        }

        public async Task<string> QuerySwitchMatrixIdentityAsync()
        {
            try
            {
                var info = await _switch.ReadConnectionAsync();
                return $"Switch matrix info: {info}";
            }
            catch (Exception ex)
            {
                return $"Error querying switch matrix identity: {ex.Message}";
            }
        }

        public async Task<string> CreateSwitchMatrixConnectionAsync(string x, string y)
        {
            try
            {
                await _switch.ConnectAsync();
                var channel = await _switch.CreateConnectionAsync(x, y, overrideCheck: true);
                await _switch.DisconnectAsync();
                return $"Connection successfully created! Channel: {channel}";
            }
            catch (Exception ex)
            {
                return $"Failed to create connection: {ex.Message}";
            }
        }

        public async Task<string> ReadSwitchMatrixConnectionAsync()
        {
            try
            {
                await _switch.ConnectAsync();
                var connectionInfo = await _switch.ReadConnectionAsync();
                await _switch.DisconnectAsync();
                return $"Switch matrix connections: {connectionInfo}";
            }
            catch (Exception ex)
            {
                return $"Failed to read switch matrix connections: {ex.Message}";
            }
        }
    }
}
