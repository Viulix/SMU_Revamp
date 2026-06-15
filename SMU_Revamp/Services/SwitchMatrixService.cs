using System;
using System.Threading;
using System.Threading.Tasks;
using NationalInstruments.Visa;
namespace SMU_Revamp.Services
{
    /// <summary>
    /// Service implementing basic VISA connectivity for a switch matrix instrument.
    /// Translates functionality from the provided Visual Basic switchmatrix module.
    /// Supports persistent session management and configurable resource addressing.
    /// Singleton instance: access via SwitchMatrixService.Instance.
    /// </summary>
    public sealed class SwitchMatrixService : ISwitchMatrixService
    {
        private static readonly Lazy<SwitchMatrixService> _instance = new(() => new SwitchMatrixService());

        /// <summary>
        /// Gets the singleton instance.
        /// </summary>
        public static SwitchMatrixService Instance => _instance.Value;

        private MessageBasedSession? _session;
        private bool _isConnected = false;
        private int _timeoutMilliseconds = 5000;
        private string _resourceString = DefaultResource;

        /// <summary>
        /// Default resource string used by the VB example.
        /// </summary>
        public const string DefaultResource = "GPIB0::23::INSTR";

        public bool IsConnected => _isConnected;

        public string ResourceString
        {
            get => _resourceString;
            set => _resourceString = value ?? "GPIB0::23::INSTR";
        }


        private SwitchMatrixService()
        {
        }

        private MessageBasedSession CreateSession()
        {
            using var rm = new ResourceManager();
            if (rm.Open(_resourceString) is not MessageBasedSession session)
            {
                throw new InvalidOperationException($"Failed to create GPIB session for resource {_resourceString}");
            }

            session.TimeoutMilliseconds = _timeoutMilliseconds;
            return session;
        }

        /// <summary>
        /// Connects to the given VISA resource string.
        /// </summary>
        public async Task ConnectAsync()
        {
            try
            {
                if (_isConnected && _session != null)
                    return;
                await Task.Run(() => 
                {
                    _session = CreateSession();
                    _isConnected = _session != null;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProberService] Error during connect: {ex.Message}");
                throw new InvalidOperationException("Failed to connect to prober. Check resource string and connection.", ex);
            }
        }

        /// <summary>
        /// Disconnects from the switch matrix, closing the persistent GPIB session.
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                await Task.Run(() => 
                {
                    _session?.Dispose();
                    _session = null;
                    _isConnected = false;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProberService] Error during disconnect: {ex.Message}");
            }
        }


