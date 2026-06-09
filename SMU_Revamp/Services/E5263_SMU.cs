using System;
using System.Threading;
using System.Threading.Tasks;
using NationalInstruments.Visa;

namespace SMU_Revamp.Services
{
    /// <summary>
    /// Service implementing basic VISA connectivity for the E5263 SMU instrument.
    /// Acts as a visa connection point using persistent session management.
    /// Singleton instance: access via E5263_SMU.Instance.
    /// </summary>
    public sealed class E5263_SMU
    {
        private static readonly Lazy<E5263_SMU> _instance = new(() => new E5263_SMU());

        /// <summary>
        /// Gets the singleton instance.
        /// </summary>
        public static E5263_SMU Instance => _instance.Value;

        private MessageBasedSession? _session;
        private bool _isConnected = false;
        private int _timeoutMilliseconds = 300000; // default 5 minutes for sweep
        private string _resourceString = DefaultResource;

        /// <summary>
        /// Default GPIB resource string for E5263 SMU.
        /// </summary>
        public const string DefaultResource = "GPIB0::17::INSTR";

        public bool IsConnected => _isConnected;

        public string ResourceString
        {
            get => _resourceString;
            set => _resourceString = value ?? DefaultResource;
        }

        private E5263_SMU()
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
        /// Connects to the E5263 SMU, opening a persistent GPIB session.
        /// </summary>
        public async Task ConnectAsync()
        {
            try
            {
                if (_isConnected && _session != null)
                    return;
                _session = CreateSession();
                _isConnected = _session != null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[E5263_SMU] Error during connect: {ex.Message}");
                throw new InvalidOperationException("Failed to connect to E5263 SMU. Check resource string and connection.", ex);
            }
        }

        /// <summary>
        /// Disconnects from the E5263 SMU, closing the persistent GPIB session.
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
                System.Diagnostics.Debug.WriteLine($"[E5263_SMU] Error during disconnect: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends a command to the SMU without expecting a response.
        /// </summary>
        public async Task SendCommandAsync(string command)
        {
            if (!IsConnected || _session == null)
                throw new InvalidOperationException("Not connected to E5263 SMU.");
            try
            {
                _session.RawIO.Write(command + "\n");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[E5263_SMU] Error sending command: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Reads a response from the SMU.
        /// </summary>
        public async Task<string> ReadResponseAsync(int readBufferChars = 1024)
        {
            if (!IsConnected || _session == null)
                throw new InvalidOperationException("Not connected to E5263 SMU.");
            try
            {
                return _session.RawIO.ReadString(readBufferChars);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[E5263_SMU] Error reading response: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Sends a command and reads a response.
        /// </summary>
        public async Task<string> QueryAsync(string command, int readBufferChars = 1024, int postWriteDelayMs = 0)
        {
            await SendCommandAsync(command);
            if (postWriteDelayMs > 0)
            {
                await Task.Delay(postWriteDelayMs);
            }
            return await ReadResponseAsync(readBufferChars);
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
    }
}
