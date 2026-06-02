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
            _session = CreateSession();
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
                var rm = new ResourceManager();
                _session = rm.Open(_resourceString) as MessageBasedSession;
                _isConnected = true;
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
                _session?.Dispose();
                _session = null;
                _isConnected = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProberService] Error during disconnect: {ex.Message}");
            }
        }


        /// <summary>
        /// Send a command that does not expect a response.
        /// </summary>
        public async Task<string> SendReadCommandAsync(string command, int readBufferChars = 50, int postWriteDelayMs = 0)
        {
            if (!IsConnected || _session == null)
                throw new InvalidOperationException("Not connected to an instrument.");
            try
            {
                _session.RawIO.Write(command + "\n");
                Thread.Sleep(postWriteDelayMs);
                return _session.RawIO.ReadString(readBufferChars);
            }
            catch (Exception ex)
            {
                return $"Error sending command: {ex.Message}";
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
        /// Reads the connection/card descriptions from the switch matrix and returns them concatenated.
        /// Mirrors the VB <c>read_connection</c> behavior.
        /// </summary>
        public async Task<string> ReadConnectionAsync()
        {
            try
            {
                string temp1 = await SendReadCommandAsync(":CLOS:CARD? 1");
                string temp2 = await SendReadCommandAsync(":CLOS:CARD? 2");
                return temp1 + temp2;
            }
            catch (Exception ex)
            {
                return $"Error reading connection: {ex.Message}";
            }
        }

        /// <summary>
        /// Creates a connection on the switch matrix similar to the VB <c>create_connection</c>.
        /// If <paramref name="overrideCheck"/> is true the method will perform the connection; otherwise it will do nothing.
        /// Returns the channel string that was used (for logging/verification).
        /// </summary>
        public async Task<string> CreateConnectionAsync(object x, object y, bool overrideCheck = false)
        {
            if (!overrideCheck)
                return string.Empty;

            try
            {
                // We cannot tell what data type x and y are here, so we will convert them to strings and handle nulls gracefully.
                string xs = x?.ToString() ?? string.Empty;
                string ys = y?.ToString() ?? string.Empty;

                string channelstring;
                if (xs.Length > 4)
                    channelstring = "@" + xs + ", " + ys;
                else
                    channelstring = "@1" + xs + ", 1" + ys;

                Thread.Sleep(10);

                await SendReadCommandAsync("*RST");
                await SendReadCommandAsync(":ROUT:CONN:RULE ALL,FREE");
                await SendReadCommandAsync(":ROUT:CONN:SEQ ALL,BBM");
                await SendReadCommandAsync(":ROUT:CLOSE (" + channelstring + ")");

                Thread.Sleep(5);
                await SendReadCommandAsync("*OPC?", readBufferChars: 10);
                Thread.Sleep(5);
                return channelstring;
            }
            catch (Exception ex)
            {
                return $"Error creating connection: {ex.Message}";
                throw;
            }
        }
    }
}