        /// <summary>
        /// Sends a command to the switch matrix that does not expect a response.
        /// </summary>
        public async Task SendWriteCommandAsync(string command)
        {
            if (!IsConnected || _session == null)
                throw new InvalidOperationException("Not connected to switch matrix.");
            try
            {
                await Task.Run(() => _session.RawIO.Write(command + "\n"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SwitchMatrixService] Error sending command: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Sends a command to the switch matrix and reads a response.
        /// </summary>
        public async Task<string> SendReadCommandAsync(string command, int readBufferChars = 50, int postWriteDelayMs = 0)
        {
            if (!IsConnected || _session == null)
                throw new InvalidOperationException("Not connected to switch matrix.");
            try
            {
                await Task.Run(() => _session.RawIO.Write(command + "\n"));
                if (postWriteDelayMs > 0)
                {
                    await Task.Delay(postWriteDelayMs);
                }
                return await Task.Run(() => _session.RawIO.ReadString(readBufferChars));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SwitchMatrixService] Error reading response: {ex.Message}");
                throw;
            }
        }

        public void SetTimeout(int timeoutMilliseconds)
        {
            _timeoutMilliseconds = timeoutMilliseconds;
            try
            {
                _session?.TimeoutMilliseconds = timeoutMilliseconds;
            }
            catch { }
        }

        public int GetTimeout() => _timeoutMilliseconds;

        // --- Methods translated from the VB module ---

        /// <summary>
        /// Reads human-readable connection/card information from the matrix by querying closed channels.
        /// Queries cards 1 and formats the E5250A 5-digit channel list response.
        /// </summary>
        public async Task<string> ReadConnectionAsync()
        {
            try
            {
                var activeConnections = new System.Collections.Generic.List<string>();

                int slot = 1;
                try
                {
                    string response = await SendReadCommandAsync($":ROUT:CLOS:CARD? {slot}");
                    response = response.Trim();

                    if (!string.IsNullOrEmpty(response) && response != "@")
                    {
                        if (response.StartsWith("@"))
                        {
                            response = response.Substring(1);
                        }

                        var channels = response.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var channel in channels)
                        {
                            if (channel.Length == 5)
                            {
                                int card = int.Parse(channel.Substring(0, 1));
                                int input = int.Parse(channel.Substring(1, 2));
                                int output = int.Parse(channel.Substring(3, 2));
                                activeConnections.Add($"Slot {card}: Input {input:D2} -> Output {output:D2}");
                            }
                            else
                            {
                                activeConnections.Add(channel);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SwitchMatrixService] Failed reading card {slot}: {ex.Message}");
                }

                if (activeConnections.Count == 0)
                {
                    return "No active connections.";
                }

                return string.Join("; ", activeConnections);
            }
            catch (Exception ex)
            {
                return $"Error reading connection: {ex.Message}";
            }
        }

        private string FormatChannelString(object x, object y)
        {
            string xs = x?.ToString()?.Trim() ?? string.Empty;
            string ys = y?.ToString()?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(xs) || string.IsNullOrEmpty(ys))
            {
                throw new ArgumentException("Input (x) and output (y) ports cannot be empty.");
            }

            string channelstring;
            if (xs.StartsWith("@") || xs.Length >= 5)
            {
                channelstring = xs.StartsWith("@") ? xs : "@" + xs;
                if (!string.IsNullOrEmpty(ys) && !xs.StartsWith("@"))
                {
                    channelstring = "@" + xs + "," + ys;
                }
            }
            else
            {
                var inputs = xs.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var outputs = ys.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (inputs.Length == 1 && outputs.Length == 1)
                {
                    if (int.TryParse(inputs[0], out int inPort) && int.TryParse(outputs[0], out int outPort))
                    {
                        channelstring = $"@1{inPort:D2}{outPort:D2}";
                    }
                    else
                    {
                        channelstring = $"@1{inputs[0].PadLeft(2, '0')}{outputs[0].PadLeft(2, '0')}";
                    }
                }
                else if (inputs.Length == outputs.Length)
                {
                    var channels = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < inputs.Length; i++)
                    {
                        if (int.TryParse(inputs[i], out int inPort) && int.TryParse(outputs[i], out int outPort))
                        {
                            channels.Add($"1{inPort:D2}{outPort:D2}");
                        }
                        else
                        {
                            channels.Add($"1{inputs[i].PadLeft(2, '0')}{outputs[i].PadLeft(2, '0')}");
                        }
                    }
                    channelstring = "@" + string.Join(",", channels);
                }
                else
                {
                    channelstring = $"@1{xs.PadLeft(2, '0')}{ys.PadLeft(2, '0')}";
                }
            }
            return channelstring;
        }

        /// <summary>
        /// Creates a connection on the switch matrix using the E5250A 5-digit crosspoint format:
        /// Card/Slot (1 digit, e.g. 1) + Input Port (2 digits, e.g. 01-10) + Output Port (2 digits, e.g. 01-12).
        /// Returns the channel string that was used.
        /// </summary>
        public async Task<string> CreateConnectionAsync(object x, object y, bool overrideCheck = false)
        {
            if (!overrideCheck)
                return string.Empty;

            try
            {
                string channelstring = FormatChannelString(x, y);

                await Task.Delay(10);

                //await SendWriteCommandAsync("*RST");
                await SendWriteCommandAsync(":ROUT:CONN:RULE ALL,FREE");
                await SendWriteCommandAsync(":ROUT:CONN:SEQ ALL,BBM");
                await SendWriteCommandAsync(":ROUT:CLOSE (" + channelstring + ")");

                await Task.Delay(5);
                await SendReadCommandAsync("*OPC?", readBufferChars: 10);
                await Task.Delay(5);
                return channelstring;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SwitchMatrixService] Error creating connection: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Removes the requested connection on the switch matrix.
        /// </summary>
        public async Task<string> RemoveConnectionAsync(object x, object y)
        {
            try
            {
                string channelstring = FormatChannelString(x, y);

                await Task.Delay(10);
                await SendWriteCommandAsync(":ROUT:OPEN (" + channelstring + ")");

                await Task.Delay(5);
                await SendReadCommandAsync("*OPC?", readBufferChars: 10);
                await Task.Delay(5);
                return channelstring;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SwitchMatrixService] Error removing connection: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Clears all connections on the switch matrix.
        /// </summary>
        public async Task ClearAllConnectionsAsync()
        {
            try
            {
                await Task.Delay(10);
                await SendWriteCommandAsync(":ROUT:OPEN:ALL");

                await Task.Delay(5);
                await SendReadCommandAsync("*OPC?", readBufferChars: 10);
                await Task.Delay(5);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SwitchMatrixService] Error clearing connections: {ex.Message}");
                throw;
            }
        }
    }
}
