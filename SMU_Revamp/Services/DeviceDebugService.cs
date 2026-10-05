using System;
using System.Threading.Tasks;
using SMU_Revamp.Interfaces;

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
            bool wasConnected = _prober.IsConnected;
            try
            {
                await _prober.ConnectAsync();
                if (!wasConnected)
                {
                    await _prober.DisconnectAsync();
                }
                return $"Prober connected (resource={_prober.ResourceString})";
            }
            catch (Exception ex)
            {
                return $"Prober connection failed: {ex.Message}";
            }
        }



        public async Task<string> TestSwitchMatrixConnectionAsync()
        {
            bool wasConnected = _switch.IsConnected;
            try
            {
                await _switch.ConnectAsync();
                var info = await _switch.ReadConnectionAsync();
                if (!wasConnected)
                {
                    await _switch.DisconnectAsync();
                }
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
            bool wasConnected = _switch.IsConnected;
            try
            {
                await _switch.ConnectAsync();
                var channel = await _switch.CreateConnectionAsync(x, y, overrideCheck: true);
                if (!wasConnected)
                {
                    await _switch.DisconnectAsync();
                }
                return $"Connection successfully created! Channel: {channel}";
            }
            catch (Exception ex)
            {
                return $"Failed to create connection: {ex.Message}";
            }
        }

        public async Task<string> RemoveSwitchMatrixConnectionAsync(string x, string y)
        {
            bool wasConnected = _switch.IsConnected;
            try
            {
                await _switch.ConnectAsync();
                var channel = await _switch.RemoveConnectionAsync(x, y);
                if (!wasConnected)
                {
                    await _switch.DisconnectAsync();
                }
                return $"Connection successfully removed! Channel: {channel}";
            }
            catch (Exception ex)
            {
                return $"Failed to remove connection: {ex.Message}";
            }
        }

        public async Task<string> ClearAllSwitchMatrixConnectionsAsync()
        {
            bool wasConnected = _switch.IsConnected;
            try
            {
                await _switch.ConnectAsync();
                await _switch.ClearAllConnectionsAsync();
                if (!wasConnected)
                {
                    await _switch.DisconnectAsync();
                }
                return "Successfully cleared all switch matrix connections.";
            }
            catch (Exception ex)
            {
                return $"Failed to clear connections: {ex.Message}";
            }
        }

        public async Task<string> ReadSwitchMatrixConnectionAsync()
        {
            bool wasConnected = _switch.IsConnected;
            try
            {
                await _switch.ConnectAsync();
                var connectionInfo = await _switch.ReadConnectionAsync();
                if (!wasConnected)
                {
                    await _switch.DisconnectAsync();
                }
                return $"Switch matrix connections: {connectionInfo}";
            }
            catch (Exception ex)
            {
                return $"Failed to read switch matrix connections: {ex.Message}";
            }
        }

        public async Task<string> TestSMUConnectionAsync()
        {
            var smu = E5263_SMU.Instance;
            bool wasConnected = smu.IsConnected;
            try
            {
                var config = ConfigurationService.Instance.GetConfig();
                smu.ResourceString = config.SMUResource;
                smu.SetTimeout(config.SMUTimeoutMs);
                await smu.ConnectAsync();
                var identity = await smu.QueryAsync("*IDN?");
                if (!wasConnected)
                {
                    await smu.DisconnectAsync();
                }
                return $"SMU connected (resource={smu.ResourceString}). Identity: {identity.Trim()}";
            }
            catch (Exception ex)
            {
                return $"SMU connection failed: {ex.Message}";
            }
        }

        public async Task<string> QuerySMUIdentityAsync()
        {
            var smu = E5263_SMU.Instance;
            bool wasConnected = smu.IsConnected;
            try
            {
                var config = ConfigurationService.Instance.GetConfig();
                smu.ResourceString = config.SMUResource;
                smu.SetTimeout(config.SMUTimeoutMs);
                await smu.ConnectAsync();
                var identity = await smu.QueryAsync("*IDN?");
                if (!wasConnected)
                {
                    await smu.DisconnectAsync();
                }
                return $"SMU Identity: {identity.Trim()}";
            }
            catch (Exception ex)
            {
                return $"Error querying SMU identity: {ex.Message}";
            }
        }

        public async Task<string> ForceSMUDCVoltageAsync(string channel, double voltage, double compliance, double seconds, string returnChannel = "")
        {
            var smu = E5263_SMU.Instance;
            bool wasConnected = smu.IsConnected;
            bool hasReturn = !string.IsNullOrWhiteSpace(returnChannel) && returnChannel.Trim() != channel.Trim();
            string actualReturn = hasReturn ? returnChannel.Trim() : string.Empty;

            try
            {
                var config = ConfigurationService.Instance.GetConfig();
                smu.ResourceString = config.SMUResource;
                smu.SetTimeout(config.SMUTimeoutMs);
                await smu.ConnectAsync();

                // Reset and base configuration
                await smu.SendCommandAsync("*RST");
                await smu.SendCommandAsync("FMT 1");
                await smu.SendCommandAsync("TSC 1");

                if (hasReturn)
                {
                    await smu.SendCommandAsync($"CN {channel},{actualReturn}");
                    // Hold return channel at 0 V with specified compliance
                    var dvReturnCmd = System.FormattableString.Invariant($"DV {actualReturn},0,0,{compliance}");
                    await smu.SendCommandAsync(dvReturnCmd);
                    var dvReturnError = await smu.CheckErrorAsync();
                    if (dvReturnError != null)
                    {
                        await smu.SendCommandAsync($"CL {channel},{actualReturn}");
                        if (!wasConnected) await smu.DisconnectAsync();
                        return $"SMU return channel setup failed: {dvReturnError}";
                    }
                }
                else
                {
                    await smu.SendCommandAsync($"CN {channel}");
                }

                // Force DC Voltage: DV <ch>,0,<voltage>,<compliance> (0 is auto-range for voltage)
                var dvCommand = System.FormattableString.Invariant($"DV {channel},0,{voltage},{compliance}");
                await smu.SendCommandAsync(dvCommand);

                // Error detection after configuration
                var error = await smu.CheckErrorAsync();
                if (error != null)
                {
                    await smu.SendCommandAsync(hasReturn ? $"CL {channel},{actualReturn}" : $"CL {channel}");
                    if (!wasConnected) await smu.DisconnectAsync();
                    return $"SMU configuration failed: {error}";
                }

                // Spot-measure current while voltage is forced
                string measChannel = hasReturn ? actualReturn : channel;
                await smu.SendCommandAsync($"MM 1,{measChannel}");
                await smu.SendCommandAsync($"CMM {measChannel},1");
                await smu.SendCommandAsync("TSR");
                await smu.SendCommandAsync("XE");
                await smu.SendCommandAsync("TSQ");

                string measuredCurrentText = "N/A";
                double measuredCurrent = 0;
                bool measurementParsed = false;
                try
                {
                    string rawData = await smu.ReadResponseAsync(100);
                    foreach (var token in rawData.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var trimmed = token.Trim();
                        int idx = trimmed.IndexOf('I');
                        if (idx >= 0 && idx < trimmed.Length - 1)
                        {
                            string numStr = trimmed.Substring(idx + 1);
                            if (double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double val))
                            {
                                measuredCurrent = val;
                                measurementParsed = true;
                                measuredCurrentText = System.FormattableString.Invariant($"{val:E4} A ({val * 1000:F3} mA)");
                                break;
                            }
                        }
                    }
                }
                catch (Exception mex)
                {
                    measuredCurrentText = $"Read error: {mex.Message}";
                }

                // Hold voltage for specified duration
                int delayMs = (int)(seconds * 1000);
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }

                // Disable channels and zero voltage
                try { await smu.SendCommandAsync("DZ"); } catch { }
                await smu.SendCommandAsync(hasReturn ? $"CL {channel},{actualReturn}" : $"CL {channel}");
                if (!wasConnected)
                {
                    await smu.DisconnectAsync();
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[SMU Output Active -> Safely Closed]");
                sb.AppendLine($"Forced {voltage:F3} V on Ch {channel} (Comp: {compliance:E2} A) for {seconds:F1}s.");
                if (hasReturn)
                {
                    sb.AppendLine($"Return Ch {actualReturn}: held at 0 V with Compliance {compliance:E2} A.");
                }
                if (measurementParsed)
                {
                    sb.AppendLine($"Live Measured Current: {measuredCurrentText}");
                    if (Math.Abs(measuredCurrent) >= compliance * 0.95)
                    {
                        sb.AppendLine($">>> COMPLIANCE ACTIVE: Strom hat das Limit ({compliance:E2} A) erreicht! <<<");
                    }
                    else
                    {
                        sb.AppendLine($"Compliance Status: OK (Strom liegt unterhalb des Limits).");
                    }
                }
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                try { await E5263_SMU.Instance.SendCommandAsync("DZ"); } catch { }
                try { await E5263_SMU.Instance.SendCommandAsync(hasReturn ? $"CL {channel},{actualReturn}" : $"CL {channel}"); } catch { }
                if (!wasConnected)
                {
                    try { await E5263_SMU.Instance.DisconnectAsync(); } catch { }
                }
                return $"Failed to force SMU voltage: {ex.Message}";
            }
        }
    }
}
